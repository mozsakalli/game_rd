using System;
using System.Collections.Generic;
using System.Reflection;

namespace DigitoyEngine;

// "Animate everything" temeli: motorun TEK animasyon ilkeli PropertyBinding'dir.
//   AnimProperty = (sahip tip, yol) → tipli Get/Set (boxing yok, reflection yok)
//   AnimValue    = 4 float + 1 ref: her animatable degerin ortak tasiyicisi
// Tween de MovieClip de bunun uzerinden yazar; ozel kanal/placement kodu YOK.
// Kaynaklar (hepsi ayni tabloya girer, cagiran farki bilmez):
//   - her serilesen sayisal/vektor/renk/bool/enum/asset alani (ic ice yollar dahil: "Fill.Color")
//   - [Animatable] property/alan (serilesmeyen runtime degerleri)
//   - [Animatable] parametresiz metod → Trigger (frame'e key = cagri)
//   - builtin pseudo: Transform.Position/Rotation/Scale/Active, <her component>.Enabled
// Cozumleme (string → AnimProperty) yalniz Rebuild/Start aninda; sicak yolda delegate cagrisi.
// Erisimciler Reflect kabugundan (editor/.NET: expression-compile; AOT: offset intrinsic'i) — tek kod yolu.

public enum AnimKind : byte
{
    Float, Int, Bool, Enum,  // skaler (X)
    Vec2, Vec3, Vec4,        // X..W
    Color,                   // X=r Y=g Z=b W=a (0..255 float)
    Ref,                     // Ref (IAsset vb.; step)
    Trigger,                 // deger yok: Set = cagri
}

// Serilesmeyen property/alanlari ve tetik metodlari animasyona acar.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method)]
public sealed class AnimatableAttribute : Attribute
{
}

public struct AnimValue
{
    public float X, Y, Z, W;
    public object Ref;

    public AnimValue(float x, float y = 0f, float z = 0f, float w = 0f) { X = x; Y = y; Z = z; W = w; Ref = null; }

    public static AnimValue FromFloat(float v) => new(v);
    public static AnimValue FromInt(int v) => new(v);
    public static AnimValue FromBool(bool v) => new(v ? 1f : 0f);
    public static AnimValue FromVec2(Vec2 v) => new(v.x, v.y);
    public static AnimValue FromVec3(Vec3 v) => new(v.x, v.y, v.z);
    public static AnimValue FromVec4(Vec4 v) => new(v.x, v.y, v.z, v.w);
    public static AnimValue FromColor(Color c) => new(c.r, c.g, c.b, c.a);
    public static AnimValue FromRef(object o) => new() { Ref = o };

    public readonly float ToFloat() => X;
    public readonly int ToInt() => (int)MathF.Round(X);
    public readonly bool ToBool() => X != 0f;
    public readonly Vec2 ToVec2() => new(X, Y);
    public readonly Vec3 ToVec3() => new(X, Y, Z);
    public readonly Vec4 ToVec4() => new(X, Y, Z, W);
    public readonly Color ToColor() => new(B(X), B(Y), B(Z), B(W));
    static byte B(float v) => (byte)Math.Clamp((int)(v + 0.5f), 0, 255);

    // Tur bilincli interpolasyon: step turler t<1 iken a'da kalir (Hold semantigi).
    public static AnimValue Lerp(in AnimValue a, in AnimValue b, float t, AnimKind kind)
    {
        switch (kind)
        {
            case AnimKind.Bool:
            case AnimKind.Enum:
            case AnimKind.Ref:
            case AnimKind.Trigger:
                return t >= 1f ? b : a;
            default:
                return new AnimValue(
                    a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t,
                    a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t) { Ref = t >= 1f ? b.Ref : a.Ref };
        }
    }

    // Kanal maskesi (bit0=X..bit3=W): maskelenmemis kanallar cur'dan korunur.
    public static AnimValue Merge(in AnimValue cur, in AnimValue v, byte mask)
    {
        var r = cur;
        if ((mask & 1) != 0) r.X = v.X;
        if ((mask & 2) != 0) r.Y = v.Y;
        if ((mask & 4) != 0) r.Z = v.Z;
        if ((mask & 8) != 0) r.W = v.W;
        r.Ref = v.Ref;
        return r;
    }

    public readonly bool Same(in AnimValue o)
        => X == o.X && Y == o.Y && Z == o.Z && W == o.W && ReferenceEquals(Ref, o.Ref);

    public const byte MaskX = 1, MaskY = 2, MaskZ = 4, MaskW = 8, MaskXY = 3, MaskXYZ = 7, MaskAll = 15;
}

public sealed class AnimProperty
{
    public string Path;      // "Width", "Fill.Color", "Position", "Emit"
    public AnimKind Kind;
    public Type OwnerType;   // Component tipi (Transform dahil)
    public Type ValueType;   // Ref/Enum icin somut tip (editor alan cizimi)
    public Func<Component, AnimValue> Get;   // Trigger: null
    public Action<Component, AnimValue> Set; // Trigger: cagri (deger yok sayilir)

    public override string ToString() => OwnerType?.Name + "." + Path;
}

public static class AnimRegistry
{
    // Transform katalogda yok (ozel tip) — pseudo tablo statik: oyun tipi tutmaz, reload-guvenli.
    static readonly AnimProperty[] _transform =
    {
        new() { Path = "Position", Kind = AnimKind.Vec3, OwnerType = typeof(Transform), ValueType = typeof(Vec3),
            Get = c => AnimValue.FromVec3(((Transform)c).localPosition),
            Set = (c, v) => ((Transform)c).localPosition = v.ToVec3() },
        new() { Path = "Rotation", Kind = AnimKind.Vec3, OwnerType = typeof(Transform), ValueType = typeof(Vec3),
            Get = c => AnimValue.FromVec3(((Transform)c).localEulerAngles),
            Set = (c, v) => ((Transform)c).localEulerAngles = v.ToVec3() },
        new() { Path = "Scale", Kind = AnimKind.Vec3, OwnerType = typeof(Transform), ValueType = typeof(Vec3),
            Get = c => AnimValue.FromVec3(((Transform)c).localScale),
            Set = (c, v) => ((Transform)c).localScale = v.ToVec3() },
        new() { Path = "Active", Kind = AnimKind.Bool, OwnerType = typeof(Transform), ValueType = typeof(bool),
            Get = c => AnimValue.FromBool(c.gameObject.activeSelf),
            Set = (c, v) => c.gameObject.SetActive(v.ToBool()) },
    };

    public static readonly AnimProperty Enabled = new()
    {
        Path = "Enabled", Kind = AnimKind.Bool, OwnerType = typeof(Component), ValueType = typeof(bool),
        Get = c => AnimValue.FromBool(c.enabled),
        Set = (c, v) => c.enabled = v.ToBool(),
    };

    public static AnimProperty TransformPosition => _transform[0];
    public static AnimProperty TransformRotation => _transform[1];
    public static AnimProperty TransformScale => _transform[2];
    public static AnimProperty TransformActive => _transform[3];

    // Tipin animatable listesi: Transform → pseudo; diger → katalog Entry.Anim
    // (ilk istekte reflection'dan kurulur, Reflect kabugu) + Enabled.
    public static AnimProperty[] PropsOf(TypeCatalog catalog, Type type)
    {
        if (type == typeof(Transform))
            return _transform;
        var e = catalog?.Find(type);
        if (e == null)
            return Array.Empty<AnimProperty>();
        return e.Anim ??= BuildReflective(type, e.Schema);
    }

    public static AnimProperty Find(TypeCatalog catalog, Type type, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (type != typeof(Transform) && path == "Enabled")
            return Enabled;
        var props = PropsOf(catalog, type);
        for (int i = 0; i < props.Length; i++)
            if (string.Equals(props[i].Path, path, StringComparison.Ordinal))
                return props[i];
        return null;
    }

    public static bool TryKind(Type t, out AnimKind kind)
    {
        if (t == typeof(float)) { kind = AnimKind.Float; return true; }
        if (t == typeof(int)) { kind = AnimKind.Int; return true; }
        if (t == typeof(bool)) { kind = AnimKind.Bool; return true; }
        if (t.IsEnum) { kind = AnimKind.Enum; return true; }
        if (t == typeof(Vec2)) { kind = AnimKind.Vec2; return true; }
        if (t == typeof(Vec3)) { kind = AnimKind.Vec3; return true; }
        if (t == typeof(Vec4)) { kind = AnimKind.Vec4; return true; }
        if (t == typeof(Color)) { kind = AnimKind.Color; return true; }
        if (typeof(IAsset).IsAssignableFrom(t)) { kind = AnimKind.Ref; return true; }
        kind = default;
        return false;
    }

    // --- Reflection yolu (editor + .NET player + AOT; Reflect kabugu): sema alanlari + [Animatable] uyeler ---
    // Tipli erisimciler kurulumda bir kez olusur; sicak yolda delegate cagrisi, boxing yok.

    static AnimProperty[] BuildReflective(Type type, SerializedType.FieldSchema[] schema)
    {
        var list = new List<AnimProperty>();
        if (schema != null)
            Walk(schema, "", new List<FieldInfo>(), type, list, 0);
        foreach (var pi in Reflect.InstanceProperties(type))
        {
            if (!Reflect.IsAnimatable(pi) || !TryKind(pi.PropertyType, out var k) || !Reflect.HasGetSet(pi))
                continue;
            AddProperty(list, type, pi, k);
        }
        foreach (var fi in Reflect.DeclaredInstanceFields(type))
        {
            if (!Reflect.IsAnimatable(fi) || !TryKind(fi.FieldType, out var k))
                continue;
            if (Exists(list, fi.Name))
                continue; // serilesen alan zaten listede
            AddField(list, type, fi.Name, k, new[] { fi });
        }
        foreach (var mi in Reflect.InstanceMethods(type))
        {
            if (!Reflect.IsAnimatable(mi) || !Reflect.IsParameterlessVoid(mi))
                continue;
            var call = Reflect.MethodCaller(mi);
            list.Add(new AnimProperty { Path = mi.Name, Kind = AnimKind.Trigger, OwnerType = type, ValueType = typeof(void), Set = (c, _) => call(c) });
        }
        return list.ToArray();
    }

    static bool Exists(List<AnimProperty> list, string path)
    {
        foreach (var p in list)
            if (p.Path == path) return true;
        return false;
    }

    static void Walk(SerializedType.FieldSchema[] schema, string prefix, List<FieldInfo> chain, Type ownerType,
        List<AnimProperty> list, int depth)
    {
        foreach (var f in schema)
        {
            string path = prefix + f.Name;
            var fi = f.Info ?? Reflect.FindField(f.DeclaringType, f.Name);
            if (fi == null)
                continue;
            chain.Add(fi);
            if (f.Kind == SerializedType.Kind.Object && f.Nested != null && depth < 6)
                Walk(f.Nested, path + ".", chain, ownerType, list, depth + 1);
            else if (TryKind(fi.FieldType, out var k))
                AddField(list, ownerType, path, k, chain.ToArray());
            chain.RemoveAt(chain.Count - 1);
        }
    }

    static void AddField(List<AnimProperty> list, Type ownerType, string path, AnimKind kind, FieldInfo[] chain)
    {
        var valueType = chain[chain.Length - 1].FieldType;
        var p = new AnimProperty { Path = path, Kind = kind, OwnerType = ownerType, ValueType = valueType };
        switch (kind)
        {
            case AnimKind.Float: { var g = Reflect.FieldGetter<float>(chain); var s = Reflect.FieldSetter<float>(chain); p.Get = c => AnimValue.FromFloat(g(c)); p.Set = (c, v) => s(c, v.ToFloat()); break; }
            case AnimKind.Int:
            case AnimKind.Enum: { var g = Reflect.FieldGetter<int>(chain); var s = Reflect.FieldSetter<int>(chain); p.Get = c => AnimValue.FromInt(g(c)); p.Set = (c, v) => s(c, v.ToInt()); break; }
            case AnimKind.Bool: { var g = Reflect.FieldGetter<bool>(chain); var s = Reflect.FieldSetter<bool>(chain); p.Get = c => AnimValue.FromBool(g(c)); p.Set = (c, v) => s(c, v.ToBool()); break; }
            case AnimKind.Vec2: { var g = Reflect.FieldGetter<Vec2>(chain); var s = Reflect.FieldSetter<Vec2>(chain); p.Get = c => AnimValue.FromVec2(g(c)); p.Set = (c, v) => s(c, v.ToVec2()); break; }
            case AnimKind.Vec3: { var g = Reflect.FieldGetter<Vec3>(chain); var s = Reflect.FieldSetter<Vec3>(chain); p.Get = c => AnimValue.FromVec3(g(c)); p.Set = (c, v) => s(c, v.ToVec3()); break; }
            case AnimKind.Vec4: { var g = Reflect.FieldGetter<Vec4>(chain); var s = Reflect.FieldSetter<Vec4>(chain); p.Get = c => AnimValue.FromVec4(g(c)); p.Set = (c, v) => s(c, v.ToVec4()); break; }
            case AnimKind.Color: { var g = Reflect.FieldGetter<Color>(chain); var s = Reflect.FieldSetter<Color>(chain); p.Get = c => AnimValue.FromColor(g(c)); p.Set = (c, v) => s(c, v.ToColor()); break; }
            case AnimKind.Ref: { var g = Reflect.FieldGetter<object>(chain); var s = Reflect.FieldSetter<object>(chain); p.Get = c => AnimValue.FromRef(g(c)); p.Set = (c, v) => s(c, v.Ref); break; }
            default: return;
        }
        list.Add(p);
    }

    static void AddProperty(List<AnimProperty> list, Type ownerType, PropertyInfo pi, AnimKind kind)
    {
        var p = new AnimProperty { Path = pi.Name, Kind = kind, OwnerType = ownerType, ValueType = pi.PropertyType };
        switch (kind)
        {
            case AnimKind.Float: { var g = Reflect.PropertyGetter<float>(pi); var s = Reflect.PropertySetter<float>(pi); p.Get = c => AnimValue.FromFloat(g(c)); p.Set = (c, v) => s(c, v.ToFloat()); break; }
            case AnimKind.Int:
            case AnimKind.Enum: { var g = Reflect.PropertyGetter<int>(pi); var s = Reflect.PropertySetter<int>(pi); p.Get = c => AnimValue.FromInt(g(c)); p.Set = (c, v) => s(c, v.ToInt()); break; }
            case AnimKind.Bool: { var g = Reflect.PropertyGetter<bool>(pi); var s = Reflect.PropertySetter<bool>(pi); p.Get = c => AnimValue.FromBool(g(c)); p.Set = (c, v) => s(c, v.ToBool()); break; }
            case AnimKind.Vec2: { var g = Reflect.PropertyGetter<Vec2>(pi); var s = Reflect.PropertySetter<Vec2>(pi); p.Get = c => AnimValue.FromVec2(g(c)); p.Set = (c, v) => s(c, v.ToVec2()); break; }
            case AnimKind.Vec3: { var g = Reflect.PropertyGetter<Vec3>(pi); var s = Reflect.PropertySetter<Vec3>(pi); p.Get = c => AnimValue.FromVec3(g(c)); p.Set = (c, v) => s(c, v.ToVec3()); break; }
            case AnimKind.Vec4: { var g = Reflect.PropertyGetter<Vec4>(pi); var s = Reflect.PropertySetter<Vec4>(pi); p.Get = c => AnimValue.FromVec4(g(c)); p.Set = (c, v) => s(c, v.ToVec4()); break; }
            case AnimKind.Color: { var g = Reflect.PropertyGetter<Color>(pi); var s = Reflect.PropertySetter<Color>(pi); p.Get = c => AnimValue.FromColor(g(c)); p.Set = (c, v) => s(c, v.ToColor()); break; }
            case AnimKind.Ref: { var g = Reflect.PropertyGetter<object>(pi); var s = Reflect.PropertySetter<object>(pi); p.Get = c => AnimValue.FromRef(g(c)); p.Set = (c, v) => s(c, v.Ref); break; }
            default: return;
        }
        list.Add(p);
    }
}