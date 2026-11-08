using System;
using System.IO;
using DigitoyEngine;

namespace DigitoyEditor;

// Assets/ DOSYA islemleri (rename / tasima / silme): Project paneli ve RPC buradan gecer. Kurallar:
//  - .meta sahibiyle birlikte gider -> GUID korunur, sahne referanslari kirilmaz (Unity modeli).
//  - Library/Artifacts guid-anahtarli: tasima/rename artifact'e dokunmaz; silmede artifact klasoru de temizlenir.
//  - Hedefte ayni ad varsa islem REDDEDILIR (sessiz uzerine yazma yok). Undo yok (dosya duzeyi).
//  - Acik sahne / prefab stage dosyasi: tasinirsa EditScene.Path izler; SILINEMEZ (once kapat).
//  - Container ici alt ogeler ("yol#id") salt okunur turevdir: islem yok.
//  - Yol-string kurallar (AtlasGroup.folders onekleri, Localize kapsami) otomatik guncellenmez; bilinen sinir.
// Kendi yazdigimiz yollar AssetWatcher'a NoteSelfWrite ile maskelenir; tarama/liste tazeleme burada yapilir.
public static class AssetOps
{
    public static bool IsOperable(string rel)
        => !string.IsNullOrEmpty(rel) && rel.IndexOf('#') < 0;

    // Dosya ya da klasoru ayni klasorde yeniden adlandirir. newName uzantiyi da icerir (dosyada).
    public static bool Rename(string rel, string newName)
    {
        if (!Validate(rel, out _)) return false;
        newName = (newName ?? "").Trim();
        if (newName.Length == 0 || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName.Contains('/') || newName.Contains('\\'))
        {
            EditorLog.Warning($"[assets] invalid name: '{newName}'");
            return false;
        }
        string parent = AssetTree.Parent(rel);
        string dstRel = parent.Length > 0 ? parent + "/" + newName : newName;
        if (string.Equals(dstRel, rel, StringComparison.Ordinal))
            return true;
        return MoveTo(rel, dstRel, "renamed");
    }

    // Dosya ya da klasoru destDir klasorune (Assets'e goreli, "" = kok) tasir.
    public static bool Move(string rel, string destDir)
    {
        if (!Validate(rel, out _)) return false;
        destDir = (destDir ?? "").Replace('\\', '/').Trim('/');
        string name = Path.GetFileName(rel);
        string dstRel = destDir.Length > 0 ? destDir + "/" + name : name;
        if (string.Equals(AssetTree.Parent(rel), destDir, StringComparison.OrdinalIgnoreCase))
            return true; // ayni klasor: no-op
        if (destDir.Length > 0 && (string.Equals(destDir, rel, StringComparison.OrdinalIgnoreCase)
            || destDir.StartsWith(rel + "/", StringComparison.OrdinalIgnoreCase)))
        {
            EditorLog.Warning($"[assets] cannot move '{rel}' into itself");
            return false;
        }
        string destFull = Path.Combine(App.Assets.Root, destDir);
        if (!Directory.Exists(destFull))
        {
            EditorLog.Warning($"[assets] target folder not found: '{destDir}'");
            return false;
        }
        return MoveTo(rel, dstRel, "moved");
    }

    static bool MoveTo(string rel, string dstRel, string verb)
    {
        var assets = App.Assets;
        string src = Path.Combine(assets.Root, rel), dst = Path.Combine(assets.Root, dstRel);
        bool isDir = Directory.Exists(src);
        // Yalniz buyuk-kucuk harf degisiyorsa Windows'ta Exists(dst) true doner; buna izin ver.
        bool caseOnly = string.Equals(rel, dstRel, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(dst) || Directory.Exists(dst)))
        {
            EditorLog.Warning($"[assets] target already exists: '{dstRel}'");
            return false;
        }
        try
        {
            AssetWatcher.NoteSelfWrite(src);
            AssetWatcher.NoteSelfWrite(dst);
            if (isDir)
            {
                Directory.Move(src, dst);
            }
            else
            {
                File.Move(src, dst);
                if (File.Exists(src + ".meta"))
                {
                    AssetWatcher.NoteSelfWrite(src + ".meta");
                    AssetWatcher.NoteSelfWrite(dst + ".meta");
                    File.Move(src + ".meta", dst + ".meta");
                }
            }
        }
        catch (Exception e)
        {
            EditorLog.Error($"[assets] {verb} failed for '{rel}': {e.Message}");
            return false;
        }
        RetargetOpenScene(src, dst, isDir);
        RetargetSelection(src, dst, isDir);
        AfterChange(assets);
        EditorLog.Info($"[assets] {verb}: {rel} -> {dstRel}");
        return true;
    }

    // Kalici silme (geri donusum kutusu yok). Klasorde tum icerik + artifact'lar gider.
    public static bool Delete(string rel)
    {
        if (!Validate(rel, out var assets)) return false;
        string full = Path.Combine(assets.Root, rel);
        bool isDir = Directory.Exists(full);
        if (IsOpenScenePath(full, isDir))
        {
            EditorLog.Warning($"[assets] '{rel}' is open in the editor; close it before deleting");
            return false;
        }
        try
        {
            string prefix = rel + "/";
            foreach (var kv in assets.AllAssets)
                if (string.Equals(kv.Key, rel, StringComparison.OrdinalIgnoreCase)
                    || (isDir && kv.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    ImportPipeline.DeleteArtifacts(kv.Value);
            AssetWatcher.NoteSelfWrite(full);
            if (isDir)
                Directory.Delete(full, recursive: true);
            else
            {
                File.Delete(full);
                if (File.Exists(full + ".meta"))
                {
                    AssetWatcher.NoteSelfWrite(full + ".meta");
                    File.Delete(full + ".meta");
                }
            }
        }
        catch (Exception e)
        {
            EditorLog.Error($"[assets] delete failed for '{rel}': {e.Message}");
            return false;
        }
        if (Selection.AssetPath != null && IsUnder(Selection.AssetPath, full, isDir))
            Selection.AssetPath = null;
        AfterChange(assets);
        EditorLog.Info($"[assets] deleted: {rel}");
        return true;
    }

    // Klasorun altindaki asset sayisi (silme onayinda gosterilir).
    public static int CountAssets(string rel)
    {
        var assets = App.Assets;
        if (assets == null) return 0;
        string prefix = rel + "/";
        int n = 0;
        foreach (var k in assets.AllAssets.Keys)
            if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                n++;
        return n;
    }

    public static void ShowInExplorer(string rel)
    {
        var assets = App.Assets;
        if (assets == null) return;
        string full = Path.GetFullPath(Path.Combine(assets.Root, rel ?? ""));
        try
        {
            if (OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                    Directory.Exists(full) ? "\"" + full + "\"" : "/select,\"" + full + "\"") { UseShellExecute = false });
            else if (OperatingSystem.IsMacOS())
                System.Diagnostics.Process.Start("open", Directory.Exists(full) ? "\"" + full + "\"" : "-R \"" + full + "\"");
            else
                System.Diagnostics.Process.Start("xdg-open", "\"" + (Directory.Exists(full) ? full : Path.GetDirectoryName(full)) + "\"");
        }
        catch (Exception e)
        {
            EditorLog.Warning("[assets] could not open file browser: " + e.Message);
        }
    }

    static bool Validate(string rel, out AssetDatabase assets)
    {
        assets = App.Assets;
        if (assets == null || !IsOperable(rel))
        {
            EditorLog.Warning($"[assets] not operable: '{rel}'");
            return false;
        }
        string full = Path.Combine(assets.Root, rel);
        if (!File.Exists(full) && !Directory.Exists(full))
        {
            EditorLog.Warning($"[assets] not found: '{rel}'");
            return false;
        }
        return true;
    }

    static bool IsUnder(string fullPath, string root, bool rootIsDir)
    {
        string a = Path.GetFullPath(fullPath), b = Path.GetFullPath(root);
        if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;
        return rootIsDir && a.StartsWith(b.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsOpenScenePath(string full, bool isDir)
    {
        string open = App.EditScene?.Path;
        return open != null && IsUnder(open, full, isDir);
    }

    // Acik sahne/prefab stage dosyasi tasindiysa yolu izle (doc degismez).
    static void RetargetOpenScene(string src, string dst, bool isDir)
    {
        var es = App.EditScene;
        if (es?.Path == null || !IsUnder(es.Path, src, isDir)) return;
        string rebased = Rebase(es.Path, src, dst);
        es.SetPath(rebased);
        AssetWatcher.NoteSelfWrite(rebased);
    }

    static void RetargetSelection(string src, string dst, bool isDir)
    {
        string sel = Selection.AssetPath;
        if (sel == null || !IsUnder(sel, src, isDir)) return;
        string sub = Selection.AssetSub;
        string rebased = Rebase(sel, src, dst);
        if (sub != null) Selection.SelectSubAsset(rebased, sub);
        else Selection.AssetPath = rebased;
    }

    static string Rebase(string path, string src, string dst)
    {
        string a = Path.GetFullPath(path), b = Path.GetFullPath(src);
        return Path.GetFullPath(dst) + a.Substring(b.Length);
    }

    static void AfterChange(AssetDatabase assets)
    {
        assets.ScanMetas(createMissing: true);
        AssetWatcher.NoteFileListChanged();
        AtlasSystem.RefreshAll(); // yol-onekli atlas uyeligi degismis olabilir
    }
}
