using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Editor kucuk resim cache'i (Project paneli, Inspector alt oge basligi, alan onizlemesi).
// Anahtar: "yol" (asset) ya da "yol#id" (alt oge). Kaynak: alt oge -> sahibin index'indeki piksel artifact'i +
// dikdortgen; asset -> gorsel ise kaynak dosya; yoksa null (cagiran tip ikonu cizer).
//
// ATLAS: thumbnail'ler ayri doku DEGIL — 1024x1024 RGBA sayfalarda 128x128 hucre (64/sayfa). Binlerce thumbnail =
// onlarca GPU image (sokol havuzu / surucu handle'lari icin). Sayfa DINAMIK doku + CPU kopyasi; hucre yazimi CPU'ya,
// frame basina sayfa basina tek UpdateRgba (immutable doku guncellenemez — sokol kurali). Hucre LRU ile geri alinir.
// DISK ONBELLEGI: uretilen 128px thumbnail Library/Thumbs/<guid>-<fnv(key)>-<fnv(stamp)>.png; geri yukleme tam
// kaynak decode'u yerine kucuk PNG okur. Damga (import .stamp) degisince ad degisir, eski dosya silinir.
// Dokular straight RGBA, satir 0 ALTTA (EditorPixels ile ayni) -> cizim DrawRegion v ters.
public static class ThumbnailCache
{
    public const int MaxSize = 128;
    const int PageSize = 1024, CellsPerRow = PageSize / MaxSize, CellsPerPage = CellsPerRow * CellsPerRow;
    const int MaxPages = 32;             // 2048 thumbnail GPU'da ayni anda
    const int DecodeBudget = 2;          // frame basina tam kaynak decode (bloklayici)
    const int DiskBudget = 8;            // frame basina disk onbellek okuma (ucuz)

    sealed class Page
    {
        public Texture Tex;                 // dinamik RGBA (frame'de en fazla bir UpdateRgba)
        public byte[] Pixels;               // CPU kopyasi (hucre yazimi buraya; Flush GPU'ya basar)
        public bool Dirty;
        public int Free; // bos hucre sayisi (hizli secim icin)
        public readonly bool[] Used = new bool[CellsPerPage];
    }

    sealed class Slot
    {
        public int Page = -1, Cell = -1;
        public int W, H;        // thumbnail boyutu (hucre icinde sol-ust)
        public bool Done;       // uretim denendi (Page<0 ise kaynak yok / hata)
        public int LastUse;
    }

    static readonly Dictionary<string, Slot> _slots = new(StringComparer.Ordinal);
    static readonly List<Page> _pages = new();
    static readonly List<string> _pending = new();
    static int _seenVersion = -1;
    static int _frame = -1, _decodes, _diskReads;
    static Material _material;
    static string _diskDir;

    static void Sync()
    {
        if (_seenVersion != AssetWatcher.AssetChangeVersion)
        {
            _seenVersion = AssetWatcher.AssetChangeVersion;
            Clear(); // slotlar duser; disk onbellegi damgayla kendini dogrular
        }
        if (_frame != Time.frameCount)
        {
            _frame = Time.frameCount;
            _decodes = _diskReads = 0;
            Pump();
            FlushPages(); // kirli sayfalar GPU'ya (frame basina sayfa basina tek upload)
        }
    }

    static unsafe void FlushPages()
    {
        foreach (var p in _pages)
        {
            if (!p.Dirty) continue;
            p.Dirty = false;
            fixed (byte* ptr = p.Pixels)
                p.Tex.UpdateRgba((Color*)ptr);
        }
    }

    // GPU/slot tablosunu bosaltir (sayfalar kalir; hucreler serbest). Reimport/proje degisiminde.
    public static void Clear()
    {
        _slots.Clear();
        _pending.Clear();
        foreach (var p in _pages)
        {
            Array.Clear(p.Used);
            p.Free = CellsPerPage;
        }
    }

    // Thumbnail var mi: sayfa dokusu + hucre piksel dikdortgeni (bellek satirlari; satir 0 altta).
    public static bool TryGet(string key, out Texture page, out int x, out int y, out int w, out int h)
    {
        page = null; x = y = w = h = 0;
        if (string.IsNullOrEmpty(key))
            return false;
        Sync();
        if (!_slots.TryGetValue(key, out var s))
        {
            s = new Slot();
            _slots[key] = s;
            _pending.Add(key);
        }
        s.LastUse = _frame;
        if (s.Page < 0)
            return false;
        page = _pages[s.Page].Tex;
        x = (s.Cell % CellsPerRow) * MaxSize;
        y = (s.Cell / CellsPerRow) * MaxSize;
        w = s.W;
        h = s.H;
        return true;
    }

    // Repaint yardimcisi: kutuya oranli sigdirip cizer; doku yoksa false (cagiran ikon cizer).
    public static bool Draw(string key, in Rect box, int layerOffset = 1)
    {
        if (!TryGet(key, out var page, out int x, out int y, out int w, out int h) || w <= 0 || h <= 0)
            return false;
        float sc = Math.Min(box.width / w, box.height / h);
        float dw = w * sc, dh = h * sc;
        var r = new Rect(box.x + (box.width - dw) * 0.5f, box.y + (box.height - dh) * 0.5f, dw, dh);
        _material ??= new Material { SortMode = SortMode.Ui };
        _material.MainTexture = page;
        const float inv = 1f / PageSize;
        // Satir 0 altta: quad ustu <-> bellek son satiri (y+h), alti <-> y.
        GuiRenderer.DrawRegion(r, _material, Color.White, x * inv, (y + h) * inv, (x + w) * inv, y * inv, layerOffset);
        return true;
    }

    // ---- hucre yonetimi ----

    static bool AllocCell(out int page, out int cell)
    {
        for (int p = 0; p < _pages.Count; p++)
        {
            if (_pages[p].Free == 0) continue;
            var pg = _pages[p];
            for (int c = 0; c < CellsPerPage; c++)
                if (!pg.Used[c]) { pg.Used[c] = true; pg.Free--; page = p; cell = c; return true; }
        }
        if (_pages.Count < MaxPages)
        {
            var tex = Texture.CreateDynamicRgba(PageSize, PageSize);
            tex.Name = "thumb-page" + _pages.Count;
            var pg = new Page { Tex = tex, Pixels = new byte[PageSize * PageSize * 4], Free = CellsPerPage - 1 };
            pg.Used[0] = true;
            _pages.Add(pg);
            page = _pages.Count - 1; cell = 0;
            return true;
        }
        // Dolu: en eski kullanilan slotu geri al (bu frame gorulenlere dokunma).
        string victim = null; int oldest = int.MaxValue;
        foreach (var kv in _slots)
        {
            var s = kv.Value;
            if (s.Page < 0 || s.LastUse >= _frame) continue;
            if (s.LastUse < oldest) { oldest = s.LastUse; victim = kv.Key; }
        }
        if (victim == null) { page = cell = -1; return false; }
        var vs = _slots[victim];
        page = vs.Page; cell = vs.Cell;
        _slots.Remove(victim);
        return true;
    }

    static void Pump()
    {
        // Son istenen once (gorunur hucreler): kuyrugun sonundan tuket.
        while (_pending.Count > 0 && (_decodes < DecodeBudget || _diskReads < DiskBudget))
        {
            string key = _pending[^1];
            if (!_slots.TryGetValue(key, out var s) || s.Done) { _pending.RemoveAt(_pending.Count - 1); continue; }
            // Disk onbellegi varsa ucuz yol; yoksa decode butcesi gerek.
            string disk = DiskPath(key, out string stampHash);
            bool cached = disk != null && File.Exists(disk);
            if (cached ? _diskReads >= DiskBudget : _decodes >= DecodeBudget)
                break;
            _pending.RemoveAt(_pending.Count - 1);
            s.Done = true;
            try
            {
                if (cached) { _diskReads++; if (LoadCached(disk, s)) continue; }
                _decodes++;
                Build(key, s, disk, stampHash);
            }
            catch (Exception e) { EditorLog.Warning($"[thumb] {key}: {e.Message}"); }
        }
    }

    // ---- disk onbellegi ----

    static string DiskDir()
    {
        if (_diskDir != null) return _diskDir;
        string lib = App.Project?.LibraryPath;
        if (lib == null) return null;
        _diskDir = Path.Combine(lib, "Thumbs");
        Directory.CreateDirectory(_diskDir);
        return _diskDir;
    }

    static string DiskPath(string key, out string stampHash)
    {
        stampHash = null;
        string dir = DiskDir();
        string owner = SubAssets.Split(key, out _);
        string guid = App.Assets?.PathToGuid(owner);
        string stamp = ImportPipeline.PeekStamp(owner);
        if (dir == null || guid == null || stamp == null)
            return null;
        stampHash = Fnv("v2:" + stamp); // v2: yon duzeltmesi (eski onbellek dosyalari ters) — ad degisince eskisi silinir
        return Path.Combine(dir, guid + "-" + Fnv(key) + "-" + stampHash + ".png");
    }

    static string Fnv(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
        return h.ToString("x16");
    }

    static bool LoadCached(string path, Slot s)
    {
        var px = EditorPixels.DecodeFile(path, out int w, out int h);
        if (px == null || w <= 0 || h <= 0 || w > MaxSize || h > MaxSize)
            return false;
        Place(s, px, w, h);
        return true;
    }

    static void SaveCached(string path, byte[] rgba, int w, int h)
    {
        if (path == null) return;
        try
        {
            // Ayni anahtarin eski damgali kopyalari silinir (yeniden import).
            string prefix = Path.GetFileName(path);
            prefix = prefix.Substring(0, prefix.LastIndexOf('-') + 1);
            foreach (var f in Directory.GetFiles(Path.GetDirectoryName(path), prefix + "*.png"))
                if (!string.Equals(f, path, StringComparison.OrdinalIgnoreCase))
                    File.Delete(f);
            File.WriteAllBytes(path, PngWriter.Encode(rgba, w, h, flip: true)); // decode flip'i bellek duzenini geri getirir
        }
        catch (Exception e) { EditorLog.Warning("[thumb] onbellek yazilamadi: " + e.Message); }
    }

    // ---- uretim ----

    static void Place(Slot s, byte[] rgba, int w, int h)
    {
        if (!AllocCell(out int page, out int cell))
            return;
        // Hucre tamami yazilir (kalan seffaf): komsu bulasmasi yok.
        var pg = _pages[page];
        int cx = (cell % CellsPerRow) * MaxSize, cy = (cell / CellsPerRow) * MaxSize;
        for (int y = 0; y < MaxSize; y++)
        {
            int dst = ((cy + y) * PageSize + cx) * 4;
            Array.Clear(pg.Pixels, dst, MaxSize * 4);
            if (y < h)
                Buffer.BlockCopy(rgba, y * w * 4, pg.Pixels, dst, w * 4);
        }
        pg.Dirty = true;
        s.Page = page; s.Cell = cell; s.W = w; s.H = h;
    }

    static void Build(string key, Slot s, string diskPath, string stampHash)
    {
        string owner = SubAssets.Split(key, out string id);
        byte[] px; int w = 0, h = 0, x0 = 0, y0 = 0, cw, ch; bool sdf = false;
        if (id != null)
        {
            if (!SubAssets.TryGetEntry(owner, id, out var e) || string.IsNullOrEmpty(e.Pixels))
                return;
            string path = ImportPipeline.ArtifactPath(owner, e.Pixels);
            px = path != null ? EditorPixels.DecodeFile(path, out w, out h) : null;
            if (px == null)
                return;
            if (e.W > 0 && e.H > 0) { x0 = e.X; y0 = e.Y; cw = e.W; ch = e.H; }
            else { cw = w; ch = h; }
            sdf = e.Sdf;
        }
        else
        {
            if (AssetDatabase.ImportTypeOf(owner) != typeof(Sprite))
                return;
            string full = ImportPipeline.FullPath(owner);
            px = full != null ? EditorPixels.DecodeFile(full, out w, out h) : null;
            if (px == null)
                return;
            cw = w; ch = h;
        }
        if (x0 < 0 || y0 < 0 || x0 + cw > w || y0 + ch > h || cw <= 0 || ch <= 0)
            return;
        // Kutu filtresiyle kucult (tam sayi faktor).
        int f = Math.Max(1, (int)Math.Ceiling(Math.Max(cw, ch) / (double)MaxSize));
        int tw = Math.Max(1, cw / f), th = Math.Max(1, ch / f);
        var outPx = new byte[tw * th * 4];
        for (int ty = 0; ty < th; ty++)
        {
            for (int tx = 0; tx < tw; tx++)
            {
                int r = 0, g = 0, b = 0, a = 0, n = 0;
                for (int dy = 0; dy < f; dy++)
                {
                    int sy = y0 + ty * f + dy;
                    if (sy >= y0 + ch) break;
                    for (int dx = 0; dx < f; dx++)
                    {
                        int sx = x0 + tx * f + dx;
                        if (sx >= x0 + cw) break;
                        int i = (sy * w + sx) * 4;
                        int pa = px[i + 3];
                        if (sdf)
                        {
                            // MTSDF: sekil = median(r,g,b) >= 0.5 -> duz beyaz glyph.
                            int cr = px[i], cg = px[i + 1], cb = px[i + 2];
                            int med = Math.Max(Math.Min(cr, cg), Math.Min(Math.Max(cr, cg), cb));
                            pa = med >= 128 ? 255 : 0;
                            r += 255; g += 255; b += 255; a += pa; n++;
                            continue;
                        }
                        // Alfa agirlikli renk ortalamasi (seffaf pikselin rengi sonucu kirletmesin).
                        r += px[i] * pa; g += px[i + 1] * pa; b += px[i + 2] * pa; a += pa; n++;
                    }
                }
                int o = (ty * tw + tx) * 4;
                if (n == 0) continue;
                if (sdf)
                {
                    outPx[o] = outPx[o + 1] = outPx[o + 2] = 255;
                    outPx[o + 3] = (byte)(a / n);
                }
                else if (a > 0)
                {
                    outPx[o] = (byte)(r / a); outPx[o + 1] = (byte)(g / a); outPx[o + 2] = (byte)(b / a);
                    outPx[o + 3] = (byte)(a / n);
                }
            }
        }
        // YON: motor dokulari satir 0 ALTTA (png decode, PSD katmani ayni duzene cevrilir); SDF glyph rasteri satir 0 USTTE
        // (TextSprite V takasi yapar). Sayfa cizimi (v0=1,v1=0) satir 0'i alta koyar -> SDF disi kaynaklar burada cevrilir.
        if (!sdf)
        {
            var row = new byte[tw * 4];
            for (int a0 = 0, b0 = th - 1; a0 < b0; a0++, b0--)
            {
                Buffer.BlockCopy(outPx, a0 * tw * 4, row, 0, tw * 4);
                Buffer.BlockCopy(outPx, b0 * tw * 4, outPx, a0 * tw * 4, tw * 4);
                Buffer.BlockCopy(row, 0, outPx, b0 * tw * 4, tw * 4);
            }
        }
        Place(s, outPx, tw, th);
        SaveCached(diskPath, outPx, tw, th);
    }
}
