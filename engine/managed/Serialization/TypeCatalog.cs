using System;
using System.Collections.Generic;

namespace DigitoyEngine;

// Tip metaverisinin TEK sahibi — motor assembly'sinde statik Type cache YOK:
// oyun assembly'si reload edilince katalog atilip yeniden kurulur (bayat Type
// koklenmez, AssemblyLoadContext bosalabilir). Editor: FromReflection. Release/
// AOT: source generator ayni Register cagrilarini duz kod olarak uretecek
// (Activator/GetMethod/FieldInfo yok). Katalog yalniz serilestirme yollarindan
// gecer; AddComponent<T> generic hizli yolu (TypeFlags<T>) katalogsuzdur.
public sealed class TypeCatalog
{
    public sealed class Entry
    {
        public Type Type;
        public string Name;
        public Func<Component> Create;
        // Alan kopyalama (Instantiate/clone): editor'de reflection, release'te
        // source-gen duz atamalar. TUM public alanlar shallow kopyalanir
        // (Unity gibi: nesne referanslari by-ref, Texture/Material dahil).
        public Action<Component, Component> CopyTo;
        public LifecycleFlags Flags;
        public SerializedType.FieldSchema[] Schema;
        public bool Previewable; // [Previewable] — Inspector jenerik preview kontrolu
        // Animatable property tablosu (AnimRegistry): uretilmis katalogda dolu gelir,
        // reflection katalogunda ilk istekte expression-compile ile kurulur.
        public AnimProperty[] Anim;
    }

    readonly Dictionary<string, Entry> _byName = new();
    readonly Dictionary<Type, Entry> _byType = new();
    readonly Dictionary<string, string> _aliases = new(); // eski ad -> yeni ad (rename migrasyonu)

    // Dinamik modul kataloglari (docs/modules.md): modul kendi tiplerini CHILD kataloga kaydeder; sahnesi
    // child ile okunur (ad: once modul, sonra host). Host'un Type ile aramalari (Instantiate/CopyTo) child'lara
    // da iner. Unload = child'i zincirden cikarmak; host tablosuna hic yazilmaz, ad cakismasi yok.
    public TypeCatalog Parent { get; }
    readonly List<TypeCatalog> _children = new();

    public TypeCatalog() { }
    public TypeCatalog(TypeCatalog parent)
    {
        Parent = parent;
        parent?._children.Add(this);
    }

    public void Detach()
    {
        Parent?._children.Remove(this);
    }

    public Entry Find(string name)
    {
        for (int hop = 0; hop < 8 && name != null; hop++)
        {
            if (_byName.TryGetValue(name, out var e))
                return e;
            if (!_aliases.TryGetValue(name, out name))
                return Parent?.Find(name);
        }
        return Parent?.Find(name);
    }

    public Entry Find(Type type)
    {
        if (_byType.TryGetValue(type, out var e))
            return e;
        if (Parent != null)
            return Parent.Find(type);
        for (int i = 0; i < _children.Count; i++)
            if (_children[i]._byType.TryGetValue(type, out e))
                return e;
        return null;
    }

    public void RegisterAlias(string oldName, string newName) => _aliases[oldName] = newName;

    // CatalogWriter alias'lari uretilen koda gecirir.
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    // Editor menuleri icin: kayitli tum tipler (ad sirasiz).
    public Dictionary<string, Entry>.ValueCollection Entries => _byName.Values;
    public int Count => _byName.Count;

    public void Register(Entry e)
    {
        _byName[e.Name] = e;
        _byType[e.Type] = e;
    }

    // Reflection katalogu — editor, .NET player ve AOT ayni yol (Reflect kabugu; docs/registry-removal.md).
    public static TypeCatalog FromReflection()
        => FromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

    // Editor bunu kullanir: yalniz bilinen assembly'ler (unload edilmis eski oyun
    // assembly'si AppDomain listesinde gorunup ad cakismasi yaratmasin). AOT: assembly listesi yok sayilir.
    public static TypeCatalog FromAssemblies(params System.Reflection.Assembly[] assemblies)
    {
        var cat = new TypeCatalog();
        foreach (var t in Reflect.ComponentTypes(assemblies))
            cat.RegisterReflective(t);
        return cat;
    }

    // Tek tipin reflection-tabanli kaydi.
    public void RegisterReflective(Type tt)
    {
        // Clone kapsami (Unity Instantiate): TUM public alanlar + [SerializeField] private alanlar, shallow
        // (nesne referanslari by-ref, Texture/Material dahil). Semadan GENIS: sema disi public alanlar da kopyalanir.
        var fields = new List<System.Reflection.FieldInfo>();
        for (var t = tt; t != null && t != typeof(object); t = t.BaseType)
            foreach (var fi in Reflect.DeclaredInstanceFields(t))
                if (fi.IsPublic || Reflect.IsSerialized(fi))
                    fields.Add(fi);
        var copy = fields.ToArray();
        Register(new Entry
        {
            Type = tt,
            Name = tt.Name,
            Create = () => (Component)Reflect.NewInstance(tt),
            CopyTo = (src, dst) =>
            {
                for (int i = 0; i < copy.Length; i++)
                    copy[i].SetValue(dst, copy[i].GetValue(src));
            },
            Flags = Component.ComputeFlags(tt),
            Schema = SerializedType.Build(tt),
#if DE_EDITOR
            Previewable = tt.IsDefined(typeof(PreviewableAttribute), false),
#endif
        });
        foreach (var oldName in Reflect.MovedFrom(tt))
            RegisterAlias(oldName, tt.Name);
    }
}
