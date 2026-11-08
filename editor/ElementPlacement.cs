using System;
using System.Globalization;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// UI elemani (= prefab) yerlestirme: Library panelinden cift tik / Scene View'a birakma.
// Editor core somut component tiplerini TANIMAZ: kabin cocugunu nasil yerlestirdigi
// ITransformDriver'dan ogrenilir (MoveTool ile ayni sozlesme):
//   - surulmuyor            -> serbest: lokal pozisyon = imlec
//   - suruluyor + proxy alan -> anchor yerlesimi: proxy alana (anchoredPos) delta yazilir
//   - suruluyor, proxy yok   -> grup diziyor: imlece gore kardes sirasi secilir
// Tum adimlar tek undo grubu.
static unsafe class ElementPlacement
{
    // Birakma hedefi: imlec altindaki GO; prefab instance cocuguysa instance koku
    // (cocuklara GO eklenmez, Unity gibi). 0 = sahne koku (prefab stage'de tek kok).
    public static int ContainerFor(EditorScene es, int hitDocId)
    {
        var g = hitDocId > 0 ? es.FindGo(hitDocId) : null;
        int id = g == null ? 0 : g.IsPrefabChild ? g.PrefabRootId : g.Id;
        if (id == 0 && es.SingleRoot)
            foreach (var o in es.Doc.Objects)
                if (o.Parent == 0) { id = o.Id; break; }
        return id;
    }

    // Secili GO'nun icine ekle (secim yoksa / kok ise sahne koku). targetDocId verilirse secim yerine o
    // kullanilir (Library'de ilk tik asset secimi yapip GO secimini sildigi icin cift tik onceki hedefi iletir).
    public static SceneDoc.GoDoc AddToSelection(string guid, int targetDocId = -1)
    {
        var es = App.EditScene;
        if (es == null || string.IsNullOrEmpty(guid))
            return null;
        int parent = ContainerFor(es, targetDocId >= 0 ? targetDocId : Selection.DocId);
        var root = es.InstantiatePrefab(guid, parent);
        if (root != null)
            Selection.DocId = root.Id;
        return root;
    }

    // Dunya noktasina birak: parent = ContainerFor(hit). Yerlesim kabin surucusune gore.
    public static SceneDoc.GoDoc DropAt(string guid, int parentId, Vec2 world)
    {
        var es = App.EditScene;
        if (es == null || string.IsNullOrEmpty(guid))
            return null;
        es.History.BeginGroup();
        SceneDoc.GoDoc root;
        try
        {
            root = es.InstantiatePrefab(guid, parentId);
            if (root != null)
                PlaceAt(es, root, parentId, world);
        }
        finally
        {
            es.History.EndGroup();
        }
        if (root != null)
            Selection.DocId = root.Id;
        return root;
    }

    static void PlaceAt(EditorScene es, SceneDoc.GoDoc root, int parentId, Vec2 world)
    {
        var live = es.Live(root.Id);
        if (live == null)
            return;
        Vec2 local = ToParentLocal(es, parentId, world);
        ITransformDriver driver = null;
        var driven = TransformDriver.Driven(live, out driver);
        if ((driven & DrivenTransformProperties.Position) == 0)
        {
            es.SetTransform(root, new Vec3(local.x, local.y, root.Pos.z), root.Rot, root.Scale);
            return;
        }
        string field = driver.PositionEditField;
        if (field == null)
        {
            int before = PickInsertBefore(es, parentId, root.Id, world);
            if (before != 0)
                es.MoveGameObject(root, parentId, before);
            return;
        }
        // Anchor yerlesimi: surucunun yazdigi lokal pozisyonu tazele (lazy pull), farki proxy alana ekle.
        live.GetComponent<Renderer>()?.GetLocalSelectionBounds(out _, out _);
        var lp = live.transform.localPosition;
        var comp = (Component)driver;
        string typeName = comp.GetType().Name;
        SceneDoc.CompDoc cd = null;
        foreach (var c in root.Components)
            if (c.Type == typeName) { cd = c; break; }
        SerializedType.FieldSchema f = null;
        var entry = App.Catalog?.Find(typeName);
        if (entry != null)
            foreach (var fs in entry.Schema)
                if (fs.Name == field) { f = fs; break; }
        if (cd == null || f == null)
            return;
        var cur = f.Get(comp) is Vec2 v ? v : default;
        var nv = new Vec2(cur.x + local.x - lp.x, cur.y + local.y - lp.y);
        es.SetProp(root, cd, f, V2(cur), V2(nv));
    }

    static Vec2 ToParentLocal(EditorScene es, int parentId, Vec2 world)
    {
        var p = parentId > 0 ? es.Live(parentId) : null;
        if (p == null)
            return world;
        ref var m = ref p.transform._getWorldMatrix();
        float a = m.m[0], b = m.m[1], c = m.m[4], e = m.m[5];
        float det = a * e - c * b;
        if (MathF.Abs(det) < 1e-8f)
            return world;
        float dx = world.x - m.m[12], dy = world.y - m.m[13];
        return new Vec2((e * dx - c * dy) / det, (-b * dx + a * dy) / det);
    }

    // Grup dizen kapta kardes sirasi: kardes merkezlerinin yayildigi eksende imlecten
    // sonra gelen ilk kardesin onune (0 = sona). Eksen bilgisi kaptan degil geometriden.
    static int PickInsertBefore(EditorScene es, int parentId, int selfId, Vec2 world)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        int n = 0;
        Vec2 oneHalf = default;
        foreach (var g in es.Doc.Objects)
        {
            if (g.Parent != parentId || g.Id == selfId || !Center(es, g.Id, out var c, out var half))
                continue;
            n++;
            oneHalf = half;
            if (c.x < minX) minX = c.x; if (c.x > maxX) maxX = c.x;
            if (c.y < minY) minY = c.y; if (c.y > maxY) maxY = c.y;
        }
        if (n == 0)
            return 0;
        bool horiz = n == 1 ? oneHalf.x >= oneHalf.y : (maxX - minX) >= (maxY - minY);
        foreach (var g in es.Doc.Objects)
        {
            if (g.Parent != parentId || g.Id == selfId || !Center(es, g.Id, out var c, out _))
                continue;
            if (horiz ? c.x > world.x : c.y > world.y)
                return g.Id;
        }
        return 0;
    }

    static bool Center(EditorScene es, int docId, out Vec2 center, out Vec2 halfWorld)
    {
        center = halfWorld = default;
        var live = es.Live(docId);
        if (live == null || !live.activeInHierarchy)
            return false;
        var r = live.GetComponent<Renderer>();
        if (r == null || !r.GetLocalSelectionBounds(out var c, out var h))
            return false;
        ref var m = ref live.transform._getWorldMatrix();
        center = new Vec2(m.m[0] * c.x + m.m[4] * c.y + m.m[12], m.m[1] * c.x + m.m[5] * c.y + m.m[13]);
        halfWorld = new Vec2(MathF.Abs(m.m[0] * h.x) + MathF.Abs(m.m[4] * h.y), MathF.Abs(m.m[1] * h.x) + MathF.Abs(m.m[5] * h.y));
        return true;
    }

    static string V2(Vec2 v)
        => v.x.ToString("R", CultureInfo.InvariantCulture) + " " + v.y.ToString("R", CultureInfo.InvariantCulture);
}
