using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DigitoyEngine;

// Authored sahnenin bellek ici modeli — KAYNAK GERCEK budur; canli sahne bunun
// projeksiyonudur. Akis: duzenleme doc + canliya patch, Ctrl+S = YAML yaz + FULL
// RELOAD (init'ler taze kosar; runtime durumu dosyaya asla sizamaz).
public sealed partial class SceneDoc
{
    public string Name = "Main";
    public readonly List<GoDoc> Objects = new();

    public sealed class GoDoc
    {
        public int Id;
        public int Parent; // 0 = kok
        public string Name = "GameObject";
        public bool Active = true;
        public Vec3 Pos, Rot;
        public Vec3 Scale = new(1f, 1f, 1f);
        public readonly List<CompDoc> Components = new();

        // --- Prefab kaynak isaretleri (yalniz BELLEKTE, expand edilmis halde) ---
        public string PrefabGuid;              // instance KOKUNDE dolu
        public int PrefabLocalId;              // prefab icindeki localId (0 = prefab disi)
        public int PrefabRootId;               // bagli instance kokunun sahne id'si (kok: kendi Id)
        public Dictionary<int, int> PrefabIds; // KOKTE: prefabLocalId -> sceneId (KALICI)
        public List<int> PrefabRemoved;        // KOKTE: silinen prefab localId'leri
        internal DocNode PendingOverrides;     // Parse -> Expand arasi tasiyici

        public bool IsPrefabRoot => PrefabGuid != null;
        public bool IsPrefabChild => PrefabRootId != 0 && PrefabGuid == null;
    }

    public sealed class CompDoc
    {
        public string Type;
        public bool Enabled = true; // Unity m_Enabled karsiligi
        public readonly List<KeyValuePair<string, DocNode>> Props = new();
    }

    // --- Canli sahne → doc (bootstrap/kaydet) ---

    public static SceneDoc Capture(Scene scene, TypeCatalog catalog, AssetDatabase assets = null)
    {
        var doc = new SceneDoc { Name = scene.Name };
        // 1. gecis: id atamasi — ileri referanslar (henuz gezilmemis GO'ya) cozulebilsin.
        var ids = new Dictionary<GameObject, int>();
        int nextId = 1;
        for (int i = 0; i < scene.RootCount; i++)
            AssignIds(scene.GetRoot(i), ids, ref nextId);
        for (int i = 0; i < scene.RootCount; i++)
            CaptureGo(doc, catalog, scene.GetRoot(i), 0, ids, assets);
        return doc;
    }

    static void AssignIds(GameObject go, Dictionary<GameObject, int> ids, ref int nextId)
    {
        ids[go] = nextId++;
        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t._gameObject != null)
                AssignIds(t._gameObject, ids, ref nextId);
    }

    // Prefab: tek alt agac — kok, doc icinde parent=0 olur.
    public static SceneDoc CaptureSubtree(GameObject root, TypeCatalog catalog, AssetDatabase assets = null)
    {
        var doc = new SceneDoc { Name = root.name };
        var ids = new Dictionary<GameObject, int>();
        int nextId = 1;
        AssignIds(root, ids, ref nextId);
        CaptureGo(doc, catalog, root, 0, ids, assets);
        return doc;
    }

    static void CaptureGo(SceneDoc doc, TypeCatalog catalog, GameObject go, int parentId, Dictionary<GameObject, int> ids, AssetDatabase assets)
    {
        var g = new GoDoc
        {
            Id = ids[go],
            Parent = parentId,
            Name = go.name,
            Active = go.activeSelf,
            Pos = go.transform.localPosition,
            Rot = go.transform.localEulerAngles,
            Scale = go.transform.localScale,
        };
        doc.Objects.Add(g);

        int n = go.ComponentCount;
        for (int i = 0; i < n; i++)
        {
            var c = go.ComponentAt(i);
            if (c is Transform)
                continue; // transform GoDoc'ta (Unity da ayni: ozel serilesir)
            var entry = catalog.Find(c.GetType());
            if (entry == null)
                continue;
            var cd = new CompDoc { Type = entry.Name, Enabled = c._enabled };
            foreach (var f in entry.Schema)
                cd.Props.Add(new(f.Name, SerializedType.WriteField(c, f,
                    target => GoRefValue(target, ids),
                    target => CompRefValue(target, ids),
                    assets)));
            g.Components.Add(cd);
        }

        for (var t = go.transform.FirstChild; t != null; t = t.NextSibling)
            if (t._gameObject != null)
                CaptureGo(doc, catalog, t._gameObject, g.Id, ids, assets);
    }

    internal static string GoRefValue(GameObject target, Dictionary<GameObject, int> ids)
        => target != null && ids.TryGetValue(target, out int id)
            ? id.ToString(CultureInfo.InvariantCulture) : "";

    // "goId:TipAdi:ayniTiptenKacinci" — sahne disi referans kaydedilemez (bos).
    internal static string CompRefValue(Component c, Dictionary<GameObject, int> ids)
    {
        if (c == null || c._gameObject == null || !ids.TryGetValue(c._gameObject, out int id))
            return "";
        var go = c._gameObject;
        int idx = 0;
        for (int i = 0; i < go.ComponentCount; i++)
        {
            var o = go.ComponentAt(i);
            if (o == c)
                break;
            if (o.GetType() == c.GetType())
                idx++;
        }
        return id + ":" + c.GetType().Name + ":" + idx;
    }

    // --- Doc → canli sahne ---

    // Unity deserialize sirasi: alanlar Awake'ten ONCE yazilir. Bu yuzden
    // component'ler aktivasyonsuz eklenir, tum alanlar yazildiktan sonra
    // topluca aktive edilir (Awake taze degerleri gorur).
    public void InstantiateInto(Scene scene, TypeCatalog catalog, AssetDatabase assets)
    {
        scene.Catalog = catalog;
        Scene.SetActive(scene); // new GameObject bu sahneye dogar
        Spawn(null, catalog, assets);
    }

    // Doc'u AKTIF sahneye (istege bagli parent altina) kurar; ilk kok GO'yu dondurur.
    // Prefab spawn da sahne yukleme de ayni yol: referans cozumu + gec aktivasyon.
    // liveMap verilirse docId -> canli GO eslesmesi doldurulur (editor patch'leri icin).
    public GameObject Spawn(Transform parent, TypeCatalog catalog, AssetDatabase assets,
        Dictionary<int, GameObject> liveMap = null)
    {
        GameObject first = null;
        var byId = liveMap ?? new Dictionary<int, GameObject>();
        byId.Clear();
        // Referanslar TUM nesneler dogduktan sonra cozulur (ileri referans serbest).
        // Liste/ic-ice nesne icindeki referanslar da ayni kuyruga girer (closure set).
        var deferred = new List<(SerializedType.Kind kind, string val, Action<object> set)>();
        void Sink(SerializedType.Kind kind, string val, Action<object> set) => deferred.Add((kind, val, set));

        // 1. gecis: tum GO'lar dogar — parent cozumu liste sirasindan BAGIMSIZ
        // (reparent edilmis doc'ta cocuk parent'tan once gelebilir).
        foreach (var g in Objects)
        {
            var go = new GameObject(g.Name);
            byId[g.Id] = go;
            first ??= go;
        }

        foreach (var g in Objects)
        {
            var go = byId[g.Id];
            if (g.Parent != 0 && byId.TryGetValue(g.Parent, out var p))
                go.transform.SetParent(p.transform, false);
            else if (parent != null)
                go.transform.SetParent(parent, false);
            go.transform.localPosition = g.Pos;
            go.transform.localEulerAngles = g.Rot;
            go.transform.localScale = g.Scale;

            foreach (var cd in g.Components)
            {
                var entry = catalog.Find(cd.Type);
                if (entry == null)
                    continue; // bilinmeyen tip: sessiz atla (eski sahne + silinmis component)
                var c = go.AddComponentRaw(entry);
                c._enabled = cd.Enabled;
                foreach (var kv in cd.Props)
                {
                    var f = SerializedType.Find(entry.Schema, kv.Key);
                    if (f != null)
                        SerializedType.ReadField(c, f, kv.Value, assets, Sink);
                }
            }
            if (!g.Active)
                go.SetActive(false);
        }
        foreach (var (kind, val, set) in deferred)
            set(ResolveRef(kind, val, byId));
        foreach (var g in Objects)
            byId[g.Id].ActivateComponentsNow();
        return first;
    }

    static object ResolveRef(SerializedType.Kind kind, string val, Dictionary<int, GameObject> byId)
    {
        if (string.IsNullOrEmpty(val))
            return null;
        if (kind == SerializedType.Kind.GoRef)
            return int.TryParse(val, out int goId) && byId.TryGetValue(goId, out var go) ? go : null;
        var p = val.Split(':');
        if (p.Length != 3 || !int.TryParse(p[0], out int id) || !byId.TryGetValue(id, out var target))
            return null;
        if (!int.TryParse(p[2], out int wantIdx))
            return null;
        int idx = 0;
        for (int i = 0; i < target.ComponentCount; i++)
        {
            var c = target.ComponentAt(i);
            if (c.GetType().Name != p[1])
                continue;
            if (idx == wantIdx)
                return c;
            idx++;
        }
        return null;
    }

    // --- YAML (el yazimi dar altkume: 2'ser bosluk girinti, "- " liste ogesi) ---

    public void Save(string path) => File.WriteAllText(path, ToYaml());

    public static SceneDoc Load(string path) => Parse(File.ReadAllText(path));

    public string ToYaml() => ToYaml(null, null);

    // catalog+assets verilirse prefab instance'lari DELTAYA sikistirilir (collapse):
    // expand-node'lar yazilmaz, kokte guid + ids esleme + override diff'i yazilir.
    public string ToYaml(TypeCatalog catalog, AssetDatabase assets)
    {
        bool collapse = catalog != null && assets != null;
        var root = DocNode.Map();
        root.Add("scene", DocNode.Scal(Name));
        var objects = DocNode.Seq();
        root.Add("objects", objects);
        foreach (var g in Objects)
        {
            if (collapse && g.IsPrefabChild)
                continue; // delta: expand-node diske yazilmaz
            var m = DocNode.Map();
            m.Add("id", DocNode.Scal(g.Id.ToString(CultureInfo.InvariantCulture)));
            m.Add("parent", DocNode.Scal(g.Parent.ToString(CultureInfo.InvariantCulture)));
            m.Add("name", DocNode.Scal(g.Name));
            if (!g.Active)
                m.Add("active", DocNode.Scal("false"));
            m.Add("pos", DocNode.Scal(V3(g.Pos)));
            m.Add("rot", DocNode.Scal(V3(g.Rot)));
            m.Add("scale", DocNode.Scal(V3(g.Scale)));
            if (collapse && g.IsPrefabRoot)
            {
                WritePrefabRecord(m, g, catalog, assets);
                objects.Items.Add(m);
                continue;
            }
            if (g.Components.Count > 0)
            {
                var comps = DocNode.Seq();
                m.Add("components", comps);
                foreach (var cd in g.Components)
                    comps.Items.Add(CompToNode(cd));
            }
            objects.Items.Add(m);
        }
        return Yaml.Write(root);
    }

    static DocNode CompToNode(CompDoc cd)
    {
        var cm = DocNode.Map();
        cm.Add("type", DocNode.Scal(cd.Type));
        if (!cd.Enabled)
            cm.Add("enabled", DocNode.Scal("false"));
        foreach (var kv in cd.Props)
            cm.Add(kv.Key, kv.Value);
        return cm;
    }

    public static SceneDoc Parse(string text)
    {
        var doc = new SceneDoc();
        var root = Yaml.Parse(text);
        doc.Name = root.GetScalar("scene", "Main");
        var objects = root.Get("objects");
        if (objects?.Items == null)
            return doc;
        foreach (var m in objects.Items)
        {
            if (!m.IsMap)
                continue;
            var g = new GoDoc
            {
                Id = ParseInt(m.GetScalar("id")),
                Parent = ParseInt(m.GetScalar("parent")),
                Name = m.GetScalar("name", "GameObject"),
                Active = m.GetScalar("active", "true") != "false",
                Pos = SerializedType.ParseVec3(m.GetScalar("pos", "0 0 0")),
                Rot = SerializedType.ParseVec3(m.GetScalar("rot", "0 0 0")),
                Scale = SerializedType.ParseVec3(m.GetScalar("scale", "1 1 1")),
            };
            doc.Objects.Add(g);
            string prefab = m.GetScalar("prefab", null);
            if (!string.IsNullOrEmpty(prefab))
            {
                // Delta kaydi: ExpandPrefabs cagrilana kadar bosaltilmis instance koku.
                g.PrefabGuid = prefab;
                g.PrefabIds = ParseIdMap(m.GetScalar("prefabIds", ""));
                g.PrefabRemoved = ParseIdList(m.GetScalar("removed", ""));
                g.PendingOverrides = m.Get("overrides");
                continue; // component'ler prefab'dan gelir
            }
            var comps = m.Get("components");
            if (comps?.Items == null)
                continue;
            foreach (var cm in comps.Items)
                if (cm.IsMap)
                    g.Components.Add(CompFromNode(cm));
        }
        return doc;
    }

    // Yaml comp haritasi -> CompDoc (Parse ve prefab addComp override'i ayni yolu kullanir).
    internal static CompDoc CompFromNode(DocNode cm)
    {
        var cd = new CompDoc
        {
            Type = cm.GetScalar("type"),
            Enabled = cm.GetScalar("enabled", "true") != "false",
        };
        foreach (var kv in cm.Fields)
            if (kv.Key != "type" && kv.Key != "enabled")
                cd.Props.Add(kv);
        return cd;
    }

    static int ParseInt(string s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    static Dictionary<int, int> ParseIdMap(string s)
    {
        var map = new Dictionary<int, int>();
        foreach (var pair in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            int c = pair.IndexOf(':');
            if (c > 0 && int.TryParse(pair.Substring(0, c), out int k) && int.TryParse(pair.Substring(c + 1), out int v))
                map[k] = v;
        }
        return map;
    }

    static List<int> ParseIdList(string s)
    {
        var list = new List<int>();
        foreach (var part in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (int.TryParse(part, out int v))
                list.Add(v);
        return list;
    }

    static string V3(Vec3 v)
        => v.x.ToString("R", CultureInfo.InvariantCulture) + " "
         + v.y.ToString("R", CultureInfo.InvariantCulture) + " "
         + v.z.ToString("R", CultureInfo.InvariantCulture);
}
