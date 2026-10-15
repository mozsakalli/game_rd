#if DE_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Tema katmani smoke testleri (docs/engine-notes.md TEMA): dosya round-trip (pasif katman korunur), ApplyTheme
// (override/removed/added + eklenen prefab instance), nested (Avatar xmas → Table → sahne), DiffTheme ⇄ Apply simetrisi,
// EditorScene tema modu (edit → Collapse katman; taban dokunulmaz; sira degisimi reddi; dirty korunur), bake farki +
// pasif tema asset'i referanslarda yok, yetim denetimi. Gecici klasorde gercek AssetDatabase.
public static class ThemeTests
{
    static int _pass, _fail;

    [AddComponentMenu("")]
    public sealed class ThemeTestComp : Component
    {
        public float Size;
        public string Label = "";
        public Sprite Icon;
    }

    const string C = nameof(ThemeTestComp);

    public static void Run(TypeCatalog catalog)
    {
        _pass = _fail = 0;
        string root = Path.Combine(Path.GetTempPath(), "de_themetests_" + Guid.NewGuid().ToString("N"), "Assets");
        Directory.CreateDirectory(root);
        string prevTheme = SceneDoc.ActiveTheme;
        var prevActive = Scene.Active;
        var prevWarn = AssetDatabase.LogWarning;
        AssetDatabase.LogWarning = _ => { };
        try { RunAll(catalog, root); }
        catch (Exception e) { Check(false, "HATA: " + e); }
        finally
        {
            SceneDoc.ActiveTheme = prevTheme;
            AssetDatabase.LogWarning = prevWarn;
            Scene.SetActive(prevActive);
            try { Directory.Delete(Path.GetDirectoryName(root), recursive: true); } catch { }
        }
        Console.WriteLine($"[theme] {_pass} PASS, {_fail} FAIL");
    }

    static void RunAll(TypeCatalog catalog, string root)
    {
        SceneDoc.ActiveTheme = "";
        // Avatar.prefab: xmas katmani Frame Label=snow + Icon=xmas_frame.png (pasif 'kurban' katmani da var).
        File.WriteAllBytes(Path.Combine(root, "frame.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(root, "xmas_frame.png"), new byte[] { 2 });
        var assets = new AssetDatabase(root);
        assets.ScanMetas(createMissing: true);
        assets.Catalog = catalog;
        string frameGuid = assets.PathToGuid("frame.png"), xmasGuid = assets.PathToGuid("xmas_frame.png");
        File.WriteAllText(Path.Combine(root, "Avatar.prefab"), string.Join("\n",
            "scene: Avatar",
            "themes:",
            "  xmas:",
            "    overrides:",
            "      - node: 2", "        comp: " + C + ":0", "        prop: Label", "        value: snow",
            "      - node: 2", "        comp: " + C + ":0", "        prop: Icon", "        value: " + xmasGuid,
            "  kurban:",
            "    overrides:",
            "      - node: 2", "        comp: " + C + ":0", "        prop: Label", "        value: moon",
            "objects:",
            "  - id: 1", "    parent: 0", "    name: Avatar",
            "    components:", "      - type: " + C, "        Size: 10",
            "  - id: 2", "    parent: 1", "    name: Frame",
            "    components:", "      - type: " + C, "        Size: 1", "        Label: frame", "        Icon: " + frameGuid,
            ""));
        assets.ScanMetas(createMissing: true);
        string avatarGuid = assets.PathToGuid("Avatar.prefab");
        File.WriteAllText(Path.Combine(root, "Table.prefab"), string.Join("\n",
            "scene: Table",
            "objects:",
            "  - id: 1", "    parent: 0", "    name: Table",
            "  - id: 2", "    parent: 1", "    name: Avatar", "    prefab: " + avatarGuid, "    prefabIds: 1:2 2:3",
            "  - id: 4", "    parent: 1", "    name: Avatar (1)", "    prefab: " + avatarGuid, "    prefabIds: 1:4 2:5",
            ""));
        assets.ScanMetas(createMissing: true);
        string tableGuid = assets.PathToGuid("Table.prefab");

        // --- T1: round-trip, pasif katman korunur; tema yokken Prepare = ExpandPrefabs ---
        string avText = File.ReadAllText(Path.Combine(root, "Avatar.prefab"));
        var av = SceneDoc.Parse(avText);
        Check(av.Themes.Count == 2 && av.Themes.ContainsKey("xmas") && av.Themes.ContainsKey("kurban"), "themes parse");
        string avOut = av.ToYaml(catalog, assets);
        Check(SceneDoc.Parse(avOut).ToYaml(catalog, assets) == avOut && avOut.Contains("kurban") && avOut.Contains("value: moon"),
            "themes yazim round-trip (pasif katman aynen)");
        av.Prepare(catalog, assets, "");
        Check(av.AppliedTheme == "" && Label(av, 2) == "frame", "taban: tema etkisi yok");
        var av2 = SceneDoc.Parse(avText);
        av2.Prepare(catalog, assets, "xmas");
        Check(av2.AppliedTheme == "xmas" && Label(av2, 2) == "snow" && Prop(av2, 2, "Icon") == xmasGuid && Prop(av2, 1, "Size") == "10",
            "ApplyTheme override");
        var av3 = SceneDoc.Parse(avText);
        av3.Prepare(catalog, assets, "nosuch");
        Check(av3.AppliedTheme == "nosuch" && Label(av3, 2) == "frame", "bilinmeyen tema = taban, cokme yok");

        // --- T3: nested — Avatar'in xmas katmani Table uzerinden sahneye ---
        string sceneText = string.Join("\n",
            "scene: Main",
            "themes:",
            "  xmas:",
            "    overrides:",
            "      - node: 10", "        prop: name", "        value: Xmas Table",
            "      - node: 10", "        prop: layer", "        value: 7",
            "    removed: 20",
            "    added:",
            "      - id: 30", "        parent: 10", "        name: Garland",
            "        components:", "          - type: " + C, "            Size: 3",
            "      - id: 31", "        parent: 0", "        name: Santa", "        prefab: " + avatarGuid, "        prefabIds: 1:31 2:32",
            "        overrides:", "          - node: 2", "            comp: " + C + ":0", "            prop: Size", "            value: 77",
            "objects:",
            "  - id: 10", "    parent: 0", "    name: Table", "    prefab: " + tableGuid, "    prefabIds: 1:10 2:11 3:12 4:13 5:14",
            "  - id: 20", "    parent: 0", "    name: Summer",
            "  - id: 21", "    parent: 20", "    name: Sun",
            "");
        var sc = SceneDoc.Parse(sceneText);
        sc.Prepare(catalog, assets, "");
        Check(sc.Objects.Count == 7 && Label(sc, 12) == "frame" && Label(sc, 14) == "frame" && Find(sc, 10).Name == "Table", "sahne taban");

        SceneDoc.ActiveTheme = "xmas";
        assets.InvalidateAllPrefabs();
        var sx = SceneDoc.Parse(sceneText);
        sx.Prepare(catalog, assets, "xmas");
        Check(Label(sx, 12) == "snow" && Label(sx, 14) == "snow", "nested: Avatar xmas katmani Table uzerinden sahnede");
        Check(Find(sx, 10).Name == "Xmas Table" && Find(sx, 10).Layer == 7, "sahne katmani: name + layer override");
        Check(Find(sx, 20) == null && Find(sx, 21) == null, "removed alt agac");
        Check(Find(sx, 30) != null && Prop(sx, 30, "Size") == "3", "added duz GO");
        Check(Find(sx, 31)?.IsPrefabRoot == true && Find(sx, 32) != null && Prop(sx, 32, "Size") == "77" && Label(sx, 32) == "snow",
            "added prefab instance: expand + instance override + Avatar tema katmani");
        Check(sx.Objects.Count == 7 - 2 + 1 + 2, "node sayisi (7 - removed 2 + garland + santa 2)");

        // --- T6: bake farki + pasif tema asset'i referanslarda yok ---
        var refsBase = new List<string>();
        var refsX = new List<string>();
        var bBase = SceneBinary.Bake(sc, catalog, null, refsBase);
        var bX = SceneBinary.Bake(sx, catalog, null, refsX);
        Check(!bBase.AsSpan().SequenceEqual(bX), "bake: tema farkli bayt");
        Check(refsBase.Contains(frameGuid) && !refsBase.Contains(xmasGuid), "taban pak: xmas asset'i referanslarda yok");
        Check(refsX.Contains(xmasGuid), "xmas pak: xmas asset'i referanslarda");

        // --- T4: DiffTheme ⇄ Apply simetrisi (taban da AYNI prefab cache'iyle: prefab'in kendi tema katkisi bu dosyanin
        // katmani DEGILDIR — Base bunu icermeli ki diff yalniz bu dosyanin farkini yazsin) ---
        var scX = SceneDoc.Parse(sceneText);
        scX.Prepare(catalog, assets, "");
        Check(Label(scX, 12) == "snow", "taban projeksiyonu nested tema katkisini icerir (xmas cache)");
        var layer = sx.DiffTheme(scX, catalog, assets);
        Check(!Has(layer, "12", "Label", null), "diff: prefab'tan gelen tema farki sahne katmanina yazilmaz");
        var sc2 = SceneDoc.Parse(sceneText);
        sc2.Themes["xmas"] = layer;
        sc2.Prepare(catalog, assets, "xmas");
        sc2.Themes.Clear();
        sx.Themes.Clear();
        Check(sc2.ToYaml() == sx.ToYaml(), "DiffTheme → Apply = ayni projeksiyon");
        Check(layer.GetScalar("removed", "") == "20" && (layer.Get("added")?.Items.Count ?? 0) == 2, "diff: removed en ust node, added 2 kayit");
        var sameLayer = scX.DiffTheme(scX, catalog, assets);
        Check(sameLayer.Fields.Count == 0, "ayni doc diff'i bos");

        // --- T7: yetim denetimi ---
        var orphanDoc = SceneDoc.Parse(sceneText.Replace("- node: 10\n        prop: layer", "- node: 99\n        prop: layer"));
        orphanDoc.Prepare(catalog, assets, "");
        var orphans = orphanDoc.ThemeOrphans("xmas");
        Check(orphans.Count == 1 && orphans[0].Contains("99"), "ThemeOrphans");

        // --- T5: EditorScene tema modu ---
        string scenePath = Path.Combine(root, "Main.scene");
        File.WriteAllText(scenePath, sceneText);
        SceneDoc.ActiveTheme = "xmas";
        assets.InvalidateAllPrefabs();
        var es = new EditorScene();
        es.Load(scenePath, catalog, assets);
        Check(es.ThemeMode && es.Theme == "xmas" && es.Base.Objects.Count == 7 && es.Doc.Objects.Count == 8, "EditorScene tema modu: Base/Doc");
        Check(es.BaseNodeOf(es.FindGo(10)) != null && es.BaseNodeOf(es.FindGo(30)) == null, "BaseNodeOf");
        var fSize = SerializedType.Find(catalog.Find(C).Schema, "Size");
        var g12 = es.FindGo(12);
        es.SetProp(g12, g12.Components[0], fSize, EditorScene.FindProp(g12.Components[0], fSize)?.Scalar, "55");
        var newGo = es.AddGameObject(10);
        es.SetName(newGo, "Snow");
        es.RemoveGameObject(es.FindGo(30)); // tema-only GO silinir: katmandan duser
        int idx10 = es.Doc.Objects.IndexOf(es.FindGo(10));
        es.MoveGameObject(es.FindGo(10), 0, 0); // tabanda var olan GO'nun sirasi: ret
        Check(es.Doc.Objects.IndexOf(es.FindGo(10)) == idx10, "tema modunda sira degisimi reddi");
        es.Reparent(newGo, 0);
        Check(newGo.Parent == 0, "tema modunda reparent serbest");
        Check(es.Dirty, "dirty");
        string collapsed = es.Collapse();
        var cdoc = SceneDoc.Parse(collapsed);
        Check(cdoc.Themes.Count == 1 && cdoc.Themes.ContainsKey("xmas"), "collapse: tek katman");
        string baseOut = SceneDoc.Parse(sceneText).ToYaml();
        var cBase = SceneDoc.Parse(collapsed);
        cBase.Themes.Clear();
        var oBase = SceneDoc.Parse(sceneText);
        oBase.Themes.Clear();
        Check(cBase.ToYaml() == oBase.ToYaml(), "collapse: taban nesneleri dokunulmadi");
        var lx = cdoc.Themes["xmas"];
        Check(Has(lx, "12", "Size", "55") && Has(lx, "10", "name", "Xmas Table") && !Has(lx, "30", null, null)
            && lx.GetScalar("removed", "") == "20" && (lx.Get("added")?.Items.Count ?? 0) == 2, "collapse: katman = eski + yeni degisiklikler, silinen tema-only GO yok");
        es.ResetHistoryKeepDirty();
        Check(es.Dirty && es.History.UndoCount == 0, "ResetHistoryKeepDirty");
        es.UnloadLive();

        // Tabana donus: ayni metin, tema "" → taban degerleri; tema modu kapali.
        SceneDoc.ActiveTheme = "";
        assets.InvalidateAllPrefabs();
        File.WriteAllText(scenePath, collapsed);
        var es2 = new EditorScene();
        es2.Load(scenePath, catalog, assets);
        Check(!es2.ThemeMode && Prop(es2.Doc, 12, "Size") == "1" && es2.FindGo(10).Name == "Table" && es2.FindGo(20) != null, "default'a donus: taban");
        Check(es2.Collapse() == collapsed, "taban modunda Save pasif katmani aynen tasir");
        es2.UnloadLive();
    }

    static bool Has(DocNode layer, string node, string prop, string value)
    {
        var ovs = layer.Get("overrides");
        if (ovs?.Items == null) return false;
        foreach (var o in ovs.Items)
            if (o.GetScalar("node") == node && (prop == null || (o.GetScalar("prop") == prop && (value == null || o.Get("value")?.Scalar == value))))
                return true;
        return false;
    }

    static SceneDoc.GoDoc Find(SceneDoc d, int id) => d.FindGo(id);
    static string Label(SceneDoc d, int id) => Prop(d, id, "Label");

    static string Prop(SceneDoc d, int id, string prop)
    {
        var g = d.FindGo(id);
        if (g == null || g.Components.Count == 0) return null;
        foreach (var kv in g.Components[0].Props) if (kv.Key == prop) return kv.Value?.Scalar;
        return null;
    }

    static void Check(bool cond, string name)
    {
        if (cond) _pass++;
        else { _fail++; Console.WriteLine($"[theme] FAIL: {name}"); }
    }
}
#endif
