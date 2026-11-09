using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Hiyerarsi = AUTHORED agac (SceneDoc). Runtime'da dogan nesneler (spawn/klon)
// burada gorunmez — kaynak dosyada olmayan sey duzenlenemez.
// Satir tik = secim (Ctrl = coklu toggle), ok bolgesi = daralt/genislet,
// satir surukle-birak = reparent (bos alana birak = koke tasi).
[MenuItem("Window/Hierarchy", 0)]
public sealed class HierarchyPanel : EditorWindow
{
    static readonly int _dragHash = "Hierarchy.Drag".GetHashCode();

    Vec2 _scroll;
    readonly HashSet<int> _collapsed = new();
    readonly List<(int Id, Rect Row)> _rows = new(); // bu pass'in gorunur satirlari
    readonly List<int> _dragIdsTmp = new();

    int _pressId;      // mousedown'daki satir (0 = yok)
    bool _dragging;    // esik asildi: aktif reparent suruklemesi
    bool _pressCtrl;
    Vec2 _pressPos;
    int _pendingMenuId;   // sag tik: satir id (>0) / bos alan (-1) / yok (0) — menu enumerasyon disinda acilir
    int _menuWait;        // menu acilmadan once atlanacak Repaint sayisi (secim vurgusu ekrana bassin)

    static float RowH => Gui.FontSize + 6f; // Inspector RowH ile ayni oran
    const float DragThreshold = 5f;

    public HierarchyPanel() => Title = "Hierarchy";

    protected override void OnGui()
    {
        var es = App.EditScene;
        var doc = es.Doc;
        var vis = GuiClip.VisibleRect;

        // Prefab stage seridi: geri don (dirty ise Save/Discard/Cancel) + duzenlenen prefab adi.
        float top = 0f;
        if (App.InPrefabStage)
        {
            const float barH = 22f;
            if (Event.Current.Type == EventType.Repaint)
            {
                GuiRenderer.DrawRect(new Rect(0, 0, vis.width, barH), new Color(38, 46, 66, 255), 0);
                GuiRenderer.DrawTextIn(new Rect(66, 0, vis.width - 70, barH),
                    "Prefab: " + System.IO.Path.GetFileName(App.PrefabStagePath) + (es.Dirty ? "*" : ""),
                    Gui.FontSize - 2f, new Color(150, 180, 235, 255), false, 2);
            }
            if (Gui.Button(new Rect(2, 2, 60, 18), "< Back"))
                App.TryExitPrefabStage();
            top = barH;
        }

        // Olusturma/silme sag tik menusu ve kisayollardan; ust arac cubugu yok.
        float toolbarH = top;

        HandleKeyboard(es);

        int rows = 0;
        foreach (var g in doc.Objects)
            if (g.Parent == 0)
                rows += CountVisible(doc, g);

        int idDrag = GuiUtility.GetControlID(_dragHash, FocusType.Passive);
        var view = new Rect(0, 0, vis.width - 20, rows * RowH);
        _scroll = Gui.BeginScrollView(new Rect(0, toolbarH, vis.width, vis.height - toolbarH), _scroll, view);
        var ev = Event.Current;
        _rows.Clear();
        float y = 0;
        foreach (var g in doc.Objects)
            if (g.Parent == 0)
                DrawNode(doc, g, 0, ref y, ev, view.width, idDrag);
        HandleDrag(es, doc, ev, idDrag, view);
        // Sol tik bos alan: secimi kaldir (satir tiklamasi DrawNode'da ev.Use() ile yutuldugundan buraya dusmez).
        if (ev.Type == EventType.MouseDown && ev.Button == 0 && GuiUtility.HotControl == 0
            && GuiClip.VisibleRect.Contains(ev.MousePosition))
        {
            Selection.Clear();
            ev.Use();
        }
        // Sag tik bos alan: baglam menusu (satir ustu DrawNode'da isaretlenir, asagida acilir).
        if (ev.Type == EventType.MouseDown && ev.Button == 1 && GuiUtility.HotControl == 0
            && GuiClip.VisibleRect.Contains(ev.MousePosition))
        {
            _pendingMenuId = -1;
            _menuWait = 1;
            ev.Use();
        }
        Gui.EndScrollView();

        // Enumerasyon bitti: menu burada acilir (senkron eylem doc'u guvenle degistirir). Secim sag tikta hemen
        // degistigi icin menu BIR SONRAKI frame'in Repaint'ine ertelenir: vurgulu satir once ekrana basilir
        // (senkron native popup acikken frame sunulmaz).
        if (_pendingMenuId != 0 && ev.Type == EventType.Repaint)
        {
            if (_menuWait-- > 0)
                return;
            int id = _pendingMenuId;
            _pendingMenuId = 0;
            ShowContextMenu(es, id < 0 ? 0 : id);
        }
    }

    // ---- Copy / Paste / Duplicate / Delete (Unity Ctrl+C/V/D, Ctrl+Shift+V = child, Del) ----

    // Kisayollar yalniz imlec panel uzerindeyken (IMGUI'de panel odagi yok), textfield odagi
    // ve aktif drag yokken (drag ortasinda yapisal degisiklik kontrol id dizilimini kaydirir).
    void HandleKeyboard(EditorScene es)
    {
        var ev = Event.Current;
        if (ev.Type != EventType.KeyDown || GuiUtility.KeyboardControl != 0 || GuiUtility.HotControl != 0
            || !GuiClip.VisibleRect.Contains(ev.MousePosition))
            return;
        bool ctrl = (ev.Modifiers & EventModifiers.Control) != 0;
        bool shift = (ev.Modifiers & EventModifiers.Shift) != 0;
        switch (ev.KeyCode)
        {
            case GLFWConst.KEY_C when ctrl: CopySelection(es); ev.Use(); break;
            case GLFWConst.KEY_V when ctrl: Paste(es, asChild: shift); ev.Use(); break;
            case GLFWConst.KEY_D when ctrl: DuplicateSelection(es); ev.Use(); break;
            case GLFWConst.KEY_DELETE: DeleteSelection(es); ev.Use(); break;
        }
    }

    void ShowContextMenu(EditorScene es, int rowId)
    {
        if (rowId != 0 && !Selection.Contains(rowId))
            Selection.DocId = rowId;
        bool hasSel = Selection.Count > 0 && es.FindGo(Selection.DocId) != null;
        var items = new List<(string, Action)>();
        if (hasSel)
        {
            items.Add(("Copy", () => CopySelection(es)));
            items.Add(("Duplicate", () => DuplicateSelection(es)));
        }
        if (GoClipboard.HasContent)
        {
            items.Add(("Paste", () => Paste(es, asChild: false)));
            if (hasSel)
                items.Add(("Paste as Child", () => Paste(es, asChild: true)));
        }
        if (hasSel)
        {
            if (HasPrefabNode(es))
                items.Add(("Unpack Prefab Completely", () => UnpackSelection(es)));
            items.Add(("Delete", () => DeleteSelection(es)));
            items.Add(("-", null));
        }
        items.Add(("Create Empty", () => Selection.DocId = es.AddGameObject(hasSel ? Selection.DocId : 0).Id));
        EditorMenu.ShowContext(items.ToArray());
    }

    static void CopySelection(EditorScene es) => GoClipboard.Copy(es, Selection.Ids);

    static void DuplicateSelection(EditorScene es)
    {
        var created = es.DuplicateGameObjects(new List<int>(Selection.Ids));
        SelectAll(created);
    }

    // Yapistirma hedefi (Unity): secilinin parent'i altina, secilinin arkasina; secim yoksa koke/sona.
    // asChild: secilinin cocugu olarak (sona).
    static void Paste(EditorScene es, bool asChild)
    {
        var sel = es.FindGo(Selection.DocId);
        int parent = 0, before = 0;
        if (sel != null)
        {
            if (asChild) parent = sel.Id;
            else { parent = sel.Parent; before = IdAfter(es.Doc, sel.Id); }
        }
        var created = es.PasteSubtrees(GoClipboard.Trees, parent, before);
        SelectAll(created);
    }

    static bool HasPrefabNode(EditorScene es)
    {
        foreach (int id in Selection.Ids)
        {
            var g = es.FindGo(id);
            if (g != null && (g.IsPrefabRoot || g.IsPrefabChild))
                return true;
        }
        return false;
    }

    // Secimdeki her prefab node'unun instance'i (cocuk seciliyse koku) tamamen cozulur; tek undo.
    static void UnpackSelection(EditorScene es)
    {
        var ids = new List<int>(Selection.Ids);
        es.BeginBatch();
        try
        {
            foreach (int id in ids)
                es.UnpackPrefab(es.FindGo(id)); // ayni instance ikinci kez gelirse artik duz GO → no-op
        }
        finally { es.EndBatch(); }
    }

    static void DeleteSelection(EditorScene es)
    {
        // Coklu sil: kopya uzerinden (RemoveGameObject dogrudan doc'u degistirir); tek undo.
        var ids = new List<int>(Selection.Ids);
        if (ids.Count == 0)
            return;
        es.BeginBatch();
        try
        {
            foreach (int id in ids)
            {
                var g = es.FindGo(id);
                if (g != null)
                    es.RemoveGameObject(g);
            }
        }
        finally { es.EndBatch(); }
        Selection.Clear();
    }

    static void SelectAll(List<int> ids)
    {
        if (ids.Count == 0)
            return;
        Selection.Clear();
        foreach (int id in ids)
            Selection.Add(id);
    }

    void HandleDrag(EditorScene es, SceneDoc doc, Event ev, int idDrag, in Rect view)
    {
        switch (ev.GetTypeForControl(idDrag))
        {
            case EventType.MouseDrag:
                if (GuiUtility.HotControl == idDrag)
                {
                    float dx = ev.MousePosition.x - _pressPos.x, dy = ev.MousePosition.y - _pressPos.y;
                    if (!_dragging && dx * dx + dy * dy > DragThreshold * DragThreshold)
                    {
                        _dragging = true;
                        DragDrop.BeginSceneObject(_pressId); // Inspector ref alanlari da kabul edebilsin
                    }
                    ev.Use();
                }
                break;
            case EventType.MouseUp:
                if (GuiUtility.HotControl == idDrag)
                {
                    GuiUtility.HotControl = 0;
                    // Reparent yalniz birakma BU panelin gorunur alanindaysa;
                    // baska paneldeki drop'u DragDrop.EndFrame uygular.
                    if (_dragging && GuiClip.VisibleRect.Contains(ev.MousePosition))
                        DropReparent(es, doc, ev.MousePosition, view);
                    else if (!_dragging && _pressId != 0)
                    {
                        if (_pressCtrl) Selection.Toggle(_pressId);
                        else Selection.DocId = _pressId;
                    }
                    _pressId = 0;
                    _dragging = false;
                    ev.Use();
                }
                break;
            case EventType.Repaint:
                if (_dragging)
                    DrawDragFeedback(doc, ev.MousePosition, view);
                // Proje panelinden PREFAB birakma: kabul eden hedef olarak kaydol
                // (kabul karari importer tipinden — dosya tipi hardcode edilmez).
                if (DragDrop.Kind == DragDrop.Payload.Asset
                    && AssetDatabase.ImportTypeOf(DragDrop.AssetPath) == typeof(Prefab)
                    && GuiClip.VisibleRect.Contains(ev.MousePosition))
                {
                    var (target, zone, row) = DropAt(ev.MousePosition, view);
                    if (zone != DropZone.None)
                    {
                        if (target > 0)
                            GuiRenderer.DrawRect(row, new Color(90, 160, 90, 90), 3);
                        int parent = zone == DropZone.Into ? target : FindGo(doc, target)?.Parent ?? 0;
                        string guid = DragDrop.AssetGuid;
                        DragDrop.RegisterTarget(() =>
                        {
                            var r = es.InstantiatePrefab(guid, parent);
                            if (r != null)
                                Selection.DocId = r.Id;
                        });
                    }
                }
                break;
        }
    }

    // Birakma bolgeleri: satirin ust %25'i = ONUNE sirala, alt %25'i = ARKASINA
    // sirala (ayni parent), ortasi = COCUGU yap. Bos alan = koke (sona).
    enum DropZone { None, Into, Before, After }

    (int Id, DropZone Zone, Rect Row) DropAt(Vec2 m, in Rect view)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            var (id, r) = (_rows[i].Id, _rows[i].Row);
            if (!r.Contains(m))
                continue;
            float t = (m.y - r.y) / r.height;
            var zone = t < 0.25f ? DropZone.Before : t > 0.75f ? DropZone.After : DropZone.Into;
            return (id, zone, r);
        }
        if (view.Contains(m) || m.y >= 0)
            return (0, DropZone.Into, default); // liste alani/alti = kok (sona)
        return (0, DropZone.None, default);
    }

    // Birakma: bolgeye gore reparent ve/veya kardes siralamasi.
    void DropReparent(EditorScene es, SceneDoc doc, Vec2 m, in Rect view)
    {
        var (target, zone, _) = DropAt(m, view);
        if (zone == DropZone.None)
            return;
        _dragIdsTmp.Clear();
        if (!Selection.Contains(_pressId))
        {
            _dragIdsTmp.Add(_pressId);
        }
        else foreach (int id in Selection.Ids)
        {
            // Parent zinciri secimde olan atlanir: alt-agac butun tasinir (Unity gibi).
            if (!AncestorSelected(doc, id))
                _dragIdsTmp.Add(id);
        }
        var targetGo = target != 0 ? FindGo(doc, target) : null;
        foreach (int id in _dragIdsTmp)
        {
            var g = es.FindGo(id);
            if (g == null || id == target)
                continue;
            if (target != 0 && IsSameOrDescendant(doc, target, id))
                continue; // kendi alt-agacinin altina/yanina kendi uzerinden tasinamaz
            switch (zone)
            {
                case DropZone.Into:
                    if (target == 0)
                        es.MoveGameObject(g, 0, 0); // koke, listenin sonuna
                    else if (g.Parent != target)
                        es.Reparent(g, target);
                    break;
                case DropZone.Before:
                    es.MoveGameObject(g, targetGo.Parent, target);
                    break;
                case DropZone.After:
                    es.MoveGameObject(g, targetGo.Parent, IdAfter(doc, target));
                    break;
            }
        }
    }

    // Listede target'tan sonraki elemanin id'si (0 = son eleman).
    static int IdAfter(SceneDoc doc, int target)
    {
        for (int i = 0; i < doc.Objects.Count - 1; i++)
            if (doc.Objects[i].Id == target)
                return doc.Objects[i + 1].Id;
        return 0;
    }

    static bool AncestorSelected(SceneDoc doc, int id)
    {
        var g = FindGo(doc, id);
        int cur = g?.Parent ?? 0, guard = 0;
        while (cur != 0 && guard++ < 1000)
        {
            if (Selection.Contains(cur))
                return true;
            cur = FindGo(doc, cur)?.Parent ?? 0;
        }
        return false;
    }

    // candidate, ancestor'in kendisi ya da alt-agacinda mi.
    static bool IsSameOrDescendant(SceneDoc doc, int candidate, int ancestor)
    {
        int cur = candidate, guard = 0;
        while (cur != 0 && guard++ < 1000)
        {
            if (cur == ancestor)
                return true;
            cur = FindGo(doc, cur)?.Parent ?? 0;
        }
        return false;
    }

    static SceneDoc.GoDoc FindGo(SceneDoc doc, int id)
    {
        foreach (var g in doc.Objects)
            if (g.Id == id)
                return g;
        return null;
    }

    void DrawDragFeedback(SceneDoc doc, Vec2 m, in Rect view)
    {
        var (target, zone, row) = DropAt(m, view);
        if (target > 0)
        {
            switch (zone)
            {
                case DropZone.Into:
                    GuiRenderer.DrawRect(row, new Color(90, 160, 90, 90), 3);
                    break;
                case DropZone.Before:
                    GuiRenderer.DrawRect(new Rect(row.x, row.y - 1, row.width, 2), new Color(255, 220, 120, 255), 3);
                    break;
                case DropZone.After:
                    GuiRenderer.DrawRect(new Rect(row.x, row.y + row.height - 1, row.width, 2), new Color(255, 220, 120, 255), 3);
                    break;
            }
        }
        // Hayalet etiket global: DragDrop.DrawGhost (DrawUi) cizer.
    }

    static bool HasChildren(SceneDoc doc, int id)
    {
        foreach (var g in doc.Objects)
            if (g.Parent == id)
                return true;
        return false;
    }

    int CountVisible(SceneDoc doc, SceneDoc.GoDoc g)
    {
        int n = 1;
        if (!_collapsed.Contains(g.Id))
            foreach (var c in doc.Objects)
                if (c.Parent == g.Id)
                    n += CountVisible(doc, c);
        return n;
    }

    void DrawNode(SceneDoc doc, SceneDoc.GoDoc g, int depth, ref float y, Event ev, float width, int idDrag)
    {
        var row = new Rect(0, y, width, RowH - 1);
        _rows.Add((g.Id, row));
        bool hasKids = HasChildren(doc, g.Id);
        float indent = 4 + depth * 14;
        var arrow = new Rect(indent, y, 14, RowH - 1);

        // Yalniz GORUNUR alandaki satir tiklanir ve baska bir kontrol (acik combo popup'i
        // gibi) hot degilken: scroll disindaki satirlar / modal popup ustundeki tiklama yutulmasin.
        if (ev.Type == EventType.MouseDown && ev.Button == 0 && row.Contains(ev.MousePosition)
            && GuiClip.VisibleRect.Contains(ev.MousePosition) && GuiUtility.HotControl == 0)
        {
            if (hasKids && arrow.Contains(ev.MousePosition))
            {
                if (!_collapsed.Remove(g.Id))
                    _collapsed.Add(g.Id);
            }
            else
            {
                if (ev.ClickCount == 2)
                {
                    Selection.DocId = g.Id;
                    EditorWindow.GetWindow<SceneViewPanel>().FocusOn(g.Id); // Scene tab'i one + focus
                    ev.Use();
                    return;
                }
                _pressCtrl = (ev.Modifiers & EventModifiers.Control) != 0;
                _pressId = g.Id;
                _pressPos = ev.MousePosition;
                GuiUtility.HotControl = idDrag;
            }
            ev.Use();
        }

        // Sag tik satir: secimde degilse once sec; menu SENKRON (eylem donmeden calisir) oldugundan
        // doc.Objects enumerasyonu icinde ACILMAZ — OnGui basinda (bir sonraki pass) acilir.
        if (ev.Type == EventType.MouseDown && ev.Button == 1 && row.Contains(ev.MousePosition)
            && GuiClip.VisibleRect.Contains(ev.MousePosition) && GuiUtility.HotControl == 0)
        {
            // Unity gibi: secim hemen degisir (bu frame vurgulu cizilir), menu sonraki frame'de acilir.
            if (!Selection.Contains(g.Id))
                Selection.DocId = g.Id;
            _pendingMenuId = g.Id;
            _menuWait = 1;
            ev.Use();
        }

        if (ev.Type == EventType.Repaint)
        {
            if (Selection.Contains(g.Id))
                GuiRenderer.DrawRect(row, Selection.DocId == g.Id
                    ? new Color(60, 100, 180, 255)
                    : new Color(52, 78, 128, 255), 1); // ikincil secim: soluk mavi
            if (hasKids)
                GuiRenderer.DrawTextIn(arrow, _collapsed.Contains(g.Id) ? "\u25b8" : "\u25be",
                    Gui.FontSize - 3f, new Color(150, 153, 163, 255));
            // Prefab instance (kok ve cocuklari): Unity gibi mavi metin — kucuk resim yok (UI prefab'i
            // izole kadrajda anlamsiz cikiyordu).
            bool prefab = g.IsPrefabRoot || g.IsPrefabChild;
            var textCol = g.Active
                ? (prefab ? new Color(130, 180, 240, 255) : new Color(220, 224, 234, 255))
                : (prefab ? new Color(80, 105, 140, 255) : new Color(120, 123, 133, 255)); // inaktif: soluk
            float textX = indent + 14;
            GuiRenderer.DrawTextIn(new Rect(textX, y, width - textX, RowH - 1),
                g.Name, Gui.FontSize, textCol, false, 2);
        }

        y += RowH;
        if (hasKids && !_collapsed.Contains(g.Id))
            foreach (var c in doc.Objects)
                if (c.Parent == g.Id)
                    DrawNode(doc, c, depth + 1, ref y, ev, width, idDrag);
    }
}
