using System;
using System.Collections.Generic;
using System.Reflection;
#if !DE_AOT
using System.Linq.Expressions;
#endif

namespace DigitoyEngine;

// Motorun reflection'a dokundugu TEK kapi (docs/registry-removal.md Faz 3/4d). AOT corelib .NET reflection yuzeyini
// (BindingFlags secimi, custom attribute'lar, dizi/generic tip sorgulari, Assembly.GetTypes, Activator) birebir sagladigi icin
// buradaki kod .NET ve AOT'ta AYNIDIR. Tek ikili nokta: alanlara tipli (boxing'siz) erisim — .NET'te runtime'da
// kurulabilen GC-free alan erisimcisi yok (expression-compile), AOT'ta FieldInfo.Offset + RefAt<T> intrinsic'i.
// Property/metot delegeleri: .NET expression; AOT simdilik boxed Invoke (Faz 5+: MethodInfo.CreateDelegate).
public static class Reflect
{
    const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // ---- tip sorgulari ----

    // Tipin KENDI bildirdigi instance alanlari (public + private), bildirim sirasinda.
    public static FieldInfo[] DeclaredInstanceFields(Type t) => t.GetFields(Declared);

    // Ada gore instance alan (base zinciri dahil, private dahil).
    public static FieldInfo FindField(Type t, string name)
    {
        for (var tt = t; tt != null; tt = tt.BaseType)
        {
            var f = tt.GetField(name, Declared);
            if (f != null)
                return f;
        }
        return null;
    }

    // Serilesme kurali (Unity): public ve [NonSerialized] degil, YA DA [SerializeField].
    public static bool IsSerialized(FieldInfo fi)
        => (fi.IsPublic && !fi.IsNotSerialized) || fi.IsDefined(typeof(SerializeFieldAttribute), false);

    public static string FormerName(FieldInfo fi) => fi.GetCustomAttribute<FormerlySerializedAsAttribute>(false)?.OldName;

    public static bool IsAnimatable(MemberInfo m) => m.IsDefined(typeof(AnimatableAttribute), false);

    // Inline serilesen duz tip: [Serializable] sinif/struct — motor/Unity nesnesi degil.
    public static bool IsInlineObject(Type t)
        => (t.Attributes & TypeAttributes.Serializable) != 0 && !t.IsAbstract && !t.IsEnum && !t.IsPrimitive
            && t != typeof(string)
            && !typeof(Component).IsAssignableFrom(t)
            && t != typeof(GameObject) && !typeof(IAsset).IsAssignableFrom(t)
            && (t.IsValueType || t.GetConstructor(AllInstance, null, Type.EmptyTypes, null) != null);

    public static bool IsArray(Type t, out Type elem)
    {
        if (t.IsArray) { elem = t.GetElementType(); return true; }
        elem = null;
        return false;
    }

    public static bool IsList(Type t, out Type elem)
    {
        if (t.IsGenericType && !t.IsGenericTypeDefinition && t.GetGenericTypeDefinition() == typeof(List<>))
        { elem = t.GetGenericArguments()[0]; return true; }
        elem = null;
        return false;
    }

    // Tipin kendisi ya da base'i 'method'u Component disinda override etmis mi (lifecycle bayraklari).
    public static bool DeclaresOverride(Type t, string method, Type[] args)
    {
        var m = t.GetMethod(method, AllInstance, null, args, null);
        return m != null && m.DeclaringType != typeof(Component);
    }

    // Verilen assembly'lerdeki somut Component turevleri (Transform haric). AOT: tek sahte assembly = tum program.
    public static List<Type> ComponentTypes(Assembly[] assemblies)
    {
        var result = new List<Type>();
        foreach (var asm in assemblies)
        {
            if (asm == null) continue;
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            foreach (var t in types)
                if (!t.IsAbstract && typeof(Component).IsAssignableFrom(t) && t != typeof(Transform))
                    result.Add(t);
        }
        return result;
    }

    // [MovedFrom] eski tip adlari.
    public static IEnumerable<string> MovedFrom(Type t)
    {
        foreach (var a in t.GetCustomAttributes(typeof(MovedFromAttribute), false))
            yield return ((MovedFromAttribute)a).OldName;
    }

    // ---- kurucular ----

    public static object NewInstance(Type t) => Activator.CreateInstance(t, nonPublic: true);
    public static Array NewArray(Type elem, int n) => Array.CreateInstance(elem, n);

    // ---- boxed erisim (serializer/clone yolu) ----

    public static Func<object, object> Getter(FieldInfo fi) => fi.GetValue;
    public static Action<object, object> Setter(FieldInfo fi) => fi.SetValue;

    // ---- tipli (boxing'siz) erisim: animasyon sicak yolu ----
    // chain: kokten (Component) hedefe alan zinciri; aradaki struct'lar inline, class'lar deref.
    // T alanin tipi ya da donusturulebilir karsiligi (enum -> int, referans -> object).

    public static Func<Component, T> FieldGetter<T>(FieldInfo[] chain)
    {
#if DE_AOT
        var path = AotPath.Build(chain);
        return c => path.Ref<T>(c);
#else
        var c = Expression.Parameter(typeof(Component), "c");
        var member = Member(c, chain);
        Expression body = member.Type == typeof(T) ? member : Expression.Convert(member, typeof(T));
        return Expression.Lambda<Func<Component, T>>(body, c).Compile();
#endif
    }

    public static Action<Component, T> FieldSetter<T>(FieldInfo[] chain)
    {
#if DE_AOT
        var path = AotPath.Build(chain);
        return (c, v) => path.Ref<T>(c) = v;
#else
        var c = Expression.Parameter(typeof(Component), "c");
        var v = Expression.Parameter(typeof(T), "v");
        var member = Member(c, chain);
        Expression value = member.Type == typeof(T) ? v : Expression.Convert(v, member.Type);
        return Expression.Lambda<Action<Component, T>>(Expression.Assign(member, value), c, v).Compile();
#endif
    }

    public static Func<Component, T> PropertyGetter<T>(PropertyInfo pi)
    {
#if DE_AOT
        return c => (T)pi.GetValue(c); // boxed (seyrek: [Animatable] property); Faz 5+: CreateDelegate
#else
        var c = Expression.Parameter(typeof(Component), "c");
        Expression member = Expression.Property(Expression.Convert(c, pi.DeclaringType), pi);
        Expression body = member.Type == typeof(T) ? member : Expression.Convert(member, typeof(T));
        return Expression.Lambda<Func<Component, T>>(body, c).Compile();
#endif
    }

    public static Action<Component, T> PropertySetter<T>(PropertyInfo pi)
    {
#if DE_AOT
        return (c, v) => pi.SetValue(c, v);
#else
        var c = Expression.Parameter(typeof(Component), "c");
        var v = Expression.Parameter(typeof(T), "v");
        Expression member = Expression.Property(Expression.Convert(c, pi.DeclaringType), pi);
        Expression value = member.Type == typeof(T) ? v : Expression.Convert(v, member.Type);
        return Expression.Lambda<Action<Component, T>>(Expression.Assign(member, value), c, v).Compile();
#endif
    }

    public static Action<Component> MethodCaller(MethodInfo mi)
    {
#if DE_AOT
        return c => mi.Invoke(c, null);
#else
        var c = Expression.Parameter(typeof(Component), "c");
        return Expression.Lambda<Action<Component>>(Expression.Call(Expression.Convert(c, mi.DeclaringType), mi), c).Compile();
#endif
    }

    public static PropertyInfo[] InstanceProperties(Type t) => t.GetProperties(AllInstance);
    public static MethodInfo[] InstanceMethods(Type t) => t.GetMethods(AllInstance);
    public static bool HasGetSet(PropertyInfo pi) => pi.GetMethod != null && pi.SetMethod != null;
    public static bool IsParameterlessVoid(MethodInfo mi) => mi.GetParameters().Length == 0 && mi.ReturnType == typeof(void);

#if DE_AOT
    // Alan zinciri -> (class deref'leri, toplam struct offset'i). RefAt: (T*)((char*)obj + off).
    sealed class AotPath
    {
        int[] _derefOff;      // kokten itibaren class-tipli ara alanlarin offset'leri (her biri nesne atlatir)
        int _leafOff;         // son class nesnesi icinde hedefin offset'i (aradaki struct'lar toplanmis)

        public static AotPath Build(FieldInfo[] chain)
        {
            var p = new AotPath();
            var derefOff = new List<int>();
            int acc = 0;
            for (int i = 0; i < chain.Length; i++)
            {
                acc += chain[i].Offset;
                if (i < chain.Length - 1 && !chain[i].FieldType.IsValueType)
                {
                    derefOff.Add(acc);
                    acc = 0;
                }
            }
            p._derefOff = derefOff.ToArray();
            p._leafOff = acc;
            return p;
        }

        public ref T Ref<T>(object root)
        {
            object o = root;
            for (int i = 0; i < _derefOff.Length; i++)
                o = FieldInfo.RefAt<object>(o, _derefOff[i]);
            return ref FieldInfo.RefAt<T>(o, _leafOff);
        }
    }
#else
    static MemberExpression Member(ParameterExpression c, FieldInfo[] chain)
    {
        Expression owner = Expression.Convert(c, chain[0].DeclaringType);
        MemberExpression m = Expression.Field(owner, chain[0]);
        for (int i = 1; i < chain.Length; i++)
            m = Expression.Field(m, chain[i]);
        return m;
    }
#endif
}
