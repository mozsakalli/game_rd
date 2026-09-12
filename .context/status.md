# COMPILED CONTEXT

## TASK
Summarize the current engine and editor architecture: scene and prefab model, hot reload, tween and MovieClip animation, GUI system, rendering, serialization, and open items

## REPOSITORY
root: C:/Work/digitoygames/game_rd
csharp_files_indexed: 92
index_cache: miss
semantic_symbols: 1431
call_edges: 3282
syntax_diagnostics: 0

## PROJECT STATE
goal: Evolve the DigitoyEngine editor without breaking authored scene and prefab behavior.
constraints:
- Preserve existing scene and prefab serialization compatibility.
- Keep SceneDoc as authored state and live scenes as projections.
- Do not introduce static caches that retain collectible game assemblies.
decisions:
- Prefab instances are stored as deltas on disk and expanded into ordinary GoDoc nodes in memory.
- Prefab-local object and component references are remapped through persistent local-to-scene IDs.
- Editor structural mutations update the document and rebuild the live projection.
- A tween that dies early (destroyed target or Cancel) wakes its Then() chain; TweenPool.Kill owns chain wake-up for all death paths.
- Script changes compile in a background task with debounce; on failure the old game assembly keeps running.
facts:
- Prefab expansion and collapse are implemented by the SceneDoc partial. [evidence: engine/managed/Serialization/SceneDoc.Prefab.cs]
- The editor and engine currently target .NET 9. [evidence: editor/DigitoyEditor.csproj; engine/managed/DigitoyEngine.csproj]
- Animation core is a zero-alloc scene-owned TweenPool plus MovieClip tracks with LUT easing; chained tweens survive early kills. [evidence: engine/managed/Tween.cs; engine/managed/MovieClip.cs; editor/LifecycleTests.cs TweenChainSurvivesKill]
- AssetWatcher drives hot reload: debounced .cs rebuild via GameCode.CompileOnly plus per-extension asset patching. [evidence: editor/AssetWatcher.cs; editor/GameCode.cs]
- GUI runs the Unity multi-pass model: a fresh Layout pass before every queued input event, then Repaint. [evidence: engine/managed/Gui/GuiHost.cs]
- Editor lifecycle smoke tests run at startup in an isolated scene. [evidence: editor/LifecycleTests.cs; lifecycle_out.txt]
open_items:
- Prefab child removal, component diffs, and nested prefab behavior remain active areas.
- Source generator for TypeCatalog/FieldSchema accessors (release/AOT) is designed but not built.
- Atlas v2 debt: trim/rotate, in-place blit, Library persistence, build-phase strip.
validations:
- dotnet build editor -v q --nologo: passed 2026-09-12 after tween chain fix
- editor startup lifecycle tests: 18 PASS, 0 FAIL (2026-09-12, includes TweenChainSurvivesKill)

## GIT CHANGES
```text
No tracked baseline; 9172 untracked paths omitted.
```

## RELEVANT CODE

### method DrawPrefabHeader
source: editor/InspectorPanel.cs:247
symbol: DigitoyEditor.InspectorPanel.DrawPrefabHeader(DigitoyEditor.EditorScene, DigitoyEngine.SceneDoc.GoDoc, ref float, float)
score: 155
calls: DigitoyEditor.EditorScene.FindGo(int), DigitoyEditor.InspectorPanel.Overrides(DigitoyEditor.EditorScene, DigitoyEngine.SceneDoc.GoDoc), DigitoyEditor.InspectorPanel.TryDescribe(DigitoyEditor.EditorScene, DigitoyEngine.SceneDoc.GoDoc, string, string, out string, out System.Action, out System.Action), DigitoyEngine.AssetDatabase.ResolvePath(string), DigitoyEngine.Color.Color(byte, byte, byte, byte), DigitoyEngine.DocNode.GetScalar(string, string), DigitoyEngine.Gui.Button(in DigitoyEngine.Rect, System.ReadOnlySpan<char>, DigitoyEngine.GuiStyle), DigitoyEngine.GuiRenderer.DrawRect(in DigitoyEngine.Rect, DigitoyEngine.Color, int)
called_by: DigitoyEditor.InspectorPanel.OnGui()
depends_on: DigitoyEditor.App, DigitoyEditor.App.Assets, DigitoyEditor.EditorScene, DigitoyEngine.Color, DigitoyEngine.DocNode, DigitoyEngine.Event, DigitoyEngine.Event.Current, DigitoyEngine.Event.Type
```csharp
// Unity modeli: instance basligi + acilir Overrides listesi (Revert/Apply burada).
    void DrawPrefabHeader(EditorScene es, SceneDoc.GoDoc instRoot, ref float y, float w)
    {
        var ovs = Overrides(es, instRoot);
        int n = ovs?.Items.Count ?? 0;
        if (Event.Current.Type == EventType.Repaint)
        {
            GuiRenderer.DrawRect(new Rect(2, y, w, 20), new Color(38, 46, 66, 255), 0);
            string ppath = App.Assets?.ResolvePath(instRoot.PrefabGuid) ?? "?";
            int slash = ppath.LastIndexOf('/');
            if (slash >= 0) ppath = ppath[(slash + 1)..];
            int dot = ppath.IndexOf('.');
            if (dot > 0) ppath = ppath[..dot];
            GuiRenderer.DrawTextIn(new Rect(8, y, w - 130, 20), "Prefab: " + ppath, 11f,
                new Color(150, 180, 235, 255), false, 2);
        }
        if (Gui.Button(new Rect(w - 120, y + 1, 118, 18),
                _showOverrides ? $"Overrides ({n}) -" : $"Overrides ({n}) +"))
            _showOverrides = !_showOverrides;
        y += RowH + 2;
        if (!_showOverrides || ovs == null)
            return;

        foreach (var o in ovs.Items)
        {
            int local = int.TryParse(o.GetScalar("node"), out int l) ? l : 0;
            string comp = o.GetScalar("comp", null);
            string prop = o.GetScalar("prop");
            instRoot.PrefabIds.TryGetValue(local, out int sceneId);
            var tg = es.FindGo(sceneId);
            if (tg == null)
                continue;

            bool canApply = TryDescribe(es, tg, comp, prop, out string label, out var apply, out var revert);
            if (Event.Current.Type == EventType.Repaint)
                GuiRenderer.DrawTextIn(new Rect(12, y, w - 130, 18), label, 11f,
                    new Color(200, 205, 215, 255), false, 2);
            if (Gui.Button(new Rect(w - 118, y, 56, 18), "Revert"))
            {
                revert?.Invoke();
                break; // liste degisti: bu frame'i kes
            }
            if (canApply && Gui.Button(new Rect(w - 58, y, 56, 18), "Apply"))
            {
                apply?.Invoke();
                break;
            }
            y += RowH - 2;
        }
        y += 4;
    }
```

### method ApplyFieldToPrefab
source: editor/EditorScene.cs:272
symbol: DigitoyEditor.EditorScene.ApplyFieldToPrefab(DigitoyEngine.SceneDoc.GoDoc, DigitoyEngine.SceneDoc.CompDoc, DigitoyEngine.SerializedType.FieldSchema)
score: 155
calls: DigitoyEditor.AssetWatcher.NoteSelfWrite(string), DigitoyEditor.EditorScene.FindGo(int), DigitoyEditor.EditorScene.FindProp(DigitoyEngine.SceneDoc.CompDoc, DigitoyEngine.SerializedType.FieldSchema), DigitoyEditor.EditorScene.RefreshLive(), DigitoyEditor.EditorScene.RemapAcross(DigitoyEngine.DocNode, DigitoyEngine.SerializedType.FieldSchema, System.Collections.Generic.Dictionary<int, int>, System.Collections.Generic.Dictionary<int, int>), DigitoyEngine.AssetDatabase.ResolvePath(string), DigitoyEngine.DocNode.Clone(), DigitoyEngine.DocNode.Equal(DigitoyEngine.DocNode, DigitoyEngine.DocNode)
called_by: DigitoyEditor.InspectorPanel.TryDescribe(DigitoyEditor.EditorScene, DigitoyEngine.SceneDoc.GoDoc, string, string, out string, out System.Action, out System.Action)
depends_on: DigitoyEditor.AssetWatcher, DigitoyEditor.EditorScene.Doc, DigitoyEngine.AssetDatabase.Root, DigitoyEngine.DocNode, DigitoyEngine.SceneDoc, DigitoyEngine.SceneDoc.CompDoc, DigitoyEngine.SceneDoc.GoDoc, DigitoyEngine.SceneDoc.GoDoc.IsPrefabRoot
```csharp
// Sahnedeki degeri PREFAB'A yazar (dosya dahil) ve override'i olmayan diger
    // instance'lara yayar. v1: undo'suz (prefab dosyasi degisir — Unity'de de ayri dert).
    public void ApplyFieldToPrefab(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f)
    {
        if (!Doc.TryGetPrefabSource(g, _assets, out var prefabDoc, out var sourceNode, out var rootRec))
            return;
        string addr = SceneDoc.CompAddressOf(g, cd);
        var sourceComp = SceneDoc.FindCompByAddress(sourceNode, addr);
        if (sourceComp == null)
            return;
        var cur = FindProp(cd, f);
        if (cur == null)
            return;
        var oldPrefabVal = SceneDoc.RemappedPrefabValue(sourceComp, f, rootRec.PrefabIds); // yayilim kiyasi icin

        // Sahne degeri prefab uzayina (ters remap'li klon) yazilir.
        var inv = SceneDoc.InvertMap(rootRec.PrefabIds);
        var toPrefab = cur.Clone();
        SceneDoc.RemapValue(toPrefab, f, inv);
        bool wrote = false;
        for (int i = 0; i < sourceComp.Props.Count; i++)
            if (sourceComp.Props[i].Key == f.Name || (f.FormerName != null && sourceComp.Props[i].Key == f.FormerName))
            {
                sourceComp.Props[i] = new(f.Name, toPrefab);
                wrote = true;
                break;
            }
        if (!wrote)
            sourceComp.Props.Add(new(f.Name, toPrefab));

        // Prefab dosyasini kaydet (cache'teki Doc mutasyonla ayni nesne).
        string rel = _assets.ResolvePath(rootRec.PrefabGuid);
        if (!string.IsNullOrEmpty(rel))
        {
            string pp = System.IO.Path.Combine(_assets.Root, rel);
            AssetWatcher.NoteSelfWrite(pp);
            prefabDoc.Save(pp);
        }

        // Diger instance'lara yayilim: alanin ESKI prefab degerinde kalanlara
        // (override'sizlara) yeni deger; override'i olan instance korunur.
        foreach (var r in Doc.Objects)
        {
            if (!r.IsPrefabRoot || r.PrefabGuid != rootRec.PrefabGuid || r == rootRec)
                continue;
            if (!r.PrefabIds.TryGetValue(sourceNode.Id, out int sceneId))
                continue;
            var tg = FindGo(sceneId);
            var tcd = tg != null ? SceneDoc.FindCompByAddress(tg, addr) : null;
            if (tcd == null)
                continue;
            var tCur = FindProp(tcd, f);
            var oldInR = RemapAcross(oldPrefabVal, f, inv, r.PrefabIds);
            if (tCur != null && !DocNode.Equal(tCur, oldInR))
                continue; // instance'in kendi override'i var: dokunma
            var newInR = SceneDoc.RemappedPrefabValue(sourceComp, f, r.PrefabIds);
            if (newInR == null)
                continue;
            if (tCur == null)
                tcd.Props.Add(new(f.Name, newInR));
            else
            {
                tCur.Scalar = newInR.Scalar;
                tCur.Items = newInR.Items;
                tCur.Fields = newInR.Fields;
            }
        }
        RefreshLive();
    }
```

### method OnGui
source: editor/ProjectPanel.cs:68
symbol: DigitoyEditor.ProjectPanel.OnGui()
score: 149
calls: DigitoyEditor.App.OpenScene(string), DigitoyEditor.DragDrop.BeginAsset(string, string), DigitoyEditor.EditorMenu.ShowContext(params (string Path, System.Action Run)[]), DigitoyEditor.ProjectPanel.CreateAsset(DigitoyEngine.AssetDatabase, string, System.Type), DigitoyEditor.ProjectPanel.CreateFolder(DigitoyEngine.AssetDatabase, string), DigitoyEditor.ProjectPanel.CreateScene(DigitoyEngine.AssetDatabase, string), DigitoyEditor.ProjectPanel.RebuildRows(DigitoyEngine.AssetDatabase), DigitoyEngine.AssetDatabase.ScanMetas(bool)
depends_on: DigitoyEditor.App, DigitoyEditor.App.Assets, DigitoyEditor.DragDrop, DigitoyEditor.DragDrop.Active, DigitoyEditor.DragDrop.AssetGuid, DigitoyEditor.EditorMenu, DigitoyEditor.Selection, DigitoyEditor.Selection.AssetPath
```csharp
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
                _dir.Length > 0 ? _dir : "Assets", 11f, new Color(150, 153, 163, 255), false, 2);
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
                    isDir ? ">" : "-", 11f, new Color(150, 153, 163, 255), false, 2);
                GuiRenderer.DrawTextIn(new Rect(24, row.y, view.width - 28, RowH - 1),
                    display, 12f, isDir ? new Color(200, 190, 140, 255) : new Color(214, 218, 228, 255), false, 2);
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
```

### method OnGui
source: editor/SceneViewPanel.cs:126
symbol: DigitoyEditor.SceneViewPanel.OnGui()
score: 147
calls: DigitoyEditor.DragDrop.RegisterTarget(System.Action), DigitoyEditor.EditorScene.InstantiatePrefab(string, int, DigitoyEngine.Vec3?), DigitoyEditor.SceneViewPanel.DoGizmoAndSelect(in DigitoyEngine.Rect, DigitoyEngine.Event), DigitoyEditor.SceneViewPanel.DrawGameFrame(), DigitoyEditor.SceneViewPanel.FitGameFrame(), DigitoyEditor.SceneViewPanel.PanelToWorld(DigitoyEngine.Vec2), DigitoyEditor.SceneViewPanel.Resize(int, int), DigitoyEngine.AssetDatabase.ImportTypeOf(string)
depends_on: DigitoyEditor.App, DigitoyEditor.App.EditScene, DigitoyEditor.DragDrop, DigitoyEditor.DragDrop.AssetGuid, DigitoyEditor.DragDrop.AssetPath, DigitoyEditor.DragDrop.Kind, DigitoyEditor.DragDrop.Payload, DigitoyEditor.SceneViewPanel.Tool
```csharp
protected override void OnGui()
    {
        var vis = GuiClip.VisibleRect;
        var ev = Event.Current;

        if (Gui.Button(new Rect(4, 4, 36, 20), "1:1"))
            _zoom = 1f;
        if (Gui.Button(new Rect(44, 4, 36, 20), "Fit"))
            FitGameFrame();
        if (Gui.Button(new Rect(92, 4, 46, 20), _tool == Tool.Move ? "Move*" : "Move"))
            _tool = Tool.Move;
        if (Gui.Button(new Rect(142, 4, 58, 20), _tool == Tool.Rotate ? "Rotate*" : "Rotate"))
            _tool = Tool.Rotate;
        if (Gui.Button(new Rect(204, 4, 50, 20), _tool == Tool.Scale ? "Scale*" : "Scale"))
            _tool = Tool.Scale;

        // Unity W/E/R kisayollari (textfield odagi yokken).
        if (ev.Type == EventType.KeyDown && GuiUtility.KeyboardControl == 0)
        {
            if (ev.KeyCode == GLFWConst.KEY_W) { _tool = Tool.Move; ev.Use(); }
            else if (ev.KeyCode == GLFWConst.KEY_E) { _tool = Tool.Rotate; ev.Use(); }
            else if (ev.KeyCode == GLFWConst.KEY_R) { _tool = Tool.Scale; ev.Use(); }
        }

        if (ev.Type == EventType.Repaint && vis.width >= 8 && vis.height >= 8)
        {
            _seen = true;
            _viewW = vis.width;
            _viewH = vis.height;
            float s = Gui.Scale > 0 ? Gui.Scale : 1f;
            int w = Math.Max(8, (int)(vis.width * s));
            int h = Math.Max(8, (int)(vis.height * s));
            if (w != _rtW || h != _rtH)
                Resize(w, h); // eski RT BeginFrame'de olur; hedef encode oncesi baglanir
            GuiRenderer.DrawTexture(new Rect(0, 0, vis.width, vis.height), _rt);
        }

        DoGizmoAndSelect(vis, ev); // pan'den ONCE: tiklama onceligi

        int id = GuiUtility.GetControlID(_panHash, FocusType.Passive);
        switch (ev.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (vis.Contains(ev.MousePosition))
                {
                    GuiUtility.HotControl = id;
                    ev.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == id)
                {
                    _center.x -= ev.Delta.x / _zoom;
                    _center.y -= ev.Delta.y / _zoom;
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == id)
                {
                    GuiUtility.HotControl = 0;
                    ev.Use();
                }
                break;
            case EventType.ScrollWheel:
                if (vis.Contains(ev.MousePosition))
                {
                    // Imlec merkezli zoom: imlecin altindaki dunya noktasi sabit kalir.
                    Vec2 before = PanelToWorld(ev.MousePosition);
                    _zoom = Math.Clamp(_zoom * MathF.Pow(1.1f, ev.Delta.y), 0.05f, 40f);
                    Vec2 after = PanelToWorld(ev.MousePosition);
                    _center.x += before.x - after.x;
                    _center.y += before.y - after.y;
                    ev.Use();
                }
                break;
        }

        if (ev.Type == EventType.Repaint)
        {
            DrawGameFrame();
            // Proje panelinden PREFAB birakma: imlecin dunya konumuna instance
            // (kabul karari importer tipinden — dosya tipi hardcode edilmez).
            if (DragDrop.Kind == DragDrop.Payload.Asset
                && AssetDatabase.ImportTypeOf(DragDrop.AssetPath) == typeof(Prefab)
                && vis.Contains(ev.MousePosition))
            {
                var wp = PanelToWorld(ev.MousePosition);
                string guid = DragDrop.AssetGuid;
                GuiRenderer.DrawRect(new Rect(ev.MousePosition.x - 4, ev.MousePosition.y - 4, 8, 8),
                    new Color(90, 170, 90, 220), 5);
                DragDrop.RegisterTarget(() =>
                {
                    var r = App.EditScene.InstantiatePrefab(guid, 0, new Vec3(wp.x, wp.y, 0f));
                    if (r != null)
                        Selection.DocId = r.Id;
                });
            }
        }
    }
```

### method ApplyRemovePrefabChild
source: editor/EditorScene.cs:446
symbol: DigitoyEditor.EditorScene.ApplyRemovePrefabChild(int, int, System.Collections.Generic.List<(DigitoyEngine.SceneDoc.GoDoc doc, int index)>, bool)
score: 134
calls: DigitoyEditor.EditorScene.FindGo(int), DigitoyEditor.EditorScene.RefreshLive()
called_by: DigitoyEditor.RemovePrefabChildOp.Apply(DigitoyEditor.EditorScene, bool)
depends_on: DigitoyEditor.EditorScene.Doc, DigitoyEngine.SceneDoc, DigitoyEngine.SceneDoc.GoDoc
```csharp
internal void ApplyRemovePrefabChild(int rootId, int localId, List<(SceneDoc.GoDoc doc, int index)> items, bool undo)
    {
        var root = FindGo(rootId);
        if (root == null)
            return;
        if (undo)
        {
            root.PrefabRemoved?.Remove(localId);
            foreach (var (doc, index) in items)
                Doc.Objects.Insert(Math.Clamp(index, 0, Doc.Objects.Count), doc);
        }
        else
        {
            root.PrefabRemoved ??= new List<int>();
            if (!root.PrefabRemoved.Contains(localId))
                root.PrefabRemoved.Add(localId);
            for (int i = items.Count - 1; i >= 0; i--)
                Doc.Objects.Remove(items[i].doc);
        }
        RefreshLive();
    }
```

### method Apply
source: editor/UndoStack.cs:262
symbol: DigitoyEditor.RemovePrefabChildOp.Apply(DigitoyEditor.EditorScene, bool)
score: 116
calls: DigitoyEditor.EditorScene.ApplyRemovePrefabChild(int, int, System.Collections.Generic.List<(DigitoyEngine.SceneDoc.GoDoc doc, int index)>, bool)
depends_on: DigitoyEditor.EditorScene
```csharp
public override void Apply(EditorScene es, bool undo)
        => es.ApplyRemovePrefabChild(RootId, LocalId, Items, undo);
```

## BUDGET
token_budget: 6000
