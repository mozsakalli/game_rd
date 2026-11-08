using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Net;

// Kablo (docs/server-protocol.md "Cerceveleme"): tek WebSocket + Codec. Cerceve = requestId(long) endpoint(long) govde.
// Cevap requestId>0 bekleyen istegi cozer, -1 = push (OnPush), 0 = protokol ping. Istek zaman asimi 30 sn, 25 sn
// gonderim yoksa Endpoint.Ping (sunucu 60 sn'de duser). Yalniz tasima: auth/oturum Session'da, model World'de.
// Hot reload: Detach() soketi dinleyicisiz birakir (motor Keep ile tutar), Adopt() geri alir — bekleyen istekler
// o anda iptal olur (kod yeniden yuklendi, cagiranlar zaten yok).
public sealed class Wire
{
    public const float RequestTimeout = 30f;
    public const float HeartbeatAfter = 25f;

    readonly Codec _codec = new(isClient: true);
    byte[] _rx = new byte[4096];
    readonly Dictionary<long, (TaskCompletionSource<object> tcs, DateTime deadline)> _pending = new();
    readonly List<long> _expired = new();
    long _nextId;
    DateTime _lastSend;
    int _generation; // tick dongusu bayatlama bekcisi (Detach sonrasi eski dongu cikar)

    public WebSocket Socket { get; private set; }
    public bool IsOpen => Socket != null && Socket.State == WebSocketState.Open;
    public Action<object> OnPush;
    public Action<int, string> OnClosed; // kapanis kodu (CloseReason 4000+ ya da 1000/0), sebep

    // Yeni baglanti. Basarisizlikta Exception (gateway ulasilamaz).
    public static async Task<Wire> ConnectAsync(string url, CancellationToken ct)
    {
        var ws = await WebSocket.ConnectAsync(url, ct);
        var w = new Wire();
        w.Adopt(ws);
        return w;
    }

    // Var olan (kimlikli) soketi sahiplen.
    public static Wire From(WebSocket ws)
    {
        var w = new Wire();
        w.Adopt(ws);
        return w;
    }

    void Adopt(WebSocket ws)
    {
        Socket = ws;
        _lastSend = DateTime.UtcNow;
        ws.OnClose = (code, reason) => { FailAll("closed: " + reason); OnClosed?.Invoke(code, reason); };
        ws.OnMessage = OnMessage; // atama birikmis mesajlari akitir
        _ = TickAsync(++_generation);
    }

    // Soketi dinleyicisiz birak ve ver (cagiran WebSocket.Keep yapar). Bekleyen istekler iptal.
    public WebSocket Detach()
    {
        var ws = Socket;
        Socket = null;
        _generation++;
        FailAll("detached");
        return ws;
    }

    public void Close(int code = 1000, string reason = "")
    {
        var ws = Socket;
        if (ws == null) return;
        _ = ws.CloseAsync(code, reason);
    }

    public async Task<T> RequestAsync<T>(int endpoint, object body, CancellationToken ct = default) where T : class
    {
        var r = await RequestAsync(endpoint, body, ct);
        return r as T;
    }

    // Govde: sunucunun yanit nesnesi (tip Codec'ten). Soket kapali/istek dusmusse Exception; zaman asimi TaskCanceled.
    public Task<object> RequestAsync(int endpoint, object body, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<object>();
        var ws = Socket;
        if (ws == null || ws.State != WebSocketState.Open)
        {
            tcs.TrySetException(new Exception("wire: not connected (endpoint " + endpoint + ")"));
            return tcs.Task;
        }
        long id = ++_nextId;
        _pending[id] = (tcs, DateTime.UtcNow.AddSeconds(RequestTimeout));
        if (ct.CanBeCanceled)
            ct.Register(() => { if (_pending.Remove(id)) tcs.TrySetCanceled(ct); });
        _codec.BeginEncoding();
        _codec.Encode(id);
        _codec.Encode((long)endpoint);
        _codec.EncodeObject(body);
        var seg = _codec.GetBuffer();
        if (!ws.Send(new ReadOnlySpan<byte>(seg.Array, seg.Offset, seg.Count)))
        {
            _pending.Remove(id);
            tcs.TrySetException(new Exception("wire: send failed (endpoint " + endpoint + ")"));
            return tcs.Task;
        }
        _lastSend = DateTime.UtcNow;
        return tcs.Task;
    }

    void OnMessage(WsMessage msg)
    {
        if (msg.IsText || msg.Length == 0) return;
        try
        {
            if (_rx.Length < msg.Length) _rx = new byte[Math.Max(msg.Length, _rx.Length * 2)];
            msg.Bytes.CopyTo(_rx);
            _codec.BeginDecoding(_rx, msg.Length, 0);
            long id = _codec.TryDecodeNumber();
            object body = _codec.DecodeObject();
            if (id == -1) { OnPush?.Invoke(body); return; }
            if (id == 0) return;
            if (_pending.Remove(id, out var p)) p.tcs.TrySetResult(body);
        }
        catch (Exception e)
        {
            Debug.LogError("[wire] message decode failed (len=" + msg.Length + "): " + e);
        }
    }

    async Task TickAsync(int gen)
    {
        while (gen == _generation && IsOpen)
        {
            await Wait.Seconds(1f);
            if (gen != _generation || !IsOpen) return;
            var now = DateTime.UtcNow;
            if (_pending.Count > 0)
            {
                foreach (var kv in _pending) if (kv.Value.deadline <= now) _expired.Add(kv.Key);
                for (int i = 0; i < _expired.Count; i++)
                    if (_pending.Remove(_expired[i], out var p)) p.tcs.TrySetCanceled();
                if (_expired.Count > 0) Debug.LogWarning("[wire] " + _expired.Count + " request(s) timed out");
                _expired.Clear();
            }
            if ((now - _lastSend).TotalSeconds >= HeartbeatAfter)
                _ = RequestAsync(Endpoint.Ping, null);
        }
    }

    void FailAll(string why)
    {
        if (_pending.Count == 0) return;
        var all = new List<TaskCompletionSource<object>>(_pending.Count);
        foreach (var kv in _pending) all.Add(kv.Value.tcs);
        _pending.Clear();
        foreach (var t in all) t.TrySetException(new Exception("wire: " + why));
    }
}
