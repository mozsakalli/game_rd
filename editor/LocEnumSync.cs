using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using DigitoyEngine;

namespace DigitoyEditor;

// [Localize] enum -> ceviri tablosu senkronu (docs/engine-notes.md "LOCALIZATION: [Localize] ENUM"). Her kod yuklemesinde
// (RebuildCatalog) ve RPC loc.sync ile: yuklu oyun/plugin assembly'lerindeki isaretli enum'lar taranir (AD KURALI: kisa adi
// "LocalizeAttribute" olan her attribute, Preserve gibi; ilk string ctor arg'i = prefix), kapsam tablosunda eksik
// "<prefix>.<slug(Uye)>" satirlari acilir (kaynak dil = uye adindan metin: "InsufficientCoins" -> "Insufficient coins"),
// enum'da olmayan ya da enum'u silinen prefix satirlari SILINIR (undo'suz; kullanici karari: enum ucunca metni de gereksiz).
// Tablo Enums kaydi runtime'in prefix'i bulmasini saglar (Localize.EnumKey). Kapsam: plugin dll'i Assets/Modules/<ad>/
// altindaysa modul tablosu, aksi halde host (oyun assembly'si tek dosya: her zaman host).
public static class LocEnumSync
{
    public sealed class EnumInfo
    {
        public Type Type;
        public string Prefix;
        public string Scope;
        public readonly List<string> Keys = new();
        public readonly List<string> Members = new();
    }

    public sealed class Result
    {
        public int Enums, Added, Removed;
        public readonly List<string> Log = new();
    }

    // Yuklu assembly'lerdeki [Localize] enum'lar (kapsam + key listesi).
    public static List<EnumInfo> Scan(Assembly[] assemblies, Project project)
    {
        var list = new List<EnumInfo>();
        if (assemblies == null) return list;
        foreach (var asm in assemblies)
        {
            string scope = ScopeOf(asm, project);
            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types; }
            foreach (var t in types)
            {
                if (t == null || !t.IsEnum) continue;
                if (!TryLocalizeAttr(t, out string prefix)) continue;
                var info = new EnumInfo { Type = t, Prefix = string.IsNullOrEmpty(prefix) ? Localize.EnumSlug(t.Name) : prefix, Scope = scope };
                foreach (var name in Enum.GetNames(t))
                {
                    info.Members.Add(name);
                    info.Keys.Add(info.Prefix + "." + Localize.EnumSlug(name));
                }
                list.Add(info);
            }
        }
        return list;
    }

    // Kisa ad kurali: "LocalizeAttribute" ya da onun alt sinifi; ilk string ctor arg'i prefix. Tip cozumu gerekmez
    // (CustomAttributeData: plugin kendi attribute'unu tanimlamis olabilir).
    static bool TryLocalizeAttr(Type t, out string prefix)
    {
        prefix = null;
        IList<CustomAttributeData> attrs;
        try { attrs = t.GetCustomAttributesData(); }
        catch (Exception) { return false; }
        foreach (var a in attrs)
        {
            bool match = false;
            for (var at = a.AttributeType; at != null; at = at.BaseType)
                if (at.Name == "LocalizeAttribute") { match = true; break; }
            if (!match) continue;
            foreach (var arg in a.ConstructorArguments)
                if (arg.ArgumentType == typeof(string)) { prefix = arg.Value as string; break; }
            return true;
        }
        return false;
    }

    // Plugin dll'i Modules/<ad>/ altinda -> modul kapsami; aksi halde host.
    static string ScopeOf(Assembly asm, Project project)
    {
        if (project == null) return LocTableStore.HostScope;
        string name = asm.GetName().Name;
        foreach (var dll in GameCode.PluginDlls(project))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(dll), name, StringComparison.OrdinalIgnoreCase)) continue;
            string rel = Path.GetRelativePath(project.AssetsPath, dll).Replace('\\', '/');
            return Localize.TableKeyFor(rel) ?? LocTableStore.HostScope;
        }
        return LocTableStore.HostScope;
    }

    // Tum kapsamlari senkronla (tablo yoksa ve enum varsa acilir). Hata startup'i kesmez (cagiran try/catch).
    public static Result SyncAll(Assembly[] assemblies, Project project)
    {
        var res = new Result();
        var enums = Scan(assemblies, project);
        res.Enums = enums.Count;
        var scopes = new HashSet<string>(LocTableStore.Scopes(), StringComparer.Ordinal);
        foreach (var e in enums) scopes.Add(e.Scope);
        foreach (var scope in scopes)
        {
            var inScope = enums.FindAll(e => e.Scope == scope);
            var table = LocTableStore.Get(scope, create: inScope.Count > 0);
            if (table == null) continue;
            if (SyncTable(table, inScope, res))
                LocTableStore.Touch(scope);
        }
        if (res.Added + res.Removed > 0)
            EditorLog.Info($"[loc] enum sync: {res.Enums} enum, +{res.Added} -{res.Removed} satir");
        return res;
    }

    // Tek tablo (test edilebilir): Enums kaydi + satirlar. Degisiklik olduysa true.
    public static bool SyncTable(LocalizationTable table, List<EnumInfo> enums, Result res)
    {
        bool changed = false;
        table.Enums ??= new List<LocEnum>();
        var ownedPrefixes = new HashSet<string>(StringComparer.Ordinal);
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in enums)
        {
            if (!ownedPrefixes.Add(e.Prefix))
            {
                res.Log.Add($"prefix cakismasi: {e.Type.Name} '{e.Prefix}' (atlandi)");
                EditorLog.Warning($"[loc] enum prefix cakismasi: {e.Type.FullName} '{e.Prefix}' zaten baska enum'un; atlandi");
                continue;
            }
            seenTypes.Add(e.Type.Name);
            foreach (var k in e.Keys)
                expected.Add(k);
            var rec = table.Enums.Find(x => x != null && x.Type == e.Type.Name);
            if (rec == null)
            {
                table.Enums.Add(new LocEnum { Type = e.Type.Name, Prefix = e.Prefix });
                changed = true;
            }
            else if (rec.Prefix != e.Prefix)
            {
                ownedPrefixes.Add(rec.Prefix); // eski prefix satirlari da enum'undu: silinir
                rec.Prefix = e.Prefix;
                changed = true;
            }
        }
        // Enum'u kodda kalmayan kayitlar: prefix'i sahipli kalir (satirlari silinsin), kayit duser.
        for (int i = table.Enums.Count - 1; i >= 0; i--)
        {
            var rec = table.Enums[i];
            if (rec == null || seenTypes.Contains(rec.Type)) continue;
            ownedPrefixes.Add(rec.Prefix);
            table.Enums.RemoveAt(i);
            res.Log.Add($"enum silindi: {rec.Type} ({rec.Prefix}.*)");
            changed = true;
        }
        // Sahipli prefix altinda beklenmeyen satirlar silinir.
        for (int i = table.Entries.Count - 1; i >= 0; i--)
        {
            string k = table.Entries[i]?.Key;
            if (string.IsNullOrEmpty(k) || expected.Contains(k)) continue;
            int dot = k.IndexOf('.');
            if (dot <= 0 || !ownedPrefixes.Contains(k.Substring(0, dot))) continue;
            table.Entries.RemoveAt(i);
            res.Removed++;
            res.Log.Add("-" + k);
            changed = true;
        }
        if (changed) table.Invalidate();
        // Eksik satirlar: kaynak dil hucresi uye adindan (enum/uye sirasiyla: tablo sirasi deterministik).
        foreach (var e in enums)
        {
            if (!seenTypes.Contains(e.Type.Name)) continue; // prefix cakismasiyla atlanan
            for (int i = 0; i < e.Keys.Count; i++)
            {
                string k = e.Keys[i];
                if (table.Find(k) >= 0) continue;
                var entry = table.GetOrAdd(k);
                entry.Values[0] = Humanize(e.Members[i]);
                res.Added++;
                res.Log.Add("+" + k);
                changed = true;
            }
        }
        return changed;
    }

    // "InsufficientCoins" -> "Insufficient coins", "HTTPError" -> "HTTP error", "ROOM_FULL" -> "Room full".
    public static string Humanize(string member)
    {
        if (string.IsNullOrEmpty(member)) return "";
        var words = new List<string>();
        var cur = new StringBuilder();
        for (int i = 0; i < member.Length; i++)
        {
            char ch = member[i];
            if (ch == '_' || (i > 0 && Localize.IsEnumWordStart(member, i)))
            {
                if (cur.Length > 0) { words.Add(cur.ToString()); cur.Clear(); }
                if (ch == '_') continue;
            }
            cur.Append(ch);
        }
        if (cur.Length > 0) words.Add(cur.ToString());
        var sb = new StringBuilder(member.Length + 4);
        for (int i = 0; i < words.Count; i++)
        {
            string w = words[i];
            bool acronym = w.Length > 1 && IsAllUpper(w) && words.Count > 1 && !IsAllUpper(member.Replace("_", ""));
            if (!acronym)
                w = w.ToLowerInvariant();
            if (i == 0 && !acronym)
                w = char.ToUpperInvariant(w[0]) + w.Substring(1);
            if (i > 0) sb.Append(' ');
            sb.Append(w);
        }
        return sb.ToString();
    }

    static bool IsAllUpper(string s)
    {
        foreach (var c in s)
            if (char.IsLetter(c) && !char.IsUpper(c)) return false;
        return true;
    }
}
