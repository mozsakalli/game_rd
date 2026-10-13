using System;
using System.Collections.Generic;
using System.Reflection;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Unity "Add Component" popup'i: tepede filtre, altinda [AddComponentMenu] yoluna gore
// gruplanmis alfabetik liste ("Scripts" = isaretsiz oyun kodu, en sonda; bos yol gizli).
// Yazinca gruplar duzlesir (ad + tip adinda substring). Enter = secili/ilk eslesen,
// Up/Down = gez, tik = ekle; eklenince kapanir. Ekleme EditorScene.AddComponent bogazi.
public sealed class AddComponentPopup : EditorPopup
{
    const float W = 260f, H = 380f, ItemH = 20f, Pad = 8f;
    static readonly int _rowsHash = "AddComponent.Rows".GetHashCode();

    sealed class Item
    {
        public string Group, Label, TypeName, LabelLower, TypeLower;
    }

    static AddComponentPopup _instance;
    public static void Show(int goId) => (_instance ??= new AddComponentPopup()).OpenFor(goId);
    public static void CloseIfOpen() => _instance?.Close();

    int _goId;
    readonly List<Item> _all = new();
    readonly List<Item> _visible = new();   // filtreli (grup sirali)
    readonly List<int> _headerAt = new();    // satir index -> grup basligi mi (-1 = oge indexi)
    char[] _filter = new char[64];
    int _filterLen;
    string _lastFilter = "";
    int _filterId, _rowsId;
    int _selected;
    Vec2 _scroll;

    void OpenFor(int goId)
    {
        _goId = goId;
        _filterLen = 0;
        _lastFilter = "";
        _selected = 0;
        _scroll = default;
        BuildAll();
        Rebuild("");
        Open(W, H);
    }

    protected override void OnOpened()
    {
        // Filtre alanina klavye odagi: id ilk pass'te atanir; o zamana kadar bayrak.
        _wantFocus = true;
    }
    bool _wantFocus;

    protected override void OnClosing()
    {
        if (GuiUtility.KeyboardControl == _filterId && _filterId != 0)
            GuiUtility.KeyboardControl = 0;
        if (GuiUtility.HotControl == _rowsId && _rowsId != 0)
            GuiUtility.HotControl = 0;
    }

    void BuildAll()
    {
        _all.Clear();
        foreach (var e in App.Catalog.Entries)
        {
            if (e.Create == null || e.Type == null)
                continue;
            var attr = e.Type.GetCustomAttribute<AddComponentMenuAttribute>();
            string group, label;
            if (attr == null)
            {
                group = "Scripts";
                label = InspectorPanel.Nicify(e.Name);
            }
            else
            {
                if (string.IsNullOrEmpty(attr.Path))
                    continue; // gizli
                int slash = attr.Path.LastIndexOf('/');
                group = slash > 0 ? attr.Path.Substring(0, slash) : "Scripts";
                label = slash >= 0 ? attr.Path.Substring(slash + 1) : attr.Path;
            }
            _all.Add(new Item
            {
                Group = group,
                Label = label,
                TypeName = e.Name,
                LabelLower = label.ToLowerInvariant(),
                TypeLower = e.Name.ToLowerInvariant(),
            });
        }
        _all.Sort(static (a, b) =>
        {
            int g = GroupOrder(a.Group).CompareTo(GroupOrder(b.Group));
            if (g != 0) return g;
            g = string.CompareOrdinal(a.Group, b.Group);
            if (g != 0) return g;
            return string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        });
    }

    static int GroupOrder(string g) => g == "Scripts" ? 1 : 0;

    // Filtre bos: grup basliklari + ogeler. Dolu: duz eslesen liste (basliksiz).
    void Rebuild(string filter)
    {
        _lastFilter = filter;
        _visible.Clear();
        _headerAt.Clear();
        string f = filter.ToLowerInvariant();
        string lastGroup = null;
        for (int i = 0; i < _all.Count; i++)
        {
            var it = _all[i];
            if (f.Length > 0 && !it.LabelLower.Contains(f) && !it.TypeLower.Contains(f))
                continue;
            if (f.Length == 0 && it.Group != lastGroup)
            {
                lastGroup = it.Group;
                _headerAt.Add(-1);
                _visible.Add(it); // baslik satiri grubu bu ogeden okur
            }
            _headerAt.Add(_visible.Count);
            _visible.Add(it);
        }
        _selected = FirstSelectable(0, +1);
    }

    bool IsHeader(int row) => _headerAt[row] < 0;

    int FirstSelectable(int from, int dir)
    {
        for (int r = from; r >= 0 && r < _headerAt.Count; r += dir)
            if (!IsHeader(r))
                return r;
        return -1;
    }

    void Commit(int row)
    {
        if (row < 0 || row >= _headerAt.Count || IsHeader(row))
            return;
        var es = App.EditScene;
        var g = es?.FindGo(_goId);
        if (g != null)
            es.AddComponent(g, _visible[row].TypeName);
        Close();
    }

    protected override void OnGui(Event ev)
    {
        var win = WindowRect;
        var filterRect = new Rect(Pad, Pad, win.width - Pad * 2, 22);
        Gui.TextField(filterRect, ref _filter, ref _filterLen);
        _filterId = GuiUtility.LastControlID;
        if (_wantFocus && _filterId != 0)
        {
            GuiUtility.KeyboardControl = _filterId;
            _wantFocus = false;
        }
        string cur = new string(_filter, 0, _filterLen);
        if (cur != _lastFilter)
            Rebuild(cur);
        if (ev.Type == EventType.Repaint && _filterLen == 0)
            GuiRenderer.DrawTextIn(new Rect(filterRect.x + 6, filterRect.y, filterRect.width - 12, filterRect.height),
                "Search...", Gui.FontSize - 1, new Color(120, 124, 136, 255), false, 3);

        // Klavye gezinme (TextField odakli olsa da pencere bize ait: Enter/Up/Down burada).
        if (ev.Type == EventType.KeyDown)
        {
            if (ev.KeyCode == GLFWConst.KEY_ENTER) { ev.Use(); Commit(_selected); return; }
            if (ev.KeyCode == GLFWConst.KEY_DOWN)
            {
                int n = FirstSelectable(_selected + 1, +1);
                if (n >= 0) _selected = n;
                EnsureVisible(); ev.Use();
            }
            else if (ev.KeyCode == GLFWConst.KEY_UP)
            {
                int n = FirstSelectable(_selected - 1, -1);
                if (n >= 0) _selected = n;
                EnsureVisible(); ev.Use();
            }
        }

        var listRect = new Rect(Pad, filterRect.yMax + 6, win.width - Pad * 2, win.height - filterRect.yMax - 6 - Pad);
        float contentH = _headerAt.Count * ItemH;
        _scroll = Gui.BeginScrollView(listRect, _scroll, new Rect(0, 0, listRect.width - 14, contentH));

        _rowsId = GuiUtility.GetControlID(_rowsHash, FocusType.Passive);
        float rowW = listRect.width - 14;
        for (int r = 0; r < _headerAt.Count; r++)
        {
            var row = new Rect(0, r * ItemH, rowW, ItemH);
            bool header = IsHeader(r);
            var it = _visible[r];
            switch (ev.GetTypeForControl(_rowsId))
            {
                case EventType.MouseDown:
                    if (!header && ev.Button == 0 && row.Contains(ev.MousePosition))
                    {
                        ev.Use();
                        Gui.EndScrollView();
                        Commit(r);
                        return;
                    }
                    break;
                case EventType.Repaint:
                    if (header)
                    {
                        GuiRenderer.DrawRect(row, new Color(44, 47, 56, 255), 1);
                        GuiRenderer.DrawTextIn(new Rect(row.x + 6, row.y, row.width - 12, row.height),
                            it.Group, Gui.FontSize - 2, new Color(160, 165, 180, 255), false, 2);
                    }
                    else
                    {
                        bool hover = row.Contains(ev.MousePosition);
                        if (hover)
                            GuiRenderer.DrawRect(row, new Color(50, 110, 200, 255), 1);
                        else if (r == _selected)
                            GuiRenderer.DrawRect(row, new Color(62, 66, 80, 255), 1);
                        GuiRenderer.DrawTextIn(new Rect(row.x + 16, row.y, row.width - 22, row.height),
                            it.Label, Gui.FontSize - 1, new Color(215, 218, 228, 255), false, 2);
                    }
                    break;
            }
        }
        Gui.EndScrollView();
    }

    void EnsureVisible()
    {
        float top = _selected * ItemH, bottom = top + ItemH;
        float viewH = H - (Pad + 22 + 6) - Pad;
        if (top < _scroll.y) _scroll.y = top;
        else if (bottom > _scroll.y + viewH) _scroll.y = bottom - viewH;
    }
}
