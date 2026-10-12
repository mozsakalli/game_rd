using System;
using System.Collections.Generic;

namespace Digitoy.Box.Editor
{
    /// <summary>Shelf packer for the font sheet (power-of-two, ≤ MaxSize). Row 0 = top. No rotation.</summary>
    public static class SheetPacker
    {
        public const int MaxSize = 4096;

        public struct Item
        {
            public int w, h;          // cell size incl. padding
            public int x, y;          // result (cell origin)
            public int tag;
        }

        /// <summary>Packs items in place; returns false if they don't fit in MaxSize².</summary>
        public static bool Pack(Item[] items, out int sheetW, out int sheetH)
        {
            sheetW = sheetH = 0;
            var order = new int[items.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (a, b) =>
            {
                int c = items[b].h.CompareTo(items[a].h);
                return c != 0 ? c : items[b].w.CompareTo(items[a].w);
            });

            long area = 0;
            int maxW = 1, maxH = 1;
            foreach (var it in items)
            {
                area += (long)it.w * it.h;
                if (it.w > maxW) maxW = it.w;
                if (it.h > maxH) maxH = it.h;
            }
            int side = 64;
            while (side < maxW || side < maxH || (long)side * side < area)
                side <<= 1;

            for (; side <= MaxSize; side <<= 1)
            {
                if (TryShelf(items, order, side, side, out int usedH))
                {
                    sheetW = side;
                    sheetH = 1;
                    while (sheetH < usedH) sheetH <<= 1;
                    return true;
                }
            }
            return false;
        }

        static bool TryShelf(Item[] items, int[] order, int w, int h, out int usedH)
        {
            int x = 0, y = 0, rowH = 0;
            usedH = 0;
            foreach (int i in order)
            {
                ref var it = ref items[i];
                if (x + it.w > w)
                {
                    x = 0;
                    y += rowH;
                    rowH = 0;
                }
                if (y + it.h > h)
                    return false;
                it.x = x;
                it.y = y;
                x += it.w;
                if (it.h > rowH) rowH = it.h;
                usedH = y + rowH;
            }
            return true;
        }
    }
}
