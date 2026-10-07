using System;
using System.Collections.Generic;
#if !DE_AOT
using System.Linq.Expressions;
#endif
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
// Editor: expression-compile. Release/AOT: CatalogWriter ayni lambdalari uretir (Entry.Anim).

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
    // (uretilmis ya da ilk istekte reflection'dan kurulur) + Enabled.
    public static AnimProperty[] PropsOf(TypeCatalog catalog, Type type)
    {
        if (type == typeof(Transform))
            return _transform;
        var e = catalog?.Find(type);
        if (e == null)
            return Array.Empty<AnimProperty>();
#if DE_AOT
        return e.Anim ?? Array.Empty<AnimProperty>(); // CatalogWriter uretir; RegisterReflective girisinde bos
#else
        return e.Anim ??= BuildReflective(type, e.Schema);
#endif
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

#if !DE_AOT
    // --- Editor/reflection yolu: sema + [Animatable] → expression-compile ---

    static AnimProperty[] BuildReflective(Type type, SerializedType.FieldSchema[] schema)
    {
        var list = new List<AnimProperty>();
        var c = Expression.Parameter(typeof(Component), "c");
        var v = Expression.Parameter(typeof(AnimValue), "v");
        Expression self = Expression.Convert(c, type);
        if (schema != null)
            Walk(schema, "", self, type, c, v, list, 0);
        foreach (var pi in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!pi.IsDefined(typeof(AnimatableAttribute), false) || !TryKind(pi.PropertyType, out var k))
                continue;
            if (pi.GetMethod == null || pi.SetMethod == null)
                continue;
            Add(list, type, pi.Name, k, pi.PropertyType, Expression.Property(self, pi), c, v);
        }
        foreach (var fi in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!fi.IsDefined(typeof(AnimatableAttribute), false) || !TryKind(fi.FieldType, out var k))
                continue;
            if (Exists(list, fi.Name))
                continue; // serilesen alan zaten listede
            Add(list, type, fi.Name, k, fi.FieldType, Expression.Field(self, fi), c, v);
        }
        foreach (var mi in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!mi.IsDefined(typeof(AnimatableAttribute), false) || mi.GetParameters().Length != 0 || mi.ReturnType != typeof(void))
                continue;
            var call = Expression.Lambda<Action<Component, AnimValue>>(Expression.Call(self, mi), c, v).Compile();
            list.Add(new AnimProperty { Path = mi.Name, Kind = AnimKind.Trigger, OwnerType = type, ValueType = typeof(void), Set = call });
        }
        return list.ToArray();
    }

    static bool Exists(List<AnimProperty> list, string path)
    {
        foreach (var p in list)
            if (p.Path == path) return true;
        return false;
    }

    static void Walk(SerializedType.FieldSchema[] schema, string prefix, Expression owner, Type ownerType,
        ParameterExpression c, ParameterExpression v, List<AnimProperty> list, int depth)
    {
        foreach (var f in schema)
        {
            string path = prefix + f.Name;
            // Uretilmis katalogda Info yok: ad + sahip tipten reflection ile bul (yalniz kurulumda).
            var fi = f.Info ?? f.DeclaringType?.GetField(f.Name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null)
                continue;
            var member = Expression.Field(owner, fi);
            if (f.Kind == SerializedType.Kind.Object && f.Nested != null && depth < 6)
            {
                Walk(f.Nested, path + ".", member, ownerType, c, v, list, depth + 1);
                continue;
            }
            if (!TryKind(fi.FieldType, out var k))
                continue;
            Add(list, ownerType, path, k, fi.FieldType, member, c, v);
        }
    }

    static void Add(List<AnimProperty> list, Type ownerType, string path, AnimKind kind, Type valueType,
        Expression member, ParameterExpression c, ParameterExpression v)
    {
        Expression toValue, fromValue;
        switch (kind)
        {
            case AnimKind.Float: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromFloat), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToFloat), null); break;
            case AnimKind.Int: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromInt), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToInt), null); break;
            case AnimKind.Bool: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromBool), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToBool), null); break;
            case AnimKind.Enum:
                toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromInt), null, Expression.Convert(member, typeof(int)));
                fromValue = Expression.Convert(Expression.Call(v, nameof(AnimValue.ToInt), null), valueType);
                break;
            case AnimKind.Vec2: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromVec2), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToVec2), null); break;
            case AnimKind.Vec3: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromVec3), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToVec3), null); break;
            case AnimKind.Vec4: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromVec4), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToVec4), null); break;
            case AnimKind.Color: toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromColor), null, member); fromValue = Expression.Call(v, nameof(AnimValue.ToColor), null); break;
            case AnimKind.Ref:
                toValue = Expression.Call(typeof(AnimValue), nameof(AnimValue.FromRef), null, Expression.Convert(member, typeof(object)));
                fromValue = Expression.Convert(Expression.Field(v, nameof(AnimValue.Ref)), valueType);
                break;
            default: return;
        }
        list.Add(new AnimProperty
        {
            Path = path, Kind = kind, OwnerType = ownerType, ValueType = valueType,
            Get = Expression.Lambda<Func<Component, AnimValue>>(toValue, c).Compile(),
            Set = Expression.Lambda<Action<Component, AnimValue>>(Expression.Assign(member, fromValue), c, v).Compile(),
        });
    }
#endif
}
