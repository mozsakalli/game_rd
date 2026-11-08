using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Digitoy;

// SUNUCU BAGLANTISI (docs/server-protocol.md §3-4): settings -> gateway sirasi -> WebSocket -> auth-on-open -> Connected.
// Tek olumsuz dongu (_tick): govde try/catch'li, asla cikmaz; durum makinesi State ile. Kopunca 0/1/2 sn backoff,
// sonraki gateway; tum gateway'ler 3 turda duserse settings tazelenir ve Offline'da 5 sn'de bir yeniden dener.
// "Oldurucu" kapanislar (Logout/Unauthorized/AnotherLogin/Kicked/IdleTimeout) -> Killed, deneme yok; Start() acar.
// Istekler: SendAsync TCS'yi _pending'e kaydeder, 30 sn zaman asimi, kopmada hepsi null ile cozulur (firlatmaz).
// Heartbeat: 25 sn gonderim yoksa Endpoint.Ping. Hot reload: Park() soketi motora emanet eder (WebSocket.Keep),
// sonraki Start() Take ile alir (auth'lu soket, dogrudan Connected). _generation: eski dongu ilk await'te cikar.
// Her sey ana thread'de (NativeAsync.Pump): kilit yok.
public class Server
{
    public enum State { Stopped, LoadingSettings, Connecting, Connected, Reconnecting, Offline, Killed }

    public const string KeepKey = "server.ws";
    public const float RequestTimeout = 30f;
    public const float HeartbeatAfter = 25f;
    const float OfflineRetryAfter = 5f;
    static readonly float[] Backoff = { 0f, 1f, 2f };

    static State _state = State.Stopped;
    static WebSocket _webSocket;
    static string[] _seeds = new string[0];
    static string[] _gateways = new string[0];
    static bool _settingsLoaded;
    static int _gatewayIndex;
    static int _attempt;            // mevcut gateway turundaki deneme (Backoff indeksi)
    static DateTime _lastSend;
    static DateTime _offlineSince;
    static int _generation;         // dongu bayatlama bekcisi (Park/Stop sonrasi eski dongu cikar)
    static bool _ticking;
    static readonly Codec _codec = new(isClient: true);
    static AuthRequest _authRequest;
    static string _jwtToken;
    static int _nextRequestId;
    static readonly Dictionary<int, (TaskCompletionSource<object> tcs, DateTime deadline)> _pending = new();
    static readonly List<int> _expired = new();
    static byte[] _rx = new byte[4096];

    public static bool UseTestServer;
    public static int GameId;
    public static string IosForceVersion { get; private set; } = "";
    public static string AndroidForceVersion { get; private set; } = "";
    public static bool IsMaintenance { get; private set; }
    public static int PolicyVersion { get; private set; }
    public static string PrivacyUrl { get; private set; } = "";
    public static string TermsUrl { get; private set; } = "";

    public static string AppleToken;
    public static string FBLinkToken;
    public static string FBToken;

    public static State Current => _state;
    public static string LastError { get; private set; } = "";
    public static long PlayerId { get; private set; }
    public static bool IsConnected => _state == State.Connected && _webSocket != null && _webSocket.State == WebSocketState.Open;
    public static Action<State> OnStateChanged;
    public static Action<object> OnPush;   // requestId == -1: sunucu push'u

    // Baglantiyi acar (Stopped/Killed/Offline'dan cikar). Emanet soket varsa (hot reload) onu sahiplenir.
    public static void Start()
    {
        if (_webSocket == null)
        {
            var kept = WebSocket.Take(KeepKey);
            if (kept != null)
            {
                Debug.Log("[server] adopted live socket (hot reload)");
                Attach(kept);
                SetState(State.Connected);
            }
        }
        if (_state != State.Connected)
        {
            _attempt = 0;
            SetState(_webSocket != null ? State.Connected : (_settingsLoaded ? State.Connecting : State.LoadingSettings));
        }
        if (!_ticking)
        {
            _ticking = true;
            _ = Tick(++_generation);
        }
    }

    // Kapatir, dongu durur. Start() yeniden acar.
    public static void Stop()
    {
        _generation++;
        _ticking = false;
        Close();
        SetState(State.Stopped);
    }

    // Sahne yikiliyor / kod yeniden yukleniyor: soketi motora emanet et. Bekleyen istekler null ile cozulur
    // (cagiranlar zaten olecek). Dongu durur; sonraki Start() soketi Take ile geri alir.
    public static void Park()
    {
        _generation++;
        _ticking = false;
        var ws = _webSocket;
        _webSocket = null;
        FailAll();
        if (ws != null)
        {
            ws.OnClose = null;
            ws.OnMessage = null;
            WebSocket.Keep(KeepKey, ws);
        }
        SetState(State.Stopped);
    }

    public static void Logout()
    {
        _jwtToken = null;
        if (_webSocket != null) _ = _webSocket.CloseAsync((int)CloseReason.Logout, "logout");
        else SetState(State.Killed);
    }

    // --- istekler ---

    // Cevapsiz paket (requestId = -1). false = bagli degil / gonderilemedi.
    public static bool Send(int endpoint, object payload)
    {
        if (!IsConnected) return false;
        _codec.BeginEncoding();
        _codec.Encode(-1);
        _codec.Encode(endpoint);
        _codec.EncodeObject(payload);
        var seg = _codec.GetBuffer();
        if (!_webSocket.Send(new ReadOnlySpan<byte>(seg.Array, seg.Offset, seg.Count))) return false;
        _lastSend = DateTime.UtcNow;
        return true;
    }

    // Istek-cevap. null = bagli degil / gonderilemedi / zaman asimi / kopma / cevap tipi uyusmuyor.
    public static async Task<TResponse> SendAsync<TResponse>(int endpoint, object payload) where TResponse : class
        => await SendAsync(endpoint, payload) as TResponse;

    public static Task<object> SendAsync(int endpoint, object payload)
    {
        if (!IsConnected) return Task.FromResult<object>(null);
        int id = ++_nextRequestId;
        _codec.BeginEncoding();
        _codec.Encode(id);
        _codec.Encode(endpoint);
        _codec.EncodeObject(payload);
        var seg = _codec.GetBuffer();
        if (!_webSocket.Send(new ReadOnlySpan<byte>(seg.Array, seg.Offset, seg.Count))) return Task.FromResult<object>(null);
        _lastSend = DateTime.UtcNow;
        var tcs = new TaskCompletionSource<object>();
        _pending[id] = (tcs, DateTime.UtcNow.AddSeconds(RequestTimeout));
        return tcs.Task;
    }

    static void OnMessage(WsMessage message)
    {
        if (message.IsText || message.Length == 0) return;
        try
        {
            // Mesaj native bellekte (zero-GC); Codec byte[] istiyor -> yeniden kullanilan tampona tek kopya.
            if (_rx.Length < message.Length) _rx = new byte[Math.Max(message.Length, _rx.Length * 2)];
            message.Bytes.CopyTo(_rx);
            _codec.BeginDecoding(_rx, message.Length, 0);
            var requestId = (int)_codec.TryDecodeNumber();
            var body = _codec.DecodeObject();
            if (requestId > 0)
            {
                if (_pending.Remove(requestId, out var p)) p.tcs.TrySetResult(body);
            }
            else if (requestId == -1) OnPush?.Invoke(body);
            // 0 = protokol ping cevabi, yok say
        }
        catch (Exception e)
        {
            Debug.LogError("[server] message decode failed (len=" + message.Length + "): " + e);
        }
    }

    static void FailAll()
    {
        if (_pending.Count == 0) return;
        var all = new List<TaskCompletionSource<object>>(_pending.Count);
        foreach (var kv in _pending) all.Add(kv.Value.tcs);
        _pending.Clear();
        foreach (var t in all) t.TrySetResult(null);
    }

    // --- baglanti ---

    static void Attach(WebSocket ws)
    {
        _webSocket = ws;
        _lastSend = DateTime.UtcNow;
        ws.OnClose = OnClose;
        ws.OnMessage = OnMessage; // atama birikmis mesajlari akitir (Keep/Take arasi)
    }

    static void Close()
    {
        var ws = _webSocket;
        _webSocket = null;
        FailAll();
        if (ws == null) return;
        ws.OnClose = null;
        ws.OnMessage = null;
        ws.Dispose();
    }

    static void OnClose(int code, string reason)
    {
        Debug.Log("[server] closed " + code + " " + reason);
        Close();
        if (_state == State.Stopped || _state == State.Killed) return;
        var cr = (CloseReason)code;
        if (cr is CloseReason.Logout or CloseReason.Unauthorized or CloseReason.AnotherLogin or CloseReason.Kicked or CloseReason.IdleTimeout)
        {
            LastError = cr.ToString();
            Kill();
            return;
        }
        LastError = "closed: " + code + " " + reason;
        _attempt = 0;
        SetState(State.Reconnecting);
    }

    static void Kill()
    {
        FBLinkToken = null;
        FBToken = null;
        AppleToken = null;
        _authRequest = null;
        SetState(State.Killed);
    }

    static void SetState(State s)
    {
        if (_state == s) return;
        _state = s;
        Debug.Log("[server] state " + s + (s == State.Offline || s == State.Killed ? " (" + LastError + ")" : ""));
        try { OnStateChanged?.Invoke(s); }
        catch (Exception e) { Debug.LogException(e); }
    }

    // OLUMSUZ DONGU: her tur tek bir adim; govde try/catch'li. gen != _generation -> bu dongu bayat, cik.
    static async Task Tick(int gen)
    {
        while (gen == _generation)
        {
            float wait = 0.5f;
            try { wait = await Step(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                wait = 1f;
            }
            if (gen != _generation) return;
            await Wait.Seconds(wait);
        }
    }

    // Bir adim; donus = sonraki adima kadar bekleme (sn).
    static async Task<float> Step()
    {
        switch (_state)
        {
            case State.Stopped:
            case State.Killed:
                if (_webSocket != null) Close();
                return 0.5f;

            case State.Offline:
                if ((DateTime.UtcNow - _offlineSince).TotalSeconds < OfflineRetryAfter) return 0.5f;
                _attempt = 0;
                SetState(_settingsLoaded ? State.Connecting : State.LoadingSettings);
                return 0f;

            case State.LoadingSettings:
                await LoadSettings();
                if (!_settingsLoaded || _gateways == null || _gateways.Length == 0)
                {
                    LastError = "settings unreachable";
                    return 1f;
                }
                _gatewayIndex = 0;
                _attempt = 0;
                SetState(State.Connecting);
                return 0f;

            case State.Connecting:
            case State.Reconnecting:
                return await ConnectStep();

            case State.Connected:
                if (_webSocket == null || _webSocket.State == WebSocketState.Closed)
                {
                    // OnClose kacirilmis (Dispose vb.): kopma olarak isle
                    if (_webSocket != null) OnClose(_webSocket.CloseCode, _webSocket.CloseReason ?? "closed");
                    else { _attempt = 0; SetState(State.Reconnecting); }
                    return 0f;
                }
                ExpireRequests();
                if ((DateTime.UtcNow - _lastSend).TotalSeconds >= HeartbeatAfter)
                    _ = SendAsync(Endpoint.Ping, null);
                return 0.5f;
        }
        return 0.5f;
    }

    // Tek baglanti denemesi. Basarisiz -> backoff; gateway turu bitince settings tazele / Offline.
    static async Task<float> ConnectStep()
    {
        if (_gateways == null || _gateways.Length == 0) { SetState(State.LoadingSettings); return 0f; }
        if (_gatewayIndex >= _gateways.Length) _gatewayIndex = 0;
        if (_attempt >= Backoff.Length)
        {
            // bu gateway bitti -> siradaki; tum liste bittiyse settings bayat olabilir
            _attempt = 0;
            _gatewayIndex++;
            if (_gatewayIndex >= _gateways.Length)
            {
                _gatewayIndex = 0;
                _settingsLoaded = false;
                _offlineSince = DateTime.UtcNow;
                SetState(State.Offline);
                return 0.5f;
            }
        }
        float delay = Backoff[_attempt++];
        if (delay > 0) await Wait.Seconds(delay);
        if (_state != State.Connecting && _state != State.Reconnecting) return 0f; // beklerken Stop/Park geldi

        string host = _gateways[_gatewayIndex];
        string url = (OperatingSystem.IsBrowser() ? "wss" : "ws") + "://" + host + "/api/websocket/connect";
        WebSocket ws;
        try { ws = await WebSocket.ConnectAsync(url); }
        catch (Exception e)
        {
            LastError = "connect " + host + ": " + e.Message;
            Debug.Log("[server] " + LastError);
            return 0f;
        }
        if (_state != State.Connecting && _state != State.Reconnecting) { ws.Dispose(); return 0f; }
        Attach(ws);

        // Auth-on-open: tasima kuralini SendAsync uygular; IsConnected icin gecici Connected (olay atmadan).
        var prev = _state;
        _state = State.Connected;
        var auth = await SendAsync<AuthResult>(Endpoint.AuthLogin, BuildAuthRequest());
        if (_webSocket != ws) return 0f; // beklerken koptu/park edildi; OnClose durumu isledi
        _state = prev;
        if (auth == null)
        {
            LastError = "auth: no response";
            Close();
            return 0f;
        }
        if (!auth.Ok && auth.Error != ErrorCode.AlreadyLoggedIn)
        {
            LastError = "auth rejected: " + auth.Error;
            Debug.Log("[server] " + LastError);
            Kill();
            _ = CloseLater(ws); // sunucu 3 sn icinde kapatmazsa biz
            return 0f;
        }
        PlayerId = auth.PlayerId;
        if (!string.IsNullOrEmpty(auth.Token))
        {
            _jwtToken = auth.Token;
        }
        _attempt = 0;
        Debug.Log("[server] authenticated player " + PlayerId + " via " + host);
        SetState(State.Connected);
        return 0.5f;
    }

    static AuthRequest BuildAuthRequest()
    {
        _authRequest ??= new AuthRequest { GameId = GameId, Params = new() };
        _authRequest.GameId = GameId;
        _authRequest.Token = _jwtToken;
        _authRequest.FbToken = _authRequest.FbLinkToken = _authRequest.AppleToken = null;
        if (FBToken != null) _authRequest.FbToken = FBToken;
        else if (FBLinkToken != null) _authRequest.FbLinkToken = FBLinkToken;
        else if (AppleToken != null) _authRequest.AppleToken = AppleToken;
        _authRequest.Params[AuthRequestParams.DeviceKey] = "dummy-digitoy-client";
        return _authRequest;
    }

    static async Task CloseLater(WebSocket ws)
    {
        await Wait.Seconds(3f);
        if (ws.State != WebSocketState.Closed) ws.Dispose();
    }

    static void ExpireRequests()
    {
        if (_pending.Count == 0) return;
        var now = DateTime.UtcNow;
        foreach (var kv in _pending) if (kv.Value.deadline <= now) _expired.Add(kv.Key);
        for (int i = 0; i < _expired.Count; i++)
            if (_pending.Remove(_expired[i], out var p)) p.tcs.TrySetResult(null);
        if (_expired.Count > 0) Debug.LogWarning("[server] " + _expired.Count + " request(s) timed out");
        _expired.Clear();
    }

    // --- settings ---

    public static async Task LoadSettings()
    {
        var tmp = PlayerPrefs.GetString("__seeds__", "");
        if (!string.IsNullOrEmpty(tmp)) _seeds = tmp.Split(',');
        tmp = PlayerPrefs.GetString("__gateways__", "");
        if (!string.IsNullOrEmpty(tmp)) _gateways = tmp.Split(',');

        if (_seeds == null || _seeds.Length == 0)
            _seeds = new[] { UseTestServer ? "test-node.digitoy.co" : "api.digitoy.co" };

        for (var i = 0; i < _seeds.Length; i++)
        {
            try
            {
                var response = await Http.GetAsync($"https://{_seeds[i]}/api/config/{GameId}/settings");
                if (!response.Ok)
                {
                    Debug.Log("[server] settings " + _seeds[i] + ": " + (response.Error ?? response.Status.ToString()));
                    continue;
                }
                var json = Json.Parse(response.Text);
                var settings = json["settings"];
                if (settings != null)
                {
                    IosForceVersion = settings.GetString("ios_force_version");
                    AndroidForceVersion = settings.GetString("android_force_version");
                    IsMaintenance = settings.GetBool("maintenance");
                    PolicyVersion = settings.GetInt("policy_version");
                    PrivacyUrl = settings.GetString("privacy_url");
                    TermsUrl = settings.GetString("terms_url");
                }
                var gateways = ReadList(json["gateways"]);
                if (gateways != null)
                {
                    _gateways = gateways;
                    PlayerPrefs.SetString("__gateways__", string.Join(",", _gateways));
                }
                var seeds = ReadList(json["seeds"]);
                if (seeds != null)
                {
                    _seeds = seeds;
                    PlayerPrefs.SetString("__seeds__", string.Join(",", _seeds));
                }
                PlayerPrefs.Save();
                _settingsLoaded = _gateways != null && _gateways.Length > 0;
                Debug.Log("[server] settings loaded; gateways: " + string.Join(",", _gateways));
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[server] settings " + _seeds[i] + " failed: " + e.Message);
            }
        }
    }

    static string[] ReadList(JsonNode node)
    {
        if (node == null || node.Count == 0) return null;
        var list = new List<string>(node.Count);
        for (int k = 0; k < node.Count; k++)
        {
            var s = node[k].AsString();
            if (!string.IsNullOrEmpty(s)) list.Add(s);
        }
        return list.Count > 0 ? list.ToArray() : null;
    }
}
