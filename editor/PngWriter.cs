using System;
using System.IO;
using System.IO.Compression;

namespace DigitoyEditor;

// Editor-only PNG yazici: RGBA8 -> standart PNG (IHDR / tek IDAT / IEND). Adaptif satir filtresi (None/Sub/Up/
// Average/Paeth, libpng min-sum heuristigi) + .NET ZLibStream SmallestSize (gercek zlib, seviye 9). stb_image_write'in
// deflate'i LZ77 yapmadigi icin sayfalar ~2x buyuk cikiyordu; bu yazici libpng varsayilaniyla ayni sinifta.
// flip: satirlar dosyaya TERS yazilir (stb_image yukleme flip'i bellek duzenini birebir geri getirsin diye).
public static class PngWriter
{
    static readonly uint[] _crc = BuildCrcTable();

    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, bool flip)
    {
        int stride = width * 4;
        if (rgba.Length < stride * height)
            throw new ArgumentException("png: piksel tamponu kisa");

        // Filtreli ham akis: satir basi 1 filtre bayti + stride.
        var raw = new byte[height * (1 + stride)];
        var cand = new byte[5][];
        for (int f = 0; f < 5; f++)
            cand[f] = new byte[stride];
        int o = 0;
        for (int i = 0; i < height; i++)
        {
            int y = flip ? height - 1 - i : i;
            int yPrev = flip ? y + 1 : y - 1; // dosya sirasinda bir onceki satir
            var row = rgba.Slice(y * stride, stride);
            var prev = i > 0 ? rgba.Slice(yPrev * stride, stride) : ReadOnlySpan<byte>.Empty;
            int bestF = 0;
            long bestSum = long.MaxValue;
            for (int f = 0; f < 5; f++)
            {
                var dst = cand[f];
                long sum = 0;
                for (int x = 0; x < stride; x++)
                {
                    int left = x >= 4 ? row[x - 4] : 0;
                    int up = i > 0 ? prev[x] : 0;
                    int ul = i > 0 && x >= 4 ? prev[x - 4] : 0;
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
            raw[o++] = (byte)bestF;
            cand[bestF].CopyTo(raw, o);
            o += stride;
        }

        byte[] idat;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                z.Write(raw);
            idat = ms.ToArray();
        }

        using var outMs = new MemoryStream(idat.Length + 64);
        outMs.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, width);
        WriteBE(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // color type: RGBA
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter method
        ihdr[12] = 0; // interlace
        Chunk(outMs, "IHDR", ihdr);
        Chunk(outMs, "IDAT", idat);
        Chunk(outMs, "IEND", Array.Empty<byte>());
        return outMs.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // Screenshot yolu (GameOutput): 8-bit RGB, filtresiz satirlar, hizli zlib.
    public static void WriteRgb(Stream s, int w, int h, byte[] rgb)
    {
        s.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w);
        WriteBE(ihdr, 4, h);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type RGB
        Chunk(s, "IHDR", ihdr);
        int stride = w * 3 + 1;
        var raw = new byte[stride * h];
        for (int y = 0; y < h; y++)
            Buffer.BlockCopy(rgb, y * w * 3, raw, y * stride + 1, w * 3); // raw[y*stride] = 0 (filtre None)
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(raw);
        Chunk(s, "IDAT", ms.ToArray());
        Chunk(s, "IEND", Array.Empty<byte>());
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, data.Length);
        s.Write(len);
        var t = new byte[4] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        s.Write(t);
        s.Write(data);
        uint crc = Crc(0xFFFFFFFFu, t);
        crc = Crc(crc, data) ^ 0xFFFFFFFFu;
        var c = new byte[4];
        WriteBE(c, 0, (int)crc);
        s.Write(c);
    }

    static void WriteBE(byte[] b, int o, int v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static uint Crc(uint crc, byte[] data)
    {
        foreach (byte d in data)
            crc = _crc[(crc ^ d) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
