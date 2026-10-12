using System;
using System.IO;
using Digitoy.Box.Editor.Msdf;

namespace Digitoy.Box.Editor
{

    // msdf_shim.cpp de_msdf_font_glyph + sokol_shim.c de_sdf_font_metrics/kern_table sozlesmesi.
    public sealed class MsdfFontBaker
    {
        public readonly TrueTypeFont Font;

        public MsdfFontBaker(byte[] ttf)
        {
            Font = TrueTypeFont.Open(ttf) ?? throw new InvalidDataException("font acilamadi (TrueType glyf bekleniyor)");
        }

        // out5 = ascent, descent, lineHeight, kernScale, sdfSize
        public float[] Metrics(float sdfSize)
        {
            float scale = Font.ScaleForPixelHeight(sdfSize);
            Font.GetFontVMetrics(out int ascent, out int descent, out int lineGap);
            return new[] { scale * ascent, scale * descent, scale * (ascent - descent + lineGap), scale, sdfSize };
        }

        public short[] KernTable(int[] codepoints)
        {
            int n = codepoints.Length;
            var gi = new int[n];
            for (int i = 0; i < n; i++)
                gi[i] = Font.FindGlyphIndex(codepoints[i]);
            var outT = new short[n * n];
            for (int l = 0; l < n; l++)
                for (int r = 0; r < n; r++)
                    outT[l * n + r] = (short)Font.GetGlyphKernAdvance(gi[l], gi[r]);
            return outT;
        }

        // stbtt vertex listesi -> msdfgen Shape (de_build_shape birebir).
        static bool BuildShape(TrueTypeFont.Vertex[] v, int n, Shape shape)
        {
            if (n <= 0 || v == null)
                return false;
            Contour contour = null;
            Vector2 cur = default, start = default;
            for (int i = 0; i < n; i++)
            {
                ref readonly var p = ref v[i];
                var to = new Vector2(p.x, p.y);
                switch (p.type)
                {
                    case TrueTypeFont.VMove:
                        if (contour != null && contour.edges.Count > 0 && !(cur == start))
                            contour.AddEdge(EdgeSegment.Create(cur, start));
                        contour = shape.AddContour();
                        cur = start = to;
                        break;
                    case TrueTypeFont.VLine:
                        if (contour != null && !(cur == to))
                            contour.AddEdge(EdgeSegment.Create(cur, to));
                        cur = to;
                        break;
                    case TrueTypeFont.VCurve:
                        if (contour != null)
                            contour.AddEdge(EdgeSegment.Create(cur, new Vector2(p.cx, p.cy), to));
                        cur = to;
                        break;
                    case TrueTypeFont.VCubic:
                        if (contour != null)
                            contour.AddEdge(EdgeSegment.Create(cur, new Vector2(p.cx, p.cy), new Vector2(p.cx1, p.cy1), to));
                        cur = to;
                        break;
                }
            }
            if (contour != null && contour.edges.Count > 0 && !(cur == start))
                contour.AddEdge(EdgeSegment.Create(cur, start));
            shape.contours.RemoveAll(c => c.edges.Count == 0);
            return shape.contours.Count > 0;
        }

        // Donus: RGBA8 (satir 0 ustte) veya null (bosluk); out5 = advance, xoff, yoff, w, h.
        public byte[] Glyph(int codepoint, float sdfSize, int pad, float pixelDistScale, double angleThreshold, float[] out5)
        {
            float scale = Font.ScaleForPixelHeight(sdfSize);
            int glyphIndex = Font.FindGlyphIndex(codepoint);
            Font.GetGlyphHMetrics(glyphIndex, out int advance, out _);
            out5[0] = scale * advance;
            out5[1] = out5[2] = out5[3] = out5[4] = 0;

            if (Font.IsGlyphEmpty(glyphIndex))
                return null;

            Font.GetGlyphBitmapBox(glyphIndex, scale, scale, out int ix0, out int iy0, out int ix1, out int iy1);
            ix0 -= pad; iy0 -= pad; ix1 += pad; iy1 += pad;
            int w = ix1 - ix0, h = iy1 - iy0;
            if (w <= 0 || h <= 0)
                return null;

            int nv = Font.GetGlyphShape(glyphIndex, out var verts);
            var shape = new Shape();
            if (!BuildShape(verts, nv, shape))
                return null;
            shape.Normalize();
            shape.OrientContours();
            EdgeColoring.EdgeColoringSimple(shape, angleThreshold);

            var projection = new Projection(new Vector2(scale, scale), new Vector2(-ix0 / (double)scale, iy1 / (double)scale));
            double range = (255.0 / (double)pixelDistScale) / (double)scale;
            var xf = new SdfTransformation(projection, new DistanceMapping(range));

            float[] bmp = Generator.GenerateMtsdf(shape, w, h, xf);

            var buffer = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w * 4;
                int dst = y * w * 4;
                for (int i = 0; i < w * 4; i++)
                    buffer[dst + i] = Arith.PixelFloatToByte(bmp[src + i]);
            }
            out5[1] = ix0;
            out5[2] = iy0;
            out5[3] = w;
            out5[4] = h;
            return buffer;
        }
    }
}
