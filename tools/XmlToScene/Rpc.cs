using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace XmlToScene;

// Editorun satir bazli JSON RPC'sine (localhost:5157) ince istemci. Bir baglanti, N istek.
sealed class Rpc : IDisposable
{
    readonly TcpClient _tcp;
    readonly StreamReader _rd;
    readonly StreamWriter _wr;
    public bool Dry;
    public bool Verbose;

    public Rpc(int port, bool dry)
    {
        Dry = dry;
        if (dry) return;
        _tcp = new TcpClient("127.0.0.1", port);
        var s = _tcp.GetStream();
        _rd = new StreamReader(s, new UTF8Encoding(false));
        _wr = new StreamWriter(s, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    }

    public JsonNode Call(string cmd, JsonObject args)
    {
        var req = new JsonObject { ["cmd"] = cmd, ["args"] = args };
        string line = req.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        if (Verbose || Dry)
            Console.WriteLine((Dry ? "[dry] " : "> ") + (line.Length > 400 && !Verbose ? line[..400] + "..." : line));
        if (Dry) return new JsonObject();
        _wr.WriteLine(line);
        string resp = _rd.ReadLine() ?? throw new Exception("rpc: baglanti kapandi");
        var j = JsonNode.Parse(resp)!;
        if (j["ok"]?.GetValue<bool>() != true)
            throw new RpcException(cmd, j["error"]?.ToString() ?? resp);
        return j["result"] ?? new JsonObject();
    }

    public void Dispose() { _wr?.Dispose(); _rd?.Dispose(); _tcp?.Dispose(); }
}

sealed class RpcException : Exception
{
    public RpcException(string cmd, string err) : base($"{cmd}: {err}") { }
}
