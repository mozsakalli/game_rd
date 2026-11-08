using DigitoyEngine;

// Gin masa ekrani â€” KOD SAHNESI (PlayerSettings.startScene = "TableScene"). Sahne dosyasi yok; agac Start'ta kurulur.
// Gorsel kararlar `protected virtual` uyelerdir (ACIK NOKTA): tema = turetilmis sinif override eder.
public class TableScene : Component
{
    // --- acik noktalar ---
    protected virtual float OpponentTop => 140;      // rakip avatarinin ust kenardan uzakligi (mantiksal nokta)
    protected virtual float AvatarSize => 93;
    protected virtual Color FeltColor => new Color(0x0E, 0x32, 0x98);
    protected virtual Color AvatarRing => new Color(0xFF, 0xB8, 0x07, 0x49);
    protected virtual Color PlateColor => new Color(0x6A, 0x3D, 0xB8);

    protected override void Start()
    {
        Net.Session.Start(GinGameId);
        _ = WatchAsync();

        var root = Box("Background", transform);
        root.FitScreen = true;
        root.Fill = FeltColor;

        var opp = Box("Opponent", root.transform);
        opp.Width = AvatarSize; opp.Height = AvatarSize;
        opp.AnchorMin = new Vec2(0.5f, 0f); opp.AnchorMax = new Vec2(0.5f, 0f);
        opp.Pivot = new Vec2(0.5f, 0f);
        opp.AnchoredPos = new Vec2(0, OpponentTop);
        opp.CornerRadius = AvatarSize / 2;
        opp.Fill = PlateColor;
        opp.BorderWidth = 4; opp.BorderFill = AvatarRing;
    }

    static LayoutBox Box(string name, Transform parent)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.AddComponent<LayoutBox>();
    }

    static string Inv()
    {
        var inv = Net.World.Me?.Inventory;
        if (inv == null) return "-";
        var sb = new System.Text.StringBuilder();
        foreach (var kv in inv) sb.Append(kv.Key).Append(':').Append(kv.Value.Amount).Append(' ');
        return sb.ToString();
    }

    const int GinGameId = 7;

    // Sahne yikilirken (hot reload / Stop) kimlikli soket motora emanet; sonraki Start geri alir.
    protected override void OnDestroy() => Net.Session.Park();

    // Adim B dogrulamasi: oturum hazir olunca World ozetini logla; masada degilsem PlayNow ile masaya otur.
    // (Adim C'de yerini World.Version takipli Sync alacak.)
    async System.Threading.Tasks.Task WatchAsync()
    {
        var ct = destroyCancellationToken;
        int seenVersion = -1;
        bool playNowSent = false;
        while (!ct.IsCancellationRequested)
        {
            await Frame.Next(ct);
            if (Net.World.Version == seenVersion) continue;
            seenVersion = Net.World.Version;
            var t = Net.World.Table;
            Debug.Log("[table] v" + seenVersion + " session=" + Net.Session.Current + " me=" + Net.World.MyId + " '" + Net.World.Nick
                + "' inv=" + Inv() + " table=" + (t?.TableId ?? 0) + " phase=" + (t?.Phase.ToString() ?? "-")
                + " seat=" + Net.World.MySeat + " seats=" + (t?.Players?.Count ?? 0) + " turn=" + (t?.TurnOwner?.ToString() ?? "-")
                + (Net.World.Reconciled ? " [full]" : " [delta]"));
            if (Net.Session.IsReady && !Net.World.InTable && !playNowSent)
            {
                playNowSent = true;
                var e = await Net.Session.PlayNowAsync();
                Debug.Log("[table] PlayNow -> " + e);
            }
        }
    }
}
