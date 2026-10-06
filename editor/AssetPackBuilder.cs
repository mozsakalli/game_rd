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
    // Paketlenmeyenler: meta (guid pak index'inde), kod (ayri derlenir), editor icerigi.
    static bool Skip(string rel)
        => rel.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
        || rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || rel.Contains("/Editor/", StringComparison.OrdinalIgnoreCase)
        || rel.StartsWith("Editor/", StringComparison.OrdinalIgnoreCase);

    [MenuItem("Project/Build Asset Pack", 1)]
    internal static void Build()
    {
        var assets = App.Assets;
        var catalog = App.Catalog;
        var project = App.Project;
        string root = assets.Root;
        string outPath = Path.Combine(project.Root, "Build", "game.pak");

        // Kokler: Scenes In Build (+ startScene). Eksik kok = build iptal (sessiz bos pak yok).
        var roots = project.Player.BuildScenes();
        if (roots.Count == 0)
        {
            EditorLog.Error("[pak] build'e dahil sahne yok: Project > Player Settings > Scenes In Build");
            return;
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
            return;
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
                foreach (var r in refs)
                {
                    string p = assets.ResolvePath(r); // guid -> yol (yol ise aynen)
                    if (!assets.AllAssets.ContainsKey(p))
                    {
                        unresolved.Add($"{p} <- {key}");
                        continue;
                    }
                    AddDep(key, p);
                    queue.Enqueue(p);
                }
            }
            live.Add(key);
            if (absorbedBy.TryGetValue(key, out var consumer))
            {
                AddDep(key, consumer); // uye yuklenmeden once tuketici (sayfa) hazir olmali
                queue.Enqueue(consumer);
            }
        }
        foreach (var u in unresolved)
            EditorLog.Warning($"[pak] cozulemeyen referans: {u}");

        // ---- Faz 2: veri (yalniz canli kume, deterministik sira) ----
        bool IsLive(string p) => live.Contains(p);
        var keys = new List<string>(live);
        keys.Sort(string.CompareOrdinal);

        int dtexCount = 0, skipped = 0, bakedCount = 0;
        var items = new List<(string Key, string Guid, byte[] Data)>();
        string texKey = null, texFile = null;
        foreach (var key in keys)
        {
            string file = Path.Combine(root, key);
            bool absorbed = absorbedBy.ContainsKey(key);
            byte[] data;
            if (AssetDatabase.ImportTypeOf(key) == typeof(Sprite) && ImportPipeline.ImporterFor(key) == null)
            {
                if (absorbed)
                {
                    // Pikseller tuketicinin sayfasinda; guid->yol eslemesi icin bos stub girisi
                    // kalir (sahne guid'le referanslar, LoadSprite bolge tablosundan baglar).
                    items.Add((key, assets.PathToGuid(key), Array.Empty<byte>()));
                    skipped++;
                    continue;
                }
                data = DecodeToDtex(file);
                if (data != null)
                    dtexCount++;
                else
                {
                    EditorLog.Warning($"[pak] decode edilemedi, ham kopyalandi: {key}");
                    data = File.ReadAllBytes(file); // runtime stbi yolu hala calisir
                }
                if (texKey == null)
                {
                    texKey = key;
                    texFile = file;
                }
            }
            else if (ImportPipeline.ImporterFor(key) != null)
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
                }
                if (data == null)
                {
                    EditorLog.Warning($"[pak] 'main' artifact yok, atlandi: {key}");
                    continue;
                }
            }
            else if (IsSceneLike(key))
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

        var (rawTotal, pakSize) = PakWriter.Write(outPath, items,
            // Stream tipli ses: native player dosyadan offset'le okur -> zlib OLAMAZ.
            key => AssetDatabase.ImportTypeOf(key) == typeof(AudioClip) && AudioImporter.IsStream(key),
            key => deps.TryGetValue(key, out var l) ? l : null);

        int candidates = 0;
        foreach (var rel in assets.AllAssets.Keys)
            if (!Skip(rel))
                candidates++;
        EditorLog.Info($"[pak] {roots.Count} kok sahne -> {live.Count}/{candidates} asset canli ({candidates - live.Count} strip), " +
            $"{items.Count} giris ({dtexCount} dtex, {bakedCount} baked sahne/prefab, {skipped} yutulmus) -> {outPath} " +
            $"({pakSize / 1024.0:0.0} KB, acik {rawTotal / 1024.0:0.0} KB, %{100.0 * pakSize / Math.Max(1, rawTotal):0.0})");

        Verify(outPath, items, texKey, texFile);
    }

    static bool IsSceneLike(string key)
        => key.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

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
    static byte[] DecodeToDtex(string file)
    {
        var rgba = EditorPixels.DecodeFile(file, out int w, out int h);
        return rgba != null ? DtexFormat.BuildFiltered(w, h, rgba) : null;
    }

    static byte[] AwaitPixels(int job, out int w, out int h) => EditorPixels.Await(job, out w, out h);

    // Yazilan pak'i geri okuyup dogrular: index sayisi, tum girisler bayt-esit
    // (zlib round-trip dahil), texture pikselleri pak(DTEX+inflate+defilter)
    // yolundan kaynagin dogrudan decode'uyla BIREBIR ayni mi.
    static void Verify(string pakPath, List<(string Key, string Guid, byte[] Data)> items,
        string texKey, string texFile)
    {
        try
        {
            var pak = new PakSource(pakPath);
            if (pak.Count != items.Count)
            {
                EditorLog.Error($"[pak] verify FAIL: index {pak.Count} != {items.Count}");
                return;
            }
            foreach (var it in items)
            {
                var a = pak.ReadBytes(it.Key);
                if (a == null || !a.AsSpan().SequenceEqual(it.Data))
                {
                    EditorLog.Error($"[pak] verify FAIL: bayt farki {it.Key}");
                    return;
                }
            }

            if (texKey != null)
            {
                var pakPx = AwaitPixels(pak.StartLoad(texKey), out int pw, out int ph);
                var srcPx = AwaitPixels(Sokol.AssetLoad(texFile), out int sw, out int sh);
                if (pakPx == null || srcPx == null || pw != sw || ph != sh
                    || !pakPx.AsSpan().SequenceEqual(srcPx))
                {
                    EditorLog.Error($"[pak] verify FAIL: piksel farki {texKey} (pak {pw}x{ph}, kaynak {sw}x{sh})");
                    return;
                }
                EditorLog.Info($"[pak] verify OK: {items.Count} bayt-esit, {texKey} defilter pikselleri kaynakla BIREBIR ({pw}x{ph})");
            }
            else
                EditorLog.Info($"[pak] verify OK: {items.Count} bayt-esit (texture yok, decode atlandi)");
        }
        catch (Exception e)
        {
            EditorLog.Error("[pak] verify FAIL: " + e.Message);
        }
    }
}
