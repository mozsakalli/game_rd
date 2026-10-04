using System;
using System.Collections.Generic;
using System.Text;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Inspector gorsel dili: tek yerden font/renk/olcu. Unity Inspector'a yakin:
// belirgin baslik bari, okunur etiketler, panel genisligine gore etiket sutunu.
public sealed partial class InspectorPanel
{
    const float HeaderH = 24f;

    static float LabelFont => Gui.FontSize - 1f;
    static float HeaderFont => Gui.FontSize;
    static float SmallFont => Gui.FontSize - 2f;

    static readonly Color LabelColor = new(206, 210, 220, 255);
    static readonly Color LabelDimColor = new(130, 134, 144, 255);
    static readonly Color HeaderTextColor = new(238, 241, 248, 255);
    static readonly Color HeaderBg = new(58, 62, 74, 255);
    static readonly Color HeaderLine = new(24, 26, 32, 255);
    static readonly Color HeaderHighlight = new(76, 81, 95, 255);
    static readonly Color ArrowColor = new(196, 200, 210, 255);
    static readonly Color RefTextColor = new(196, 200, 210, 255);

    // Etiket sutunu: Unity gibi genisligin ~%40'i, mantikli sinirlarla.
    static float LabelWidthFor(float contentWidth)
        => MathF.Round(Math.Clamp(contentWidth * 0.40f, 110f, 220f));

    // Baslik bari: arka plan + ust vurgu + alt ayirici cizgi. Metin cizilmez
    // (cagiran toggle/ok/baslik yerlesimini kendi yapar).
    static void DrawHeaderBar(float y, float x, float width)
    {
        if (Event.Current.Type != EventType.Repaint)
            return;
        GuiRenderer.DrawRect(new Rect(x, y, width, HeaderH), HeaderBg, 1);
        GuiRenderer.DrawRect(new Rect(x, y, width, 1), HeaderHighlight, 1);
        GuiRenderer.DrawRect(new Rect(x, y + HeaderH - 1, width, 1), HeaderLine, 1);
    }

    static readonly Dictionary<string, string> _niceNames = new();

    // Unity ObjectNames.NicifyVariableName paritesi: "m_"/"_" on eki dusur,
    // camelCase -> "Camel Case", kisaltma korunur ("UIButton" -> "UI Button").
    static string Nicify(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (_niceNames.TryGetValue(name, out string cached))
            return cached;

        int i = 0;
        if (name.Length > 2 && name[0] == 'm' && name[1] == '_')
            i = 2;
        while (i < name.Length && name[i] == '_')
            i++;
        if (i >= name.Length)
            i = 0;

        var sb = new StringBuilder(name.Length + 4);
        for (; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_')
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
                continue;
            }
            if (sb.Length == 0 || sb[^1] == ' ')
            {
                sb.Append(char.ToUpperInvariant(c));
                continue;
            }
            char prev = sb[^1];
            if (char.IsUpper(c))
            {
                bool afterLower = char.IsLower(prev);
                bool acronymEnd = char.IsUpper(prev) && i + 1 < name.Length && char.IsLower(name[i + 1]);
                if (afterLower || acronymEnd)
                    sb.Append(' ');
            }
            sb.Append(c);
        }
        string result = sb.ToString();
        _niceNames[name] = result;
        return result;
    }
}
