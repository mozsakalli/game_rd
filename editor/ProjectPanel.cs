using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Proje paneli (asset browser): Assets/ altindaki guid'li dosyalari klasor klasor
// gezdirir. Dosya satirini surukle = DragDrop.BeginAsset (Inspector alanlarina
// birakilir). Satirlar yalniz gerektiginde (klasor degisince/yenilemede) kurulur.
// CONTAINER asset (index yayan importer: psd, font...) klasor gibi gezilir; icindeki alt ogeler
// "yol#id" anahtariyla secilir/suruklenir. Panel dosya tipi bilmez: SubAssets + ThumbnailCache.
// Gorunum: liste (ikon + ad) / izgara (buyuk kucuk resim); tercih EditorSettings'te.
[MenuItem("Window/Project", 2)]
public sealed class ProjectPanel : EditorWindow
{
    static readonly int _dragHash = "Project.Drag".GetHashCode();

    string _dir = "";          // gorunen klasor YA DA container yolu (Assets'e goreli, "" = kok)
    bool _dirIsContainer;
    bool _dirty = true;        // satir cache'i yeniden kurulsun
    int _seenListVersion = -1; // AssetWatcher.FileListVersion takibi
    int _seenChangeVersion = -1;
    readonly List<AssetTree.Node> _rows = new();
    Vec2 _scroll;

    int _pressRow = -1;
    Vec2 _pressPos;
    bool _dragging;
    bool _pendingToggleGrid; // hot control yokken uygulanir (kontrol sayisi sabit kalsin)
    int _pendingCellDelta;

    const float RowH = 20f;
    const float DragThreshold = 5f;
    const float GridGap = 6f, GridLabelH = 16f;
    const int CellMin = 48, CellMax = 160, CellStep = 16;

    public ProjectPanel() => Title = "Project";

    // Benzersiz "ad", "ad 1", "ad 2"... yolu uretir.
    static string UniquePath(string dir, string name, string ext)
    {
        for (int i = 0; ; i++)
        {
            string p = System.IO.Path.Combine(dir, i == 0 ? name + ext : $"{name} {i}{ext}");
            if (!System.IO.File.Exists(p) && !System.IO.Directory.Exists(p))
                return p;
        }
    }

    void CreateFolder(AssetDatabase assets, string rel)
    {
        System.IO.Directory.CreateDirectory(
            UniquePath(System.IO.Path.Combine(assets.Root, rel), "New Folder", ""));
        _dirty = true;
    }

    void CreateScene(AssetDatabase assets, string rel)
    {
        string path = UniquePath(System.IO.Path.Combine(assets.Root, rel), "New Scene", ".scene");
        AssetWatcher.NoteSelfWrite(path);
        System.IO.File.WriteAllText(path, new SceneDoc().ToYaml());
        assets.ScanMetas(createMissing: true); // guid alsin
        _dirty = true;
    }

    // [CreateAssetMenu] tipten varsayilan degerli yeni asset dosyasi.
    void CreateAsset(AssetDatabase assets, string rel, Type type)
    {
        var ca = (CreateAssetMenuAttribute)Attribute.GetCustomAttribute(type, typeof(CreateAssetMenuAttribute));
        string name = string.IsNullOrEmpty(ca?.FileName) ? type.Name : ca.FileName;
        string path = UniquePath(System.IO.Path.Combine(assets.Root, rel), name, ".asset");
        AssetWatcher.NoteSelfWrite(path);
        ObjectSerializer.Save(Activator.CreateInstance(type), path, assets);
        assets.ScanMetas(createMissing: true);
        _dirty = true;
    }

    // Create/prefab-drop hedef klasoru: container icindeyken sahibin klasoru.
    string TargetDir => _dirIsContainer ? AssetTree.Parent(_dir) : _dir;

    void Enter(string rel)
    {
        _dir = rel;
        _dirty = true;
        _scroll = default;
    }

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var ev = Event.Current;
        var assets = App.Assets;
        if (assets == null)
            return;
        var settings = App.Settings;

        if (Gui.Button(new Rect(2, 2, 28, 20), "^") && _dir.Length > 0)
            Enter(AssetTree.Parent(_dir));
        if (Gui.Button(new Rect(34, 2, 70, 20), "Refresh"))
        {
            assets.ScanMetas(createMissing: true);
            _dirty = true;
        }
        // Gorunum kontrolleri (sag): List/Grid + hucre boyutu. Sayi sabit; uygulama hot control yokken.
        if (Gui.Button(new Rect(vis.width - 118, 2, 50, 20), settings.projectGrid ? "List" : "Grid"))
            _pendingToggleGrid = true;
        if (Gui.Button(new Rect(vis.width - 64, 2, 28, 20), "-"))
            _pendingCellDelta -= CellStep;
        if (Gui.Button(new Rect(vis.width - 32, 2, 28, 20), "+"))
            _pendingCellDelta += CellStep;
        if (GuiUtility.HotControl == 0 && (_pendingToggleGrid || _pendingCellDelta != 0))
        {
            if (_pendingToggleGrid)
                settings.projectGrid = !settings.projectGrid;
            if (_pendingCellDelta != 0)
                settings.projectCell = Math.Clamp(settings.projectCell + _pendingCellDelta, CellMin, CellMax);
            _pendingToggleGrid = false;
            _pendingCellDelta = 0;
        }

        // Native sag-tik menusu: sabit ogeler + [Serializable] tiplerden OTOMATIK
        // Create/<Tip> girdileri (editor tipleri bilmez, kesif attribute'tan).
        if (ev.Type == EventType.MouseDown && ev.Button == 1 && vis.Contains(ev.MousePosition))
        {
            string dir = TargetDir;
            var items = new List<(string, Action)>
            {
                ("Create/Folder", () => CreateFolder(assets, dir)),
                ("Create/Scene", () => CreateScene(assets, dir)),
            };
            if (App.AssetTypes.Count > 0)
                items.Add(("Create/-", null));
            foreach (var t in App.AssetTypes)
            {
                var tt = t;
                var ca = (CreateAssetMenuAttribute)Attribute.GetCustomAttribute(tt, typeof(CreateAssetMenuAttribute));
                string menu = string.IsNullOrEmpty(ca?.MenuName) ? tt.Name : ca.MenuName;
                items.Add(($"Create/{menu}", () => CreateAsset(assets, dir, tt)));
            }
            items.Add(("-", null));
            items.Add(("Refresh", () => { assets.ScanMetas(createMissing: true); _dirty = true; }));
            EditorMenu.ShowContext(items.ToArray());
            ev.Use();
        }
        if (ev.Type == EventType.Repaint)
            GuiRenderer.DrawTextIn(new Rect(110, 2, vis.width - 232, 20),
                _dir.Length > 0 ? _dir : "Assets", Gui.FontSize - 3f, new Color(150, 153, 163, 255), false, 2);

        // Hiyerarsiden GO birakma: gorunen klasore .prefab yaz + instance'a bagla (connect).
        if (ev.Type == EventType.Repaint && DragDrop.Kind == DragDrop.Payload.SceneObject
            && vis.Contains(ev.MousePosition))
        {
            string dropDir = TargetDir;
            int goId = DragDrop.GoId;
            DragDrop.RegisterTarget(() =>
            {
                var es = App.EditScene;
                var g = es?.FindGo(goId);
                if (g == null)
                    return;
                string path = UniquePath(System.IO.Path.Combine(assets.Root, dropDir), g.Name, ".prefab");
                if (es.CreatePrefabAsset(g, path))
                    _dirty = true;
            });
        }
        const float toolbarH = 26f;

        if (_seenListVersion != AssetWatcher.FileListVersion || _seenChangeVersion != AssetWatcher.AssetChangeVersion)
        {
            _seenListVersion = AssetWatcher.FileListVersion;
            _seenChangeVersion = AssetWatcher.AssetChangeVersion; // reimport: container uyeligi/index degismis olabilir
            _dirty = true;
        }
        if (_dirty)
            RebuildRows(assets);

        int idDrag = GuiUtility.GetControlID(_dragHash, FocusType.Passive);
        bool grid = settings.projectGrid;
        float cell = settings.projectCell;
        float viewW = vis.width - 20;
        int cols = grid ? Math.Max(1, (int)((viewW - GridGap) / (cell + GridGap))) : 1;
        float cellH = cell + GridLabelH + GridGap;
        int rowsCount = grid ? (_rows.Count + cols - 1) / cols : _rows.Count;
        var view = new Rect(0, 0, viewW, grid ? rowsCount * cellH + GridGap : _rows.Count * RowH);
        _scroll = Gui.BeginScrollView(new Rect(0, toolbarH, vis.width, vis.height - toolbarH), _scroll, view);
        var clipVis = GuiClip.VisibleRect;

        for (int i = 0; i < _rows.Count; i++)
        {
            Rect item, enterZone;
            if (grid)
            {
                int c = i % cols, r = i / cols;
                item = new Rect(GridGap + c * (cell + GridGap), GridGap + r * cellH, cell, cell + GridLabelH);
                enterZone = new Rect(item.x, item.y, 22, 22); // sol ust kose: klasor/container'a gir
            }
            else
            {
                item = new Rect(0, i * RowH, view.width, RowH - 1);
                enterZone = new Rect(0, item.y, 24, item.height);
            }
            // Gorunur degilse ne event ne cizim (buyuk klasorde ucuz).
            if (item.yMax < clipVis.y || item.y > clipVis.yMax)
                continue;
            var row = _rows[i];

            if (ev.Type == EventType.MouseDown && ev.Button == 0 && ev.MouseInClip && item.Contains(ev.MousePosition))
            {
                if (row.Kind == AssetTree.Kind.Folder || (row.Kind == AssetTree.Kind.Container && (ev.ClickCount == 2 || enterZone.Contains(ev.MousePosition))))
                {
                    Enter(row.Key);
                }
                else if (ev.ClickCount == 2 && row.Key.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                {
                    App.OpenScene(System.IO.Path.Combine(assets.Root, row.Key));
                }
                else if (ev.ClickCount == 2 && row.Key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    App.OpenPrefab(System.IO.Path.Combine(assets.Root, row.Key));
                }
                else
                {
                    _pressRow = i;
                    _pressPos = ev.MousePosition;
                    GuiUtility.HotControl = idDrag;
                }
                ev.Use();
            }

            if (ev.Type == EventType.Repaint)
            {
                bool selected = row.Kind != AssetTree.Kind.Folder && IsSelected(assets, row);
                if (selected)
                    GuiRenderer.DrawRect(item, new Color(60, 100, 180, 255), 1); // secili: hiyerarsiyle ayni mavi
                else if (row.Kind != AssetTree.Kind.Folder && DragDrop.Active && DragDrop.AssetGuid == row.RefKey)
                    GuiRenderer.DrawRect(item, new Color(60, 100, 180, 120), 1);
                if (grid)
                    DrawGridCell(row, item, cell);
                else
                    DrawListRow(row, item);
            }
        }

        switch (ev.GetTypeForControl(idDrag))
        {
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == idDrag && _pressRow >= 0)
                {
                    float dx = ev.MousePosition.x - _pressPos.x, dy = ev.MousePosition.y - _pressPos.y;
                    if (!_dragging && dx * dx + dy * dy > DragThreshold * DragThreshold)
                    {
                        _dragging = true;
                        var r = _rows[_pressRow];
                        DragDrop.BeginAsset(r.RefKey, r.Key);
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == idDrag)
                {
                    GuiUtility.HotControl = 0;
                    if (!_dragging && _pressRow >= 0 && _pressRow < _rows.Count)
                        Select(assets, _rows[_pressRow]);
                    _pressRow = -1;
                    _dragging = false;
                    // Drop'u DragDrop.EndFrame uygular/temizler (kayitli hedefe).
                    ev.Use();
                }
                break;
        }
        Gui.EndScrollView();
    }

    static void Select(AssetDatabase assets, in AssetTree.Node row)
    {
        if (row.Kind == AssetTree.Kind.Sub)
        {
            string owner = SubAssets.Split(row.Key, out string id);
            Selection.SelectSubAsset(System.IO.Path.Combine(assets.Root, owner), id);
        }
        else
            Selection.AssetPath = System.IO.Path.Combine(assets.Root, row.Key);
    }

    static bool IsSelected(AssetDatabase assets, in AssetTree.Node row)
    {
        if (Selection.AssetPath == null)
            return false;
        string owner = SubAssets.Split(row.Key, out string id);
        return Selection.AssetPath == System.IO.Path.Combine(assets.Root, owner) && Selection.AssetSub == id;
    }

    // ---- cizim ----

    static readonly Color DirColor = new(200, 190, 140, 255);
    static readonly Color FileColor = new(214, 218, 228, 255);
    static readonly Color DimColor = new(150, 153, 163, 255);

    void DrawListRow(in AssetTree.Node row, in Rect item)
    {
        var icon = new Rect(4, item.y + 2, 16, 16);
        if (row.Kind != AssetTree.Kind.Folder && !ThumbnailCache.Draw(row.Key, icon))
            DrawIcon(row, icon);
        else if (row.Kind == AssetTree.Kind.Folder)
            DrawIcon(row, icon);
        GuiRenderer.DrawTextIn(new Rect(24, item.y, item.width - 28, item.height), row.Name, Gui.FontSize - 2f,
            row.Enterable ? DirColor : FileColor, false, 2);
    }

    void DrawGridCell(in AssetTree.Node row, in Rect item, float cell)
    {
        var box = new Rect(item.x + 4, item.y + 4, cell - 8, cell - 8);
        GuiRenderer.DrawRect(box, new Color(38, 41, 50, 255), 1);
        var inner = new Rect(box.x + 3, box.y + 3, box.width - 6, box.height - 6);
        if (row.Kind == AssetTree.Kind.Folder || !ThumbnailCache.Draw(row.Key, inner))
            DrawIcon(row, inner);
        if (row.Kind == AssetTree.Kind.Container)
            GuiRenderer.DrawTextIn(new Rect(item.x + 2, item.y + 2, 20, 18), ">", Gui.FontSize - 3f, DirColor, true, 3);
        GuiRenderer.DrawTextIn(new Rect(item.x, item.y + cell, cell, GridLabelH), ObjectDrawer.Ellipsize(row.Name, cell - 8, Gui.FontSize - 3f),
            Gui.FontSize - 3f, row.Enterable ? DirColor : FileColor, true, 2);
    }

    // Tip ikonu (kucuk resim yoksa): klasor sekli ya da uzantiya gore renkli rozet + kisa etiket.
    // Dosya tipi bilgisi yalniz GORSEL ipucu; davranis yok. Yeni tipler varsayilan rozetle gelir.
    static void DrawIcon(in AssetTree.Node row, in Rect r)
    {
        float s = Math.Min(r.width, r.height);
        var box = new Rect(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f, s, s);
        if (row.Kind is AssetTree.Kind.Folder or AssetTree.Kind.Container)
        {
            // Klasor: sekme + govde; container govdeye kucuk izgara.
            var tab = new Rect(box.x + s * 0.06f, box.y + s * 0.2f, s * 0.4f, s * 0.14f);
            var body = new Rect(box.x + s * 0.06f, box.y + s * 0.3f, s * 0.88f, s * 0.52f);
            GuiRenderer.DrawRect(tab, DirColor, 2);
            GuiRenderer.DrawRect(body, DirColor, 2);
            if (row.Kind == AssetTree.Kind.Container && s >= 24)
            {
                var dark = new Color(60, 56, 44, 255);
                float g = body.width * 0.18f, gap = body.width * 0.08f;
                for (int yy = 0; yy < 2; yy++)
                    for (int xx = 0; xx < 3; xx++)
                        GuiRenderer.DrawRect(new Rect(body.x + gap + xx * (g + gap), body.y + gap + yy * (g + gap), g, g), dark, 3);
            }
            return;
        }
        string ext = System.IO.Path.GetExtension(row.Key).ToLowerInvariant();
        (Color col, string tag) = ext switch
        {
            ".scene" => (new Color(70, 120, 200, 255), "Sc"),
            ".prefab" => (new Color(90, 160, 220, 255), "Pf"),
            ".asset" => (new Color(120, 120, 150, 255), "As"),
            ".ttf" or ".otf" => (new Color(190, 150, 90, 255), "Aa"),
            ".mp3" or ".ogg" or ".wav" => (new Color(150, 110, 190, 255), "Au"),
            ".fx" => (new Color(90, 170, 130, 255), "Fx"),
            ".cs" => (new Color(100, 160, 110, 255), "C#"),
            ".svg" => (new Color(200, 120, 90, 255), "Sv"),
            _ => (new Color(95, 100, 115, 255), ""),
        };
        if (row.Kind == AssetTree.Kind.Sub)
        {
            col = new Color(110, 130, 160, 255);
            tag = "";
        }
        var page = new Rect(box.x + s * 0.18f, box.y + s * 0.08f, s * 0.64f, s * 0.84f);
        GuiRenderer.DrawRect(page, col, 2);
        if (tag.Length > 0 && s >= 20)
            GuiRenderer.DrawTextIn(page, tag, Math.Clamp(s * 0.34f, 8f, 28f), new Color(20, 22, 28, 255), true, 3);
    }

    // ---- satirlar ----

    void RebuildRows(AssetDatabase assets)
    {
        _dirty = false;
        // Container index'i kaybolduysa (reimport sonrasi bos) klasore don.
        if (_dir.Length > 0 && assets.AllAssets.ContainsKey(_dir) && !SubAssets.IsContainer(_dir))
            _dir = AssetTree.Parent(_dir);
        _dirIsContainer = AssetTree.IsContainerPath(assets, _dir);
        AssetTree.List(assets, _dir, _rows);
    }
}
