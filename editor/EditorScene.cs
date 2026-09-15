using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Acik sahnenin DOC-KAYNAKLI durumu: SceneDoc = kaynak gercek, canli sahne =
// projeksiyon. Paneller DOC'u gosterir/duzenler; her duzenleme canliya PATCH
// olarak basilir (anlik gorsel). Save doc'u yazar — runtime degerleri dosyaya
// ASLA sizamaz. Reload/kod degisikligi = doc'tan taze projeksiyon.
public sealed class EditorScene
{
    public SceneDoc Doc { get; private set; } = new();
    public string Path { get; private set; }

    // Canli projeksiyonun sahnesi: HIC simule olmaz (ExternallyDriven), editor
    // her frame lifecycle-only Update ile surer. Play'in oyun sahnesinden ayridir.
    public Scene LiveScene { get; private set; }

    readonly Dictionary<int, GameObject> _live = new();
    TypeCatalog _catalog;
    AssetDatabase _assets;

    public void Load(string path, TypeCatalog catalog, AssetDatabase assets)
    {
        Path = path;
        Doc = File.Exists(path) ? SceneDoc.Load(path) : new SceneDoc();
        Doc.ExpandPrefabs(catalog, assets); // delta kayitlari tam agaca acilir
        Instantiate(catalog, assets);
        _savedVersion = History.Version;
    }

    // Kaydedilmemis duzenleme var mi (disk cakisma politikasi: bellek kazanir).
    public bool Dirty => History.Version != _savedVersion;
    int _savedVersion;

    // Prefab dosyasi DISKTE degisti: doc deltaya sikistirilip taze prefab'la yeniden
    // acilir. prefabIds kalici oldugundan id/secim/undo bozulmaz; kaydedilmemis
    // override'lar delta olarak yasamaya devam eder.
    public void ReexpandPrefabs()
    {
        if (_catalog == null)
            return;
        Doc = SceneDoc.Parse(Doc.ToYaml(_catalog, _assets));
        Doc.ExpandPrefabs(_catalog, _assets);
        Instantiate(_catalog, _assets);
    }

    // Doc'tan taze projeksiyon: eski projeksiyon sahnesi tamamen yikilir.
    // Play sirasinda cagrilirsa aktif sahne (oyun) CALINMAZ — projeksiyon arkada yenilenir.
    public void Instantiate(TypeCatalog catalog, AssetDatabase assets)
    {
        _catalog = catalog;
        _assets = assets;
        var old = LiveScene;
        var prevActive = Scene.Active;
        bool oldWasActive = old == null || prevActive == old;
        var fresh = Scene.Create(Doc.Name);
        fresh.Catalog = catalog;
        fresh.ExternallyDriven = true; // UpdateAll atlar; EditorApp elle surer (simulate: false)
        Scene.SetActive(fresh); // Spawn aktif sahneye dogar
        Doc.Spawn(null, catalog, assets, _live);
        if (!oldWasActive && prevActive != null)
            Scene.SetActive(prevActive);
        LiveScene = fresh;
        if (old != null)
            Scene.Unload(old);
    }

    // Projeksiyonu tamamen yik (kod reload: ALC bosalmadan once tum instance'lar olmeli).
    public void UnloadLive()
    {
        if (LiveScene == null)
            return;
        Scene.Unload(LiveScene);
        LiveScene = null;
        _live.Clear();
    }

    public void Save()
    {
        if (Path == null)
            return;
        AssetWatcher.NoteSelfWrite(Path);
        File.WriteAllText(Path, Doc.ToYaml(_catalog, _assets)); // prefab'lar deltaya sikisir
        _savedVersion = History.Version;
    }

    public GameObject Live(int docId) => _live.GetValueOrDefault(docId);

    public SceneDoc.GoDoc FindGo(int docId)
    {
        foreach (var g in Doc.Objects)
            if (g.Id == docId)
                return g;
        return null;
    }

    // --- Patch'ler: doc degeri degisti, canli esleigine yansit ---

    public void ApplyTransform(SceneDoc.GoDoc g)
    {
        var go = Live(g.Id);
        if (go == null)
            return;
        go.transform.localPosition = g.Pos;
        go.transform.localEulerAngles = g.Rot;
        go.transform.localScale = g.Scale;
    }

    public void ApplyActive(SceneDoc.GoDoc g) => Live(g.Id)?.SetActive(g.Active);

    public void ApplyLayer(SceneDoc.GoDoc g)
    {
        var go = Live(g.Id);
        if (go != null)
            go.layer = g.Layer;
    }

    // --- Kayitli mutasyonlar: TUM authored degisikliklerin bogazi (undo otomatik) ---

    public readonly UndoStack History = new();

    public void SetProp(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f,
        string oldValue, string newValue)
        => SetProp(g, cd, f,
            oldValue == null ? null : DocNode.Scal(oldValue),
            newValue == null ? null : DocNode.Scal(newValue));

    public void SetProp(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f,
        DocNode oldValue, DocNode newValue)
    {
        History.Push(new PropOp
        {
            GoId = g.Id,
            CompIndex = g.Components.IndexOf(cd),
            Prop = f.Name,
            OldValue = oldValue?.Clone(),
            NewValue = newValue?.Clone(),
        });
        WriteProp(g, cd, f, newValue);
    }

    public void SetTransform(SceneDoc.GoDoc g, Vec3 pos, Vec3 rot, Vec3 scale)
    {
        // Driven pozisyon (orn. layout'un yerlestirdigi kutu) editorden degistirilemez
        // (Unity driven-property): pos kismi sessizce eski degerde tutulur, rot/scale serbest.
        var live = Live(g.Id);
        if (live != null
            && (TransformDriver.Driven(live, out _) & DrivenTransformProperties.Position) != 0)
            pos = g.Pos;
        History.Push(new TransformOp
        {
            GoId = g.Id,
            OldPos = g.Pos,
            OldRot = g.Rot,
            OldScale = g.Scale,
            NewPos = pos,
            NewRot = rot,
            NewScale = scale,
        });
        g.Pos = pos;
        g.Rot = rot;
        g.Scale = scale;
        ApplyTransform(g);
    }

    public void SetActive(SceneDoc.GoDoc g, bool value)
    {
        History.Push(new ActiveOp { GoId = g.Id, Old = g.Active, New = value });
        g.Active = value;
        ApplyActive(g);
    }

    public void SetLayer(SceneDoc.GoDoc g, int value)
    {
        History.Push(new LayerOp { GoId = g.Id, Old = g.Layer, New = value });
        g.Layer = value;
        ApplyLayer(g);
    }

    public void SetEnabled(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, bool value)
    {
        History.Push(new EnabledOp
        {
            GoId = g.Id,
            CompIndex = g.Components.IndexOf(cd),
            Old = cd.Enabled,
            New = value,
        });
        cd.Enabled = value;
        ApplyEnabled(g, cd);
    }

    public void DoUndo() => History.Undo(this);
    public void DoRedo() => History.Redo(this);

    // --- Yapisal mutasyonlar: doc + full reload (canli cerrahi yok — reload ucuz ve tek yol) ---

    public void RefreshLive() => Instantiate(_catalog, _assets);

    int NextId()
    {
        int max = 0;
        foreach (var g in Doc.Objects)
            if (g.Id > max)
                max = g.Id;
        return max + 1;
    }

    // "ad", "ad (1)", "ad (2)"... (Unity deseni) — ayni ad varsa ek numara.
    string UniqueName(string baseName)
    {
        bool exists = false;
        foreach (var o in Doc.Objects)
            if (o.Name == baseName) { exists = true; break; }
        if (!exists)
            return baseName;
        for (int n = 1; ; n++)
        {
            string cand = baseName + " (" + n + ")";
            bool used = false;
            foreach (var o in Doc.Objects)
                if (o.Name == cand) { used = true; break; }
            if (!used)
                return cand;
        }
    }

    public SceneDoc.GoDoc AddGameObject(int parentId)
    {
        var g = new SceneDoc.GoDoc { Id = NextId(), Parent = parentId, Name = UniqueName("GameObject") };
        History.Push(new AddGoOp { Doc = g, Index = Doc.Objects.Count });
        Doc.Objects.Add(g);
        RefreshLive();
        return g;
    }

    // Prefab instance'i: kok kaydi eklenir ve hemen expand edilir (bellek hep acik model).
    public SceneDoc.GoDoc InstantiatePrefab(string guid, int parentId, Vec3? worldPos = null)
    {
        var prefab = _assets.LoadPrefab(guid)?.Doc;
        if (prefab == null)
            return null;
        string name = "Prefab";
        foreach (var pl in prefab.Objects)
            if (pl.Parent == 0)
            {
                name = pl.Name;
                break;
            }
        name = UniqueName(name); // instance adi benzersiz (kok adi instance'a aittir)
        var root = new SceneDoc.GoDoc { Id = NextId(), Parent = parentId, Name = name, PrefabGuid = guid };
        if (worldPos.HasValue)
            root.Pos = worldPos.Value;
        Doc.Objects.Add(root);
        Doc.ExpandInstance(root, _catalog, _assets);
        History.Push(new PrefabAddOp { RootId = root.Id, Guid = guid, Parent = parentId, Name = name });
        RefreshLive();
        return root;
    }

    // Instance kokunu + tum expand-node'larini doc'tan cikarir (PrefabAddOp undo).
    internal void RemovePrefabInstance(int rootId)
    {
        for (int i = Doc.Objects.Count - 1; i >= 0; i--)
            if (Doc.Objects[i].Id == rootId || Doc.Objects[i].PrefabRootId == rootId)
                Doc.Objects.RemoveAt(i);
        RefreshLive();
    }

    internal void ReaddPrefabInstance(int rootId, string guid, int parentId, string name)
    {
        var root = new SceneDoc.GoDoc { Id = rootId, Parent = parentId, Name = name, PrefabGuid = guid };
        Doc.Objects.Add(root);
        Doc.ExpandInstance(root, _catalog, _assets);
        RefreshLive();
    }

    // Doc alt agacindan .prefab dosyasi uretir ve alt agaci o prefab'in instance'i
    // olarak BAGLAR (Unity connect). Node'lar yerinde kalir, yalniz prefab isaretleri
    // basilir — canli sahne degismez. v1: prefab'a bagli nesneden yeni prefab yok (nested v2).
    public bool CreatePrefabAsset(SceneDoc.GoDoc g, string absPath)
    {
        if (g.IsPrefabRoot || g.IsPrefabChild)
        {
            EditorLog.Warning("[prefab] prefab'a bagli nesneden yeni prefab uretilemez (nested v2)");
            return false;
        }
        var sub = new List<(SceneDoc.GoDoc doc, int index)>();
        CollectSubtree(g, sub);
        var toLocal = new Dictionary<int, int>(); // sceneId -> localId (kok=1, doc sirasi)
        int next = 0;
        foreach (var (o, _) in sub)
            toLocal[o.Id] = ++next;

        var pd = new SceneDoc { Name = g.Name };
        foreach (var (o, _) in sub)
        {
            var pg = new SceneDoc.GoDoc
            {
                Id = toLocal[o.Id],
                Parent = o == g ? 0 : toLocal.GetValueOrDefault(o.Parent),
                Name = o.Name,
                Active = o.Active,
                Pos = o.Pos,
                Rot = o.Rot,
                Scale = o.Scale,
            };
            foreach (var cd in o.Components)
            {
                var nc = new SceneDoc.CompDoc { Type = cd.Type, Enabled = cd.Enabled };
                foreach (var kv in cd.Props)
                    nc.Props.Add(new(kv.Key, kv.Value.Clone()));
                // Sahne ref'leri prefab yerel uzayina; alt-agac disini gosterenler bosalir.
                SceneDoc.RemapCompRefs(nc, _catalog, toLocal, clearUnmapped: true);
                pg.Components.Add(nc);
            }
            pd.Objects.Add(pg);
        }

        AssetWatcher.NoteSelfWrite(absPath);
        pd.Save(absPath);
        _assets.ScanMetas(createMissing: true); // guid alsin
        string rel = System.IO.Path.GetRelativePath(_assets.Root, absPath).Replace('\\', '/');
        string guid = _assets.PathToGuid(rel);
        if (guid == null)
        {
            EditorLog.Error("[prefab] guid uretilemedi: " + rel);
            return false;
        }

        var op = new ConnectPrefabOp { RootId = g.Id, Guid = guid };
        foreach (var (o, _) in sub)
            op.Locals.Add((o.Id, toLocal[o.Id]));
        History.Push(op);
        ApplyConnectPrefab(op, undo: false);
        EditorLog.Info("[prefab] olusturuldu: " + rel);
        return true;
    }

    // Connect isaretleri: undo soker (dosya kalir), redo yeniden basar. Canli etkilenmez.
    internal void ApplyConnectPrefab(ConnectPrefabOp op, bool undo)
    {
        var root = FindGo(op.RootId);
        if (root == null)
            return;
        if (undo)
        {
            foreach (var (goId, _) in op.Locals)
            {
                var o = FindGo(goId);
                if (o == null)
                    continue;
                o.PrefabLocalId = 0;
                o.PrefabRootId = 0;
            }
            root.PrefabGuid = null;
            root.PrefabIds = null;
            root.PrefabRemoved = null;
        }
        else
        {
            root.PrefabGuid = op.Guid;
            root.PrefabIds = new Dictionary<int, int>();
            foreach (var (goId, localId) in op.Locals)
            {
                root.PrefabIds[localId] = goId;
                var o = FindGo(goId);
                if (o == null)
                    continue;
                o.PrefabLocalId = localId;
                o.PrefabRootId = op.RootId;
            }
        }
    }

    // Override'i prefab degerine geri dondurur (scalar/liste/nesne; normal undo'lu SetProp).
    // Prefab prop'u serilestirmemisse (default) null gelir: prop dokumandan SILINIR
    // ki deger gercek default'una donsun ("" parse edilmez).
    public void RevertField(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f, DocNode prefabValue)
    {
        var cur = FindProp(cd, f);
        SetProp(g, cd, f, cur, prefabValue);
    }

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

    // rootRec uzayindaki degeri once prefab uzayina (inv), sonra hedef uzaya (map) cevirir.
    static DocNode RemapAcross(DocNode v, SerializedType.FieldSchema f,
        Dictionary<int, int> inv, Dictionary<int, int> map)
    {
        if (v == null)
            return null;
        var c = v.Clone();
        SceneDoc.RemapValue(c, f, inv);
        SceneDoc.RemapValue(c, f, map);
        return c;
    }

    // Node duzeyi alani (pos/rot/scale/name/active) PREFAB'A yazar + dosya + yayilim.
    public void ApplyNodeFieldToPrefab(SceneDoc.GoDoc g, string prop)
    {
        if (!Doc.TryGetPrefabSource(g, _assets, out var prefabDoc, out var src, out var rootRec))
            return;
        // Eski prefab degeri (yayilim kiyasi) + yeni deger src'ye yazilir.
        Vec3 oldPos = src.Pos, oldRot = src.Rot, oldScale = src.Scale;
        string oldName = src.Name;
        bool oldActive = src.Active;
        switch (prop)
        {
            case "pos": src.Pos = g.Pos; break;
            case "rot": src.Rot = g.Rot; break;
            case "scale": src.Scale = g.Scale; break;
            case "name": src.Name = g.Name; break;
            case "active": src.Active = g.Active; break;
            default: return;
        }
        string rel = _assets.ResolvePath(rootRec.PrefabGuid);
        if (!string.IsNullOrEmpty(rel))
        {
            string pp = System.IO.Path.Combine(_assets.Root, rel);
            AssetWatcher.NoteSelfWrite(pp);
            prefabDoc.Save(pp);
        }
        foreach (var r in Doc.Objects)
        {
            if (!r.IsPrefabRoot || r.PrefabGuid != rootRec.PrefabGuid || r == rootRec)
                continue;
            if (!r.PrefabIds.TryGetValue(src.Id, out int sceneId))
                continue;
            var tg = FindGo(sceneId);
            if (tg == null || tg == r)
                continue; // kok node alanlari instance'a aittir
            switch (prop)
            {
                case "pos": if (SameV3(tg.Pos, oldPos)) tg.Pos = g.Pos; break;
                case "rot": if (SameV3(tg.Rot, oldRot)) tg.Rot = g.Rot; break;
                case "scale": if (SameV3(tg.Scale, oldScale)) tg.Scale = g.Scale; break;
                case "name": if (tg.Name == oldName) tg.Name = g.Name; break;
                case "active": if (tg.Active == oldActive) tg.Active = g.Active; break;
            }
        }
        RefreshLive();
    }

    static bool SameV3(Vec3 a, Vec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    // 'removeComp' revert'i: prefab comp'unu remap'li klonla geri ekler (undo'lu).
    public void RestoreComponent(SceneDoc.GoDoc g, SceneDoc.CompDoc prefabComp, Dictionary<int, int> map)
    {
        var nc = new SceneDoc.CompDoc { Type = prefabComp.Type, Enabled = prefabComp.Enabled };
        foreach (var kv in prefabComp.Props)
            nc.Props.Add(new(kv.Key, kv.Value.Clone()));
        var entry = _catalog?.Find(nc.Type);
        if (entry != null)
        {
            foreach (var kv in nc.Props)
            {
                var f = SerializedType.Find(entry.Schema, kv.Key);
                if (f == null)
                    continue;
                SceneDoc.RemapValue(kv.Value, f, map);
            }
        }
        History.Push(new AddCompOp { GoId = g.Id, Comp = nc, Index = g.Components.Count });
        g.Components.Add(nc);
        RefreshLive();
    }

    public void RemoveGameObject(SceneDoc.GoDoc g)
    {
        // Prefab COCUGU: alt-agac silinir + koke 'removed' isareti (expand bir daha uretmez).
        if (g.IsPrefabChild)
        {
            var root = Doc.PrefabRootOf(g);
            var pop = new RemovePrefabChildOp { RootId = root.Id, LocalId = g.PrefabLocalId };
            CollectSubtree(g, pop.Items);
            History.Push(pop);
            root.PrefabRemoved ??= new List<int>();
            root.PrefabRemoved.Add(g.PrefabLocalId);
            for (int i = pop.Items.Count - 1; i >= 0; i--)
                Doc.Objects.Remove(pop.Items[i].doc);
            RefreshLive();
            return;
        }
        var op = new RemoveGoOp();
        CollectSubtree(g, op.Items);
        History.Push(op);
        for (int i = op.Items.Count - 1; i >= 0; i--)
            Doc.Objects.Remove(op.Items[i].doc);
        RefreshLive();
    }

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

    void CollectSubtree(SceneDoc.GoDoc root, List<(SceneDoc.GoDoc, int)> into)
    {
        into.Add((root, Doc.Objects.IndexOf(root)));
        foreach (var c in Doc.Objects)
            if (c.Parent == root.Id)
                CollectSubtree(c, into);
    }

    public void Reparent(SceneDoc.GoDoc g, int newParentId)
    {
        if (g.Parent == newParentId || g.Id == newParentId || IsDescendantOf(newParentId, g.Id))
            return; // dongu olusturamaz
        // Prefab cocugu tasinamaz: parent/sira override modeli yok, Save'de sessizce kaybolurdu.
        if (g.IsPrefabChild)
        {
            EditorLog.Warning("[prefab] prefab cocugu tasinamaz (Unity gibi); instance kokunu tasiyin");
            return;
        }
        // Unity gibi: world transform korunur, lokal TRS yeni parent uzayina cevrilir.
        var (wp, wr, ws) = WorldTrs(g.Pos, g.Rot, g.Scale, g.Parent);
        var (np, nr, ns) = LocalTrs(wp, wr, ws, newParentId);
        History.Push(new ReparentOp
        {
            GoId = g.Id,
            OldParent = g.Parent,
            NewParent = newParentId,
            OldPos = g.Pos,
            OldRot = g.Rot,
            OldScale = g.Scale,
            NewPos = np,
            NewRot = nr,
            NewScale = ns,
        });
        g.Parent = newParentId;
        g.Pos = np;
        g.Rot = nr;
        g.Scale = ns;
        RefreshLive();
    }

    // Parent + kardes sirasi: g'yi newParent altina, insertBeforeId'li elemanin
    // ONUNE tasir (0 = listenin sonuna). Liste sirasi = cizim/hiyerarsi sirasi.
    public void MoveGameObject(SceneDoc.GoDoc g, int newParentId, int insertBeforeId)
    {
        if (g.Id == newParentId || g.Id == insertBeforeId || IsDescendantOf(newParentId, g.Id))
            return;
        // Prefab cocugu tasinamaz/siralanamaz: parent+sira diff'e girmiyor, kaydolmaz.
        if (g.IsPrefabChild)
        {
            EditorLog.Warning("[prefab] prefab cocugu tasinamaz/siralanamaz (Unity gibi); instance kokunu tasiyin");
            return;
        }
        int oldIndex = Doc.Objects.IndexOf(g);
        if (oldIndex < 0)
            return;
        int oldParent = g.Parent;
        Doc.Objects.RemoveAt(oldIndex); // index'ler cikartilmis liste uzerinden
        int newIndex = Doc.Objects.Count;
        if (insertBeforeId != 0)
        {
            for (int i = 0; i < Doc.Objects.Count; i++)
            {
                if (Doc.Objects[i].Id == insertBeforeId)
                {
                    newIndex = i;
                    break;
                }
            }
        }
        if (newIndex == oldIndex && oldParent == newParentId)
        {
            Doc.Objects.Insert(oldIndex, g); // degisiklik yok: geri koy, op uretme
            return;
        }
        // Unity gibi: world transform korunur — lokal TRS yeni parent uzayina cevrilir.
        // Ayni parent icinde reorder'da dokunulmaz (float drift olmasin).
        Vec3 newPos = g.Pos, newRot = g.Rot, newScale = g.Scale;
        if (oldParent != newParentId)
        {
            var (wp, wr, ws) = WorldTrs(g.Pos, g.Rot, g.Scale, oldParent);
            (newPos, newRot, newScale) = LocalTrs(wp, wr, ws, newParentId);
        }
        History.Push(new MoveGoOp
        {
            GoId = g.Id,
            OldParent = oldParent,
            NewParent = newParentId,
            OldIndex = oldIndex,
            NewIndex = newIndex,
            OldPos = g.Pos,
            OldRot = g.Rot,
            OldScale = g.Scale,
            NewPos = newPos,
            NewRot = newRot,
            NewScale = newScale,
        });
        g.Parent = newParentId;
        g.Pos = newPos;
        g.Rot = newRot;
        g.Scale = newScale;
        Doc.Objects.Insert(newIndex, g);
        RefreshLive();
    }

    // Doc zinciri uzerinden 2D TRS compose (motorla ayni kural: rot yalniz z anlamli,
    // scale bilesen bazli — rotasyonlu non-uniform parent'ta yaklasik, Unity gibi).
    (Vec3 pos, Vec3 rot, Vec3 scale) WorldTrs(Vec3 pos, Vec3 rot, Vec3 scale, int parentId)
    {
        int cur = parentId, guard = 0;
        while (cur != 0 && guard++ < 256)
        {
            var p = FindGo(cur);
            if (p == null)
                break;
            float rad = p.Rot.z * (MathF.PI / 180f);
            float c = MathF.Cos(rad), s = MathF.Sin(rad);
            float sx = p.Scale.x * pos.x, sy = p.Scale.y * pos.y;
            pos = new Vec3(p.Pos.x + c * sx - s * sy,
                           p.Pos.y + s * sx + c * sy,
                           p.Pos.z + p.Scale.z * pos.z);
            rot = new Vec3(rot.x + p.Rot.x, rot.y + p.Rot.y, rot.z + p.Rot.z);
            scale = new Vec3(scale.x * p.Scale.x, scale.y * p.Scale.y, scale.z * p.Scale.z);
            cur = p.Parent;
        }
        return (pos, rot, scale);
    }

    (Vec3 pos, Vec3 rot, Vec3 scale) LocalTrs(Vec3 wpos, Vec3 wrot, Vec3 wscale, int parentId)
    {
        if (parentId == 0)
            return (wpos, wrot, wscale);
        var pg = FindGo(parentId);
        if (pg == null)
            return (wpos, wrot, wscale);
        var (pp, pr, ps) = WorldTrs(pg.Pos, pg.Rot, pg.Scale, pg.Parent);
        float rad = -pr.z * (MathF.PI / 180f);
        float c = MathF.Cos(rad), s = MathF.Sin(rad);
        float dx = wpos.x - pp.x, dy = wpos.y - pp.y;
        static float Div(float a, float b) => b != 0f ? a / b : a;
        var pos = new Vec3(Div(c * dx - s * dy, ps.x), Div(s * dx + c * dy, ps.y), Div(wpos.z - pp.z, ps.z));
        var rot = new Vec3(wrot.x - pr.x, wrot.y - pr.y, wrot.z - pr.z);
        var scale = new Vec3(Div(wscale.x, ps.x), Div(wscale.y, ps.y), Div(wscale.z, ps.z));
        return (pos, rot, scale);
    }

    internal void ApplyMoveOp(int goId, int parent, int index, Vec3 pos, Vec3 rot, Vec3 scale)
    {
        var g = FindGo(goId);
        if (g == null)
            return;
        Doc.Objects.Remove(g);
        g.Parent = parent;
        g.Pos = pos;
        g.Rot = rot;
        g.Scale = scale;
        Doc.Objects.Insert(Math.Clamp(index, 0, Doc.Objects.Count), g);
        RefreshLive();
    }

    public void SetName(SceneDoc.GoDoc g, string name)
    {
        if (string.IsNullOrEmpty(name) || g.Name == name)
            return;
        History.Push(new NameOp { GoId = g.Id, Old = g.Name, New = name });
        g.Name = name;
        var go = Live(g.Id);
        if (go != null)
            go.name = name;
    }

    internal void ApplyNameOp(int goId, string name)
    {
        var g = FindGo(goId);
        if (g == null)
            return;
        g.Name = name;
        var go = Live(goId);
        if (go != null)
            go.name = name;
    }

    bool IsDescendantOf(int id, int ancestorId)
    {
        var g = FindGo(id);
        while (g != null && g.Parent != 0)
        {
            if (g.Parent == ancestorId)
                return true;
            g = FindGo(g.Parent);
        }
        return false;
    }

    public SceneDoc.CompDoc AddComponent(SceneDoc.GoDoc g, string typeName)
    {
        var cd = new SceneDoc.CompDoc { Type = typeName }; // props bos = tip default'lari
        History.Push(new AddCompOp { GoId = g.Id, Comp = cd, Index = g.Components.Count });
        g.Components.Add(cd);
        RefreshLive();
        return cd;
    }

    public void RemoveComponent(SceneDoc.GoDoc g, SceneDoc.CompDoc cd)
    {
        History.Push(new RemoveCompOp { GoId = g.Id, Comp = cd, Index = g.Components.IndexOf(cd) });
        g.Components.Remove(cd);
        RefreshLive();
    }

    // Custom editor bogazi: CANLI component'in TUM alanlarini doc'a serilestirir
    // (zengin nesne modelini dogrudan duzenleyen editorler icin — timeline gibi)
    // + tek undo girisi (ardisik commit'ler coalesce olur, drag spam'lemez).
    public void CommitComponent(SceneDoc.GoDoc g, SceneDoc.CompDoc cd)
    {
        var entry = _catalog?.Find(cd.Type);
        var live = FindLiveComponent(g, cd);
        if (entry == null || live == null)
            return;
        var old = new List<KeyValuePair<string, DocNode>>(cd.Props);
        var ids = new Dictionary<GameObject, int>(); // canli GO -> docId (ref kodlama)
        foreach (var kv in _live)
            ids[kv.Value] = kv.Key;
        cd.Props.Clear();
        foreach (var f in entry.Schema)
            cd.Props.Add(new(f.Name, SerializedType.WriteField(live, f,
                t => SceneDoc.GoRefValue(t, ids),
                t => SceneDoc.CompRefValue(t, ids),
                _assets)));
        History.Push(new CompPropsOp
        {
            GoId = g.Id,
            CompIndex = g.Components.IndexOf(cd),
            Old = old,
            New = new List<KeyValuePair<string, DocNode>>(cd.Props),
        });
    }

    internal void ApplyCompProps(int goId, int compIndex, List<KeyValuePair<string, DocNode>> props)
    {
        var g = FindGo(goId);
        if (g == null || compIndex < 0 || compIndex >= g.Components.Count)
            return;
        var cd = g.Components[compIndex];
        cd.Props.Clear();
        cd.Props.AddRange(props);
        RefreshLive(); // doc disaridan degisti: canliyi doc'tan tazele
    }

    // Doc node yaz (yoksa ekle) + canliya patch. Ref alanlari (Go/CompRef) canliya
    // dogrudan patch'lenemez (localId haritasi ister) — tam projeksiyon yenilenir;
    // undo/redo da ayni bogazdan gectigi icin tutarli kalir.
    void WriteProp(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f, DocNode value)
    {
        // null = "prop serilestirilmemis" durumuna donus: node silinir, canli
        // projeksiyon yenilenir (component default'u devreye girer).
        if (value == null)
        {
            for (int i = cd.Props.Count - 1; i >= 0; i--)
                if (cd.Props[i].Key == f.Name || (f.FormerName != null && cd.Props[i].Key == f.FormerName))
                    cd.Props.RemoveAt(i);
            RefreshLive();
            return;
        }
        var node = value.Clone();
        bool replaced = false;
        for (int i = 0; i < cd.Props.Count; i++)
        {
            if (cd.Props[i].Key != f.Name && (f.FormerName == null || cd.Props[i].Key != f.FormerName))
                continue;
            cd.Props[i] = new(f.Name, node);
            replaced = true;
            break;
        }
        if (!replaced)
            cd.Props.Add(new(f.Name, node));
        if (f.Kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef
            or SerializedType.Kind.List or SerializedType.Kind.Object)
            RefreshLive();
        else
            ApplyField(g, cd, f, node);
    }

    public static DocNode FindProp(SceneDoc.CompDoc cd, SerializedType.FieldSchema f)
    {
        foreach (var kv in cd.Props)
            if (kv.Key == f.Name || (f.FormerName != null && kv.Key == f.FormerName))
                return kv.Value;
        return null;
    }

    // --- Undo/redo uygulayicilari (docId adresli — reload'a dayanikli) ---

    internal void ApplyPropOp(int goId, int compIndex, string prop, DocNode value)
    {
        var g = FindGo(goId);
        if (g == null || compIndex < 0 || compIndex >= g.Components.Count)
            return;
        var cd = g.Components[compIndex];
        var entry = _catalog?.Find(cd.Type);
        var f = entry != null ? SerializedType.Find(entry.Schema, prop) : null;
        if (f != null)
            WriteProp(g, cd, f, value);
    }

    internal void ApplyTransformOp(int goId, Vec3 pos, Vec3 rot, Vec3 scale)
    {
        var g = FindGo(goId);
        if (g == null)
            return;
        g.Pos = pos;
        g.Rot = rot;
        g.Scale = scale;
        ApplyTransform(g);
    }

    internal void ApplyActiveOp(int goId, bool value)
    {
        var g = FindGo(goId);
        if (g == null)
            return;
        g.Active = value;
        ApplyActive(g);
    }

    internal void ApplyLayerOp(int goId, int value)
    {
        var g = FindGo(goId);
        if (g == null)
            return;
        g.Layer = value;
        ApplyLayer(g);
    }

    internal void ApplyEnabledOp(int goId, int compIndex, bool value)
    {
        var g = FindGo(goId);
        if (g == null || compIndex < 0 || compIndex >= g.Components.Count)
            return;
        var cd = g.Components[compIndex];
        cd.Enabled = value;
        ApplyEnabled(g, cd);
    }

    public void ApplyEnabled(SceneDoc.GoDoc g, SceneDoc.CompDoc cd)
    {
        var c = FindLiveComponent(g, cd);
        if (c != null)
            c.enabled = cd.Enabled;
    }

    public void ApplyField(SceneDoc.GoDoc g, SceneDoc.CompDoc cd,
        SerializedType.FieldSchema f, DocNode node)
    {
        var c = FindLiveComponent(g, cd);
        if (c != null)
        {
            SerializedType.ReadField(c, f, node, _assets, NoopRef); // ref alanlari v1'de duzenlenmez
            c.OnValidate(); // alan dogrudan yazildi — component tepki versin (LayoutBox.MarkDirty)
        }
    }

    static void NoopRef(SerializedType.Kind kind, string val, System.Action<object> set)
    {
    }

    // Doc'taki component'in canli esleigi: ayni tipten kacinci oldugu uzerinden.
    Component FindLiveComponent(SceneDoc.GoDoc g, SceneDoc.CompDoc cd)
    {
        var go = Live(g.Id);
        var entry = _catalog?.Find(cd.Type);
        if (go == null || entry == null)
            return null;
        int want = 0;
        foreach (var other in g.Components)
        {
            if (other == cd)
                break;
            if (other.Type == cd.Type)
                want++;
        }
        int seen = 0;
        int n = go.ComponentCount;
        for (int i = 0; i < n; i++)
        {
            var c = go.ComponentAt(i);
            if (c.GetType() != entry.Type)
                continue;
            if (seen == want)
                return c;
            seen++;
        }
        return null;
    }
}
