using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Window/Localization: kapsam (Host / modul) tablosunu duzenler — dil sutunlari, satir filtre, hucre
// yazimi (undo: LocCellOp), satir ekle/sil (LocRowOp), dil ekle/sil. Kullanim sayisi acik doc'tan
// (proje genisi tarama + missing/unused raporu F3 RPC). Kaydetme LocTableStore (bosta/Flush).
[MenuItem("Window/Localization", 6)]
public sealed class LocalizationWindow : EditorWindow
{
    const float RowH = InspectorPanel.RowH;
    const float KeyW = 200f;
    const float MinCellW = 120f;

    static readonly Color MissingColor = new(226, 96, 96, 255);
    static readonly Color HeaderColor = new(150, 180, 235, 255);

    readonly Dictionary<string, char[]> _bufs = new();
    readonly Dictionary<string, int> _lens = new();
    readonly Dictionary<string, string> _seen = new();
    readonly List<LocEntry> _rows = new();
    string[] _scopeNames = Array.Empty<string>();
    List<string> _scopes = new();
    int _scopeIdx;
    string _filter = "", _newKey = "";
    Vec2 _scroll;

    public LocalizationWindow() => Title = "Localization";

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var es = App.EditScene;
        if (App.Assets == null || es == null)
        {
            Gui.Label(new Rect(4, 4, 200, 20), "No project");
            return;
        }
        RefreshScopes();
        string scope = _scopes[_scopeIdx];
        var table = LocTableStore.Get(scope);

        float y = 4;
        // Ust bar: kapsam | filtre | Save
        int ns = Gui.ComboBox(new Rect(6, y, 120, 20), _scopeIdx, _scopeNames);
        if (ns != _scopeIdx && ns >= 0) { _scopeIdx = ns; _bufs.Clear(); _lens.Clear(); _seen.Clear(); }
        Gui.Label(new Rect(134, y + 2, 44, 18), "filter");
        _filter = TextCell("filter", _filter, new Rect(176, y, 180, 20)) ?? _filter;
        if (Gui.Button(new Rect(vis.width - 60, y, 54, 20), "Save") )
            LocTableStore.Flush();
        y += RowH + 4;

        // Diller: sabit katalogdan isaretli kutular (kaynak dil kilitli). Isaret = sutun ekle, kaldir = sutun sil.
        Gui.Label(new Rect(6, y + 2, 60, 18), "languages");
        {
            float lx = 70, lw = 92;
            var cat = LocTableStore.LanguageCatalog;
            for (int i = 0; i < cat.Length; i++)
            {
                if (lx + lw > vis.width - 8) { lx = 70; y += RowH - 2; }
                var (code, name) = cat[i];
                bool on = table != null && table.LanguageIndex(code) >= 0;
                bool source = table != null && table.LanguageIndex(code) == 0;
                bool next = Gui.Toggle(new Rect(lx, y, 18, 18), on);
                if (Event.Current.Type == EventType.Repaint)
                    GuiRenderer.DrawTextIn(new Rect(lx + 20, y, lw - 22, 18), code + " " + name,
                        InspectorPanel.SmallFont, source ? HeaderColor : InspectorPanel.RefTextColor, false, 0);
                if (next != on && !source)
                {
                    if (next) LocTableStore.AddLanguage(scope, code);
                    else LocTableStore.RemoveLanguage(scope, code);
                    ResetBuffers();
                    return;
                }
                lx += lw;
            }
        }
        y += RowH + 4;

        if (table == null)
        {
            Gui.Label(new Rect(8, y, vis.width - 16, 20), "No table yet for this scope — it is created on the first localized field (loc toggle) or when a language is ticked.");
            return;
        }

        // Yeni satir
        Gui.Label(new Rect(6, y + 2, 60, 18), "new key");
        _newKey = TextCell("newkey", _newKey, new Rect(66, y, KeyW, 20)) ?? _newKey;
        if (Gui.Button(new Rect(70 + KeyW, y, 40, 20), "Add") && _newKey.Trim().Length > 0 && table.Find(_newKey.Trim()) < 0)
        {
            var vals = new List<string>();
            for (int i = 0; i < table.Languages.Count; i++) vals.Add("");
            es.History.Push(new LocRowOp { Scope = scope, Key = _newKey.Trim(), Values = vals, Removed = false });
            LocTableStore.AddRow(scope, _newKey.Trim(), vals);
            _newKey = "";
            ResetBuffers();
        }
        y += RowH + 4;

        // Satirlar (filtre: key ya da herhangi bir dil hucresi)
        _rows.Clear();
        foreach (var e in table.Entries)
            if (Matches(e, _filter))
                _rows.Add(e);

        int nl = table.Languages.Count;
        float cellW = Math.Max(MinCellW, (vis.width - 20 - KeyW - 30 - 60) / Math.Max(1, nl));
        float contentW = KeyW + 60 + nl * (cellW + 4) + 30;
        float headerY = y;
        // Baslik satiri (dil kodlari; 0 = kaynak)
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawTextIn(new Rect(8, headerY, KeyW, 18), "key", InspectorPanel.SmallFont, HeaderColor, false, 2);
            GuiRenderer.DrawTextIn(new Rect(8 + KeyW, headerY, 56, 18), "uses", InspectorPanel.SmallFont, HeaderColor, false, 2);
        }
        for (int i = 0; i < nl; i++)
        {
            var hr = new Rect(8 + KeyW + 60 + i * (cellW + 4), headerY, cellW, 18);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(hr, table.Languages[i] + (i == 0 ? " (source)" : ""), InspectorPanel.SmallFont, HeaderColor, false, 2);
        }
        y += RowH;

        float listH = vis.height - y;
        _scroll = Gui.BeginScrollView(new Rect(0, y, vis.width, listH), _scroll,
            new Rect(0, 0, Math.Max(contentW, vis.width - 20), _rows.Count * RowH + 8));
        float ry = 2;
        for (int r = 0; r < _rows.Count; r++)
        {
            var e = _rows[r];
            if (Event.Current.Type == EventType.Repaint)
            {
                GuiRenderer.DrawTextIn(new Rect(8, ry, KeyW, 18), e.Key, InspectorPanel.SmallFont, InspectorPanel.RefTextColor, false, 2);
                GuiRenderer.DrawTextIn(new Rect(8 + KeyW, ry, 56, 18), es.CountLocUsages(e.Key).ToString(), InspectorPanel.SmallFont, InspectorPanel.LabelDimColor, false, 2);
            }
            for (int i = 0; i < nl; i++)
            {
                string v = e.Values != null && i < e.Values.Count ? e.Values[i] ?? "" : "";
                var cr = new Rect(8 + KeyW + 60 + i * (cellW + 4), ry, cellW, 18);
                if (v.Length == 0 && Event.Current.Type == EventType.Repaint)
                    GuiRenderer.DrawTextIn(new Rect(cr.x + 4, cr.y, cr.width - 8, 18), "MISSING", InspectorPanel.SmallFont, MissingColor, false, 2);
                string nv = TextCell(e.Key + "|" + table.Languages[i], v, cr);
                if (nv != null)
                {
                    es.History.Push(new LocCellOp { Scope = scope, Key = e.Key, Lang = table.Languages[i], OldValue = v, NewValue = nv });
                    LocTableStore.SetValue(scope, e.Key, table.Languages[i], nv);
                }
            }
            if (Gui.Button(new Rect(8 + KeyW + 60 + nl * (cellW + 4), ry, 18, 18), "x"))
            {
                es.History.Push(new LocRowOp { Scope = scope, Key = e.Key, Values = new List<string>(e.Values ?? new List<string>()), Removed = true });
                LocTableStore.RemoveRow(scope, e.Key);
                ResetBuffers();
                Gui.EndScrollView();
                return;
            }
            ry += RowH;
        }
        Gui.EndScrollView();
    }

    void RefreshScopes()
    {
        var scopes = LocTableStore.Scopes();
        if (scopes.Count != _scopes.Count || _scopeNames.Length != scopes.Count)
        {
            _scopes = scopes;
            _scopeNames = new string[scopes.Count];
            for (int i = 0; i < scopes.Count; i++)
                _scopeNames[i] = LocTableStore.ScopeLabel(scopes[i]);
            // Acik sahnenin kapsamini varsayilan sec.
            int cur = _scopes.IndexOf(LocTableStore.CurrentScope);
            _scopeIdx = cur >= 0 ? cur : 0;
        }
        if (_scopeIdx >= _scopes.Count) _scopeIdx = 0;
    }

    static bool Matches(LocEntry e, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if (e.Key != null && e.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (e.Values != null)
            foreach (var v in e.Values)
                if (v != null && v.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // Tek satir metin hucresi: degisince yeni degeri, aksi halde null. Dis degisiklikte tampon tazelenir.
    string TextCell(string id, string current, in Rect rect)
    {
        current ??= "";
        if (!_bufs.TryGetValue(id, out var buf) || !_seen.TryGetValue(id, out var seen) || seen != current)
        {
            int cap = 256;
            while (cap <= current.Length) cap *= 2;
            buf = new char[cap];
            current.CopyTo(0, buf, 0, current.Length);
            _bufs[id] = buf;
            _lens[id] = current.Length;
            _seen[id] = current;
        }
        int len = _lens[id];
        if (len >= buf.Length - 1) { Array.Resize(ref buf, buf.Length * 2); _bufs[id] = buf; }
        Gui.TextField(rect, ref buf, ref len);
        _bufs[id] = buf;
        _lens[id] = len;
        if (InspectorPanel.BufferEquals(current, buf, len))
            return null;
        string s = new string(buf, 0, len);
        _seen[id] = s;
        return s;
    }

    void ResetBuffers() { _bufs.Clear(); _lens.Clear(); _seen.Clear(); }
}
