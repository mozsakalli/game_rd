using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Digitoy.Mcp
{
    // Unity Editor icinde yasayan RPC sunucusu: TCP localhost, satir-bazli JSON.
    // Istek:  {"id":1,"cmd":"scene.addGo","args":{"parent":0,"name":"orb"}}
    // Yanit:  {"id":1,"ok":true,"result":{...}} | {"id":1,"ok":false,"error":"..."}
    // Okuyucu thread'ler yalniz KUYRUGA yazar; komutlar EditorApplication.update'te ana thread'de kosar.
    // Domain reload'da (derleme, play'e giris) listener kapanir ve yeniden acilir; kopru yeniden baglanir.
    [InitializeOnLoad]
    public static class RpcHost
    {
        public const int DefaultPort = 5158;

        sealed class Client
        {
            public TcpClient Tcp;
            public NetworkStream Stream;
            public readonly object WriteLock = new object();
        }

        readonly struct Request
        {
            public readonly Client From;
            public readonly string Line;
            public Request(Client from, string line) { From = from; Line = line; }
        }

        sealed class Pending
        {
            public Client From;
            public bool HasId;
            public long Id;
            public long LogMark;
            public Func<JNode> Poll; // null = bekle; JNode = yanit; RpcError = hata
        }

        static TcpListener _listener;
        static readonly ConcurrentQueue<Request> _queue = new ConcurrentQueue<Request>();
        static readonly List<Client> _clients = new List<Client>();
        static readonly List<Pending> _pending = new List<Pending>();
        static Func<JNode> _deferredPoll;

        // Pump edilen editor update sayisi (frame-bazli beklemeler icin saat).
        public static int Tick { get; private set; }
        public static int Port { get; private set; }

        static RpcHost()
        {
            EditorApplication.update += Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            Start();
        }

        // Handler icinden: yaniti erteler; poll ana thread'de her update kosar.
        public static void Defer(Func<JNode> poll) => _deferredPoll = poll;

        public static void Start(int port = 0)
        {
            if (_listener != null)
                return;
            if (port <= 0)
            {
                string s = Environment.GetEnvironmentVariable("UNITY_MCP_PORT");
                port = s != null && int.TryParse(s, out int p) ? p : DefaultPort;
            }
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start();
            }
            catch (SocketException ex)
            {
                Debug.LogWarning($"[mcp] port {port} acilamadi: {ex.Message}");
                _listener = null;
                return;
            }
            Port = port;
            var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "mcp-accept" };
            accept.Start();
        }

        public static void Stop()
        {
            var l = _listener;
            _listener = null;
            try { l?.Stop(); } catch { }
            lock (_clients)
            {
                foreach (var c in _clients)
                    try { c.Tcp.Close(); } catch { }
                _clients.Clear();
            }
            _pending.Clear();
        }

        static void AcceptLoop()
        {
            var listener = _listener;
            while (listener != null && ReferenceEquals(_listener, listener))
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch { break; }
                var client = new Client { Tcp = tcp, Stream = tcp.GetStream() };
                lock (_clients)
                    _clients.Add(client);
                var reader = new Thread(() => ReadLoop(client)) { IsBackground = true, Name = "mcp-read" };
                reader.Start();
            }
        }

        static void ReadLoop(Client client)
        {
            try
            {
                using (var sr = new StreamReader(client.Stream, Encoding.UTF8, false, 4096, true))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                        if (line.Length > 0)
                        {
                            _queue.Enqueue(new Request(client, line));
                            Wake();
                        }
                }
            }
            catch { }
            finally
            {
                lock (_clients)
                    _clients.Remove(client);
                try { client.Tcp.Close(); } catch { }
            }
        }

        // Unity arka plandayken (ozellikle play+paused) editor dongusu uyur; ana pencereye bos mesaj atip uyandiririz.
        static IntPtr _mainWindow;

#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
#endif

        static void Wake()
        {
#if UNITY_EDITOR_WIN
            if (_mainWindow != IntPtr.Zero) PostMessage(_mainWindow, 0 /*WM_NULL*/, IntPtr.Zero, IntPtr.Zero);
#endif
        }

        // ANA THREAD (EditorApplication.update): bekleyen poll'lar + yeni istekler.
        static void Pump()
        {
            Tick++;
            if (_mainWindow == IntPtr.Zero)
                try { _mainWindow = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle; } catch { }
            if (_pending.Count > 0) Wake(); // deferred is suruyorsa dongu uyumasin
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var p = _pending[i];
                string response;
                try
                {
                    _deferredPoll = null;
                    var result = p.Poll();
                    if (_deferredPoll != null)
                    {
                        // Poll yeni bir asama kurdu (ornek: play'e girince sim adimlama baslar).
                        p.Poll = _deferredPoll;
                        _deferredPoll = null;
                        continue;
                    }
                    if (result == null)
                        continue;
                    response = Ok(p.HasId, p.Id, AttachErrors(result, p.LogMark));
                }
                catch (RpcError ex) { response = Error(p.HasId, p.Id, ex.Message); }
                catch (Exception ex) { response = Error(p.HasId, p.Id, ex.GetType().Name + ": " + ex.Message); }
                _pending.RemoveAt(i);
                Send(p.From, response);
            }
            while (_queue.TryDequeue(out var req))
            {
                string response = Execute(req.Line, req.From);
                if (response != null)
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
            catch { }
        }

        static string Execute(string line, Client from)
        {
            long id = -1;
            bool hasId = false;
            try
            {
                var msg = JNode.Parse(line);
                if (!msg.IsMap)
                    throw new RpcError("istek bir JSON nesnesi olmali");
                var idNode = msg.Get("id");
                if (idNode != null && idNode.IsScalar && long.TryParse(idNode.Str, out long idv))
                {
                    id = idv;
                    hasId = true;
                }
                string cmd = msg.GetScalar("cmd", null);
                if (string.IsNullOrEmpty(cmd))
                    throw new RpcError("'cmd' eksik");
                var entry = RpcRegistry.Find(cmd) ?? throw new RpcError($"bilinmeyen komut: '{cmd}' (rpc.describe)");
                long mark = cmd.StartsWith("log.", StringComparison.Ordinal) || cmd.StartsWith("rpc.", StringComparison.Ordinal) ? long.MaxValue : LogBuffer.Mark();
                _deferredPoll = null;
                var result = RpcRegistry.Invoke(entry, msg.Get("args"));
                if (_deferredPoll != null)
                {
                    _pending.Add(new Pending { From = from, HasId = hasId, Id = id, Poll = _deferredPoll, LogMark = mark });
                    _deferredPoll = null;
                    return null;
                }
                return Ok(hasId, id, AttachErrors(result ?? JNode.Map(), mark));
            }
            catch (RpcError ex) { return Error(hasId, id, ex.Message); }
            catch (FormatException ex) { return Error(hasId, id, "JSON: " + ex.Message); }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return Error(hasId, id, ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Komut suresince Console'a dusen Error/Exception/Assert kayitlari sonuca eklenir (oyun kodunun hatalari gorunur olsun).
        static JNode AttachErrors(JNode result, long mark)
        {
            if (mark == long.MaxValue || result == null || !result.IsMap) return result;
            var errs = LogBuffer.ErrorsSince(mark, 20, out int total);
            if (total == 0) return result;
            result.Add("errors", errs);
            result.Add("errorsTotal", total);
            return result;
        }

        static string Ok(bool hasId, long id, JNode result)
        {
            var r = JNode.Map();
            if (hasId) r.Add("id", JNode.Num(id));
            r.Add("ok", true);
            r.Add("result", result);
            return r.ToString();
        }

        static string Error(bool hasId, long id, string message)
        {
            var r = JNode.Map();
            if (hasId) r.Add("id", JNode.Num(id));
            r.Add("ok", false);
            r.Add("error", message);
            return r.ToString();
        }
    }
}
