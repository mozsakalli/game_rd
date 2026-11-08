using System;
using System.Collections.Generic;
using DigitoyEngine;
using M;

namespace Net;

// Sunucudaki butun veri modelinin YEREL KOPYASI (docs/server-protocol.md "World"). Tek gercek sunucudadir; buradaki
// alanlar yalniz Apply ile (sunucu paketi) ya da Predict ile (gecici tahmin) degisir. Oyun yorumu burada degil:
// TableState her oyunda aynidir, Gin anlami GinRules'ta. Olay/callback yok — ekran her degisimde Sync(instant) yapar;
// hangi parcanin degistigini Version/TableVersion/MeVersion ile anlar.
public static class World
{
    public static long AuthId;            // AuthResult.PlayerId (Me gelmeden once de kimligim)
    public static PlayerState Me;         // her gelisте KOMPLE degisir
    public static EconomyDefinitions Defs;
    public static TableState Table;       // null = masada degilim
    public static TableState Predicted;   // optimistic tahmin (Table'in kopyasi); sunucudan herhangi bir paket gelince null
    public static bool Kicked;            // son masa dususunun sebebi (bir sonraki Apply'da sifirlanir)

    public static int Version;            // herhangi bir parca degisti
    public static int MeVersion;
    public static int TableVersion;
    public static bool Reconciled;        // son Table yazimi tam gorunumdu (reconnect/Refresh/masaya giris) -> instant

    static long _skew; // sunucu saati - yerel saat (ms)

    // --- turevler ---
    public static long MyId => Me != null ? Me.PlayerId : AuthId;
    public static string Nick => Me?.Nick ?? "";
    public static bool InTable => Table != null && Table.TableId > 0;
    public static TableState Live => Predicted ?? Table; // ekranin gosterecegi masa (tahmin varsa o)
    public static long ServerNow => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + _skew;

    public static long Amount(string itemId)
        => Me?.Inventory != null && Me.Inventory.TryGetValue(itemId, out var it) ? it.Amount : 0;

    // Oturdugum koltuk (Players anahtari); -1 = masada degilim / izleyiciyim.
    public static int MySeat => SeatOf(Live, MyId);
    public static SeatPreview MySeatState => Seat(MySeat);
    public static bool IsMyTurn => Live != null && Live.TurnOwner.HasValue && Live.TurnOwner.Value == MySeat && MySeat >= 0;

    public static SeatPreview Seat(int pos)
        => pos >= 0 && Live?.Players != null && Live.Players.TryGetValue(pos, out var s) ? s : null;

    public static int SeatOf(TableState t, long playerId)
    {
        if (t?.Players == null || playerId <= 0) return -1;
        foreach (var kv in t.Players)
            if (kv.Value.PlayerId.HasValue && kv.Value.PlayerId.Value == playerId) return kv.Key;
        return -1;
    }

    // Zaman asimina kalan saniye (TimeoutAt'a gore); yoksa -1.
    public static float TurnSecondsLeft
    {
        get
        {
            var at = Live?.TimeoutAt;
            if (!at.HasValue || at.Value <= 0) return -1f;
            return Math.Max(0, at.Value - ServerNow) / 1000f;
        }
    }

    // --- uygulama: sunucudan gelen her govde (istek cevabi ya da push) ---
    public static void Apply(object msg, bool reconcile = false)
    {
        if (msg == null) return;
        Predicted = null; // sunucu konustu: tahmin biter (dogruysa zaten ayni, yanlissa sunucu kazanir)
        switch (msg)
        {
            case TableState t: ApplyTable(t, reconcile); break;
            case GameActionResult r: if (r.TableState != null) ApplyTable(r.TableState, reconcile); break;
            case PlayerState p: ApplyMe(p); break;
            case EventExecutionResult e: ApplyEventResult(e); break;
            case ConfigResult c: if (c.Definitions != null) { Defs = c.Definitions; Version++; } break;
            default: return;
        }
    }

    public static void Reset()
    {
        AuthId = 0; Me = null; Defs = null; Table = null; Predicted = null; Kicked = false; Reconciled = false;
        _skew = 0; Version++; MeVersion++; TableVersion++;
    }

    // Tahmin: Table'in kopyasini al, cagiran uzerinde degisikligi yapar; sunucu paketi gelince dusur.
    public static TableState Predict()
    {
        if (Table == null) return null;
        Predicted = TableExtensions.Clone(Table, null);
        TableVersion++; Version++;
        return Predicted;
    }

    static void ApplyMe(PlayerState p)
    {
        Me = p;
        if (p.ServerTime > 0) _skew = p.ServerTime - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        MeVersion++; Version++;
        // sunucu "masada degilsin" diyorsa eski kopya duser
        if (p.CurrentTableId <= 0 && Table != null) ClearTable(false);
    }

    static void ApplyEventResult(EventExecutionResult r)
    {
        if (r == null || Me == null || !r.Ok) return;
        if (r.InventoryUpdates != null)
        {
            Me.Inventory ??= new Dictionary<string, InventoryItem>();
            foreach (var kv in r.InventoryUpdates)
            {
                if (!Me.Inventory.TryGetValue(kv.Key, out var item)) Me.Inventory[kv.Key] = item = new InventoryItem();
                var u = kv.Value;
                if (u.Amount.HasValue) item.Amount += u.Amount.Value;
                if (u.Rate.HasValue) item.Rate = u.Rate.Value;
                if (u.Time.HasValue) item.Time = u.Time.Value;
                if (u.ResetTime.HasValue) item.ResetTime = u.ResetTime.Value;
            }
        }
        if (r.EventStateUpdates != null)
        {
            Me.EventStates ??= new Dictionary<string, EventState>();
            foreach (var kv in r.EventStateUpdates) Me.EventStates[kv.Key] = kv.Value;
        }
        if (r.LeaderboardUpdates != null)
        {
            Me.LeaderboardStates ??= new Dictionary<string, LeaderboardState>();
            foreach (var kv in r.LeaderboardUpdates) Me.LeaderboardStates[kv.Key] = kv.Value;
        }
        MeVersion++; Version++;
    }

    // Masa merge kurallari (docs/server-protocol.md "TableState merge").
    static void ApplyTable(TableState inc, bool reconcile)
    {
        Kicked = false;
        if (inc.ServerTime.HasValue && inc.ServerTime.Value > 0)
            _skew = inc.ServerTime.Value - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (inc.TableId.HasValue && inc.TableId.Value <= 0) { ClearTable(inc.Action == GameAction.PlayerKicked); return; }

        var prev = Table;
        bool had = prev != null && prev.TableId > 0;
        bool full = !had || reconcile || (inc.TableId.HasValue && prev.TableId != inc.TableId);
        long myId = MyId;

        if (!full && inc.Players != null)
            foreach (var s in inc.Players.Values)
                if (s.PlayerId.HasValue && s.PlayerId.Value == myId && s.Hand != null) { full = true; break; } // elim geldi = tam gorunum

        if (full)
        {
            if (had && !inc.TableId.HasValue) inc.TableId = prev.TableId;
            Table = inc;
            bool meGone = false, meKicked = false;
            if (inc.Players != null) RemoveLeft(inc.Players, myId, out meGone, out meKicked);
            Reconciled = true;
            if (meGone) { ClearTable(meKicked); return; }
        }
        else
        {
            var s = prev;
            if (inc.GameId.HasValue) s.GameId = inc.GameId;
            if (inc.TurnNo.HasValue) s.TurnNo = inc.TurnNo;
            if (inc.TurnOwner.HasValue) s.TurnOwner = inc.TurnOwner;
            if (inc.Phase != TablePhase.None) s.Phase = inc.Phase;
            if (inc.SubPhase != TableSubPhase.None) s.SubPhase = inc.SubPhase;
            if (inc.ServerTime.HasValue) s.ServerTime = inc.ServerTime;
            if (inc.TimeoutAt.HasValue) s.TimeoutAt = inc.TimeoutAt;
            if (inc.GameEndsAt.HasValue) s.GameEndsAt = inc.GameEndsAt;
            if (inc.Deck != null) s.Deck = inc.Deck;
            if (inc.DeckCount.HasValue) s.DeckCount = inc.DeckCount;
            if (inc.DeckLastNo.HasValue) s.DeckLastNo = inc.DeckLastNo;
            if (inc.Discards != null) { s.Discards ??= new Dictionary<int, List<int>>(); foreach (var kv in inc.Discards) s.Discards[kv.Key] = kv.Value; }
            if (inc.Flags.HasValue) s.Flags = inc.Flags;
            if (inc.Bet.HasValue) s.Bet = inc.Bet;
            if (inc.Jackpot.HasValue) s.Jackpot = inc.Jackpot;
            if (inc.Action.HasValue) s.Action = inc.Action;
            if (inc.PluginState != null) s.PluginState = inc.PluginState;
            if (inc.Spectators != null) MergeSeats(s.Spectators ??= new Dictionary<int, SeatPreview>(), inc.Spectators);
            if (inc.Players != null)
            {
                int mySeat = SeatOf(s, myId);
                MergeSeats(s.Players ??= new Dictionary<int, SeatPreview>(), inc.Players);
                // oturdugum koltuk "ayrildi/atildi" ile dustu -> masadan dustum
                if (mySeat >= 0 && inc.Players.TryGetValue(mySeat, out var gone)
                    && (gone.LastMove == GameAction.PlayerLeft || gone.LastMove == GameAction.PlayerKicked))
                {
                    ClearTable(gone.LastMove == GameAction.PlayerKicked);
                    return;
                }
            }
            Reconciled = false;
        }

        // koltuktaki ekonomi sonucu (masa odulu) oyuncu modeline
        var me = Seat(SeatOf(Table, myId));
        if (me?.EventResult != null) { var r = me.EventResult; me.EventResult = null; ApplyEventResult(r); }

        TableVersion++; Version++;
    }

    static void RemoveLeft(Dictionary<int, SeatPreview> players, long myId, out bool meGone, out bool meKicked)
    {
        meGone = false; meKicked = false;
        List<int> gone = null;
        foreach (var kv in players)
        {
            var mv = kv.Value.LastMove;
            if (mv != GameAction.PlayerLeft && mv != GameAction.PlayerKicked) continue;
            (gone ??= new List<int>()).Add(kv.Key);
            if (kv.Value.PlayerId.HasValue && kv.Value.PlayerId.Value == myId) { meGone = true; meKicked = mv == GameAction.PlayerKicked; }
        }
        if (gone != null) foreach (int k in gone) players.Remove(k);
    }

    static void MergeSeats(Dictionary<int, SeatPreview> cached, Dictionary<int, SeatPreview> incoming)
    {
        foreach (var kv in incoming)
        {
            int pos = kv.Key; var inc = kv.Value;
            if (inc.LastMove == GameAction.PlayerLeft || inc.LastMove == GameAction.PlayerKicked) { cached.Remove(pos); continue; }
            if (!cached.TryGetValue(pos, out var c) || inc.LastMove == GameAction.PlayerJoined
                || (inc.PlayerId.HasValue && c.PlayerId.HasValue && inc.PlayerId != c.PlayerId))
            {
                cached[pos] = inc;
                continue;
            }
            if (inc.PlayerId.HasValue) c.PlayerId = inc.PlayerId;
            if (inc.FacebookId.HasValue) c.FacebookId = inc.FacebookId;
            if (!string.IsNullOrEmpty(inc.Nick)) c.Nick = inc.Nick;
            if (inc.Hand != null) c.Hand = inc.Hand;
            if (inc.Balance.HasValue) c.Balance = inc.Balance;
            if (inc.Stats != null && inc.Stats.Count > 0) { c.Stats ??= new Dictionary<string, long>(); foreach (var s in inc.Stats) c.Stats[s.Key] = s.Value; }
            if (inc.LastMove.HasValue) c.LastMove = inc.LastMove;
            if (inc.Score.HasValue) c.Score = inc.Score;
            if (inc.Position != 0) c.Position = inc.Position;
            if (inc.EventResult != null) c.EventResult = inc.EventResult;
            if (!string.IsNullOrEmpty(inc.Message)) c.Message = inc.Message;
        }
    }

    static void ClearTable(bool kicked)
    {
        Table = null; Predicted = null; Kicked = kicked; Reconciled = true;
        TableVersion++; Version++;
    }
}
