using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DigitoyEngine;
using M;

namespace Net;

// OTURUM (docs/server-protocol.md "Oturum"): settings -> gateway sirasi -> soket -> auth-on-open -> Defs ‖ Me ->
// masadaysam Refresh -> Ready. Kopunca 0/1/2 sn ile 3 deneme; "oldurucu" kapanislarda (Logout/Unauthorized/
// AnotherLogin/Kicked/IdleTimeout) deneme yok, Killed. Tek kurtarma kapisi Resync(): Me + Table tam gorunum.
// Hot reload: Park() soketi motora emanet eder (WebSocket.Keep); sonraki Start() Take ile alir, oturum kimlikli
// kaldigi icin yalniz Resync kosar. Oyun kodu statikleri reload'da sifirlandigindan burada "kalici" tek sey sokettir.
public static class Session
{
    public enum State { Idle, Connecting, Loading, Ready, Reconnecting, Killed, Offline }

    public const string KeepKey = "session.ws";
    const string TokenKey = "auth.token";
    const string SettingsUrl = "https://api.digitoy.co/api/config/{0}/settings";
    public const int ConfigGameId = 5;

    public static int GameId { get; private set; }
    public static State Current { get; private set; } = State.Idle;
    public static string Gateway { get; private set; } = "";
    public static string LastError { get; private set; } = "";
    public static int Generation { get; private set; } // her (yeniden) baglanti +1: ekran "baglanti degisti" farkeder
    public static bool IsReady => Current == State.Ready && _wire != null && _wire.IsOpen;

    static Wire _wire;
    static Task _connectTask;
    static List<string> _gateways = new();
    static int _gatewayIndex;

    // Sahne Start'i: emanet soket varsa al ve Resync; yoksa tam baglanti.
    public static void Start(int gameId)
    {
        GameId = gameId;
        if (_wire != null) return; // ayni kod yasam dongusunde ikinci sahne
        var ws = WebSocket.Take(KeepKey);
        if (ws != null)
        {
            Debug.Log("[session] adopted live socket (hot reload)");
            Attach(Wire.From(ws));
            Current = State.Loading;
            _connectTask = AdoptAsync();
            return;
        }
        if (_connectTask == null) _connectTask = ConnectLoopAsync(fresh: true);
    }

    static async Task AdoptAsync()
    {
        try { await LoadAsync(); }
        finally { _connectTask = null; }
    }

    // Sahne yikiliyor: soketi motora birak (hot reload / sahne gecisi). Stop'ta motor kapatir.
    public static void Park()
    {
        if (_wire == null) return;
        var ws = _wire.Detach();
        _wire = null;
        WebSocket.Keep(KeepKey, ws);
    }

    public static void Logout()
    {
        PlayerPrefs.DeleteKey(TokenKey);
        PlayerPrefs.Save();
        _wire?.Close((int)CloseReason.Logout, "logout");
    }

    // --- istekler ---
    public static async Task<T> RequestAsync<T>(int endpoint, object body) where T : class
    {
        if (_wire == null) throw new Exception("session: no connection");
        var r = await _wire.RequestAsync(endpoint, body);
        World.Apply(r);
        return r as T;
    }

    // Masa istegi: hata -> ErrorCode; TableState cevabi World'e (reconcile = tam gorunum).
    public static async Task<ErrorCode> TableAsync(int endpoint, GameAction action, List<long> parameters = null,
        List<FlagQuery> queries = null, long? bet = null, int? tableId = null, bool reconcile = false)
    {
        if (_wire == null || !_wire.IsOpen) return ErrorCode.NoInternet;
        var req = new GameActionRequest
        {
            GameId = GameId, TableId = tableId ?? World.Table?.TableId, Action = action,
            Params = parameters, Queries = queries, Bet = bet
        };
        object r;
        try { r = await _wire.RequestAsync(endpoint, req); }
        catch (OperationCanceledException) { return ErrorCode.InternalError; }
        catch (Exception e) { Debug.LogWarning("[session] " + action + " failed: " + e.Message); return ErrorCode.InternalError; }
        if (r is not GameActionResult res) return ErrorCode.InternalError;
        if (!res.Ok) { World.Predicted = null; return res.Error ?? ErrorCode.UnknownError; }
        if (res.TableState != null) World.Apply(res.TableState, reconcile);
        else if (reconcile || action == GameAction.LeaveTable) World.Apply(new TableState { TableId = 0 });
        return ErrorCode.None;
    }

    public static Task<ErrorCode> PlayNowAsync(List<FlagQuery> queries = null, long? bet = null)
        => TableAsync(Endpoint.TablePlayNow, GameAction.PlayNow, queries: queries, bet: bet, reconcile: true);
    public static Task<ErrorCode> LeaveAsync() => TableAsync(Endpoint.TableLeave, GameAction.LeaveTable, reconcile: true);
    public static Task<ErrorCode> RefreshAsync() => TableAsync(Endpoint.TableAction, GameAction.Refresh, reconcile: true);

    // Hamle: tahmin cagiran tarafindan World.Predict() ile yapilmistir; red -> Resync (tahmin coktu).
    public static async Task<ErrorCode> ActAsync(GameAction action, List<long> parameters = null)
    {
        var e = await TableAsync(Endpoint.TableAction, action, parameters);
        if (e != ErrorCode.None && e != ErrorCode.InsufficientItems && World.InTable) await ResyncAsync();
        return e;
    }

    // TEK KURTARMA: Me + (masadaysam) masa tam gorunum. Ekran bunu World.Reconciled ile gorur -> Sync(instant).
    public static async Task ResyncAsync()
    {
        if (_wire == null || !_wire.IsOpen) return;
        World.Predicted = null;
        try
        {
            var me = await _wire.RequestAsync<PlayerState>(Endpoint.PlayerGetState, null);
            if (me != null) World.Apply(me);
            if (me != null && me.CurrentTableId > 0) await RefreshAsync();
        }
        catch (Exception e) { Debug.LogWarning("[session] resync failed: " + e.Message); }
    }

    // --- baglanti ---
    static void Attach(Wire w)
    {
        _wire = w;
        Generation++;
        w.OnPush = msg => World.Apply(msg);
        w.OnClosed = OnClosed;
    }

    static void OnClosed(int code, string reason)
    {
        Debug.Log("[session] closed " + code + " " + reason);
        _wire = null;
        var cr = (CloseReason)code;
        if (cr is CloseReason.Logout or CloseReason.Unauthorized or CloseReason.AnotherLogin or CloseReason.Kicked or CloseReason.IdleTimeout)
        {
            Current = State.Killed; LastError = cr.ToString();
            return;
        }
        Current = State.Reconnecting;
        if (_connectTask == null) _connectTask = ConnectLoopAsync(fresh: false);
    }

    static async Task ConnectLoopAsync(bool fresh)
    {
        Current = fresh ? State.Connecting : State.Reconnecting;
        try
        {
            float[] delays = { 0f, 1f, 2f };
            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                if (delays[attempt] > 0) await Wait.Seconds(delays[attempt]);
                if (_gateways.Count == 0 || _gatewayIndex >= _gateways.Count)
                {
                    if (!await LoadGatewaysAsync()) { LastError = "settings unreachable"; continue; }
                }
                var err = await TryConnectAsync();
                if (err == null)
                {
                    Current = State.Loading;
                    await LoadAsync();
                    return;
                }
                LastError = err;
                if (Current == State.Killed) return; // auth reddi: deneme anlamsiz
            }
            Current = State.Offline;
            Debug.LogWarning("[session] offline: " + LastError);
        }
        finally { _connectTask = null; }
    }

    static async Task<bool> LoadGatewaysAsync()
    {
        var res = await Http.GetAsync(string.Format(SettingsUrl, ConfigGameId));
        if (!res.Ok) { Debug.LogWarning("[session] settings: " + (res.Error ?? res.Status.ToString())); return false; }
        var node = Json.Parse(res.Text);
        var gws = node?["gateways"];
        _gateways.Clear(); _gatewayIndex = 0;
        if (gws != null && gws.Items != null) foreach (var g in gws.Items) if (!string.IsNullOrEmpty(g.AsString())) _gateways.Add(g.AsString());
        Debug.Log("[session] gateways: " + string.Join(",", _gateways));
        return _gateways.Count > 0;
    }

    // null = bagli ve kimlikli; metin = hata (gateway ulasilamaz -> siradaki).
    static async Task<string> TryConnectAsync()
    {
        string host = _gateways[_gatewayIndex];
        string url = "ws://" + host + "/api/websocket/connect";
        Wire w;
        try { w = await Wire.ConnectAsync(url, CancellationToken.None); }
        catch (Exception e) { _gatewayIndex++; return "connect " + host + ": " + e.Message; }
        Gateway = host;
        Attach(w);
        AuthResult auth;
        try { auth = await w.RequestAsync<AuthResult>(Endpoint.AuthLogin, Auth.Request(GameId, PlayerPrefs.GetString(TokenKey, null))); }
        catch (Exception e) { w.Close(); return "auth: " + e.Message; }
        if (auth == null) { w.Close(); return "auth: empty"; }
        if (!auth.Ok && auth.Error != ErrorCode.AlreadyLoggedIn)
        {
            Current = State.Killed;
            _ = CloseLater(w); // sunucu kapatmazsa biz
            return "auth rejected: " + auth.Error;
        }
        World.AuthId = auth.PlayerId;
        if (!string.IsNullOrEmpty(auth.Token)) PlayerPrefs.SetString(TokenKey, auth.Token);
        DeviceKey.StoreCurrent();
        Debug.Log("[session] authenticated player " + auth.PlayerId + " via " + host);
        return null;
    }

    static async Task CloseLater(Wire w)
    {
        await Wait.Seconds(3f);
        w.Close((int)CloseReason.Unauthorized, "auth failed");
    }

    // Defs ‖ Me, sonra masadaysam Refresh. Hot reload'da da ayni yol (Resync muadili).
    static async Task LoadAsync()
    {
        var w = _wire;
        if (w == null) return;
        try
        {
            var defs = w.RequestAsync(Endpoint.ConfigDefinitions, new ConfigRequest { Key = ConfigKey.Definitions, Version = 0 });
            var me = w.RequestAsync(Endpoint.PlayerGetState, null);
            World.Apply(await defs);
            World.Apply(await me);
            if (World.Me != null && World.Me.CurrentTableId > 0) await RefreshAsync();
            if (_wire != w) return; // yuklerken koptu
            Current = State.Ready;
            Debug.Log("[session] ready: id=" + World.MyId + " nick=" + World.Nick + " table=" + (World.Table?.TableId ?? 0));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[session] load failed: " + e.Message);
            if (_wire == w) w.Close();
        }
    }
}
