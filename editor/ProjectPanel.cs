using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Proje paneli (asset browser): Assets/ altindaki guid'li dosyalari klasor klasor
// gezdirir. Dosya satirini surukle = DragDrop.BeginAsset (Inspector alanlarina
// birakilir). Satirlar yalniz gerektiginde (klasor degisince/yenilemede) kurulur.
[MenuItem("Window/Project", 2)]
public sealed class ProjectPanel : EditorWindow
{
    static readonly int _dragHash = "Project.Drag".GetHashCode();

    string _dir = "";          // gorunen klasor (Assets'e goreli, "" = kok)
    bool _dirty = true;        // satir cache'i yeniden kurulsun
    readonly List<(string Display, string Rel, bool IsDir, string Guid)> _rows = new();
    Vec2 _scroll;

    int _pressRow = -1;
    Vec2 _pressPos;
    bool _dragging;

    const float RowH = 20f;
    const float DragThreshold = 5f;

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

    protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var ev = Event.Current;
        var assets = App.Assets;
        if (assets == null)
            return;

        if (Gui.Button(new Rect(2, 2, 28, 20), "^") && _dir.Length > 0)
        {
            int cut = _dir.LastIndexOf('/');
            _dir = cut > 0 ? _dir.Substring(0, cut) : "";
            _dirty = true;
        }
        if (Gui.Button(new Rect(34, 2, 70, 20), "Refresh"))
        {
            assets.ScanMetas(createMissing: true);
            _dirty = true;
        }
        // Native sag-tik menusu: sabit ogeler + [Serializable] tiplerden OTOMATIK
        // Create/<Tip> girdileri (editor tipleri bilmez, kesif attribute'tan).
        if (ev.Type == EventType.MouseDown && ev.Button == 1 && vis.Contains(ev.MousePosition))
        {
            string dir = _dir;
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
            GuiRenderer.DrawTextIn(new Rect(110, 2, vis.width - 114, 20),
                _dir.Length > 0 ? _dir : "Assets", Gui.FontSize - 3f, new Color(150, 153, 163, 255), false, 2);

        // Hiyerarsiden GO birakma: gorunen klasore .prefab yaz + instance'a bagla (connect).
        if (ev.Type == EventType.Repaint && DragDrop.Kind == DragDrop.Payload.SceneObject
            && vis.Contains(ev.MousePosition))
        {
            string dropDir = _dir;
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

        if (_dirty)
            RebuildRows(assets);

        int idDrag = GuiUtility.GetControlID(_dragHash, FocusType.Passive);
        var view = new Rect(0, 0, vis.width - 20, _rows.Count * RowH);
        _scroll = Gui.BeginScrollView(new Rect(0, toolbarH, vis.width, vis.height - toolbarH), _scroll, view);

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = new Rect(0, i * RowH, view.width, RowH - 1);
            var (display, rel, isDir, guid) = _rows[i];

            if (ev.Type == EventType.MouseDown && ev.Button == 0 && row.Contains(ev.MousePosition))
            {
                if (isDir)
                {
                    _dir = rel;
                    _dirty = true;
                }
                else if (ev.ClickCount == 2 && rel.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                {
                    App.OpenScene(System.IO.Path.Combine(assets.Root, rel));
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
                if (!isDir && Selection.AssetPath != null
                    && Selection.AssetPath == System.IO.Path.Combine(assets.Root, rel))
                    GuiRenderer.DrawRect(row, new Color(60, 100, 180, 255), 1); // secili: hiyerarsiyle ayni mavi
                else if (!isDir && DragDrop.AssetGuid == guid && DragDrop.Active)
                    GuiRenderer.DrawRect(row, new Color(60, 100, 180, 120), 1);
                GuiRenderer.DrawTextIn(new Rect(6, row.y, 18, RowH - 1),
                    isDir ? ">" : "-", Gui.FontSize - 3f, new Color(150, 153, 163, 255), false, 2);
                GuiRenderer.DrawTextIn(new Rect(24, row.y, view.width - 28, RowH - 1),
                    display, Gui.FontSize - 2f, isDir ? new Color(200, 190, 140, 255) : new Color(214, 218, 228, 255), false, 2);
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
                        DragDrop.BeginAsset(r.Guid, r.Rel);
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == idDrag)
                {
                    GuiUtility.HotControl = 0;
                    if (!_dragging && _pressRow >= 0 && _pressRow < _rows.Count)
                    {
                        var pressed = _rows[_pressRow];
                        Selection.AssetPath = System.IO.Path.Combine(assets.Root, pressed.Rel);
                    }
                    _pressRow = -1;
                    _dragging = false;
                    // Drop'u DragDrop.EndFrame uygular/temizler (kayitli hedefe).
                    ev.Use();
                }
                break;
        }
        Gui.EndScrollView();
    }

    void RebuildRows(AssetDatabase assets)
    {
        _dirty = false;
        _rows.Clear();
        string prefix = _dir.Length > 0 ? _dir + "/" : "";
        var dirs = new HashSet<string>();
        foreach (var kv in assets.AllAssets)
        {
            string rel = kv.Key;
            if (!rel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            string rest = rel.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash >= 0)
            {
                string sub = rest.Substring(0, slash);
                if (dirs.Add(sub))
                    _rows.Add((sub, prefix + sub, true, null));
            }
            else
            {
                _rows.Add((rest, rel, false, kv.Value));
            }
        }
        _rows.Sort((a, b) => a.IsDir != b.IsDir ? (a.IsDir ? -1 : 1)
            : string.CompareOrdinal(a.Display, b.Display));
    }
}
