#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;

namespace DigitoyEditor;

// Acilis smoke: Http + WebSocket (de_net WinHTTP) — localhost HttpListener sunucusuna karsi.
// Oyun kodu ile AYNI yol (NativeAsync.Pump ana thread'de elle surulur). Sunucu .NET'in; istemci bizim native.
public static class NetTests
{
    static int _pass, _fail;

    public static void Run()
    {
        _pass = _fail = 0;
        HttpListener listener = null;
        int port = 0;
        // DE_NET_SERVER_PORT=N: sunucu bu portta acilir ve EDITOR KAPANANA DEK kalir (player/wasm net-probe hedefi; CORS acik).
        string keepEnv = Environment.GetEnvironmentVariable("DE_NET_SERVER_PORT");
        int keepPort = int.TryParse(keepEnv, out int kp) ? kp : 0;
        try
        {
            for (int attempt = 0; attempt < 10 && listener == null; attempt++)
            {
                port = keepPort > 0 ? keepPort : 18200 + new Random().Next(1000);
                var l = new HttpListener();
                l.Prefixes.Add($"http://127.0.0.1:{port}/");
                try { l.Start(); listener = l; } catch { if (keepPort > 0) break; }
            }
            if (listener == null) { Console.WriteLine("[net] SKIP: HttpListener acilamadi"); return; }
            _ = ServeAsync(listener);
            string baseUrl = $"http://127.0.0.1:{port}";

            var t = Run(Scenario(baseUrl));
            Check(t.IsCompletedSuccessfully, "senaryo tamamlandi" + (t.IsFaulted ? ": " + t.Exception?.GetBaseException().Message : ""));
            if (keepPort > 0) { Console.WriteLine($"[net] test sunucusu acik kaliyor: {baseUrl}"); listener = null; }
        }
        finally
        {
            try { listener?.Stop(); } catch { }
        }
        Console.WriteLine($"[net] {_pass} PASS, {_fail} FAIL");
    }

    // Ana thread pompasi: Task bitene kadar Pump + kisa uyku (frame dongusu taklidi).
    static Task Run(Task task)
    {
        for (int i = 0; i < 2000 && !task.IsCompleted; i++)
        {
            NativeAsync.Pump(0.016f);
            if (!task.IsCompleted) Thread.Sleep(5);
        }
        return task;
    }

    static async Task Scenario(string baseUrl)
    {
        // GET + baslik
        var r = await Http.GetAsync(baseUrl + "/hello");
        Check(r.Ok && r.Status == 200 && r.Text == "merhaba", $"GET 200 govde ({r.Status} {r.Error} '{r.Text}')");
        Check(r.Header("X-Test") == "evet" && r.Header("x-test") == "evet", "yanit basligi (buyuk/kucuk harf duyarsiz)");

        // POST govde + istek basligi echo
        var body = Encoding.UTF8.GetBytes("selam dünya");
        var p = await Http.SendAsync(new HttpRequest { Method = "POST", Url = baseUrl + "/echo", Body = body }.Header("X-Echo", "deger").Header("Content-Type", "text/plain"));
        Check(p.Ok && p.Text == "selam dünya" && p.Header("X-Echo-Back") == "deger", $"POST echo + istek basligi ({p.Status} '{p.Text}' {p.Header("X-Echo-Back")})");
        var pj = await Http.PostAsync(baseUrl + "/echo", "{\"a\":1}");
        Check(pj.Ok && pj.Text == "{\"a\":1}" && pj.Header("X-Content-Type") == "application/json", "PostAsync(string) JSON content-type");

        // 404: tasima basarili, Ok false
        var nf = await Http.GetAsync(baseUrl + "/yok");
        Check(!nf.Ok && nf.Status == 404 && nf.Error == null, "404 -> Ok=false, Error yok");

        // Buyuk govde (parcali okuma)
        var big = await Http.GetAsync(baseUrl + "/big");
        Check(big.Ok && big.Body.Length == 300000 && big.Body[0] == (byte)'a' && big.Body[299999] == (byte)'a', $"300 KB govde parcali okuma ({big.Body?.Length})");

        // Baglanti hatasi (kapali port): Error dolu, status 0
        var bad = await Http.GetAsync("http://127.0.0.1:1/x");
        Check(!bad.Ok && bad.Status == 0 && !string.IsNullOrEmpty(bad.Error), $"baglanti hatasi Error ({bad.Error})");

        // Iptal: yavas endpoint, token iptal -> OperationCanceledException, kayit kalmaz
        var cts = new CancellationTokenSource();
        var slow = Http.GetAsync(baseUrl + "/slow", cts.Token);
        await Frame.Next();
        cts.Cancel();
        bool canceled = false;
        try { await slow; } catch (OperationCanceledException) { canceled = true; }
        Check(canceled && NativeAsync.PendingCount == 0, "HTTP iptal -> OperationCanceledException");

        // Paralel istekler
        var all = Task.WhenAll(new Task[] { Http.GetAsync(baseUrl + "/hello"), Http.GetAsync(baseUrl + "/hello"), Http.GetAsync(baseUrl + "/hello") });
        await all;
        Check(all.IsCompletedSuccessfully, "3 paralel GET");

        // WebSocket: baglan, metin/ikili echo, ReceiveAsync + OnMessage, kapat
        var ws = await WebSocket.ConnectAsync(baseUrl.Replace("http://", "ws://") + "/ws");
        Check(ws.State == WebSocketState.Open, "WS baglandi");
        ws.Send("ping");
        var m1 = await ws.ReceiveAsync();
        Check(m1.IsText && m1.Text == "echo:ping", $"WS metin echo ('{m1.Text}')");
        ws.Send(new byte[] { 1, 2, 3, 250 });
        var m2 = await ws.ReceiveAsync();
        Check(!m2.IsText && m2.Data.Length == 4 && m2.Data[3] == 250, "WS ikili echo");
        // sunucudan 3 mesaj pes pese -> kuyruk sirasi
        ws.Send("burst");
        var b1 = await ws.ReceiveAsync(); var b2 = await ws.ReceiveAsync(); var b3 = await ws.ReceiveAsync();
        Check(b1.Text == "b1" && b2.Text == "b2" && b3.Text == "b3", "WS kuyruk sirasi");
        int closeCode = -1;
        ws.OnClose = (code, reason) => closeCode = code;
        await ws.CloseAsync(1000, "bye");
        Check(ws.State == WebSocketState.Closed && closeCode == 1000 && ws.CloseCode == 1000, $"WS kapanis el sikisma (code {ws.CloseCode})");

        // WS: sunucu kapatir -> OnClose + bekleyen Receive hata
        var ws2 = await WebSocket.ConnectAsync(baseUrl.Replace("http://", "ws://") + "/ws");
        var pending = ws2.ReceiveAsync();
        ws2.Send("close-me");
        bool recvFailed = false;
        try { await pending; } catch (Exception) { recvFailed = true; }
        Check(recvFailed && ws2.State == WebSocketState.Closed && ws2.CloseCode == 4000, $"sunucu kapanisi: Receive hata, kod {ws2.CloseCode}");

        // WS: baglanti iptali (token)
        var cts2 = new CancellationTokenSource();
        var connecting = WebSocket.ConnectAsync("ws://127.0.0.1:1/ws", cts2.Token);
        cts2.Cancel();
        bool wsCanceled = false;
        try { await connecting; } catch (OperationCanceledException) { wsCanceled = true; } catch (Exception) { }
        Check(wsCanceled || connecting.IsFaulted, "WS baglanti iptal/hata");

        // WS: baglanti reddi (HTTP endpoint'e ws)
        bool refused = false;
        try { await WebSocket.ConnectAsync(baseUrl.Replace("http://", "ws://") + "/hello"); } catch (Exception) { refused = true; }
        Check(refused, "WS upgrade reddi Exception");
    }

    static async Task ServeAsync(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Handle(ctx));
        }
    }

    static async Task Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request; var res = ctx.Response;
        try
        {
            string path = req.Url.AbsolutePath;
            // CORS (wasm probe baska origin'den gelir): tum yanitlara izin + preflight
            res.Headers["Access-Control-Allow-Origin"] = "*";
            res.Headers["Access-Control-Allow-Headers"] = "*";
            res.Headers["Access-Control-Expose-Headers"] = "*";
            if (req.HttpMethod == "OPTIONS") { res.StatusCode = 204; res.Close(); return; }
            if (req.IsWebSocketRequest)
            {
                if (path != "/ws") { res.StatusCode = 400; res.Close(); return; }
                var wsc = await ctx.AcceptWebSocketAsync(null);
                var sock = wsc.WebSocket;
                var buf = new byte[65536];
                while (sock.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var rr = await sock.ReceiveAsync(new ArraySegment<byte>(buf), CancellationToken.None);
                    if (rr.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        await sock.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                        break;
                    }
                    if (rr.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        string s = Encoding.UTF8.GetString(buf, 0, rr.Count);
                        if (s == "burst")
                        {
                            foreach (var b in new[] { "b1", "b2", "b3" })
                                await sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(b)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                        }
                        else if (s == "close-me")
                            await sock.CloseAsync((System.Net.WebSockets.WebSocketCloseStatus)4000, "server-close", CancellationToken.None);
                        else
                            await sock.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("echo:" + s)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    else
                        await sock.SendAsync(new ArraySegment<byte>(buf, 0, rr.Count), System.Net.WebSockets.WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                return;
            }
            byte[] outBytes;
            switch (path)
            {
                case "/hello":
                    res.Headers["X-Test"] = "evet";
                    outBytes = Encoding.UTF8.GetBytes("merhaba");
                    break;
                case "/echo":
                    {
                        using var ms = new System.IO.MemoryStream();
                        await req.InputStream.CopyToAsync(ms);
                        outBytes = ms.ToArray();
                        res.Headers["X-Echo-Back"] = req.Headers["X-Echo"] ?? "";
                        res.Headers["X-Content-Type"] = req.ContentType ?? "";
                        break;
                    }
                case "/big":
                    outBytes = new byte[300000];
                    for (int i = 0; i < outBytes.Length; i++) outBytes[i] = (byte)'a';
                    break;
                case "/slow":
                    await Task.Delay(3000);
                    outBytes = Encoding.UTF8.GetBytes("gec");
                    break;
                default:
                    res.StatusCode = 404;
                    outBytes = Encoding.UTF8.GetBytes("yok");
                    break;
            }
            res.ContentLength64 = outBytes.Length;
            await res.OutputStream.WriteAsync(outBytes, 0, outBytes.Length);
            res.Close();
        }
        catch { try { res.Abort(); } catch { } }
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[net] FAIL: {name}"); }
    }
}
#endif
