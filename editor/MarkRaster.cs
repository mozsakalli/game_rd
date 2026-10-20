using System;
using System.Collections.Generic;

namespace DigitoyEditor;

// Screenshot uzerine CPU'da "Set-of-Mark" isaretleri: her describe rect'ine ince cerceve + kosede numarali
// etiket. Model goruntuyu "3 numarali kutu" diye konusur, koordinat tahmin etmez. Yalniz RGB tampon
// uzerinde calisir; motor/GPU'ya dokunmaz. Rakamlar 3x5 bit-font (0-9), olcek goruntu boyutuna gore 1x/2x.
static class MarkRaster
{
    public struct Mark
    {
        public int N;
        public float X, Y, W, H; // goruntu pikseli (kucultulmus/kirpilmis koordinat)
    }

    // 3x5 bit-font: satir basina 3 bit, ustten alta (bit 2 = sol).
    static readonly byte[][] Digits =
    {
        new byte[] { 0b111, 0b101, 0b101, 0b101, 0b111 }, // 0
        new byte[] { 0b010, 0b110, 0b010, 0b010, 0b111 }, // 1
        new byte[] { 0b111, 0b001, 0b111, 0b100, 0b111 }, // 2
        new byte[] { 0b111, 0b001, 0b111, 0b001, 0b111 }, // 3
        new byte[] { 0b101, 0b101, 0b111, 0b001, 0b001 }, // 4
        new byte[] { 0b111, 0b100, 0b111, 0b001, 0b111 }, // 5
        new byte[] { 0b111, 0b100, 0b111, 0b101, 0b111 }, // 6
        new byte[] { 0b111, 0b001, 0b001, 0b010, 0b010 }, // 7
        new byte[] { 0b111, 0b101, 0b111, 0b101, 0b111 }, // 8
        new byte[] { 0b111, 0b101, 0b111, 0b001, 0b111 }, // 9
    };

    const byte FillR = 0xFF, FillG = 0xD4, FillB = 0x00; // sari: UI renklerinden bagimsiz yuksek kontrast
    const byte InkR = 0x00, InkG = 0x00, InkB = 0x00;

    public static void Draw(byte[] rgb, int w, int h, List<Mark> marks)
    {
        int scale = Math.Max(w, h) >= 512 ? 2 : 1;
        int pad = scale;                 // etiket ic bosluk
        int glyphW = 3 * scale, glyphH = 5 * scale, gap = scale;
        var placed = new List<(int x, int y, int w, int h)>(marks.Count);

        // Once cerceveler (etiketler uste gelsin).
        foreach (var m in marks)
            StrokeRect(rgb, w, h, m.X, m.Y, m.W, m.H, scale);

        foreach (var m in marks)
        {
            int digits = m.N < 10 ? 1 : m.N < 100 ? 2 : 3;
            int lw = pad * 2 + digits * glyphW + (digits - 1) * gap;
            int lh = pad * 2 + glyphH;
            int lx = (int)MathF.Round(m.X), ly = (int)MathF.Round(m.Y);
            // Kucuk rect'te etiket iceriyi ortmesin: dis sol-uste tasi (yer varsa).
            if (m.W < lw * 3 || m.H < lh * 3)
            {
                if (ly - lh >= 0) ly -= lh;
                else if (ly + (int)m.H + lh <= h) ly += (int)m.H;
            }
            lx = Math.Clamp(lx, 0, Math.Max(0, w - lw));
            ly = Math.Clamp(ly, 0, Math.Max(0, h - lh));
            // Ayni kosede baska etiket varsa asagi kaydir (N kucuk; O(N^2) kabul).
            for (int guard = 0; guard < 16; guard++)
            {
                bool hit = false;
                foreach (var p in placed)
                    if (lx < p.x + p.w && lx + lw > p.x && ly < p.y + p.h && ly + lh > p.y) { hit = true; ly = p.y + p.h; break; }
                if (!hit || ly + lh > h) break;
            }
            ly = Math.Clamp(ly, 0, Math.Max(0, h - lh));
            placed.Add((lx, ly, lw, lh));

            FillRect(rgb, w, h, lx, ly, lw, lh, FillR, FillG, FillB);
            int gx = lx + pad, gy = ly + pad;
            int n = m.N, div = digits == 3 ? 100 : digits == 2 ? 10 : 1;
            for (int d = 0; d < digits; d++)
            {
                int digit = (n / div) % 10;
                div /= 10;
                Glyph(rgb, w, h, gx, gy, digit, scale);
                gx += glyphW + gap;
            }
        }
    }

    static void Glyph(byte[] rgb, int w, int h, int x, int y, int digit, int scale)
    {
        var rows = Digits[digit];
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 3; c++)
                if ((rows[r] & (4 >> c)) != 0)
                    FillRect(rgb, w, h, x + c * scale, y + r * scale, scale, scale, InkR, InkG, InkB);
    }

    static void StrokeRect(byte[] rgb, int w, int h, float fx, float fy, float fw, float fh, int t)
    {
        int x0 = (int)MathF.Round(fx), y0 = (int)MathF.Round(fy);
        int x1 = (int)MathF.Round(fx + fw), y1 = (int)MathF.Round(fy + fh);
        FillRect(rgb, w, h, x0, y0, x1 - x0, t, FillR, FillG, FillB);
        FillRect(rgb, w, h, x0, y1 - t, x1 - x0, t, FillR, FillG, FillB);
        FillRect(rgb, w, h, x0, y0, t, y1 - y0, FillR, FillG, FillB);
        FillRect(rgb, w, h, x1 - t, y0, t, y1 - y0, FillR, FillG, FillB);
    }

    static void FillRect(byte[] rgb, int w, int h, int x, int y, int rw, int rh, byte r, byte g, byte b)
    {
        int xa = Math.Max(0, x), ya = Math.Max(0, y);
        int xb = Math.Min(w, x + rw), yb = Math.Min(h, y + rh);
        for (int yy = ya; yy < yb; yy++)
        {
            int o = (yy * w + xa) * 3;
            for (int xx = xa; xx < xb; xx++, o += 3)
            {
                rgb[o] = r; rgb[o + 1] = g; rgb[o + 2] = b;
            }
        }
    }
}
