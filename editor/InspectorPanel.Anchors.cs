using System;
using System.Globalization;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Anchor preset izgarasi (Unity RectTransform "Anchor Presets" karsiligi, satir ici).
// SEMA KONVANSIYONU (tip-ozel kod yok, "Set Native Size" ile ayni model): Vec2 "anchorMin" alaninin
// kardesleri arasinda Vec2 "anchorMax" varsa anchorMin satirinin USTUNE 4x4 preset blogu gelir.
// Sutunlar Left/Center/Right/Stretch-H, satirlar Top/Middle/Bottom/Stretch-V (y asagi: 0 = ust).
// Opsiyonel kardesler: pivot (Shift = pivot'u da anchor'a getirir), anchoredPos + width/height
// (Alt = konumu sifirlar, stretch ekseninde delta boyutu 0). Olcum (MeasureHeight) ve cizim AYNI kosulu kullanir.
public sealed partial class InspectorPanel
{
    const float AnchorCell = 20f;
    const float AnchorGap = 2f;
    const float AnchorHdr = 12f;
    // Baslik + 4 hucre + ipucu satiri.
    internal const float AnchorBlockH = AnchorHdr + 4 * (AnchorCell + AnchorGap) + 4 + 16 + 4;

    static readonly Color AnchorFrameColor = new(150, 154, 166, 255);
    static readonly Color AnchorMarkColor = new(230, 232, 238, 255);
    static readonly Color AnchorCurrentColor = new(50, 110, 200, 255);
    static readonly string[] AnchorColHdr = { "L", "C", "R", "S" };
    static readonly string[] AnchorRowHdr = { "T", "M", "B", "S" };

    static bool AnchorPresetTargets(SerializedType.FieldSchema[] schema, SerializedType.FieldSchema f,
        out SerializedType.FieldSchema anchorMax, out SerializedType.FieldSchema pivot,
        out SerializedType.FieldSchema anchoredPos, out SerializedType.FieldSchema width,
        out SerializedType.FieldSchema height)
    {
        anchorMax = pivot = anchoredPos = width = height = null;
        if (f.Kind != SerializedType.Kind.Vec2 || !string.Equals(f.Name, "anchorMin", StringComparison.OrdinalIgnoreCase))
            return false;
        foreach (var s in schema)
        {
            if (s.Kind == SerializedType.Kind.Vec2)
            {
                if (string.Equals(s.Name, "anchorMax", StringComparison.OrdinalIgnoreCase)) anchorMax = s;
                else if (string.Equals(s.Name, "pivot", StringComparison.OrdinalIgnoreCase)) pivot = s;
                else if (string.Equals(s.Name, "anchoredPos", StringComparison.OrdinalIgnoreCase)) anchoredPos = s;
            }
            else if (s.Kind == SerializedType.Kind.Float)
            {
                if (string.Equals(s.Name, "Width", StringComparison.OrdinalIgnoreCase)) width = s;
                else if (string.Equals(s.Name, "Height", StringComparison.OrdinalIgnoreCase)) height = s;
            }
        }
        return anchorMax != null;
    }

    // Eksen presetinin (min,max) ikilisi: 0/1/2 nokta (0, 0.5, 1), 3 stretch (0..1).
    static void AnchorAxis(int idx, out float min, out float max)
    {
        switch (idx)
        {
            case 0: min = max = 0f; break;
            case 1: min = max = 0.5f; break;
            case 2: min = max = 1f; break;
            default: min = 0f; max = 1f; break;
        }
    }

    // Mevcut (min,max) hangi preset sutunu/satiri; eslesmiyorsa -1.
    static int AnchorAxisIndex(float min, float max)
    {
        static bool Eq(float a, float b) => MathF.Abs(a - b) < 0.001f;
        if (Eq(min, max))
        {
            if (Eq(min, 0f)) return 0;
            if (Eq(min, 0.5f)) return 1;
            if (Eq(min, 1f)) return 2;
            return -1;
        }
        return Eq(min, 0f) && Eq(max, 1f) ? 3 : -1;
    }

    static Vec2 ReadVec2(SceneDoc.CompDoc cd, SerializedType.FieldSchema f, Vec2 fallback)
    {
        var node = FindProp(cd, f);
        if (node?.Scalar is { Length: > 0 } s)
        {
            var v = SerializedType.ParseVec3(s);
            return new Vec2(v.x, v.y);
        }
        return fallback;
    }

    static string V2(float x, float y)
        => x.ToString(CultureInfo.InvariantCulture) + " " + y.ToString(CultureInfo.InvariantCulture);

    void DrawAnchorPresets(EditorScene es, SceneDoc.GoDoc g, SceneDoc.CompDoc cd,
        SerializedType.FieldSchema anchorMin, SerializedType.FieldSchema anchorMax,
        SerializedType.FieldSchema pivot, SerializedType.FieldSchema anchoredPos,
        SerializedType.FieldSchema width, SerializedType.FieldSchema height, ref float y, float w)
    {
        var ev = Event.Current;
        bool repaint = ev.Type == EventType.Repaint;
        Vec2 curMin = ReadVec2(cd, anchorMin, new Vec2(0.5f, 0.5f));
        Vec2 curMax = ReadVec2(cd, anchorMax, new Vec2(0.5f, 0.5f));
        int curCol = AnchorAxisIndex(curMin.x, curMax.x);
        int curRow = AnchorAxisIndex(curMin.y, curMax.y);

        DrawFieldLabel(new Rect(12, y, LabelW - 4, RowH), "Anchors");

        float gx = 12 + LabelW + AnchorHdr;
        float gy = y + AnchorHdr;
        float step = AnchorCell + AnchorGap;
        if (repaint)
        {
            for (int c = 0; c < 4; c++)
                GuiRenderer.DrawTextIn(new Rect(gx + c * step, y, AnchorCell, AnchorHdr), AnchorColHdr[c],
                    SmallFont, c == curCol ? LabelColor : LabelDimColor, true);
            for (int r = 0; r < 4; r++)
                GuiRenderer.DrawTextIn(new Rect(gx - AnchorHdr, gy + r * step, AnchorHdr, AnchorCell), AnchorRowHdr[r],
                    SmallFont, r == curRow ? LabelColor : LabelDimColor, true);
        }

        int hitCol = -1, hitRow = -1;
        for (int r = 0; r < 4; r++)
        for (int c = 0; c < 4; c++)
        {
            var cell = new Rect(gx + c * step, gy + r * step, AnchorCell, AnchorCell);
            bool current = c == curCol && r == curRow;
            if (Gui.Button(cell))
            {
                hitCol = c;
                hitRow = r;
            }
            if (!repaint)
                continue;
            if (current)
                GuiRenderer.DrawRect(cell, AnchorCurrentColor);
            DrawAnchorIcon(cell, c, r);
        }

        if (repaint)
            GuiRenderer.DrawTextIn(new Rect(12 + LabelW, gy + 4 * step + 4, w - LabelW - 24, 16),
                "Shift: also set pivot \u00b7 Alt: also set position", SmallFont, LabelDimColor);

        if (hitCol >= 0)
            ApplyAnchorPreset(es, g, cd, hitCol, hitRow, ev.Modifiers, anchorMin, anchorMax, pivot, anchoredPos, width, height);
        y += AnchorBlockH;
    }

    // Hucre ikonu: dis cerceve = parent, isaret = anchor konumu (nokta kare, stretch cubuk, ikisi dolu kutu).
    static void DrawAnchorIcon(in Rect cell, int col, int row)
    {
        var inner = new Rect(cell.x + 3, cell.y + 3, cell.width - 6, cell.height - 6);
        GuiRenderer.DrawRect(new Rect(inner.x, inner.y, inner.width, 1), AnchorFrameColor, 1);
        GuiRenderer.DrawRect(new Rect(inner.x, inner.y + inner.height - 1, inner.width, 1), AnchorFrameColor, 1);
        GuiRenderer.DrawRect(new Rect(inner.x, inner.y, 1, inner.height), AnchorFrameColor, 1);
        GuiRenderer.DrawRect(new Rect(inner.x + inner.width - 1, inner.y, 1, inner.height), AnchorFrameColor, 1);

        const float mark = 4f, bar = 3f, pad = 2f;
        bool sx = col == 3, sy = row == 3;
        float px = col switch { 0 => inner.x + pad, 1 => inner.x + (inner.width - mark) * 0.5f, 2 => inner.x + inner.width - pad - mark, _ => 0 };
        float py = row switch { 0 => inner.y + pad, 1 => inner.y + (inner.height - mark) * 0.5f, 2 => inner.y + inner.height - pad - mark, _ => 0 };
        Rect m;
        if (sx && sy)
            m = new Rect(inner.x + pad, inner.y + pad, inner.width - pad * 2, inner.height - pad * 2);
        else if (sx)
            m = new Rect(inner.x + pad, py + (mark - bar) * 0.5f, inner.width - pad * 2, bar);
        else if (sy)
            m = new Rect(px + (mark - bar) * 0.5f, inner.y + pad, bar, inner.height - pad * 2);
        else
            m = new Rect(px, py, mark, mark);
        GuiRenderer.DrawRect(m, AnchorMarkColor, 1);
    }

    void ApplyAnchorPreset(EditorScene es, SceneDoc.GoDoc g, SceneDoc.CompDoc cd, int col, int row,
        EventModifiers mods, SerializedType.FieldSchema anchorMin, SerializedType.FieldSchema anchorMax,
        SerializedType.FieldSchema pivot, SerializedType.FieldSchema anchoredPos,
        SerializedType.FieldSchema width, SerializedType.FieldSchema height)
    {
        AnchorAxis(col, out float minX, out float maxX);
        AnchorAxis(row, out float minY, out float maxY);
        bool sx = col == 3, sy = row == 3;

        es.BeginBatch();
        try
        {
            Set(anchorMin, V2(minX, minY));
            Set(anchorMax, V2(maxX, maxY));
            if ((mods & EventModifiers.Shift) != 0 && pivot != null)
                Set(pivot, V2(sx ? 0.5f : minX, sy ? 0.5f : minY));
            if ((mods & EventModifiers.Alt) != 0)
            {
                if (anchoredPos != null) Set(anchoredPos, "0 0");
                if (sx && width != null) Set(width, "0");
                if (sy && height != null) Set(height, "0");
            }
        }
        finally { es.EndBatch(); }

        void Set(SerializedType.FieldSchema f, string value)
        {
            var old = FindProp(cd, f);
            if (old?.Scalar != value)
                es.SetProp(g, cd, f, old?.Scalar, value);
        }
    }
}
