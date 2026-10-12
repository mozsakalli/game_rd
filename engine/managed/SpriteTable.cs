using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Bolge turu: cizim modu + paketleme politikasi. Color = duz RGBA (sprite), Sdf =
// tek kanal mesafe alani (glyph, UI parcasi). Sayfa formati uyelerden turer:
// hepsi Sdf -> R8, karisik -> RGBA8 (SDF degeri dort kanala kopyalanir; shader .r okur).
public enum RegionKind : byte { Color = 0, Sdf = 1 }

// TEK sprite tablosu sozlesmesi: her gorsel ureticisi importer (png grubu, font, spine...) "hangi anahtar
// hangi dokunun neresinde" bilgisini bu tabloyla bildirir; AtlasGroup uyelerin tablolarini okuyup kendi
// sayfalarina yeniden paketler ve KENDI tablosunu yazar. Runtime (AssetDatabase) tablolari birlestirir:
// LoadSprite(key) bir kez cozer, Sprite yerinde baglanir, frame'de lookup yok.
// Serilesen [Asset] sinif -> artifact ObjectBinary (DOBJ) olarak yasar: atlas "main", uretici "sprites".
// Anahtar = "<assetPath>" (png) ya da "<assetPath>#<alt>" (glyph "U+0041", spine parcasi).
// Koordinatlar sayfa DEPOLAMA satirlari cinsinden (satir 0 = ilk yuklenen satir).
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

    public int Channels = 1;        // 1 = R8, 4 = RGBA8
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

// DPIX: ham piksel blobu — [magic][channels 1|4][w][h][w*h*channels bayt].
// Atlas sayfalari ve font glyph sheet'i bu bicimde artifact olur; pak zlib'ler.
public static class PixelBlob
{
    public const int Magic = 0x58495044; // "DPIX"
    public const int HeaderSize = 16;

#if DE_EDITOR // uretici (importer); runtime yalniz TryParse okur
    public static byte[] Build(int channels, int width, int height, ReadOnlySpan<byte> pixels)
    {
        var b = new byte[HeaderSize + pixels.Length];
        BitConverter.TryWriteBytes(b.AsSpan(0), Magic);
        BitConverter.TryWriteBytes(b.AsSpan(4), channels);
        BitConverter.TryWriteBytes(b.AsSpan(8), width);
        BitConverter.TryWriteBytes(b.AsSpan(12), height);
        pixels.CopyTo(b.AsSpan(HeaderSize));
        return b;
    }
#endif

    public static bool TryParse(byte[] blob, out int channels, out int width, out int height)
    {
        channels = width = height = 0;
        if (blob == null || blob.Length < HeaderSize || BitConverter.ToInt32(blob, 0) != Magic)
            return false;
        channels = BitConverter.ToInt32(blob, 4);
        width = BitConverter.ToInt32(blob, 8);
        height = BitConverter.ToInt32(blob, 12);
        return (channels == 1 || channels == 4) && width > 0 && height > 0
            && blob.Length == HeaderSize + width * height * channels;
    }
}
