using System.Collections.Generic;
using M;
using M.GinRummy;

namespace Gin;

// Gin'in TableState YORUMU — saf statik, durumsuz (docs/server-protocol.md 8). Ekran kodu TableState alanlarini
// dogrudan okumaz; her "ne demek" sorusu burada tek yerde cevaplanir.
public static class GinRules
{
    public const int DiscardKey = -1;

    public static List<int> Discards(TableState t)
        => t?.Discards != null && t.Discards.TryGetValue(DiscardKey, out var l) ? l : null;

    public static int TopDiscard(TableState t)
    {
        var d = Discards(t);
        return d != null && d.Count > 0 ? d[d.Count - 1] : -1;
    }

    public static int DeckCount(TableState t) => t?.DeckCount ?? 0;

    public static bool Playing(TableState t) => t != null && t.Phase == TablePhase.Playing;

    // Rakip koltugu: benim disimdaki ilk koltuk (yoksa -1).
    public static int OpponentSeat(TableState t, int mySeat)
    {
        if (t?.Players == null) return -1;
        foreach (var kv in t.Players)
            if (kv.Key != mySeat) return kv.Key;
        return -1;
    }

    public static int Deadwood(List<int> hand)
    {
        if (hand == null || hand.Count == 0) return 0;
        return HandScore.Analyze(hand).DeadwoodScore;
    }

    // Izinler (veriden turer)
    public static bool CanPick(TableState t, int mySeat, List<int> hand)
        => Playing(t) && t.TurnOwner == mySeat && mySeat >= 0 && hand != null && hand.Count == 10;
    public static bool CanPickFromDiscard(TableState t, int mySeat, List<int> hand)
        => CanPick(t, mySeat, hand) && TopDiscard(t) >= 0;
    public static bool CanPickFromDeck(TableState t, int mySeat, List<int> hand)
        => CanPick(t, mySeat, hand) && t.SubPhase != TableSubPhase.Pass && (t.DeckLastNo ?? -1) >= 0;
    public static bool CanDiscard(TableState t, int mySeat, List<int> hand)
        => Playing(t) && t.TurnOwner == mySeat && mySeat >= 0 && hand != null && hand.Count == 11;

    // Kisa sayi: 1200 -> 1.2K, 2 000 000 000 -> 2B
    public static string Short(long v)
    {
        if (v < 1000) return v.ToString();
        if (v < 1_000_000) return Trim(v / 1000.0) + "K";
        if (v < 1_000_000_000) return Trim(v / 1_000_000.0) + "M";
        return Trim(v / 1_000_000_000.0) + "B";
    }

    static string Trim(double d) => d >= 100 ? ((long)d).ToString() : d.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
