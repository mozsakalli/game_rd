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

    public static bool IsAtlas(byte[] blob)
        => blob != null && blob.Length >= 8 && BitConverter.ToInt32(blob, 0) == Magic;

    public static AtlasData Parse(byte[] blob)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(blob), Encoding.UTF8);
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
                return null;
            var a = new AtlasData { Channels = r.ReadInt32() };
            int pages = r.ReadInt32();
            for (int i = 0; i < pages; i++)
                a.Pages.Add(new PageInfo
                {
                    Width = r.ReadInt32(),
                    Height = r.ReadInt32(),
                    PiecesX = r.ReadInt32(),
                    PiecesY = r.ReadInt32(),
                });
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                a.Regions.Add(new AtlasRegion
                {
                    Name = r.ReadString(),
                    Page = r.ReadInt32(),
                    X = r.ReadInt32(), Y = r.ReadInt32(), W = r.ReadInt32(), H = r.ReadInt32(),
                    OffX = r.ReadInt32(), OffY = r.ReadInt32(), OrigW = r.ReadInt32(), OrigH = r.ReadInt32(),
                    Kind = (RegionKind)r.ReadByte(),
                });
            }
            return a;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
