using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Bolge turu (paketleyici etiketi): Color = duz RGBA sprite, Sdf = mesafe alani (glyph, UI parcasi).
// Sayfa formati hep RGBA8; SDF degeri dort kanala kopyalanir (median == a).
public enum RegionKind : byte { Color = 0, Sdf = 1 }

// TEK sprite tablosu sozlesmesi: her gorsel ureticisi importer (png grubu, font, spine...) "hangi anahtar
// hangi dokunun neresinde" bilgisini bu tabloyla bildirir; AtlasGroup uyelerin tablolarini okuyup kendi
// sayfalarina yeniden paketler ve KENDI tablosunu yazar. Runtime (AssetDatabase) tablolari birlestirir:
// LoadSprite(key) bir kez cozer, Sprite yerinde baglanir, frame'de lookup yok.
// Serilesen [Asset] sinif -> artifact ObjectBinary (DOBJ) olarak yasar: atlas "main", uretici "sprites".
// Anahtar = "<assetPath>" (png) ya da "<assetPath>#<alt>" (glyph "U+0041", spine parcasi).
// Koordinatlar sayfa DEPOLAMA satirlari cinsinden (satir 0 = ilk yuklenen satir). Sayfalar PNG (RGBA8),
// flip'li yazilir: stb_image yukleme flip'i bellek duzenini birebir geri getirir.
[Serializable, Asset]
public sealed class SpriteTable
{
    [Serializable]
    public struct Page
    {
        public string Artifact;     // sahibin piksel artifact adi ("page0", "sheet"); doku anahtari = sahip + "#" + Artifact
        public int Width, Height;
        public int PiecesX, PiecesY; // UiPieces bolge orijini (-1 = yok)
    }

    [Serializable]
    public struct Region
    {
        public string Name;
        public int Page;
        public int X, Y, W, H;
        public int OffX, OffY, OrigW, OrigH; // trim ofseti + mantiksal boyut
        public RegionKind Kind;
        public int Pad;                      // paketleme girdisi (yeniden paketlenirken korunur)
    }

    public bool SinglePage;         // yeniden paketlenirken tum bolgeler ayni sayfada kalmali (font)
    public List<Page> Pages = new();
    public List<Region> Regions = new();

    public static string PageArtifact(int index) => "page" + index;

    // Glyph bolge adi: "U+0041" (asset anahtarina '#' ile eklenir).
    public static string GlyphName(int codepoint) => "U+" + codepoint.ToString("X4");

    // Bu baytlar bir sprite tablosu mu (DOBJ + tip adi).
    public static bool IsTable(byte[] blob)
        => ObjectBinary.IsBaked(blob) && ObjectBinary.TypeNameOf(blob) == nameof(SpriteTable);
}
