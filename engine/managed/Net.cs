using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Motorun ince ag yuzeyi (docs/network.md). Altta platformun hazir yigini (engine/native/de_net.h);
// TLS/proxy/cookie OS'un. Tamamlanmalar NativeAsync uzerinden ana thread'de; kendi pump'i yok.
// Zaman asimi native'de (TimeoutMs); CancellationToken yalnizca kullanici/yasam dongusu iptali
// (ornegin Component.destroyCancellationToken). Editor ve release ayni native kodu kullanir.
static unsafe class NativeNet
{
    const string Lib = "digitoyengine_native";

    [DllImport(Lib, EntryPoint = "de_http_request")]
    public static extern int HttpRequest([MarshalAs(UnmanagedType.LPUTF8Str)] string method, [MarshalAs(UnmanagedType.LPUTF8Str)] string url,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string headers, byte* body, int bodyLen, int timeoutMs);
    [DllImport(Lib, EntryPoint = "de_http_cancel")]
    public static extern void HttpCancel(int job);

    [DllImport(Lib, EntryPoint = "de_ws_open")]
    public static extern int WsOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string url, [MarshalAs(UnmanagedType.LPUTF8Str)] string headers);
    [DllImport(Lib, EntryPoint = "de_ws_send")]
    public static extern int WsSend(int ws, byte* data, int len, int isText);
    [DllImport(Lib, EntryPoint = "de_ws_close")]
    public static extern void WsClose(int ws, int code, [MarshalAs(UnmanagedType.LPUTF8Str)] string reason);
    [DllImport(Lib, EntryPoint = "de_ws_free")]
    public static extern void WsFree(int ws);

    public const int WsOpenEvent = NativeAsync.EventBit | 1;
    public const int WsMessageEvent = NativeAsync.EventBit | 2;
    public const int WsCloseCode = 2;

    // "K: V\n" satirlari (de_net sozlesmesi). null = basliksiz.
    public static string JoinHeaders(List<KeyValuePair<string, string>> headers)
    {
        if (headers == null || headers.Count == 0) return null;
        var sb = new StringBuilder();
        for (int i = 0; i < headers.Count; i++)
            sb.Append(headers[i].Key).Append(": ").Append(headers[i].Value).Append('\n');
        return sb.ToString();
    }

    public static byte[] CopyBytes(IntPtr p, int offset, int count)
    {
        var b = new byte[count];
        if (count > 0) Marshal.Copy(p + offset, b, 0, count);
        return b;
    }

    public static string Utf8(IntPtr p, int offset, int count)
        => count <= 0 || p == IntPtr.Zero ? "" : Encoding.UTF8.GetString(CopyBytes(p, offset, count));

    public static int StartHttp(HttpRequest req)
    {
        var body = req.Body;
        if (body != null && body.Length > 0)
            fixed (byte* p = body) return HttpRequest(req.Method, req.Url, req.HeaderBlock, p, body.Length, req.TimeoutMs);
        return HttpRequest(req.Method, req.Url, req.HeaderBlock, null, 0, req.TimeoutMs);
    }

    public static bool SendBytes(int ws, byte[] data, bool isText)
    {
        fixed (byte* p = data) return WsSend(ws, p, data.Length, isText ? 1 : 0) == 0;
    }
}

public sealed class HttpRequest
{
    public string Method = "GET";
    public string Url;
    public byte[] Body;
    public int TimeoutMs = 30000;
    List<KeyValuePair<string, string>> _headers;
    public HttpRequest Header(string name, string value)
    {
        (_headers ??= new List<KeyValuePair<string, string>>()).Add(new KeyValuePair<string, string>(name, value));
        return this;
    }
    internal string HeaderBlock => NativeNet.JoinHeaders(_headers);
}

public sealed class HttpResponse
{
    public int Status;          // 0 = yanit yok (ag/zaman asimi hatasi; Error dolu)
    public byte[] Body;         // null degil (bos olabilir)
    public string Error;        // null = tasima basarili (HTTP hata kodu olsa bile)
    public string RawHeaders;   // "HTTP/1.1 200 OK\r\nK: V\r\n..." (platform bicimi)
    public bool Ok => Error == null && Status >= 200 && Status < 300;
    string _text;
    public string Text => _text ??= Body == null || Body.Length == 0 ? "" : Encoding.UTF8.GetString(Body);

    // Baslik degeri (ad buyuk/kucuk harf duyarsiz); yoksa null.
    public string Header(string name)
    {
        if (string.IsNullOrEmpty(RawHeaders) || string.IsNullOrEmpty(name)) return null;
        var lines = RawHeaders.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            if (string.Equals(line.Substring(0, colon).Trim(), name, StringComparison.OrdinalIgnoreCase))
                return line.Substring(colon + 1).Trim();
        }
        return null;
    }
}

public static class Http
{
    public static Task<HttpResponse> GetAsync(string url, CancellationToken ct = default)
        => SendAsync(new HttpRequest { Url = url }, ct);

    public static Task<HttpResponse> PostAsync(string url, byte[] body, string contentType = "application/octet-stream", CancellationToken ct = default)
        => SendAsync(new HttpRequest { Method = "POST", Url = url, Body = body }.Header("Content-Type", contentType), ct);

    public static Task<HttpResponse> PostAsync(string url, string text, string contentType = "application/json", CancellationToken ct = default)
        => PostAsync(url, Encoding.UTF8.GetBytes(text ?? ""), contentType, ct);

    public static async Task<HttpResponse> SendAsync(HttpRequest req, CancellationToken ct = default)
    {
        if (req == null || string.IsNullOrEmpty(req.Url))
            return new HttpResponse { Error = "url bos", Body = Array.Empty<byte>() };
        ct.ThrowIfCancellationRequested();
        int job = NativeNet.StartHttp(req);
        var c = await NativeAsync.Await(job, ct, () => NativeNet.HttpCancel(job));
        var res = new HttpResponse();
        if (!c.Ok)
        {
            res.Error = c.Len > 0 ? NativeNet.Utf8(c.Data, 0, (int)c.Len) : "network error";
            res.Body = Array.Empty<byte>();
        }
        else
        {
            res.Status = c.A;
            int headerLen = c.B, total = (int)c.Len;
            res.RawHeaders = NativeNet.Utf8(c.Data, 0, headerLen);
            res.Body = NativeNet.CopyBytes(c.Data, headerLen, total - headerLen);
        }
        NativeAsync.Free(c.Data);
        return res;
    }
}

public enum WebSocketState { Connecting, Open, Closed }

// class: struct'ta `this` spill + `??=` deseni AOT frontend'inde pointer/deger karisikligi uretiyor (mesaj zaten alloc).
public sealed class WsMessage
{
    public bool IsText;
    public byte[] Data;
    string _text;
    public string Text => _text ??= Data == null || Data.Length == 0 ? "" : Encoding.UTF8.GetString(Data);
}

// Tek baglanti. Alim iki bicimde: `await ReceiveAsync()` (ic kuyruk) ya da OnMessage (Pump'ta, ana thread).
// OnMessage atanmissa mesajlar kuyruga girmez. Kapanis/hata: State=Closed, bekleyen ReceiveAsync'ler iptal, OnClose.
public sealed class WebSocket : IDisposable
{
    sealed class Receiver
    {
        public TaskCompletionSource<WsMessage> Tcs = new();
        public CancellationTokenRegistration Reg;
    }
    int _job;
    WebSocketState _state = WebSocketState.Connecting;
    readonly List<WsMessage> _inbox = new();
    readonly List<Receiver> _receivers = new();
    TaskCompletionSource<bool> _connect;
    TaskCompletionSource<bool> _closeTask;
    public Action<WsMessage> OnMessage;
    public Action<int, string> OnClose; // kod, sebep (hata: kod 0, sebep = hata metni)
    public WebSocketState State => _state;
    public int CloseCode { get; private set; }
    public string CloseReason { get; private set; }
    public string Error { get; private set; }

    WebSocket() { }

    // Baglanti kurulana kadar bekler; basarisizlikta Exception (Error metni). ct: baglanmayi/baglantiyi iptal eder.
    public static async Task<WebSocket> ConnectAsync(string url, CancellationToken ct = default, List<KeyValuePair<string, string>> headers = null)
    {
        ct.ThrowIfCancellationRequested();
        var ws = new WebSocket();
        ws._connect = new TaskCompletionSource<bool>();
        ws._job = NativeNet.WsOpen(url, NativeNet.JoinHeaders(headers));
        NativeAsync.Listen(ws._job, ws.OnCompletion, ct, ws.OnCanceled);
        await ws._connect.Task; // TrySetCanceled/Exception ile firlatir
        return ws;
    }

    void OnCanceled()
    {
        // Listen dustu: artik olay gelmez. Native'i kapat ve serbest birak; bekleyenleri iptal et.
        NativeNet.WsFree(_job);
        Finish(0, "canceled", true);
    }

    void OnCompletion(Completion c)
    {
        if (c.Code == NativeNet.WsOpenEvent)
        {
            _state = WebSocketState.Open;
            _connect?.TrySetResult(true);
            return;
        }
        if (c.Code == NativeNet.WsMessageEvent)
        {
            var m = new WsMessage { IsText = c.A != 0, Data = NativeNet.CopyBytes(c.Data, 0, (int)c.Len) };
            NativeAsync.Free(c.Data);
            if (_receivers.Count > 0)
            {
                var r = _receivers[0];
                _receivers.RemoveAt(0);
                r.Reg.Dispose();
                r.Tcs.TrySetResult(m);
            }
            else if (OnMessage != null) OnMessage(m);
            else _inbox.Add(m);
            return;
        }
        // son tamamlanma: CLOSE ya da hata
        string text = c.Len > 0 ? NativeNet.Utf8(c.Data, 0, (int)c.Len) : null;
        NativeAsync.Free(c.Data);
        NativeNet.WsFree(_job);
        if (c.Code == NativeNet.WsCloseCode) Finish(c.A, text ?? "", false);
        else Finish(0, text ?? "network error", false);
    }

    void Finish(int code, string reason, bool canceled)
    {
        if (_state == WebSocketState.Closed) return;
        bool wasConnecting = _state == WebSocketState.Connecting;
        _state = WebSocketState.Closed;
        CloseCode = code;
        CloseReason = reason;
        if (code == 0) Error = reason;
        if (wasConnecting && _connect != null)
        {
            if (canceled) _connect.TrySetCanceled();
            else _connect.TrySetException(new Exception("websocket: " + reason));
        }
        for (int i = 0; i < _receivers.Count; i++)
        {
            _receivers[i].Reg.Dispose();
            if (canceled) _receivers[i].Tcs.TrySetCanceled();
            else _receivers[i].Tcs.TrySetException(new Exception("websocket closed: " + reason));
        }
        _receivers.Clear();
        if (!wasConnecting) OnClose?.Invoke(code, reason);
        _closeTask?.TrySetResult(true); // en son: await CloseAsync devami inline kosar, OnClose'u gormus olsun
    }

    public bool Send(string text)
    {
        if (_state == WebSocketState.Closed) return false;
        return NativeNet.SendBytes(_job, Encoding.UTF8.GetBytes(text ?? ""), true);
    }

    public bool Send(byte[] data)
    {
        if (_state == WebSocketState.Closed || data == null) return false;
        return NativeNet.SendBytes(_job, data, false);
    }

    // Siradaki mesaj (kuyrukta varsa hemen). Kapanista Exception / iptalde OperationCanceledException.
    public Task<WsMessage> ReceiveAsync(CancellationToken ct = default)
    {
        var r = new Receiver();
        if (_inbox.Count > 0)
        {
            var m = _inbox[0];
            _inbox.RemoveAt(0);
            r.Tcs.TrySetResult(m);
            return r.Tcs.Task;
        }
        if (_state == WebSocketState.Closed) { r.Tcs.TrySetException(new Exception("websocket closed: " + CloseReason)); return r.Tcs.Task; }
        if (ct.IsCancellationRequested) { r.Tcs.TrySetCanceled(ct); return r.Tcs.Task; }
        _receivers.Add(r);
        if (ct.CanBeCanceled)
            r.Reg = ct.Register(() => { if (_receivers.Remove(r)) r.Tcs.TrySetCanceled(ct); });
        return r.Tcs.Task;
    }

    // Kapanis el sikismasini baslatir; CLOSE tamamlaninca biter.
    public Task CloseAsync(int code = 1000, string reason = "")
    {
        if (_state == WebSocketState.Closed) return Task.CompletedTask;
        _closeTask ??= new TaskCompletionSource<bool>();
        NativeNet.WsClose(_job, code, reason ?? "");
        return _closeTask.Task;
    }

    // Beklemeden kapat ve kaynaklari birak.
    public void Dispose()
    {
        if (_state == WebSocketState.Closed) return;
        NativeAsync.Unlisten(_job);
        NativeNet.WsFree(_job);
        Finish(1000, "disposed", true);
    }
}
