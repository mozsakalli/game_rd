using UnityEngine;

namespace Digitoy.Box
{
    /// <summary>
    /// Procedural SDF UI pieces baked into every font sheet (engine UiPieces.cs, same constants):
    /// quarter-disc corner (box radius, fwidth AA), wide-spread shadow corner (variable blur via
    /// per-vertex smoothing) and a solid white square. Layout is relative to the region origin and
    /// FIXED, so box quads compute UVs the same way whichever sheet is bound. Value mapping equals
    /// the font SDF: 0.5 = edge.
    /// </summary>
    public static class UIPieces
    {
        // Region-relative placement (px). 8px gaps between pieces (bilinear bleed).
        public const int CornerX = 0, CornerY = 0, CornerSize = 128;
        public const float CornerRadius = 88f;   // px, disc centre at the piece's (0,0)
        public const float CornerSpread = 64f;   // px width of the 0..1 value band

        public const int ShadowX = 136, ShadowY = 0, ShadowSize = 128;
        public const float ShadowRadius = 64f;
        public const float ShadowSpread = 128f;

        public const int SolidX = 272, SolidY = 0, SolidSize = 32;

        public const int RegionW = 312, RegionH = 128;

        /// <summary>SDF continuation outside each piece; bilinear at the piece border stays exact.</summary>
        public const int Apron = 4;

        /// <summary>Bump when piece content changes (importer version depends on it).</summary>
        public const int BakeVersion = 1;

        /// <summary>
        /// Bakes the region into an RGBA8 buffer (row 0 at the TOP, width <paramref name="sheetW"/>),
        /// all four channels equal so median(rgb) == a in the MTSDF shader path.
        /// </summary>
        public static void BakeRegion(Color32[] sheet, int sheetW, int ox, int oy)
        {
            int sheetH = sheet.Length / sheetW;
            BakeDisc(sheet, sheetW, sheetH, ox + CornerX, oy + CornerY, CornerSize, CornerRadius, CornerSpread);
            BakeDisc(sheet, sheetW, sheetH, ox + ShadowX, oy + ShadowY, ShadowSize, ShadowRadius, ShadowSpread);
            var white = new Color32(255, 255, 255, 255);
            for (int y = 0; y < SolidSize; y++)
                for (int x = 0; x < SolidSize; x++)
                    sheet[(oy + SolidY + y) * sheetW + ox + SolidX + x] = white;
        }

        // Quarter disc SDF: centre at the piece's (0,0) pixel, 0.5 = edge (pixel centres sampled).
        static void BakeDisc(Color32[] sheet, int sheetW, int sheetH, int px, int py, int size, float radius, float spread)
        {
            for (int y = -Apron; y < size + Apron; y++)
            {
                int ay = py + y;
                if (ay < 0 || ay >= sheetH)
                    continue;
                int row = ay * sheetW;
                for (int x = -Apron; x < size + Apron; x++)
                {
                    int ax = px + x;
                    if (ax < 0 || ax >= sheetW)
                        continue;
                    float d = Mathf.Sqrt((x + 0.5f) * (x + 0.5f) + (y + 0.5f) * (y + 0.5f));
                    float v = Mathf.Clamp01(0.5f + (radius - d) / spread);
                    byte b = (byte)(v * 255f + 0.5f);
                    sheet[row + ax] = new Color32(b, b, b, b);
                }
            }
        }
    }
}
