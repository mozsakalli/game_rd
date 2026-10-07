using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace DigitoyEngine;

// Asset BAYTLARININ nereden geldigi soyutlamasi. Editor: loose dosyalar
// (esnek, .meta/hot-reload). Release: tek pak dosyasi (tarama yok, meta yok,
// guid tablosu gomulu). Her iki kaynak da NativeFs (handle/yol + offset) uzerinden
// konusur: IO ve decode native worker'da, managed tek thread, API async-first.
// Senkron okuma YALNIZ editorde (DE_EDITOR); release'te bloklayan yol yok (web).
public abstract class AssetSource
{
    // Async piksel decode isi baslatir (native worker). Donus: job id, dolu: -1.
    public abstract int StartLoad(string key);

    // Ham baytlar (zlib acilmis) â€” job'u AsyncJobs.Pump tamamlar. Yoksa null.
    public abstract Task<byte[]> ReadBytesAsync(string key);

#if DE_EDITOR
    // Editor/arac yolu: bloklayan okuma (import, verify, YAML sahne).
    public abstract byte[] ReadBytes(string key);
#endif

    public abstract bool Exists(string key);

    // Akis kaynagi (muzik/radyo native player'i icin): anahtarin ham baytlarinin
    // yasadigi dosya + aralik. length 0 = dosyanin tamami. Pak'ta yalniz STORED
    // (sikistirilmamis) girisler akitilabilir; zlib'li giris = false.
    public abstract bool TryGetRange(string key, out string path, out long offset, out long length);

    // Kaynaktaki tum anahtarlar (atlas kesfi gibi toplu isler; sicak yolda cagrilmaz).
    public abstract IEnumerable<string> Keys { get; }

    // Asset bagimlilik grafigi (build'de yazilir): anahtarin yuklenmeden once hazir
    // olmasi gereken diger anahtarlar (sahne -> asset'leri, sprite -> atlasi, atlas -> sayfalari).
    // Loose kaynakta bos: editor tembel/senkron yukler.
    public virtual string[] GetDependencies(string key) => Array.Empty<string>();

    // Release'te guid->yol eslemesi kaynaktan gelir (ScanMetas kosmaz).
    public virtual void FillGuidTable(
        Dictionary<string, string> guidToPath, Dictionary<string, string> pathToGuid)
    { }
}

#if DE_EDITOR
// Editor/dev kaynagi: Assets/ altindaki loose dosyalar. Release'e girmez.
public sealed class LooseFileSource : AssetSource
{
    public readonly string Root;

    public LooseFileSource(string root) => Root = root;

    public override int StartLoad(string key) => NativeFs.Decode(-1, Path.Combine(Root, key), 0, 0, 0);

    public override Task<byte[]> ReadBytesAsync(string key)
    {
        var p = Path.Combine(Root, key);
        if (!File.Exists(p))
        {
            var tcs = new TaskCompletionSource<byte[]>();
            tcs.TrySetResult(null);
            return tcs.Task;
        }
        return AsyncJobs.ReadAsync(NativeFs.Read(-1, p, 0, 0, 0));
    }

    public override byte[] ReadBytes(string key)
    {
        var p = Path.Combine(Root, key);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    public override bool Exists(string key) => File.Exists(Path.Combine(Root, key));

    public override bool TryGetRange(string key, out string path, out long offset, out long length)
    {
        path = Path.Combine(Root, key);
        offset = length = 0;
        return File.Exists(path);
    }

    public override IEnumerable<string> Keys
    {
        get
        {
            if (!Directory.Exists(Root))
                yield break;
            foreach (var f in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;
                yield return Path.GetRelativePath(Root, f).Replace('\\', '/');
            }
        }
    }
}
#endif

// Pak dosyasi v3: [magic][count][indexLen][index][blob].
//   index girisi: u16 keyLen + key(utf8), u16 guidLen + guid, i64 offset, i64 storedLen, i64 rawLen,
//                 u16 nDep + { u16 len + key }[]   (bagimlilik kenarlari)
// Girisler TEK TEK zlib'lenir (rastgele erisim bozulmaz); sikismayanlar (png gibi)
// oldugu gibi saklanir (stored: rawLen==storedLen). Index acilista TEK okumayla
// gelir (indexLen header'da); tum okumalar native handle + offset ile worker'da
// (inflate dahil, stb zlib). Acma da async: web'de fetch, desktop'ta aninda.
public sealed class PakSource : AssetSource
{
    public const int Magic = 0x334B5044; // "DPK3"
    public const int HeaderSize = 12;

    struct Entry
    {
        public string Guid;
        public long Offset;
        public long StoredLength;
        public long RawLength; // == StoredLength: sikistirmasiz
        public string[] Deps;
    }

    readonly string _path;
    readonly int _handle; // NativeFs handle (editor senkron modunda -1)
    readonly Dictionary<string, Entry> _entries = new();

    PakSource(string path, int handle)
    {
        _path = path;
        _handle = handle;
    }

    // Release acilis yolu: open job -> header -> index (hepsi worker; bloklamaz).
    public static async Task<PakSource> OpenAsync(string pakPath)
    {
        var open = await AsyncJobs.Await(NativeFs.Open(pakPath));
        if (!open.Ok)
            return null;
        int h = open.W;
        var header = await AsyncJobs.ReadAsync(NativeFs.Read(h, null, 0, HeaderSize, HeaderSize));
        if (header == null || header.Length < HeaderSize || ReadI32(header, 0) != Magic)
        {
            NativeFs.Close(h);
            throw new InvalidOperationException("pak magic uyusmuyor (eski pak? yeniden build edin): " + pakPath);
        }
        int count = ReadI32(header, 4), indexLen = ReadI32(header, 8);
        var index = await AsyncJobs.ReadAsync(NativeFs.Read(h, null, HeaderSize, indexLen, indexLen));
        if (index == null)
        {
            NativeFs.Close(h);
            return null;
        }
        var pak = new PakSource(pakPath, h);
        pak.ParseIndex(index, count);
        return pak;
    }

    void ParseIndex(byte[] b, int count)
    {
        int p = 0;
        for (int i = 0; i < count; i++)
        {
            string key = ReadStr(b, ref p);
            var e = new Entry { Guid = ReadStr(b, ref p) };
            e.Offset = ReadI64(b, ref p);
            e.StoredLength = ReadI64(b, ref p);
            e.RawLength = ReadI64(b, ref p);
            int nDep = b[p] | (b[p + 1] << 8);
            p += 2;
            e.Deps = nDep == 0 ? Array.Empty<string>() : new string[nDep];
            for (int d = 0; d < nDep; d++)
                e.Deps[d] = ReadStr(b, ref p);
            _entries[key] = e;
        }
    }

    static int ReadI32(byte[] b, int p) => b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);

    static long ReadI64(byte[] b, ref int p)
    {
        long v = (uint)ReadI32(b, p) | ((long)ReadI32(b, p + 4) << 32);
        p += 8;
        return v;
    }

    static string ReadStr(byte[] b, ref int p)
    {
        int len = b[p] | (b[p + 1] << 8);
        p += 2;
        var s = Encoding.UTF8.GetString(b, p, len);
        p += len;
        return s;
    }

    public int Count => _entries.Count;

    public override int StartLoad(string key)
        => _entries.TryGetValue(key, out var e) && e.StoredLength > 0 // bos stub (atlas uyesi) = yuklenemez
            ? NativeFs.Decode(_handle, _handle < 0 ? _path : null, e.Offset, e.StoredLength, e.RawLength) : -1;

    public override Task<byte[]> ReadBytesAsync(string key)
    {
        if (!_entries.TryGetValue(key, out var e))
        {
            var tcs = new TaskCompletionSource<byte[]>();
            tcs.TrySetResult(null);
            return tcs.Task;
        }
        if (e.StoredLength == 0)
        {
            var tcs = new TaskCompletionSource<byte[]>();
            tcs.TrySetResult(Array.Empty<byte>());
            return tcs.Task;
        }
        return AsyncJobs.ReadAsync(NativeFs.Read(_handle, _handle < 0 ? _path : null, e.Offset, e.StoredLength, e.RawLength));
    }

#if DE_EDITOR
    // Editor/verify: senkron acma (FileStream) â€” native handle kullanilmaz.
    public PakSource(string pakPath)
    {
        _path = Path.GetFullPath(pakPath);
        _handle = -1;
        using var fs = File.OpenRead(_path);
        var header = new byte[HeaderSize];
        fs.ReadExactly(header);
        if (ReadI32(header, 0) != Magic)
            throw new InvalidDataException("pak magic uyusmuyor: " + pakPath);
        int count = ReadI32(header, 4), indexLen = ReadI32(header, 8);
        var index = new byte[indexLen];
        fs.ReadExactly(index);
        ParseIndex(index, count);
    }

    public override byte[] ReadBytes(string key)
    {
        if (!_entries.TryGetValue(key, out var e))
            return null;
        var buf = new byte[e.StoredLength];
        using (var fs = File.OpenRead(_path))
        {
            fs.Position = e.Offset;
            fs.ReadExactly(buf);
        }
        if (e.RawLength == e.StoredLength)
            return buf;
        var raw = new byte[e.RawLength];
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(buf),
            System.IO.Compression.CompressionMode.Decompress);
        z.ReadExactly(raw);
        return raw;
    }
#endif

    public override bool Exists(string key) => _entries.ContainsKey(key);

    public override string[] GetDependencies(string key)
        => _entries.TryGetValue(key, out var e) ? e.Deps : Array.Empty<string>();

    public override bool TryGetRange(string key, out string path, out long offset, out long length)
    {
        path = _path;
        offset = length = 0;
        if (!_entries.TryGetValue(key, out var e) || e.StoredLength <= 0 || e.RawLength != e.StoredLength)
            return false; // yok / bos stub / zlib'li (akitilamaz)
        offset = e.Offset;
        length = e.StoredLength;
        return true;
    }

    public override IEnumerable<string> Keys => _entries.Keys;
    // Girisin bagimlilik kenarlari (index'ten); yoksa null. Pak'i yeniden paketleyen araclar (modul testi) icin.
    public string[] DepsOf(string key) => _entries.TryGetValue(key, out var e) ? e.Deps : null;
    public string GuidOf(string key) => _entries.TryGetValue(key, out var e) ? e.Guid : null;

    public override void FillGuidTable(
        Dictionary<string, string> guidToPath, Dictionary<string, string> pathToGuid)
    {
        foreach (var kv in _entries)
        {
            if (string.IsNullOrEmpty(kv.Value.Guid))
                continue;
            guidToPath[kv.Value.Guid] = kv.Key;
            pathToGuid[kv.Key] = kv.Value.Guid;
        }
    }
}

#if DE_EDITOR
// Pak yazici (build adimi kullanir; format okuyucuyla ayni dosyada dursun).
public static class PakWriter
{
    // items: (key, guid, icerik). Icerik cagiran tarafta hazirlanir (texture'lar
    // build'de DTEX'e cevrilir). Her giris zlib'lenir; kazanc yoksa ham saklanir.
    // forceStore(key)=true: giris HER ZAMAN ham (akitilacak muzik gibi â€” native
    // player dosyadan offset'le okur, zlib olamaz). Donus: (rawToplam, pakBoyu).
    // depsOf(key): girisin bagimlilik kenarlari (null = yok).
    public static (long RawTotal, long PakSize) Write(
        string outPath, IReadOnlyList<(string Key, string Guid, byte[] Data)> items,
        Func<string, bool> forceStore = null, Func<string, IReadOnlyList<string>> depsOf = null)
    {
        var blobs = new byte[items.Count][];
        long rawTotal = 0;
        for (int i = 0; i < items.Count; i++)
        {
            rawTotal += items[i].Data.Length;
            if (forceStore != null && forceStore(items[i].Key))
            {
                blobs[i] = items[i].Data;
                continue;
            }
            var packed = Compress(items[i].Data);
            blobs[i] = packed.Length < items[i].Data.Length ? packed : items[i].Data;
        }

        var index = BuildIndex(items, blobs, dataStart: 0, depsOf); // uzunluk offset'ten bagimsiz
        long dataStart = PakSource.HeaderSize + index.Length;
        index = BuildIndex(items, blobs, dataStart, depsOf);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using var fs = File.Create(outPath);
        using (var w = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(PakSource.Magic);
            w.Write(items.Count);
            w.Write(index.Length);
        }
        fs.Write(index);
        foreach (var b in blobs)
            fs.Write(b);
        return (rawTotal, fs.Length);
    }

    static byte[] Compress(byte[] raw)
    {
        using var ms = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(ms,
            System.IO.Compression.CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw);
        return ms.ToArray();
    }

    // Index girisi: u16 len + utf8 (BinaryWriter'in 7-bit uzunlugu DEGIL — runtime
    // okuyucu bagimliliksiz, SceneBinary ile ayni string bicimi).
    static byte[] BuildIndex(IReadOnlyList<(string Key, string Guid, byte[] Data)> items,
        byte[][] blobs, long dataStart, Func<string, IReadOnlyList<string>> depsOf)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        long off = dataStart;
        for (int i = 0; i < items.Count; i++)
        {
            WriteStr(w, items[i].Key);
            WriteStr(w, items[i].Guid ?? "");
            w.Write(off);
            w.Write((long)blobs[i].Length);
            w.Write((long)items[i].Data.Length);
            var deps = depsOf?.Invoke(items[i].Key);
            w.Write((ushort)(deps?.Count ?? 0));
            if (deps != null)
                foreach (var d in deps)
                    WriteStr(w, d);
            off += blobs[i].Length;
        }
        w.Flush();
        return ms.ToArray();
    }

    static void WriteStr(BinaryWriter w, string s)
    {
        var b = Encoding.UTF8.GetBytes(s);
        if (b.Length > ushort.MaxValue)
            throw new InvalidOperationException("pak anahtari cok uzun: " + s);
        w.Write((ushort)b.Length);
        w.Write(b);
    }
}

// DTEX: pak'ta decode'suz texture blobu â€” [magic][format][w][h][veri].
// Build PNG'yi bir kez cozer, runtime yalniz defilter+upload yapar (stbi yolu atlanir).
// format 0=RGBA8 duz; 1=RGBA8 PNG-tarzi satir filtreli (satir basi 1 filtre baytÄ± +
// stride bayt) â€” zlib'e PNG'ye yakin sikisma kazandirir, defilter lineer/ucuz.
// Satir 0 altta (GL yonelimi). ASTC/BCn ileride yeni format degeri.
public static class DtexFormat
{
    public const int Magic = 0x58455444; // "DTEX"
    public const int Rgba8 = 0;
    public const int Rgba8Filtered = 1;
    public const int HeaderSize = 16;

    public static byte[] Build(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var b = new byte[HeaderSize + rgba.Length];
        WriteHeader(b, Rgba8, width, height);
        rgba.CopyTo(b.AsSpan(HeaderSize));
        return b;
    }

    // PNG filtre heuristigi: satir basina None/Sub/Up/Average/Paeth denenir,
    // mutlak toplami en kucuk olan yazilir (libpng min-sum yaklasimi).
    public static byte[] BuildFiltered(int width, int height, ReadOnlySpan<byte> rgba)
    {
        int stride = width * 4;
        var b = new byte[HeaderSize + height * (1 + stride)];
        WriteHeader(b, Rgba8Filtered, width, height);
        var cand = new byte[5][];
        for (int f = 0; f < 5; f++)
            cand[f] = new byte[stride];
        int o = HeaderSize;
        for (int y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * stride, stride);
            var prev = y > 0 ? rgba.Slice((y - 1) * stride, stride) : ReadOnlySpan<byte>.Empty;
            int bestF = 0;
            long bestSum = long.MaxValue;
            for (int f = 0; f < 5; f++)
            {
                var dst = cand[f];
                long sum = 0;
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= 4 ? row[x - 4] : 0;
                    int up = y > 0 ? prev[x] : 0;
                    int ul = y > 0 && x >= 4 ? prev[x - 4] : 0;
                    int pred = f switch
                    {
                        1 => left,
                        2 => up,
                        3 => (left + up) >> 1,
                        4 => Paeth(left, up, ul),
                        _ => 0,
                    };
                    byte v = (byte)(row[x] - pred);
                    dst[x] = v;
                    sum += v < 128 ? v : 256 - v; // signed mutlak deger
                }
                if (sum < bestSum)
                {
                    bestSum = sum;
                    bestF = f;
                }
            }
            b[o++] = (byte)bestF;
            cand[bestF].CopyTo(b, o);
            o += stride;
        }
        return b;
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static void WriteHeader(byte[] b, int format, int width, int height)
    {
        BitConverter.GetBytes(Magic).CopyTo(b, 0);
        BitConverter.GetBytes(format).CopyTo(b, 4);
        BitConverter.GetBytes(width).CopyTo(b, 8);
        BitConverter.GetBytes(height).CopyTo(b, 12);
    }
}
#endif
