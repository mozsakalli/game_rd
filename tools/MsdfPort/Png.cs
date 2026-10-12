using System.IO;
using System.IO.Compression;

namespace MsdfPort;

// Minimal PNG yazici (RGB8, filtre 0) — teshis dokumleri icin.
static class Png
{
    public static byte[] Encode(byte[] rgb, int w, int h)
    {
        var raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++)
        {
            raw[y * (w * 3 + 1)] = 0;
            System.Buffer.BlockCopy(rgb, y * w * 3, raw, y * (w * 3 + 1) + 1, w * 3);
        }
        byte[] z;
        using (var ms = new MemoryStream())
        {
            using (var zs = new ZLibStream(ms, CompressionLevel.Fastest, true))
                zs.Write(raw, 0, raw.Length);
            z = ms.ToArray();
        }
        using var o = new MemoryStream();
        o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, w); WriteBE(ihdr, 4, h);
        ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        Chunk(o, "IHDR", ihdr);
        Chunk(o, "IDAT", z);
        Chunk(o, "IEND", System.Array.Empty<byte>());
        return o.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4]; WriteBE(len, 0, data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t); s.Write(data);
        uint crc = Crc(t, 0xFFFFFFFF); crc = Crc(data, crc);
        var c = new byte[4]; WriteBE(c, 0, (int)(crc ^ 0xFFFFFFFF)); s.Write(c);
    }

    static void WriteBE(byte[] b, int o, int v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }

    static uint[] s_table;
    static uint Crc(byte[] d, uint crc)
    {
        if (s_table == null)
        {
            s_table = new uint[256];
            for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; s_table[n] = c; }
        }
        foreach (var b in d) crc = s_table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
