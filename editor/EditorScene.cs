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

    // Prefab stage: doc tek koklu kalmali (kok silinemez, koke yeni GO/reparent kok altina duser).
    public bool SingleRoot { get; init; }

    SceneDoc.GoDoc RootGo()
    {
        foreach (var g in Doc.Objects)
            if (g.Parent == 0)
                return g;
        return null;
    }

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
        Doc.LocTable = Localize.TableKeyFor(AssetKeyOf(path, assets));
        Doc.ExpandPrefabs(catalog, assets); // delta kayitlari tam agaca acilir
        Instantiate(catalog, assets);
        _savedVersion = History.Version;
    }

    // Mutlak sahne/prefab yolu -> Assets'e goreli anahtar (kapsam tablosu bundan turer); Assets disindaysa null.
    static string AssetKeyOf(string path, AssetDatabase assets)
    {
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(assets?.Root))
            return null;
        string rel = System.IO.Path.GetRelativePath(assets.Root, path).Replace('\\', '/');
        return rel.StartsWith("..") || System.IO.Path.IsPathRooted(rel) ? null : rel;
    }

    // Sahnenin kapsam tablosu anahtari (Assets'e goreli; null = kapsam yok).
    public string LocTableKey => Doc?.LocTable;

    // Kaydedilmemis duzenleme var mi (disk cakisma politikasi: bellek kazanir).
    public bool Dirty => History.Version != _savedVersion;
    int _savedVersion;

    // Kaydedilmemis duzenlemeler "atildi" sayilir (stage kapanisi gibi, doc zaten yikilacak).
    public void DiscardChanges() => _savedVersion = History.Version;

    // Prefab dosyasi DISKTE degisti: doc deltaya sikistirilip taze prefab'la yeniden
    // acilir. prefabIds kalici oldugundan id/secim/undo bozulmaz; kaydedilmemis
    // override'lar delta olarak yasamaya devam eder.
    // SIRA ONEMLI: Collapse ESKI prefab cache'iyle yapilmali (yoksa eski degerler yeni
    // prefab'a gore 'override' sanilir); cache ancak Collapse'tan sonra dusurulur.
    public void ReexpandPrefabs() => ReexpandFrom(Collapse());

    public string Collapse() => _catalog == null ? null : Doc.ToYaml(_catalog, _assets);

    public void ReexpandFrom(string collapsedYaml)
    {
        if (_catalog == null || collapsedYaml == null)
            return;
        string locTable = Doc?.LocTable;
        Doc = SceneDoc.Parse(collapsedYaml);
        Doc.LocTable = locTable;
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
        fresh.Assets = assets;
        fresh.ExternallyDriven = true; // UpdateAll atlar; EditorApp elle surer (simulate: false)
        fresh.EditMode = true; // oyun mantigi (ExecuteInEditMode'suz) projeksiyonda uyur
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
        FlushPendingLoc();
        AssetWatcher.NoteSelfWrite(Path);
        File.WriteAllText(Path, Doc.ToYaml(_catalog, _assets)); // prefab'lar deltaya sikisir
        _savedVersion = History.Version;
        LocTableStore.Flush(); // sahneyle birlikte ceviri tablosu da diske
        // Stage'de prefab cache'i BURADA dusurulmez: stash'li sahne eski prefab'la acik;
        // cache cikista collapse'tan sonra duser (App.ExitPrefabStageNow).
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

    // Kullanici duzenlemesi kancasi (record modu): (goId, compIndex | -1 = Transform, yol).
    // Yol: alan adi ("Width", ic ice icin ust alan "Fill"), Transform icin "Position"/
    // "Rotation"/"Scale"/"Active", component icin "Enabled". Undo/redo tetiklemez.
    public static Action<int, int, string, bool> LiveEdited; // son arg: false=duzenleme ONCESI, true=SONRASI

    public void SetProp(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f,
        string oldValue, string newValue)
        => SetProp(g, cd, f,
            oldValue == null ? null : DocNode.Scal(oldValue),
            newValue == null ? null : DocNode.Scal(newValue));

    public void SetProp(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f,
        DocNode oldValue, DocNode newValue)
    {
        int ci = g.Components.IndexOf(cd);
        // Bagli (cevrilen) alan: metin doc'a DEGIL tablonun calisilan-dil hucresine gider (RPC prop.set dahil tek yol).
        if (f.Kind == SerializedType.Kind.String && LocKeyOf(cd, f.Name) is { Length: > 0 } locKey && Doc.LocTable != null)
        {
            string lang = LocTableStore.EffectiveLanguage(Doc.LocTable);
            string old = LocTableStore.GetValue(Doc.LocTable, locKey, lang) ?? "";
            string next = newValue?.Scalar ?? "";
            if (old == next)
                return;
            History.Push(new LocEditOp { GoId = g.Id, CompIndex = ci, Prop = f.Name, Scope = Doc.LocTable, Key = locKey, Lang = lang, OldValue = old, NewValue = next });
            LocTableStore.SetValue(Doc.LocTable, locKey, lang, next);
            return;
        }
        LiveEdited?.Invoke(g.Id, ci, f.Name, false);
        History.Push(new PropOp
        {
            GoId = g.Id,
            CompIndex = ci,
            Prop = f.Name,
            OldValue = oldValue?.Clone(),
            NewValue = newValue?.Clone(),
        });
        WriteProp(g, cd, f, newValue);
        LiveEdited?.Invoke(g.Id, ci, f.Name, true);
        if (f.Kind == SerializedType.Kind.String)
            AutoBindLoc(g, cd, f, oldValue?.Scalar, newValue?.Scalar);
    }

    public void SetTransform(SceneDoc.GoDoc g, Vec3 pos, Vec3 rot, Vec3 scale)
    {
        // Driven pozisyon (orn. layout'un yerlestirdigi kutu) editorden degistirilemez
        // (Unity driven-property): pos kismi sessizce eski degerde tutulur, rot/scale serbest.
        var live = Live(g.Id);
        if (live != null
            && (TransformDriver.Driven(live, out _) & DrivenTransformProperties.Position) != 0)
            pos = g.Pos;
        bool posCh = !Eq(g.Pos, pos), rotCh = !Eq(g.Rot, rot), scaleCh = !Eq(g.Scale, scale);
        if (LiveEdited != null)
        {
            if (posCh) LiveEdited(g.Id, -1, "Position", false);
            if (rotCh) LiveEdited(g.Id, -1, "Rotation", false);
            if (scaleCh) LiveEdited(g.Id, -1, "Scale", false);
        }
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
        if (LiveEdited != null)
        {
            if (posCh) LiveEdited(g.Id, -1, "Position", true);
            if (rotCh) LiveEdited(g.Id, -1, "Rotation", true);
            if (scaleCh) LiveEdited(g.Id, -1, "Scale", true);
        }
    }

    static bool Eq(in Vec3 a, in Vec3 b) => a.x == b.x && a.y == b.y && a.z == b.z;

    public void SetActive(SceneDoc.GoDoc g, bool value)
    {
        LiveEdited?.Invoke(g.Id, -1, "Active", false);
        History.Push(new ActiveOp { GoId = g.Id, Old = g.Active, New = value });
        g.Active = value;
        ApplyActive(g);
        LiveEdited?.Invoke(g.Id, -1, "Active", true);
    }

    public void SetLayer(SceneDoc.GoDoc g, int value)
    {
        History.Push(new LayerOp { GoId = g.Id, Old = g.Layer, New = value });
        g.Layer = value;
        ApplyLayer(g);
    }

    public void SetEnabled(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, bool value)
    {
        LiveEdited?.Invoke(g.Id, g.Components.IndexOf(cd), "Enabled", false);
        History.Push(new EnabledOp
        {
            GoId = g.Id,
            CompIndex = g.Components.IndexOf(cd),
            Old = cd.Enabled,
            New = value,
        });
        cd.Enabled = value;
        ApplyEnabled(g, cd);
        LiveEdited?.Invoke(g.Id, g.Components.IndexOf(cd), "Enabled", true);
    }

    public void DoUndo() => History.Undo(this);
    public void DoRedo() => History.Redo(this);

    // --- Toplu mutasyon (edit.batch / scene.apply): tek undo grubu + canli projeksiyon
    // batch sonunda BIR kez tazelenir (yapisal her op'ta full reload yerine). Hata →
    // RollbackBatch: doc batch oncesine doner, journal'da iz kalmaz. ---

    int _refreshSuspend;
    bool _refreshPending;

    public void BeginBatch()
    {
        History.BeginGroup();
        _refreshSuspend++;
    }

    public void EndBatch()
    {
        History.EndGroup();
        ResumeRefresh();
    }

    public void RollbackBatch()
    {
        History.RollbackGroup(this);
        ResumeRefresh();
    }

    void ResumeRefresh()
    {
        if (_refreshSuspend > 0 && --_refreshSuspend == 0 && _refreshPending)
        {
            _refreshPending = false;
            RefreshLive();
        }
    }

    // --- Yapisal mutasyonlar: doc + full reload (canli cerrahi yok — reload ucuz ve tek yol) ---

    public void RefreshLive()
    {
        if (_refreshSuspend > 0)
        {
            _refreshPending = true;
            return;
        }
        Instantiate(_catalog, _assets);
    }

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

    // insertBeforeId: yeni GO bu id'li kardesin ONUNE girer (0 = listenin sonu).
    // Paralel RPC cagrilarinda varis sirasi belirsiz oldugundan siranin acikca verilebilmesi gerekir.
    public SceneDoc.GoDoc AddGameObject(int parentId, int insertBeforeId = 0)
    {
        if (SingleRoot && parentId == 0 && RootGo() is { } root)
            parentId = root.Id;
        var g = new SceneDoc.GoDoc { Id = NextId(), Parent = parentId, Name = UniqueName("GameObject") };
        int index = Doc.Objects.Count;
        if (insertBeforeId != 0)
        {
            for (int i = 0; i < Doc.Objects.Count; i++)
            {
                if (Doc.Objects[i].Id == insertBeforeId)
                {
                    index = i;
                    break;
                }
            }
        }
        History.Push(new AddGoOp { Doc = g, Index = index });
        Doc.Objects.Insert(index, g);
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
        if (SingleRoot && g.Parent == 0)
        {
            EditorLog.Warning("[prefab] prefab root cannot be deleted");
            return;
        }
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
        if (SingleRoot && newParentId == 0)
        {
            EditorLog.Warning("[prefab] prefab has a single root; keep objects under it");
            return;
        }
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
        if (SingleRoot && newParentId == 0 && g.Parent != 0)
        {
            EditorLog.Warning("[prefab] prefab has a single root; keep objects under it");
            return;
        }
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

    // --- Ceviri baglari ("<alan>@loc" prop'u; docs/engine-notes.md LOCALIZATION) ---

    // null = prop yok (varsayilan davranis), "" = bilerek kapali, aksi halde key.
    public static string LocKeyOf(SceneDoc.CompDoc cd, string field)
    {
        string prop = field + Localize.PropSuffix;
        foreach (var kv in cd.Props)
            if (kv.Key == prop)
                return kv.Value?.Scalar ?? "";
        return null;
    }

    // Kayitli mutasyon (undo'lu): bagi kur/kapat/degistir. createdKey: bu islemle acilan tablo satiri (undo siler).
    // Metin prop'u: baglaninca doc'tan DUSER (tek gercek tablo), kapaninca calisilan-dil hucresi prop'a GERI yazilir
    // (metin kaybolmaz). Her iki metin op'ta tasinir (undo/redo simetrik).
    public void SetLocKey(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, string field, string key,
        string createdKey = null, List<string> createdValues = null)
    {
        int ci = g.Components.IndexOf(cd);
        string oldKey = LocKeyOf(cd, field);
        string oldText = PropScalar(cd, field);
        string newText = null;
        if (string.IsNullOrEmpty(key))
            newText = (!string.IsNullOrEmpty(oldKey) ? LocTableStore.GetValue(Doc.LocTable, oldKey, LocTableStore.EffectiveLanguage(Doc.LocTable)) : null) ?? oldText;
        var op = new LocKeyOp
        {
            GoId = g.Id, CompIndex = ci, Field = field,
            OldKey = oldKey, NewKey = key, OldText = oldText, NewText = newText,
            Scope = Doc.LocTable, CreatedKey = createdKey, CreatedValues = createdValues,
        };
        History.Push(op);
        ApplyLocKeyOp(g.Id, ci, field, key, newText);
    }

    static string PropScalar(SceneDoc.CompDoc cd, string field)
    {
        foreach (var kv in cd.Props)
            if (kv.Key == field)
                return kv.Value?.Scalar;
        return null;
    }

    // text: bagsiz durumda alanin doc metni (null = prop yok); bagli durumda yoksayilir (prop dusurulur).
    internal void ApplyLocKeyOp(int goId, int compIndex, string field, string key, string text)
    {
        var g = FindGo(goId);
        if (g == null || compIndex < 0 || compIndex >= g.Components.Count)
            return;
        var cd = g.Components[compIndex];
        string prop = field + Localize.PropSuffix;
        for (int i = cd.Props.Count - 1; i >= 0; i--)
            if (cd.Props[i].Key == prop || cd.Props[i].Key == field)
                cd.Props.RemoveAt(i);
        if (key != null)
            cd.Props.Add(new(prop, DocNode.Scal(key)));
        if (string.IsNullOrEmpty(key) && text != null)
            cd.Props.Add(new(field, DocNode.Scal(text)));
        RebindLive(g, cd, field, key);
    }

    // Canli component'in bagini doc'a gore tazeler: key -> Bind (gecerli dil yazilir), yok/bos -> Unbind + doc metni.
    void RebindLive(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, string field, string key)
    {
        var c = FindLiveComponent(g, cd);
        var entry = _catalog?.Find(cd.Type);
        var f = entry != null ? SerializedType.Find(entry.Schema, field) : null;
        if (c == null || f == null)
            return;
        if (!string.IsNullOrEmpty(key))
            Localize.Bind(c, f, Localize.Table(Doc.LocTable, _assets), key, validate: true);
        else
        {
            Localize.UnbindField(c, field);
            ApplyField(g, cd, f, FindProp(cd, f) ?? DocNode.Scal(""));
        }
    }

    // Acik doc'taki tum "@loc" prop'larinda key'i degistirir (rename); canli baglar tazelenir.
    public void RenameLocKey(string oldKey, string newKey)
    {
        if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey) || oldKey == newKey)
            return;
        foreach (var g in Doc.Objects)
            foreach (var cd in g.Components)
                for (int i = 0; i < cd.Props.Count; i++)
                {
                    var kv = cd.Props[i];
                    var field = Localize.LocPropField(kv.Key);
                    if (field == null || kv.Value?.Scalar != oldKey)
                        continue;
                    cd.Props[i] = new(kv.Key, DocNode.Scal(newKey));
                    RebindLive(g, cd, field, newKey);
                }
    }

    // Yeni bag: ayni metin tabloda varsa o key'e; yoksa baglamdan key (sahne.go[.alan]) + satir (calisilan dil = metin).
    // Undo: tek adim (satir + bag). Grup icinde cagrilirsa (RPC batch) dis gruba katilir.
    public void BindLocNew(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f, string text)
    {
        string scope = Doc.LocTable;
        if (scope == null)
            return;
        string lang = LocTableStore.EffectiveLanguage(scope);
        var table = LocTableStore.Get(scope, create: true);
        if (table == null)
            return;
        string existing = LocTableStore.FindByText(table, lang, text);
        if (existing != null)
        {
            SetLocKey(g, cd, f.Name, existing);
            return;
        }
        bool suffix = false;
        foreach (var kv in cd.Props)
            if (Localize.LocPropField(kv.Key) is { } other && other != f.Name && !string.IsNullOrEmpty(kv.Value?.Scalar))
                suffix = true;
        string sceneName = Path != null ? System.IO.Path.GetFileNameWithoutExtension(Path) : "";
        string key = LocTableStore.NewKey(table, sceneName, g.Name, f.Name, suffix, text);
        var values = new List<string>();
        for (int i = 0; i < table.Languages.Count; i++)
            values.Add(string.Equals(table.Languages[i], lang, StringComparison.OrdinalIgnoreCase) ? text : "");
        bool own = !History.InGroup;
        if (own) History.BeginGroup();
        LocTableStore.AddRow(scope, key, values);
        SetLocKey(g, cd, f.Name, key, createdKey: key, createdValues: values);
        if (own) History.EndGroup();
    }

    // Baska satira gec (oneri secimi); eski satir acik doc'ta kullanilmiyorsa ve en fazla tek dilde doluysa
    // (az once otomatik acilmis) tablodan dusurulur.
    public void RebindLocExisting(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f,
        string oldKey, string newKey)
    {
        string scope = Doc.LocTable;
        bool own = !History.InGroup;
        if (own) History.BeginGroup();
        SetLocKey(g, cd, f.Name, newKey);
        var e = LocTableStore.Get(scope)?.Entry(oldKey);
        if (e != null && CountLocUsages(oldKey) == 0)
        {
            int filled = 0;
            foreach (var v in e.Values) if (!string.IsNullOrEmpty(v)) filled++;
            if (filled <= 1)
            {
                History.Push(new LocRowOp { Scope = scope, Key = oldKey, Values = new List<string>(e.Values), Removed = true });
                LocTableStore.RemoveRow(scope, oldKey);
            }
        }
        if (own) History.EndGroup();
    }

    // Varsayilan-cevrilen alan (LayoutBox.text, TextSprite.Text): bos -> dolu ilk gecisinde otomatik bag.
    // Prop "" (bilerek kapali) ya da zaten bagli ise dokunmaz. SetProp'tan cagrilir (inspector + RPC ayni yol).
    // Inspector harf harf yazar: key metinden turetilebildigi icin bag BEKLETILIR (TickLoc: klavye odagi
    // birakilinca ya da 1 sn hareketsizlik) — ilk harften "login.p" gibi key dogmasin.
    void AutoBindLoc(SceneDoc.GoDoc g, SceneDoc.CompDoc cd, SerializedType.FieldSchema f, string oldText, string newText)
    {
        if (f.Kind != SerializedType.Kind.String || Doc.LocTable == null)
            return;
        if (string.IsNullOrEmpty(newText))
            return;
        if (LocKeyOf(cd, f.Name) != null || !LocTableStore.IsDefaultLocField(cd.Type, f.Name))
            return;
        int ci = g.Components.IndexOf(cd);
        if (_pendingLoc.GoId != 0 && (_pendingLoc.GoId != g.Id || _pendingLoc.Ci != ci || _pendingLoc.Field != f.Name))
            FlushPendingLoc();
        if (!string.IsNullOrEmpty(oldText) && _pendingLoc.GoId == 0)
            return; // dolu bir metni duzenliyor (bag bekleyen yok): eski literal, kullanici 🌐 ile acar
        _pendingLoc = (g.Id, ci, f.Name);
        _pendingAt = DigitoyEngine.GLFW.GetTime();
    }

    (int GoId, int Ci, string Field) _pendingLoc;
    double _pendingAt;

    public void TickLoc(double now, bool idle)
    {
        if (_pendingLoc.GoId != 0 && (idle || now - _pendingAt > 1.0))
            FlushPendingLoc();
    }

    public void FlushPendingLoc()
    {
        var p = _pendingLoc;
        _pendingLoc = default;
        if (p.GoId == 0)
            return;
        var g = FindGo(p.GoId);
        if (g == null || p.Ci < 0 || p.Ci >= g.Components.Count)
            return;
        var cd = g.Components[p.Ci];
        var entry = _catalog?.Find(cd.Type);
        var f = entry != null ? SerializedType.Find(entry.Schema, p.Field) : null;
        if (f == null || LocKeyOf(cd, p.Field) != null)
            return;
        string text = FindProp(cd, f)?.Scalar;
        if (!string.IsNullOrEmpty(text))
            BindLocNew(g, cd, f, text);
    }

    // Acik doc'ta key'i kullanan (go, comp, field) sayisi.
    public int CountLocUsages(string key)
    {
        int n = 0;
        foreach (var g in Doc.Objects)
            foreach (var cd in g.Components)
                foreach (var kv in cd.Props)
                    if (Localize.LocPropField(kv.Key) != null && kv.Value?.Scalar == key)
                        n++;
        return n;
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
    public Component FindLiveComponent(SceneDoc.GoDoc g, SceneDoc.CompDoc cd)
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
