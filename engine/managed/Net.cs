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
//
// ZERO-GC SOZLESMESI (docs/network.md "Zero-GC"): WS mesaji NATIVE bellekte kalir, managed ona Span ile bakar;
// kopya yalniz kullanici isterse (ToArray/Text). Sicak yol OnMessage (alloc yok). Gonderim Span alir; string
// gonderimi statik scratch'e kodlanir. HTTP: dusuk frekans, tek byte[] (basliklar+govde bitisik), tembel string.
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

    public static bool SendBytes(int ws, ReadOnlySpan<byte> data, bool isText)
    {
        fixed (byte* p = data) return WsSend(ws, p, data.Length, isText ? 1 : 0) == 0;
    }

    // string -> UTF-8, statik buyuyen scratch (GC yok; ana thread, tek kullanim: hemen native'e kopyalanir).
    static byte[] _scratch = new byte[256];
    public static ReadOnlySpan<byte> EncodeUtf8(string s)
    {
        if (string.IsNullOrEmpty(s)) return ReadOnlySpan<byte>.Empty;
        int need = s.Length * 3;
        if (_scratch.Length < need) _scratch = new byte[Math.Max(need, _scratch.Length * 2)];
        var b = _scratch;
        int n = 0;
        for (int i = 0; i < s.Length; i++)
        {
            int c = s[i];
            if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.Length && s[i + 1] >= 0xDC00 && s[i + 1] <= 0xDFFF)
                c = 0x10000 + ((c - 0xD800) << 10) + (s[++i] - 0xDC00);
            if (c < 0x80) b[n++] = (byte)c;
            else if (c < 0x800) { b[n++] = (byte)(0xC0 | (c >> 6)); b[n++] = (byte)(0x80 | (c & 0x3F)); }
            else if (c < 0x10000) { b[n++] = (byte)(0xE0 | (c >> 12)); b[n++] = (byte)(0x80 | ((c >> 6) & 0x3F)); b[n++] = (byte)(0x80 | (c & 0x3F)); }
            else { b[n++] = (byte)(0xF0 | (c >> 18)); b[n++] = (byte)(0x80 | ((c >> 12) & 0x3F)); b[n++] = (byte)(0x80 | ((c >> 6) & 0x3F)); b[n++] = (byte)(0x80 | (c & 0x3F)); }
        }
        return new ReadOnlySpan<byte>(b, 0, n);
    }

    // Baslik blogunda ("K: V\r\n" satirlari, bayt) ad arar (buyuk/kucuk harf duyarsiz, ASCII). Deger araligi doner.
    public static bool FindHeader(byte[] raw, int rawLen, string name, out int valStart, out int valLen)
    {
        valStart = 0; valLen = 0;
        if (raw == null || string.IsNullOrEmpty(name)) return false;
        int i = 0;
        while (i < rawLen)
        {
            int lineEnd = i;
            while (lineEnd < rawLen && raw[lineEnd] != (byte)'\n') lineEnd++;
            int colon = i;
            while (colon < lineEnd && raw[colon] != (byte)':') colon++;
            if (colon < lineEnd && colon - i == name.Length)
            {
                bool eq = true;
                for (int k = 0; k < name.Length && eq; k++)
                {
                    int a = raw[i + k], c = name[k];
                    if (a >= 'A' && a <= 'Z') a += 32;
                    if (c >= 'A' && c <= 'Z') c += 32;
                    eq = a == c;
                }
                if (eq)
                {
                    int vs = colon + 1, ve = lineEnd;
                    while (vs < ve && (raw[vs] == ' ' || raw[vs] == '\t')) vs++;
                    while (ve > vs && (raw[ve - 1] == ' ' || raw[ve - 1] == '\t' || raw[ve - 1] == '\r')) ve--;
                    valStart = vs; valLen = ve - vs;
                    return true;
                }
            }
            i = lineEnd + 1;
        }
        return false;
    }
}

public sealed class HttpRequest
{
    public string Method = "GET";
    public string Url;
    public byte[] Body; // istek bitene dek native kopyasi yasar (1 kopya zorunlu)
    public int TimeoutMs = 30000;
    List<KeyValuePair<string, string>> _headers;
    public HttpRequest Header(string name, string value)
    {
        (_headers ??= new List<KeyValuePair<string, string>>()).Add(new KeyValuePair<string, string>(name, value));
        return this;
    }
    internal string HeaderBlock => NativeNet.JoinHeaders(_headers);
}

// Yanit: TEK tampon (Raw = basliklar + govde bitisik; HeaderLength = baslik bayti). Govde Span dilimi;
// string'ler (Text/RawHeaders/Header) yalnizca istenince uretilir.
public sealed class HttpResponse
{
    public int Status;          // 0 = yanit yok (ag/zaman asimi hatasi; Error dolu)
    public string Error;        // null = tasima basarili (HTTP hata kodu olsa bile)
    public byte[] Raw = Array.Empty<byte>();
    public int HeaderLength;
    public bool Ok => Error == null && Status >= 200 && Status < 300;
    public int BodyLength => Raw.Length - HeaderLength;
    public ReadOnlySpan<byte> Body => new ReadOnlySpan<byte>(Raw, HeaderLength, Raw.Length - HeaderLength);
    public byte[] BodyToArray() => Body.ToArray();
    public string Text => BodyLength == 0 ? "" : Encoding.UTF8.GetString(Raw, HeaderLength, BodyLength);
    public string RawHeaders => HeaderLength == 0 ? "" : Encoding.UTF8.GetString(Raw, 0, HeaderLength); // "HTTP/1.1 200 OK\r\nK: V\r\n..."

    // Baslik degeri (ad buyuk/kucuk harf duyarsiz); yoksa null. Yalniz sonuc string'i alloc eder.
    public string Header(string name)
    {
        if (!NativeNet.FindHeader(Raw, HeaderLength, name, out int vs, out int vl)) return null;
        return vl == 0 ? "" : Encoding.UTF8.GetString(Raw, vs, vl);
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
            return new HttpResponse { Error = "url bos" };
        ct.ThrowIfCancellationRequested();
        int job = NativeNet.StartHttp(req);
        var c = await NativeAsync.Await(job, ct, () => NativeNet.HttpCancel(job));
        var res = new HttpResponse();
        if (!c.Ok)
            res.Error = c.Len > 0 ? NativeNet.Utf8(c.Data, 0, (int)c.Len) : "network error";
        else
        {
            res.Status = c.A;
            res.HeaderLength = c.B;
            res.Raw = NativeNet.CopyBytes(c.Data, 0, (int)c.Len); // native -> managed tek kopya, sahiplik kullanicida
        }
        NativeAsync.Free(c.Data);
        return res;
    }
}

public enum WebSocketState { Connecting, Open, Closed }

// Native bellekteki mesaja GORUNUM (kopya yok). Gecerlilik: OnMessage icinde callback donene dek; ReceiveAsync
// sonucu ayni soketteki BIR SONRAKI ReceiveAsync cagrisina ya da kapanisa dek. Saklamak icin ToArray().
public struct WsMessage
{
    public readonly IntPtr Data;
    public readonly int Length;
    public readonly bool IsText;
    internal WsMessage(IntPtr data, int length, bool isText) { Data = data; Length = length; IsText = isText; }
    public unsafe ReadOnlySpan<byte> Bytes => Data == IntPtr.Zero ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>((void*)Data, Length);
    public byte[] ToArray() => NativeNet.CopyBytes(Data, 0, Length);
    public string Text => Length == 0 ? "" : NativeNet.Utf8(Data, 0, Length); // her cagri alloc (acik)
}

// Tek baglanti. Alim iki bicimde: OnMessage (Pump'ta, ana thread; SIFIR alloc) ya da `await ReceiveAsync()`
// (TCS alloc; kolaylik). OnMessage atanmissa mesajlar kuyruga girmez. Kapanis/hata: State=Closed, bekleyen
// ReceiveAsync'ler iptal, OnClose. Kuyruktaki mesajlar native'de bekler (kopya yok), tuketilince/kapanista free.
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
    IntPtr _lastReceived; // ReceiveAsync ile verilen son mesaj; bir sonraki ReceiveAsync/kapanis free eder
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
            NativeAsync.Free(c.Data);
            _state = WebSocketState.Open;
            _connect?.TrySetResult(true);
            return;
        }
        if (c.Code == NativeNet.WsMessageEvent)
        {
            var m = new WsMessage(c.Data, (int)c.Len, c.A != 0);
            if (_receivers.Count > 0)
            {
                var r = _receivers[0];
                _receivers.RemoveAt(0);
                r.Reg.Dispose();
                ReleaseLast();
                _lastReceived = c.Data;
                r.Tcs.TrySetResult(m);
            }
            else if (OnMessage != null)
            {
                OnMessage(m);
                NativeAsync.Free(c.Data); // callback donunce gorunum gecersiz
            }
            else _inbox.Add(m); // native'de bekler
            return;
        }
        // son tamamlanma: CLOSE ya da hata
        string text = c.Len > 0 ? NativeNet.Utf8(c.Data, 0, (int)c.Len) : null;
        NativeAsync.Free(c.Data);
        NativeNet.WsFree(_job);
        if (c.Code == NativeNet.WsCloseCode) Finish(c.A, text ?? "", false);
        else Finish(0, text ?? "network error", false);
    }

    void ReleaseLast()
    {
        if (_lastReceived == IntPtr.Zero) return;
        NativeAsync.Free(_lastReceived);
        _lastReceived = IntPtr.Zero;
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
        for (int i = 0; i < _inbox.Count; i++) NativeAsync.Free(_inbox[i].Data);
        _inbox.Clear();
        ReleaseLast();
        if (!wasConnecting) OnClose?.Invoke(code, reason);
        _closeTask?.TrySetResult(true); // en son: await CloseAsync devami inline kosar, OnClose'u gormus olsun
    }

    // Metin: statik scratch'e UTF-8 kodlanir (GC yok), native kuyruguna kopyalanir.
    public bool Send(string text)
    {
        if (_state != WebSocketState.Open) return false;
        return NativeNet.SendBytes(_job, NativeNet.EncodeUtf8(text), true);
    }

    public bool Send(byte[] data) => data != null && Send(new ReadOnlySpan<byte>(data));

    public bool Send(ReadOnlySpan<byte> data)
    {
        if (_state != WebSocketState.Open) return false;
        return NativeNet.SendBytes(_job, data, false);
    }

    // Siradaki mesaj (kuyrukta varsa hemen). Donen gorunum bir sonraki ReceiveAsync'e / kapanisa dek gecerli.
    // Kapanista Exception / iptalde OperationCanceledException.
    public Task<WsMessage> ReceiveAsync(CancellationToken ct = default)
    {
        var r = new Receiver();
        if (_inbox.Count > 0)
        {
            var m = _inbox[0];
            _inbox.RemoveAt(0);
            ReleaseLast();
            _lastReceived = m.Data;
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
