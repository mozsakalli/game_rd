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
            Name = name,
            Kind = kind,
            Pixels = pixels,
            Channels = channels,
            Stride = stride,
            SrcX = x,
            SrcY = y,
            W = w,
            H = h,
            OrigW = w,
            OrigH = h,
        };
}

public sealed class PackedPage
{
    public int Width, Height, Channels;
    public byte[] Pixels;
    public int PiecesX = -1, PiecesY = -1;
    public readonly List<(RegionSource Src, int X, int Y)> Placed = new();

    // Sayfa PNG'si: satirlar TERS yazilir ki stb_image'in yukleme flip'i bellek duzenini (depolama
    // satiri 0 = ilk) birebir geri getirsin — bolge koordinatlari degismez.
    public byte[] EncodePng() => PngWriter.Encode(Pixels, Width, Height, flip: true);
}

// Ortak paketleyici (font sheet'i + atlas gruplari AYNI kodu kosar): MaxRects (best short side fit,
// rotasyon yok), SIKI dikdortgen sayfa (pow2 degil: dokular mip'siz + clamp, NPOT her hedefte calisir;
// mip gerekirse upload'da sag/alt pad eklenir, bolge koordinatlari piksel oldugundan tablo bozulmaz),
// otomatik sayfa (maxSize asilinca yeni sayfa), Unit butunlugu (font bir sayfaya sigmali), her sayfaya
// UiPieces bandi. Rotate yok.
public static class RegionPacker
{
    const int MinSize = 128;
    const int Align = 4; // sayfa W/H hizasi (sikistirilmis format bloklari icin)
    const int PiecesMargin = 4; // BakeDisc apron'u bandin disina 4px tasar

    // TRIM: Color bolgelerin tamamen seffaf (alfa == 0) kenar bantlari sayfaya girmez; bolge kucultulur,
    // OffX/OffY/OrigW/OrigH mantiksal boyutu korur (cizim ayni: Sprite tuketicileri ofsetle yerlestirir).
    // Kaynak zaten trim tasiyorsa (font sayfasindan gelen bolge) birlesir (Off += bbox). SDF ASLA kirpilmaz:
    // alfa = mesafe, her yerde sifirdan farkli; kirpmak alanin dis yumusamasini keser. Tamamen seffaf bolge
    // -> 1x1 (paketleyici 0 boyutu atar, anahtar kaybolurdu). Yalniz RGBA (Channels == 4) kaynaklar.
    // Donus: atilan piksel sayisi (log icin).
    public static long TrimTransparent(IReadOnlyList<RegionSource> sources)
    {
        long saved = 0;
        foreach (var s in sources)
        {
            if (s.Kind != RegionKind.Color || s.Channels != 4 || s.W <= 0 || s.H <= 0 || s.Pixels == null)
                continue;
            int x0 = s.W, y0 = s.H, x1 = -1, y1 = -1;
            for (int y = 0; y < s.H; y++)
            {
                int row = ((s.SrcY + y) * s.Stride + s.SrcX) * 4 + 3;
                for (int x = 0; x < s.W; x++)
                {
                    if (s.Pixels[row + x * 4] == 0)
                        continue;
                    if (x < x0) x0 = x;
                    if (x > x1) x1 = x;
                    if (y < y0) y0 = y;
                    if (y > y1) y1 = y;
                }
            }
            if (x1 < 0) { x0 = y0 = 0; x1 = y1 = 0; } // tamamen seffaf: 1x1 tut
            int nw = x1 - x0 + 1, nh = y1 - y0 + 1;
            if (nw == s.W && nh == s.H)
                continue;
            saved += (long)s.W * s.H - (long)nw * nh;
            // Tampon satir 0 ALTTA (stbi flip): y0 = alt bosluk; OffY ise mantiksal UST bosluk (Sprite sozlesmesi).
            int topGap = s.H - 1 - y1;
            s.SrcX += x0; s.SrcY += y0;
            s.OffX += x0; s.OffY += topGap;
            s.W = nw; s.H = nh;
        }
        return saved;
    }

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
        Layout currentLayout = null;
        var errors = new StringBuilder();

        foreach (var unit in units)
        {
            var trial = new List<RegionSource>(current);
            trial.AddRange(unit);
            var layout = Fit(trial, maxSize);
            if (layout != null)
            {
                current = trial;
                currentLayout = layout;
                continue;
            }
            bool emptyPage = current.Count == (pieces != null ? 1 : 0);
            if (emptyPage)
            {
                errors.Append(unit.Count == 1 ? unit[0].Name : unit[0].Name + " (+" + (unit.Count - 1) + ")")
                      .Append(" maxSize ").Append(maxSize).Append("'a sigmiyor; ");
                continue;
            }
            pages.Add(Render(current, currentLayout, channels, pieces));
            current = new List<RegionSource>();
            if (pieces != null)
                current.Add(pieces);
            current.AddRange(unit);
            currentLayout = Fit(current, maxSize);
            if (currentLayout == null)
            {
                errors.Append(unit[0].Name).Append(" maxSize ").Append(maxSize).Append("'a sigmiyor; ");
                current.RemoveRange(pieces != null ? 1 : 0, unit.Count);
            }
        }
        if (current.Count > (pieces != null ? 1 : 0))
        {
            currentLayout ??= Fit(current, maxSize);
            if (currentLayout != null)
                pages.Add(Render(current, currentLayout, channels, pieces));
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

    sealed class Layout
    {
        public int W, H;
        public int[] X, Y; // items indeksli sol-ust (pad dahil degil: bolgenin kendi koordinati)
    }

    static int AlignUp(int v) => (v + Align - 1) / Align * Align;

    // En kucuk ALANLI sayfa (W,H <= maxSize): aday genisliklerde MaxRects kosulur, kullanilan yukseklik olculur.
    // Esit alanda kareye yakin olan secilir. Sigmiyorsa null.
    static Layout Fit(List<RegionSource> items, int maxSize)
    {
        var order = Order(items);
        long area = 0;
        int maxW = 0, maxH = 0;
        foreach (var s in items)
        {
            int w = s.W + s.Pad * 2, h = s.H + s.Pad * 2;
            area += (long)w * h;
            maxW = Math.Max(maxW, w);
            maxH = Math.Max(maxH, h);
        }
        if (maxW > maxSize || maxH > maxSize)
            return null;

        Layout best = null;
        int w0 = AlignUp(Math.Max(maxW, (int)Math.Ceiling(Math.Sqrt(area))));
        w0 = Math.Max(w0, Align);
        var px = new int[items.Count];
        var py = new int[items.Count];
        int prev = -1;
        for (int k = 0; ; k++)
        {
            int W = Math.Min(maxSize, AlignUp((int)(w0 * (1.0 + 0.12 * k))));
            if (W == prev)
            {
                if (W >= maxSize) break;
                continue;
            }
            prev = W;
            if (MaxRectsPlace(items, order, W, maxSize, px, py, out int usedH))
            {
                int H = Math.Max(Align, AlignUp(usedH));
                long a = (long)W * H;
                long ba = best == null ? long.MaxValue : (long)best.W * best.H;
                if (a < ba || (a == ba && Math.Abs(W - H) < Math.Abs(best.W - best.H)))
                    best = new Layout { W = W, H = H, X = (int[])px.Clone(), Y = (int[])py.Clone() };
            }
            if (W >= maxSize)
                break;
        }
        return best;
    }

    static List<int> Order(List<RegionSource> items)
    {
        var order = new List<int>(items.Count);
        for (int i = 0; i < items.Count; i++)
            order.Add(i);
        order.Sort((a, b) =>
        {
            int c = (items[b].H + items[b].Pad * 2).CompareTo(items[a].H + items[a].Pad * 2);
            if (c != 0) return c;
            c = (items[b].W + items[b].Pad * 2).CompareTo(items[a].W + items[a].Pad * 2);
            return c != 0 ? c : string.CompareOrdinal(items[a].Name, items[b].Name);
        });
        return order;
    }

    struct FreeRect { public int X, Y, W, H; }

    // MaxRects (Jylanki) — Best Short Side Fit, rotasyon yok. Sira deterministik (order). Kutu W x maxH;
    // tumunu yerlestirirse kullanilan yuksekligi doner. px/py bolge sol-ustu (pad icerde).
    static bool MaxRectsPlace(List<RegionSource> items, List<int> order, int W, int maxH, int[] px, int[] py, out int usedH)
    {
        usedH = 0;
        var free = new List<FreeRect>(items.Count * 2) { new FreeRect { X = 0, Y = 0, W = W, H = maxH } };
        foreach (int i in order)
        {
            var s = items[i];
            int w = s.W + s.Pad * 2, h = s.H + s.Pad * 2;
            int bestIdx = -1, bestShort = int.MaxValue, bestLong = int.MaxValue;
            for (int f = 0; f < free.Count; f++)
            {
                var r = free[f];
                if (r.W < w || r.H < h)
                    continue;
                int dw = r.W - w, dh = r.H - h;
                int sh = Math.Min(dw, dh), lo = Math.Max(dw, dh);
                // Esitlikte ust-sol tercih: kullanilan yukseklik duzgun buyur (sayfa alani kucuk kalir).
                if (sh < bestShort || (sh == bestShort && (lo < bestLong || (lo == bestLong && (r.Y < free[bestIdx].Y || (r.Y == free[bestIdx].Y && r.X < free[bestIdx].X))))))
                {
                    bestIdx = f; bestShort = sh; bestLong = lo;
                }
            }
            if (bestIdx < 0)
                return false;
            var at = free[bestIdx];
            int nx = at.X, ny = at.Y;
            px[i] = nx + s.Pad;
            py[i] = ny + s.Pad;
            usedH = Math.Max(usedH, ny + h);

            // Yerlesen kutuyla kesisen serbest dikdortgenleri bol (en fazla 4 parca); yeni parcalardan
            // baskasinin icinde kalanlar atilir (eskiler zaten birbirine gore maksimal).
            var next = new List<FreeRect>(free.Count + 4);
            int keep = 0;
            foreach (var r in free)
            {
                if (nx >= r.X + r.W || nx + w <= r.X || ny >= r.Y + r.H || ny + h <= r.Y)
                {
                    next.Insert(keep++, r);
                    continue;
                }
                if (nx > r.X) next.Add(new FreeRect { X = r.X, Y = r.Y, W = nx - r.X, H = r.H });
                if (nx + w < r.X + r.W) next.Add(new FreeRect { X = nx + w, Y = r.Y, W = r.X + r.W - (nx + w), H = r.H });
                if (ny > r.Y) next.Add(new FreeRect { X = r.X, Y = r.Y, W = r.W, H = ny - r.Y });
                if (ny + h < r.Y + r.H) next.Add(new FreeRect { X = r.X, Y = ny + h, W = r.W, H = r.Y + r.H - (ny + h) });
            }
            for (int a = keep; a < next.Count; a++)
            {
                bool drop = false;
                for (int b = 0; b < next.Count && !drop; b++)
                    if (b != a && Contains(next[b], next[a]) && (b < a || !Equal(next[a], next[b])))
                        drop = true; // esitlerde kucuk indeksli kalir
                if (drop)
                {
                    next.RemoveAt(a);
                    a--;
                }
            }
            for (int b = 0; b < keep; b++)
                for (int a = keep; a < next.Count; a++)
                    if (Contains(next[a], next[b]))
                    {
                        next.RemoveAt(b);
                        b--; keep--;
                        break;
                    }
            free = next;
        }
        return true;
    }

    static bool Equal(in FreeRect a, in FreeRect b) => a.X == b.X && a.Y == b.Y && a.W == b.W && a.H == b.H;

    static bool Contains(in FreeRect outer, in FreeRect inner)
        => inner.X >= outer.X && inner.Y >= outer.Y
           && inner.X + inner.W <= outer.X + outer.W && inner.Y + inner.H <= outer.Y + outer.H;

    static PackedPage Render(List<RegionSource> items, Layout layout, int channels, RegionSource pieces)
    {
        var px = layout.X;
        var py = layout.Y;
        var page = new PackedPage
        {
            Width = layout.W,
            Height = layout.H,
            Channels = channels,
            Pixels = new byte[layout.W * layout.H * channels],
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
}

// Editor-zamani senkron piksel decode (native worker + sinirli bekleme; import/build
// baglami, frame butcesi yok). Cikti RGBA8, satir 0 ALTTA (stbi flip; dokularla ayni).
public static class EditorPixels
{
    public static byte[] DecodeFile(string file, out int w, out int h)
        => Await(Sokol.AssetLoad(file), out w, out h);

    // Bloklayan bekleme: yalniz bu job'un sonucu kuyruktan cekilir (NativeAsync.TryTake), Pump kosmaz.
    public static byte[] Await(int job, out int w, out int h)
    {
        w = h = 0;
        if (job < 0)
            return null;
        for (int i = 0; i < 1000; i++)
        {
            if (NativeAsync.TryTake(job, out var c))
            {
                byte[] rgba = null;
                if (c.Ok)
                {
                    w = c.A; h = c.B;
                    rgba = new byte[w * h * 4];
                    if (c.Len == (long)w * h)
                    {
                        // Gray kaynak (R8 decode): editor araclari RGBA bekler, dort kanala kopyala.
                        var r8 = new byte[w * h];
                        Marshal.Copy(c.Data, r8, 0, r8.Length);
                        for (int k = 0; k < r8.Length; k++)
                            rgba[k * 4] = rgba[k * 4 + 1] = rgba[k * 4 + 2] = rgba[k * 4 + 3] = r8[k];
                    }
                    else
                        Marshal.Copy(c.Data, rgba, 0, rgba.Length);
                }
                NativeAsync.Free(c.Data);
                return rgba;
            }
            System.Threading.Thread.Sleep(5);
        }
        return null;
    }
}
