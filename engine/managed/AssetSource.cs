using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DigitoyEngine;

// Asset BAYTLARININ nereden geldigi soyutlamasi. Editor: loose dosyalar
// (esnek, .meta/hot-reload). Release: tek pak dosyasi (tarama yok, meta yok,
// guid tablosu gomulu, native worker decode'u pak icinden offset'le okur).
public abstract class AssetSource
{
    // Async piksel decode isi baslatir (native worker). Donus: job id, dolu: -1.
    public abstract int StartLoad(string key);

    // Kucuk metin asset'leri (prefab/scene) icin senkron okuma. Yoksa null.
    public abstract byte[] ReadBytes(string key);

    public abstract bool Exists(string key);

    // Release'te guid->yol eslemesi kaynaktan gelir (ScanMetas kosmaz).
    public virtual void FillGuidTable(
        Dictionary<string, string> guidToPath, Dictionary<string, string> pathToGuid)
    { }
}

public sealed class LooseFileSource : AssetSource
{
    public readonly string Root;

    public LooseFileSource(string root) => Root = root;

    public override int StartLoad(string key) => Sokol.AssetLoad(Path.Combine(Root, key));

    public override byte[] ReadBytes(string key)
    {
        var p = Path.Combine(Root, key);
        return File.Exists(p) ? File.ReadAllBytes(p) : null;
    }

    public override bool Exists(string key) => File.Exists(Path.Combine(Root, key));
}

// Pak dosyasi: [magic][count][index: key,guid,offset,storedLen,rawLen]*[blob].
// Girisler TEK TEK zlib'lenir (rastgele erisim bozulmaz); sikismayanlar (png gibi)
// oldugu gibi saklanir (stored: rawLen==storedLen). Index acilista bir kez okunur;
// texture yuklemesi native'e (path,offset,storedLen,rawLen) gider — decode+inflate
// worker'da (native cozucu stb_image'in gomulu zlib'i, ek bagimlilik yok).
public sealed class PakSource : AssetSource
{
    public const int Magic = 0x324B5044; // "DPK2"

    struct Entry
    {
        public string Guid;
        public long Offset;
        public long StoredLength;
        public long RawLength; // == StoredLength: sikistirmasiz
    }

    readonly string _path;
    readonly FileStream _stream; // senkron ReadBytes icin acik tutulur
    readonly Dictionary<string, Entry> _entries = new();

    public PakSource(string pakPath)
    {
        _path = Path.GetFullPath(pakPath);
        _stream = File.OpenRead(_path);
        using var r = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
        if (r.ReadInt32() != Magic)
            throw new InvalidDataException("pak magic uyusmuyor: " + pakPath);
        int count = r.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string key = r.ReadString();
            var e = new Entry
            {
                Guid = r.ReadString(),
                Offset = r.ReadInt64(),
                StoredLength = r.ReadInt64(),
                RawLength = r.ReadInt64(),
            };
            _entries[key] = e;
        }
    }

    public int Count => _entries.Count;

    public override int StartLoad(string key)
        => _entries.TryGetValue(key, out var e)
            ? Sokol.AssetLoadRange(_path, e.Offset, e.StoredLength, e.RawLength) : -1;

    public override byte[] ReadBytes(string key)
    {
        if (!_entries.TryGetValue(key, out var e))
            return null;
        var buf = new byte[e.StoredLength];
        _stream.Position = e.Offset;
        _stream.ReadExactly(buf);
        if (e.RawLength == e.StoredLength)
            return buf;
        var raw = new byte[e.RawLength];
        using var z = new System.IO.Compression.ZLibStream(new MemoryStream(buf),
            System.IO.Compression.CompressionMode.Decompress);
        z.ReadExactly(raw);
        return raw;
    }

    public override bool Exists(string key) => _entries.ContainsKey(key);

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

// Pak yazici (build adimi kullanir; format okuyucuyla ayni dosyada dursun).
public static class PakWriter
{
    // items: (key, guid, icerik). Icerik cagiran tarafta hazirlanir (texture'lar
    // build'de DTEX'e cevrilir). Her giris zlib'lenir; kazanc yoksa ham saklanir.
    // Donus: (rawToplam, pakBoyu).
    public static (long RawTotal, long PakSize) Write(
        string outPath, IReadOnlyList<(string Key, string Guid, byte[] Data)> items)
    {
        var blobs = new byte[items.Count][];
        long rawTotal = 0;
        for (int i = 0; i < items.Count; i++)
        {
            rawTotal += items[i].Data.Length;
            var packed = Compress(items[i].Data);
            blobs[i] = packed.Length < items[i].Data.Length ? packed : items[i].Data;
        }

        using var ms = new MemoryStream();
        WriteIndex(ms, items, blobs, dataStart: 0);
        long dataStart = ms.Length;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
        using var fs = File.Create(outPath);
        WriteIndex(fs, items, blobs, dataStart);
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

    static void WriteIndex(Stream s, IReadOnlyList<(string Key, string Guid, byte[] Data)> items,
        byte[][] blobs, long dataStart)
    {
        using var w = new BinaryWriter(s, Encoding.UTF8, leaveOpen: true);
        w.Write(PakSource.Magic);
        w.Write(items.Count);
        long off = dataStart;
        for (int i = 0; i < items.Count; i++)
        {
            w.Write(items[i].Key);
            w.Write(items[i].Guid ?? "");
            w.Write(off);
            w.Write((long)blobs[i].Length);
            w.Write((long)items[i].Data.Length);
            off += blobs[i].Length;
        }
        w.Flush();
    }
}

// DTEX: pak'ta decode'suz texture blobu — [magic][format][w][h][veri].
// Build PNG'yi bir kez cozer, runtime yalniz defilter+upload yapar (stbi yolu atlanir).
// format 0=RGBA8 duz; 1=RGBA8 PNG-tarzi satir filtreli (satir basi 1 filtre baytı +
// stride bayt) — zlib'e PNG'ye yakin sikisma kazandirir, defilter lineer/ucuz.
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
