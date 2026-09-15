using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// AI/otomasyon icin RPC komutu: static metodu isimle disari acar ([MenuItem]
// deseni). Ilk parametre RpcContext olabilir; kalanlar JSON args'tan ada gore
// baglanir (int/float/bool/string/DocNode; default degerli = opsiyonel).
// Oyun assembly'si de komut ekleyebilir — RebuildCatalog'da yeniden taranir.
[AttributeUsage(AttributeTargets.Method)]
public sealed class RpcCommandAttribute : Attribute
{
    public readonly string Name;
    public readonly string Help;

    public RpcCommandAttribute(string name, string help = "")
    {
        Name = name;
        Help = help;
    }
}

// Handler'larin editor durumuna tek erisim kapisi (global statiklere uzanmasin:
// test edilebilir + ileride headless host ayni handler'lari farkli ctx ile kosar).
public sealed class RpcContext
{
    public EditorScene Scene;
    public TypeCatalog Catalog;
    public AssetDatabase Assets;
}

// [RpcCommand] kayit defteri: tarama + imzadan parametre baglama + self-describe.
public static class RpcRegistry
{
    public sealed class Entry
    {
        public string Name;
        public string Help;
        public MethodInfo Method;
        public ParameterInfo[] Params;
    }

    static readonly Dictionary<string, Entry> _byName = new();

    public static void Rebuild(params Assembly[] assemblies)
    {
        _byName.Clear();
        foreach (var asm in assemblies)
        {
            if (asm == null)
                continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = m.GetCustomAttribute<RpcCommandAttribute>();
                    if (attr == null)
                        continue;
                    _byName[attr.Name] = new Entry
                    {
                        Name = attr.Name,
                        Help = attr.Help,
                        Method = m,
                        Params = m.GetParameters(),
                    };
                }
        }
    }

    public static Entry Find(string name) => _byName.GetValueOrDefault(name);

    // Tum komutlarin makine-okunur semasi (AI oturum basinda bunu ceker;
    // ayni sema ileride MCP tool listesine birebir servis edilir).
    public static DocNode Describe()
    {
        var root = DocNode.Map();
        var names = new List<string>(_byName.Keys);
        names.Sort(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var e = _byName[name];
            var cmd = DocNode.Map();
            if (e.Help.Length > 0)
                cmd.Add("help", DocNode.Scal(e.Help));
            var ps = DocNode.Seq();
            foreach (var p in e.Params)
            {
                if (p.ParameterType == typeof(RpcContext))
                    continue;
                var pd = DocNode.Map();
                pd.Add("name", DocNode.Scal(p.Name));
                pd.Add("type", DocNode.Scal(TypeLabel(p.ParameterType)));
                if (p.HasDefaultValue)
                    pd.Add("default", DocNode.Scal(
                        Convert.ToString(p.DefaultValue, CultureInfo.InvariantCulture) ?? "null"));
                ps.Items.Add(pd);
            }
            cmd.Add("params", ps);
            root.Add(name, cmd);
        }
        return root;
    }

    static string TypeLabel(Type t) => t == typeof(int) ? "int"
        : t == typeof(float) ? "float"
        : t == typeof(bool) ? "bool"
        : t == typeof(string) ? "string"
        : t == typeof(DocNode) ? "node"
        : t.Name;

    // Komut cagrisi: args JSON nesnesinden parametreleri baglar, handler'i kosar.
    // Donen DocNode = result; exception = hata yaniti (host sarar).
    public static DocNode Invoke(Entry e, RpcContext ctx, JsonElement? args)
    {
        var values = new object[e.Params.Length];
        for (int i = 0; i < e.Params.Length; i++)
        {
            var p = e.Params[i];
            if (p.ParameterType == typeof(RpcContext))
            {
                values[i] = ctx;
                continue;
            }
            if (args.HasValue && args.Value.ValueKind == JsonValueKind.Object
                && args.Value.TryGetProperty(p.Name, out var je))
            {
                values[i] = BindArg(je, p.ParameterType, p.Name);
                continue;
            }
            if (!p.HasDefaultValue)
                throw new RpcError($"eksik parametre: '{p.Name}'");
            values[i] = p.DefaultValue;
        }
        try
        {
            return (DocNode)e.Method.Invoke(null, values);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            throw tie.InnerException; // gercek hata mesaji AI'a gitsin
        }
    }

    static object BindArg(JsonElement je, Type t, string name)
    {
        try
        {
            if (t == typeof(DocNode))
                return RpcJson.ToDocNode(je);
            if (t == typeof(string))
                return je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText();
            if (t == typeof(int))
                return je.ValueKind == JsonValueKind.String
                    ? int.Parse(je.GetString(), CultureInfo.InvariantCulture) : je.GetInt32();
            if (t == typeof(float))
                return je.ValueKind == JsonValueKind.String
                    ? float.Parse(je.GetString(), CultureInfo.InvariantCulture) : je.GetSingle();
            if (t == typeof(bool))
                return je.GetBoolean();
        }
        catch (Exception ex) when (ex is not RpcError)
        {
            throw new RpcError($"parametre '{name}' {TypeLabel(t)} degil: {je.GetRawText()}");
        }
        throw new RpcError($"desteklenmeyen parametre tipi: {t.Name} ('{name}')");
    }
}

// Kullaniciya/AI'a donecek beklenen hatalar (stack trace'siz temiz mesaj).
public sealed class RpcError : Exception
{
    public RpcError(string message) : base(message) { }
}

// Editor icinde yasayan RPC sunucusu: TCP localhost, satir-bazli JSON.
// Istek:  {"id":1,"cmd":"scene.addGo","args":{"parent":0,"name":"orb"}}
// Yanit:  {"id":1,"ok":true,"result":{...}} | {"id":1,"ok":false,"error":"..."}
// Dinleyici/okuyucular ayri thread'de yalniz KUYRUGA yazar; komutlar ana
// thread'de frame basinda Pump ile kosar (motor tek-thread sozlesmesi —
// AssetWatcher'la ayni desen). Insan jesti ortasinda (HotControl) drain edilmez.
public static class RpcHost
{
    public const int DefaultPort = 5157;

    sealed class Client
    {
        public TcpClient Tcp;
        public NetworkStream Stream;
        public readonly object WriteLock = new();
    }

    readonly struct Request
    {
        public readonly Client From;
        public readonly string Line;
        public Request(Client from, string line) { From = from; Line = line; }
    }

    static TcpListener _listener;
    static readonly ConcurrentQueue<Request> _queue = new();
    static readonly List<Client> _clients = new(); // yalniz teshis; lock'lu

    // Deferred yanitlar: handler Defer(poll) cagirirsa yanit hemen yazilmaz;
    // poll her frame denenip DocNode dondurunce yazilir (uzun isler frame'i bloklamaz).
    sealed class Pending
    {
        public Client From;
        public bool HasId;
        public long Id;
        public Func<DocNode> Poll; // null = bekle; DocNode = yanit; RpcError firlat = hata
    }

    static readonly List<Pending> _pending = new();
    static Func<DocNode> _deferredPoll; // Invoke sirasinda handler'in biraktigi isaret (ana thread)

    // Pump edilen frame sayisi (sim.run gibi frame-bazli beklemeler icin saat).
    public static int FrameCount { get; private set; }

    // Handler icinden: yaniti erteler. poll ana thread'de her frame kosar.
    public static void Defer(Func<DocNode> poll) => _deferredPoll = poll;

    public static void Start(int port = 0)
    {
        if (_listener != null)
            return;
        if (port <= 0)
            port = Environment.GetEnvironmentVariable("DIGITOY_RPC_PORT") is { } s
                && int.TryParse(s, out int p) ? p : DefaultPort;
        try
        {
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start();
        }
        catch (SocketException ex)
        {
            EditorLog.Warning($"[rpc] port {port} acilamadi: {ex.Message}");
            _listener = null;
            return;
        }
        var accept = new System.Threading.Thread(AcceptLoop) { IsBackground = true, Name = "rpc-accept" };
        accept.Start();
        EditorLog.Info($"[rpc] dinleniyor: localhost:{port}");
    }

    public static void Stop()
    {
        _listener?.Stop();
        _listener = null;
        lock (_clients)
        {
            foreach (var c in _clients)
                try { c.Tcp.Close(); } catch { }
            _clients.Clear();
        }
    }

    static void AcceptLoop()
    {
        var listener = _listener;
        while (listener != null && _listener == listener)
        {
            TcpClient tcp;
            try { tcp = listener.AcceptTcpClient(); }
            catch { break; } // Stop() dinleyiciyi kapatti
            var client = new Client { Tcp = tcp, Stream = tcp.GetStream() };
            lock (_clients)
                _clients.Add(client);
            var reader = new System.Threading.Thread(() => ReadLoop(client)) { IsBackground = true, Name = "rpc-read" };
            reader.Start();
        }
    }

    static void ReadLoop(Client client)
    {
        try
        {
            using var sr = new StreamReader(client.Stream, Encoding.UTF8, false, 4096, leaveOpen: true);
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (line.Length == 0)
                    continue;
                _queue.Enqueue(new Request(client, line));
            }
        }
        catch { } // baglanti koptu
        finally
        {
            lock (_clients)
                _clients.Remove(client);
            try { client.Tcp.Close(); } catch { }
        }
    }

    // ANA THREAD, frame basinda (jest yokken) cagrilir: biriken istekleri kosar.
    public static void Pump(RpcContext ctx)
    {
        FrameCount++;
        // Bekleyen deferred yanitlar: poll dene, sonuclanani yaz.
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            string response = null;
            try
            {
                var result = p.Poll();
                if (result == null)
                    continue; // hala bekliyor
                response = Ok(p.HasId, p.Id, result);
            }
            catch (RpcError ex)
            {
                response = Error(p.HasId, p.Id, ex.Message);
            }
            catch (Exception ex)
            {
                response = Error(p.HasId, p.Id, ex.GetType().Name + ": " + ex.Message);
            }
            _pending.RemoveAt(i);
            Send(p.From, response);
        }
        while (_queue.TryDequeue(out var req))
        {
            string response = Execute(ctx, req.Line, req.From);
            if (response != null) // null = deferred, yanit sonra
                Send(req.From, response);
        }
    }

    static void Send(Client to, string response)
    {
        var bytes = Encoding.UTF8.GetBytes(response + "\n");
        try
        {
            lock (to.WriteLock)
                to.Stream.Write(bytes, 0, bytes.Length);
        }
        catch { } // istemci gitti; komut yine de kostu (idempotent istemci sorumlulugu)
    }

    static string Execute(RpcContext ctx, string line, Client from)
    {
        long id = -1;
        bool hasId = false;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                id = idEl.GetInt64();
                hasId = true;
            }
            if (!root.TryGetProperty("cmd", out var cmdEl) || cmdEl.ValueKind != JsonValueKind.String)
                return Error(hasId, id, "istek 'cmd' alani icermiyor");
            string cmd = cmdEl.GetString();
            var entry = RpcRegistry.Find(cmd);
            if (entry == null)
                return Error(hasId, id, $"bilinmeyen komut: '{cmd}' (rpc.describe ile listele)");
            JsonElement? args = root.TryGetProperty("args", out var argsEl) ? argsEl : null;
            _deferredPoll = null;
            var result = RpcRegistry.Invoke(entry, ctx, args);
            if (_deferredPoll != null)
            {
                _pending.Add(new Pending { From = from, HasId = hasId, Id = id, Poll = _deferredPoll });
                _deferredPoll = null;
                return null; // yanit poll sonuclaninca
            }
            return Ok(hasId, id, result);
        }
        catch (JsonException ex)
        {
            return Error(hasId, id, "gecersiz JSON: " + ex.Message);
        }
        catch (RpcError ex)
        {
            return Error(hasId, id, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(hasId, id, ex.GetType().Name + ": " + ex.Message);
        }
    }

    static string Ok(bool hasId, long id, DocNode result)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (hasId)
                w.WriteNumber("id", id);
            w.WriteBoolean("ok", true);
            if (result != null)
            {
                w.WritePropertyName("result");
                RpcJson.Write(w, result);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    static string Error(bool hasId, long id, string message)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            if (hasId)
                w.WriteNumber("id", id);
            w.WriteBoolean("ok", false);
            w.WriteString("error", message);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}

// DocNode <-> JSON koprusu: handler'lar motorun kendi agacini dondurur (tek
// serilestirme modeli), host JSON'a cevirir. Skaler yazimda dar tip tespiti:
// tam sayi/bool/ondalik ise typed, degilse string (kanonik doc formatlari korunur).
public static class RpcJson
{
    public static void Write(Utf8JsonWriter w, DocNode n)
    {
        if (n.IsMap)
        {
            w.WriteStartObject();
            foreach (var kv in n.Fields)
            {
                w.WritePropertyName(kv.Key);
                Write(w, kv.Value);
            }
            w.WriteEndObject();
        }
        else if (n.IsSeq)
        {
            w.WriteStartArray();
            foreach (var it in n.Items)
                Write(w, it);
            w.WriteEndArray();
        }
        else
        {
            string s = n.Scalar ?? "";
            if (s == "true") w.WriteBooleanValue(true);
            else if (s == "false") w.WriteBooleanValue(false);
            else if (long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l))
                w.WriteNumberValue(l);
            else if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                && !s.Contains(' '))
                w.WriteNumberValue(d);
            else
                w.WriteStringValue(s);
        }
    }

    public static DocNode ToDocNode(JsonElement je)
    {
        switch (je.ValueKind)
        {
            case JsonValueKind.Object:
                var map = DocNode.Map();
                foreach (var p in je.EnumerateObject())
                    map.Add(p.Name, ToDocNode(p.Value));
                return map;
            case JsonValueKind.Array:
                var seq = DocNode.Seq();
                foreach (var it in je.EnumerateArray())
                    seq.Items.Add(ToDocNode(it));
                return seq;
            case JsonValueKind.String:
                return DocNode.Scal(je.GetString());
            case JsonValueKind.True:
                return DocNode.Scal("true");
            case JsonValueKind.False:
                return DocNode.Scal("false");
            case JsonValueKind.Number:
                // Kanonik doc formati: sayi invariant string olarak tasinir.
                return DocNode.Scal(je.GetRawText());
            default:
                return DocNode.Scal("");
        }
    }
}
