#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;

namespace DigitoyEngine;

// TEMA KATMANI (docs/engine-notes.md TEMA): sahne/prefab dosyasinin icinde isimli delta —
// `themes: { xmas: { overrides:[{node,comp,prop,value}], removed:"7 9", added:[GoDoc...] } }`.
// node = doc'un KENDI id'si (ayni dosya, remap yok). Build-time projeksiyon: Prepare(theme) bir kez
// bindirir, runtime tema bilmez. Prefab instance override'iyla AYNI sema/kod (map = kimlik).
public sealed partial class SceneDoc
{
    // TEK BOGAZ: delta kayitlari acilir, aktif tema bindirilir, temanin ekledigi instance'lar acilir.
    // Tum yukleyiciler (editor, LoadPrefab, pak, loose host) buradan gecer.
    public void Prepare(TypeCatalog catalog, AssetDatabase assets, string theme)
    {
        ExpandPrefabs(catalog, assets);
        if (ApplyTheme(theme))
            ExpandPrefabs(catalog, assets);
    }

    // Katmani projeksiyona bindirir. false = tema yok/bos (taban). Sira: removed -> added -> overrides
    // (override eklenen node'u da hedefleyebilir).
    public bool ApplyTheme(string theme)
    {
        AppliedTheme = theme ?? "";
        if (string.IsNullOrEmpty(theme) || !Themes.TryGetValue(theme, out var layer) || layer == null)
            return false;
        foreach (int id in ParseIdList(layer.GetScalar("removed", "")))
            RemoveSubtree(id);
        var added = layer.Get("added");
        if (added?.Items != null)
            foreach (var m in added.Items)
                if (m.IsMap)
                    Objects.Add(ParseGoRecord(m.Clone()));
        ApplyOverrideItems(layer.Get("overrides"), null);
        return true;
    }

    // Bu doc (projeksiyon) ile taban arasindaki fark = tema katmani. Ikisi de ayni metinden Prepare edilmis
    // (expanded) olmali; id'ler ortak uzay. Prefab instance diff'iyle ayni kod (DiffNodeFields/DiffComponents).
    public DocNode DiffTheme(SceneDoc baseDoc, TypeCatalog catalog, AssetDatabase assets)
    {
        var layer = DocNode.Map();
        var ovs = DocNode.Seq();
        var removed = new List<int>();
        var added = DocNode.Seq();

        foreach (var b in baseDoc.Objects)
        {
            var g = FindById(b.Id);
            if (g == null)
            {
                // Yalniz en ustteki silinen yazilir (ApplyTheme alt agaci birlikte dusurur).
                var bp = b.Parent != 0 ? baseDoc.FindById(b.Parent) : null;
                if (bp == null || FindById(bp.Id) != null)
                    removed.Add(b.Id);
                continue;
            }
            DiffNodeFields(ovs, b, g);
            if (g.Parent != b.Parent)
                AddOverride(ovs, b.Id, null, "parent", DocNode.Scal(g.Parent.ToString(CultureInfo.InvariantCulture)));
            DiffComponents(ovs, b, g, catalog, null);
        }
        foreach (var g in Objects)
        {
            if (baseDoc.FindById(g.Id) != null)
                continue;
            if (g.IsPrefabChild && baseDoc.FindById(g.PrefabRootId) == null)
                continue; // eklenen instance'in expand-node'u: kayit kokte (delta)
            added.Items.Add(GoToNode(g, collapse: catalog != null && assets != null, catalog, assets));
        }

        if (ovs.Items.Count > 0)
            layer.Add("overrides", ovs);
        if (removed.Count > 0)
            layer.Add("removed", DocNode.Scal(string.Join(' ', removed)));
        if (added.Items.Count > 0)
            layer.Add("added", added);
        return layer;
    }

    // Katmandaki yetim hedefler (tabanda olmayan node'lar): theme.check raporu.
    public List<string> ThemeOrphans(string theme)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(theme) || !Themes.TryGetValue(theme, out var layer) || layer == null)
            return list;
        var known = new HashSet<int>();
        foreach (var g in Objects)
            known.Add(g.Id);
        var added = layer.Get("added");
        if (added?.Items != null)
            foreach (var m in added.Items)
                known.Add(ParseInt(m.GetScalar("id")));
        foreach (int id in ParseIdList(layer.GetScalar("removed", "")))
            if (!known.Contains(id))
                list.Add("removed " + id);
        var ovs = layer.Get("overrides");
        if (ovs?.Items != null)
            foreach (var o in ovs.Items)
                if (o.IsMap && !known.Contains(ParseInt(o.GetScalar("node"))))
                    list.Add("override node " + o.GetScalar("node") + " " + (o.GetScalar("comp", null) ?? "") + "." + o.GetScalar("prop"));
        return list;
    }

    public GoDoc FindGo(int id) => FindById(id);

    void RemoveSubtree(int id)
    {
        for (int i = Objects.Count - 1; i >= 0; i--)
            if (Objects[i].Parent == id)
                RemoveSubtree(Objects[i].Id);
        for (int i = 0; i < Objects.Count; i++)
            if (Objects[i].Id == id)
            {
                Objects.RemoveAt(i);
                return;
            }
    }
}
#endif
