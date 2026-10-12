using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace Digitoy.Box.Editor
{
    /// <summary>
    /// <c>.uiboxatlas</c> (empty marker file; settings live in this importer) → <see cref="UIBoxAtlas"/> main asset +
    /// page textures + <see cref="UIFont"/>s as sub-assets. Every listed TTF is baked to MTSDF (pure C# msdfgen,
    /// byte-identical to the engine) and packed with the <see cref="UIPieces"/> band; a font never splits across
    /// pages. Re-imports automatically when a source TTF changes (DependsOnSourceAsset).
    /// </summary>
    [ScriptedImporter(1, Extension)]
    public sealed class UIBoxAtlasImporter : ScriptedImporter
    {
        public const string Extension = "uiboxatlas";
        public const string DefaultCharset = "32-126,160-383";

        // Engine FontImporter / sokol_shim bake constants.
        const int BakePad = 6;
        const float BakePixelDistScale = UIFont.SdfSpread;
        const double MsdfAngleThreshold = 3.0;
        const int GlyphPad = 1;
        const int PiecesMargin = 4;    // UIPieces.Apron spills this far outside the band
        const int PiecesPad = 2;

        [Serializable]
        public sealed class FontEntry
        {
            [Tooltip("TrueType (.ttf) asset. OTF/CFF outlines are not supported.")]
            public Font font;
            [Tooltip("Sub-asset name UIBox references; empty = file name.")]
            public string name = "";
            [Range(8, 128)] public int sdfSize = 32;
            [Tooltip("Comma list: \"32-126\", \"U+00A0-U+017F\", \"U+20AC\" or literal characters. ' ' and '?' always included.")]
            public string charset = DefaultCharset;
        }

        public List<FontEntry> fonts = new List<FontEntry>();
        [Tooltip("Pages are power-of-two up to this size; a font that does not fit the current page opens a new one.")]
        public int maxPageSize = 4096;

        sealed class BakedFont
        {
            public FontEntry entry;
            public string name;
            public MsdfFontBaker baker;
            public List<int> codepoints;
            public UIFont.Glyph[] glyphs;
            public byte[][] bitmaps;
            public float[] metrics;
            public int page;
        }

        sealed class Page
        {
            public readonly List<SheetPacker.Item> items = new List<SheetPacker.Item>();
            public SheetPacker.Item[] packed;
            public int w, h;
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var atlas = ScriptableObject.CreateInstance<UIBoxAtlas>();
            atlas.name = Path.GetFileNameWithoutExtension(ctx.assetPath);
            ctx.AddObjectToAsset("atlas", atlas);
            ctx.SetMainObject(atlas);

            int maxSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.Max(maxPageSize, 64)), 64, SheetPacker.MaxSize);

            // 1) bake every font
            var baked = new List<BakedFont>();
            var usedNames = new HashSet<string>();
            foreach (var e in fonts)
            {
                if (e == null || e.font == null)
                    continue;
                string path = AssetDatabase.GetAssetPath(e.font);
                if (string.IsNullOrEmpty(path)) { ctx.LogImportError($"[UIBoxAtlas] font has no asset path: {e.font.name}"); continue; }
                ctx.DependsOnSourceAsset(path);
                byte[] ttf;
                try { ttf = File.ReadAllBytes(path); }
                catch (Exception ex) { ctx.LogImportError($"[UIBoxAtlas] cannot read {path}: {ex.Message}"); continue; }
                MsdfFontBaker baker;
                try { baker = new MsdfFontBaker(ttf); }
                catch (Exception ex) { ctx.LogImportError($"[UIBoxAtlas] {Path.GetFileName(path)}: {ex.Message} (TrueType 'glyf' outlines required)"); continue; }

                var name = string.IsNullOrWhiteSpace(e.name) ? Path.GetFileNameWithoutExtension(path) : e.name.Trim();
                if (!usedNames.Add(name)) { ctx.LogImportError($"[UIBoxAtlas] duplicate font name '{name}'"); continue; }

                var cps = ParseCharset(e.charset);
                float size = Mathf.Clamp(e.sdfSize, 8, 128);
                var bf = new BakedFont { entry = e, name = name, baker = baker, codepoints = cps, metrics = baker.Metrics(size) };
                int n = cps.Count;
                bf.glyphs = new UIFont.Glyph[n];
                bf.bitmaps = new byte[n][];
                var out5 = new float[5];
                for (int i = 0; i < n; i++)
                {
                    int cp = cps[i];
                    byte[] px = baker.Glyph(cp, size, BakePad, BakePixelDistScale, MsdfAngleThreshold, out5);
                    int w = (int)out5[3], h = (int)out5[4];
                    InkExtents(px, w, h, 4, out float inkL, out float inkR);
                    bf.glyphs[i] = new UIFont.Glyph
                    {
                        codepoint = cp,
                        advance = out5[0],
                        xOff = out5[1],
                        yOff = out5[2],
                        w = w,
                        h = h,
                        inkL = w > 0 ? out5[1] + inkL : 0f,
                        inkR = w > 0 ? out5[1] + inkR : 0f,
                    };
                    bf.bitmaps[i] = px;
                }
                baked.Add(bf);
            }

            // 2) pack: a font is a unit; try existing pages, else a new page. Tag = fontIndex*2^20 + glyphIndex; -1 = pieces.
            var pages = new List<Page>();
            var piecesItem = new SheetPacker.Item
            {
                w = UIPieces.RegionW + 2 * (PiecesMargin + PiecesPad),
                h = UIPieces.RegionH + 2 * (PiecesMargin + PiecesPad),
                tag = -1,
            };
            // Largest fonts first (better page fill).
            var order = new List<int>();
            for (int i = 0; i < baked.Count; i++) order.Add(i);
            order.Sort((a, b) => FontArea(baked[b]).CompareTo(FontArea(baked[a])));

            foreach (int fi in order)
            {
                var bf = baked[fi];
                var fontItems = new List<SheetPacker.Item>();
                for (int g = 0; g < bf.glyphs.Length; g++)
                    if (bf.glyphs[g].w > 0 && bf.glyphs[g].h > 0)
                        fontItems.Add(new SheetPacker.Item { w = (int)bf.glyphs[g].w + 2 * GlyphPad, h = (int)bf.glyphs[g].h + 2 * GlyphPad, tag = (fi << 20) | g });

                bf.page = -1;
                for (int p = 0; p < pages.Count && bf.page < 0; p++)
                    if (TryAddToPage(pages[p], fontItems, maxSize))
                        bf.page = p;
                if (bf.page < 0)
                {
                    var page = new Page();
                    page.items.Add(piecesItem);
                    if (!TryAddToPage(page, fontItems, maxSize))
                    {
                        ctx.LogImportError($"[UIBoxAtlas] font '{bf.name}' does not fit a {maxSize}² page; reduce charset or sdfSize");
                        continue;
                    }
                    pages.Add(page);
                    bf.page = pages.Count - 1;
                }
            }
            if (pages.Count == 0)
            {
                // Font-less atlas: still emit one pieces-only page so boxes can reference it later.
                var page = new Page();
                page.items.Add(piecesItem);
                TryAddToPage(page, new List<SheetPacker.Item>(), maxSize);
                pages.Add(page);
            }

            // 3) blit pages + create textures
            atlas.pages = new UIBoxAtlas.PageInfo[pages.Count];
            for (int p = 0; p < pages.Count; p++)
            {
                var page = pages[p];
                var sheet = new Color32[page.w * page.h];
                int piecesX = 0, piecesY = 0;
                foreach (var it in page.packed)
                {
                    if (it.tag < 0)
                    {
                        piecesX = it.x + PiecesPad + PiecesMargin;
                        piecesY = it.y + PiecesPad + PiecesMargin;
                        UIPieces.BakeRegion(sheet, page.w, piecesX, piecesY);
                        continue;
                    }
                    var bf = baked[it.tag >> 20];
                    int gi = it.tag & 0xFFFFF;
                    ref var g = ref bf.glyphs[gi];
                    int gx = it.x + GlyphPad, gy = it.y + GlyphPad, gw = (int)g.w, gh = (int)g.h;
                    g.x = gx;
                    g.y = gy;
                    var src = bf.bitmaps[gi];
                    for (int y = 0; y < gh; y++)
                    {
                        int s = y * gw * 4, d = (gy + y) * page.w + gx;
                        for (int x = 0; x < gw; x++, s += 4)
                            sheet[d + x] = new Color32(src[s], src[s + 1], src[s + 2], src[s + 3]);
                    }
                }
                var flipped = new Color32[sheet.Length];
                for (int y = 0; y < page.h; y++)
                    Array.Copy(sheet, y * page.w, flipped, (page.h - 1 - y) * page.w, page.w);
                var tex = new Texture2D(page.w, page.h, TextureFormat.RGBA32, false, true)
                {
                    name = $"Page{p}",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                };
                tex.SetPixels32(flipped);
                tex.Apply(false, false);   // readable KALMALI: importer serilestirirken veri gerekir
                ctx.AddObjectToAsset($"page{p}", tex);
                atlas.pages[p] = new UIBoxAtlas.PageInfo { texture = tex, width = page.w, height = page.h, piecesX = piecesX, piecesY = piecesY };
            }

            // 4) font sub-assets
            var fontAssets = new List<UIFont>();
            foreach (var bf in baked)
            {
                if (bf.page < 0 || bf.page >= pages.Count)
                    continue;
                var pi = atlas.pages[bf.page];
                var font = ScriptableObject.CreateInstance<UIFont>();
                font.name = bf.name;
                font.ascent = bf.metrics[0];
                font.descent = bf.metrics[1];
                font.lineHeight = bf.metrics[2];
                font.kernScale = bf.metrics[3];
                font.sdfSize = bf.metrics[4];
                font.channels = 4;
                font.sheet = pi.texture;
                font.sheetWidth = pi.width;
                font.sheetHeight = pi.height;
                font.page = bf.page;
                font.piecesX = pi.piecesX;
                font.piecesY = pi.piecesY;
                font.glyphs = bf.glyphs;
                font.kern = bf.baker.KernTable(bf.codepoints.ToArray());
                ctx.AddObjectToAsset("font:" + bf.name, font);
                fontAssets.Add(font);
            }
            atlas.fonts = fontAssets.ToArray();
        }

        static long FontArea(BakedFont bf)
        {
            long a = 0;
            foreach (var g in bf.glyphs)
                a += (long)(g.w + 2 * GlyphPad) * (long)(g.h + 2 * GlyphPad);
            return a;
        }

        // Sayfaya font eklemeyi dener: mevcut + yeni ogeler birlikte yeniden paketlenir; sigmazsa sayfa dokunulmaz.
        static bool TryAddToPage(Page page, List<SheetPacker.Item> fontItems, int maxSize)
        {
            var all = new SheetPacker.Item[page.items.Count + fontItems.Count];
            page.items.CopyTo(all, 0);
            fontItems.CopyTo(all, page.items.Count);
            if (!SheetPacker.Pack(all, out int w, out int h) || w > maxSize || h > maxSize)
                return false;
            page.items.AddRange(fontItems);
            page.packed = all;
            page.w = w;
            page.h = h;
            return true;
        }

        /// <summary>Visible ink left/right edge from the SDF bitmap (A = true distance), threshold 128 linear crossing.</summary>
        static void InkExtents(byte[] sdf, int w, int h, int channels, out float left, out float right)
        {
            left = 0f;
            right = w;
            if (w <= 0 || h <= 0 || sdf == null)
                return;
            int off = channels - 1;
            var colMax = new int[w];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int v = sdf[(y * w + x) * channels + off];
                    if (v > colMax[x]) colMax[x] = v;
                }
            int c0 = 0;
            while (c0 < w && colMax[c0] < 128) c0++;
            if (c0 >= w)
            {
                left = right = w * 0.5f;
                return;
            }
            int c1 = w - 1;
            while (c1 > c0 && colMax[c1] < 128) c1--;
            left = c0 > 0
                ? c0 - 0.5f + (128f - colMax[c0 - 1]) / (colMax[c0] - colMax[c0 - 1])
                : 0f;
            right = c1 < w - 1
                ? c1 + 0.5f + (colMax[c1] - 128f) / (colMax[c1] - colMax[c1 + 1])
                : w;
        }

        /// <summary>Charset text → sorted unique codepoints (' ' and '?' guaranteed).</summary>
        public static List<int> ParseCharset(string text)
        {
            var set = new SortedSet<int> { ' ', '?' };
            foreach (var raw in (text ?? "").Split(','))
            {
                string tok = raw.Trim();
                if (tok.Length == 0)
                    continue;
                int dash = tok.IndexOf('-', 1);
                if (dash > 0 && TryCodepoint(tok.Substring(0, dash), out int a) && TryCodepoint(tok.Substring(dash + 1), out int b))
                {
                    if (b < a) (a, b) = (b, a);
                    for (int c = a; c <= b && c - a < 65536; c++)
                        set.Add(c);
                    continue;
                }
                if (TryCodepoint(tok, out int single))
                {
                    set.Add(single);
                    continue;
                }
                for (int i = 0; i < tok.Length; i++)
                {
                    int c = char.IsHighSurrogate(tok[i]) && i + 1 < tok.Length
                        ? char.ConvertToUtf32(tok[i], tok[++i]) : tok[i];
                    set.Add(c);
                }
            }
            set.RemoveWhere(c => c < 0 || c > 0x10FFFF);
            return new List<int>(set);
        }

        static bool TryCodepoint(string s, out int cp)
        {
            s = s.Trim();
            if (s.StartsWith("U+", StringComparison.OrdinalIgnoreCase) || s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out cp);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out cp);
        }

        // --- olusturma menusu ---
        [MenuItem("Assets/Create/Digitoy/UIBox Atlas", false, 200)]
        static void CreateAtlas()
        {
            string dir = "Assets";
            var sel = Selection.activeObject;
            if (sel != null)
            {
                var p = AssetDatabase.GetAssetPath(sel);
                if (!string.IsNullOrEmpty(p))
                    dir = AssetDatabase.IsValidFolder(p) ? p : Path.GetDirectoryName(p).Replace('\\', '/');
            }
            var path = AssetDatabase.GenerateUniqueAssetPath($"{dir}/UIBoxAtlas.{Extension}");
            File.WriteAllText(path, "# Digitoy UIBox atlas — settings live in the importer (Inspector).\n");
            AssetDatabase.ImportAsset(path);
            Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(path);
        }
    }
}
