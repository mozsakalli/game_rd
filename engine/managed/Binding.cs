using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace DigitoyEngine;

// VERI BAGLAMA (pull modeli) — docs/engine-notes.md "BINDING".
//   Kaynak = duz C# model ([Bindable] sinif: public alanlar + [Bindable] property'ler; ic ice yollar "A.B")
//            YA DA IDictionary<string,object> (anahtar zinciri; deger turu calisma zamaninda).
//   Hedef  = component + yol: sema alanlari (ic ice "Fill.Color"), public get/set property'ler, pseudo "Active"/"Enabled".
//   Yayilim: abonelik YOK. Her frame Scene'in Sync fazinda BindingSystem aktif Binding'leri gezer: degeri okur
//   (BindValue, alloc'suz; long kayipsiz), onceki degerle kiyaslar, farkliysa hedefe yazar; sweep sonunda dokunulan
//   component basina TEK OnValidate. Model setter'i yok, event yok, codegen yok → sizinti yapisal olarak imkansiz.
//   Animasyon alt sistemiyle ORTAK HICBIR SEY YOK (AnimValue/AnimKind/AnimRegistry kullanilmaz): iki konu bagimsiz.
//   Model cozumlemesi: hiyerarside yukari ilk BindingContext (Source "Total"), ya da "$Ad.Total" = sahne globali.

// Model sinifi: binding kaynagi. Property'de: getter binding'e acilir (computed deger).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property)]
public sealed class BindableAttribute : Attribute
{
}

public enum BindType : byte
{
    Null,     // deger yok (sozluk anahtari eksik / null referans): yazilmaz, son deger kalir
    Int,      // I  (int/long/short/byte/enum — long kayipsiz)
    Float,    // F  (float/double)
    Bool,     // I  (0/1)
    String,   // Ref (icerik kiyasi)
    Vec2, Vec3, Vec4, Color, // V
    Object,   // Ref (referans kiyasi; asset, liste, sinif)
}

// Alloc'suz deger tasiyici: kaynak okur, Binding son degerle kiyaslar, hedef yazicisi tipli alir.
public struct BindValue
{
    public BindType Type;
    public long I;
    public double F;
    public Vec4 V;
    public object Ref;

    public static BindValue Null => default;
    public static BindValue Int(long v) => new() { Type = BindType.Int, I = v };
    public static BindValue Float(double v) => new() { Type = BindType.Float, F = v };
    public static BindValue Bool(bool v) => new() { Type = BindType.Bool, I = v ? 1 : 0 };
    public static BindValue Str(string v) => v == null ? Null : new() { Type = BindType.String, Ref = v };
    public static BindValue Vec(Vec2 v) => new() { Type = BindType.Vec2, V = new Vec4(v.x, v.y, 0, 0) };
    public static BindValue Vec(Vec3 v) => new() { Type = BindType.Vec3, V = new Vec4(v.x, v.y, v.z, 0) };
    public static BindValue Vec(Vec4 v) => new() { Type = BindType.Vec4, V = v };
    public static BindValue Col(Color c) => new() { Type = BindType.Color, V = new Vec4(c.r, c.g, c.b, c.a) };
    public static BindValue Obj(object o) => o == null ? Null : new() { Type = BindType.Object, Ref = o };

    // Kutulu deger (sozluk modeli): calisma zamani turune gore. Unbox alloc yapmaz.
    public static BindValue From(object o)
    {
        switch (o)
        {
            case null: return Null;
            case string s: return Str(s);
            case int i: return Int(i);
            case long l: return Int(l);
            case bool b: return Bool(b);
            case float f: return Float(f);
            case double d: return Float(d);
            case short sh: return Int(sh);
            case byte by: return Int(by);
            case uint ui: return Int(ui);
#if !DE_AOT
            case decimal m: return Float((double)m); // corelib'de decimal yok
#endif
            case Vec2 v2: return Vec(v2);
            case Vec3 v3: return Vec(v3);
            case Vec4 v4: return Vec(v4);
            case Color c: return Col(c);
            default:
                if (o is Enum) return Int(Convert.ToInt64(o));
                return Obj(o);
        }
    }

    // Sayi -> string invariant. DE_AOT: corelib'de CultureInfo yok; ToString() zaten invariant + shortest-roundtrip (PlayerPrefs ile ayni).
#if DE_AOT
    static string FmtI(long v) => v.ToString();
    static string FmtF(double v) => v.ToString();
#else
    static string FmtI(long v) => v.ToString(CultureInfo.InvariantCulture);
    static string FmtF(double v) => v.ToString("R", CultureInfo.InvariantCulture);
#endif

    public readonly bool Same(in BindValue o)
    {
        if (Type != o.Type) return false;
        switch (Type)
        {
            case BindType.Null: return true;
            case BindType.Int:
            case BindType.Bool: return I == o.I;
            case BindType.Float: return F == o.F;
            case BindType.String: return string.Equals((string)Ref, (string)o.Ref, StringComparison.Ordinal);
            case BindType.Vec2: case BindType.Vec3: case BindType.Vec4: case BindType.Color:
                return V.x == o.V.x && V.y == o.V.y && V.z == o.V.z && V.w == o.V.w;
            default: return ReferenceEquals(Ref, o.Ref);
        }
    }

    public readonly Vec2 ToVec2() => new(V.x, V.y);
    public readonly Vec3 ToVec3() => new(V.x, V.y, V.z);
    public readonly Color ToColor() => new((byte)V.x, (byte)V.y, (byte)V.z, (byte)V.w);

    // Hedef turune donusum: sayi↔sayi↔bool, sayi/bool→string (invariant); string/vektor/referans yalniz kendine.
    // Yalniz degisiklikte cagrilir (string uretimi o zaman alloc eder, kabul).
    public readonly bool TryConvert(BindType dst, out BindValue r)
    {
        r = this;
        if (dst == Type) return true;
        switch (dst)
        {
            case BindType.Int:
                if (Type == BindType.Float) { r = Int((long)F); return true; }
                if (Type == BindType.Bool) { r = Int(I); return true; }
                return false;
            case BindType.Float:
                if (Type == BindType.Int || Type == BindType.Bool) { r = Float(I); return true; }
                return false;
            case BindType.Bool:
                if (Type == BindType.Int) { r = Bool(I != 0); return true; }
                if (Type == BindType.Float) { r = Bool(F != 0); return true; }
                return false;
            case BindType.String:
                if (Type == BindType.Int) { r = Str(FmtI(I)); return true; }
                if (Type == BindType.Float) { r = Str(FmtF(F)); return true; }
                if (Type == BindType.Bool) { r = Str(I != 0 ? "true" : "false"); return true; }
                return false;
            default:
                return false;
        }
    }

    public override readonly string ToString() => Type switch
    {
        BindType.Int => FmtI(I),
        BindType.Float => FmtF(F),
        BindType.Bool => I != 0 ? "true" : "false",
        BindType.String => (string)Ref,
        BindType.Null => "null",
        _ => Ref?.ToString() ?? V.ToString(),
    };
}

// (model tipi, yol) → tipli okuyucu. Kurulum bir kez (reflection), sicak yolda delegate.
public sealed class BindSource
{
    public string Path;
    public BindType Type;        // statik tur; Dynamic ise her okumada BindValue.Type gecerli
    public Type OwnerType;
    public Type ValueType;
    public bool IsList;          // IList kaynagi (ListBinding icin); Binding bunu atlar
    public bool Dynamic;         // sozluk modeli: tur/liste olup olmadigi okumada belli olur
    public Func<object, BindValue> Get;
    public override string ToString() => OwnerType?.Name + "." + Path;
}

// (component tipi, yol) → tipli yazici. Sema alanlari (ic ice), public get/set property'ler, pseudo Active/Enabled.
public sealed class BindTarget
{
    public string Path;
    public BindType Type;
    public Type OwnerType;
    public Type ValueType;
    public bool Pseudo;          // Active/Enabled: OnValidate bildirimi yok
    public Action<Component, BindValue> Set;
    public override string ToString() => OwnerType?.Name + "." + Path;
}

[Serializable]
public sealed class Bind
{
    public string Source = "";  // model yolu: "Total", "Lobby.Count", "$Global.Total"
    public Component Target;    // prefab-local ref (ClipTrack gibi remap'lenir)
    public string Path = "";    // hedef yolu: "text", "Active", "Fill.Color", "Enabled"
}

// Modeli hiyerarsiye verir. Runtime: SetModel(obj). Edit modu: Model null ise ModelType adindan
// parametresiz ctor ile ORNEK instance (editor onizleme; oyun kodu kosmaz, yalniz ctor).
[ExecuteInEditMode]
[AddComponentMenu("Data/Binding Context")]
public sealed class BindingContext : Component
{
    public string ModelType = ""; // tip adi (Name ya da FullName); yalniz edit-modu ornegi icin

    [NonSerialized] public object Model;
    object _sample;
    string _sampleFor;

    public void SetModel(object model) => Model = model;

    internal object Current(BindingSystem sys)
    {
        if (Model != null)
            return Model;
        var sc = _gameObject?._scene;
        if (sc == null || !sc.EditMode || string.IsNullOrEmpty(ModelType))
            return null;
        if (!ReferenceEquals(_sampleFor, ModelType))
        {
            _sampleFor = ModelType;
            _sample = null;
            var t = sys.ResolveModelType(ModelType);
            if (t != null)
            {
                try { _sample = Reflect.NewInstance(t); }
                catch { _sample = null; } // ctor patladi: onizleme yok, hata yok
            }
        }
        return _sample;
    }
}

[ExecuteInEditMode]
[AddComponentMenu("Data/Binding")]
public sealed class Binding : Component
{
    public List<Bind> Binds = new();

    struct Rt
    {
        public int Authoring;        // Binds indeksi
        public string Path;          // model yolu (global ise "$" ve ad soyulmus)
        public string Global;        // null = context
        public BindingContext Ctx;   // cozulmus context (context modunda)
        public object Model;         // son gorulen model (tip degisimi = Src yenile)
        public BindSource Src;
        public Component Target;
        public BindTarget Prop;
        public BindValue Last;
        public bool HasLast;
    }

    Rt[] _rt = Array.Empty<Rt>();
    int _rtCount;
    bool _dirty = true;
    int _globalsSeen = -1;
    BindingSystem _sys;
    internal int _slot = -1;

    // Kod sahnesi API'si: ayni frame'den itibaren gecerli.
    public Bind Add(Component target, string path, string source)
    {
        var b = new Bind { Source = source ?? "", Target = target, Path = path ?? "" };
        Binds.Add(b);
        _dirty = true;
        return b;
    }

    // Hiyerarsi/context degisti: cozumlemeyi yenile (reparent sonrasi cagiran soyler).
    public void Invalidate() => _dirty = true;

    protected internal override void OnEnable()
    {
        _sys = _gameObject._scene.GetSystem<BindingSystem>();
        _sys.Register(this);
        _dirty = true;
    }

    protected internal override void OnDisable()
    {
        _sys?.Unregister(this);
        _sys = null;
    }

    protected internal override void OnValidate() => _dirty = true;

    void Rebuild(BindingSystem sys)
    {
        _dirty = false;
        _globalsSeen = sys.GlobalsVersion;
        if (_rt.Length < Binds.Count)
            _rt = new Rt[Math.Max(4, Binds.Count)];
        _rtCount = 0;
        var catalog = _gameObject._scene.Catalog;
        for (int i = 0; i < Binds.Count; i++)
        {
            var b = Binds[i];
            if (b == null || b.Target == null || b.Target.IsDestroyed || string.IsNullOrEmpty(b.Source) || string.IsNullOrEmpty(b.Path))
                continue;
            var prop = sys.ResolveTarget(catalog, b.Target.GetType(), b.Path);
            if (prop == null)
                continue;
            ref var r = ref _rt[_rtCount];
            r = default;
            r.Authoring = i;
            r.Target = b.Target;
            r.Prop = prop;
            if (b.Source[0] == '$')
            {
                int dot = b.Source.IndexOf('.');
                if (dot < 2 || dot == b.Source.Length - 1)
                    continue; // "$Ad.yol" bicimi degil
                r.Global = b.Source.Substring(1, dot - 1);
                r.Path = b.Source.Substring(dot + 1);
            }
            else
            {
                r.Path = b.Source;
                r.Ctx = FindContext();
                if (r.Ctx == null)
                    continue; // context yok: bu bind bu turda yok sayilir; Invalidate ile yeniden denenir
            }
            _rtCount++;
        }
    }

    BindingContext FindContext()
    {
        for (var t = transform; t != null; t = t.parent)
        {
            var c = t.gameObject.GetComponent<BindingContext>();
            if (c != null && !c.IsDestroyed)
                return c;
        }
        return null;
    }

    internal void Sync(BindingSystem sys)
    {
        if (_dirty || _globalsSeen != sys.GlobalsVersion)
            Rebuild(sys);
        for (int i = 0; i < _rtCount; i++)
        {
            ref var r = ref _rt[i];
            if (r.Target.IsDestroyed)
                continue;
            object model;
            if (r.Global != null)
                model = sys.GetGlobal(r.Global);
            else
            {
                if (r.Ctx.IsDestroyed) { _dirty = true; continue; }
                model = r.Ctx.Current(sys);
            }
            if (model == null)
                continue; // model yok: son yazilan deger kalir
            if (!ReferenceEquals(model, r.Model) || r.Src == null)
            {
                if (r.Model == null || model.GetType() != r.Model.GetType() || r.Src == null)
                    r.Src = sys.Resolve(model.GetType(), r.Path);
                r.Model = model;
                r.HasLast = false; // yeni model: ilk okuma hep yazar
            }
            if (r.Src == null || r.Src.IsList)
                continue;
            var v = r.Src.Get(model);
            if (v.Type == BindType.Null)
                continue;
            if (r.HasLast && v.Same(r.Last))
                continue;
            r.Last = v;
            r.HasLast = true;
            if (!v.TryConvert(r.Prop.Type, out var w))
                continue; // uyumsuz tur: sessiz (kaynak kiyasi yine de guncel; her frame tekrar denemez)
            if (r.Prop.Type == BindType.Object && w.Ref != null && !r.Prop.ValueType.IsInstanceOfType(w.Ref))
                continue;
            r.Prop.Set(r.Target, w);
            if (!r.Prop.Pseudo)
                sys.Touch(r.Target);
        }
    }

    // Editor/teshis: cozulmus bind sayisi.
    public int ResolvedCount => _rtCount;
}

// Sahne-sahipli sistem (GetSystem<BindingSystem>()): aktif Binding listesi, sahne globalleri,
// model kaynak / hedef tablolari (instance cache — statik tip cache yasagi; sahneyle olur, ALC guvenli).
public sealed class BindingSystem : ISceneSystem
{
    Binding[] _active = new Binding[16];
    int _count;
    ListBinding[] _lists = new ListBinding[8];
    int _listCount;
    readonly Dictionary<Type, BindSource[]> _sources = new();
    readonly Dictionary<string, BindSource> _dictSources = new();
    readonly Dictionary<Type, BindTarget[]> _targets = new();
    readonly Dictionary<string, object> _globals = new();
    Dictionary<string, Type> _modelTypes;

    // Sweep'te yazilan component'ler: sonunda tekil OnValidate (kendi dedupe'u; anim bildirimi degil).
    Component[] _touched = new Component[32];
    int _touchedCount;
    readonly HashSet<Component> _touchedSet = new();

    public int ActiveCount => _count;
    public int ListCount => _listCount;
    public int GlobalsVersion { get; private set; }

    // Sahne globali: Source "$Ad.yol". Ayni adla tekrar = degistir (binding'ler yeniden cozer).
    public void SetGlobal(string name, object model)
    {
        if (model == null) _globals.Remove(name);
        else _globals[name] = model;
        GlobalsVersion++;
    }

    public object GetGlobal(string name) => _globals.TryGetValue(name, out var m) ? m : null;

    internal void Register(Binding b)
    {
        if (b._slot >= 0)
            return;
        if (_count == _active.Length) Array.Resize(ref _active, _count * 2);
        b._slot = _count;
        _active[_count++] = b;
    }

    internal void Unregister(Binding b)
    {
        int i = b._slot;
        if (i < 0)
            return;
        int last = --_count;
        _active[i] = _active[last];
        _active[i]._slot = i;
        _active[last] = null;
        b._slot = -1;
    }

    internal void Register(ListBinding b)
    {
        if (b._slot >= 0)
            return;
        if (_listCount == _lists.Length) Array.Resize(ref _lists, _listCount * 2);
        b._slot = _listCount;
        _lists[_listCount++] = b;
    }

    internal void Unregister(ListBinding b)
    {
        int i = b._slot;
        if (i < 0)
            return;
        int last = --_listCount;
        _lists[i] = _lists[last];
        _lists[i]._slot = i;
        _lists[last] = null;
        b._slot = -1;
    }

    internal void Touch(Component c)
    {
        if ((c._flags & LifecycleFlags.Validate) == 0)
            return; // OnValidate override'i yok: bildirim gereksiz
        if (!_touchedSet.Add(c))
            return;
        if (_touchedCount == _touched.Length) Array.Resize(ref _touched, _touchedCount * 2);
        _touched[_touchedCount++] = c;
    }

    public void Sync(Scene scene)
    {
        // Once listeler: dogan gorunumlerin Binding'leri ayni sweep'te (sinir canli okunur) baglanir.
        for (int i = 0; i < _listCount; i++)
        {
            var l = _lists[i];
            if (l.IsDestroyed)
                continue;
            l.Sync(this, scene);
            if (!ReferenceEquals(_lists[i], l)) i--;
        }
        // Sweep icinde eklenen binding ayni turda islenir; silinen (swap) ayni indekste yeniden okunur.
        for (int i = 0; i < _count; i++)
        {
            var b = _active[i];
            if (b.IsDestroyed)
                continue;
            b.Sync(this);
            if (!ReferenceEquals(_active[i], b)) i--; // sweep icinde disable (Active hedefi) → swap-remove: gelen ayni slotu islesin
        }
        // Dokunulan component basina tek OnValidate (yazimlar bitti; turetilmis durum tazelenir).
        int n = _touchedCount;
        for (int i = 0; i < n; i++)
        {
            var c = _touched[i];
            _touched[i] = null;
            if (!c.IsDestroyed)
                c.OnValidate();
        }
        _touchedCount = 0;
        _touchedSet.Clear();
    }

    public void AfterUpdate(Scene scene) { }
    public void BeginRender(Scene scene, RenderQueue queue) { }
    public void EndRender(Scene scene, RenderQueue queue) { }

    // ================= kaynak tablolari =================

    public static bool IsDictionaryModel(Type t) => typeof(IDictionary<string, object>).IsAssignableFrom(t);

    public BindSource[] SourcesOf(Type modelType)
    {
        if (!_sources.TryGetValue(modelType, out var arr))
            _sources[modelType] = arr = IsDictionaryModel(modelType) ? Array.Empty<BindSource>() : Build(modelType);
        return arr;
    }

    public BindSource Resolve(Type modelType, string path)
    {
        if (IsDictionaryModel(modelType))
            return DictSource(path);
        var arr = SourcesOf(modelType);
        for (int i = 0; i < arr.Length; i++)
            if (string.Equals(arr[i].Path, path, StringComparison.Ordinal))
                return arr[i];
        return null;
    }

    // Sozluk modeli: yol = anahtar zinciri ("ME.faceId"); ara dugumler IDictionary<string,object>. Eksik → Null.
    BindSource DictSource(string path)
    {
        if (_dictSources.TryGetValue(path, out var s))
            return s;
        var keys = path.Split('.');
        s = new BindSource { Path = path, Type = BindType.Object, OwnerType = typeof(IDictionary<string, object>), ValueType = typeof(object), Dynamic = true };
        s.Get = o =>
        {
            object cur = o;
            for (int i = 0; i < keys.Length; i++)
            {
                if (cur is not IDictionary<string, object> d || !d.TryGetValue(keys[i], out cur))
                    return BindValue.Null;
            }
            return BindValue.From(cur);
        };
        _dictSources[path] = s;
        return s;
    }

    // Edit-modu ornegi icin tip adi cozumu: yuklu assembly'lerdeki [Bindable] siniflar (Name ve FullName).
    public Type ResolveModelType(string name)
    {
        if (_modelTypes == null)
        {
            _modelTypes = new Dictionary<string, Type>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); } catch { continue; }
                foreach (var t in types)
                    if (t.IsClass && !t.IsAbstract && t.IsDefined(typeof(BindableAttribute), false))
                    {
                        _modelTypes[t.FullName] = t;
                        if (!_modelTypes.ContainsKey(t.Name)) _modelTypes.Add(t.Name, t); // corelib Dictionary'de TryAdd yok
                    }
            }
        }
        return _modelTypes.TryGetValue(name, out var r) ? r : null;
    }

    static BindSource[] Build(Type type)
    {
        var list = new List<BindSource>();
        Walk(type, type, "", new List<FieldInfo>(), list, 0);
        return list.ToArray();
    }

    // Public alanlar (base zinciri dahil) + [Bindable] okunabilir property'ler; ic ice [Bindable]/[Serializable]
    // nesnelere iner (derinlik 6). Koleksiyon = liste kaynagi; taninmayan sinif = Object (referans).
    static void Walk(Type owner, Type type, string prefix, List<FieldInfo> chain, List<BindSource> list, int depth)
    {
        var fields = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            foreach (var fi in Reflect.DeclaredInstanceFields(t))
                if (fi.IsPublic)
                    fields.Add(fi);
        foreach (var fi in fields)
        {
            string path = prefix + fi.Name;
            chain.Add(fi);
            if (TryType(fi.FieldType, out var k))
                AddSource(list, owner, path, k, fi.FieldType, chain.ToArray(), null);
            else if (IsList(fi.FieldType))
                AddList(list, owner, path, fi.FieldType, chain.ToArray(), null);
            else if (depth < 6 && IsNested(fi.FieldType))
                Walk(owner, fi.FieldType, path + ".", chain, list, depth + 1);
            chain.RemoveAt(chain.Count - 1);
        }
        foreach (var pi in Reflect.InstanceProperties(type))
        {
            if (pi.GetMethod == null || IsIndexer(pi) || !pi.IsDefined(typeof(BindableAttribute), false))
                continue;
            if (TryType(pi.PropertyType, out var k))
                AddSource(list, owner, prefix + pi.Name, k, pi.PropertyType, chain.ToArray(), pi);
            else if (IsList(pi.PropertyType))
                AddList(list, owner, prefix + pi.Name, pi.PropertyType, chain.ToArray(), pi);
        }
    }

    static bool IsList(Type t) => t != typeof(string) && typeof(IList).IsAssignableFrom(t);

    // Indexer property (this[...]) binding yolu olamaz. DE_AOT: corelib PropertyInfo'da GetIndexParameters yok;
    // indexer'in metadata adi "Item" (IndexerName ozniteligi motor/oyun kodunda kullanilmaz).
#if DE_AOT
    static bool IsIndexer(PropertyInfo pi) => pi.Name == "Item";
#else
    static bool IsIndexer(PropertyInfo pi) => pi.GetIndexParameters().Length != 0;
#endif

    static void AddList(List<BindSource> list, Type owner, string path, Type valueType, FieldInfo[] chain, PropertyInfo leaf)
    {
        var g = Reflect.ModelGetter<object>(chain, leaf);
        list.Add(new BindSource { Path = path, Type = BindType.Object, IsList = true, OwnerType = owner, ValueType = valueType,
            Get = o => BindValue.Obj(g(o)) });
    }

    static bool IsNested(Type t)
        => !t.IsPrimitive && !t.IsEnum && t != typeof(string) && !t.IsArray
            && !typeof(IEnumerable).IsAssignableFrom(t)
            && !typeof(Component).IsAssignableFrom(t) && t != typeof(GameObject)
            && (t.IsDefined(typeof(BindableAttribute), false) || (t.Attributes & TypeAttributes.Serializable) != 0);

    // Yaprak tur eslemesi (kaynak ve hedef ortak). Ic ice nesne/koleksiyon → false (cagiran karar verir).
    static bool TryType(Type t, out BindType k)
    {
        if (t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(sbyte)
            || t == typeof(ushort) || t == typeof(uint) || t.IsEnum) { k = BindType.Int; return true; }
        if (t == typeof(float) || t == typeof(double)) { k = BindType.Float; return true; }
        if (t == typeof(bool)) { k = BindType.Bool; return true; }
        if (t == typeof(string)) { k = BindType.String; return true; }
        if (t == typeof(Vec2)) { k = BindType.Vec2; return true; }
        if (t == typeof(Vec3)) { k = BindType.Vec3; return true; }
        if (t == typeof(Vec4)) { k = BindType.Vec4; return true; }
        if (t == typeof(Color)) { k = BindType.Color; return true; }
        if (typeof(IAsset).IsAssignableFrom(t) || typeof(Component).IsAssignableFrom(t) || t == typeof(GameObject))
        { k = BindType.Object; return true; }
        k = default;
        return false;
    }

    // Tam tipli getter: AOT'de Ref<T> alan tipiyle birebir olmali (genisletme yok); enum → int.
    static void AddSource(List<BindSource> list, Type owner, string path, BindType kind, Type vt, FieldInfo[] chain, PropertyInfo leaf)
    {
        var s = new BindSource { Path = path, Type = kind, OwnerType = owner, ValueType = vt };
        if (vt == typeof(int) || vt.IsEnum) { var g = Reflect.ModelGetter<int>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(long)) { var g = Reflect.ModelGetter<long>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(short)) { var g = Reflect.ModelGetter<short>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(byte)) { var g = Reflect.ModelGetter<byte>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(sbyte)) { var g = Reflect.ModelGetter<sbyte>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(ushort)) { var g = Reflect.ModelGetter<ushort>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(uint)) { var g = Reflect.ModelGetter<uint>(chain, leaf); s.Get = o => BindValue.Int(g(o)); }
        else if (vt == typeof(float)) { var g = Reflect.ModelGetter<float>(chain, leaf); s.Get = o => BindValue.Float(g(o)); }
        else if (vt == typeof(double)) { var g = Reflect.ModelGetter<double>(chain, leaf); s.Get = o => BindValue.Float(g(o)); }
        else if (vt == typeof(bool)) { var g = Reflect.ModelGetter<bool>(chain, leaf); s.Get = o => BindValue.Bool(g(o)); }
        else if (vt == typeof(string)) { var g = Reflect.ModelGetter<string>(chain, leaf); s.Get = o => BindValue.Str(g(o)); }
        else if (vt == typeof(Vec2)) { var g = Reflect.ModelGetter<Vec2>(chain, leaf); s.Get = o => BindValue.Vec(g(o)); }
        else if (vt == typeof(Vec3)) { var g = Reflect.ModelGetter<Vec3>(chain, leaf); s.Get = o => BindValue.Vec(g(o)); }
        else if (vt == typeof(Vec4)) { var g = Reflect.ModelGetter<Vec4>(chain, leaf); s.Get = o => BindValue.Vec(g(o)); }
        else if (vt == typeof(Color)) { var g = Reflect.ModelGetter<Color>(chain, leaf); s.Get = o => BindValue.Col(g(o)); }
        else { var g = Reflect.ModelGetter<object>(chain, leaf); s.Get = o => BindValue.Obj(g(o)); }
        list.Add(s);
    }

    // ================= hedef tablolari =================

    public BindTarget[] TargetsOf(TypeCatalog catalog, Type type)
    {
        if (!_targets.TryGetValue(type, out var arr))
            _targets[type] = arr = BuildTargets(catalog, type);
        return arr;
    }

    public BindTarget ResolveTarget(TypeCatalog catalog, Type type, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (path == "Active") return _active_;
        if (path == "Enabled") return _enabled_;
        var arr = TargetsOf(catalog, type);
        for (int i = 0; i < arr.Length; i++)
            if (string.Equals(arr[i].Path, path, StringComparison.Ordinal))
                return arr[i];
        return null;
    }

    // Pseudo hedefler: oyun tipi tutmaz, statik guvenli.
    static readonly BindTarget _active_ = new()
    {
        Path = "Active", Type = BindType.Bool, OwnerType = typeof(Component), ValueType = typeof(bool), Pseudo = true,
        Set = (c, v) => c.gameObject.SetActive(v.I != 0),
    };
    static readonly BindTarget _enabled_ = new()
    {
        Path = "Enabled", Type = BindType.Bool, OwnerType = typeof(Component), ValueType = typeof(bool), Pseudo = true,
        Set = (c, v) => c.enabled = v.I != 0,
    };

    // Sema alanlari (katalogdan; yoksa reflection'dan kurulur; ic ice Object yollari "A.B") + semaya girmeyen public
    // alanlar + Component altinda bildirilen public get/set property'ler (ad cakismasinda sema alani kazanir).
    static BindTarget[] BuildTargets(TypeCatalog catalog, Type type)
    {
        var list = new List<BindTarget>();
        var schema = catalog?.Find(type)?.Schema ?? SerializedType.Build(type);
        if (schema != null)
            WalkTargets(schema, "", new List<FieldInfo>(), type, list, 0);
        for (var t = type; t != null && t != typeof(Component) && t != typeof(object); t = t.BaseType)
        {
            // Semaya girmeyen public alanlar (ornegin long: serilesmez ama baglanir).
            foreach (var fi in Reflect.DeclaredInstanceFields(t))
            {
                if (!fi.IsPublic || fi.IsInitOnly || !TryType(fi.FieldType, out var k) || Exists(list, fi.Name))
                    continue;
                AddTarget(list, type, fi.Name, k, fi.FieldType, new[] { fi }, null);
            }
            foreach (var pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsIndexer(pi) || pi.GetMethod == null || pi.SetMethod == null || !pi.SetMethod.IsPublic)
                    continue;
                if (!TryType(pi.PropertyType, out var k) || Exists(list, pi.Name))
                    continue;
                AddTarget(list, type, pi.Name, k, pi.PropertyType, null, pi);
            }
        }
        return list.ToArray();
    }

    static bool Exists(List<BindTarget> list, string path)
    {
        foreach (var p in list)
            if (p.Path == path) return true;
        return false;
    }

    static void WalkTargets(SerializedType.FieldSchema[] schema, string prefix, List<FieldInfo> chain, Type owner, List<BindTarget> list, int depth)
    {
        foreach (var f in schema)
        {
            string path = prefix + f.Name;
            var fi = f.Info ?? Reflect.FindField(f.DeclaringType, f.Name);
            if (fi == null)
                continue;
            chain.Add(fi);
            if (f.Kind == SerializedType.Kind.Object && f.Nested != null && depth < 6)
                WalkTargets(f.Nested, path + ".", chain, owner, list, depth + 1);
            else if (TryType(fi.FieldType, out var k))
                AddTarget(list, owner, path, k, fi.FieldType, chain.ToArray(), null);
            chain.RemoveAt(chain.Count - 1);
        }
    }

    // Tam tipli setter (AOT Ref<T> birebir); BindValue → alan tipi daraltma burada. Donusum (Int→Float vb.)
    // Binding'de TryConvert ile ONCE yapilir: Set hep kendi turunde BindValue alir.
    static void AddTarget(List<BindTarget> list, Type owner, string path, BindType kind, Type vt, FieldInfo[] chain, PropertyInfo pi)
    {
        var t = new BindTarget { Path = path, Type = kind, OwnerType = owner, ValueType = vt };
        if (vt == typeof(int) || vt.IsEnum) { var s = Setter<int>(chain, pi); t.Set = (c, v) => s(c, (int)v.I); }
        else if (vt == typeof(long)) { var s = Setter<long>(chain, pi); t.Set = (c, v) => s(c, v.I); }
        else if (vt == typeof(short)) { var s = Setter<short>(chain, pi); t.Set = (c, v) => s(c, (short)v.I); }
        else if (vt == typeof(byte)) { var s = Setter<byte>(chain, pi); t.Set = (c, v) => s(c, (byte)v.I); }
        else if (vt == typeof(sbyte)) { var s = Setter<sbyte>(chain, pi); t.Set = (c, v) => s(c, (sbyte)v.I); }
        else if (vt == typeof(ushort)) { var s = Setter<ushort>(chain, pi); t.Set = (c, v) => s(c, (ushort)v.I); }
        else if (vt == typeof(uint)) { var s = Setter<uint>(chain, pi); t.Set = (c, v) => s(c, (uint)v.I); }
        else if (vt == typeof(float)) { var s = Setter<float>(chain, pi); t.Set = (c, v) => s(c, (float)v.F); }
        else if (vt == typeof(double)) { var s = Setter<double>(chain, pi); t.Set = (c, v) => s(c, v.F); }
        else if (vt == typeof(bool)) { var s = Setter<bool>(chain, pi); t.Set = (c, v) => s(c, v.I != 0); }
        else if (vt == typeof(string)) { var s = Setter<string>(chain, pi); t.Set = (c, v) => s(c, (string)v.Ref); }
        else if (vt == typeof(Vec2)) { var s = Setter<Vec2>(chain, pi); t.Set = (c, v) => s(c, v.ToVec2()); }
        else if (vt == typeof(Vec3)) { var s = Setter<Vec3>(chain, pi); t.Set = (c, v) => s(c, v.ToVec3()); }
        else if (vt == typeof(Vec4)) { var s = Setter<Vec4>(chain, pi); t.Set = (c, v) => s(c, v.V); }
        else if (vt == typeof(Color)) { var s = Setter<Color>(chain, pi); t.Set = (c, v) => s(c, v.ToColor()); }
        else { var s = Setter<object>(chain, pi); t.Set = (c, v) => s(c, v.Ref); }
        list.Add(t);
    }

    static Action<Component, T> Setter<T>(FieldInfo[] chain, PropertyInfo pi)
        => pi != null ? Reflect.PropertySetter<T>(pi) : Reflect.FieldSetter<T>(chain);
}
