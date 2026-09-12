#if DE_EDITOR
using System;
using System.Collections.Generic;
using DigitoyEngine;

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
        c.Targets.Add(srB); // liste ICINDE component referansi
        c.Targets.Add(srA);
        c.Data = new SubData { X = 8f, Tint = new Color(10, 20, 30, 40) };
        c.Data.Nums.Add(7);
        c.Data.Nums.Add(-3);
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

        Scene.SetActive(prev);
        Scene.Unload(s3);
        Scene.Unload(s2);
        Scene.Unload(s);
        Console.WriteLine($"[serialization] {_pass} PASS, {_fail} FAIL");
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[serialization] FAIL: {name}"); }
    }
}
#endif
