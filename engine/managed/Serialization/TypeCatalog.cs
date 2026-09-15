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
    }

    readonly Dictionary<string, Entry> _byName = new();
    readonly Dictionary<Type, Entry> _byType = new();
    readonly Dictionary<string, string> _aliases = new(); // eski ad -> yeni ad (rename migrasyonu)

    public Entry Find(string name)
    {
        for (int hop = 0; hop < 8 && name != null; hop++)
        {
            if (_byName.TryGetValue(name, out var e))
                return e;
            if (!_aliases.TryGetValue(name, out name))
                return null;
        }
        return null;
    }

    public Entry Find(Type type) => _byType.GetValueOrDefault(type);

    public void RegisterAlias(string oldName, string newName) => _aliases[oldName] = newName;

    // CatalogWriter alias'lari uretilen koda gecirir.
    public IReadOnlyDictionary<string, string> Aliases => _aliases;

    // Editor menuleri icin: kayitli tum tipler (ad sirasiz).
    public Dictionary<string, Entry>.ValueCollection Entries => _byName.Values;

    public void Register(Entry e)
    {
        _byName[e.Name] = e;
        _byType[e.Type] = e;
    }

    public static TypeCatalog FromReflection()
        => FromAssemblies(AppDomain.CurrentDomain.GetAssemblies());

    // Editor bunu kullanir: yalniz bilinen assembly'ler (unload edilmis eski oyun
    // assembly'si AppDomain listesinde gorunup ad cakismasi yaratmasin).
    public static TypeCatalog FromAssemblies(params System.Reflection.Assembly[] assemblies)
    {
        var cat = new TypeCatalog();
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
            {
                if (t.IsAbstract || !typeof(Component).IsAssignableFrom(t) || t == typeof(Transform))
                    continue;
                cat.RegisterReflective(t);
            }
        }
        return cat;
    }

    // Tek tipin reflection-tabanli kaydi. Source generator da erisemedigi tipler
    // (private nested) icin bunu cagirir — davranis FromAssemblies ile birebir.
    public void RegisterReflective(Type tt)
    {
        // Clone kapsami = serilesme kurali: public VEYA [SerializeField] alanlar
        // (property-backing private alanlar da kopyalansin — LayoutBox deseni).
        var all = tt.GetFields(System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        int keep = 0;
        for (int i = 0; i < all.Length; i++)
            if (all[i].IsPublic || all[i].IsDefined(typeof(SerializeFieldAttribute), false))
                all[keep++] = all[i];
        var fields = all;
        var fieldCount = keep;
        Register(new Entry
        {
            Type = tt,
            Name = tt.Name,
            Create = () => (Component)Activator.CreateInstance(tt, nonPublic: true),
            CopyTo = (src, dst) =>
            {
                for (int i = 0; i < fieldCount; i++)
                    fields[i].SetValue(dst, fields[i].GetValue(src));
            },
            Flags = Component.ComputeFlags(tt),
            Schema = SerializedType.Build(tt),
            Previewable = tt.IsDefined(typeof(PreviewableAttribute), false),
        });
        foreach (MovedFromAttribute moved in tt.GetCustomAttributes(typeof(MovedFromAttribute), false))
            RegisterAlias(moved.OldName, tt.Name);
    }
}
