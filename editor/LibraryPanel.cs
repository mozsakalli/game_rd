using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Library paneli: UI elemani paleti. ELEMAN = PREFAB (Assets/Library altindaki .prefab'lar);
// sablon degisince instance'lar prefab sistemiyle guncellenir — ayri bir eleman kavrami YOK.
// Kategori = Library altindaki alt klasor. Kucuk resim = PrefabThumbs (ThumbnailCache).
//   tek tik   : sec (Inspector)      cift tik : secili GO'nun icine ekle
//   surukle   : Scene View / Hierarchy'e birak (DragDrop.BeginAsset)
//   Hierarchy'den GO birak : secili kategoriye .prefab yaz + instance'a bagla ("kendi yaptigini library'ye at")
// Panel dosya tipi/component bilmez: liste importer tipinden (Prefab), yerlesim ElementPlacement'tan.
[MenuItem("Window/Library", 2)]
public sealed class LibraryPanel : EditorWindow
{
    public const string LibraryRel = "Library"; // Assets'e goreli kok klasor
    static readonly int _dragHash = "Library.Drag".GetHashCode();

    struct Item
    {
        public string Key;      // Assets'e goreli yol (thumbnail/drag anahtari)
        public string Guid;
        public string Name;     // dosya adi (uzantisiz)
        public string Category; // "" = kok
    }

    readonly List<Item> _all = new();
    readonly List<int> _visible = new(); // filtre sonrasi _all indeksleri
    readonly List<string> _categories = new();
    string _category = null; // null = All
    bool _dirty = true;
    int _seenListVersion = -1, _seenChangeVersion = -1;
    Vec2 _scroll;
    char[] _search = new char[64];
    int _searchLen;
    string _searchText = "";

    int _pressItem = -1;
    Vec2 _pressPos;
    bool _dragging;
    int _targetGo; // Library'ye tiklamadan onceki GO secimi (cift tik / Add to Scene hedefi)

    const float ToolbarH = 26f, Cell = 72f, LabelH = 16f, Gap = 8f, DragThreshold = 5f;

    public LibraryPanel() => Title = "Library";

    static string LibraryDir(AssetDatabase assets) => Path.Combine(assets.Root, LibraryRel);
    string TargetDir(AssetDatabase assets)
        => _category == null || _category.Length == 0 ? LibraryDir(assets) : Path.Combine(LibraryDir(assets), _category);

    void Rebuild(AssetDatabase assets)
    {
        _dirty = false;
        _all.Clear();
        _categories.Clear();
        string prefix = LibraryRel + "/";
        foreach (var kv in assets.AllAssets)
        {
            string key = kv.Key;
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || AssetDatabase.ImportTypeOf(key) != typeof(Prefab))
                continue;
            string rest = key.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            string cat = slash < 0 ? "" : rest.Substring(0, slash);
            _all.Add(new Item { Key = key, Guid = kv.Value, Name = Path.GetFileNameWithoutExtension(key), Category = cat });
            if (cat.Length > 0 && !_categories.Contains(cat))
                _categories.Add(cat);
        }
        _all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        _categories.Sort(StringComparer.OrdinalIgnoreCase);
        if (_category != null && _category.Length > 0 && !_categories.Contains(_category))
            _category = null;
        Filter();
    }

    void Filter()
    {
        _visible.Clear();
        for (int i = 0; i < _all.Count; i++)
        {
            var it = _all[i];
            if (_category != null && !string.Equals(it.Category, _category, StringComparison.OrdinalIgnoreCase))
                continue;
            if (_searchText.Length > 0 && it.Name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            _visible.Add(i);
        }
    }

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var ev = Event.Current;
        var assets = App.Assets;
        if (assets == null)
            return;
        if (_seenListVersion != AssetWatcher.FileListVersion || _seenChangeVersion != AssetWatcher.AssetChangeVersion)
        {
            _seenListVersion = AssetWatcher.FileListVersion;
            _seenChangeVersion = AssetWatcher.AssetChangeVersion;
            _dirty = true;
        }
        if (_dirty)
            Rebuild(assets);

        // Arac cubugu: kategori sekmeleri (All + alt klasorler) + arama.
        float x = 2f;
        x += Tab(new Rect(x, 2, 0, 20), "All", _category == null, () => { _category = null; Filter(); });
        foreach (var cat in _categories)
        {
            string c = cat;
            x += Tab(new Rect(x, 2, 0, 20), c, string.Equals(_category, c, StringComparison.OrdinalIgnoreCase), () => { _category = c; Filter(); });
        }
        float searchW = MathF.Min(180f, MathF.Max(80f, vis.width - x - 6f));
        Gui.TextField(new Rect(vis.width - searchW - 2, 2, searchW, 20), _search, ref _searchLen);
        string st = new string(_search, 0, _searchLen);
        if (st != _searchText) { _searchText = st; Filter(); }
        if (ev.Type == EventType.Repaint && _searchLen == 0 && GuiUtility.KeyboardControl != GuiUtility.LastControlID)
            GuiRenderer.DrawTextIn(new Rect(vis.width - searchW - 2, 2, searchW, 20), "Search", Gui.FontSize - 3f, Dim, false, 3);

        // Hierarchy'den GO birakma: secili kategoriye prefab yaz + bagla.
        if (ev.Type == EventType.Repaint && DragDrop.Kind == DragDrop.Payload.SceneObject && vis.Contains(ev.MousePosition))
        {
            int goId = DragDrop.GoId;
            string dir = TargetDir(assets);
            GuiRenderer.DrawRect(new Rect(vis.x, vis.y + ToolbarH, vis.width, vis.height - ToolbarH), new Color(60, 100, 180, 60), 1);
            DragDrop.RegisterTarget(() =>
            {
                var es = App.EditScene;
                var g = es?.FindGo(goId);
                if (g == null)
                    return;
                Directory.CreateDirectory(dir);
                string path = UniquePath(dir, g.Name, ".prefab");
                if (es.CreatePrefabAsset(g, path))
                {
                    assets.ScanMetas(createMissing: true);
                    _dirty = true;
                }
            });
        }

        int idDrag = GuiUtility.GetControlID(_dragHash, FocusType.Passive);
        float viewW = vis.width - 20;
        int cols = Math.Max(1, (int)((viewW - Gap) / (Cell + Gap)));
        float cellH = Cell + LabelH + Gap;
        int rows = (_visible.Count + cols - 1) / cols;
        var view = new Rect(0, 0, viewW, MathF.Max(rows * cellH + Gap, 60f));
        _scroll = Gui.BeginScrollView(new Rect(0, ToolbarH, vis.width, vis.height - ToolbarH), _scroll, view);
        var clipVis = GuiClip.VisibleRect;

        if (_visible.Count == 0)
        {
            if (ev.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(Gap, Gap, viewW - Gap * 2, 20),
                    _all.Count == 0 ? "No elements in Assets/" + LibraryRel : "No matching elements", Gui.FontSize - 2f, Dim, false, 2);
            if (_all.Count == 0 && Gui.Button(new Rect(Gap, Gap + 26, 160, 22), "Add starter elements"))
                StarterKit.Install(assets);
        }

        for (int vi = 0; vi < _visible.Count; vi++)
        {
            int c = vi % cols, r = vi / cols;
            var item = new Rect(Gap + c * (Cell + Gap), Gap + r * cellH, Cell, Cell + LabelH);
            if (item.yMax < clipVis.y || item.y > clipVis.yMax)
                continue;
            var it = _all[_visible[vi]];

            if (ev.Type == EventType.MouseDown && ev.Button == 1 && ev.MouseInClip && item.Contains(ev.MousePosition))
            {
                if (Selection.DocId != 0) _targetGo = Selection.DocId;
                Select(assets, it);
                ShowContextMenu(assets, it);
                ev.Use();
            }
            if (ev.Type == EventType.MouseDown && ev.Button == 0 && ev.MouseInClip && item.Contains(ev.MousePosition))
            {
                if (ev.ClickCount == 2)
                    ElementPlacement.AddToSelection(it.Guid, _targetGo);
                else
                {
                    if (Selection.DocId != 0) _targetGo = Selection.DocId;
                    _pressItem = vi;
                    _pressPos = ev.MousePosition;
                    GuiUtility.HotControl = idDrag;
                }
                ev.Use();
            }
            if (ev.Type == EventType.Repaint)
            {
                bool selected = Selection.AssetPath != null && Selection.AssetSub == null
                    && string.Equals(Selection.AssetPath, Path.Combine(assets.Root, it.Key), StringComparison.OrdinalIgnoreCase);
                if (selected)
                    GuiRenderer.DrawRect(item, new Color(60, 100, 180, 255), 1);
                else if (DragDrop.Active && DragDrop.AssetGuid == it.Guid)
                    GuiRenderer.DrawRect(item, new Color(60, 100, 180, 120), 1);
                DrawCell(it, item);
            }
        }

        switch (ev.GetTypeForControl(idDrag))
        {
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == idDrag && _pressItem >= 0)
                {
                    float dx = ev.MousePosition.x - _pressPos.x, dy = ev.MousePosition.y - _pressPos.y;
                    if (!_dragging && dx * dx + dy * dy > DragThreshold * DragThreshold && _pressItem < _visible.Count)
                    {
                        _dragging = true;
                        var it = _all[_visible[_pressItem]];
                        DragDrop.BeginAsset(it.Guid, it.Key);
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == idDrag)
                {
                    GuiUtility.HotControl = 0;
                    if (!_dragging && _pressItem >= 0 && _pressItem < _visible.Count)
                        Select(assets, _all[_visible[_pressItem]]);
                    _pressItem = -1;
                    _dragging = false;
                    ev.Use(); // drop'u DragDrop.EndFrame uygular
                }
                break;
        }
        Gui.EndScrollView();

        if (ev.Type == EventType.MouseDown && ev.Button == 1 && vis.Contains(ev.MousePosition))
        {
            ShowContextMenu(assets, null);
            ev.Use();
        }
    }

    // Sekme butonu: genisligi metinden; secili olan vurgulu. Dondurdugu deger = kaplanan genislik.
    static float Tab(Rect r, string label, bool active, Action onClick)
    {
        float w = (Gui.Font?.TextSize(label, Gui.FontSize - 2f).x ?? 40f) + 16f;
        r.width = w;
        if (Event.Current.Type == EventType.Repaint && active)
            GuiRenderer.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(90, 150, 230, 255), 3);
        if (Gui.Button(r, label))
            onClick();
        return w + 2f;
    }

    static void Select(AssetDatabase assets, in Item it)
        => Selection.AssetPath = Path.Combine(assets.Root, it.Key);

    void ShowContextMenu(AssetDatabase assets, Item? itemOrNull)
    {
        var items = new List<(string, Action)>();
        if (itemOrNull is Item it)
        {
            string guid = it.Guid, key = it.Key;
            int target = _targetGo;
            items.Add(("Add to Scene", () => ElementPlacement.AddToSelection(guid, target)));
            items.Add(("Edit Prefab", () => App.OpenPrefab(Path.Combine(assets.Root, key))));
            items.Add(("-", null));
            if (AssetOps.IsOperable(key))
            {
                items.Add(("Rename...", () => TextPromptPopup.Show("Rename " + it.Name, it.Name + ".prefab",
                    newName => AssetOps.Rename(key, newName), it.Name.Length)));
                items.Add(("Delete...", () => ConfirmPopup.Show($"Delete \"{it.Name}\"?",
                    "Instances in scenes keep their last expanded state but lose the link.", "Delete", () => AssetOps.Delete(key))));
            }
            items.Add(("Show in Explorer", () => AssetOps.ShowInExplorer(key)));
            items.Add(("-", null));
        }
        items.Add(("New Category...", () => TextPromptPopup.Show("New category", "Category", name =>
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            Directory.CreateDirectory(Path.Combine(LibraryDir(assets), name.Trim()));
            assets.ScanMetas(createMissing: true);
            _dirty = true;
        })));
        items.Add(("Add starter elements", () => StarterKit.Install(assets)));
        items.Add(("Refresh", () => { assets.ScanMetas(createMissing: true); _dirty = true; }));
        EditorMenu.ShowContext(items.ToArray());
    }

    static string UniquePath(string dir, string name, string ext)
    {
        for (int i = 0; ; i++)
        {
            string p = Path.Combine(dir, i == 0 ? name + ext : $"{name} {i}{ext}");
            if (!File.Exists(p) && !Directory.Exists(p))
                return p;
        }
    }

    // ---- cizim ----

    static readonly Color Dim = new(150, 153, 163, 255);
    static readonly Color FileColor = new(214, 218, 228, 255);

    static void DrawCell(in Item it, in Rect item)
    {
        var box = new Rect(item.x + 3, item.y + 3, Cell - 6, Cell - 6);
        GuiRenderer.DrawRect(box, new Color(38, 41, 50, 255), 1);
        var inner = new Rect(box.x + 4, box.y + 4, box.width - 8, box.height - 8);
        if (!ThumbnailCache.Draw(it.Key, inner))
        {
            // Kucuk resim henuz yok: rozet + bas harf.
            GuiRenderer.DrawRect(new Rect(inner.x + inner.width * 0.2f, inner.y + inner.height * 0.2f, inner.width * 0.6f, inner.height * 0.6f),
                new Color(70, 110, 170, 255), 2);
            GuiRenderer.DrawTextIn(inner, it.Name.Length > 0 ? it.Name.Substring(0, 1).ToUpperInvariant() : "?", Gui.FontSize + 4f, Color.White, true, 3);
        }
        GuiRenderer.DrawTextIn(new Rect(item.x, item.y + Cell, Cell, LabelH), ObjectDrawer.Ellipsize(it.Name, Cell - 6, Gui.FontSize - 3f),
            Gui.FontSize - 3f, FileColor, true, 2);
    }
}

// Baslangic elemanlari: editor assembly'sine gomulu prefab YAML'lari (Resources/Library/*.prefab)
// projenin Assets/Library klasorune kopyalar (var olanin ustune yazmaz). Font alani bos → proje
// varsayilan fontu (Fonts.Resolve("")).
static class StarterKit
{
    public static int Install(AssetDatabase assets)
    {
        string dir = Path.Combine(assets.Root, LibraryPanel.LibraryRel);
        Directory.CreateDirectory(dir);
        var asm = typeof(StarterKit).Assembly;
        int n = 0;
        foreach (var res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("Library/", StringComparison.Ordinal) || !res.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;
            string path = Path.Combine(dir, res.Substring("Library/".Length));
            if (File.Exists(path))
                continue;
            using var s = asm.GetManifestResourceStream(res);
            if (s == null)
                continue;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            AssetWatcher.NoteSelfWrite(path);
            File.WriteAllBytes(path, ms.ToArray());
            n++;
        }
        if (n > 0)
            assets.ScanMetas(createMissing: true);
        EditorLog.Info($"[library] {n} starter element(s) added to Assets/{LibraryPanel.LibraryRel}");
        return n;
    }
}
