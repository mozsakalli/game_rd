using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Release asset ciktisi: PlayerSettings "Scenes In Build" listesinden ERISILEN icerik
// tek game.pak dosyasina paketlenir (guid tablosu gomulu — release'te ScanMetas/.meta/
// dizin taramasi yok). Iki faz:
//   1) Erisilebilirlik: kok sahneler bake edilir, asset referanslari gezilir (prefab'lar
//      kendi referanslariyla gecisli); yutulan girdi (atlas uyesi) tuketicisini de ceker.
//      Tuketicinin kendi girdileri CEKILMEZ: atlas klasorundeki referanssiz png gelmez.
//   2) Veri: yalniz canli kume. Importer'li asset'ler ImportPipeline.BuildArtifacts ile
//      canli-filtreli uretilir (atlas sayfasina yalniz canli uyeler girer); artifact
//      kapsami (Runtime/Editor/Standalone) importer'dan gelir, builder format bilmez.
// Texture'lar build'de BIR KEZ cozulup DTEX (ham RGBA) yazilir: runtime'da png decode
// maliyeti sifir, giris-bazli zlib boyutu geri alir.
static class AssetPackBuilder
{
    // Paketlenmeyenler: meta (guid pak index'inde), kod (ayri derlenir; .dll managed plugin AOT'ye IL olarak girer), editor icerigi.
    static bool Skip(string rel)
        => rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/Editor/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("Editor/", StringComparison.OrdinalIgnoreCase)
        || AssetDatabase.IsPluginNativePath(rel);

    [MenuItem("Project/Build Asset Pack", 1)]
    internal static void BuildMenu() => Build();

    // Donus: pak yazildi ve dogrulandi. Hata/iptal loglanir (PlayerBuilder bunu kapi olarak kullanir).
    internal static bool Build() => Build(null, null);

    // outPath: null = <proje>/Build/game.pak. extra: pak'a eklenecek ham girdiler (modul kodu gibi; sikistirilir, bagimliliksiz).
    // TEMA: pak TEK temayla piser — ThemeOverride (RPC/CLI) yoksa PlayerSettings.activeTheme. Prefab cache calisilan temayla
    // dolu olabilir: build boyunca build temasiyla yeniden kurulur, sonunda editorun temasina geri doner.
    internal static string ThemeOverride;

    // Oyun cikti koku (docs/plugins.md "Games"): Build/<game>/ ; aotcompiler ayni kural.
    internal static string GameBuildDir(Project project) => project.BuildDir;

    internal static bool Build(string outPath, IReadOnlyList<(string Key, byte[] Data)> extra)
    {
        var assets = App.Assets;
        string prevTheme = SceneDoc.ActiveTheme;
        SceneDoc.ActiveTheme = ThemeOverride ?? App.Project.Player.activeTheme ?? "";
        assets.InvalidateAllPrefabs();
        try
        {
            if (!string.IsNullOrEmpty(SceneDoc.ActiveTheme))
                EditorLog.Info("[pak] theme: " + SceneDoc.ActiveTheme);
            return BuildCore(outPath, extra);
        }
        finally
        {
            SceneDoc.ActiveTheme = prevTheme;
            assets.InvalidateAllPrefabs();
        }
    }

    static bool BuildCore(string outPath, IReadOnlyList<(string Key, byte[] Data)> extra)
    {
        var assets = App.Assets;
        var catalog = App.Catalog;
        var project = App.Project;
        string root = assets.Root;
        outPath ??= Path.Combine(GameBuildDir(project), "game.pak");

        // Kokler: Scenes In Build (+ startScene). Eksik kok = build iptal (sessiz bos pak yok).
        var roots = project.Player.BuildScenes();
        if (roots.Count == 0)
        {
            EditorLog.Error("[pak] build'e dahil sahne yok: Project > Player Settings > Scenes In Build");
            return false;
        }
        bool rootsOk = true;
        foreach (var r in roots)
        {
            if (!assets.AllAssets.ContainsKey(r))
            {
                EditorLog.Error($"[pak] kok sahne bulunamadi: {r}");
                rootsOk = false;
            }
        }
        if (!rootsOk)
            return false;
        if (!project.Player.scenes.Contains(project.Player.startScene))
            EditorLog.Warning($"[pak] startScene listede degil, otomatik eklendi: {project.Player.startScene}");

        // Bagimlilik grafigi (pak index'ine yazilir): kim neyi yuklenmeden once ister.
        // Kenarlari build bilgisi olan yazar: importer artifact'leri, yutma (uye->tuketici),
        // sahne/prefab asset referanslari. Runtime tek genel yuruyucuyle gezer.
        var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void AddDep(string from, string to)
        {
            if (string.IsNullOrEmpty(to) || to == from) return;
            if (!deps.TryGetValue(from, out var l)) deps[from] = l = new List<string>();
            if (!l.Contains(to)) l.Add(to);
        }

        // ---- Faz 1: erisilebilirlik (worklist) ----
        // Canlilik ALT OGE duzeyinde: "guid#id" yalniz o parcayi, duz "guid" sahibin tamamini canli yapar. Sahne
        // koklerinden erisilmeyen hicbir sey (gelistirme copu dahil) pak'a girmez — importer'dan bagimsiz garanti:
        // predicate importer'a verilir, cikti asagida DOGRULANIR.
        var memberConsumers = new Dictionary<string, string>(StringComparer.Ordinal); // "sahip#id" -> atlas
        var absorbedBy = ImportPipeline.AbsorbedByMap(memberConsumers); // yutulan girdi -> tuketici
        var live = new HashSet<string>(StringComparer.Ordinal);        // sahip duzeyi (pak'a giren asset'ler)
        var liveWhole = new HashSet<string>(StringComparer.Ordinal);   // butunuyle referanslanan sahipler
        var liveSubs = new HashSet<string>(StringComparer.Ordinal);    // "sahip#id" referanslari
        var baked = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var queue = new Queue<string>();
        foreach (var r0 in roots) { liveWhole.Add(r0); queue.Enqueue(r0); }
        while (queue.Count > 0)
        {
            string key = queue.Dequeue();
            if (live.Contains(key))
                continue;
            if (!assets.AllAssets.ContainsKey(key))
            {
                unresolved.Add(key);
                continue;
            }
            if (Skip(key))
            {
                EditorLog.Warning($"[pak] referanslanan asset paketlenmeyen sinifta, atlandi: {key}");
                continue;
            }
            if (IsSceneLike(key))
            {
                var refs = new List<string>();
                var data = BakeScene(key, Path.Combine(root, key), catalog, assets, refs);
                if (data == null)
                    continue; // hata loglandi; sahne pak'a girmez
                baked[key] = data;
                EnqueueRefs(key, refs);
            }
            else if (IsDataAsset(key))
            {
                // [Asset] veri nesnesi (.asset, importer'siz): YAML -> ObjectBinary; referanslari da kenar.
                var refs = new List<string>();
                var data = BakeObject(key, Path.Combine(root, key), catalog, refs);
                if (data == null)
                    continue;
                baked[key] = data;
                EnqueueRefs(key, refs);
            }
            live.Add(key);
            if (absorbedBy.TryGetValue(key, out var consumer))
            {
                AddDep(key, consumer); // uye yuklenmeden once tuketici (sayfa) hazir olmali
                queue.Enqueue(consumer);
            }
            // Butunuyle canli container: tum alt ogelerinin tuketicileri (atlas) de canli.
            if (liveWhole.Contains(key))
                foreach (var e in SubAssets.IndexOf(key).Entries)
                    EnqueueSubConsumer(key, key + "#" + e.Id);
        }
        void EnqueueSubConsumer(string owner, string subKey)
        {
            if (memberConsumers.TryGetValue(subKey, out var atlas))
            {
                AddDep(owner, atlas);
                queue.Enqueue(atlas);
            }
        }
        void EnqueueRefs(string key, List<string> refs)
        {
            foreach (var r in refs)
            {
                string p = SubAssets.Split(assets.ResolvePath(r), out string subId); // guid#id -> sahip yolu + id
                if (!assets.AllAssets.ContainsKey(p))
                {
                    unresolved.Add($"{p} <- {key}");
                    continue;
                }
                if (subId == null)
                {
                    if (liveWhole.Add(p) && live.Contains(p)) // daha once yalniz parcayla canliydi: tuketicileri simdi ekle
                        foreach (var e in SubAssets.IndexOf(p).Entries)
                            EnqueueSubConsumer(p, p + "#" + e.Id);
                }
                else
                {
                    string sk = p + "#" + subId;
                    if (liveSubs.Add(sk))
                        EnqueueSubConsumer(p, sk);
                }
                AddDep(key, p);
                queue.Enqueue(p);
            }
        }
        foreach (var u in unresolved)
            EditorLog.Warning($"[pak] cozulemeyen referans: {u}");

        // Atlas kapsami: canli gruplarin uyesi olan canli alt ogeler sahibin kendi sayfasina GIRMEZ (cift piksel yok).
        var atlasCovered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rel in live)
        {
            if (!AtlasSystem.IsAtlasGroupAsset(Path.Combine(root, rel)))
                continue;
            foreach (var m in AtlasImporter.MemberKeys(rel))
                if (SubLive(m)) atlasCovered.Add(m);
        }
        bool SubLive(string subKey)
        {
            string owner = SubAssets.Split(subKey, out string id);
            if (!live.Contains(owner)) return false;
            return id == null || liveWhole.Contains(owner) || liveSubs.Contains(subKey);
        }
        if (liveSubs.Count > 0 || atlasCovered.Count > 0)
            EditorLog.Info($"[pak] alt oge canliligi: {liveSubs.Count} parca referansli, {atlasCovered.Count} atlas kapsaminda" +
                (liveSubs.Count <= 8 ? ": " + string.Join(", ", liveSubs) : ""));
        // Importer predicate'i: atlas uye alir (canli), diger importer'lar canli VE atlas'in kapsamadigi parcayi.
        Func<string, bool> LivenessFor(string rel)
        {
            bool isAtlas = AtlasSystem.IsAtlasGroupAsset(Path.Combine(root, rel));
            return k => SubLive(k) && (isAtlas || !atlasCovered.Contains(k));
        }

        // ---- Faz 2: veri (yalniz canli kume, deterministik sira) ----
        bool IsLive(string p) => live.Contains(p);
        var keys = new List<string>(live);
        keys.Sort(string.CompareOrdinal);

        int imgCount = 0, skipped = 0, bakedCount = 0;
        var items = new List<(string Key, string Guid, byte[] Data)>();
        string texKey = null;
        foreach (var key in keys)
        {
            string file = Path.Combine(root, key);
            bool absorbed = absorbedBy.ContainsKey(key);
            bool isImage = AssetDatabase.ImportTypeOf(key) == typeof(Sprite);
            byte[] data;
            if (ImportPipeline.ImporterFor(key) != null)
            {
                // Importer'li kaynak: pak'a ARTIFACT'ler girer, kaynak degil (ttf disarida
                // kalir). "main" asset anahtariyla, digerleri "<anahtar>#<ad>" ile. Kapsam:
                // Editor girmez; Standalone yalniz asset yutulmamissa (fontun kendi sheet'i).
                var liveness = LivenessFor(key);
                var arts = ImportPipeline.BuildArtifacts(key, liveness);
                if (arts == null)
                {
                    EditorLog.Warning($"[pak] import basarisiz, atlandi: {key}");
                    continue;
                }
                // GARANTI: importer canli olmayan alt ogeyi pakete sokamaz. Sprite tablolari denetlenir; ihlal = build iptal.
                if (!VerifyLiveness(key, arts, liveness, out string violation))
                {
                    EditorLog.Error($"[pak] {key}: importer canli olmayan parca uretti: {violation} — build iptal (importer ctx.IsLive'a uymali)");
                    return false;
                }
                data = null;
                foreach (var (name, bytes, scope) in arts)
                {
                    if (scope == ArtifactScope.Editor)
                        continue;
                    if (scope == ArtifactScope.Standalone && absorbed)
                    {
                        skipped++;
                        continue;
                    }
                    if (name == "main")
                    {
                        data = bytes;
                        continue;
                    }
                    items.Add((key + "#" + name, "", bytes));
                    AddDep(key, key + "#" + name);
                    // Sprite tablosu aktivasyonu sayfa dokularini SENKRON okur: sayfalar tablodan once insin.
                    if (SpriteTable.IsTable(bytes) && ObjectBinary.Read(bytes, catalog, null) is SpriteTable st)
                        foreach (var pg in st.Pages)
                            AddDep(key + "#" + name, key + "#" + pg.Artifact);
                }
                // Atlas main'i de tablo: sayfalar main'den once (AddDep(key, key#page) zaten var).
                if (data == null)
                {
                    // Pikseller/bolgeler tuketicinin sayfasinda ya da main yok: guid->yol eslemesi icin bos stub
                    // girisi kalir (sahne "guid" / "guid#sub" referanslar; ResolvePath sahibi buradan cozer).
                    if (!absorbed)
                        EditorLog.Warning($"[pak] 'main' artifact yok, yalniz guid stub'i yazildi: {key}");
                    items.Add((key, assets.PathToGuid(key), Array.Empty<byte>()));
                    continue;
                }
                if (isImage)
                {
                    imgCount++;
                    texKey ??= key;
                }
            }
            else if (IsSceneLike(key) || IsDataAsset(key))
            {
                // Faz 1'de pisti: YAML -> SceneBinary (prefab'lar ACILMIS, override'lar
                // uygulanmis, alanlar sema indeksiyle). Runtime YAML/DocNode gormez.
                data = baked[key];
                bakedCount++;
            }
            else
                data = File.ReadAllBytes(file);
            items.Add((key, assets.PathToGuid(key), data));
        }

        // Proje ayarlari: runtime project.yaml okumaz; pismis kayit pak'ta (sahne listesi dahil).
        items.Add((ProjectBinary.PakKey, "", ProjectBinary.Write(project.Name, project.StartScene, roots, project.Game)));
        // Plugin ayarlari (docs/plugins.md): her Plugin<TSettings> / [ProjectSetting] tipi icin ProjectSettings/Plugins/<Ad>.asset -> DOBJ.
        var pluginSettings = PluginSettingsEditor.CollectForPak(project, assets);
        if (pluginSettings.Count > 0)
            items.Add((Plugin.SettingsPakKey, "", Plugin.WriteSettingsBlob(pluginSettings, assets)));
        if (extra != null)
            foreach (var (key, data) in extra)
                items.Add((key, "", data));

        var (rawTotal, pakSize) = PakWriter.Write(outPath, items,
            // Stream tipli ses: native player dosyadan offset'le okur -> zlib OLAMAZ.
            key => AssetDatabase.ImportTypeOf(key) == typeof(AudioClip) && AudioImporter.IsStream(key),
            key => deps.TryGetValue(key, out var l) ? l : null);

        int candidates = 0;
        foreach (var rel in assets.AllAssets.Keys)
            if (!Skip(rel))
                candidates++;
        EditorLog.Info($"[pak] {roots.Count} kok sahne -> {live.Count}/{candidates} asset canli ({candidates - live.Count} strip), " +
            $"{items.Count} giris ({imgCount} gorsel, {bakedCount} baked sahne/prefab, {skipped} yutulmus) -> {outPath} " +
            $"({pakSize / 1024.0:0.0} KB, acik {rawTotal / 1024.0:0.0} KB, %{100.0 * pakSize / Math.Max(1, rawTotal):0.0})");
        LocWarnings(live);

        return Verify(outPath, items, texKey);
    }

    // Ceviri denetimi (uyari, build'i durdurmaz): pak'a giren her tablo icin eksik ceviri sayisi + glyph kapsami.
    static void LocWarnings(HashSet<string> live)
    {
        foreach (var scope in live)
        {
            if (!scope.EndsWith(Localize.TableFile, StringComparison.OrdinalIgnoreCase))
                continue;
            var t = LocTableStore.Get(scope);
            if (t == null) continue;
            for (int i = 1; i < t.Languages.Count; i++)
            {
                int n = LocAudit.Missing(scope, t.Languages[i]).Count;
                if (n > 0)
                    EditorLog.Warning($"[loc] {scope}: '{t.Languages[i]}' dilinde {n} eksik ceviri (loc.missing)");
            }
            foreach (var gap in LocAudit.GlyphCheck(scope))
                EditorLog.Warning($"[loc] {scope}: {gap.Font} fontunda {gap.Codepoints.Count} karakter yok: {LocAudit.CodepointsText(gap.Codepoints, 20)} (keys: {gap.Keys.Count})");
        }
    }

    // Build garantisi: importer ciktisindaki sprite tablolari (Runtime/Standalone) yalniz CANLI alt oge/uye icerebilir.
    // Importer'dan bagimsiz: builder kokten gezerek buldugu canliligi tabloya karsi dogrular.
    static bool VerifyLiveness(string key, List<(string Name, byte[] Data, ArtifactScope Scope)> arts,
        Func<string, bool> liveness, out string violation)
    {
        violation = null;
        foreach (var (name, bytes, scope) in arts)
        {
            if (scope == ArtifactScope.Editor || !SpriteTable.IsTable(bytes))
                continue;
            if (ObjectBinary.Read(bytes, App.Catalog, null) is not SpriteTable table)
                continue;
            foreach (var r in table.Regions)
            {
                if (liveness(r.Name))
                    continue;
                violation = $"{name}: {r.Name}";
                return false;
            }
        }
        return true;
    }

    static bool IsSceneLike(string key)
        => key.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

    // Importer'siz .asset = [Asset] veri nesnesi (importer'li .asset'ler, AtlasGroup gibi, artifact yolundan gider).
    static bool IsDataAsset(string key)
        => key.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && ImportPipeline.ImporterFor(key) == null;

    static byte[] BakeObject(string key, string file, TypeCatalog catalog, List<string> assetRefs)
    {
        try
        {
            var root = Yaml.Parse(File.ReadAllText(file));
            string typeName = root.GetScalar("type", null);
            if (typeName == null || catalog.Find(typeName) is not { IsAsset: true })
            {
                EditorLog.Warning($"[pak] .asset tipi katalogda yok, atlandi: {key} ({typeName})");
                return null;
            }
            return ObjectBinary.Bake(typeName, root.Get("fields") ?? root, catalog, m => EditorLog.Warning($"{m} ({key})"), assetRefs);
        }
        catch (Exception e)
        {
            EditorLog.Error($"[pak] asset bake basarisiz, atlandi: {key}: {e.Message}");
            return null;
        }
    }

    static byte[] BakeScene(string key, string file, TypeCatalog catalog, AssetDatabase assets, List<string> assetRefs)
    {
        try
        {
            var doc = SceneDoc.Parse(File.ReadAllText(file), key);
            doc.Prepare(catalog, assets, SceneDoc.ActiveTheme); // delta -> tam agac (ic ice) + build temasi (tek tema pak'a girer)
            // Kapsam ceviri tablosu varsa sahnenin bagimliligi (pak'a girer, Spawn'dan once hazir olur).
            if (doc.LocTable != null && assets.Exists(doc.LocTable) && !assetRefs.Contains(doc.LocTable))
                assetRefs.Add(doc.LocTable);
            // Font kayit defteri: host + kapsam tablosu sahnenin bagimliligi (listeledigi fontlar pak'a girer,
            // component'ler fonta dogrudan referans vermez).
            foreach (var fk in new[] { Fonts.TableFile, Fonts.TableKeyFor(doc.LocTable) })
                if (assets.Exists(fk) && !assetRefs.Contains(fk))
                    assetRefs.Add(fk);
            return SceneBinary.Bake(doc, catalog, m => EditorLog.Warning($"{m} ({key})"), assetRefs);
        }
        catch (Exception e)
        {
            EditorLog.Error($"[pak] bake basarisiz, atlandi: {key}: {e.Message}");
            return null;
        }
    }

    // Kaynak texture'i native worker'la cozup DTEX blobu uretir (build-time,
    // senkron bekleme kabul). Basarisiz olursa null (cagiran ham dosyaya duser).
    static byte[] AwaitPixels(int job, out int w, out int h) => EditorPixels.Await(job, out w, out h);

    // Yazilan pak'i geri okuyup dogrular: index sayisi, tum girisler bayt-esit (zlib round-trip dahil),
    // ilk gorselin pak yolundan decode'u editor artifact'inin decode'uyla BIREBIR ayni mi.
    static bool Verify(string pakPath, List<(string Key, string Guid, byte[] Data)> items, string texKey)
    {
        try
        {
            var pak = new PakSource(pakPath);
            if (pak.Count != items.Count)
            {
                EditorLog.Error($"[pak] verify FAIL: index {pak.Count} != {items.Count}");
                return false;
            }
            foreach (var it in items)
            {
                var a = pak.ReadBytes(it.Key);
                if (a == null || !a.AsSpan().SequenceEqual(it.Data))
                {
                    EditorLog.Error($"[pak] verify FAIL: bayt farki {it.Key}");
                    return false;
                }
            }

            if (texKey != null)
            {
                var pakPx = AwaitPixels(pak.StartLoad(texKey), out int pw, out int ph);
                var srcPx = AwaitPixels(Sokol.AssetLoad(ImportPipeline.ArtifactPath(texKey, "main")), out int sw, out int sh);
                if (pakPx == null || srcPx == null || pw != sw || ph != sh
                    || !pakPx.AsSpan().SequenceEqual(srcPx))
                {
                    EditorLog.Error($"[pak] verify FAIL: piksel farki {texKey} (pak {pw}x{ph}, artifact {sw}x{sh})");
                    return false;
                }
                EditorLog.Info($"[pak] verify OK: {items.Count} bayt-esit, {texKey} pikselleri artifact ile BIREBIR ({pw}x{ph})");
            }
            else
                EditorLog.Info($"[pak] verify OK: {items.Count} bayt-esit (texture yok, decode atlandi)");
            return true;
        }
        catch (Exception e)
        {
            EditorLog.Error("[pak] verify FAIL: " + e.Message);
            return false;
        }
    }
}
