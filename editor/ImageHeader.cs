using System;
using System.IO;

namespace DigitoyEditor;

// Gorsel boyutunu DOSYA BASLIGINDAN okur (PNG IHDR / JPEG SOFn / DVEC vektor): piksel decode ve
// GPU yuklemesi yok, senkron ve ucuz. Editor tooling (asset.info RPC, inspector)
// sahnede hic kullanilmamis bir png'nin boyutunu bununla ogrenir.
public static class ImageHeader
{
    public static bool TryReadSize(string file, out int width, out int height)
    {
        width = height = 0;
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> head = stackalloc byte[26];
            int n = fs.Read(head);
            if (n >= 24 && head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G')
            {
                width = BE32(head.Slice(16));
                height = BE32(head.Slice(20));
                return width > 0 && height > 0;
            }
            if (n >= 4 && head[0] == 0xFF && head[1] == 0xD8)
                return ReadJpeg(fs, out width, out height);
            if (n >= 12 && head[0] == 'D' && head[1] == 'V' && head[2] == 'E' && head[3] == 'C')
            {
                // DVEC vektor blob'u (de_vector.h): u16 LE w/h @8.
                width = head[8] | (head[9] << 8);
                height = head[10] | (head[11] << 8);
                return width > 0 && height > 0;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        return false;
    }

    // JPEG: segmentleri atlayarak ilk SOF (C0..CF, C4/C8/CC haric) baslayincaya kadar yuru.
    static bool ReadJpeg(FileStream fs, out int width, out int height)
    {
        width = height = 0;
        fs.Position = 2;
        Span<byte> buf = stackalloc byte[9];
        for (int guard = 0; guard < 4096; guard++)
        {
            int b = fs.ReadByte();
            if (b < 0)
                return false;
            if (b != 0xFF)
                continue;
            int marker = fs.ReadByte();
            while (marker == 0xFF)
                marker = fs.ReadByte();
            if (marker < 0)
                return false;
            if (marker == 0xD8 || marker == 0x01 || (marker >= 0xD0 && marker <= 0xD7))
                continue; // boyutsuz marker
            if (marker == 0xD9 || marker == 0xDA)
                return false; // EOI / SOS: SOF gelmedi
            if (fs.Read(buf.Slice(0, 2)) != 2)
                return false;
            int len = (buf[0] << 8) | buf[1];
            bool sof = marker >= 0xC0 && marker <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
            if (sof)
            {
                if (fs.Read(buf.Slice(0, 5)) != 5)
                    return false;
                height = (buf[1] << 8) | buf[2];
                width = (buf[3] << 8) | buf[4];
                return width > 0 && height > 0;
            }
            fs.Position += len - 2;
        }
        return false;
    }

    static int BE32(ReadOnlySpan<byte> s) => (s[0] << 24) | (s[1] << 16) | (s[2] << 8) | s[3];
}
