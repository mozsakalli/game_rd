using System;
using System.Collections.Generic;
using UnityEngine;

namespace Digitoy.Box
{
    /// <summary>
    /// Prebaked MTSDF font (engine Font.cs port): metrics + glyph table + kerning + the single sheet
    /// texture that also carries the <see cref="UIPieces"/> band, so text and boxes share one draw.
    /// Produced by the editor-only <c>.msdf</c> importer; runtime only reads.
    /// All pixel rects are in SHEET px with row 0 at the TOP (storage convention); convert to UV with
    /// <see cref="UvRect"/>.
    /// </summary>
    public sealed class UIFont : ScriptableObject
    {
        /// <summary>Bake constant (stbtt pixelDistScale): SDF units per px; 255/21 ≈ 12 px full range.</summary>
        public const float SdfSpread = 21f;

        [Serializable]
        public struct Glyph
        {
            public int codepoint;
            public float advance, xOff, yOff, w, h;
            /// <summary>Visible ink left/right edge relative to the pen origin (SDF px, sub-pixel). 0 when w == 0.</summary>
            public float inkL, inkR;
            /// <summary>Sheet rect (px, row 0 top). Valid when w > 0.</summary>
            public int x, y;
        }

        public float ascent;      // SDF px
        public float descent;
        public float lineHeight;
        public float kernScale;
        public float sdfSize;
        public int channels = 4;  // 4 = MTSDF (rgb msdf, a true sdf)

        public Texture2D sheet;
        public int sheetWidth, sheetHeight;
        /// <summary>Page index inside the owning <see cref="UIBoxAtlas"/>.</summary>
        public int page;
        /// <summary>UIPieces region origin on the sheet (px, row 0 top).</summary>
        public int piecesX, piecesY;

        public Glyph[] glyphs = Array.Empty<Glyph>();
        /// <summary>glyphs.Length² kerning (font units; multiply by <see cref="kernScale"/>).</summary>
        public short[] kern = Array.Empty<short>();

        // --- derived (not serialized) ---
        [NonSerialized] int[] _latin;
        [NonSerialized] Dictionary<int, int> _map;
        [NonSerialized] int _fallbackIndex;
        [NonSerialized] Glyph[] _builtFor;

        public int GlyphCount => glyphs.Length;

        void EnsureTables()
        {
            if (ReferenceEquals(_builtFor, glyphs) && _latin != null)
                return;
            var g = glyphs;
            _latin = new int[256];
            for (int i = 0; i < 256; i++) _latin[i] = -1;
            _map = null;
            _fallbackIndex = 0;
            for (int i = 0; i < g.Length; i++)
            {
                int cp = g[i].codepoint;
                if (cp < 256)
                    _latin[cp] = i;
                else
                    (_map ??= new Dictionary<int, int>())[cp] = i;
                if (cp == '?')
                    _fallbackIndex = i;
            }
            _builtFor = g;
        }

        public int GlyphIndex(char c) => GlyphIndex((int)c);

        public int GlyphIndex(int codepoint)
        {
            EnsureTables();
            if (codepoint < 256)
            {
                int i = _latin[codepoint];
                return i >= 0 ? i : _fallbackIndex;
            }
            return _map != null && _map.TryGetValue(codepoint, out int gi) ? gi : _fallbackIndex;
        }

        public ref readonly Glyph GlyphAt(int index) => ref glyphs[index];

        public float Kerning(int left, int right)
        {
            int n = glyphs.Length;
            int i = left * n + right;
            return i < kern.Length ? kern[i] * kernScale : 0f;
        }

        /// <summary>Single line width in SDF px (kerning included; no '\n').</summary>
        public float MeasureLine(string text)
        {
            float w = 0;
            int prev = -1;
            for (int i = 0; i < text.Length; i++)
            {
                int gi = GlyphIndex(text[i]);
                if (prev >= 0)
                    w += Kerning(prev, gi);
                w += glyphs[gi].advance;
                prev = gi;
            }
            return w;
        }

        /// <summary>Sheet px rect (row 0 top) → UV rect (Unity: v=0 bottom). x=u0, y=v0, z=u1, w=v1.</summary>
        public Vector4 UvRect(int px, int py, int pw, int ph)
        {
            float iw = 1f / sheetWidth, ih = 1f / sheetHeight;
            return new Vector4(px * iw, 1f - (py + ph) * ih, (px + pw) * iw, 1f - py * ih);
        }

        public Vector4 GlyphUv(in Glyph g) => UvRect(g.x, g.y, (int)g.w, (int)g.h);

        /// <summary>UV of the solid white piece centre (bilinear-safe flat sample).</summary>
        public Vector2 SolidUv()
            => new Vector2((piecesX + UIPieces.SolidX + UIPieces.SolidSize * 0.5f) / sheetWidth,
                           1f - (piecesY + UIPieces.SolidY + UIPieces.SolidSize * 0.5f) / sheetHeight);
    }
}
