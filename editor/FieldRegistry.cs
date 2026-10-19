using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Cizilen Inspector alanlarinin kaydi (editor otomasyonu icin): ObjectDrawer her Repaint'te asset alanlarini ve
// liste basliklarini yol + GLOBAL (ana pencere mantiksal) dikdortgenle bildirir. RPC ui.fields listeler,
// ui.dropAsset hedefi yoldan bulur — AI/test tarafi piksel hesaplamaz. Yalniz son tamamlanan frame'in kaydi gecerli.
public static class FieldRegistry
{
    public readonly struct Entry
    {
        public readonly string Path;   // alan yolu ("a:sources", "a:sources[0]", "c3:Sprite")
        public readonly Rect Rect;     // global mantiksal px
        public readonly string Kind;   // "asset" | "list"
        public readonly string Type;   // alan/eleman tipi adi
        public Entry(string path, Rect rect, string kind, string type) { Path = path; Rect = rect; Kind = kind; Type = type; }
    }

    static readonly List<Entry> _cur = new(), _last = new();
    static int _frame = -1;

    public static void Note(string path, in Rect local, string kind, Type type)
    {
        if (Event.Current.Type != EventType.Repaint)
            return;
        Roll();
        var tl = GuiClip.Unclip(new Vec2(local.x, local.y));
        _cur.Add(new Entry(path, new Rect(tl.x, tl.y, local.width, local.height), kind, type?.Name ?? ""));
    }

    static void Roll()
    {
        if (_frame == Time.frameCount)
            return;
        _frame = Time.frameCount;
        _last.Clear();
        _last.AddRange(_cur);
        _cur.Clear();
    }

    // Son tamamlanan frame'in alanlari (bu frame'de henuz cizilmediyse bir onceki).
    public static IReadOnlyList<Entry> Current
    {
        get
        {
            Roll();
            return _last.Count > 0 ? _last : _cur;
        }
    }

    public static bool TryFind(string path, out Entry e)
    {
        foreach (var x in Current)
            if (x.Path == path) { e = x; return true; }
        // Esnek: on eksiz ("sprites") ya da sonu eslesen
        foreach (var x in Current)
            if (x.Path.EndsWith(":" + path, StringComparison.Ordinal) || x.Path.EndsWith("." + path, StringComparison.Ordinal))
            { e = x; return true; }
        e = default;
        return false;
    }
}
