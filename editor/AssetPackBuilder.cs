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
    static void Build()
    {
        var assets = App.Assets;
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

        int dtexCount = 0;
        var items = new List<(string Key, string Guid, byte[] Data)>();
        foreach (var (key, file) in files)
        {
            byte[] data;
            if (AssetDatabase.ImportTypeOf(key) == typeof(Sprite))
            {
                data = DecodeToDtex(file);
                if (data != null)
                    dtexCount++;
                else
                {
                    EditorLog.Warning($"[pak] decode edilemedi, ham kopyalandi: {key}");
                    data = File.ReadAllBytes(file); // runtime stbi yolu hala calisir
                }
            }
            else if (ImportPipeline.ImporterFor(key) != null)
            {
                // Importer'li kaynak: pak'a ARTIFACT girer, kaynak degil (ttf disarida kalir).
                data = ImportPipeline.GetArtifact(key, "main");
                if (data == null)
                {
                    EditorLog.Warning($"[pak] import basarisiz, atlandi: {key}");
                    continue;
                }
            }
            else
                data = File.ReadAllBytes(file);
            items.Add((key, assets.PathToGuid(key), data));
        }

        var (rawTotal, pakSize) = PakWriter.Write(outPath, items);
        EditorLog.Info($"[pak] {items.Count} asset ({dtexCount} dtex) -> {outPath} " +
            $"({pakSize / 1024.0:0.0} KB, acik {rawTotal / 1024.0:0.0} KB, %{100.0 * pakSize / Math.Max(1, rawTotal):0.0})");

        var texPair = files.Find(f => AssetDatabase.ImportTypeOf(f.Key) == typeof(Sprite));
        Verify(outPath, items, texPair.Key, texPair.File);
    }

    // Kaynak texture'i native worker'la cozup DTEX blobu uretir (build-time,
    // senkron bekleme kabul). Basarisiz olursa null (cagiran ham dosyaya duser).
    static byte[] DecodeToDtex(string file)
    {
        var rgba = AwaitPixels(Sokol.AssetLoad(file), out int w, out int h);
        return rgba != null ? DtexFormat.BuildFiltered(w, h, rgba) : null;
    }

    // Job'u sinirli bekleyip piksel kopyasini dondurur (~5s, build/verify baglami).
    static byte[] AwaitPixels(int job, out int w, out int h)
    {
        w = h = 0;
        if (job < 0)
            return null;
        try
        {
            for (int i = 0; i < 1000; i++)
            {
                int r = Sokol.AssetPoll(job, out var pixels, out w, out h);
                if (r < 0)
                    return null;
                if (r == 1)
                {
                    var rgba = new byte[w * h * 4];
                    Marshal.Copy(pixels, rgba, 0, rgba.Length);
                    return rgba;
                }
                System.Threading.Thread.Sleep(5);
            }
            return null;
        }
        finally
        {
            Sokol.AssetFreeJob(job);
        }
    }

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
