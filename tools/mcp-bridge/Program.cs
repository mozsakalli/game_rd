using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// MCP (stdio, JSON-RPC 2.0) <-> DigitoyEditor RPC (TCP localhost:5157, satir-bazli JSON) koprusu.
// Araç listesi editorun rpc.describe self-schema'sindan URETILIR: editorde yeni [RpcCommand]
// = otomatik yeni MCP tool. PNG yolu donen sonuclara (render.screenshot, sim.run screenshot)
// image content eklenir -> AI ekran goruntusunu dogrudan gorur.
//
// Kullanim (.vscode/mcp.json): "command": "dotnet", "args": ["run","--project","tools/mcp-bridge"]
// Env: DIGITOY_RPC_PORT (default 5157).

static class Program
{
    static readonly object _rpcLock = new();
    static TcpClient? _tcp;
    static StreamReader? _rd;
    static StreamWriter? _wr;
    static int _nextId = 1;
    static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("DIGITOY_RPC_PORT"), out int p) ? p : 5157;

    // MCP tool adi: harf/rakam/_/- (nokta bazi istemcilerde yasak) -> "scene.addGo" => "scene_addGo".
    static readonly Dictionary<string, string> _toolToCmd = new();

    static int Main()
    {
        var stdin = Console.OpenStandardInput();
        var stdout = Console.OpenStandardOutput();
        var reader = new StreamReader(stdin, new UTF8Encoding(false));
        var writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true };

        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length == 0)
                continue;
            JsonNode? msg;
            try { msg = JsonNode.Parse(line); }
            catch (Exception e) { Log("parse hatasi: " + e.Message); continue; }
            if (msg == null)
                continue;
            var response = Handle(msg);
            if (response != null)
                writer.WriteLine(response.ToJsonString());
        }
        return 0;
    }

    static JsonNode? Handle(JsonNode msg)
    {
        string method = msg["method"]?.GetValue<string>() ?? "";
        var id = msg["id"];
        bool isNotification = id == null;
        try
        {
            JsonNode? result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = msg["params"]?["protocolVersion"]?.GetValue<string>() ?? "2024-11-05",
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = "digitoy-editor", ["version"] = "0.1" },
                    ["instructions"] = Instructions,
                },
                "ping" => new JsonObject(),
                "tools/list" => ToolsList(),
                "tools/call" => ToolsCall(msg["params"]),
                "notifications/initialized" or "notifications/cancelled" => null,
                _ => isNotification ? null : throw new McpError(-32601, "bilinmeyen metod: " + method),
            };
            if (isNotification)
                return null;
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id!.DeepClone(), ["result"] = result ?? new JsonObject() };
        }
        catch (McpError e)
        {
            if (isNotification) return null;
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id!.DeepClone(),
                ["error"] = new JsonObject { ["code"] = e.Code, ["message"] = e.Message },
            };
        }
        catch (Exception e)
        {
            Log(e.ToString());
            if (isNotification) return null;
            return new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id!.DeepClone(),
                ["error"] = new JsonObject { ["code"] = -32000, ["message"] = e.Message },
            };
        }
    }

    const string Instructions =
        "DigitoyEditor canli oturumu. KOORDINAT: tum input_* ve render_screenshot (scale=1) MANTIKSAL nokta, " +
        "sol-ust orijin; screenshot pikseli == tiklama koordinati. Tipik akis: view_device(name) -> sim_run(frames, screenshot=true) " +
        "-> goruntuyu incele -> input_tap/input_drag -> render_screenshot. Yeni component tipi icin ONCE comp_schema. " +
        "Motor API'si icin engine_api (tip listesi / uyeler + protected override kancalari + tween extension'lari) — proje disina cikma. " +
        "SAHNE/PREFAB YAML'INI ASLA DOGRUDAN DUZENLEME: prop_set/transform_set/comp_add/scene_addGo kullan (undo'lu, canli patch; " +
        "kaydetme karari kullanicinin). prop_set 'value' skaler de alir (72, \"OutBack\", \"0 10 0\"). " +
        "Sahne degisikliklerinde <sahne>.intent.md'yi oku/guncelle (asset_read/asset_write). " +
        "ASSET BILGISI (yol/guid/tip, gorsel width/height, atlas) icin asset_list(filter,type)/asset_info(path) — " +
        "boyut bulmak icin kod yazip calistirma. " +
        "ANIMASYON: sahne intro/giris-cikis/idle gibi hedefi belli koreografi = MovieClip component'i (doc'ta, scrub/undo'lu); " +
        "Tween yalniz runtime'da hesaplanan tepkisel hareket. MovieClip tarifi: comp_add(go,\"MovieClip\") → anim_props(type) ile yol " +
        "(Transform: Position/Rotation/Scale/Active; LayoutBox: anchoredPos/width/fill.color...) → prop_set ic ice yol: " +
        "Tracks[0].Target=\"goId:Transform:0\" (CompRef \"goId:Tip:ordinal\"), Tracks[0].Path=\"Scale\", Tracks[0].Keys[0].Value=\"0 0 1 0\", " +
        "Tracks[0].Keys[0].Ease=\"OutBack\", Tracks[0].Keys[1].Frame=30, Tracks[0].Keys[1].Value=\"1 1 1 0\" (index==sayi → sona ekle; " +
        "value null → sil). Frame = Fps*saniye (Fps 30). Dogrulama: sim_run(frames) + scene_dumpLive(play).";

    // --- tools ---

    static JsonNode ToolsList()
    {
        var tools = new JsonArray();
        JsonNode describe;
        try { describe = Rpc("rpc.describe", null); }
        catch (Exception e)
        {
            Log("editor'e baglanilamadi: " + e.Message);
            return new JsonObject { ["tools"] = tools };
        }
        _toolToCmd.Clear();
        foreach (var kv in describe.AsObject())
        {
            string cmd = kv.Key;
            string tool = cmd.Replace('.', '_');
            _toolToCmd[tool] = cmd;
            var props = new JsonObject();
            var required = new JsonArray();
            if (kv.Value?["params"] is JsonArray ps)
                foreach (var pn in ps)
                {
                    if (pn == null) continue;
                    string name = pn["name"]!.GetValue<string>();
                    string type = pn["type"]?.GetValue<string>() ?? "string";
                    var prop = new JsonObject();
                    if (type == "node")
                    {
                        // Kanonik doc degeri: skaler (72, "OutBack", true, "0 10 0") ya da nested map/list.
                        prop["type"] = new JsonArray("string", "number", "boolean", "object", "array", "null");
                        prop["description"] = "kanonik doc degeri: skaler (sayi/metin/bool, Vec 'x y z', renk '#rrggbbaa') veya nested nesne/liste";
                    }
                    else
                        prop["type"] = type switch
                        {
                            "int" => "integer",
                            "float" => "number",
                            "bool" => "boolean",
                            _ => "string",
                        };
                    var def = pn["default"];
                    if (def != null)
                        prop["description"] = (prop["description"] is JsonNode d ? d.GetValue<string>() + "; " : "") + "default: " + def.ToJsonString().Trim('"');
                    else
                        required.Add(name);
                    props[name] = prop;
                }
            var schema = new JsonObject { ["type"] = "object", ["properties"] = props };
            if (required.Count > 0)
                schema["required"] = required;
            tools.Add(new JsonObject
            {
                ["name"] = tool,
                ["description"] = kv.Value?["help"]?.GetValue<string>() ?? cmd,
                ["inputSchema"] = schema,
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    static JsonNode ToolsCall(JsonNode? prm)
    {
        string tool = prm?["name"]?.GetValue<string>() ?? throw new McpError(-32602, "name eksik");
        if (!_toolToCmd.TryGetValue(tool, out var cmd))
        {
            ToolsList(); // describe tazele (editor yeniden basladi / yeni komut)
            if (!_toolToCmd.TryGetValue(tool, out cmd))
                throw new McpError(-32602, "bilinmeyen tool: " + tool);
        }
        var args = prm["arguments"] as JsonObject;
        var content = new JsonArray();
        try
        {
            var result = Rpc(cmd, args);
            content.Add(new JsonObject { ["type"] = "text", ["text"] = result.ToJsonString(new JsonSerializerOptions { WriteIndented = false }) });
            foreach (var png in FindPngs(result))
                content.Add(new JsonObject
                {
                    ["type"] = "image",
                    ["data"] = Convert.ToBase64String(File.ReadAllBytes(png)),
                    ["mimeType"] = "image/png",
                });
            return new JsonObject { ["content"] = content };
        }
        catch (RpcFailure e)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = e.Message });
            return new JsonObject { ["content"] = content, ["isError"] = true };
        }
    }

    // Sonucta "path": "*.png" (kok ya da "screenshot" cocugu) varsa dosyayi gorsel olarak ekle.
    static IEnumerable<string> FindPngs(JsonNode result)
    {
        if (result is not JsonObject o)
            yield break;
        if (o["path"]?.GetValue<string>() is string p && p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(p))
            yield return p;
        if (o["screenshot"] is JsonObject s
            && s["path"]?.GetValue<string>() is string p2 && p2.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && File.Exists(p2))
            yield return p2;
    }

    // --- editor RPC (TCP, satir-bazli) ---

    static JsonNode Rpc(string cmd, JsonObject? args)
    {
        lock (_rpcLock)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    Connect();
                    int id = _nextId++;
                    var req = new JsonObject { ["id"] = id, ["cmd"] = cmd, ["args"] = args?.DeepClone() ?? new JsonObject() };
                    _wr!.WriteLine(req.ToJsonString());
                    string? line = _rd!.ReadLine() ?? throw new IOException("baglanti kapandi");
                    var resp = JsonNode.Parse(line)!;
                    if (resp["ok"]?.GetValue<bool>() == true)
                        return resp["result"] ?? new JsonObject();
                    throw new RpcFailure(resp["error"]?.ToString() ?? "bilinmeyen hata");
                }
                catch (IOException) when (attempt == 0) { Disconnect(); }
                catch (SocketException) when (attempt == 0) { Disconnect(); }
            }
            throw new RpcFailure($"editor RPC'ye ulasilamiyor (localhost:{Port}) — DigitoyEditor acik mi?");
        }
    }

    static void Connect()
    {
        if (_tcp is { Connected: true })
            return;
        Disconnect();
        _tcp = new TcpClient();
        _tcp.Connect("127.0.0.1", Port);
        var s = _tcp.GetStream();
        _rd = new StreamReader(s, new UTF8Encoding(false));
        _wr = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true };
    }

    static void Disconnect()
    {
        try { _tcp?.Close(); } catch { }
        _tcp = null; _rd = null; _wr = null;
    }

    static void Log(string s) => Console.Error.WriteLine("[mcp-bridge] " + s);

    sealed class McpError(int code, string message) : Exception(message) { public int Code = code; }
    sealed class RpcFailure(string message) : Exception(message);
}
