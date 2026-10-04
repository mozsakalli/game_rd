#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Serilestirme round-trip smoke testleri: attribute kurallari, koleksiyonlar,
// ic ice [Serializable] nesne, referanslar, FormerlySerializedAs. Izole sahnede
// kosar; canli -> doc -> yaml -> doc -> canli zinciri dogrulanir.
public static class SerializationTests
{
    static int _pass, _fail;

    [Serializable]
    public sealed class SubData
    {
        public float X;
        public Color Tint = Color.White;
        public Vec4 Quad;
        public List<int> Nums = new();
        public GameObject Owner;
        public List<SpriteRenderer> MoreTargets = new();
    }

    public sealed class SerTestComp : Component
    {
        public float PlainFloat = 1f;
        [NonSerialized] public float Skipped = 5f;
        [SerializeField] float _hidden = 2f;
        [FormerlySerializedAs("OldCount")] public int Renamed = 3;
        public float[] Arr;
        public List<Vec3> Points = new();
        public Vec4 Quad;
        public List<Vec4> Quads = new();
        public List<SpriteRenderer> Targets = new();
        public SubData Data = new();
        public SortMode Mode = SortMode.Ui;

        public float Hidden { get => _hidden; set => _hidden = value; }
    }

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        var prev = Scene.Active;
        var s = Scene.Create("ser-test");
        s.Catalog = catalog;
        Scene.SetActive(s);

        // --- Kaynak sahne kur ---
        var goA = new GameObject("A");
        var srA = goA.AddComponent<SpriteRenderer>();
        var goB = new GameObject("B");
        var srB = goB.AddComponent<SpriteRenderer>();
        var c = goA.AddComponent<SerTestComp>();
        c.PlainFloat = 42.5f;
        c.Skipped = 99f;
        c.Hidden = 7.25f;
        c.Renamed = 11;
        c.Arr = new[] { 1.5f, -2f, 3f };
        c.Points.Add(new Vec3(1, 2, 3));
        c.Points.Add(new Vec3(-4, 0, 9));
        c.Quad = new Vec4(1.25f, -2.5f, 3.75f, 4.5f);
        c.Quads.Add(new Vec4(5, 6, 7, 8));
        c.Targets.Add(srB); // liste ICINDE component referansi
        c.Targets.Add(srA);
        c.Data = new SubData { X = 8f, Tint = new Color(10, 20, 30, 40) };
        c.Data.Nums.Add(7);
        c.Data.Nums.Add(-3);
        c.Data.Quad = new Vec4(9, 10, 11, 12);
        c.Data.Owner = goB;
        c.Data.MoreTargets.Add(srB);

        // --- Round trip: capture -> yaml -> parse -> spawn ---
        string yaml = SceneDoc.Capture(s, catalog).ToYaml();
        var doc = SceneDoc.Parse(yaml);
        var s2 = Scene.Create("ser-test-2");
        s2.Catalog = catalog;
        Scene.SetActive(s2);
        doc.Spawn(null, catalog, null);

        SerTestComp r = null;
        SpriteRenderer rSrA = null, rSrB = null;
        for (int i = 0; i < s2.RootCount; i++)
        {
            var go = s2.GetRoot(i);
            r ??= go.GetComponent<SerTestComp>();
            if (go.name == "A") rSrA = go.GetComponent<SpriteRenderer>();
            if (go.name == "B") rSrB = go.GetComponent<SpriteRenderer>();
        }

        Check(r != null, "component geri geldi");
        Check(r.PlainFloat == 42.5f, "public alan");
        Check(r.Skipped == 5f, "[NonSerialized] atlandi (default kaldi)");
        Check(r.Hidden == 7.25f, "[SerializeField] private alan");
        Check(r.Renamed == 11, "normal ad round-trip");
        Check(r.Arr != null && r.Arr.Length == 3 && r.Arr[1] == -2f, "float[] dizi");
        Check(r.Points.Count == 2 && r.Points[1].x == -4f && r.Points[1].z == 9f, "List<Vec3>");
        Check(Same(r.Quad, c.Quad), "Vec4 scalar round-trip");
        Check(r.Quads.Count == 1 && Same(r.Quads[0], c.Quads[0]), "List<Vec4> round-trip");
        Check(Same(r.Data.Quad, c.Data.Quad), "nested Vec4 round-trip");
        Check(r.Targets.Count == 2 && r.Targets[0] == rSrB && r.Targets[1] == rSrA,
            "liste icinde CompRef cozumu");
        Check(r.Data != null && r.Data.X == 8f && r.Data.Tint.b == 30, "ic ice [Serializable] nesne");
        Check(r.Data.Nums.Count == 2 && r.Data.Nums[1] == -3, "ic ice nesnede liste");
        Check(r.Data.Owner == rSrB.gameObject && r.Data.MoreTargets.Count == 1
            && r.Data.MoreTargets[0] == rSrB, "ic ice nesnede referanslar");
        Check(r.Mode == SortMode.Ui, "enum");

        // Prefab id remap'i nested object ve onun referans listesine kadar iner.
        var dataField = SerializedType.Find(SerializedType.Build(typeof(SerTestComp)), nameof(SerTestComp.Data));
        var nestedRefs = DocNode.Map();
        nestedRefs.Add(nameof(SubData.Owner), DocNode.Scal("2"));
        var nestedTargets = DocNode.Seq();
        nestedTargets.Items.Add(DocNode.Scal("2:SpriteRenderer:0"));
        nestedRefs.Add(nameof(SubData.MoreTargets), nestedTargets);
        SceneDoc.RemapValue(nestedRefs, dataField, new Dictionary<int, int> { [2] = 42 });
        Check(nestedRefs.Get(nameof(SubData.Owner)).Scalar == "42"
            && nestedRefs.Get(nameof(SubData.MoreTargets)).Items[0].Scalar == "42:SpriteRenderer:0",
            "nested prefab ref remap");

        // --- FormerlySerializedAs: eski adla yazilmis yaml yeni alana yuklenir ---
        string migrated = yaml.Replace("Renamed:", "OldCount:");
        var s3 = Scene.Create("ser-test-3");
        s3.Catalog = catalog;
        Scene.SetActive(s3);
        SceneDoc.Parse(migrated).Spawn(null, catalog, null);
        SerTestComp m = null;
        for (int i = 0; i < s3.RootCount && m == null; i++)
            m = s3.GetRoot(i).GetComponent<SerTestComp>();
        Check(m != null && m.Renamed == 11, "[FormerlySerializedAs] eski ad eslesti");

        LayoutBoxFourValues(catalog);

        Scene.SetActive(prev);
        Scene.Unload(s3);
        Scene.Unload(s2);
        Scene.Unload(s);
        Console.WriteLine($"[serialization] {_pass} PASS, {_fail} FAIL");
    }

    static bool Same(Vec4 a, Vec4 b)
        => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    static void LayoutBoxFourValues(TypeCatalog catalog)
    {
        var schema = catalog.Find(nameof(LayoutBox)).Schema;
        foreach (string name in new[] { "padding", "border", "radius", "slice9" })
            Check(SerializedType.Find(schema, name)?.Kind == SerializedType.Kind.Vec4,
                "LayoutBox " + name + " tek satir Vec4 semasi");
        Check(SerializedType.Find(schema, "borderLeft") == null, "eski tekil alanlar semada yok");

        var box = new GameObject("quad-box").AddComponent<LayoutBox>();
        box.Padding = new Vec4(1, 2, 3, 4);
        box.Border = new Vec4(5, 6, 7, 8);
        box.Radius = new Vec4(9, 10, 11, 12);
        box.Scale9 = new Vec4(13, 14, 15, 16);
        var node = ObjectSerializer.ToNode(box, null);
        var restored = new GameObject("quad-restored").AddComponent<LayoutBox>();
        ObjectSerializer.FromNode(restored, node, null);
        Check(Same(restored.Padding, box.Padding) && Same(restored.Border, box.Border)
            && Same(restored.Radius, box.Radius) && Same(restored.Scale9, box.Scale9),
            "LayoutBox grouped serialization round-trip");
        Check(box.PadBottom == 4 && box.BorderRight == 7 && box.RadiusBL == 12 && box.Slice9Top == 14,
            "grouped degerler tekil API'de");
        box.BorderLeft = 25;
        box.RadiusTR = 30;
        box.Slice9Bottom = 40;
        Check(box.Border.x == 25 && box.Radius.y == 30 && box.Scale9.w == 40,
            "tekil setter grouped degeri degistirir");
        box.CornerRadius = 2;
        box.BorderWidth = 3;
        box.Slice9 = 4;
        Check(Same(box.Radius, new Vec4(2, 2, 2, 2)) && Same(box.Border, new Vec4(3, 3, 3, 3))
            && Same(box.Scale9, new Vec4(4, 4, 4, 4)), "uniform setter'lar korunur");
        box.Layout = LayoutMode.Horizontal;
        var child = new GameObject("quad-child").AddComponent<LayoutBox>();
        child.transform.SetParent(box.transform, false);
        child.Width = 20;
        child.Height = 10;
        Check(box.RectWidth == 24 && box.RectHeight == 16, "grouped padding layout olcumu");
        box.Padding = new Vec4(5, 6, 7, 8);
        Check(box.RectWidth == 32 && box.RectHeight == 24, "grouped padding lazy dirty pull");

        var legacy = DocNode.Map();
        legacy.Add("type", DocNode.Scal(nameof(LayoutBox)));
        string[][] groups =
        {
            new[] { "padding", "padLeft", "padTop", "padRight", "padBottom" },
            new[] { "border", "borderLeft", "borderTop", "borderRight", "borderBottom" },
            new[] { "radius", "radiusTL", "radiusTR", "radiusBR", "radiusBL" },
            new[] { "slice9", "slice9Left", "slice9Top", "slice9Right", "slice9Bottom" },
        };
        foreach (var group in groups)
            for (int axis = 0; axis < 4; axis++)
                legacy.Add(group[axis + 1], DocNode.Scal((axis + 1).ToString()));
        var migrated = SceneDoc.CompFromNode(legacy);
        Check(migrated.Props.Count == 4, "legacy LayoutBox alanlari dort gruba tasindi");
        foreach (var group in groups)
        {
            Check(migrated.Props.Find(p => p.Key == group[0]).Value.Scalar == "1 2 3 4",
                "legacy " + group[0] + " sirasi korunur");
            SceneDoc.ApplyLegacyLayoutBoxOverride(migrated, group[2], DocNode.Scal("20"));
            SceneDoc.ApplyLegacyLayoutBoxOverride(migrated, group[4], DocNode.Scal("40"));
            Check(migrated.Props.Find(p => p.Key == group[0]).Value.Scalar == "1 20 3 40",
                "legacy " + group[0] + " prefab delta diger eksenleri korur");
        }
        legacy.Add("border", DocNode.Scal("9 8 7 6"));
        Check(SceneDoc.CompFromNode(legacy).Props.Find(p => p.Key == "border").Value.Scalar == "9 8 7 6",
            "yeni grouped deger legacy degerden once gelir");
        var partial = DocNode.Map();
        partial.Add("type", DocNode.Scal(nameof(LayoutBox)));
        partial.Add("radiusBL", DocNode.Scal("7"));
        Check(SceneDoc.CompFromNode(partial).Props[0].Value.Scalar == "0 0 0 7",
            "eksik legacy eksenler sifir kalir");

        GameObject.Destroy(box.gameObject);
        GameObject.Destroy(restored.gameObject);
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[serialization] FAIL: {name}"); }
    }
}
#endif
