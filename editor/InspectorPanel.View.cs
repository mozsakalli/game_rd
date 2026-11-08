using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Component "gorunumu": semadaki DocVisible alanlar [Category] gruplarina ayrilir (herkese acik attribute; oyun kodu
// dahil). Kategorisiz grup ("General") en basta, gerisi semadaki ilk gorulme sirasi; hic kategori yoksa duz liste.
// IMGUI tuzagi: gorunum = kontrol sayisi. Hot/klavye kontrol varken (surukleme, yazim) YENIDEN HESAPLANMAZ; cache
// (goId, compIndex) anahtarli, History.Version/tip degisince tazelenir. MeasureHeight ve OnGui ayni gorunumu okur.
public sealed partial class InspectorPanel
{
    internal const float CatH = 20f;
    const string GeneralCategory = "General";

    internal sealed class FieldGroup
    {
        public string Name;                                   // null = kategorisiz (General)
        public readonly List<SerializedType.FieldSchema> Fields = new();
    }

    sealed class CompView
    {
        public string Type;
        public int Version;
        public readonly List<FieldGroup> Groups = new();
        public bool Grouped => Groups.Count > 1 || (Groups.Count == 1 && Groups[0].Name != null);
    }

    readonly Dictionary<long, CompView> _views = new();

    static bool InputBusy => GuiUtility.HotControl != 0 || GuiUtility.KeyboardControl != 0;

    CompView GetView(EditorScene es, SceneDoc.GoDoc g, SceneDoc.CompDoc cd, int compIndex,
        SerializedType.FieldSchema[] schema)
    {
        long key = CompKey(g.Id, compIndex);
        if (_views.TryGetValue(key, out var v))
        {
            bool stale = v.Version != es.History.Version || v.Type != cd.Type;
            if (!stale || InputBusy)
                return v;
        }
        else
            v = new CompView();
        v.Type = cd.Type;
        v.Version = es.History.Version;
        Build(cd, schema, v);
        _views[key] = v;
        return v;
    }

    static void Build(SceneDoc.CompDoc cd, SerializedType.FieldSchema[] schema, CompView v)
    {
        v.Groups.Clear();
        foreach (var f in schema)
        {
            if (!DocVisible(f, cd))
                continue;
            GroupFor(v.Groups, f.Category).Fields.Add(f);
        }
        for (int i = 1; i < v.Groups.Count; i++)
            if (v.Groups[i].Name == null)
            {
                var gen = v.Groups[i];
                v.Groups.RemoveAt(i);
                v.Groups.Insert(0, gen);
                break;
            }
    }

    static FieldGroup GroupFor(List<FieldGroup> groups, string cat)
    {
        foreach (var gr in groups)
            if (gr.Name == cat)
                return gr;
        var n = new FieldGroup { Name = cat };
        groups.Add(n);
        return n;
    }

    // --- kategori katlama (tur bazli, EditorSettings.inspectorFolded) ---
    static string FoldKey(string type, string cat) => type + "/" + (cat ?? GeneralCategory);

    static bool CatFolded(string type, string cat) => App.Settings.inspectorFolded.Contains(FoldKey(type, cat));

    static void SetCatFolded(string type, string cat, bool folded)
    {
        string key = FoldKey(type, cat);
        var list = App.Settings.inspectorFolded;
        if (folded) { if (!list.Contains(key)) list.Add(key); }
        else list.Remove(key);
        App.SaveSettings();
    }

    static readonly Color CatBg = new(48, 51, 61, 255);
    static readonly Color CatTextColor = new(186, 190, 202, 255);

    // Kategori alt-basligi: ince bar + ok + ad; tiklama katlar. Geriye acik mi dondurur.
    bool CategoryHeader(ref float y, float w, string type, string cat)
    {
        bool folded = CatFolded(type, cat);
        var r = new Rect(8, y, w - 8, CatH);
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(r, CatBg, 1);
            GuiRenderer.DrawTextIn(new Rect(10, y, 16, CatH), folded ? "\u25b8" : "\u25be", SmallFont, ArrowColor, false, 2);
            GuiRenderer.DrawTextIn(new Rect(26, y, w - 30, CatH), cat ?? GeneralCategory, SmallFont, CatTextColor, false, 2);
        }
        if (HeaderClick(r))
            SetCatFolded(type, cat, !folded);
        y += CatH + 2;
        return !folded;
    }
}
