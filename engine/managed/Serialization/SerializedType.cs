using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace DigitoyEngine;

// Property modeli: tip basina BIR KEZ reflection (TypeFlags deseniyle ayni felsefe).
// TEK veri modeli — sahne kaydetme, Inspector, Undo ve prefab diff hep bu tablonun
// tuketicisi olacak. Unity kurallari:
//   public alan ([NonSerialized] haric) + [SerializeField] private/protected (base dahil)
//   koleksiyon: T[] ve List<T> (ic ice liste YOK — Unity da desteklemez)
//   ic ice [Serializable] sinif/struct: inline, polimorfizmsiz; derinlik siniri 7
//   SINIR: ic ice STRUCT icinde Go/CompRef yok (boxed kopya vs deferred cozum)
public static class SerializedType
{
    public enum Kind : byte { Float, Int, Bool, String, Enum, Vec2, Vec3, Color, Asset, GoRef, CompRef, List, Object }

    public sealed class FieldSchema
    {
        public string Name;
        public string FormerName;      // [FormerlySerializedAs] eslesmesi
        public FieldInfo Info;
        public Kind Kind;
        public Kind ElementKind;       // Kind.List: eleman turu
        public Type ElementType;       // liste elemani / ic ice nesne tipi
        public FieldSchema[] Nested;   // Kind.Object ya da Object-elemanli liste alt semasi

        // [ShowIf] kosulu (Build'de cozulur): ShowIf = ayni duzeydeki kardes alan,
        // ShowIfScalar = beklenen kanonik deger (null = truthy testi). Inspector
        // gizlerken OLCUM ve CIZIM ayni kosulu kullanmali.
        public FieldSchema ShowIf;
        public string ShowIfScalar;

        // Tiplendirilmis erisimciler (tween/binding/inspector sicak yollari — boxing yok).
        // Editorde expression-compile ile TEMBEL doldurulur; release/AOT'de source
        // generator ayni slotlara duz kod basar.
        public Func<Component, float> GetFloat;
        public Action<Component, float> SetFloat;
    }

    // Float/Int alan icin erisimcileri kurar (alan basina BIR KEZ; cagri ~2-3ns).
    public static void EnsureFloatAccessors(FieldSchema f)
    {
        if (f.SetFloat != null)
            return;
        var comp = System.Linq.Expressions.Expression.Parameter(typeof(Component));
        var val = System.Linq.Expressions.Expression.Parameter(typeof(float));
        var fld = System.Linq.Expressions.Expression.Field(
            System.Linq.Expressions.Expression.Convert(comp, f.Info.DeclaringType), f.Info);
        if (f.Kind == Kind.Int)
        {
            f.SetFloat = System.Linq.Expressions.Expression.Lambda<Action<Component, float>>(
                System.Linq.Expressions.Expression.Assign(fld,
                    System.Linq.Expressions.Expression.Convert(val, typeof(int))), comp, val).Compile();
            f.GetFloat = System.Linq.Expressions.Expression.Lambda<Func<Component, float>>(
                System.Linq.Expressions.Expression.Convert(fld, typeof(float)), comp).Compile();
        }
        else
        {
            f.SetFloat = System.Linq.Expressions.Expression.Lambda<Action<Component, float>>(
                System.Linq.Expressions.Expression.Assign(fld, val), comp, val).Compile();
            f.GetFloat = System.Linq.Expressions.Expression.Lambda<Func<Component, float>>(
                fld, comp).Compile();
        }
    }

    const int MaxDepth = 7;

    // Cache YOK: sonucu TypeCatalog sahiplenir (reload'da katalogla birlikte olur).
    public static FieldSchema[] Build(Type t) => Build(t, 0, insideStruct: false);

    static FieldSchema[] Build(Type t, int depth, bool insideStruct)
    {
        var list = new List<FieldSchema>();
        // Base-once: private [SerializeField] alanlar yalniz bildiren tipte gorunur,
        // hiyerarsi yukaridan asagi yurunur (kararli cikti sirasi).
        var chain = new List<Type>();
        for (var tt = t; tt != null && tt != typeof(Component) && tt != typeof(object) && tt != typeof(ValueType); tt = tt.BaseType)
            chain.Add(tt);
        chain.Reverse();
        foreach (var tt in chain)
        {
            foreach (var fi in tt.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                // NotSerialized/Serializable flag'leri dogrudan okunur. SYSLIB0050
                // yanlis pozitif: uyari BinaryFormatter icin, biz kendi serializer'imizda
                // ayni metadata'yi Unity semantigiyle kullaniyoruz.
#pragma warning disable SYSLIB0050
                bool serialized = (fi.IsPublic && (fi.Attributes & FieldAttributes.NotSerialized) == 0)
                    || fi.GetCustomAttribute<SerializeFieldAttribute>() != null;
#pragma warning restore SYSLIB0050
                if (!serialized)
                    continue;
                var f = BuildField(fi, depth, insideStruct);
                if (f != null)
                    list.Add(f);
            }
        }
        var schema = list.ToArray();
        ResolveShowIf(schema);
        return schema;
    }

    // [ShowIf] kardes referanslari sema kurulduktan sonra cozulur (ileri referans serbest).
    static void ResolveShowIf(FieldSchema[] schema)
    {
        foreach (var f in schema)
        {
            var attr = f.Info.GetCustomAttribute<ShowIfAttribute>();
            if (attr == null)
                continue;
            var sibling = Find(schema, attr.Field);
            if (sibling == null || sibling == f)
                continue; // cozumsuz kosul = hep gorunur (sessiz dusme, crash yok)
            f.ShowIf = sibling;
            f.ShowIfScalar = attr.Value != null ? Format(attr.Value, sibling.Kind) : null;
        }
    }

    // Canli nesne uzerinden kosul (asset inspector / live mod).
    public static bool ShowIfVisible(FieldSchema f, object owner)
    {
        if (f.ShowIf == null || owner == null)
            return true;
        object v = f.ShowIf.Info.GetValue(owner);
        if (f.ShowIfScalar == null && f.ShowIf.Kind is Kind.Asset or Kind.GoRef or Kind.CompRef)
            return v != null;
        return ShowIfMatch(f, Format(v, f.ShowIf.Kind));
    }

    // Kanonik skaler uzerinden kosul (doc modu kardes DocNode.Scalar verir).
    public static bool ShowIfMatch(FieldSchema f, string actualScalar)
    {
        if (f.ShowIf == null)
            return true;
        if (f.ShowIfScalar != null)
            return actualScalar == f.ShowIfScalar;
        return f.ShowIf.Kind switch
        {
            Kind.Bool => actualScalar == "true",
            Kind.Float => float.TryParse(actualScalar, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv) && fv != 0f,
            Kind.Int => int.TryParse(actualScalar, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv) && iv != 0,
            _ => !string.IsNullOrEmpty(actualScalar),
        };
    }

    static FieldSchema BuildField(FieldInfo fi, int depth, bool insideStruct)
    {
        var ft = fi.FieldType;
        FieldSchema f = null;

        if (TryKind(ft, out var k))
        {
            if (insideStruct && k is Kind.GoRef or Kind.CompRef)
                return null; // struct kopya semantigi deferred ref cozumuyle celisir
            f = new FieldSchema { Kind = k };
        }
        else if (ft.IsArray || (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>)))
        {
            var elem = ft.IsArray ? ft.GetElementType() : ft.GetGenericArguments()[0];
            if (TryKind(elem, out var ek))
            {
                if (insideStruct && ek is Kind.GoRef or Kind.CompRef)
                    return null;
                f = new FieldSchema { Kind = Kind.List, ElementKind = ek, ElementType = elem };
            }
            else if (IsInlineObject(elem) && depth < MaxDepth)
            {
                f = new FieldSchema
                {
                    Kind = Kind.List,
                    ElementKind = Kind.Object,
                    ElementType = elem,
                    Nested = Build(elem, depth + 1, insideStruct || elem.IsValueType),
                };
            }
        }
        else if (IsInlineObject(ft) && depth < MaxDepth)
        {
            f = new FieldSchema
            {
                Kind = Kind.Object,
                ElementType = ft,
                Nested = Build(ft, depth + 1, insideStruct || ft.IsValueType),
            };
        }

        if (f == null)
            return null;
        f.Name = fi.Name;
        f.Info = fi;
        f.FormerName = fi.GetCustomAttribute<FormerlySerializedAsAttribute>()?.OldName;
        return f;
    }

    // Inline serilesen duz tip: [Serializable] sinif/struct — motor/Unity nesnesi degil.
    static bool IsInlineObject(Type t)
#pragma warning disable SYSLIB0050 // yanlis pozitif: [Serializable] bayragini kendi serializer'imiz icin okuyoruz
        => (t.Attributes & TypeAttributes.Serializable) != 0 && !t.IsAbstract && !t.IsEnum && !t.IsPrimitive
#pragma warning restore SYSLIB0050
           && t != typeof(string) && t != typeof(decimal)
           && !typeof(Component).IsAssignableFrom(t)
           && t != typeof(GameObject) && !typeof(IAsset).IsAssignableFrom(t)
           && (t.IsValueType || t.GetConstructor(Type.EmptyTypes) != null);

    public static FieldSchema Find(FieldSchema[] schema, string name)
    {
        for (int i = 0; i < schema.Length; i++)
            if (schema[i].Name == name)
                return schema[i];
        for (int i = 0; i < schema.Length; i++)
            if (schema[i].FormerName == name)
                return schema[i]; // [FormerlySerializedAs]: eski adla eslesme
        return null;
    }

    // --- Deger <-> DocNode (agac: liste ve ic ice nesne dahil) ---

    // Referans kodlama baglami SceneDoc'tan gelir (localId haritalari).
    public delegate string GoEncoder(GameObject go);
    public delegate string CompEncoder(Component c);
    // Deferred cozum: tum sahne dogduktan sonra set cagrilir.
    public delegate void RefSink(Kind kind, string val, Action<object> set);

    public static DocNode WriteField(object owner, FieldSchema f, GoEncoder goEnc, CompEncoder compEnc, AssetDatabase assets)
            => WriteValue(f.Info.GetValue(owner), f.Kind, f, goEnc, compEnc, assets);

    static DocNode WriteValue(object v, Kind kind, FieldSchema f, GoEncoder goEnc, CompEncoder compEnc, AssetDatabase assets)
    {
        switch (kind)
        {
            case Kind.GoRef:
                return DocNode.Scal(goEnc((GameObject)v));
            case Kind.CompRef:
                return DocNode.Scal(compEnc((Component)v));
            case Kind.Asset:
                {
                    // GUID varsa guid yazilir (tasima/rename kirmaz); yoksa yol.
                    var name = (v as IAsset)?.Name ?? "";
                    return DocNode.Scal(assets?.PathToGuid(name) ?? name);
                }
            case Kind.List:
                {
                    var seq = DocNode.Seq();
                    if (v is System.Collections.IList list)
                        foreach (var item in list)
                            seq.Items.Add(WriteValue(item, f.ElementKind, f, goEnc, compEnc, assets));
                    return seq;
                }
            case Kind.Object:
                {
                    var map = DocNode.Map();
                    if (v != null && f.Nested != null)
                        foreach (var nf in f.Nested)
                            map.Add(nf.Name, WriteField(v, nf, goEnc, compEnc, assets));
                    return map;
                }
            default:
                return DocNode.Scal(Format(v, kind));
        }
    }

    public static void ReadField(object owner, FieldSchema f, DocNode node, AssetDatabase assets, RefSink defer)
    {
        switch (f.Kind)
        {
            case Kind.GoRef:
            case Kind.CompRef:
                defer(f.Kind, node.Scalar ?? "", v => f.Info.SetValue(owner, v));
                return;
            case Kind.List:
                {
                    if (node.Items == null)
                        return;
                    int count = node.Items.Count;
                    var ft = f.Info.FieldType;
                    if (ft.IsArray)
                    {
                        var arr = Array.CreateInstance(f.ElementType, count);
                        for (int i = 0; i < count; i++)
                            ReadElement(node.Items[i], f, assets, defer, arr, i);
                        f.Info.SetValue(owner, arr);
                    }
                    else
                    {
                        var list = (System.Collections.IList)Activator.CreateInstance(ft);
                        for (int i = 0; i < count; i++)
                        {
                            list.Add(f.ElementType.IsValueType ? Activator.CreateInstance(f.ElementType) : null);
                            ReadElement(node.Items[i], f, assets, defer, list, i);
                        }
                        f.Info.SetValue(owner, list);
                    }
                    return;
                }
            case Kind.Object:
                {
                    if (node.Fields == null)
                        return;
                    object inst = f.Info.GetValue(owner) ?? Activator.CreateInstance(f.ElementType);
                    ReadObjectInto(inst, f.Nested, node, assets, defer);
                    f.Info.SetValue(owner, inst); // struct: boxed kopya geri yazilir
                    return;
                }
            default:
                f.Info.SetValue(owner, Parse(node.Scalar ?? "", f.Kind, f.Info.FieldType, assets));
                return;
        }
    }

    static void ReadElement(DocNode node, FieldSchema f, AssetDatabase assets, RefSink defer,
        object container, int index)
    {
        switch (f.ElementKind)
        {
            case Kind.GoRef:
            case Kind.CompRef:
                defer(f.ElementKind, node.Scalar ?? "", v => SetAt(container, index, v));
                return;
            case Kind.Object:
                {
                    object inst = Activator.CreateInstance(f.ElementType);
                    ReadObjectInto(inst, f.Nested, node, assets, defer);
                    SetAt(container, index, inst);
                    return;
                }
            default:
                SetAt(container, index, Parse(node.Scalar ?? "", f.ElementKind, f.ElementType, assets));
                return;
        }
    }

    static void SetAt(object container, int index, object value)
    {
        if (container is Array arr)
            arr.SetValue(value, index);
        else
            ((System.Collections.IList)container)[index] = value;
    }

    static void ReadObjectInto(object inst, FieldSchema[] schema, DocNode map, AssetDatabase assets, RefSink defer)
    {
        if (schema == null || map.Fields == null)
            return;
        foreach (var kv in map.Fields)
        {
            var nf = Find(schema, kv.Key);
            if (nf != null)
                ReadField(inst, nf, kv.Value, assets, defer);
        }
    }

    static bool TryKind(Type ft, out Kind k)
    {
        if (ft == typeof(float)) { k = Kind.Float; return true; }
        if (ft == typeof(int)) { k = Kind.Int; return true; }
        if (ft == typeof(bool)) { k = Kind.Bool; return true; }
        if (ft == typeof(string)) { k = Kind.String; return true; }
        if (ft.IsEnum) { k = Kind.Enum; return true; }
        if (ft == typeof(Vec2)) { k = Kind.Vec2; return true; }
        if (ft == typeof(Vec3)) { k = Kind.Vec3; return true; }
        if (ft == typeof(Color)) { k = Kind.Color; return true; }
        if (AssetDatabase.IsAssetType(ft)) { k = Kind.Asset; return true; }
        if (ft == typeof(GameObject)) { k = Kind.GoRef; return true; }
        if (typeof(Component).IsAssignableFrom(ft)) { k = Kind.CompRef; return true; }
        k = default;
        return false;
    }

    // --- Kanonik string bicimleri (YAML skalarlariyla birebir) ---

    public static string Format(object v, Kind k) => k switch
    {
        Kind.Float => ((float)v).ToString("R", CultureInfo.InvariantCulture),
        Kind.Int => ((int)v).ToString(CultureInfo.InvariantCulture),
        Kind.Bool => (bool)v ? "true" : "false",
        Kind.String => (string)v ?? "",
        Kind.Enum => v.ToString(),
        Kind.Vec2 => FormatVec2((Vec2)v),
        Kind.Vec3 => FormatVec3((Vec3)v),
        Kind.Color => FormatColor((Color)v),
        Kind.Asset => (v as IAsset)?.Name ?? "", // asset anahtari (AssetDatabase key)
        // Go/CompRef sahne baglami ister (localId haritasi) — SceneDoc ozel isler.
        Kind.GoRef or Kind.CompRef => "",
        _ => "",
    };

    public static object Parse(string s, Kind k, Type fieldType, AssetDatabase assets) => k switch
    {
        Kind.Float => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv) ? fv : 0f,
        Kind.Int => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv) ? iv : 0,
        Kind.Bool => s == "true",
        Kind.String => s,
        Kind.Enum => Enum.TryParse(fieldType, s, out object ev) ? ev : Enum.ToObject(fieldType, 0),
        Kind.Vec2 => ParseVec2(s),
        Kind.Vec3 => ParseVec3(s),
        Kind.Color => ParseColor(s),
        Kind.Asset => string.IsNullOrEmpty(s) ? null : assets?.LoadAsset(s, fieldType),
        _ => null,
    };

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string FormatVec2(Vec2 v) => F(v.x) + " " + F(v.y);
    static string FormatVec3(Vec3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);
    static string FormatColor(Color c) => $"#{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}";

    static Vec2 ParseVec2(string s)
    {
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Vec2(PF(p, 0), PF(p, 1));
    }

    internal static Vec3 ParseVec3(string s)
    {
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return new Vec3(PF(p, 0), PF(p, 1), PF(p, 2));
    }

    static float PF(string[] p, int i)
        => i < p.Length ? float.Parse(p[i], CultureInfo.InvariantCulture) : 0f;

    static Color ParseColor(string s)
    {
        if (s.Length < 9 || s[0] != '#')
            return Color.White;
        return new Color(HexByte(s, 1), HexByte(s, 3), HexByte(s, 5), HexByte(s, 7));
    }

    static byte HexByte(string s, int i)
        => (byte)(HexNib(s[i]) << 4 | HexNib(s[i + 1]));

    static int HexNib(char c)
        => c <= '9' ? c - '0' : (char.ToLowerInvariant(c) - 'a' + 10);
}
