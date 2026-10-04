using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// Paketleyiciye giren TEK bolge: pikselleri bir kaynak tampondan satir-satir
// kopyalanir (yonelim donusumu YOK — her tuketici kendi V kuralini korur).
public sealed class RegionSource
{
    public string Name;          // tam bolge anahtari ("UI/icon.png", "Fonts/a.ttf#U+0041")
    public RegionKind Kind;
    public int W, H;             // kopyalanacak piksel boyutu
    public int OffX, OffY, OrigW, OrigH; // trim ofseti + mantiksal boyut (yoksa 0,0,W,H)
    public int Pad = 1;          // bolge etrafinda bos piksel (bilinear sizma)
    public int Unit = -1;        // >=0: ayni Unit'tekiler AYNI sayfaya duser (font glyph'leri)
    public byte[] Pixels;        // kaynak tampon
    public int Channels;         // 1 (R8) veya 4 (RGBA8)
    public int Stride;           // kaynak satir uzunlugu (piksel)
    public int SrcX, SrcY;       // kaynak tampondaki sol-ust

    public static RegionSource FromBuffer(string name, RegionKind kind, byte[] pixels, int channels,
        int stride, int x, int y, int w, int h)
        => new()
        {
            Name = name, Kind = kind, Pixels = pixels, Channels = channels, Stride = stride,
            SrcX = x, SrcY = y, W = w, H = h, OrigW = w, OrigH = h,
        };
}

public sealed class PackedPage
{
    public int Width, Height, Channels;
    public byte[] Pixels;
    public int PiecesX = -1, PiecesY = -1;
    public readonly List<(RegionSource Src, int X, int Y)> Placed = new();
}

// Ortak paketleyici (font sheet'i + atlas gruplari AYNI kodu kosar): shelf pack,
// otomatik sayfa (maxSize asilinca yeni sayfa), Unit butunlugu (font bir sayfaya
// sigmali), her sayfaya UiPieces bandi. v1: trim/rotate yok (faz 2).
public static class RegionPacker
{
    const int MinSize = 128;
    const int PiecesMargin = 4; // BakeDisc apron'u bandin disina 4px tasar

    public static List<PackedPage> Pack(IReadOnlyList<RegionSource> sources, int maxSize, int channels,
        bool uiPieces, out string error)
    {
        error = null;
        maxSize = Math.Max(MinSize, maxSize);
        var pages = new List<PackedPage>();

        // Unit gruplari (tekil kaynak = kendi birimi), buyukten kucuge deterministik.
        var units = new List<List<RegionSource>>();
        var byUnit = new Dictionary<int, List<RegionSource>>();
        foreach (var s in sources)
        {
            if (s.W <= 0 || s.H <= 0)
                continue;
            if (s.Unit < 0)
            {
                units.Add(new List<RegionSource> { s });
                continue;
            }
            if (!byUnit.TryGetValue(s.Unit, out var list))
                byUnit[s.Unit] = list = new List<RegionSource>();
            list.Add(s);
        }
        foreach (var kv in byUnit)
            units.Add(kv.Value);
        units.Sort((a, b) =>
        {
            int c = UnitHeight(b).CompareTo(UnitHeight(a));
            return c != 0 ? c : string.CompareOrdinal(a[0].Name, b[0].Name);
        });

        RegionSource pieces = uiPieces ? MakePiecesSource() : null;
        var current = new List<RegionSource>();
        if (pieces != null)
            current.Add(pieces);
        int currentSize = 0;
        var errors = new StringBuilder();

        foreach (var unit in units)
        {
            var trial = new List<RegionSource>(current);
            trial.AddRange(unit);
            int size = FitSize(trial, maxSize);
            if (size > 0)
            {
                current = trial;
                currentSize = size;
                continue;
            }
            bool emptyPage = current.Count == (pieces != null ? 1 : 0);
            if (emptyPage)
            {
                errors.Append(unit.Count == 1 ? unit[0].Name : unit[0].Name + " (+" + (unit.Count - 1) + ")")
                      .Append(" maxSize ").Append(maxSize).Append("'a sigmiyor; ");
                continue;
            }
            pages.Add(Render(current, currentSize, channels, pieces));
            current = new List<RegionSource>();
            if (pieces != null)
                current.Add(pieces);
            current.AddRange(unit);
            currentSize = FitSize(current, maxSize);
            if (currentSize <= 0)
            {
                errors.Append(unit[0].Name).Append(" maxSize ").Append(maxSize).Append("'a sigmiyor; ");
                current.RemoveRange(pieces != null ? 1 : 0, unit.Count);
            }
        }
        if (current.Count > (pieces != null ? 1 : 0))
        {
            if (currentSize <= 0)
                currentSize = FitSize(current, maxSize);
            if (currentSize > 0)
                pages.Add(Render(current, currentSize, channels, pieces));
        }
        if (errors.Length > 0)
            error = errors.ToString().TrimEnd(' ', ';');
        return pages;
    }

    static int UnitHeight(List<RegionSource> u)
    {
        int h = 0;
        foreach (var s in u)
            h = Math.Max(h, s.H + s.Pad * 2);
        return h;
    }

    // En kucuk kare pow2 boyut (<= maxSize) — sigmiyorsa 0.
    static int FitSize(List<RegionSource> items, int maxSize)
    {
        var order = Order(items);
        for (int size = MinSize; size <= maxSize; size *= 2)
            if (TryPlace(items, order, size, null, null))
                return size;
        return 0;
    }

    static List<int> Order(List<RegionSource> items)
    {
        var order = new List<int>(items.Count);
        for (int i = 0; i < items.Count; i++)
            order.Add(i);
        order.Sort((a, b) =>
        {
            int c = (items[b].H + items[b].Pad * 2).CompareTo(items[a].H + items[a].Pad * 2);
            return c != 0 ? c : string.CompareOrdinal(items[a].Name, items[b].Name);
        });
        return order;
    }

    // Shelf pack: yukseklige gore sirali raflar. px/py null ise yalniz sigma testi.
    static bool TryPlace(List<RegionSource> items, List<int> order, int size, int[] px, int[] py)
    {
        int x = 0, y = 0, shelfH = 0;
        foreach (int i in order)
        {
            var s = items[i];
            int w = s.W + s.Pad * 2, h = s.H + s.Pad * 2;
            if (w > size || h > size)
                return false;
            if (x + w > size)
            {
                x = 0;
                y += shelfH;
                shelfH = 0;
            }
            if (y + h > size)
                return false;
            if (px != null)
            {
                px[i] = x + s.Pad;
                py[i] = y + s.Pad;
            }
            x += w;
            shelfH = Math.Max(shelfH, h);
        }
        return true;
    }

    static PackedPage Render(List<RegionSource> items, int size, int channels, RegionSource pieces)
    {
        var order = Order(items);
        var px = new int[items.Count];
        var py = new int[items.Count];
        TryPlace(items, order, size, px, py);
        var page = new PackedPage
        {
            Width = size,
            Height = size,
            Channels = channels,
            Pixels = new byte[size * size * channels],
        };
        for (int i = 0; i < items.Count; i++)
        {
            var s = items[i];
            Blit(page, s, px[i], py[i]);
            if (ReferenceEquals(s, pieces))
            {
                page.PiecesX = px[i] + PiecesMargin;
                page.PiecesY = py[i] + PiecesMargin;
            }
            else
                page.Placed.Add((s, px[i], py[i]));
        }
        return page;
    }

    // Satir-satir kopya; kanal donusumu: 1->4 deger dort kanala kopyalanir (SDF .r'den
    // okunur, duz shader beyaz*alpha gorur). 4->1 desteklenmez (RGBA sayfa secilmeli).
    static void Blit(PackedPage page, RegionSource s, int dx, int dy)
    {
        int dc = page.Channels, sc = s.Channels;
        for (int r = 0; r < s.H; r++)
        {
            int srcRow = ((s.SrcY + r) * s.Stride + s.SrcX) * sc;
            int dstRow = ((dy + r) * page.Width + dx) * dc;
            if (sc == dc)
                Buffer.BlockCopy(s.Pixels, srcRow, page.Pixels, dstRow, s.W * sc);
            else if (sc == 1 && dc == 4)
            {
                for (int x = 0; x < s.W; x++)
                {
                    byte v = s.Pixels[srcRow + x];
                    int o = dstRow + x * 4;
                    page.Pixels[o] = v; page.Pixels[o + 1] = v; page.Pixels[o + 2] = v; page.Pixels[o + 3] = v;
                }
            }
            else
                throw new InvalidOperationException($"bolge {s.Name}: {sc} kanal -> {dc} kanal sayfa desteklenmiyor");
        }
    }

    // UiPieces bandi: apron paylı R8 tampon; her sayfaya ayni kaynak kopyalanir.
    static RegionSource MakePiecesSource()
    {
        int w = UiPieces.RegionW + PiecesMargin * 2, h = UiPieces.RegionH + PiecesMargin * 2;
        var px = new byte[w * h];
        UiPieces.BakeRegion(px, w, PiecesMargin, PiecesMargin);
        var s = RegionSource.FromBuffer("#ui/pieces", RegionKind.Sdf, px, 1, w, 0, 0, w, h);
        s.Pad = 2;
        return s;
    }

    public static bool AllSdf(IReadOnlyList<RegionSource> sources)
    {
        foreach (var s in sources)
            if (s.Kind != RegionKind.Sdf)
                return false;
        return true;
    }
}

// Bolge manifestosu ("regions" artifact'i): bolge URETICISI importer'lar (font,
// spine, texturepacker...) ciktilarini bu sozlesmeyle bildirir; AtlasImporter
// baska hicbir sey bilmez. Pikseller ayni importer'in bir piksel artifact'inde
// (DPIX) yasar; her giris o artifact'in adini ve icindeki dikdortgeni verir.
// [magic][version][singlePage][count]{sub, pixels, x,y,w,h, offX,offY,origW,origH, kind, pad}
public sealed class RegionManifest
{
    public const int Magic = 0x4E475244; // "DRGN"
    public const int Version = 1;

    public struct Entry
    {
        public string Sub;       // alt ad ("U+0041"); tam anahtar = asset + "#" + Sub
        public string Pixels;    // piksel artifact adi ("sheet")
        public int X, Y, W, H;
        public int OffX, OffY, OrigW, OrigH;
        public RegionKind Kind;
        public int Pad;
    }

    public bool SinglePage; // tum bolgeler ayni sayfada olmali (font)
    public readonly List<Entry> Entries = new();

    public byte[] Write()
    {
        var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(Version);
        w.Write(SinglePage);
        w.Write(Entries.Count);
        foreach (var e in Entries)
        {
            w.Write(e.Sub ?? "");
            w.Write(e.Pixels ?? "");
            w.Write(e.X); w.Write(e.Y); w.Write(e.W); w.Write(e.H);
            w.Write(e.OffX); w.Write(e.OffY); w.Write(e.OrigW); w.Write(e.OrigH);
            w.Write((byte)e.Kind);
            w.Write(e.Pad);
        }
        w.Flush();
        return ms.ToArray();
    }

    public static RegionManifest Parse(byte[] blob)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(blob), Encoding.UTF8);
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version)
                return null;
            var m = new RegionManifest { SinglePage = r.ReadBoolean() };
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                m.Entries.Add(new Entry
                {
                    Sub = r.ReadString(),
                    Pixels = r.ReadString(),
                    X = r.ReadInt32(), Y = r.ReadInt32(), W = r.ReadInt32(), H = r.ReadInt32(),
                    OffX = r.ReadInt32(), OffY = r.ReadInt32(), OrigW = r.ReadInt32(), OrigH = r.ReadInt32(),
                    Kind = (RegionKind)r.ReadByte(),
                    Pad = r.ReadInt32(),
                });
            }
            return m;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

// Editor-zamani senkron piksel decode (native worker + sinirli bekleme; import/build
// baglami, frame butcesi yok). Cikti RGBA8, satir 0 ALTTA (stbi flip; dokularla ayni).
public static class EditorPixels
{
    public static byte[] DecodeFile(string file, out int w, out int h)
        => Await(Sokol.AssetLoad(file), out w, out h);

    public static byte[] Await(int job, out int w, out int h)
    {
        w = h = 0;
        if (job < 0)
            return null;
        try
        {
            for (int i = 0; i < 1000; i++)
            {
                int r = Sokol.AssetPoll(job, out var pixels, out w, out h);
                if (r < 0)
                    return null;
                if (r == 1)
                {
                    var rgba = new byte[w * h * 4];
                    Marshal.Copy(pixels, rgba, 0, rgba.Length);
                    return rgba;
                }
                System.Threading.Thread.Sleep(5);
            }
            return null;
        }
        finally
        {
            Sokol.AssetFreeJob(job);
        }
    }
}
