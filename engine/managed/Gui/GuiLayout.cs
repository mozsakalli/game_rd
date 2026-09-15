#if DE_EDITOR
using System;

namespace DigitoyEngine.Editor;

// GUILayoutOption'in boxing'siz hali: tip + float deger struct'i.
public enum LayoutOptionType : byte
{
    Width, Height, MinWidth, MaxWidth, MinHeight, MaxHeight, ExpandWidth, ExpandHeight,
}

public readonly struct LayoutOption
{
    internal readonly LayoutOptionType Type;
    internal readonly float Value;
    internal LayoutOption(LayoutOptionType type, float value) { Type = type; Value = value; }
}

// Unity GUILayoutUtility + LayoutGroup karsiligi: iki-gecisli auto-layout.
//
// MODEL (Unity ile ayni):
//   - Layout event'inde her GetRect/BeginGroup cagrisi struct havuzuna bir
//     entry ekler; event sonunda Compute() calisir:
//     CalcWidth (bottom-up) -> SetHorizontal (top-down) ->
//     CalcHeight (bottom-up, genislik bilinir) -> SetVertical (top-down).
//   - Diger event'lerde ayni cagrilar cursor REPLAY ile cache'lenmis rect'i
//     dondurur. INVARIANT: kontrol sirasi tum event'lerde ayni olmali.
//   - Kardesler arasi margin CAKISTIRMA stack yonunde: max(oncekiAlt, sonrakiUst).
//
// ZERO-ALLOC: entry'ler int-index'li tek struct dizisinde (agac = Parent/
// FirstChild/NextSibling indeksleri); frame basina alloc yok, dizi sadece buyur.
public static class GuiLayoutUtility
{
    internal struct Entry
    {
        // Icerik olcusu (leaf: GetRect'ten; group: cocuklardan hesaplanir).
        public float MinW, MaxW, MinH, MaxH;
        public bool StretchW, StretchH;
        public bool FixedWOpt, FixedHOpt; // Width/Height option'i: stretch propagasyonunu keser
        // Kenar bosluklari (stil gelince stilden dolacak).
        public float MarginL, MarginR, MarginT, MarginB;
        // Grup alanlari.
        public bool IsGroup, IsVertical;
        public float PadL, PadR, PadT, PadB;
        public int StretchWCount, StretchHCount;
        // Agac.
        public int Parent, FirstChild, LastChild, NextSibling, Cursor;
        // Sonuc.
        public Rect Rect;
    }

    static Entry[] _entries = new Entry[1024];
    static int _count;
    static int _current;      // aktif grup indeksi
    static bool _layoutPass;  // Layout event'inde miyiz (kaydet) yoksa replay mi
    static Rect _screen;

    // Area'lar: ekrandan bagimsiz koklu layout agaclari (Unity BeginArea).
    // Icerik koordinatlari area'nin LOKAL uzayindadir (GuiClip.Push cevirir).
    static readonly int[] _areaRoots = new int[64];
    static readonly Rect[] _areaRects = new Rect[64];
    static int _areaCount;
    static readonly int[] _areaStack = new int[16]; // onceki _current saklanir (-1 = dummy area)
    static int _areaDepth;
    static int _areaCursor; // replay sirasi
    // Pass ortasinda UI yapisi degisirse (secim MouseDown'da degisti vb.) cache'te
    // olmayan kontroller DUMMY rect alir (Unity davranisi) — sonraki pass'in taze
    // Layout'u duzeltir. >0 iken GetRect default doner.
    static int _overrun;

    internal static ref Entry EntryAt(int i) => ref _entries[i];

    // Her event pass'i basinda cagrilir (GuiUtility.BeginPass'ten sonra host cagirir).
    public static void BeginPass(EventType eventType, Rect screen)
    {
        _screen = screen;
        _layoutPass = eventType == EventType.Layout;
        _areaDepth = 0;
        _areaCursor = 0;
        _overrun = 0;
        if (_layoutPass)
        {
            _count = 0;
            _areaCount = 0;
            _current = NewEntry(isGroup: true, isVertical: true, parent: -1);
        }
        else
        {
            // Replay: tum cursor'lari basa sar.
            for (int i = 0; i < _count; i++)
                _entries[i].Cursor = _entries[i].FirstChild;
            _current = 0;
        }
    }

    // Layout event'inin sonunda cagrilir: agaci ve area agaclarini coz.
    public static void EndPass(EventType eventType)
    {
        if (eventType != EventType.Layout || _count == 0)
            return;
        CalcWidth(0);
        SetHorizontal(0, _screen.x, _screen.width);
        CalcHeight(0);
        SetVertical(0, _screen.y, _screen.height);
        for (int a = 0; a < _areaCount; a++)
        {
            int r = _areaRoots[a];
            Rect rect = _areaRects[a];
            CalcWidth(r);
            SetHorizontal(r, 0, rect.width);  // area icerigi LOKAL uzayda
            CalcHeight(r);
            SetVertical(r, 0, rect.height);
        }
    }

    // Ekranda serbest konumlu, kendi layout agacli bolge (Unity GUILayout.BeginArea).
    // rect aktif clip'in lokal uzayindadir; icerik koordinatlari (0,0)'dan baslar.
    public static void BeginArea(in Rect rect)
    {
        if (_layoutPass)
        {
            int root = NewEntry(isGroup: true, isVertical: true, parent: -1);
            _areaRoots[_areaCount] = root;
            _areaRects[_areaCount] = rect;
            _areaCount++;
            _areaStack[_areaDepth++] = _current;
            _current = root;
        }
        else if (_overrun > 0 || _areaCursor >= _areaCount)
        {
            _areaStack[_areaDepth++] = -1; // dummy area isareti
            _overrun++;
        }
        else
        {
            _areaStack[_areaDepth++] = _current;
            _current = _areaRoots[_areaCursor++];
        }
        GuiClip.Push(rect);
    }

    public static void EndArea()
    {
        GuiClip.Pop();
        int prev = _areaStack[--_areaDepth];
        if (prev == -1)
            _overrun--;
        else
            _current = prev;
    }

    // --- Kayit / replay ---

    public static Rect GetRect(float minW, float maxW, float minH, float maxH,
        bool stretchW, bool stretchH, ReadOnlySpan<LayoutOption> options)
    {
        if (_layoutPass)
        {
            int i = NewEntry(isGroup: false, isVertical: false, parent: _current);
            ref Entry e = ref _entries[i];
            e.MinW = minW; e.MaxW = maxW; e.MinH = minH; e.MaxH = maxH;
            e.StretchW = stretchW; e.StretchH = stretchH;
            ApplyOptions(ref e, options);
            return default; // dummy (Unity kDummyRect)
        }
        if (_overrun > 0)
            return default;
        int idx = NextIndex();
        return idx >= 0 ? _entries[idx].Rect : default;
    }

    public static Rect GetRect(float width, float height)
        => GetRect(width, width, height, height, false, false, default);

    public static int BeginGroup(bool vertical, ReadOnlySpan<LayoutOption> options)
    {
        if (_layoutPass)
        {
            int i = NewEntry(isGroup: true, isVertical: vertical, parent: _current);
            ApplyOptions(ref _entries[i], options);
            _current = i;
            return i;
        }
        if (_overrun > 0)
        {
            _overrun++;
            return _current;
        }
        int gi = NextIndex();
        if (gi < 0)
        {
            _overrun++;
            return _current;
        }
        _current = gi;
        return gi;
    }

    public static Rect EndGroup()
    {
        if (!_layoutPass && _overrun > 0)
        {
            _overrun--;
            return default;
        }
        ref Entry g = ref _entries[_current];
        _current = g.Parent >= 0 ? g.Parent : 0;
        return g.Rect;
    }

    // Aktif grubun coz ulmus rect'i (replay'de gecerli).
    public static Rect CurrentGroupRect => _entries[_current].Rect;

    static int NextIndex()
    {
        ref Entry g = ref _entries[_current];
        int i = g.Cursor;
        if (i < 0)
            return -1; // cache'ten fazla kontrol: dummy (pass ortasi yapi degisikligi)
        g.Cursor = _entries[i].NextSibling;
        return i;
    }

    static int NewEntry(bool isGroup, bool isVertical, int parent)
    {
        if (_count == _entries.Length)
            Array.Resize(ref _entries, _entries.Length * 2);
        int i = _count++;
        ref Entry e = ref _entries[i];
        e = default;
        e.IsGroup = isGroup;
        e.IsVertical = isVertical;
        e.Parent = parent;
        e.FirstChild = -1;
        e.LastChild = -1;
        e.NextSibling = -1;
        e.Cursor = -1;
        if (parent >= 0)
        {
            ref Entry p = ref _entries[parent];
            if (p.LastChild < 0)
                p.FirstChild = i;
            else
                _entries[p.LastChild].NextSibling = i;
            p.LastChild = i;
        }
        return i;
    }

    static void ApplyOptions(ref Entry e, ReadOnlySpan<LayoutOption> options)
    {
        for (int i = 0; i < options.Length; i++)
        {
            LayoutOption o = options[i];
            switch (o.Type)
            {
                case LayoutOptionType.Width: e.MinW = e.MaxW = o.Value; e.StretchW = false; e.FixedWOpt = true; break;
                case LayoutOptionType.Height: e.MinH = e.MaxH = o.Value; e.StretchH = false; e.FixedHOpt = true; break;
                case LayoutOptionType.MinWidth: e.MinW = o.Value; if (e.MaxW < o.Value) e.MaxW = o.Value; break;
                case LayoutOptionType.MaxWidth: e.MaxW = o.Value; if (e.MinW > o.Value) e.MinW = o.Value; break;
                case LayoutOptionType.MinHeight: e.MinH = o.Value; if (e.MaxH < o.Value) e.MaxH = o.Value; break;
                case LayoutOptionType.MaxHeight: e.MaxH = o.Value; if (e.MinH > o.Value) e.MinH = o.Value; break;
                case LayoutOptionType.ExpandWidth: e.StretchW = o.Value != 0; break;
                case LayoutOptionType.ExpandHeight: e.StretchH = o.Value != 0; break;
            }
        }
    }

    // --- Iki gecisli cozum (Unity LayoutGroup.CalcWidth/SetHorizontal/...) ---

    static void CalcWidth(int gi)
    {
        ref Entry g = ref _entries[gi];
        if (!g.IsGroup)
            return;

        float contentMin = 0, contentMax = 0;
        int stretch = 0;
        bool first = true;
        float prevTrailing = 0;

        for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
        {
            CalcWidth(c);
            ref Entry ch = ref _entries[c];
            if (g.IsVertical)
            {
                // Dikey grup: genislik = en genis cocuk (margin dahil).
                float mn = ch.MinW + ch.MarginL + ch.MarginR;
                float mx = ch.MaxW + ch.MarginL + ch.MarginR;
                if (mn > contentMin) contentMin = mn;
                if (mx > contentMax) contentMax = mx;
                if (ch.StretchW) stretch++;
            }
            else
            {
                // Yatay grup: toplam + kardesler arasi margin cakistirma.
                float gap = first ? ch.MarginL : (prevTrailing > ch.MarginL ? prevTrailing : ch.MarginL);
                contentMin += ch.MinW + gap;
                contentMax += ch.MaxW + gap;
                prevTrailing = ch.MarginR;
                if (ch.StretchW) stretch++;
                first = false;
            }
        }
        if (!g.IsVertical && !first)
        {
            contentMin += prevTrailing;
            contentMax += prevTrailing;
        }

        contentMin += g.PadL + g.PadR;
        contentMax += g.PadL + g.PadR;

        // Grubun kendi option'lari (Width/MinWidth/MaxWidth) icerik olcusunu ezer.
        if (g.MinW > 0) contentMin = g.MinW;
        if (g.MaxW > 0) contentMax = g.MaxW;
        g.MinW = contentMin;
        g.MaxW = contentMax > contentMin ? contentMax : contentMin;
        g.StretchWCount = stretch;
        if (stretch > 0 && !g.FixedWOpt)
            g.StretchW = true;
    }

    static void SetHorizontal(int gi, float x, float width)
    {
        ref Entry g = ref _entries[gi];
        g.Rect.x = x;
        g.Rect.width = width;
        if (!g.IsGroup)
            return;

        float innerX = x + g.PadL;
        float innerW = width - g.PadL - g.PadR;

        if (g.IsVertical)
        {
            for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
            {
                ref Entry ch = ref _entries[c];
                float avail = innerW - ch.MarginL - ch.MarginR;
                float w = ch.StretchW ? avail : (ch.MaxW < avail ? ch.MaxW : avail);
                if (w < ch.MinW) w = ch.MinW;
                SetHorizontal(c, innerX + ch.MarginL, w);
            }
            return;
        }

        // Yatay dagitim: once t-interpolasyonu (min..max), artan alan expand'lilere.
        float childMin = g.MinW - g.PadL - g.PadR;
        float childMax = g.MaxW - g.PadL - g.PadR;
        float t = 0;
        if (childMax > childMin)
        {
            t = (innerW - childMin) / (childMax - childMin);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
        }
        else if (innerW >= childMax)
        {
            t = 1;
        }
        float extra = innerW - (childMin + (childMax - childMin) * t);
        float perStretch = (extra > 0 && g.StretchWCount > 0) ? extra / g.StretchWCount : 0;

        float cx = innerX;
        bool first = true;
        float prevTrailing = 0;
        for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
        {
            ref Entry ch = ref _entries[c];
            float gap = first ? ch.MarginL : (prevTrailing > ch.MarginL ? prevTrailing : ch.MarginL);
            cx += gap;
            float w = ch.MinW + (ch.MaxW - ch.MinW) * t;
            if (ch.StretchW) w += perStretch;
            SetHorizontal(c, cx, w);
            cx += w;
            prevTrailing = ch.MarginR;
            first = false;
        }
    }

    static void CalcHeight(int gi)
    {
        ref Entry g = ref _entries[gi];
        if (!g.IsGroup)
            return; // leaf min/max yuksekligi GetRect'ten geldi (ileride: wrap'li text burada genislige gore olculur)

        float contentMin = 0, contentMax = 0;
        int stretch = 0;
        bool first = true;
        float prevTrailing = 0;

        for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
        {
            CalcHeight(c);
            ref Entry ch = ref _entries[c];
            if (g.IsVertical)
            {
                float gap = first ? ch.MarginT : (prevTrailing > ch.MarginT ? prevTrailing : ch.MarginT);
                contentMin += ch.MinH + gap;
                contentMax += ch.MaxH + gap;
                prevTrailing = ch.MarginB;
                if (ch.StretchH) stretch++;
                first = false;
            }
            else
            {
                float mn = ch.MinH + ch.MarginT + ch.MarginB;
                float mx = ch.MaxH + ch.MarginT + ch.MarginB;
                if (mn > contentMin) contentMin = mn;
                if (mx > contentMax) contentMax = mx;
                if (ch.StretchH) stretch++;
            }
        }
        if (g.IsVertical && !first)
        {
            contentMin += prevTrailing;
            contentMax += prevTrailing;
        }

        contentMin += g.PadT + g.PadB;
        contentMax += g.PadT + g.PadB;

        if (g.MinH > 0) contentMin = g.MinH;
        if (g.MaxH > 0) contentMax = g.MaxH;
        g.MinH = contentMin;
        g.MaxH = contentMax > contentMin ? contentMax : contentMin;
        g.StretchHCount = stretch;
        if (stretch > 0 && !g.FixedHOpt)
            g.StretchH = true;
    }

    static void SetVertical(int gi, float y, float height)
    {
        ref Entry g = ref _entries[gi];
        g.Rect.y = y;
        g.Rect.height = height;
        if (!g.IsGroup)
            return;

        float innerY = y + g.PadT;
        float innerH = height - g.PadT - g.PadB;

        if (!g.IsVertical)
        {
            for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
            {
                ref Entry ch = ref _entries[c];
                float avail = innerH - ch.MarginT - ch.MarginB;
                float h = ch.StretchH ? avail : (ch.MaxH < avail ? ch.MaxH : avail);
                if (h < ch.MinH) h = ch.MinH;
                SetVertical(c, innerY + ch.MarginT, h);
            }
            return;
        }

        float childMin = g.MinH - g.PadT - g.PadB;
        float childMax = g.MaxH - g.PadT - g.PadB;
        float t = 0;
        if (childMax > childMin)
        {
            t = (innerH - childMin) / (childMax - childMin);
            if (t < 0) t = 0;
            if (t > 1) t = 1;
        }
        else if (innerH >= childMax)
        {
            t = 1;
        }
        float extra = innerH - (childMin + (childMax - childMin) * t);
        float perStretch = (extra > 0 && g.StretchHCount > 0) ? extra / g.StretchHCount : 0;

        float cy = innerY;
        bool first = true;
        float prevTrailing = 0;
        for (int c = g.FirstChild; c >= 0; c = _entries[c].NextSibling)
        {
            ref Entry ch = ref _entries[c];
            float gap = first ? ch.MarginT : (prevTrailing > ch.MarginT ? prevTrailing : ch.MarginT);
            cy += gap;
            float h = ch.MinH + (ch.MaxH - ch.MinH) * t;
            if (ch.StretchH) h += perStretch;
            SetVertical(c, cy, h);
            cy += h;
            prevTrailing = ch.MarginB;
            first = false;
        }
    }
}

// Kullanici yuzeyi: GUILayout karsiligi kisayollar.
public static class GuiLayout
{
    public static LayoutOption Width(float v) => new(LayoutOptionType.Width, v);
    public static LayoutOption Height(float v) => new(LayoutOptionType.Height, v);
    public static LayoutOption MinWidth(float v) => new(LayoutOptionType.MinWidth, v);
    public static LayoutOption MaxWidth(float v) => new(LayoutOptionType.MaxWidth, v);
    public static LayoutOption MinHeight(float v) => new(LayoutOptionType.MinHeight, v);
    public static LayoutOption MaxHeight(float v) => new(LayoutOptionType.MaxHeight, v);
    public static LayoutOption ExpandWidth(bool v) => new(LayoutOptionType.ExpandWidth, v ? 1 : 0);
    public static LayoutOption ExpandHeight(bool v) => new(LayoutOptionType.ExpandHeight, v ? 1 : 0);

    public static void BeginHorizontal(ReadOnlySpan<LayoutOption> options = default)
        => GuiLayoutUtility.BeginGroup(vertical: false, options);
    public static void EndHorizontal() => GuiLayoutUtility.EndGroup();
    public static void BeginVertical(ReadOnlySpan<LayoutOption> options = default)
        => GuiLayoutUtility.BeginGroup(vertical: true, options);
    public static void EndVertical() => GuiLayoutUtility.EndGroup();

    public static void BeginArea(in Rect rect) => GuiLayoutUtility.BeginArea(rect);
    public static void EndArea() => GuiLayoutUtility.EndArea();

    public static void Space(float pixels)
        => GuiLayoutUtility.GetRect(pixels, pixels, pixels, pixels, false, false, default);

    // Kalan alani yutan esnek bosluk.
    public static void FlexibleSpace()
        => GuiLayoutUtility.GetRect(0, 0, 0, 0, true, true, default);
}
#endif
