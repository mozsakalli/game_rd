using System;
using System.Collections.Generic;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

[Serializable]
public sealed class PsdImportSettings
{
    public bool importHidden;          // gizli katmanlar da sprite olsun
    public bool groupPathNames;        // ad "Grup/Katman" (false: yalin katman adi)
    public bool trim = true;           // seffaf kenarlar sayfaya girmez (mantiksal boyut korunur)
    public int padding = 2;            // sayfada bolge cevresi bos piksel
    public int maxSize = 2048;         // kendi sayfasi icin ust sinir (asilinca yeni sayfa)
}

// PSD/PSB -> katman basina Sprite. UNITY'NIN MOZAIK/KARAKTER MODU YOK: dosya bir sprite KAYNAGIDIR; hangi katmanin
// hangi atlasa girecegine AtlasGroup (klasor/container onegi ya da 'sprites' listesi) karar verir. Burada uretilen
// sayfa yalniz YEDEKTIR (Standalone: atlas'a girmeyen katmanlar buradan cizilir; tam kapsamda pak'a girmez).
// Ciktilar: main = LayerSheet (dokuman duzeni), sprites = SpriteTable + pageN, index = SubAssetIndex (AtlasMembers).
// Kimlik: PSD 'lyid' (kalici; katman yeniden adlandirilsa referans kirilmaz); yoksa ad tabanli + Stable=false.
[AssetImporter(".psd", ".psb", Version = 1, Settings = typeof(PsdImportSettings))]
public sealed class PsdImporter : AssetImporter
{
    public override void Import(ImportContext ctx)
    {
        var settings = ctx.Settings as PsdImportSettings ?? new PsdImportSettings();
        PsdReader psd;
        try { psd = PsdReader.Read(ctx.SourcePath); }
        catch (Exception e)
        {
            ctx.Fail("psd okunamadi: " + e.Message);
            return;
        }

        var sources = new List<RegionSource>();
        var layers = new List<LayerSheet.Layer>();
        var stable = new Dictionary<string, bool>(StringComparer.Ordinal);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        int skippedHidden = 0, unstable = 0, skippedDead = 0;
        // Kayitlar alttan uste; kullanici sirasi (Photoshop paneli) ustten alta -> ters gez.
        for (int i = psd.Layers.Count - 1; i >= 0; i--)
        {
            var L = psd.Layers[i];
            if (L.Rgba == null)
                continue; // grup/ayarlama/bos katman
            if (!L.Visible && !settings.importHidden) { skippedHidden++; continue; }
            string name = settings.groupPathNames && L.GroupPath.Length > 0 ? L.GroupPath + "/" + L.Name : L.Name;
            string id;
            bool isStable = L.Id >= 0;
            if (isStable)
                id = L.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                id = "n:" + SanitizeId(name);
                unstable++;
            }
            if (!usedIds.Add(id))
            {
                // Ayni id iki kez (bozuk dosya / ad cakismasi): sonek — bu referans kararsizdir.
                int k = 2;
                while (!usedIds.Add(id + "~" + k)) k++;
                id = id + "~" + k;
                isStable = false;
            }
            // Satir 0 ustte -> motor duzeni (satir 0 altta).
            int w = L.W, h = L.H;
            var flipped = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                Buffer.BlockCopy(L.Rgba, y * w * 4, flipped, (h - 1 - y) * w * 4, w * 4);
            string key = ctx.AssetPath + "#" + id;
            // Pak build'i: yalniz CANLI (sahneden erisilen, atlas'in kapsamadigi) katman kendi sayfasina girer;
            // editor import'unda hepsi. Build sistemi ciktiyi dogrular (canli olmayan anahtar = hata).
            bool live = ctx.IsLive(key);
            if (!live) { skippedDead++; continue; }
            var s = RegionSource.FromBuffer(key, RegionKind.Color, flipped, 4, w, 0, 0, w, h);
            s.Pad = Math.Max(0, settings.padding);
            sources.Add(s);
            stable[key] = isStable;
            layers.Add(new LayerSheet.Layer
            {
                Id = id, Name = name, X = L.Left, Y = L.Top, W = w, H = h, Visible = L.Visible,
                Sprite = Sprite.FromTexture(null, key), // serilesirken yalniz anahtar; runtime tablodan baglar
            });
        }
        if (sources.Count == 0)
        {
            if (skippedDead == 0)
            {
                ctx.Fail("piksel katmani yok (hepsi gizli/bos?)");
                return;
            }
            // Pak: hicbir katman canli degil (ya da hepsi atlas'ta) -> yalniz dokuman duzeni; sayfa/sprites yok.
            ctx.AddArtifact("main", new LayerSheet { Width = psd.Width, Height = psd.Height });
            return;
        }

        if (settings.trim)
            RegionPacker.TrimTransparent(sources);
        List<PackedPage> pages;
        try
        {
            pages = RegionPacker.Pack(sources, Math.Max(256, settings.maxSize), 4, uiPieces: false, out string perr);
            if (perr != null)
                EditorLog.Warning($"[psd] {ctx.AssetPath}: {perr}");
        }
        catch (Exception e)
        {
            ctx.Fail(e.Message);
            return;
        }

        var table = new SpriteTable();
        for (int p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            table.Pages.Add(new SpriteTable.Page { Artifact = SpriteTable.PageArtifact(p), Width = page.Width, Height = page.Height, PiecesX = -1, PiecesY = -1 });
            foreach (var (src, x, y) in page.Placed)
                table.Regions.Add(new SpriteTable.Region
                {
                    Name = src.Name, Page = p, X = x, Y = y, W = src.W, H = src.H,
                    OffX = src.OffX, OffY = src.OffY, OrigW = src.OrigW, OrigH = src.OrigH,
                    Kind = RegionKind.Color, Pad = src.Pad,
                });
            ctx.AddArtifact(SpriteTable.PageArtifact(p), page.EncodePng(), ArtifactScope.Standalone);
        }

        // Index: kullanici sirasi (layers listesi), gorunen ad, kararlilik.
        var index = SubAssetIndex.FromSpriteTable(table, ctx.AssetPath);
        index.AtlasMembers = true;
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < index.Entries.Count; i++)
            byId[index.Entries[i].Id] = i;
        var ordered = new List<SubAssetIndex.Entry>(index.Entries.Count);
        foreach (var l in layers)
        {
            if (!byId.TryGetValue(l.Id, out int ei))
                continue;
            var e = index.Entries[ei];
            e.Name = l.Name;
            e.Stable = stable.TryGetValue(ctx.AssetPath + "#" + l.Id, out bool st) && st;
            ordered.Add(e);
        }
        index.Entries = ordered;

        var sheet = new LayerSheet { Width = psd.Width, Height = psd.Height, Layers = layers.ToArray() };
        ctx.AddArtifact("main", sheet);
        ctx.AddArtifact("sprites", table, ArtifactScope.Standalone);
        ctx.AddArtifact(SubAssetIndex.ArtifactName, index, ArtifactScope.Editor);

        var report = new StringBuilder();
        report.Append("layers=").Append(layers.Count).Append('\n');
        report.Append("doc=").Append(psd.Width).Append('x').Append(psd.Height).Append(psd.IsPsb ? " (psb)" : " (psd)").Append('\n');
        report.Append("pages=").Append(pages.Count).Append('\n');
        report.Append("hiddenSkipped=").Append(skippedHidden).Append('\n');
        report.Append("unstableIds=").Append(unstable).Append('\n');
        ctx.AddArtifact("report.txt", Encoding.UTF8.GetBytes(report.ToString()), ArtifactScope.Editor);
        if (unstable > 0)
            EditorLog.Warning($"[psd] {ctx.AssetPath}: {unstable} katmanda 'lyid' yok — ad tabanli id (yeniden adlandirma referansi kirar)");
    }

    static string SanitizeId(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (char c in name)
            sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '/' ? c : '_');
        return sb.ToString();
    }

    // --- Inspector onizleme: kisa rapor (kucuk resimler Project panelinde / alt oge basliginda) ---
    const float RowH = 20f;
    string _pvRel, _pvText;
    int _pvVersion = -1;

    void Bind(string rel)
    {
        if (_pvRel == rel && _pvVersion == AssetWatcher.AssetChangeVersion)
            return;
        _pvRel = rel;
        _pvVersion = AssetWatcher.AssetChangeVersion;
        var bytes = ImportPipeline.GetArtifact(rel, "report.txt");
        _pvText = bytes != null ? Encoding.UTF8.GetString(bytes).Replace('\n', ' ').Replace("=", ": ").Trim() : "(not imported)";
    }

    public override float PreviewHeight(string assetPath) => RowH + 4;

    public override void DrawPreview(string assetPath, in Rect rect)
    {
        Bind(assetPath);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(rect.x, rect.y, rect.width, RowH), _pvText, Gui.FontSize - 1, new Color(160, 168, 182, 255));
    }

    public override void HidePreview() => _pvRel = null;
}
