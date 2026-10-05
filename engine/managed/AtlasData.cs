using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigitoyEngine;

// Bolge turu: cizim modu + paketleme politikasi. Color = duz RGBA (sprite), Sdf =
// tek kanal mesafe alani (glyph, UI parcasi). Sayfa formati uyelerden turer:
// hepsi Sdf -> R8, karisik -> RGBA8 (SDF degeri dort kanala kopyalanir; shader .r okur).
public enum RegionKind : byte { Color = 0, Sdf = 1 }

// Atlas sayfasindaki bir bolge. Anahtar = "<assetPath>" (png) veya
// "<assetPath>#<alt>" (glyph "U+0041", spine parcasi vb.). Koordinatlar sayfa
// DEPOLAMA satirlari cinsinden (satir 0 = ilk yuklenen satir); her tuketici kendi
// V yonelimini korur (sprite bottom-up, glyph/parca top-down) — kopya satir-satir.
public struct AtlasRegion
{
    public string Name;
    public int Page;
    public int X, Y, W, H;
    public int OffX, OffY, OrigW, OrigH; // trim ofseti + mantiksal boyut
    public RegionKind Kind;
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

// DATL: atlas tanim artifact'i ("main"). Sayfa pikselleri ayri artifact'lerdir
// ("page0", "page1", ... DPIX) — tanim kucuk kalir, sayfalar bagimsiz yuklenir.
// [magic][version][channels][pageCount]{w,h,piecesX,piecesY}*[regionCount]{region}*
public sealed class AtlasData
{
    public const int Magic = 0x4C544144; // "DATL"
    public const int Version = 1;

    public struct PageInfo
    {
        public int Width, Height;
        public int PiecesX, PiecesY; // UiPieces bolge orijini (-1 = yok)
    }

    public int Channels = 1;
    public readonly List<PageInfo> Pages = new();
    public readonly List<AtlasRegion> Regions = new();

    public static string PageArtifact(int index) => "page" + index;

    // Glyph bolge adi: "U+0041" (asset anahtarina '#' ile eklenir).
    public static string GlyphName(int codepoint) => "U+" + codepoint.ToString("X4");

#if DE_EDITOR // uretici (AtlasImporter); runtime yalniz Parse okur
    public byte[] Write()
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(Channels);
        w.Write(Pages.Count);
        foreach (var p in Pages)
        {
            w.Write(p.Width);
            w.Write(p.Height);
            w.Write(p.PiecesX);
            w.Write(p.PiecesY);
        }
        w.Write(Regions.Count);
        foreach (var r in Regions)
        {
            w.Write(r.Name ?? "");
            w.Write(r.Page);
            w.Write(r.X); w.Write(r.Y); w.Write(r.W); w.Write(r.H);
            w.Write(r.OffX); w.Write(r.OffY); w.Write(r.OrigW); w.Write(r.OrigH);
            w.Write((byte)r.Kind);
        }
        w.Flush();
        return ms.ToArray();
    }
#endif

    public static bool IsAtlas(byte[] blob)
        => blob != null && blob.Length >= 8 && BitConverter.ToInt32(blob, 0) == Magic;

    // Bagimliliksiz imlec (BinaryReader/MemoryStream yok: AOT corelib yuzeyi dar kalsin).
    // Bicim BinaryWriter ile birebir: int32 LE; string = 7-bit uzunluk + UTF-8.
    public static AtlasData Parse(byte[] blob)
    {
        if (blob == null || blob.Length < 16)
            return null;
        int pos = 0;
        if (I32(blob, ref pos) != Magic || I32(blob, ref pos) != Version)
            return null;
        var a = new AtlasData { Channels = I32(blob, ref pos) };
        int pages = I32(blob, ref pos);
        if (pages < 0 || pos + pages * 16 > blob.Length)
            return null;
        for (int i = 0; i < pages; i++)
            a.Pages.Add(new PageInfo
            {
                Width = I32(blob, ref pos),
                Height = I32(blob, ref pos),
                PiecesX = I32(blob, ref pos),
                PiecesY = I32(blob, ref pos),
            });
        int n = I32(blob, ref pos);
        if (n < 0)
            return null;
        for (int i = 0; i < n; i++)
        {
            string name = Str(blob, ref pos);
            if (name == null || pos + 37 > blob.Length)
                return null;
            a.Regions.Add(new AtlasRegion
            {
                Name = name,
                Page = I32(blob, ref pos),
                X = I32(blob, ref pos), Y = I32(blob, ref pos), W = I32(blob, ref pos), H = I32(blob, ref pos),
                OffX = I32(blob, ref pos), OffY = I32(blob, ref pos), OrigW = I32(blob, ref pos), OrigH = I32(blob, ref pos),
                Kind = (RegionKind)blob[pos++],
            });
        }
        return a;
    }

    static int I32(byte[] b, ref int p)
    {
        int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
        p += 4;
        return v;
    }

    // BinaryWriter.Write(string): 7-bit kodlu bayt uzunlugu + UTF-8. Bozuk/tasan -> null.
    static string Str(byte[] b, ref int p)
    {
        int len = 0, shift = 0;
        while (true)
        {
            if (p >= b.Length || shift > 28) return null;
            byte c = b[p++];
            len |= (c & 0x7F) << shift;
            if ((c & 0x80) == 0) break;
            shift += 7;
        }
        if (len < 0 || p + len > b.Length) return null;
        string s = Encoding.UTF8.GetString(b, p, len);
        p += len;
        return s;
    }
}