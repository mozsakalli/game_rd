using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// CONTAINER ASSET sozlesmesi: icinde birden cok referanslanabilir oge tasiyan asset'in (psd katmanlari,
// font glyph'leri, ileride spine/aseprite parcalari) importer'i bu tabloyu "index" artifact'i olarak
// (ArtifactScope.Editor) yayar. Editor (Project paneli, Inspector, DragDrop, AtlasGroup) YALNIZ bu tabloyu
// okur — dosya formatini bilmez; container olmak = index yaymak. Alt oge anahtari "<assetPath>#<Id>",
// doc'ta "<guid>#<Id>" (AssetDatabase.ToRefKey). Id importer'in KALICI kimligidir (katman id'si, codepoint);
// Name gorunen ad, serbestce degisebilir.
[Serializable, Asset]
public sealed class SubAssetIndex
{
    [Serializable]
    public struct Entry
    {
        public string Id;       // alt oge kimligi ('#' sonrasi); kalici, benzersiz
        public string Name;     // gorunen ad
        public string Type;     // runtime tip adi ("Sprite", "AudioClip"...): alan atanabilirligi bundan
        public int Width, Height;
        // Onizleme/piksel kaynagi (bos = yok): sahibin artifact adi + icindeki dikdortgen (W=0 -> tum artifact).
        // Satir duzeni SpriteTable ile ayni (depolama satirlari). Sdf: mesafe alani -> onizleme esikler.
        public string Pixels;
        public int X, Y, W, H;
        public bool Sdf;
        // Id kaynagin kalici kimliginden mi (katman id'si, codepoint)? false = ad tabanli yedek: yeniden adlandirma
        // referansi kirar; editor sari uyarir.
        public bool Stable;
    }

    public List<Entry> Entries = new();

    // Girisler AtlasGroup'a klasor/container onekiyle TEK TEK uye olabilir mi? Kendi sayfasini butun olarak tasiyan
    // uretici (font: SinglePage, glyph'ler 'sources' ile birlikte gider) false; katman/parça ureticisi (psd) true.
    public bool AtlasMembers;

    public const string ArtifactName = "index";

    public static bool IsIndex(byte[] blob)
        => ObjectBinary.IsBaked(blob) && ObjectBinary.TypeNameOf(blob) == nameof(SubAssetIndex);

    // Sprite ureticileri icin tek satirlik yardimci: tablodaki her bolge bir alt oge ("Sprite").
    public static SubAssetIndex FromSpriteTable(SpriteTable table, string ownerKey)
    {
        var idx = new SubAssetIndex();
        if (table == null)
            return idx;
        string prefix = ownerKey + "#";
        foreach (var r in table.Regions)
        {
            if (r.Page < 0 || r.Page >= table.Pages.Count || !r.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            string id = r.Name.Substring(prefix.Length);
            idx.Entries.Add(new Entry
            {
                Id = id,
                Name = id,
                Type = nameof(Sprite),
                Width = r.OrigW,
                Height = r.OrigH,
                Pixels = table.Pages[r.Page].Artifact,
                X = r.X, Y = r.Y, W = r.W, H = r.H,
                Sdf = r.Kind == RegionKind.Sdf,
                Stable = true,
            });
        }
        return idx;
    }
}
