using System;
using System.Collections.Generic;
using System.Text;

namespace DigitoyEngine;

// Coklu dil cekirdegi. Model (docs/engine-notes.md LOCALIZATION):
//   * Component alani DUZ string kalir; kod hicbir sey bilmez (box.Text = nick serbest).
//   * Doc'ta alanin yaninda "<alan>@loc: <key>" prop'u = bu instance'in bu alani cevrilir. Alanin
//     doc'taki metni son calisilan dilin ONBELLEGIDIR; gercek LocalizationTable'dadir.
//   * Yuklemede (SceneDoc.Spawn / SceneBinary.Spawn) Binding kurulur ve alan gecerli dilde yazilir;
//     Language degisince tum binding'ler yeniden yazilir (OnValidate yolu). Frame maliyeti SIFIR.
//   * Parametreli metin: sablon tabloda ("{0} coins"); kod key bilmeden comp.Format(args) cagirir —
//     binding son args'i saklar, dil degisince yeniden formatlanir. Play'de args gelmeden sablon
//     GOSTERILMEZ (bos), edit projeksiyonunda sablon gorunur.
public static class Localize
{
    public const string PropSuffix = "@loc";
    public const string TableFile = "Localization.asset";

    // "text@loc" -> "text"; bag prop'u degilse null.
    public static string LocPropField(string propName)
        => propName != null && propName.Length > PropSuffix.Length && propName.EndsWith(PropSuffix, StringComparison.Ordinal)
            ? propName.Substring(0, propName.Length - PropSuffix.Length) : null;

    // Yayin dili (kod: "en", "tr"...). null/bos = tablonun kaynak dili. Degisim tum binding'leri yeniden yazar.
    public static string Language
    {
        get => _language;
        set
        {
            value = string.IsNullOrEmpty(value) ? null : value;
            if (_language == value)
                return;
            _language = value;
            Version++;
            ReapplyAll();
            Changed?.Invoke();
        }
    }
    static string _language;

    // Her dil degisiminde artar (UI/tool'lar onbellek tazelemek icin kiyaslar).
    public static int Version { get; private set; }
    public static event Action Changed;

    const string PrefKey = "Localize.Lang";

    // Oyun icinden kullanici secimi: dil + PlayerPrefs (bir sonraki acilista InitLanguage okur). Editor bunu KULLANMAZ
    // (Working language ayri; prefs'e sizmasin).
    public static void SetUserLanguage(string lang)
    {
        Language = lang;
        PlayerPrefs.SetString(PrefKey, lang ?? "");
    }

    // Acilis dili (player boot): PlayerPrefs secimi > cihaz dili > kaynak dil (null). Host cagirir, sahne yuklenmeden once.
    public static void InitLanguage()
    {
        string saved = PlayerPrefs.GetString(PrefKey, "");
        Language = !string.IsNullOrEmpty(saved) ? saved : DeviceLanguage();
    }

    // Cihaz dili (ISO 639-1 kucuk harf) ya da null. Masaustu shim; mobil kabuklar sonraki tur (null -> kaynak dil).
    public static unsafe string DeviceLanguage()
    {
#if DE_DESKTOP
        try
        {
            byte* buf = stackalloc byte[8];
            if (de_sys_language(buf, 8) != 0 && buf[0] != 0 && buf[1] != 0)
                return "" + (char)buf[0] + (char)buf[1];
        }
        catch (Exception) { }
#endif
        return null;
    }

#if DE_DESKTOP
    [System.Runtime.InteropServices.DllImport("digitoyengine_native", EntryPoint = "de_sys_language")]
    static extern unsafe int de_sys_language(byte* outBuf, int cap);
#endif

    // Sahne/prefab anahtarindan kapsam tablosu: "Modules/<ad>/..." -> modul tablosu, aksi halde host tablosu.
    // Anahtar Assets'e goreli ('/' ayirici). null -> null (kapsam yok: test sahneleri).
    public static string TableKeyFor(string assetKey)
    {
        if (string.IsNullOrEmpty(assetKey))
            return null;
        assetKey = assetKey.Replace('\\', '/');
        if (assetKey.StartsWith("Modules/", StringComparison.OrdinalIgnoreCase))
        {
            int slash = assetKey.IndexOf('/', 8);
            if (slash > 8)
                return assetKey.Substring(0, slash + 1) + TableFile;
        }
        return TableFile;
    }

    // Acik kayit: AssetDatabase olmadan tablo (testler, editor onbellegi). Yukleme once buraya bakar.
    static readonly Dictionary<string, LocalizationTable> _registered = new(StringComparer.Ordinal);

    public static void RegisterTable(string key, LocalizationTable table)
    {
        if (string.IsNullOrEmpty(key))
            return;
        if (table == null)
        {
            if (_registered.TryGetValue(key, out var old))
                _loaded.Remove(old);
            _registered.Remove(key);
            _loadedByKey.Remove(key);
        }
        else
            _registered[key] = table;
    }

    // Kapsam tablosu: kayitli degilse assets'ten [Asset] nesne olarak (pak'ta DOBJ, editorde YAML). Yoksa null.
    // Yuklenen tablolar sirayla hatirlanir (host once): tablo vermeden Get(key) bunlari tarar.
    public static LocalizationTable Table(string key, AssetDatabase assets)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        LocalizationTable t;
        if (!_registered.TryGetValue(key, out t))
        {
            if (assets == null || !assets.Exists(key))
                return null;
            t = assets.LoadObject(key, typeof(LocalizationTable)) as LocalizationTable;
        }
        if (t == null)
            return null;
        // Ayni anahtarin onceki instance'i (kod reload'inda AssetDatabase nesne cache'i duser, tablo yeniden yuklenir)
        // listeden duser: Get(key) bayat tabloyu once tarayip eski metin dondurmesin.
        if (_loadedByKey.TryGetValue(key, out var prev) && !ReferenceEquals(prev, t))
            _loaded.Remove(prev);
        _loadedByKey[key] = t;
        if (!_loaded.Contains(t))
            _loaded.Add(t);
        return t;
    }

    static readonly List<LocalizationTable> _loaded = new();
    static readonly Dictionary<string, LocalizationTable> _loadedByKey = new(StringComparer.Ordinal);
    public static IReadOnlyList<LocalizationTable> LoadedTables => _loaded;

    // Kod yolu: tablo + key -> gecerli dildeki metin (fallback kaynak dil), yoksa null.
    public static string Get(LocalizationTable table, string key) => table?.Get(key, _language);

    public static string Format(LocalizationTable table, string key, params object[] args)
    {
        var s = Get(table, key);
        return s == null ? null : FormatString(s, args, _language, table);
    }

    // Tablo vermeden: yuklu tablolar sirayla (host, sonra moduller). Yoksa "[MISSING:key]" (gorunur hata).
    public static string Get(string key)
    {
        for (int i = 0; i < _loaded.Count; i++)
        {
            var s = _loaded[i].Get(key, _language);
            if (s != null)
                return s;
        }
        return "[MISSING:" + key + "]";
    }

    public static string Format(string key, params object[] args)
    {
        for (int i = 0; i < _loaded.Count; i++)
        {
            var s = _loaded[i].Get(key, _language);
            if (s != null)
                return FormatString(s, args, _language, _loaded[i]);
        }
        return "[MISSING:" + key + "]";
    }

    // ---------------- [Localize] enum ----------------
    // Enum degeri -> key "<prefix>.<slug(Uye)>" (LocalizeAttribute, engine-notes "LOCALIZATION: [Localize] ENUM"). Prefix yuklu
    // tablolarin Enums kaydindan (editor senkronu yazar); kayit yoksa slug(tip adi). Runtime attribute OKUMAZ (AOT'ta
    // reflection yuzeyi: yalniz Type.Name + Enum.ToString, ikisi de her enum icin kok). Sicak yol degil (hata mesaji,
    // durum etiketi): cagri basina kucuk string allocu kabul; prefix onbellegi string anahtarli (ALC'yi rehin almaz).
    public static string EnumKey(Enum value)
    {
        if (value == null)
            return null;
        return EnumPrefix(value.GetType().Name) + "." + EnumSlug(value.ToString());
    }

    public static string Get(Enum value) => Get(EnumKey(value));
    public static string Format(Enum value, params object[] args) => Format(EnumKey(value), args);

    static readonly Dictionary<string, string> _enumPrefix = new(StringComparer.Ordinal);
    static int _enumPrefixLoaded = -1;

    public static string EnumPrefix(string typeName)
    {
        if (_enumPrefixLoaded != _loaded.Count) // tablo yuklendi/dustu: onbellek bayat
        {
            _enumPrefix.Clear();
            _enumPrefixLoaded = _loaded.Count;
        }
        if (_enumPrefix.TryGetValue(typeName, out var p))
            return p;
        p = null;
        for (int i = 0; i < _loaded.Count && p == null; i++)
            p = _loaded[i].EnumPrefix(typeName);
        p ??= EnumSlug(typeName);
        _enumPrefix[typeName] = p;
        return p;
    }

    // C# tanimlayicisi -> kucuk snake_case: "InsufficientCoins" -> "insufficient_coins", "HTTPError" -> "http_error",
    // "ROOM_FULL" -> "room_full". Editor ve runtime AYNI fonksiyonu kullanir (key'ler birebir eslesmeli); yalniz ASCII.
    public static string EnumSlug(string ident)
    {
        if (string.IsNullOrEmpty(ident))
            return "";
        var sb = new StringBuilder(ident.Length + 4);
        bool lastUnderscore = true;
        for (int i = 0; i < ident.Length; i++)
        {
            char ch = ident[i];
            if (ch == '_' || !(ch < 128 && (char.IsLetter(ch) || char.IsDigit(ch))))
            {
                if (!lastUnderscore) { sb.Append('_'); lastUnderscore = true; }
                continue;
            }
            if (IsEnumWordStart(ident, i) && !lastUnderscore)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(ch));
            lastUnderscore = false;
        }
        while (sb.Length > 0 && sb[sb.Length - 1] == '_')
            sb.Length--;
        return sb.ToString();
    }

    // Kelime siniri: kucuk/rakam -> buyuk ("tCo"), ya da buyuk -> buyuk+kucuk ("PEr": kisaltma sonu).
    internal static bool IsEnumWordStart(string s, int i)
    {
        if (i == 0 || !char.IsUpper(s[i]))
            return false;
        char prev = s[i - 1];
        if (char.IsLower(prev) || char.IsDigit(prev))
            return true;
        return char.IsUpper(prev) && i + 1 < s.Length && char.IsLower(s[i + 1]);
    }

    // ---------------- Binding ----------------

    public sealed class Binding
    {
        internal Component Owner;
        internal SerializedType.FieldSchema Field;
        internal LocalizationTable Table;
        internal string Key;
        internal object[] Args;    // son Format(args); null = henuz verilmedi
        internal Binding Next;     // ayni component'in diger bagli alanlari
        internal int Index = -1;   // _all icindeki yeri (swap-remove)

        public string FieldName => Field.Name;
        public string LocKey => Key;
        public LocalizationTable LocTable => Table;
    }

    static readonly List<Binding> _all = new();
    public static int BindingCount => _all.Count;

    // Alani tabloya baglar ve hemen gecerli dili yazar. Spawn sirasinda (Awake oncesi) validate=false:
    // alan setter degil dogrudan yazilir, OnValidate aktivasyonla gelir.
    public static Binding Bind(Component c, SerializedType.FieldSchema field, LocalizationTable table, string key, bool validate = false)
    {
        if (c == null || field == null || field.Kind != SerializedType.Kind.String || string.IsNullOrEmpty(key))
            return null;
        var b = FindBinding(c, field.Name);
        if (b == null)
        {
            b = new Binding { Owner = c, Field = field, Next = c._loc };
            c._loc = b;
            b.Index = _all.Count;
            _all.Add(b);
        }
        b.Table = table;
        b.Key = key;
        b.Args = null;
        Apply(b, validate);
        return b;
    }

    public static Binding FindBinding(Component c, string fieldName)
    {
        for (var b = c?._loc; b != null; b = b.Next)
            if (b.Field.Name == fieldName)
                return b;
        return null;
    }

    public static string KeyOf(Component c, string fieldName) => FindBinding(c, fieldName)?.Key;

    // Kod key bilmeden parametre verir: alanin baglandigi sablon args ile yazilir; dil degisince
    // ayni args'la yeniden. Bagli degilse alanin MEVCUT metni sablon sayilir (tek seferlik).
    public static void Format(Component c, string fieldName, object[] args)
    {
        var b = FindBinding(c, fieldName);
        if (b != null)
        {
            b.Args = args ?? Array.Empty<object>();
            Apply(b, validate: true);
            return;
        }
        var entry = c?._gameObject?._scene?.Catalog?.Find(c.GetType());
        var f = entry != null ? SerializedType.Find(entry.Schema, fieldName) : null;
        if (f == null || f.Kind != SerializedType.Kind.String)
            return;
        f.Set(c, FormatString(f.Get(c) as string ?? "", args));
        c.OnValidate();
    }

    // Instantiate: klonun ayni alanlari ayni satira baglanir (args dahil; metin CopyTo ile zaten kopyalandi).
    internal static void CopyBindings(Component src, Component dst)
    {
        for (var b = src._loc; b != null; b = b.Next)
        {
            var nb = new Binding { Owner = dst, Field = b.Field, Table = b.Table, Key = b.Key, Args = b.Args, Next = dst._loc };
            dst._loc = nb;
            nb.Index = _all.Count;
            _all.Add(nb);
        }
    }

    // Component olurken (Scene.Destroy*Now) baglar dusur — liste sizmaz, ALC reload'da tip tutulmaz.
    internal static void Unbind(Component c)
    {
        for (var b = c._loc; b != null; b = b.Next)
            RemoveFromAll(b);
        c._loc = null;
    }

    // Tek alanin bagini cozer (editor 🌐 kapatinca); alan metnine dokunmaz. Bag yoksa false.
    public static bool UnbindField(Component c, string fieldName)
    {
        Binding prev = null;
        for (var b = c?._loc; b != null; prev = b, b = b.Next)
        {
            if (b.Field.Name != fieldName)
                continue;
            if (prev == null) c._loc = b.Next; else prev.Next = b.Next;
            RemoveFromAll(b);
            return true;
        }
        return false;
    }

    static void RemoveFromAll(Binding b)
    {
        int i = b.Index;
        if (i < 0)
            return;
        int last = _all.Count - 1;
        if (i != last)
        {
            var moved = _all[last];
            _all[i] = moved;
            moved.Index = i;
        }
        _all.RemoveAt(last);
        b.Index = -1;
    }

    static void Apply(Binding b, bool validate)
    {
        var c = b.Owner;
        if (c == null || c._destroyed)
            return;
        // Metin TEK gercek: tablo. Satir yoksa gorunur hata (sessiz eski metin yok; doc'ta metin zaten serilesmez).
        string text = b.Table?.Get(b.Key, _language) ?? "[MISSING:" + b.Key + "]";
        if (HasPlaceholder(text))
        {
            if (b.Args != null)
                text = FormatString(text, b.Args, _language, b.Table);
            else if (!(c._gameObject?._scene?.EditMode ?? false))
                text = ""; // Play: args gelene kadar sablon gorunmez
        }
        if ((b.Field.Get(c) as string) == text)
            return;
        b.Field.Set(c, text);
        if (validate)
            c.OnValidate();
    }

    static void ReapplyAll()
    {
        for (int i = 0; i < _all.Count; i++)
            Apply(_all[i], validate: true);
    }

    // Editor: dil combosu / tablo duzenlemesi sonrasi yeniden yazim (dil degismeden).
    public static void RefreshAll() { Version++; ReapplyAll(); }

    // ---------------- Bicimleme ----------------

    public static bool HasPlaceholder(string s) => s != null && s.IndexOf('{') >= 0;

    // "{n}" / "{n:fmt}" / "{{" "}}" — CultureInfo/Regex yok (AOT corelib). fmt: LocFormat (cap, number, time) ya da
    // IFormattable. lang/table: dil ayiraclari + format.* metinleri (null = gecerli dil, gomulu varsayilanlar).
    // Eksik arg: yer tutucu oldugu gibi kalir (gorunur hata, sessiz bosluk degil).
    public static string FormatString(string template, object[] args) => FormatString(template, args, _language, null);

    public static string FormatString(string template, object[] args, string lang, LocalizationTable table)
    {
        if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0)
            return template ?? "";
        var sb = new StringBuilder(template.Length + 32); // ic ice cagri (format.time.at) icin paylasilan tampon degil
        int n = template.Length;
        for (int i = 0; i < n; i++)
        {
            char ch = template[i];
            if (ch == '{')
            {
                if (i + 1 < n && template[i + 1] == '{') { sb.Append('{'); i++; continue; }
                int close = template.IndexOf('}', i + 1);
                if (close < 0) { sb.Append(template, i, n - i); break; }
                int colon = template.IndexOf(':', i + 1, close - i - 1);
                int numEnd = colon < 0 ? close : colon;
                int idx = 0;
                bool ok = numEnd > i + 1;
                for (int k = i + 1; k < numEnd && ok; k++)
                {
                    char d = template[k];
                    if (d < '0' || d > '9') ok = false;
                    else idx = idx * 10 + (d - '0');
                }
                if (!ok || args == null || idx >= args.Length)
                {
                    sb.Append(template, i, close - i + 1);
                    i = close;
                    continue;
                }
                var arg = args[idx];
                string fmt = colon < 0 ? null : template.Substring(colon + 1, close - colon - 1);
                sb.Append(fmt == null ? (arg is string ss ? ss : arg?.ToString() ?? "") : LocFormat.Apply(arg, fmt, lang, table));
                i = close;
            }
            else if (ch == '}' && i + 1 < n && template[i + 1] == '}')
            {
                sb.Append('}');
                i++;
            }
            else
                sb.Append(ch);
        }
        return sb.ToString();
    }
}
