using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Release asset ciktisi: Assets/ altindaki icerik tek game.pak dosyasina
// paketlenir (guid tablosu gomulu — release'te ScanMetas/.meta/dizin taramasi yok).
// Texture'lar build'de BIR KEZ cozulup DTEX (ham RGBA) yazilir: runtime'da png
// decode maliyeti sifir, giris-bazli zlib boyutu geri alir. V1 tum asset'leri
// alir; strip/Consumes (yalniz kullanilanlar) build fazi borcu.
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
        string root = assets.Root;
        string outPath = Path.Combine(App.Project.Root, "Build", "game.pak");

        var files = new List<(string Key, string File)>();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!Skip(rel))
                files.Add((rel, file));
        }
        files.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key)); // deterministik cikti

        // Atlas gruplarina alinan uyeler: pak'ta sayfalarda yasar, tekil kopya gereksiz
        // (png tamamen atlanir; fontun yalniz "sheet" artifact'i atlanir).
        var claimed = AtlasImporter.ClaimedMembers(AtlasSystem.GroupAssets());

        int dtexCount = 0, skipped = 0, bakedCount = 0;
        var items = new List<(string Key, string Guid, byte[] Data)>();
        string texKey = null, texFile = null;
        foreach (var (key, file) in files)
        {
            byte[] data;
            if (AssetDatabase.ImportTypeOf(key) == typeof(Sprite))
            {
                if (claimed.Contains(key))
                {
                    // Pikseller atlas sayfasinda; guid->yol eslemesi icin bos stub girisi kalir
                    // (sahne guid'le referanslar, LoadSprite bolge tablosundan baglar).
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
                // kalir). "main" asset anahtariyla, digerleri "<anahtar>#<ad>" ile.
                data = ImportPipeline.GetArtifact(key, "main");
                if (data == null)
                {
                    EditorLog.Warning($"[pak] import basarisiz, atlandi: {key}");
                    continue;
                }
                bool claimedFont = claimed.Contains(key);
                foreach (var name in ImportPipeline.ArtifactNames(key))
                {
                    if (name == "main" || name == "members" || name == "regions")
                        continue; // editor-ici sozlesmeler; runtime okumaz
                    if (claimedFont && name == "sheet")
                    {
                        skipped++;
                        continue;
                    }
                    var extra = ImportPipeline.GetArtifact(key, name);
                    if (extra != null)
                        items.Add((key + "#" + name, "", extra));
                }
            }
            else if (IsSceneLike(key))
            {
                // Sahne/prefab: YAML -> pismis SceneBinary (prefab'lar ACILIR, override'lar
                // uygulanir, alanlar sema indeksiyle yazilir). Runtime YAML/DocNode gormez.
                data = BakeScene(key, file, catalog, assets);
                if (data == null)
                    continue;
                bakedCount++;
            }
            else
                data = File.ReadAllBytes(file);
            items.Add((key, assets.PathToGuid(key), data));
        }

        // Proje ayarlari: runtime project.yaml okumaz; pismis kayit pak'ta.
        items.Add((ProjectBinary.PakKey, "", ProjectBinary.Write(App.Project.Name, App.Project.StartScene)));

        var (rawTotal, pakSize) = PakWriter.Write(outPath, items,
            // Stream tipli ses: native player dosyadan offset'le okur -> zlib OLAMAZ.
            key => AssetDatabase.ImportTypeOf(key) == typeof(AudioClip) && AudioImporter.IsStream(key));
        EditorLog.Info($"[pak] {items.Count} giris ({dtexCount} dtex, {bakedCount} baked sahne/prefab, {skipped} atlas uyesi atlandi) -> {outPath} " +
            $"({pakSize / 1024.0:0.0} KB, acik {rawTotal / 1024.0:0.0} KB, %{100.0 * pakSize / Math.Max(1, rawTotal):0.0})");

        Verify(outPath, items, texKey, texFile);
    }

    static bool IsSceneLike(string key)
        => key.EndsWith(".scene", StringComparison.OrdinalIgnoreCase)
        || key.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

    static byte[] BakeScene(string key, string file, TypeCatalog catalog, AssetDatabase assets)
    {
        try
        {
            var doc = SceneDoc.Parse(File.ReadAllText(file));
            doc.ExpandPrefabs(catalog, assets); // delta kayitlari -> tam agac (ic ice dahil)
            return SceneBinary.Bake(doc, catalog, m => EditorLog.Warning($"{m} ({key})"));
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
