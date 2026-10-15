#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Prefab bilesimi (nested) smoke testleri (docs/engine-notes.md NESTED PREFAB): Avatar → Table → sahne
// iki seviyeli expand + ref remap, sifir sahte override, nested + sahne override katmanlari, yayilim
// (invalidate+reexpand), removed/addComp/removeComp iki seviyede, dongu reddi, Apply yolu (en dis prefab'a
// nested override), bake/spawn paritesi, instance iceren alt agactan prefab uretimi + undo, stage dongu reddi.
// Gecici proje klasorunde gercek AssetDatabase ile kosar.
public static class PrefabTests
{
    static int _pass, _fail;
    static readonly List<string> _warnings = new();

    [AddComponentMenu("")]
    public sealed class PrefabTestComp : Component
    {
        public float Size;
        public string Label = "";
        public GameObject Buddy;
    }

    const string C = nameof(PrefabTestComp);

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        string root = Path.Combine(Path.GetTempPath(), "de_prefabtests_" + Guid.NewGuid().ToString("N"), "Assets");
        Directory.CreateDirectory(root);
        var prevWarn = AssetDatabase.LogWarning;
        _warnings.Clear();
        AssetDatabase.LogWarning = m => _warnings.Add(m);
        var prevActive = Scene.Active;
        try
        {
            RunAll(catalog, root);
        }
        catch (Exception e)
        {
            Check(false, "HATA: " + e);
        }
        finally
        {
            AssetDatabase.LogWarning = prevWarn;
            Scene.SetActive(prevActive);
            try { Directory.Delete(Path.GetDirectoryName(root), recursive: true); } catch { }
        }
        Console.WriteLine($"[prefab] {_pass} PASS, {_fail} FAIL");
    }

    static void RunAll(TypeCatalog catalog, string root)
    {
        // --- Dosyalar: Avatar (3 node, ic ref Buddy=Frame), Table (2 Avatar instance + Deck ref'i avatar-1 Frame'e) ---
        File.WriteAllText(Path.Combine(root, "Avatar.prefab"), string.Join("\n",
            "scene: Avatar",
            "objects:",
            "  - id: 1", "    parent: 0", "    name: Avatar",
            "    components:", "      - type: " + C, "        Size: 10", "        Label: base", "        Buddy: 2",
            "  - id: 2", "    parent: 1", "    name: Frame",
            "    components:", "      - type: " + C, "        Size: 1", "        Label: frame",
            "  - id: 3", "    parent: 1", "    name: Badge",
            "    components:", "      - type: " + C, "        Size: 2",
            ""));
        var assets = new AssetDatabase(root);
        assets.ScanMetas(createMissing: true);
        assets.Catalog = catalog;
        string avatarGuid = assets.PathToGuid("Avatar.prefab");
        Check(avatarGuid != null, "avatar guid");

        // Table: avatar-1 (ids 2..4) Frame Label override "snow"; avatar-2 (ids 5..7) Badge removed; Deck(8).Buddy = 3.
        File.WriteAllText(Path.Combine(root, "Table.prefab"), string.Join("\n",
            "scene: Table",
            "objects:",
            "  - id: 1", "    parent: 0", "    name: Table",
            "  - id: 2", "    parent: 1", "    name: Avatar", "    prefab: " + avatarGuid, "    prefabIds: 1:2 2:3 3:4",
            "    overrides:", "      - node: 2", "        comp: " + C + ":0", "        prop: Label", "        value: snow",
            "  - id: 5", "    parent: 1", "    name: Avatar (1)", "    prefab: " + avatarGuid, "    prefabIds: 1:5 2:6 3:7",
            "    removed: 3",
            "  - id: 8", "    parent: 1", "    name: Deck",
            "    components:", "      - type: " + C, "        Buddy: 3",
            ""));
        assets.ScanMetas(createMissing: true);
        string tableGuid = assets.PathToGuid("Table.prefab");

        // --- T3: LoadPrefab(Table).Doc expanded gelir ---
        var tableDoc = assets.LoadPrefab(tableGuid)?.Doc;
        Check(tableDoc != null && tableDoc.Objects.Count == 7, "Table doc expanded (1+3+2+1 node)");
        Check(tableDoc != null && Find(tableDoc, 2)?.IsPrefabRoot == true && Find(tableDoc, 3)?.IsPrefabChild == true
            && Find(tableDoc, 3).Name == "Frame", "Table doc ic instance isaretleri");
        Check(assets.LoadPrefab(tableGuid).Deps.Contains("Avatar.prefab"), "Deps gecisli");

        // --- Sahne: Table instance (ids 10..16) + ikinci avatar Frame (Table-local 6) Size=99 ---
        string sceneYaml = string.Join("\n",
            "scene: Main",
            "objects:",
            "  - id: 10", "    parent: 0", "    name: Table", "    prefab: " + tableGuid,
            "    prefabIds: 1:10 2:11 3:12 4:13 5:14 6:15 8:16",
            "    overrides:", "      - node: 6", "        comp: " + C + ":0", "        prop: Size", "        value: 99",
            "");
        var doc = SceneDoc.Parse(sceneYaml);
        doc.ExpandPrefabs(catalog, assets);

        // --- T1: iki seviye expand + remap ---
        Check(doc.Objects.Count == 7, "sahne 7 node (badge-2 removed)");
        Check(Scalar(Find(doc, 12), "Label") == "snow" && Scalar(Find(doc, 12), "Size") == "1", "nested override (Table→Avatar) sahnede");
        Check(Scalar(Find(doc, 15), "Size") == "99" && Scalar(Find(doc, 15), "Label") == "frame", "sahne override nested node'da");
        Check(Scalar(Find(doc, 16), "Buddy") == "12", "Deck ref iki seviye remap (3→12)");
        Check(Scalar(Find(doc, 11), "Buddy") == "12" && Scalar(Find(doc, 14), "Buddy") == "15", "avatar ic ref'leri sahne id'sine");
        Check(Find(doc, 13) != null && Find(doc, 13).Name == "Badge" && FindByName(doc, "Badge", 14) == null, "removed iki seviyede");

        // --- T2: sifir sahte override + round-trip bayt-es ---
        var ovs = doc.BuildOverrides(Find(doc, 10), catalog, assets);
        Check(ovs != null && ovs.Items.Count == 1 && ovs.Items[0].GetScalar("prop") == "Size", "yalniz gercek override (sahte yok)");
        string y1 = doc.ToYaml(catalog, assets);
        var doc2 = SceneDoc.Parse(y1);
        doc2.ExpandPrefabs(catalog, assets);
        Check(doc2.ToYaml(catalog, assets) == y1 && doc2.Objects.Count == 7, "collapse→expand→collapse bayt-es");

        // --- T6: addComp / removeComp sahne katmaninda nested node'a ---
        string sceneYaml2 = sceneYaml.Replace("        value: 99\n",
            "        value: 99\n" +
            "      - node: 3\n        prop: addComp\n        value:\n          type: " + C + "\n          Size: 7\n" +
            "      - node: 4\n        comp: " + C + ":0\n        prop: removeComp\n        value: true\n");
        var doc3 = SceneDoc.Parse(sceneYaml2);
        doc3.ExpandPrefabs(catalog, assets);
        Check(Find(doc3, 12).Components.Count == 2 && Find(doc3, 13).Components.Count == 0, "addComp/removeComp nested node'da");
        var ovs3 = doc3.BuildOverrides(Find(doc3, 10), catalog, assets);
        Check(ovs3.Items.Count == 3, "3 override korunur (Size, addComp, removeComp)");

        // --- T8: bake paritesi ---
        var s = Scene.Create("prefab-test");
        s.Catalog = catalog;
        s.Assets = assets;
        Scene.SetActive(s);
        try
        {
            SceneBinary.Spawn(SceneBinary.Bake(doc, catalog), null, catalog, assets);
            var all = new List<GameObject>();
            for (int i = 0; i < s.RootCount; i++) CollectGo(s.GetRoot(i), all);
            Check(all.Count == 7, "baked spawn 7 GO");
            GameObject deck = all.Find(g => g.name == "Deck"), frame1 = null;
            foreach (var g in all) if (g.name == "Frame" && g.GetComponent<PrefabTestComp>()?.Label == "snow") frame1 = g;
            Check(deck != null && frame1 != null && deck.GetComponent<PrefabTestComp>().Buddy == frame1, "baked ref cozumu (Deck→Frame-1)");
        }
        catch (Exception e) { Check(false, "bake HATA: " + e.Message); }
        finally { Scene.Unload(s); }

        // --- T4: Avatar degisti → collapse(eski) → invalidate all → expand: yayilim nested uzerinden ---
        string collapsed = doc.ToYaml(catalog, assets);
        File.WriteAllText(Path.Combine(root, "Avatar.prefab"), File.ReadAllText(Path.Combine(root, "Avatar.prefab"))
            .Replace("        Size: 1\n", "        Size: 5\n")
            + "  - id: 4\n    parent: 1\n    name: Glow\n");
        assets.InvalidateAllPrefabs();
        var doc4 = SceneDoc.Parse(collapsed);
        doc4.ExpandPrefabs(catalog, assets);
        Check(Scalar(Find(doc4, 12), "Size") == "5" && Scalar(Find(doc4, 12), "Label") == "snow", "yayilim: yeni deger + nested override duruyor");
        Check(Scalar(Find(doc4, 15), "Size") == "99", "yayilim: sahne override'i korunur");
        Check(CountName(doc4, "Glow") == 2 && doc4.Objects.Count == 9, "yeni Avatar cocugu iki instance'a da geldi");
        Check(doc4.BuildOverrides(Find(doc4, 10), catalog, assets).Items.Count == 1, "yayilim sonrasi sahte override yok");

        // --- T5: dongu ---
        File.WriteAllText(Path.Combine(root, "C1.prefab"), "scene: C1\nobjects:\n  - id: 1\n    parent: 0\n    name: C1\n");
        File.WriteAllText(Path.Combine(root, "C2.prefab"), "scene: C2\nobjects:\n  - id: 1\n    parent: 0\n    name: C2\n");
        assets.ScanMetas(createMissing: true);
        string c1 = assets.PathToGuid("C1.prefab"), c2 = assets.PathToGuid("C2.prefab");
        File.WriteAllText(Path.Combine(root, "C1.prefab"), "scene: C1\nobjects:\n  - id: 1\n    parent: 0\n    name: C1\n  - id: 2\n    parent: 1\n    name: x\n    prefab: " + c2 + "\n");
        File.WriteAllText(Path.Combine(root, "C2.prefab"), "scene: C2\nobjects:\n  - id: 1\n    parent: 0\n    name: C2\n  - id: 2\n    parent: 1\n    name: y\n    prefab: " + c1 + "\n");
        assets.InvalidateAllPrefabs();
        int wBefore = _warnings.Count;
        var pc1 = assets.LoadPrefab(c1);
        Check(pc1 != null && _warnings.Count == wBefore + 1 && _warnings[wBefore].Contains("dongu"), "dongu: uyari + yukleme biter");
        Check(pc1 != null && pc1.Deps.Contains("C2.prefab") && assets.PrefabDependsOn(c1, c2) && assets.PrefabDependsOn(c1, c1)
            && !assets.PrefabDependsOn(tableGuid, c1), "PrefabDependsOn");
        assets.InvalidateAllPrefabs();

        // --- T7: Apply (EditorScene): nested node'dan en dis prefab'a; diger Table instance'ina yayilir ---
        string scenePath = Path.Combine(root, "Main.scene");
        File.WriteAllText(scenePath, collapsed.TrimEnd('\n') + "\n  - id: 20\n    parent: 0\n    name: Table (1)\n    prefab: " + tableGuid + "\n");
        var es = new EditorScene();
        es.Load(scenePath, catalog, assets);
        var g15 = es.FindGo(15);
        var cd15 = g15.Components[0];
        var fSize = SerializedType.Find(catalog.Find(C).Schema, "Size");
        es.ApplyFieldToPrefab(g15, cd15, fSize);
        string tableText = File.ReadAllText(Path.Combine(root, "Table.prefab"));
        Check(tableText.Contains("prop: Size") && tableText.Contains("value: 99") && tableText.Contains("prefab: " + avatarGuid)
            && !tableText.Contains("name: Frame"), "Apply: Table.prefab'ta nested override satiri, collapse'li (duzlesme yok)");
        Check(Scalar(es.FindGo(15), "Size") == "99" && es.Doc.BuildOverrides(es.FindGo(10), catalog, assets).Items.Count == 0,
            "Apply: uygulanan instance'in override'i dustu");
        var t2 = es.FindGo(20);
        var t2frame2 = es.FindGo(t2.PrefabIds[6]);
        Check(t2frame2 != null && Scalar(t2frame2, "Size") == "99", "Apply: ikinci Table instance'ina nested yoldan yayildi");
        es.UnloadLive();

        // --- T9: instance iceren alt agactan prefab: Seat{Avatar(Label=vip)} ---
        string seatScene = Path.Combine(root, "Seat.scene");
        File.WriteAllText(seatScene, string.Join("\n",
            "scene: Seat",
            "objects:",
            "  - id: 30", "    parent: 0", "    name: Seat",
            "    components:", "      - type: " + C, "        Buddy: 32",
            "  - id: 31", "    parent: 30", "    name: Avatar", "    prefab: " + avatarGuid, "    prefabIds: 1:31 2:32 3:33 4:34",
            "    overrides:", "      - node: 2", "        comp: " + C + ":0", "        prop: Label", "        value: vip",
            ""));
        var es2 = new EditorScene();
        es2.Load(seatScene, catalog, assets);
        var seat = es2.FindGo(30);
        string seatPath = Path.Combine(root, "Seat.prefab");
        Check(es2.CreatePrefabAsset(seat, seatPath), "CreatePrefabAsset (ic instance var)");
        string seatText = File.ReadAllText(seatPath);
        Check(seatText.Contains("prefab: " + avatarGuid) && seatText.Contains("value: vip") && !seatText.Contains("name: Frame")
            && seatText.Contains("Buddy: 3"), "Seat.prefab: ic kayit delta + override + ref yerel uzayda");
        Check(es2.FindGo(31).PrefabGuid == null && es2.FindGo(31).PrefabRootId == 30 && es2.FindGo(32).PrefabRootId == 30
            && seat.IsPrefabRoot, "connect: ic kok duz cocuk (outermost)");
        Check(es2.Doc.BuildOverrides(seat, catalog, assets).Items.Count == 0, "connect sonrasi sifir override");
        es2.History.Undo(es2);
        Check(es2.FindGo(31).PrefabGuid == avatarGuid && es2.FindGo(31).PrefabIds[2] == 32 && es2.FindGo(32).PrefabRootId == 31
            && !seat.IsPrefabRoot, "connect undo ic isaretleri geri koyar");
        es2.History.Redo(es2);
        Check(es2.FindGo(31).PrefabGuid == null && seat.IsPrefabRoot, "connect redo");
        var seatDoc = assets.LoadPrefab(assets.PathToGuid("Seat.prefab")).Doc;
        Check(seatDoc.Objects.Count == 5 && Scalar(FindByName(seatDoc, "Frame", 0), "Label") == "vip", "Seat.prefab expanded: nested override");
        es2.UnloadLive();

        // --- T10: stage'de dongu reddi ---
        var stage = new EditorScene { SingleRoot = true };
        stage.Load(Path.Combine(root, "Avatar.prefab"), catalog, assets);
        int n = stage.Doc.Objects.Count;
        Check(stage.InstantiatePrefab(avatarGuid, 1) == null && stage.InstantiatePrefab(tableGuid, 1) == null
            && stage.Doc.Objects.Count == n, "stage: kendini / kendini icereni sokma reddi");
        Check(stage.InstantiatePrefab(c1, 1) != null, "stage: bagimsiz prefab girer");
        stage.UnloadLive();
    }

    static SceneDoc.GoDoc Find(SceneDoc d, int id)
    {
        foreach (var g in d.Objects) if (g.Id == id) return g;
        return null;
    }

    static SceneDoc.GoDoc FindByName(SceneDoc d, string name, int parent)
    {
        foreach (var g in d.Objects) if (g.Name == name && (parent == 0 || g.Parent == parent)) return g;
        return null;
    }

    static int CountName(SceneDoc d, string name)
    {
        int n = 0;
        foreach (var g in d.Objects) if (g.Name == name) n++;
        return n;
    }

    static string Scalar(SceneDoc.GoDoc g, string prop)
    {
        if (g == null || g.Components.Count == 0) return null;
        foreach (var kv in g.Components[0].Props) if (kv.Key == prop) return kv.Value?.Scalar;
        return null;
    }

    static void CollectGo(GameObject go, List<GameObject> into)
    {
        into.Add(go);
        for (int i = 0; i < go.transform.childCount; i++)
            CollectGo(go.transform.GetChild(i).gameObject, into);
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[prefab] FAIL: {name}"); }
    }
}
#endif
