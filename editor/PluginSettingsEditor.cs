using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DigitoyEngine;

namespace DigitoyEditor;

// Plugin ayarlari (docs/plugins.md): oyun/plugin assembly'lerindeki Plugin<TSettings> turevleri (TSettings) ve
// [ProjectSetting] siniflari -> ProjectSettings/Plugins/<Ad>.asset (ObjectSerializer YAML, git'e girer).
// Ad = plugin sinifinin kisa adi (Plugin<T>) ya da tipin kisa adi ([ProjectSetting]). Player Settings penceresi
// ObjectDrawer ile cizer; AssetPackBuilder pak .plugins blobuna DOBJ olarak pisirir; aotcompiler kancalari YAML'dan okur.
public static class PluginSettingsEditor
{
    public sealed class Entry
    {
        public string Name;          // dosya adi / plugin adi
        public Type SettingsType;
        public Type PluginType;      // null = [ProjectSetting]
        public object Instance;      // yuklenmis (ya da varsayilan) nesne
        public bool Referenced;      // oyun kodunda Plugin.Get<Ad>() geciyor mu (metin tarama; bilgi amacli)
        public string Path;
    }

    static readonly Dictionary<string, Entry> _cache = new();
    static Assembly[] _cachedFor;

    // Mevcut oyun assembly kumesine gore giris listesi (reload'da yeniden kurulur; instance'lar dosyadan tazelenir).
    public static List<Entry> Entries(Project project, AssetDatabase assets)
    {
        var code = App.Code?.CodeAssemblies ?? Array.Empty<Assembly>();
        if (!ReferenceEquals(_cachedFor, code)) { _cache.Clear(); _cachedFor = code; }
        var list = new List<Entry>();
        foreach (var asm in code)
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null || t.IsAbstract) continue;
                if (typeof(Plugin).IsAssignableFrom(t))
                {
                    var ts = SettingsTypeOf(t);
                    if (ts != null) list.Add(Get(project, assets, t.Name, ts, t));
                }
                else if (t.IsDefined(typeof(ProjectSettingAttribute), false) && t.GetConstructor(Type.EmptyTypes) != null)
                    list.Add(Get(project, assets, t.Name, t, null));
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return list;
    }

    static Type SettingsTypeOf(Type pluginType)
    {
        for (var b = pluginType.BaseType; b != null; b = b.BaseType)
            if (b.IsGenericType && b.GetGenericTypeDefinition() == typeof(Plugin<>))
                return b.GetGenericArguments()[0];
        return null;
    }

    static Entry Get(Project project, AssetDatabase assets, string name, Type settingsType, Type pluginType)
    {
        if (_cache.TryGetValue(name, out var e) && e.SettingsType == settingsType)
            return e;
        e = new Entry { Name = name, SettingsType = settingsType, PluginType = pluginType, Path = DigitoyEngine.Build.PluginSettingsStore.PathFor(project.Root, name) };
        e.Instance = Activator.CreateInstance(settingsType);
        try { if (File.Exists(e.Path)) ObjectSerializer.LoadInto(e.Instance, e.Path, assets); }
        catch (Exception ex) { EditorLog.Warning($"[plugin] {name} ayarlari okunamadi: {ex.Message}"); }
        e.Referenced = pluginType == null || IsReferenced(project, name);
        _cache[name] = e;
        return e;
    }

    public static void Save(Entry e, AssetDatabase assets)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(e.Path));
        ObjectSerializer.Save(e.Instance, e.Path, assets);
    }

    // [Required] bos alanlar (editor kirmizi; aotcompiler ayni kurali build'de uygular).
    public static List<string> MissingRequired(object o)
    {
        var list = new List<string>();
        foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!f.IsDefined(typeof(RequiredAttribute), false)) continue;
            var v = f.GetValue(o);
            if (v == null || (v is string s && string.IsNullOrWhiteSpace(s)) || (v is Array a && a.Length == 0)) list.Add(f.Name);
        }
        return list;
    }

    // Kaba metin taramasi: plugin klasoru disindaki Assets/**/*.cs icinde "Get<Ad>" geciyor mu. Bilgi etiketi; kesin kaynak
    // aotcompiler TrimAnalysis (build).
    static bool IsReferenced(Project project, string name)
    {
        string needle = "Get<" + name + ">";
        foreach (var f in Directory.GetFiles(project.AssetsPath, "*.cs", SearchOption.AllDirectories))
        {
            string n = f.Replace('\\', '/');
            if (n.Contains("/Plugins/" + name + "/") || n.Contains("/Editor/")) continue;
            try { if (File.ReadAllText(f).Contains(needle, StringComparison.Ordinal)) return true; } catch { }
        }
        return false;
    }

    // Pak blobu: tum girisler (kullanilmayan plugin'in tipi AOT'ye girmez, blobdaki fazlalik zararsiz ve kucuk).
    public static List<(string name, object settings)> CollectForPak(Project project, AssetDatabase assets)
    {
        var list = new List<(string, object)>();
        foreach (var e in Entries(project, assets))
            list.Add((e.Name, e.Instance));
        return list;
    }
}
