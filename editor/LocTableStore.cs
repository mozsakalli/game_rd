using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// Editorun ceviri tablosu kapisi (docs/engine-notes.md LOCALIZATION F2). Tablo dosyalari KONVANSIYONLA
// yasar (Localize.TableKeyFor): host Assets/Localization.asset, modul Assets/Modules/<ad>/Localization.asset;
// kullanici dosya secmez — ilk 🌐 baginda burada yaratilir. Tum editor yazimlari (inspector, pencere,
// undo op'lari) bu siniftan gecer: AssetDatabase'in CANLI instance'i degistirilir (Play/projeksiyon ayni
// nesneyi gorur), dirty isaretlenir, bosta (Tick) ya da kritik anlarda (Flush) YAML'a yazilir.
public static class LocTableStore
{
    const double SaveDelay = 1.0; // sn: son duzenlemeden sonra

    // Yeni LayoutBox/TextSprite metni ilk girildiginde OTOMATIK baglanan alanlar ("tum metinler cevrilir"
    // varsayilani). Nick gibi istisnalar 🌐 kapatilarak (prop "" ) isaretlenir.
    static readonly HashSet<string> _defaultLocFields = new(StringComparer.Ordinal)
    {
        "LayoutBox.text", "TextSprite.Text",
    };

    public static bool IsDefaultLocField(string typeName, string fieldName)
        => _defaultLocFields.Contains(typeName + "." + fieldName);

    // ---------------- kapsam / tablo ----------------

    public static string HostScope => Localize.TableFile;

    // Acik sahnenin/prefab stage'inin kapsami (Assets'e goreli tablo anahtari).
    public static string CurrentScope => App.EditScene?.LocTableKey ?? HostScope;

    // Projedeki kapsamlar: host + her Assets/Modules/<ad>/ klasoru.
    public static List<string> Scopes()
    {
        var list = new List<string> { HostScope };
        var root = App.Assets?.Root;
        if (root != null)
        {
            string mods = Path.Combine(root, "Modules");
            if (Directory.Exists(mods))
                foreach (var dir in Directory.GetDirectories(mods))
                    list.Add("Modules/" + Path.GetFileName(dir) + "/" + Localize.TableFile);
        }
        return list;
    }

    public static string ScopeLabel(string scope)
    {
        if (scope == HostScope)
            return "Host";
        int a = scope.IndexOf('/'), b = scope.IndexOf('/', a + 1);
        return b > a ? scope.Substring(a + 1, b - a - 1) : scope;
    }

    // Kapsam tablosu; yoksa create=true ile yaratilir (diller: host tablosununki, o da yoksa ["en"] + calisilan dil).
    public static LocalizationTable Get(string scope, bool create = false)
    {
        var assets = App.Assets;
        if (string.IsNullOrEmpty(scope) || assets == null)
            return null;
        var t = Localize.Table(scope, assets);
        if (t != null || !create)
            return t;
        t = new LocalizationTable();
        var host = scope != HostScope ? Get(HostScope) : null;
        if (host != null)
            t.Languages = new List<string>(host.Languages);
        else if (!string.IsNullOrEmpty(WorkingLanguage) && t.LanguageIndex(WorkingLanguage) < 0)
            t.Languages.Add(WorkingLanguage);
        string abs = Path.Combine(assets.Root, scope);
        Directory.CreateDirectory(Path.GetDirectoryName(abs));
        AssetWatcher.NoteSelfWrite(abs);
        ObjectSerializer.Save(t, abs, assets);
        assets.ScanMetas(createMissing: true);
        t = Localize.Table(scope, assets) ?? t; // AssetDatabase'in canli instance'i (ayni anahtar = tek nesne)
        EditorLog.Info("[loc] tablo olusturuldu: " + scope);
        return t;
    }

    public static IReadOnlyList<string> Languages(string scope)
    {
        var t = Get(scope);
        return t != null ? t.Languages : _fallbackLangs;
    }
    static readonly string[] _fallbackLangs = { "en" };

    // ---------------- calisilan dil ----------------

    // Toolbar combosu: edit projeksiyonu + Play ayni dili gorur; EditorSettings'e kalici.
    public static string WorkingLanguage
    {
        get => App.Settings.workingLanguage;
        set
        {
            value ??= "";
            if (App.Settings.workingLanguage == value)
                return;
            App.Settings.workingLanguage = value;
            App.SaveSettings();
            ApplyWorkingLanguage();
        }
    }

    public static void ApplyWorkingLanguage()
        => Localize.Language = string.IsNullOrEmpty(App.Settings.workingLanguage) ? null : App.Settings.workingLanguage;

    // Calisilan dil; bos ise kapsam tablosunun kaynak dili.
    public static string EffectiveLanguage(string scope)
    {
        var w = WorkingLanguage;
        if (!string.IsNullOrEmpty(w))
            return w;
        return Get(scope)?.SourceLanguage ?? "en";
    }

    // ---------------- yazim (undo op'lari ve pencere buradan gecer) ----------------

    static readonly HashSet<string> _dirty = new(StringComparer.Ordinal);
    static double _lastEdit;

    public static string GetValue(string scope, string key, string lang)
    {
        var t = Get(scope);
        if (t == null) return null;
        int li = t.LanguageIndex(lang);
        return li < 0 ? null : t.Raw(key, li) ?? "";
    }

    public static void SetValue(string scope, string key, string lang, string value)
    {
        var t = Get(scope, create: true);
        if (t == null || string.IsNullOrEmpty(key))
            return;
        if (t.LanguageIndex(lang) < 0)
            t.Languages.Add(lang);
        t.Set(key, lang, value);
        Touch(scope);
    }

    public static void RemoveRow(string scope, string key)
    {
        var t = Get(scope);
        if (t != null && t.Remove(key))
            Touch(scope);
    }

    public static void AddRow(string scope, string key, IList<string> values)
    {
        var t = Get(scope, create: true);
        if (t == null)
            return;
        var e = t.GetOrAdd(key);
        if (values != null)
            for (int i = 0; i < values.Count && i < e.Values.Count; i++)
                e.Values[i] = values[i] ?? "";
        Touch(scope);
    }

    public static bool RenameRow(string scope, string oldKey, string newKey)
    {
        var t = Get(scope);
        if (t == null || !t.Rename(oldKey, newKey))
            return false;
        Touch(scope);
        return true;
    }

    // Sabit dil katalogu: serbest kod girisi YOK (tutarsiz kodlar dogmasin). Kod = ISO 639-1.
    public static readonly (string Code, string Name)[] LanguageCatalog =
    {
        ("en", "English"), ("tr", "Türkçe"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"),
        ("it", "Italiano"), ("pt", "Português"), ("ru", "Русский"), ("nl", "Nederlands"), ("pl", "Polski"),
        ("ar", "Arabic"), ("ja", "Japanese"), ("ko", "Korean"), ("zh", "Chinese"), ("id", "Indonesia"),
        ("vi", "Vietnamese"), ("th", "Thai"), ("hi", "Hindi"),
    };

    public static bool IsKnownLanguage(string code)
    {
        foreach (var (c, _) in LanguageCatalog)
            if (string.Equals(c, code, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    public static void AddLanguage(string scope, string code)
    {
        code = code?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(code) || !IsKnownLanguage(code))
            return;
        var t = Get(scope, create: true);
        if (t == null || t.LanguageIndex(code) >= 0)
            return;
        t.Languages.Add(code);
        foreach (var e in t.Entries)
            while (e.Values.Count < t.Languages.Count)
                e.Values.Add("");
        Touch(scope);
    }

    public static void RemoveLanguage(string scope, string code)
    {
        var t = Get(scope);
        int li = t?.LanguageIndex(code) ?? -1;
        if (li <= 0) // kaynak dil (0) silinmez
            return;
        t.Languages.RemoveAt(li);
        foreach (var e in t.Entries)
            if (e.Values.Count > li)
                e.Values.RemoveAt(li);
        Touch(scope);
    }

    internal static void Touch(string scope)
    {
        _dirty.Add(scope);
        _lastEdit = DigitoyEngine.GLFW.GetTime();
        Localize.RefreshAll(); // bagli alanlar yeni metni gorsun (projeksiyon + Play)
    }

    // Bosta kaydet: son duzenlemeden SaveDelay sonra ve jest yokken.
    public static void Tick(double now)
    {
        if (_dirty.Count == 0 || now - _lastEdit < SaveDelay)
            return;
        if (DigitoyEngine.Editor.GuiUtility.HotControl != 0)
            return;
        Flush();
    }

    public static void Flush()
    {
        if (_dirty.Count == 0 || App.Assets == null)
            return;
        foreach (var scope in _dirty)
        {
            var t = Localize.Table(scope, App.Assets);
            if (t == null)
                continue;
            string abs = Path.Combine(App.Assets.Root, scope);
            AssetWatcher.NoteSelfWrite(abs);
            ObjectSerializer.Save(t, abs, App.Assets);
        }
        _dirty.Clear();
    }

    public static bool HasUnsaved => _dirty.Count > 0;

    // Rename'i kapsamdaki DIGER sahne/prefab dosyalarina yansitir (metin duzeyi: "<alan>@loc: eski" satirlari).
    // Acik doc (skipPath) EditorScene.RenameLocKey ile zaten guncellenir. Undo'suz (dosya duzeyi); sayi doner.
    public static int RenameInOtherScenes(string scope, string oldKey, string newKey, string skipPath)
    {
        var root = App.Assets?.Root;
        if (root == null || string.IsNullOrEmpty(oldKey) || oldKey == newKey)
            return 0;
        string skip = skipPath != null ? Path.GetFullPath(skipPath) : null;
        int count = 0;
        foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!(file.EndsWith(".scene", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                continue;
            if (skip != null && string.Equals(Path.GetFullPath(file), skip, StringComparison.OrdinalIgnoreCase))
                continue;
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Localize.TableKeyFor(rel) != scope)
                continue; // baska kapsam: ayni key baska tabloya ait
            var lines = File.ReadAllLines(file);
            bool touched = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].TrimEnd();
                int c = t.IndexOf(Localize.PropSuffix + ": ", StringComparison.Ordinal);
                if (c < 0) continue;
                string val = t.Substring(c + Localize.PropSuffix.Length + 2).Trim();
                if (val == oldKey || val == "'" + oldKey + "'")
                {
                    lines[i] = t.Substring(0, c + Localize.PropSuffix.Length + 2) + newKey;
                    touched = true;
                }
            }
            if (!touched)
                continue;
            AssetWatcher.NoteSelfWrite(file);
            File.WriteAllLines(file, lines);
            count++;
        }
        return count;
    }

    // ---------------- key uretimi / oneri ----------------

    static readonly HashSet<string> _genericNames = new(StringComparer.OrdinalIgnoreCase)
        { "GameObject", "Text", "Label", "LayoutBox", "TextSprite", "Box", "New GameObject", "Button", "Title" };

    // Baglamdan key: slug(sahne).slug(go)[.alan]; GO adi jenerikse (Text, "Box (2)") metnin ilk 4 kelimesi.
    // Cakismada _2, _3... (ayni metinle cakisma cagiran tarafta FindByText ile cozulur).
    public static string NewKey(LocalizationTable table, string sceneName, string goName, string fieldName,
        bool fieldSuffix, string text)
    {
        string go = StripIndex(goName);
        string body = _genericNames.Contains(go) || Slug(go).Length == 0 ? Slug(FirstWords(text, 4)) : Slug(go);
        if (body.Length == 0)
            body = "text";
        string scene = Slug(sceneName);
        var sb = new StringBuilder();
        if (scene.Length > 0) sb.Append(scene).Append('.');
        sb.Append(body);
        if (fieldSuffix) sb.Append('.').Append(Slug(fieldName));
        string baseKey = sb.ToString();
        if (table == null || table.Find(baseKey) < 0)
            return baseKey;
        for (int i = 2; ; i++)
        {
            string k = baseKey + "_" + i;
            if (table.Find(k) < 0)
                return k;
        }
    }

    // Verilen dilde metni BIREBIR tasiyan satir (tekrar acmamak icin).
    public static string FindByText(LocalizationTable table, string lang, string text)
    {
        if (table == null || string.IsNullOrEmpty(text))
            return null;
        int li = table.LanguageIndex(lang);
        if (li < 0) return null;
        foreach (var e in table.Entries)
            if (e.Values != null && li < e.Values.Count && e.Values[li] == text)
                return e.Key;
        return null;
    }

    // Canli oneri: calisilan dilde metni iceren satirlar (key'de de arar); yoksa diger dillerde. En fazla max.
    public static void Suggest(LocalizationTable table, string lang, string text, string excludeKey,
        List<(string Key, string Text)> result, int max = 5)
    {
        result.Clear();
        if (table == null || string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return;
        int li = Math.Max(0, table.LanguageIndex(lang));
        foreach (var e in table.Entries)
        {
            if (result.Count >= max) break;
            if (e.Key == excludeKey) continue;
            string v = e.Values != null && li < e.Values.Count ? e.Values[li] : "";
            if (Contains(v, text) || Contains(e.Key, text))
                result.Add((e.Key, v));
        }
        if (result.Count > 0)
            return;
        foreach (var e in table.Entries)
        {
            if (result.Count >= max) break;
            if (e.Key == excludeKey || e.Values == null) continue;
            for (int i = 0; i < e.Values.Count; i++)
                if (i != li && Contains(e.Values[i], text))
                {
                    result.Add((e.Key, table.Languages[i] + ": " + e.Values[i]));
                    break;
                }
        }
    }

    static bool Contains(string hay, string needle)
        => !string.IsNullOrEmpty(hay) && hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    static string StripIndex(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        name = name.Trim();
        int p = name.LastIndexOf(" (");
        if (p > 0 && name.EndsWith(")") && int.TryParse(name.AsSpan(p + 2, name.Length - p - 3), out _))
            name = name.Substring(0, p);
        return name;
    }

    static string FirstWords(string text, int n)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts, 0, Math.Min(n, parts.Length));
    }

    // Translitere (tr/de/fr/es temel harfler) + kucuk harf + [a-z0-9_]; CamelCase/bosluk -> '_'.
    public static string Slug(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        bool lastUnderscore = true;
        for (int i = 0; i < s.Length; i++)
        {
            char ch = s[i];
            string rep = Translit(ch);
            if (rep != null)
            {
                foreach (var r in rep) Append(sb, r, ref lastUnderscore);
                continue;
            }
            if (char.IsUpper(ch) && i > 0 && char.IsLower(s[i - 1]))
                Append(sb, '_', ref lastUnderscore); // camelCase siniri
            if (char.IsLetterOrDigit(ch) && ch < 128)
                Append(sb, char.ToLowerInvariant(ch), ref lastUnderscore);
            else
                Append(sb, '_', ref lastUnderscore);
        }
        while (sb.Length > 0 && sb[sb.Length - 1] == '_')
            sb.Length--;
        return sb.ToString();
    }

    static void Append(StringBuilder sb, char c, ref bool lastUnderscore)
    {
        if (c == '_')
        {
            if (lastUnderscore) return;
            lastUnderscore = true;
        }
        else
            lastUnderscore = false;
        sb.Append(c);
    }

    static string Translit(char c) => c switch
    {
        'ç' or 'Ç' => "c", 'ğ' or 'Ğ' => "g", 'ı' or 'İ' => "i",
        'ö' or 'Ö' => "o", 'ş' or 'Ş' => "s", 'ü' or 'Ü' => "u",
        'ä' or 'Ä' => "ae", 'ß' => "ss", 'é' or 'è' or 'ê' or 'ë' or 'É' => "e", 'à' or 'â' or 'á' or 'Á' => "a",
        'ñ' or 'Ñ' => "n", 'ó' or 'ô' or 'Ó' => "o", 'ú' or 'ù' or 'û' or 'Ú' => "u", 'í' or 'î' or 'Í' => "i",
        _ => null,
    };
}
