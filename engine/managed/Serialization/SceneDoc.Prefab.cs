using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DigitoyEngine;

// PREFAB: "diskte delta, bellekte expanded" modeli. Instance kaydi (guid + ids
// eslemesi + override diff + removed) yuklemede tam GoDoc agacina ACILIR; editor
// ve runtime siradan GoDoc gorur (motor prefab bilmez). Save aninda prefab
// degerleriyle diff'lenip deltaya geri SIKISTIRILIR. Prefab-ici Go/CompRef'ler
// expand'de sahne id'lerine remap edilir (diff de ayni remap uzerinden yapilir).
public sealed partial class SceneDoc
{
    // Yuklemeden sonra bir kez: tum instance kayitlarini acar.
    public void ExpandPrefabs(TypeCatalog catalog, AssetDatabase assets)
    {
        // Expand liste buyutur: kayitlarin anlik kopyasi uzerinden.
        var roots = new List<GoDoc>();
        foreach (var g in Objects)
            if (g.IsPrefabRoot && g.PrefabLocalId == 0)
                roots.Add(g);
        foreach (var r in roots)
            ExpandInstance(r, catalog, assets);
    }

    // Tek instance'i acar: prefab dogunu klonlar, id esler, override'lari uygular.
    public void ExpandInstance(GoDoc root, TypeCatalog catalog, AssetDatabase assets)
    {
        var prefab = assets?.LoadPrefab(root.PrefabGuid)?.Doc;
        if (prefab == null)
            return; // missing prefab: delta oldugu gibi kalir (kaydedince kaybolmaz)
        root.PrefabIds ??= new Dictionary<int, int>();
        root.PrefabRemoved ??= new List<int>();

        int nextId = 0;
        foreach (var g in Objects)
            if (g.Id > nextId)
                nextId = g.Id;
        foreach (var kv in root.PrefabIds)
            if (kv.Value > nextId)
                nextId = kv.Value;

        // 1. gecis: localId -> sceneId eslemesi (yeni prefab cocuklari yeni id alir).
        int prefabRootLocal = 0;
        foreach (var pl in prefab.Objects)
        {
            if (pl.Parent == 0)
                prefabRootLocal = pl.Id;
            if (IsRemovedWithAncestors(prefab, root, pl))
                continue;
            if (pl.Parent == 0)
                root.PrefabIds[pl.Id] = root.Id;
            else if (!root.PrefabIds.ContainsKey(pl.Id))
                root.PrefabIds[pl.Id] = ++nextId;
        }
        root.PrefabLocalId = prefabRootLocal;
        root.PrefabRootId = root.Id;

        // 2. gecis: node'lari uret (kok instance kaydiyla birlesir).
        int insertAt = Objects.IndexOf(root) + 1;
        foreach (var pl in prefab.Objects)
        {
            if (IsRemovedWithAncestors(prefab, root, pl))
                continue;
            GoDoc g;
            if (pl.Parent == 0)
            {
                g = root; // kok: transform/parent/ad instance'a ait, component'ler prefab'dan
                g.Components.Clear();
            }
            else
            {
                g = new GoDoc
                {
                    Id = root.PrefabIds[pl.Id],
                    Parent = root.PrefabIds.GetValueOrDefault(pl.Parent, root.Id),
                    Name = pl.Name,
                    Active = pl.Active,
                    Pos = pl.Pos,
                    Rot = pl.Rot,
                    Scale = pl.Scale,
                    PrefabLocalId = pl.Id,
                    PrefabRootId = root.Id,
                };
                Objects.Insert(insertAt++, g);
            }
            foreach (var cd in pl.Components)
            {
                var nc = new CompDoc { Type = cd.Type, Enabled = cd.Enabled };
                foreach (var kv in cd.Props)
                    nc.Props.Add(new(kv.Key, kv.Value.Clone()));
                RemapCompRefs(nc, catalog, root.PrefabIds); // prefab-ici ref -> sahne id
                g.Components.Add(nc);
            }
        }

        ApplyOverrides(root);
        root.PendingOverrides = null;
    }

    static bool IsRemovedWithAncestors(SceneDoc prefab, GoDoc root, GoDoc pl)
    {
        int cur = pl.Id, guard = 0;
        while (cur != 0 && guard++ < 1000)
        {
            if (root.PrefabRemoved != null && root.PrefabRemoved.Contains(cur))
                return true;
            GoDoc p = null;
            foreach (var o in prefab.Objects)
                if (o.Id == cur) { p = o; break; }
            cur = p?.Parent ?? 0;
        }
        return false;
    }

    // Prefab-ici Go/CompRef scalar'larini sahne id'lerine cevirir (sema uzerinden).
    // clearUnmapped: haritada olmayan ref BOSALIR (prefab yaratirken alt-agac disi
    // referanslar dosyaya sizamaz); false ise oldugu gibi kalir (expand yolu).
    internal static void RemapCompRefs(CompDoc cd, TypeCatalog catalog, Dictionary<int, int> map,
        bool clearUnmapped = false)
    {
        var entry = catalog?.Find(cd.Type);
        if (entry == null)
            return;
        foreach (var kv in cd.Props)
        {
            var f = SerializedType.Find(entry.Schema, kv.Key);
            if (f == null)
                continue;
            RemapValue(kv.Value, f, map, clearUnmapped);
        }
    }

    // Bir serialized alan agacindaki tum sahne referanslarini verilen id uzayina cevirir.
    public static void RemapValue(DocNode node, SerializedType.FieldSchema field,
        Dictionary<int, int> map, bool clearUnmapped = false)
    {
        if (node == null)
            return;
        if (field.Kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
        {
            node.Scalar = RemapRefScalar(node.Scalar, field.Kind, map, clearUnmapped);
            return;
        }
        if (field.Kind == SerializedType.Kind.List && node.Items != null)
        {
            if (field.ElementKind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
            {
                foreach (var item in node.Items)
                    item.Scalar = RemapRefScalar(item.Scalar, field.ElementKind, map, clearUnmapped);
            }
            else if (field.ElementKind == SerializedType.Kind.Object)
            {
                foreach (var item in node.Items)
                    RemapObject(item, field.Nested, map, clearUnmapped);
            }
            return;
        }
        if (field.Kind == SerializedType.Kind.Object)
            RemapObject(node, field.Nested, map, clearUnmapped);
    }

    static void RemapObject(DocNode mapNode, SerializedType.FieldSchema[] schema,
        Dictionary<int, int> map, bool clearUnmapped)
    {
        if (mapNode?.Fields == null || schema == null)
            return;
        foreach (var pair in mapNode.Fields)
        {
            var field = SerializedType.Find(schema, pair.Key);
            if (field != null)
                RemapValue(pair.Value, field, map, clearUnmapped);
        }
    }

    static string RemapRefScalar(string s, SerializedType.Kind kind, Dictionary<int, int> map,
        bool clearUnmapped = false)
    {
        if (string.IsNullOrEmpty(s))
            return s;
        if (kind == SerializedType.Kind.GoRef)
        {
            if (int.TryParse(s, out int id) && map.TryGetValue(id, out int sid))
                return sid.ToString(CultureInfo.InvariantCulture);
            return clearUnmapped ? "" : s;
        }
        int c = s.IndexOf(':');
        if (c > 0 && int.TryParse(s.Substring(0, c), out int cid) && map.TryGetValue(cid, out int csid))
            return csid + s.Substring(c);
        return clearUnmapped ? "" : s;
    }

    void ApplyOverrides(GoDoc root)
    {
        var ovs = root.PendingOverrides;
        if (ovs?.Items == null)
            return;
        foreach (var o in ovs.Items)
        {
            if (!o.IsMap)
                continue;
            int local = int.TryParse(o.GetScalar("node"), out int l) ? l : 0;
            if (!root.PrefabIds.TryGetValue(local, out int sceneId))
                continue;
            var g = FindById(sceneId);
            if (g == null)
                continue;
            string comp = o.GetScalar("comp", null);
            string prop = o.GetScalar("prop");
            var value = o.Get("value");
            if (string.IsNullOrEmpty(comp))
            {
                switch (prop)
                {
                    case "name": g.Name = value?.Scalar ?? g.Name; break;
                    case "active": g.Active = value?.Scalar != "false"; break;
                    case "pos": g.Pos = SerializedType.ParseVec3(value?.Scalar ?? "0 0 0"); break;
                    case "rot": g.Rot = SerializedType.ParseVec3(value?.Scalar ?? "0 0 0"); break;
                    case "scale": g.Scale = SerializedType.ParseVec3(value?.Scalar ?? "1 1 1"); break;
                    case "addComp": // instance'a eklenen comp (ref'ler sahne uzayinda kayitli)
                        if (value != null && value.IsMap)
                            g.Components.Add(CompFromNode(value.Clone()));
                        break;
                }
                continue;
            }
            var cd = FindComp(g, comp);
            if (prop == "removeComp")
            {
                if (cd != null)
                    g.Components.Remove(cd);
                continue;
            }
            if (cd == null)
                continue;
            if (prop == "enabled")
            {
                cd.Enabled = value?.Scalar != "false";
                continue;
            }
            var clone = value?.Clone() ?? DocNode.Scal("");
            for (int i = 0; i < cd.Props.Count; i++)
            {
                if (cd.Props[i].Key == prop)
                {
                    cd.Props[i] = new(prop, clone);
                    goto next;
                }
            }
            cd.Props.Add(new(prop, clone));
        next:;
        }
    }

    GoDoc FindById(int id)
    {
        foreach (var g in Objects)
            if (g.Id == id)
                return g;
        return null;
    }

    // "Tip:idx" adresinden component (ayni tipten idx'inci).
    static CompDoc FindComp(GoDoc g, string address)
    {
        int c = address.LastIndexOf(':');
        string type = c > 0 ? address.Substring(0, c) : address;
        int want = c > 0 && int.TryParse(address.Substring(c + 1), out int w) ? w : 0;
        int idx = 0;
        foreach (var cd in g.Components)
        {
            if (cd.Type != type)
                continue;
            if (idx == want)
                return cd;
            idx++;
        }
        return null;
    }

    // --- Collapse: kok kaydina guid + ids + removed + override diff'i yazar ---

    void WritePrefabRecord(DocNode m, GoDoc root, TypeCatalog catalog, AssetDatabase assets)
    {
        m.Add("prefab", DocNode.Scal(root.PrefabGuid));
        var sb = new StringBuilder();
        if (root.PrefabIds != null)
            foreach (var kv in root.PrefabIds)
                sb.Append(kv.Key).Append(':').Append(kv.Value).Append(' ');
        m.Add("prefabIds", DocNode.Scal(sb.ToString().TrimEnd()));
        if (root.PrefabRemoved is { Count: > 0 })
            m.Add("removed", DocNode.Scal(string.Join(' ', root.PrefabRemoved)));
        // Prefab kayipsa diff uretilemez: parse'tan tasinan delta AYNEN geri yazilir
        // (yoksa missing prefab + Save = override kaybi olurdu).
        var ovs = BuildOverrides(root, catalog, assets) ?? root.PendingOverrides;
        if (ovs?.Items is { Count: > 0 })
            m.Add("overrides", ovs);
    }

    // Instance'in TUM override'lari (yaml diff'i ile ayni; editor Overrides paneli de kullanir).
    // Donen seq ogeleri: {node, comp?, prop, value}. null = prefab kayip.
    public DocNode BuildOverrides(GoDoc root, TypeCatalog catalog, AssetDatabase assets)
    {
        var prefab = assets?.LoadPrefab(root.PrefabGuid)?.Doc;
        if (prefab == null)
            return null; // missing prefab: diff uretilemez, delta korunur
        var ovs = DocNode.Seq();

        foreach (var pl in prefab.Objects)
        {
            if (!root.PrefabIds.TryGetValue(pl.Id, out int sceneId))
                continue;
            var g = FindById(sceneId);
            if (g == null)
                continue;
            bool isRoot = g == root;
            // Kok transform/ad instance'a aittir (normal alanlarda yazildi) — diff yok.
            if (!isRoot)
            {
                if (g.Name != pl.Name)
                    AddOverride(ovs, pl.Id, null, "name", DocNode.Scal(g.Name));
                if (g.Active != pl.Active)
                    AddOverride(ovs, pl.Id, null, "active", DocNode.Scal(g.Active ? "true" : "false"));
                if (!SameV3(g.Pos, pl.Pos))
                    AddOverride(ovs, pl.Id, null, "pos", DocNode.Scal(V3(g.Pos)));
                if (!SameV3(g.Rot, pl.Rot))
                    AddOverride(ovs, pl.Id, null, "rot", DocNode.Scal(V3(g.Rot)));
                if (!SameV3(g.Scale, pl.Scale))
                    AddOverride(ovs, pl.Id, null, "scale", DocNode.Scal(V3(g.Scale)));
            }
            DiffComponents(ovs, pl, g, catalog, root.PrefabIds);
        }
        return ovs;
    }

    void DiffComponents(DocNode ovs, GoDoc pl, GoDoc g, TypeCatalog catalog, Dictionary<int, int> map)
    {
        // Karsilastirma prefab'in REMAP EDILMIS klonu uzerinden: prefab-ici ref'ler
        // sahne id'lerine cevrilir ki degismemis ref esit ciksin.
        var occ = new Dictionary<string, int>();
        foreach (var pcd in pl.Components)
        {
            int idx = occ.GetValueOrDefault(pcd.Type);
            occ[pcd.Type] = idx + 1;
            string addr = pcd.Type + ":" + idx;
            var scd = FindComp(g, addr);
            if (scd == null)
            {
                AddOverride(ovs, pl.Id, addr, "removeComp", DocNode.Scal("true")); // instance'ta silinmis
                continue;
            }

            var refCmp = new CompDoc { Type = pcd.Type };
            foreach (var kv in pcd.Props)
                refCmp.Props.Add(new(kv.Key, kv.Value.Clone()));
            RemapCompRefs(refCmp, catalog, map);

            if (scd.Enabled != pcd.Enabled)
                AddOverride(ovs, pl.Id, addr, "enabled", DocNode.Scal(scd.Enabled ? "true" : "false"));
            foreach (var kv in scd.Props)
            {
                DocNode pv = null;
                foreach (var pkv in refCmp.Props)
                    if (pkv.Key == kv.Key) { pv = pkv.Value; break; }
                if (!DocNode.Equal(kv.Value, pv))
                    AddOverride(ovs, pl.Id, addr, kv.Key, kv.Value.Clone());
            }
        }
        // Instance'a EKLENEN comp'lar (prefab'da karsiligi yok): tam govde override'i.
        var sceneOcc = new Dictionary<string, int>();
        foreach (var scd in g.Components)
        {
            int idx = sceneOcc.GetValueOrDefault(scd.Type);
            sceneOcc[scd.Type] = idx + 1;
            if (idx < occ.GetValueOrDefault(scd.Type))
                continue; // prefab'daki karsiligi (ayni tip/sira) zaten diff'lendi
            AddOverride(ovs, pl.Id, null, "addComp", CompToNode(scd).Clone());
        }
    }

    static void AddOverride(DocNode ovs, int local, string comp, string prop, DocNode value)
    {
        var o = DocNode.Map();
        o.Add("node", DocNode.Scal(local.ToString(CultureInfo.InvariantCulture)));
        if (comp != null)
            o.Add("comp", DocNode.Scal(comp));
        o.Add("prop", DocNode.Scal(prop));
        o.Add("value", value);
        ovs.Items.Add(o);
    }

    static bool SameV3(Vec3 a, Vec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    // --- Editor yardimcilari: override tespiti + Revert/Apply icin kaynak erisimi ---

    // g'nin bagli oldugu instance kokunu dondurur (kendisi de olabilir); prefab disiysa null.
    public GoDoc PrefabRootOf(GoDoc g)
        => g.IsPrefabRoot ? g : g.PrefabRootId != 0 ? FindById(g.PrefabRootId) : null;

    // g'nin prefab kaynak node'u + kok kaydi. false = prefab'a bagli degil / prefab kayip.
    public bool TryGetPrefabSource(GoDoc g, AssetDatabase assets,
        out SceneDoc prefabDoc, out GoDoc sourceNode, out GoDoc rootRec)
    {
        prefabDoc = null;
        sourceNode = null;
        rootRec = PrefabRootOf(g);
        if (rootRec == null || g.PrefabLocalId == 0)
            return false;
        prefabDoc = assets?.LoadPrefab(rootRec.PrefabGuid)?.Doc;
        if (prefabDoc == null)
            return false;
        foreach (var pl in prefabDoc.Objects)
            if (pl.Id == g.PrefabLocalId)
            {
                sourceNode = pl;
                return true;
            }
        return false;
    }

    // cd'nin GO icindeki adresi ("Tip:ayniTiptenKacinci").
    public static string CompAddressOf(GoDoc g, CompDoc cd)
    {
        int idx = 0;
        foreach (var c in g.Components)
        {
            if (c == cd)
                break;
            if (c.Type == cd.Type)
                idx++;
        }
        return cd.Type + ":" + idx;
    }

    public static CompDoc FindCompByAddress(GoDoc g, string address) => FindComp(g, address);

    // Prefab'taki alan degerinin sahne uzayina REMAP edilmis klonu (yoksa null).
    public static DocNode RemappedPrefabValue(CompDoc sourceComp, SerializedType.FieldSchema f,
        Dictionary<int, int> map)
    {
        DocNode src = null;
        foreach (var kv in sourceComp.Props)
            if (kv.Key == f.Name || (f.FormerName != null && kv.Key == f.FormerName))
            {
                src = kv.Value;
                break;
            }
        if (src == null)
            return null;
        var clone = src.Clone();
        RemapValue(clone, f, map);
        return clone;
    }

    // Map'in tersi (sceneId -> localId): Apply'da sahne degeri prefab uzayina cevrilir.
    public static Dictionary<int, int> InvertMap(Dictionary<int, int> map)
    {
        var inv = new Dictionary<int, int>(map.Count);
        foreach (var kv in map)
            inv[kv.Value] = kv.Key;
        return inv;
    }

    public static string RemapScalar(string s, SerializedType.Kind kind, Dictionary<int, int> map)
        => RemapRefScalar(s, kind, map);
}
