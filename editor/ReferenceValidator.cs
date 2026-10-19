using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DigitoyEngine;
using DigitoyEngine.Editor;

namespace DigitoyEditor;

// Asset referansi dogrulayici: sahne/prefab/.asset YAML'larinda gecen "guid", "guid#id" (ve eski "yol#id") degerlerini
// cozer; kirik (sahip yok / alt oge index'te yok), eski bicimli ve kararsiz (ad tabanli id) referanslari listeler.
// Sessiz bozulma yerine tek yerden rapor: menu Project/Validate References + RPC asset.validate.
// Sema okumaz (importer/tip bilgisi gerekmez): 32 hex guid deseni ve '#'li alt oge deseniyle tarar.
public static class ReferenceValidator
{
    public enum Severity { Error, Warning }

    public readonly struct Issue
    {
        public readonly string File;   // Assets'e goreli
        public readonly int Line;      // 1 tabanli
        public readonly string Value;  // bulunan referans metni
        public readonly string Problem;
        public readonly Severity Sev;
        public Issue(string file, int line, string value, string problem, Severity sev)
        { File = file; Line = line; Value = value; Problem = problem; Sev = sev; }
    }

    static readonly Regex GuidRef = new(@"(?<![0-9a-fA-F])([0-9a-f]{32})(#([^\s""',\]\}]+))?", RegexOptions.Compiled);
    static readonly Regex PathSubRef = new(@"(?<=[:\-]\s*""?)([A-Za-z0-9_./ -]+\.[A-Za-z0-9]+)#([^\s""',\]\}]+)", RegexOptions.Compiled);

    static readonly string[] DocExts = { ".scene", ".prefab", ".asset" };

    public static List<Issue> Validate(AssetDatabase assets)
    {
        var issues = new List<Issue>();
        if (assets?.Root == null)
            return issues;
        var files = new List<string>(assets.AllAssets.Keys);
        files.Sort(StringComparer.Ordinal);
        foreach (var rel in files)
        {
            string ext = Path.GetExtension(rel).ToLowerInvariant();
            if (Array.IndexOf(DocExts, ext) < 0)
                continue;
            // Importer'li .asset (AtlasGroup) de YAML: ayni tarama.
            string full = Path.Combine(assets.Root, rel);
            string[] lines;
            try { lines = File.ReadAllLines(full); }
            catch { continue; }
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("guid:", StringComparison.Ordinal))
                    continue; // meta/kendi kimligi degil, ama .asset basliginda olabilir: atla
                foreach (Match m in GuidRef.Matches(line))
                {
                    string guid = m.Groups[1].Value;
                    string id = m.Groups[3].Success ? m.Groups[3].Value : null;
                    CheckGuid(assets, rel, i + 1, m.Value, guid, id, issues);
                }
                foreach (Match m in PathSubRef.Matches(line))
                {
                    string owner = m.Groups[1].Value.Trim();
                    if (!assets.AllAssets.ContainsKey(owner))
                        continue; // '#' iceren baska bir metin
                    string val = m.Value.Trim();
                    issues.Add(new Issue(rel, i + 1, val, "eski bicim (yol#id): kaydedilince guid#id olur; tasima kirar", Severity.Warning));
                    CheckSub(assets, rel, i + 1, val, owner, m.Groups[2].Value, issues);
                }
            }
        }
        return issues;
    }

    static void CheckGuid(AssetDatabase assets, string file, int line, string value, string guid, string id, List<Issue> issues)
    {
        string owner = assets.ResolvePath(guid);
        if (owner == guid) // cozulmedi
        {
            // Sahne/prefab icinde baska 32-hex degerler de olabilir (spriteID benzeri); yalniz 'prefab:'/ref baglaminda uyari.
            issues.Add(new Issue(file, line, value, "sahip guid cozulmedi (asset silinmis/tasinmamis .meta?)", Severity.Error));
            return;
        }
        if (id != null)
            CheckSub(assets, file, line, value, owner, id, issues);
    }

    static void CheckSub(AssetDatabase assets, string file, int line, string value, string owner, string id, List<Issue> issues)
    {
        if (!SubAssets.IsContainer(owner))
        {
            if (ImportPipeline.ImporterFor(owner) != null)
                issues.Add(new Issue(file, line, value, $"alt oge dogrulanamadi: {owner} index yaymiyor", Severity.Warning));
            return;
        }
        if (!SubAssets.TryGetEntry(owner, id, out var e))
        {
            issues.Add(new Issue(file, line, value, $"alt oge yok: {owner}#{id}", Severity.Error));
            return;
        }
        if (!e.Stable)
            issues.Add(new Issue(file, line, value, $"kararsiz id (ad tabanli): {owner}#{id} — kaynakta yeniden adlandirma kirar", Severity.Warning));
    }

    [MenuItem("Project/Validate References", 5)]
    static void MenuValidate()
    {
        var issues = Validate(App.Assets);
        int err = 0, warn = 0;
        foreach (var it in issues)
        {
            string msg = $"[refs] {it.File}:{it.Line}  {it.Value}  — {it.Problem}";
            if (it.Sev == Severity.Error) { err++; EditorLog.Error(msg); }
            else { warn++; EditorLog.Warning(msg); }
        }
        EditorLog.Info($"[refs] validate: {err} error, {warn} warning ({issues.Count} issue)");
        EditorWindow.GetWindow<ConsolePanel>();
    }
}
