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
        || rel.StartsWith("Editor/", StringComparison.OrdinalIgnoreCase);

    [MenuItem("Project/Build Asset Pack", 1)]
    internal static void BuildMenu() => Build();

    // Donus: pak yazildi ve dogrulandi. Hata/iptal loglanir (PlayerBuilder bunu kapi olarak kullanir).
    internal static bool Build() => Build(null, null);

    // outPath: null = <proje>/Build/game.pak. extra: pak'a eklenecek ham girdiler (modul kodu gibi; sikistirilir, bagimliliksiz).
    internal static bool Build(string outPath, IReadOnlyList<(string Key, byte[] Data)> extra)
    {
        var assets = App.Assets;
        var catalog = App.Catalog;
        var project = App.Project;
        string root = assets.Root;
        outPath ??= Path.Combine(project.Root, "Build", "game.pak");

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
        var absorbedBy = ImportPipeline.AbsorbedByMap(); // yutulan girdi -> tuketici
        var live = new HashSet<string>(StringComparer.Ordinal);
        var baked = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var unresolved = new List<string>();
        var queue = new Queue<string>(roots);
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
        }
        void EnqueueRefs(string key, List<string> refs)
        {
            foreach (var r in refs)
            {
                string p = assets.ResolvePath(r); // guid -> yol (yol ise aynen)
                int hash = p.IndexOf('#');
                if (hash > 0)
                    p = p.Substring(0, hash); // alt bolge ("x.ttf#U+0041"): sahibi canli olsun yeter
                if (!assets.AllAssets.ContainsKey(p))
                {
                    unresolved.Add($"{p} <- {key}");
                    continue;
                }
                AddDep(key, p);
                queue.Enqueue(p);
            }
        }
        foreach (var u in unresolved)
            EditorLog.Warning($"[pak] cozulemeyen referans: {u}");

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
                var arts = ImportPipeline.BuildArtifacts(key, IsLive);
                if (arts == null)
                {
                    EditorLog.Warning($"[pak] import basarisiz, atlandi: {key}");
                    continue;
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
                    if (isImage && absorbed)
                    {
                        // Pikseller tuketicinin sayfasinda; guid->yol eslemesi icin bos stub girisi
                        // kalir (sahne guid'le referanslar, LoadSprite bolge tablosundan baglar).
                        items.Add((key, assets.PathToGuid(key), Array.Empty<byte>()));
                        continue;
                    }
                    EditorLog.Warning($"[pak] 'main' artifact yok, atlandi: {key}");
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
        items.Add((ProjectBinary.PakKey, "", ProjectBinary.Write(project.Name, project.StartScene, roots)));
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

        return Verify(outPath, items, texKey);
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
            var doc = SceneDoc.Parse(File.ReadAllText(file));
            doc.ExpandPrefabs(catalog, assets); // delta kayitlari -> tam agac (ic ice dahil)
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
