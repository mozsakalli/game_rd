using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

// MCP (stdio, JSON-RPC 2.0) <-> Unity editor RPC (TCP localhost:5158, satir-bazli JSON) koprusu.
// Arac listesi Unity tarafindaki rpc.describe self-schema'sindan URETILIR: Unity'de yeni [RpcCommand]
// = otomatik yeni MCP tool. PNG yolu donen sonuclara image content eklenir.
//
// Unity'ye ozel: domain reload (kod derleme, play'e giris) TCP baglantisini dusurur; Rpc() yeniden
// baglanmayi birkac saniye dener. Env: UNITY_MCP_PORT (default 5158).

static class Program
{
    static readonly object _rpcLock = new();
    static TcpClient? _tcp;
    static StreamReader? _rd;
    static StreamWriter? _wr;
    static int _nextId = 1;
    static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("UNITY_MCP_PORT"), out int p) ? p : 5158;

    // MCP tool adi: harf/rakam/_/- (nokta bazi istemcilerde yasak) -> "scene.addGo" => "scene_addGo".
    static readonly Dictionary<string, string> _toolToCmd = new();

    static int Main()
    {
        var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var writer = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };

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
                    ["serverInfo"] = new JsonObject { ["name"] = "unity-editor", ["version"] = "0.1" },
                    ["instructions"] = BuildInstructions(),
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

    // Sabit talimat YOK: tek sahibi Unity tarafindaki rpc.guide. Baglanabiliyorsak initialize aninda gomeriz.
    static string BuildInstructions()
    {
        const string hint = "Unity Editor canli oturumu (Digitoy MCP). Ise baslamadan ONCE rpc_guide cagir (is akisi, kavramlar, deger bicimleri); " +
            "komut semasi icin rpc_describe. Araclar editorden canli uretilir. Kod derleme / play'e giris domain reload yapar: baglanti kopar, kopru yeniden baglanir.";
        try
        {
            var guide = Rpc("rpc.guide", null);
            var sb = new StringBuilder(hint).Append("\n\n[rpc_guide]\n");
            foreach (var kv in guide.AsObject())
            {
                sb.Append(kv.Key).Append(":\n");
                if (kv.Value is JsonArray lines)
                    foreach (var l in lines)
                        sb.Append("- ").Append(l?.GetValue<string>()).Append('\n');
                else
                    sb.Append("- ").Append(kv.Value?.ToJsonString()).Append('\n');
            }
            return sb.ToString();
        }
        catch (Exception e)
        {
            Log("rpc.guide alinamadi (Unity kapali?): " + e.Message);
            return hint;
        }
    }

    // --- tools ---

    static JsonNode ToolsList()
    {
        var tools = new JsonArray();
        JsonNode describe;
        try { describe = Rpc("rpc.describe", null); }
        catch (Exception e)
        {
            Log("Unity'ye baglanilamadi: " + e.Message);
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
                        prop["type"] = new JsonArray("string", "number", "boolean", "object", "array", "null");
                        prop["description"] = "kanonik deger: skaler (sayi/metin/bool, Vec 'x y z', renk '#rrggbbaa') veya nested nesne/liste";
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
            ToolsList(); // describe tazele (Unity yeniden derlendi / yeni komut)
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
                    ["mimeType"] = MimeOf(png),
                });
            return new JsonObject { ["content"] = content };
        }
        catch (RpcFailure e)
        {
            content.Add(new JsonObject { ["type"] = "text", ["text"] = e.Message });
            return new JsonObject { ["content"] = content, ["isError"] = true };
        }
    }

    // Sonucta "path": "*.png|*.jpg" (kok, "screenshot" cocugu ya da "frames" listesi) varsa gorsel olarak ekle.
    static IEnumerable<string> FindPngs(JsonNode result)
    {
        if (result is not JsonObject o)
            yield break;
        if (IsPng(o["path"]) is string p)
            yield return p;
        if (o["screenshot"] is JsonObject s && IsPng(s["path"]) is string p2)
            yield return p2;
        if (o["frames"] is JsonArray frames)
            foreach (var f in frames)
                if (f is JsonObject fo && IsPng(fo["path"]) is string p3)
                    yield return p3;
    }

    static string? IsPng(JsonNode? n)
    {
        if (n is not JsonValue v || !v.TryGetValue<string>(out var s))
            return null;
        bool img = s.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
        return img && File.Exists(s) ? s : null;
    }

    static string MimeOf(string path) => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

    // --- Unity RPC (TCP, satir-bazli) ---

    static JsonNode Rpc(string cmd, JsonObject? args)
    {
        lock (_rpcLock)
        {
            // Domain reload (derleme/play) soketi dusurur ve Unity birkac saniye dinlemez: sabirla yeniden dene.
            const int maxAttempts = 20;
            Exception? last = null;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
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
                catch (IOException e) { last = e; Disconnect(); }
                catch (SocketException e) { last = e; Disconnect(); }
                if (attempt < maxAttempts - 1)
                    Thread.Sleep(500);
            }
            throw new RpcFailure($"Unity RPC'ye ulasilamiyor (localhost:{Port}) — Unity Editor acik mi, Digitoy MCP paketi yuklu mu? ({last?.Message})");
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

    static void Log(string s) => Console.Error.WriteLine("[unity-mcp-bridge] " + s);

    sealed class McpError(int code, string message) : Exception(message) { public int Code = code; }
    sealed class RpcFailure(string message) : Exception(message);
}
