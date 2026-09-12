using System;

namespace DigitoyEngine;

// Unity [CreateAssetMenu] karsiligi: [Serializable] tipin Project panelindeki
// Create menusunde gorunmesi ACIK OPT-IN'dir — her serializable menuye dusmez.
// MenuName '/' ile alt menu kurar ("Config/Game Settings").
[AttributeUsage(AttributeTargets.Class)]
public sealed class CreateAssetMenuAttribute : Attribute
{
    public string MenuName;   // bos: tip adi
    public string FileName;   // bos: tip adi
    public int Order;
}

// Kok nesne serilestirme (Unity ScriptableObject'in veri yarisi): [Serializable]
// isaretli, parametresiz ctor'lu SAF VERI siniflari yaml asset'ine yazilir/okunur.
// Miras/lifecycle sarti yok; davranis isteyen ayri Component yazar. Sahne baglami
// olmadigi icin GoRef/CompRef alanlari atlanir (asset sahne nesnesine referans veremez).
public static class ObjectSerializer
{
    // Asset olabilme kurali: bilincli [Serializable] + new() + Component degil.
    // Create menusunde gorunme AYRI karar: [CreateAssetMenu] ister (IsCreatable).
    public static bool IsAssetType(Type t)
        => t is { IsSerializable: true, IsAbstract: false, IsGenericTypeDefinition: false }
        && t.GetConstructor(Type.EmptyTypes) != null
        && !typeof(Component).IsAssignableFrom(t);

    public static bool IsCreatable(Type t)
        => IsAssetType(t) && t.IsDefined(typeof(CreateAssetMenuAttribute), inherit: false);

    public static DocNode ToNode(object obj, AssetDatabase assets)
    {
        var fields = DocNode.Map();
        foreach (var f in SerializedType.Build(obj.GetType()))
        {
            if (f.Kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
                continue;
            fields.Add(f.Name, SerializedType.WriteField(obj, f, _noGo, _noComp, assets));
        }
        return fields;
    }

    public static void FromNode(object obj, DocNode fields, AssetDatabase assets)
    {
        if (fields?.Fields == null)
            return;
        var schema = SerializedType.Build(obj.GetType());
        foreach (var kv in fields.Fields)
        {
            var f = SerializedType.Find(schema, kv.Key);
            if (f == null || f.Kind is SerializedType.Kind.GoRef or SerializedType.Kind.CompRef)
                continue;
            SerializedType.ReadField(obj, f, kv.Value, assets, IgnoreRef);
        }
    }

    public static void Save(object obj, string path, AssetDatabase assets = null)
    {
        var root = DocNode.Map();
        root.Add("type", DocNode.Scal(obj.GetType().Name));
        root.Add("fields", ToNode(obj, assets));
        System.IO.File.WriteAllText(path, Yaml.Write(root));
    }

    public static T Load<T>(string path, AssetDatabase assets = null) where T : new()
    {
        var obj = new T();
        LoadInto(obj, path, assets);
        return obj;
    }

    public static void LoadInto(object obj, string path, AssetDatabase assets = null)
    {
        var root = Yaml.Parse(System.IO.File.ReadAllText(path));
        FromNode(obj, root.Get("fields") ?? root, assets);
    }

    // Dosyadaki "type:" basligi — editor dogru tipi secip Inspector cizebilsin.
    public static string TypeNameOf(string path)
    {
        try { return Yaml.Parse(System.IO.File.ReadAllText(path)).GetScalar("type", null); }
        catch { return null; }
    }

    static readonly SerializedType.GoEncoder _noGo = _ => "";
    static readonly SerializedType.CompEncoder _noComp = _ => "";
    static void IgnoreRef(SerializedType.Kind k, string v, Action<object> set) { }
}
