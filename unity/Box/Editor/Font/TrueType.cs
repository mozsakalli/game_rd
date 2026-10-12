using System;

namespace Digitoy.Box.Editor
{
    // stb_truetype.h alt kumesi (TrueType 'glyf' yolu; CFF/OTF yok). Deger/yuvarlama davranisi birebir:
    // koordinatlar int16'ya kirpilir, bilesik glyph donusumu float'ta yapilip short'a kirpilir.
    public sealed class TrueTypeFont
    {
        public const byte VMove = 1, VLine = 2, VCurve = 3, VCubic = 4;

        public struct Vertex
        {
            public byte type;
            public short x, y, cx, cy, cx1, cy1;
        }

        readonly byte[] data;
        readonly int fontstart;
        int loca, head, glyf, hhea, hmtx, kern, gpos;
        int numGlyphs, indexMap, indexToLocFormat;

        public bool HasGlyf => glyf != 0;

        TrueTypeFont(byte[] d, int start) { data = d; fontstart = start; }

        byte U8(int o) => data[o];
        sbyte S8(int o) => (sbyte)data[o];
        ushort U16(int o) => (ushort)(data[o] * 256 + data[o + 1]);
        short S16(int o) => (short)(data[o] * 256 + data[o + 1]);
        uint U32(int o) => (uint)((data[o] << 24) + (data[o + 1] << 16) + (data[o + 2] << 8) + data[o + 3]);

        bool Tag(int o, string tag)
            => data[o] == tag[0] && data[o + 1] == tag[1] && data[o + 2] == tag[2] && data[o + 3] == tag[3];

        uint FindTable(string tag)
        {
            int numTables = U16(fontstart + 4);
            int tabledir = fontstart + 12;
            for (int i = 0; i < numTables; ++i)
            {
                int loc = tabledir + 16 * i;
                if (Tag(loc, tag))
                    return U32(loc + 8);
            }
            return 0;
        }

        public static TrueTypeFont Open(byte[] ttf, int fontstart = 0)
        {
            var f = new TrueTypeFont(ttf, fontstart);
            return f.Init() ? f : null;
        }

        bool Init()
        {
            uint cmap = FindTable("cmap");
            loca = (int)FindTable("loca");
            head = (int)FindTable("head");
            glyf = (int)FindTable("glyf");
            hhea = (int)FindTable("hhea");
            hmtx = (int)FindTable("hmtx");
            kern = (int)FindTable("kern");
            gpos = (int)FindTable("GPOS");
            if (cmap == 0 || head == 0 || hhea == 0 || hmtx == 0)
                return false;
            if (glyf != 0)
            {
                if (loca == 0)
                    return false;
            }
            else
                return false; // CFF desteklenmiyor (stbtt destekler; port kapsam disi)

            uint t = FindTable("maxp");
            numGlyphs = t != 0 ? U16((int)t + 4) : 0xffff;

            int numTables = U16((int)cmap + 2);
            indexMap = 0;
            for (int i = 0; i < numTables; ++i)
            {
                int encodingRecord = (int)cmap + 4 + 8 * i;
                switch (U16(encodingRecord))
                {
                    case 3: // MICROSOFT
                        switch (U16(encodingRecord + 2))
                        {
                            case 1:
                            case 10: // UNICODE_BMP / UNICODE_FULL
                                indexMap = (int)(cmap + U32(encodingRecord + 4));
                                break;
                        }
                        break;
                    case 0: // UNICODE
                        indexMap = (int)(cmap + U32(encodingRecord + 4));
                        break;
                }
            }
            if (indexMap == 0)
                return false;
            indexToLocFormat = U16(head + 50);
            return true;
        }

        public int FindGlyphIndex(int unicodeCodepoint)
        {
            int format = U16(indexMap);
            if (format == 0)
            {
                int bytes = U16(indexMap + 2);
                if (unicodeCodepoint < bytes - 6)
                    return U8(indexMap + 6 + unicodeCodepoint);
                return 0;
            }
            else if (format == 6)
            {
                uint first = U16(indexMap + 6);
                uint count = U16(indexMap + 8);
                if ((uint)unicodeCodepoint >= first && (uint)unicodeCodepoint < first + count)
                    return U16(indexMap + 10 + (unicodeCodepoint - (int)first) * 2);
                return 0;
            }
            else if (format == 2)
                return 0;
            else if (format == 4)
            {
                int segcount = U16(indexMap + 6) >> 1;
                int searchRange = U16(indexMap + 8) >> 1;
                int entrySelector = U16(indexMap + 10);
                int rangeShift = U16(indexMap + 12) >> 1;
                int endCount = indexMap + 14;
                int search = endCount;
                if (unicodeCodepoint > 0xffff)
                    return 0;
                if (unicodeCodepoint >= U16(search + rangeShift * 2))
                    search += rangeShift * 2;
                search -= 2;
                while (entrySelector != 0)
                {
                    searchRange >>= 1;
                    int end = U16(search + searchRange * 2);
                    if (unicodeCodepoint > end)
                        search += searchRange * 2;
                    --entrySelector;
                }
                search += 2;
                {
                    int item = (ushort)((search - endCount) >> 1);
                    int start = U16(indexMap + 14 + segcount * 2 + 2 + 2 * item);
                    int last = U16(endCount + 2 * item);
                    if (unicodeCodepoint < start || unicodeCodepoint > last)
                        return 0;
                    int offset = U16(indexMap + 14 + segcount * 6 + 2 + 2 * item);
                    if (offset == 0)
                        return (ushort)(unicodeCodepoint + S16(indexMap + 14 + segcount * 4 + 2 + 2 * item));
                    return U16(offset + (unicodeCodepoint - start) * 2 + indexMap + 14 + segcount * 6 + 2 + 2 * item);
                }
            }
            else if (format == 12 || format == 13)
            {
                uint ngroups = U32(indexMap + 12);
                int low = 0, high = (int)ngroups;
                while (low < high)
                {
                    int mid = low + ((high - low) >> 1);
                    uint startChar = U32(indexMap + 16 + mid * 12);
                    uint endChar = U32(indexMap + 16 + mid * 12 + 4);
                    if ((uint)unicodeCodepoint < startChar)
                        high = mid;
                    else if ((uint)unicodeCodepoint > endChar)
                        low = mid + 1;
                    else
                    {
                        uint startGlyph = U32(indexMap + 16 + mid * 12 + 8);
                        if (format == 12)
                            return (int)(startGlyph + (uint)unicodeCodepoint - startChar);
                        return (int)startGlyph;
                    }
                }
                return 0;
            }
            return 0;
        }

        int GlyfOffset(int glyphIndex)
        {
            if (glyphIndex >= numGlyphs) return -1;
            if (indexToLocFormat >= 2) return -1;
            int g1, g2;
            if (indexToLocFormat == 0)
            {
                g1 = glyf + U16(loca + glyphIndex * 2) * 2;
                g2 = glyf + U16(loca + glyphIndex * 2 + 2) * 2;
            }
            else
            {
                g1 = (int)(glyf + U32(loca + glyphIndex * 4));
                g2 = (int)(glyf + U32(loca + glyphIndex * 4 + 4));
            }
            return g1 == g2 ? -1 : g1;
        }

        public bool GetGlyphBox(int glyphIndex, out int x0, out int y0, out int x1, out int y1)
        {
            int g = GlyfOffset(glyphIndex);
            if (g < 0) { x0 = y0 = x1 = y1 = 0; return false; }
            x0 = S16(g + 2); y0 = S16(g + 4); x1 = S16(g + 6); y1 = S16(g + 8);
            return true;
        }

        public bool IsGlyphEmpty(int glyphIndex)
        {
            int g = GlyfOffset(glyphIndex);
            if (g < 0) return true;
            return S16(g) == 0;
        }

        public void GetGlyphHMetrics(int glyphIndex, out int advanceWidth, out int leftSideBearing)
        {
            int numOfLongHorMetrics = U16(hhea + 34);
            if (glyphIndex < numOfLongHorMetrics)
            {
                advanceWidth = S16(hmtx + 4 * glyphIndex);
                leftSideBearing = S16(hmtx + 4 * glyphIndex + 2);
            }
            else
            {
                advanceWidth = S16(hmtx + 4 * (numOfLongHorMetrics - 1));
                leftSideBearing = S16(hmtx + 4 * numOfLongHorMetrics + 2 * (glyphIndex - numOfLongHorMetrics));
            }
        }

        public void GetFontVMetrics(out int ascent, out int descent, out int lineGap)
        {
            ascent = S16(hhea + 4);
            descent = S16(hhea + 6);
            lineGap = S16(hhea + 8);
        }

        public float ScaleForPixelHeight(float height)
        {
            int fheight = S16(hhea + 4) - S16(hhea + 6);
            return height / fheight;
        }

        // stbtt_GetGlyphBitmapBoxSubpixel (shift 0)
        public void GetGlyphBitmapBox(int glyph, float scaleX, float scaleY, out int ix0, out int iy0, out int ix1, out int iy1)
        {
            if (!GetGlyphBox(glyph, out int x0, out int y0, out int x1, out int y1))
            {
                ix0 = iy0 = ix1 = iy1 = 0;
                return;
            }
            ix0 = (int)Math.Floor(x0 * scaleX + 0f);
            iy0 = (int)Math.Floor(-y1 * scaleY + 0f);
            ix1 = (int)Math.Ceiling(x1 * scaleX + 0f);
            iy1 = (int)Math.Ceiling(-y0 * scaleY + 0f);
        }

        static void SetVertex(ref Vertex v, byte type, int x, int y, int cx, int cy)
        {
            v.type = type;
            v.x = (short)x; v.y = (short)y; v.cx = (short)cx; v.cy = (short)cy;
        }

        static int CloseShape(Vertex[] vertices, int numVertices, int wasOff, int startOff,
            int sx, int sy, int scx, int scy, int cx, int cy)
        {
            if (startOff != 0)
            {
                if (wasOff != 0)
                    SetVertex(ref vertices[numVertices++], VCurve, (cx + scx) >> 1, (cy + scy) >> 1, cx, cy);
                SetVertex(ref vertices[numVertices++], VCurve, sx, sy, scx, scy);
            }
            else
            {
                if (wasOff != 0)
                    SetVertex(ref vertices[numVertices++], VCurve, sx, sy, cx, cy);
                else
                    SetVertex(ref vertices[numVertices++], VLine, sx, sy, 0, 0);
            }
            return numVertices;
        }

        // stbtt__GetGlyphShapeTT
        public int GetGlyphShape(int glyphIndex, out Vertex[] pvertices)
        {
            pvertices = null;
            int g = GlyfOffset(glyphIndex);
            if (g < 0)
                return 0;
            short numberOfContours = S16(g);
            Vertex[] vertices = null;
            int numVertices = 0;

            if (numberOfContours > 0)
            {
                byte flags = 0, flagcount;
                int ins, i, j = 0, m, n, nextMove, wasOff = 0, off, startOff = 0;
                int x, y, cx, cy, sx, sy, scx, scy;
                int endPtsOfContours = g + 10;
                ins = U16(g + 10 + numberOfContours * 2);
                int points = g + 10 + numberOfContours * 2 + 2 + ins;
                n = 1 + U16(endPtsOfContours + numberOfContours * 2 - 2);
                m = n + 2 * numberOfContours;
                vertices = new Vertex[m];
                nextMove = 0;
                flagcount = 0;
                off = m - n;

                for (i = 0; i < n; ++i)
                {
                    if (flagcount == 0)
                    {
                        flags = data[points++];
                        if ((flags & 8) != 0)
                            flagcount = data[points++];
                    }
                    else
                        --flagcount;
                    vertices[off + i].type = flags;
                }

                x = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 2) != 0)
                    {
                        short dx = data[points++];
                        x += (flags & 16) != 0 ? dx : -dx;
                    }
                    else
                    {
                        if ((flags & 16) == 0)
                        {
                            x = x + (short)(data[points] * 256 + data[points + 1]);
                            points += 2;
                        }
                    }
                    vertices[off + i].x = (short)x;
                }

                y = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    if ((flags & 4) != 0)
                    {
                        short dy = data[points++];
                        y += (flags & 32) != 0 ? dy : -dy;
                    }
                    else
                    {
                        if ((flags & 32) == 0)
                        {
                            y = y + (short)(data[points] * 256 + data[points + 1]);
                            points += 2;
                        }
                    }
                    vertices[off + i].y = (short)y;
                }

                numVertices = 0;
                sx = sy = cx = cy = scx = scy = 0;
                for (i = 0; i < n; ++i)
                {
                    flags = vertices[off + i].type;
                    x = vertices[off + i].x;
                    y = vertices[off + i].y;

                    if (nextMove == i)
                    {
                        if (i != 0)
                            numVertices = CloseShape(vertices, numVertices, wasOff, startOff, sx, sy, scx, scy, cx, cy);

                        startOff = (flags & 1) == 0 ? 1 : 0;
                        if (startOff != 0)
                        {
                            scx = x;
                            scy = y;
                            if ((vertices[off + i + 1].type & 1) == 0)
                            {
                                sx = (x + vertices[off + i + 1].x) >> 1;
                                sy = (y + vertices[off + i + 1].y) >> 1;
                            }
                            else
                            {
                                sx = vertices[off + i + 1].x;
                                sy = vertices[off + i + 1].y;
                                ++i;
                            }
                        }
                        else
                        {
                            sx = x;
                            sy = y;
                        }
                        SetVertex(ref vertices[numVertices++], VMove, sx, sy, 0, 0);
                        wasOff = 0;
                        nextMove = 1 + U16(endPtsOfContours + j * 2);
                        ++j;
                    }
                    else
                    {
                        if ((flags & 1) == 0)
                        {
                            if (wasOff != 0)
                                SetVertex(ref vertices[numVertices++], VCurve, (cx + x) >> 1, (cy + y) >> 1, cx, cy);
                            cx = x;
                            cy = y;
                            wasOff = 1;
                        }
                        else
                        {
                            if (wasOff != 0)
                                SetVertex(ref vertices[numVertices++], VCurve, x, y, cx, cy);
                            else
                                SetVertex(ref vertices[numVertices++], VLine, x, y, 0, 0);
                            wasOff = 0;
                        }
                    }
                }
                numVertices = CloseShape(vertices, numVertices, wasOff, startOff, sx, sy, scx, scy, cx, cy);
            }
            else if (numberOfContours < 0)
            {
                bool more = true;
                int comp = g + 10;
                numVertices = 0;
                vertices = null;
                Span<float> mtx = stackalloc float[6];
                while (more)
                {
                    ushort flags, gidx;
                    float m, n;
                    mtx[0] = 1; mtx[1] = 0; mtx[2] = 0; mtx[3] = 1; mtx[4] = 0; mtx[5] = 0;

                    flags = (ushort)S16(comp); comp += 2;
                    gidx = (ushort)S16(comp); comp += 2;

                    if ((flags & 2) != 0)
                    {
                        if ((flags & 1) != 0)
                        {
                            mtx[4] = S16(comp); comp += 2;
                            mtx[5] = S16(comp); comp += 2;
                        }
                        else
                        {
                            mtx[4] = S8(comp); comp += 1;
                            mtx[5] = S8(comp); comp += 1;
                        }
                    }
                    if ((flags & (1 << 3)) != 0)
                    {
                        mtx[0] = mtx[3] = S16(comp) / 16384.0f; comp += 2;
                        mtx[1] = mtx[2] = 0;
                    }
                    else if ((flags & (1 << 6)) != 0)
                    {
                        mtx[0] = S16(comp) / 16384.0f; comp += 2;
                        mtx[1] = mtx[2] = 0;
                        mtx[3] = S16(comp) / 16384.0f; comp += 2;
                    }
                    else if ((flags & (1 << 7)) != 0)
                    {
                        mtx[0] = S16(comp) / 16384.0f; comp += 2;
                        mtx[1] = S16(comp) / 16384.0f; comp += 2;
                        mtx[2] = S16(comp) / 16384.0f; comp += 2;
                        mtx[3] = S16(comp) / 16384.0f; comp += 2;
                    }

                    m = (float)Math.Sqrt(mtx[0] * mtx[0] + mtx[1] * mtx[1]);
                    n = (float)Math.Sqrt(mtx[2] * mtx[2] + mtx[3] * mtx[3]);

                    int compNumVerts = GetGlyphShape(gidx, out Vertex[] compVerts);
                    if (compNumVerts > 0)
                    {
                        for (int i = 0; i < compNumVerts; ++i)
                        {
                            ref Vertex v = ref compVerts[i];
                            short x, y;
                            x = v.x; y = v.y;
                            v.x = (short)(m * (mtx[0] * x + mtx[2] * y + mtx[4]));
                            v.y = (short)(n * (mtx[1] * x + mtx[3] * y + mtx[5]));
                            x = v.cx; y = v.cy;
                            v.cx = (short)(m * (mtx[0] * x + mtx[2] * y + mtx[4]));
                            v.cy = (short)(n * (mtx[1] * x + mtx[3] * y + mtx[5]));
                        }
                        var tmp = new Vertex[numVertices + compNumVerts];
                        if (numVertices > 0 && vertices != null)
                            Array.Copy(vertices, tmp, numVertices);
                        Array.Copy(compVerts, 0, tmp, numVertices, compNumVerts);
                        vertices = tmp;
                        numVertices += compNumVerts;
                    }
                    more = (flags & (1 << 5)) != 0;
                }
            }

            pvertices = vertices;
            return numVertices;
        }

        // --- kerning: GPOS varsa yalniz GPOS, yoksa kern (stbtt_GetGlyphKernAdvance) ---

        int KernInfoAdvance(int glyph1, int glyph2)
        {
            if (kern == 0) return 0;
            int d = kern;
            if (U16(d + 2) < 1) return 0;
            if (U16(d + 8) != 1) return 0;
            int l = 0, r = U16(d + 10) - 1;
            uint needle = (uint)(glyph1 << 16 | glyph2);
            while (l <= r)
            {
                int m = (l + r) >> 1;
                uint straw = U32(d + 18 + m * 6);
                if (needle < straw) r = m - 1;
                else if (needle > straw) l = m + 1;
                else return S16(d + 22 + m * 6);
            }
            return 0;
        }

        int CoverageIndex(int coverageTable, int glyph)
        {
            int coverageFormat = U16(coverageTable);
            switch (coverageFormat)
            {
                case 1:
                    {
                        int glyphCount = U16(coverageTable + 2);
                        int l = 0, r = glyphCount - 1;
                        while (l <= r)
                        {
                            int glyphArray = coverageTable + 4;
                            int m = (l + r) >> 1;
                            int straw = U16(glyphArray + 2 * m);
                            if (glyph < straw) r = m - 1;
                            else if (glyph > straw) l = m + 1;
                            else return m;
                        }
                        break;
                    }
                case 2:
                    {
                        int rangeCount = U16(coverageTable + 2);
                        int rangeArray = coverageTable + 4;
                        int l = 0, r = rangeCount - 1;
                        while (l <= r)
                        {
                            int m = (l + r) >> 1;
                            int rangeRecord = rangeArray + 6 * m;
                            int strawStart = U16(rangeRecord);
                            int strawEnd = U16(rangeRecord + 2);
                            if (glyph < strawStart) r = m - 1;
                            else if (glyph > strawEnd) l = m + 1;
                            else
                            {
                                int startCoverageIndex = U16(rangeRecord + 4);
                                return startCoverageIndex + glyph - strawStart;
                            }
                        }
                        break;
                    }
                default:
                    return -1;
            }
            return -1;
        }

        int GlyphClass(int classDefTable, int glyph)
        {
            int classDefFormat = U16(classDefTable);
            switch (classDefFormat)
            {
                case 1:
                    {
                        int startGlyphID = U16(classDefTable + 2);
                        int glyphCount = U16(classDefTable + 4);
                        int classDef1ValueArray = classDefTable + 6;
                        if (glyph >= startGlyphID && glyph < startGlyphID + glyphCount)
                            return U16(classDef1ValueArray + 2 * (glyph - startGlyphID));
                        break;
                    }
                case 2:
                    {
                        int classRangeCount = U16(classDefTable + 2);
                        int classRangeRecords = classDefTable + 4;
                        int l = 0, r = classRangeCount - 1;
                        while (l <= r)
                        {
                            int m = (l + r) >> 1;
                            int rec = classRangeRecords + 6 * m;
                            int strawStart = U16(rec);
                            int strawEnd = U16(rec + 2);
                            if (glyph < strawStart) r = m - 1;
                            else if (glyph > strawEnd) l = m + 1;
                            else return U16(rec + 4);
                        }
                        break;
                    }
                default:
                    return -1;
            }
            return 0;
        }

        int GposInfoAdvance(int glyph1, int glyph2)
        {
            if (gpos == 0) return 0;
            int d = gpos;
            if (U16(d) != 1) return 0;
            if (U16(d + 2) != 0) return 0;
            int lookupList = d + U16(d + 8);
            int lookupCount = U16(lookupList);
            for (int i = 0; i < lookupCount; ++i)
            {
                int lookupTable = lookupList + U16(lookupList + 2 + 2 * i);
                int lookupType = U16(lookupTable);
                int subTableCount = U16(lookupTable + 4);
                int subTableOffsets = lookupTable + 6;
                if (lookupType != 2)
                    continue;
                for (int sti = 0; sti < subTableCount; sti++)
                {
                    int table = lookupTable + U16(subTableOffsets + 2 * sti);
                    int posFormat = U16(table);
                    int coverageOffset = U16(table + 2);
                    int coverageIndex = CoverageIndex(table + coverageOffset, glyph1);
                    if (coverageIndex == -1)
                        continue;
                    switch (posFormat)
                    {
                        case 1:
                            {
                                int valueFormat1 = U16(table + 4);
                                int valueFormat2 = U16(table + 6);
                                if (valueFormat1 == 4 && valueFormat2 == 0)
                                {
                                    int valueRecordPairSizeInBytes = 2;
                                    int pairSetCount = U16(table + 8);
                                    int pairPosOffset = U16(table + 10 + 2 * coverageIndex);
                                    int pairValueTable = table + pairPosOffset;
                                    int pairValueCount = U16(pairValueTable);
                                    int pairValueArray = pairValueTable + 2;
                                    if (coverageIndex >= pairSetCount)
                                        return 0;
                                    int l = 0, r = pairValueCount - 1;
                                    while (l <= r)
                                    {
                                        int m = (l + r) >> 1;
                                        int pairValue = pairValueArray + (2 + valueRecordPairSizeInBytes) * m;
                                        int secondGlyph = U16(pairValue);
                                        if (glyph2 < secondGlyph) r = m - 1;
                                        else if (glyph2 > secondGlyph) l = m + 1;
                                        else return S16(pairValue + 2);
                                    }
                                }
                                else
                                    return 0;
                                break;
                            }
                        case 2:
                            {
                                int valueFormat1 = U16(table + 4);
                                int valueFormat2 = U16(table + 6);
                                if (valueFormat1 == 4 && valueFormat2 == 0)
                                {
                                    int classDef1Offset = U16(table + 8);
                                    int classDef2Offset = U16(table + 10);
                                    int glyph1class = GlyphClass(table + classDef1Offset, glyph1);
                                    int glyph2class = GlyphClass(table + classDef2Offset, glyph2);
                                    int class1Count = U16(table + 12);
                                    int class2Count = U16(table + 14);
                                    if (glyph1class < 0 || glyph1class >= class1Count) return 0;
                                    if (glyph2class < 0 || glyph2class >= class2Count) return 0;
                                    int class1Records = table + 16;
                                    int class2Records = class1Records + 2 * (glyph1class * class2Count);
                                    return S16(class2Records + 2 * glyph2class);
                                }
                                else
                                    return 0;
                            }
                        default:
                            return 0;
                    }
                }
            }
            return 0;
        }

        public int GetGlyphKernAdvance(int g1, int g2)
        {
            int xAdvance = 0;
            if (gpos != 0)
                xAdvance += GposInfoAdvance(g1, g2);
            else if (kern != 0)
                xAdvance += KernInfoAdvance(g1, g2);
            return xAdvance;
        }
    }
}
