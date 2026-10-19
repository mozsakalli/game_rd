using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;


// Unity Project Settings > Player karsiligi: ProjectSettings/Games/<oyun>/PlayerSettings.asset'i sema uzerinden duzenler.
// DUZEN (2026-10-18): ustte OYUN SEKMELERI (duzenlenen oyun; calisilan oyundan bagimsiz — toolbar combosu Play/Build baglami,
// sekme "neyi duzenliyorum"), SOL kategori listesi + SAG icerik. Kategoriler PlayerSettings alanlarindaki [Category]'den turer
// (isaretsiz = General); Android/iOS ic nesneleri kendi sayfasinda DUZ (foldout'suz) cizilir; Scenes, Games (ekle/sil/gec) ve
// her plugin ("Plugin:<Ad>") ayri sayfa. Yeni ayar = sinifa alan eklemek (+ [Category]); yeni plugin otomatik sayfa.
// Secili sayfa/sekme EditorSettings.playerSettingsPage/Game (kullanici). Her degisiklik dosyaya yazilir (undo'suz).
[MenuItem("Project/Player Settings", 10)]
public sealed class PlayerSettingsWindow : EditorWindow
{
    const float HeaderH = InspectorPanel.HeaderH;
    const float RowH = InspectorPanel.RowH;
    const float NavW = 170f;
    const float TabH = 26f;
    const string PagePluginPrefix = "Plugin:";
    const string PageScenes = "Scenes", PageGames = "Games", PageGeneral = "General";
    const string PageAndroid = "Android", PageIos = "iOS";

    static readonly Color MissingColor = new(226, 96, 96, 255);
    static readonly Color StartColor = new(120, 200, 140, 255);
    static readonly Color NavBg = new(38, 40, 48, 255);
    static readonly Color NavSelected = new(60, 100, 180, 255);
    static readonly Color NavLine = new(24, 26, 32, 255);

    readonly ObjectDrawer _drawer = new() { KeyPrefix = "ps:" };
    readonly ObjectDrawer _androidDrawer = new() { KeyPrefix = "ps.android:" };
    readonly ObjectDrawer _iosDrawer = new() { KeyPrefix = "ps.ios:" };
    readonly Dictionary<string, ObjectDrawer> _pluginDrawers = new();
    List<PluginSettingsEditor.Entry> _plugins = new();
    readonly List<string> _sceneRows = new();
    readonly List<string> _pages = new();      // sol liste sirasi (sabit kategoriler + "Plugin:<Ad>")
    readonly List<string> _categories = new(); // PlayerSettings alanlarindan turetilen (General, Display, Build...)
    Vec2 _scroll, _navScroll;
    string _editGame = ""; // bu karede duzenlenen oyun (sekme)

    public PlayerSettingsWindow()
    {
        Title = "Player Settings";
        Utility = true; // Unity gibi: dock'lanmaz, ayri pencere
        UtilityWidth = 780;
        UtilityHeight = 660;
    }

    string Page
    {
        get => App.Settings.playerSettingsPage;
        set { if (App.Settings.playerSettingsPage != value) { App.Settings.playerSettingsPage = value; App.SaveSettings(); } }
    }

    // Duzenlenen oyun sekmesi: bos = calisilan oyun (toolbar'i izler).
    string EditGame
    {
        get => App.Settings.playerSettingsGame;
        set { if (App.Settings.playerSettingsGame != value) { App.Settings.playerSettingsGame = value; App.SaveSettings(); } }
    }

    protected override void OnGui()
    {
        var project = App.Project;
        if (project == null)
        {
            Gui.Label(new Rect(4, 4, 200, 20), "No project");
            return;
        }
        var games = project.GameNames();
        _editGame = EditGame;
        if (_editGame.Length == 0 || !games.Contains(_editGame)) _editGame = project.Game;
        var ps = project.PlayerFor(_editGame);
        if (!ReferenceEquals(_drawer.Target, ps))
        {
            _drawer.Bind(ps);
            _androidDrawer.Bind(ps.android);
            _iosDrawer.Bind(ps.ios);
        }
        _plugins = PluginSettingsEditor.Entries(project, App.Assets, _editGame);
        BuildPages();
        string page = Page;
        if (!_pages.Contains(page)) page = _pages[0];

        var vis = GuiClip.VisibleRect;
        // --- ust: oyun sekmeleri ---
        DrawGameTabs(project, games, vis.width);
        float top = TabH;
        float bodyH = vis.height - top;
        // --- sol: kategori listesi ---
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(new Rect(0, top, NavW, bodyH), NavBg, 1);
            GuiRenderer.DrawRect(new Rect(NavW - 1, top, 1, bodyH), NavLine, 1);
        }
        float navContentH = 8 + _pages.Count * RowH + RowH; // "Plugins" ara basligi icin pay
        _navScroll = Gui.BeginScrollView(new Rect(0, top, NavW, bodyH), _navScroll, new Rect(0, 0, NavW - 4, navContentH));
        float ny = 6;
        bool pluginHeaderDrawn = false;
        foreach (var p in _pages)
        {
            bool isPlugin = p.StartsWith(PagePluginPrefix, StringComparison.Ordinal);
            if (isPlugin && !pluginHeaderDrawn)
            {
                if (Event.Current.Type == EventType.Repaint)
                    GuiRenderer.DrawTextIn(new Rect(10, ny + 4, NavW - 14, RowH - 4), "PLUGINS", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
                ny += RowH;
                pluginHeaderDrawn = true;
            }
            var row = new Rect(4, ny, NavW - 10, RowH - 1);
            bool selected = p == page;
            var ev = Event.Current;
            // Secim MouseDown'da, hot control yokken (IMGUI: sayfa degisince kontrol sayisi degisir; hot varken dokunma).
            if (!selected && ev.Type == EventType.MouseDown && ev.Button == 0 && GuiUtility.HotControl == 0 && ev.MouseInClip && row.Contains(ev.MousePosition))
            {
                page = p;
                Page = p;
                _scroll = default;
                ev.Use();
            }
            if (Event.Current.Type == EventType.Repaint)
            {
                if (selected) GuiRenderer.DrawRect(row, NavSelected, 1);
                string label = isPlugin ? p.Substring(PagePluginPrefix.Length) : p;
                var col = selected ? InspectorPanel.HeaderTextColor : InspectorPanel.LabelColor;
                GuiRenderer.DrawTextIn(new Rect(isPlugin ? 22 : 12, ny, NavW - 30, RowH - 1), label, InspectorPanel.LabelFont, col, false, 2);
                if (isPlugin && PluginSettingsEditor.MissingRequired(FindPlugin(label).Instance).Count > 0)
                    GuiRenderer.DrawRect(new Rect(NavW - 20, ny + RowH / 2 - 4, 7, 7), MissingColor, 1); // [Required] eksik
            }
            ny += RowH;
        }
        Gui.EndScrollView();

        // --- sag: secili sayfa ---
        float cx = NavW + 4;
        float cw = vis.width - cx;
        float w = cw - 24;
        _drawer.LabelW = _androidDrawer.LabelW = _iosDrawer.LabelW = InspectorPanel.LabelWidthFor(w);
        float contentHeight = 4 + HeaderH + 6 + MeasurePage(page, ps, project) + 16;
        _scroll = Gui.BeginScrollView(new Rect(cx, top, cw, bodyH), _scroll, new Rect(0, 0, cw - 20, contentHeight));
        float y = 4;
        string title = page.StartsWith(PagePluginPrefix, StringComparison.Ordinal) ? "Plugin: " + page.Substring(PagePluginPrefix.Length) : page;
        title += "  — " + _editGame + (string.Equals(_editGame, project.Game, StringComparison.OrdinalIgnoreCase) ? " (working)" : "");
        DrawHeader(ref y, w, title);
        DrawPage(page, ps, project, ref y, w);
        Gui.EndScrollView();
    }

    // Oyun sekmeleri (ProjectSettings/Games/*): tiklanan oyun duzenlenir; calisilan oyun isaretli. Toolbar'i degistirmez.
    void DrawGameTabs(Project project, List<string> games, float width)
    {
        var ev = Event.Current;
        if (ev.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(new Rect(0, 0, width, TabH), NavBg, 1);
            GuiRenderer.DrawRect(new Rect(0, TabH - 1, width, 1), NavLine, 1);
            GuiRenderer.DrawTextIn(new Rect(8, 0, 44, TabH), "GAME", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor, false, 2);
        }
        float x = 52;
        foreach (var g in games)
        {
            bool working = string.Equals(g, project.Game, StringComparison.OrdinalIgnoreCase);
            string label = working ? g + " (working)" : g;
            float tw = (Gui.Font != null ? Gui.Font.TextSize(label, InspectorPanel.LabelFont).x : 60f) + 20;
            var r = new Rect(x, 3, tw, TabH - 6);
            bool selected = string.Equals(g, _editGame, StringComparison.OrdinalIgnoreCase);
            if (!selected && ev.Type == EventType.MouseDown && ev.Button == 0 && GuiUtility.HotControl == 0 && ev.MouseInClip && r.Contains(ev.MousePosition))
            {
                EditGame = g;
                _editGame = g;
                _scroll = default;
                ev.Use();
            }
            if (ev.Type == EventType.Repaint)
            {
                if (selected) GuiRenderer.DrawRect(r, NavSelected, 1);
                GuiRenderer.DrawTextIn(r, label, InspectorPanel.LabelFont, selected ? InspectorPanel.HeaderTextColor : InspectorPanel.LabelColor, true, 2);
            }
            x += tw + 4;
        }
    }

    // Sayfa listesi: PlayerSettings [Category] sirasi (ilk gorulme) + sabit ozel sayfalar + plugin'ler.
    void BuildPages()
    {
        _categories.Clear();
        _pages.Clear();
        _categories.Add(PageGeneral);
        foreach (var f in _drawer.Schema)
        {
            if (f.Hidden) continue;
            string c = f.Category ?? PageGeneral;
            if (!_categories.Contains(c)) _categories.Add(c);
        }
        // Sira: General, Scenes, sonra diger kategoriler kesif sirasiyla (Display, Themes, Build, Android, iOS), Games
        _pages.Add(PageGeneral);
        _pages.Add(PageScenes);
        foreach (var c in _categories)
            if (c != PageGeneral) _pages.Add(c);
        _pages.Add(PageGames);
        foreach (var e in _plugins) _pages.Add(PagePluginPrefix + e.Name);
    }

    PluginSettingsEditor.Entry FindPlugin(string name)
    {
        foreach (var e in _plugins) if (e.Name == name) return e;
        return null;
    }

    static bool IsCategory(SerializedType.FieldSchema f, string cat) => (f.Category ?? PageGeneral) == cat;

    float MeasurePage(string page, PlayerSettings ps, Project project)
    {
        if (page == PageScenes) { CollectSceneRows(ps); return RowH * (Math.Max(1, _sceneRows.Count) + 1) + 4 + RowH; }
        if (page == PageGames) return RowH + RowH * (project.GameNames().Count + 1) + 8;
        if (page == PageAndroid) return RowH + _androidDrawer.Measure();
        if (page == PageIos) return RowH + _iosDrawer.Measure();
        if (page.StartsWith(PagePluginPrefix, StringComparison.Ordinal))
        {
            var e = FindPlugin(page.Substring(PagePluginPrefix.Length));
            return e == null ? RowH : RowH + PluginDrawer(e).Measure();
        }
        _drawer.Filter = f => IsCategory(f, page);
        return _drawer.Measure();
    }

    void DrawPage(string page, PlayerSettings ps, Project project, ref float y, float w)
    {
        if (page == PageScenes)
        {
            Info(ref y, w, "Scenes packed into this game's build (Unity: Scenes In Build). Start = first scene; a start scene is always included.");
            if (DrawScenes(ps, ref y, w)) Save();
            return;
        }
        if (page == PageGames)
        {
            Info(ref y, w, "One project, many games (ProjectSettings/Games/<name>/): each game has its own full settings, scenes, themes and plugin configs.");
            DrawGames(project, ref y, w);
            return;
        }
        if (page == PageAndroid || page == PageIos)
        {
            Info(ref y, w, page == PageAndroid
                ? "Build Android Project → generated/app.properties (Gradle shell reads every build)."
                : "Build iOS Project → generated/app.xcconfig (Xcode shell reads every build).");
            var d = page == PageAndroid ? _androidDrawer : _iosDrawer;
            if (d.Draw(ref y, w, Save)) Save();
            return;
        }
        if (page.StartsWith(PagePluginPrefix, StringComparison.Ordinal))
        {
            var e = FindPlugin(page.Substring(PagePluginPrefix.Length));
            if (e == null) return;
            var missing = PluginSettingsEditor.MissingRequired(e.Instance);
            string info = "ProjectSettings/Games/" + e.Game + "/Plugins/" + e.Name + ".asset" + (e.Referenced ? "" : "  — not referenced by game code (Plugin.Get<" + e.Name + ">)")
                + (missing.Count > 0 ? "  — required: " + string.Join(", ", missing) : "");
            Info(ref y, w, info, missing.Count > 0 ? MissingColor : InspectorPanel.LabelDimColor);
            var d = PluginDrawer(e);
            var assets = App.Assets;
            if (d.Draw(ref y, w, () => PluginSettingsEditor.Save(e, assets)))
                PluginSettingsEditor.Save(e, assets);
            return;
        }
        _drawer.Filter = f => IsCategory(f, page);
        if (_drawer.Draw(ref y, w, Save)) Save();
    }

    static void Info(ref float y, float w, string text, Color? color = null)
    {
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(12, y, w - 12, RowH), text, InspectorPanel.SmallFont, color ?? InspectorPanel.LabelDimColor);
        y += RowH;
    }

    char[] _newGameBuf = new char[64];
    int _newGameLen;

    // Oyun listesi: ad (working isareti), "Work on" (toolbar gecisi), "Remove"; son satir: yeni ad + "Add (copy of <sekme>)".
    void DrawGames(Project project, ref float y, float w)
    {
        var names = project.GameNames();
        foreach (var g in names)
        {
            bool working = string.Equals(g, project.Game, StringComparison.OrdinalIgnoreCase);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(16, y, w - 200, RowH), g + (working ? "  (working)" : ""), InspectorPanel.LabelFont,
                    working ? StartColor : InspectorPanel.LabelColor);
            if (!working && Gui.Button(new Rect(w - 132, y + 1, 70, 18), "Work on"))
            {
                try { App.SetWorkingGame(g); }
                catch (Exception e) { EditorLog.Warning("[game] " + e.Message); }
            }
            if (names.Count > 1 && Gui.Button(new Rect(w - 56, y + 1, 60, 18), "Remove"))
            {
                try
                {
                    project.RemoveGame(g);
                    if (string.Equals(EditGame, g, StringComparison.OrdinalIgnoreCase)) EditGame = "";
                    PluginSettingsEditor.Invalidate();
                    EditorLog.Info("[game] removed: " + g);
                }
                catch (Exception e) { EditorLog.Warning("[game] " + e.Message); }
            }
            y += RowH;
        }
        Gui.TextField(new Rect(30, y + 1, w - 190, 18), _newGameBuf, ref _newGameLen);
        if (Gui.Button(new Rect(w - 152, y + 1, 156, 18), "Add (copy of " + _editGame + ")"))
        {
            string nv = new string(_newGameBuf, 0, _newGameLen).Trim();
            try
            {
                project.CreateGame(nv, _editGame);
                _newGameLen = 0;
                EditGame = nv;
                EditorLog.Info("[game] created: " + nv + " (copy of " + _editGame + ")");
            }
            catch (Exception e) { EditorLog.Warning("[game] " + e.Message); }
        }
        y += RowH;
    }

    ObjectDrawer PluginDrawer(PluginSettingsEditor.Entry e)
    {
        if (!_pluginDrawers.TryGetValue(e.Name, out var d))
            _pluginDrawers[e.Name] = d = new ObjectDrawer { KeyPrefix = "ps.plugin." + e.Name + ":" };
        if (!ReferenceEquals(d.Target, e.Instance))
            d.Bind(e.Instance);
        d.LabelW = _drawer.LabelW;
        return d;
    }

    static void DrawHeader(ref float y, float w, string title)
    {
        InspectorPanel.DrawHeaderBar(y, 0, w + 12);
        if (Event.Current.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(8, y, w - 8, HeaderH), title,
                InspectorPanel.HeaderFont, InspectorPanel.HeaderTextColor, false, 2);
        y += HeaderH + 6;
    }

    // Satirlar: projedeki tum .scene (sirali) + listede olup projede olmayanlar (missing).
    void CollectSceneRows(PlayerSettings ps)
    {
        _sceneRows.Clear();
        var assets = App.Assets;
        if (assets != null)
            foreach (var rel in assets.AllAssets.Keys)
                if (rel.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                    _sceneRows.Add(rel);
        _sceneRows.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var s in ps.BuildScenes())
            if (!ContainsIgnoreCase(_sceneRows, s))
                _sceneRows.Add(s);
    }

    // Satir: [dahil] [start] yol â€” start secimi dahil'i zorlar; start'in dahil'i kaldirilamaz.
    // Missing satir: kirmizi + "Remove" (hem listeden hem start'tan duser).
    bool DrawScenes(PlayerSettings ps, ref float y, float w)
    {
        bool changed = false;
        if (_sceneRows.Count == 0)
        {
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(12, y, w - 12, RowH), "Projede .scene yok",
                    InspectorPanel.LabelFont, InspectorPanel.LabelDimColor);
            y += RowH;
            return false;
        }
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawTextIn(new Rect(8, y, 40, 18), "Build", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
            GuiRenderer.DrawTextIn(new Rect(48, y, 40, 18), "Start", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
            GuiRenderer.DrawTextIn(new Rect(86, y, w - 86, 18), "Scene", InspectorPanel.SmallFont, InspectorPanel.LabelDimColor);
        }
        y += RowH;
        var assets = App.Assets;
        foreach (var rel in _sceneRows)
        {
            bool exists = assets != null && assets.AllAssets.ContainsKey(rel);
            bool isStart = string.Equals(ps.startScene, rel, StringComparison.OrdinalIgnoreCase);
            bool included = isStart || ContainsIgnoreCase(ps.scenes, rel);

            bool nextIncluded = Gui.Toggle(new Rect(16, y + 2, 18, 18), included);
            if (nextIncluded != included && !isStart)
            {
                SetIncluded(ps, rel, nextIncluded);
                changed = true;
            }

            bool nextStart = Gui.Toggle(new Rect(56, y + 2, 18, 18), isStart);
            if (nextStart && !isStart)
            {
                ps.startScene = rel;
                SetIncluded(ps, rel, true);
                changed = true;
            }

            float textX = 90;
            float textW = w - textX - (exists ? 0 : 70);
            if (Event.Current.Type == EventType.Repaint)
            {
                var color = !exists ? MissingColor : isStart ? StartColor : InspectorPanel.LabelColor;
                string text = exists ? rel : rel + "  (missing)";
                GuiRenderer.DrawTextIn(new Rect(textX, y + 2, textW, 18), text, InspectorPanel.LabelFont, color);
            }
            if (!exists && Gui.Button(new Rect(w - 64, y + 2, 64, 18), "Remove"))
            {
                SetIncluded(ps, rel, false);
                if (isStart)
                    ps.startScene = FirstOrEmpty(ps.scenes);
                changed = true;
            }
            y += RowH;
        }
        y += 4;
        return changed;
    }

    static void SetIncluded(PlayerSettings ps, string rel, bool on)
    {
        int idx = ps.scenes.FindIndex(s => string.Equals(s, rel, StringComparison.OrdinalIgnoreCase));
        if (on && idx < 0)
            ps.scenes.Add(rel);
        else if (!on && idx >= 0)
            ps.scenes.RemoveAt(idx);
    }

    static bool ContainsIgnoreCase(List<string> list, string s)
    {
        foreach (var x in list)
            if (string.Equals(x, s, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    static string FirstOrEmpty(List<string> list) => list.Count > 0 ? list[0] : "";

    void Save() => App.Project.SavePlayerSettings(_editGame);
}
