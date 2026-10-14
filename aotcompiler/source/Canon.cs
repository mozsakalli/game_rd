using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // CANON GENERICS (docs/engine-notes.md): ref-tip argumanli generic orneklemeleri TEK kod govdesi paylasir (.NET __Canon).
    //  - Canonicalize: exact arguman listesi -> canon bicim (ref -> __Canon; generic struct icine ozyinelemeli; value type exact).
    //  - Markerize: canon govde IL'den cevrilirken generic baglam, __Canon yerine YOL ISARETCILI marker'lar tasir
    //    (__Canon#0, __Canon#1.0 ...). Op.TypeArguments/OpenRef'te saklanan bu acik bicim sozluk slot anahtaridir;
    //    kimlik (Apply/Instantiate) marker'lari tek __Canon'a indirger -> kod/yerlesim tek, exact bilgi sozlukten.
    //  - Demarker: slot anahtari + exact argumanlar -> exact tip/metot (sozluk icerigi).
    // Pozisyonel/yapisal unify yerine yol isaretcisi: Dict<KVP<string,int>,KVP<Foo,int>> gibi ic ice struct argumanlarda
    // iki argumanin canon bicimi ayni oldugundan baska hicbir yontem exact'i geri bulamaz.
    public static class Canon
    {
        public static bool Enabled = System.Environment.GetEnvironmentVariable("AOT_CANON") != "0";

        static readonly Dictionary<string, Primitive> markers = new Dictionary<string, Primitive>();

        public static Primitive Marker(int index, int[] path, bool isMethod)
        {
            var key = (isMethod ? "M" : "T") + index + (path.Length > 0 ? "." + string.Join(".", path) : "");
            if (markers.TryGetValue(key, out var m)) return m;
            m = new Primitive { Name = "__Canon#" + key, DisplayName = "__Canon", Type = PrimitiveType.Model, Parent = Primitive.Object, IsCanon = true, CanonIndex = index, CanonPath = path, CanonIsMethod = isMethod };
            markers[key] = m;
            return m;
        }

        public static bool ContainsCanon(Primitive t)
        {
            if (t == null) return false;
            if (t.IsCanon) return true;
            if (t.ElementType != null && ContainsCanon(t.ElementType)) return true;
            foreach (var a in t.TypeArguments) if (ContainsCanon(a)) return true;
            return false;
        }
        public static bool ContainsCanon(IEnumerable<Primitive> ts) => ts != null && ts.Any(ContainsCanon);
        public static bool ContainsMarker(Primitive t)
        {
            if (t == null) return false;
            if (t.IsCanonMarker) return true;
            if (t.ElementType != null && ContainsMarker(t.ElementType)) return true;
            foreach (var a in t.TypeArguments) if (ContainsMarker(a)) return true;
            return false;
        }

        // Generic ARGUMAN canon bicimi. Ref sekilli (class/iface/delegate/dizi/string/object/marker) -> __Canon;
        // generic struct -> argumanlari canonlanmis struct ornegi; skaler/enum/duz struct -> kendisi.
        public static Primitive Canonicalize(Context ctx, Primitive t)
        {
            if (!Enabled || t == null) return t;
            if (t.IsCanon) return Primitive.Canon;
            switch (TypeShape.Of(t))
            {
                case Shape.Class: case Shape.Interface: case Shape.Delegate: case Shape.Array:
                    return Primitive.Canon;
                case Shape.Struct:
                    {
                        var def = t.Definition;
                        if (def == t || !def.IsGeneric) return t; // generic degil
                        var args = t.TypeArguments;
                        var cargs = new List<Primitive>(args.Count);
                        bool changed = false;
                        foreach (var a in args) { var c = Canonicalize(ctx, a); cargs.Add(c); changed |= c != a; }
                        if (!changed && t.InstantiatedFrom != null) return t;
                        return GenericInstantiator.InstantiatePrimitive(ctx, def, cargs);
                    }
                default: return t;
            }
        }
        public static List<Primitive> Canonicalize(Context ctx, IReadOnlyList<Primitive> args, out bool changed)
        {
            changed = false;
            var r = new List<Primitive>(args.Count);
            foreach (var a in args) { var c = Canonicalize(ctx, a); r.Add(c); changed |= c != a; }
            return r;
        }

        // Canon arguman listesi -> ceviri baglami (marker'li). Yalniz __Canon iceren argumanlar degisir.
        public static List<Primitive> Markerize(Context ctx, IReadOnlyList<Primitive> canonArgs, bool isMethod)
        {
            var r = new List<Primitive>(canonArgs.Count);
            for (int i = 0; i < canonArgs.Count; i++) r.Add(MarkerizeArg(ctx, canonArgs[i], i, new List<int>(), isMethod));
            return r;
        }
        static Primitive MarkerizeArg(Context ctx, Primitive t, int index, List<int> path, bool isMethod)
        {
            if (t == Primitive.Canon) return Marker(index, path.ToArray(), isMethod);
            if (!ContainsCanon(t)) return t;
            if (t.ElementType != null && t.TypeArguments.Count == 0)
            {
                path.Add(-1); var e = MarkerizeArg(ctx, t.ElementType, index, path, isMethod); path.RemoveAt(path.Count - 1);
                return t.Type == PrimitiveType.Array ? Primitive.ArrayOf(e, t.ArrayRank) : t.Type == PrimitiveType.Pointer ? Primitive.PointerOf(e) : Primitive.FixedArrayOf(e, t.FixedSize);
            }
            var args = new List<Primitive>(t.TypeArguments.Count);
            for (int k = 0; k < t.TypeArguments.Count; k++)
            {
                path.Add(k); args.Add(MarkerizeArg(ctx, t.TypeArguments[k], index, path, isMethod)); path.RemoveAt(path.Count - 1);
            }
            return ctx.Apply(t.Definition, args); // acik uygulama dugumu: kimligi marker'larla, somutlanmasi canon ornegine gider
        }

        // Slot anahtari (marker'li acik tip) -> exact tip: marker = exactArgs[index] icindeki yol.
        public static Primitive Demarker(Context ctx, Primitive t, IReadOnlyList<Primitive> typeArgs, IReadOnlyList<Primitive> methodArgs)
        {
            if (t == null) return null;
            if (t.IsCanonMarker)
            {
                var src = t.CanonIsMethod ? methodArgs : typeArgs;
                if (src == null || t.CanonIndex >= src.Count) throw new System.Exception($"canon marker baglam disi: {t.Name}");
                var r = src[t.CanonIndex];
                foreach (var step in t.CanonPath)
                    r = step < 0 ? r.ElementType : r.TypeArguments[step];
                return r;
            }
            if (t == Primitive.Canon) throw new System.Exception("slot anahtarinda ciplak __Canon (marker bekleniyordu)");
            if (!ContainsMarker(t)) return t;
            if (t.ElementType != null && t.TypeArguments.Count == 0)
            {
                var e = Demarker(ctx, t.ElementType, typeArgs, methodArgs);
                return t.Type == PrimitiveType.Array ? Primitive.ArrayOf(e, t.ArrayRank) : t.Type == PrimitiveType.Pointer ? Primitive.PointerOf(e) : Primitive.FixedArrayOf(e, t.FixedSize);
            }
            var args = t.TypeArguments.Select(a => Demarker(ctx, a, typeArgs, methodArgs)).ToList();
            return GenericInstantiator.InstantiatePrimitive(ctx, t.Definition, args);
        }

        // Generic ic ice derinligi (List<List<List<T>>>): sozluk talebi patlamasina korkuluk.
        public static int Depth(Primitive t)
        {
            if (t == null) return 0;
            int d = 0;
            if (t.ElementType != null) d = Depth(t.ElementType);
            foreach (var a in t.TypeArguments) d = System.Math.Max(d, Depth(a));
            return d + (t.TypeArguments.Count > 0 || t.ElementType != null ? 1 : 0);
        }
        public const int MaxDepth = 10;
    }

    public enum DictSlotKind { TypeHandle, TypeDict, MethodDict, FnPtr }

    // Sozluk slotu: anahtar marker'li acik biçim; icerik exact ornek basina Demarker ile cozulur.
    public sealed class DictSlot
    {
        public DictSlotKind Kind;
        public Primitive OpenType;             // TypeHandle/TypeDict: acik tip
        public Code OpenCode;                  // MethodDict/FnPtr: kok sablon metot
        public List<Primitive> OpenTypeArgs;   // MethodDict/FnPtr: [sinif]+[metot] acik argumanlar
        public int Index;
    }

    // Canon sahip (canon tip ya da canon generic metot ornegi) basina slot yerlesimi; append-only (indeksler sabit).
    public sealed class DictLayout
    {
        public object Owner; // Primitive (canon tip) ya da Code (canon generic metot)
        public readonly List<DictSlot> Slots = new List<DictSlot>();
        readonly Dictionary<string, DictSlot> index = new Dictionary<string, DictSlot>();

        static string Key(DictSlotKind kind, Primitive t) => kind + ":" + t.Name;
        static string Key(DictSlotKind kind, Code c, List<Primitive> args) => kind + ":" + c.EncodeName() + "<" + string.Join(",", args.Select(a => a.Name)) + ">";

        public DictSlot Request(DictSlotKind kind, Primitive openType, out bool added)
        {
            var k = Key(kind, openType);
            added = false;
            if (index.TryGetValue(k, out var s)) return s;
            s = new DictSlot { Kind = kind, OpenType = openType, Index = Slots.Count };
            Slots.Add(s); index[k] = s; added = true;
            return s;
        }
        public DictSlot Request(DictSlotKind kind, Code openRoot, List<Primitive> openArgs, out bool added)
        {
            var k = Key(kind, openRoot, openArgs);
            added = false;
            if (index.TryGetValue(k, out var s)) return s;
            s = new DictSlot { Kind = kind, OpenCode = openRoot, OpenTypeArgs = openArgs, Index = Slots.Count };
            Slots.Add(s); index[k] = s; added = true;
            return s;
        }
    }

    // Exact ornegin (tip ya da metot) cozulmus sozluk icerigi: slot sirasiyla hedefler.
    public sealed class DictInstance
    {
        public object Exact;      // Primitive (exact tip) ya da Code (exact generic metot ornegi)
        public DictLayout Layout;
        public readonly List<object> Targets = new List<object>(); // Primitive (TypeHandle/TypeDict) ya da Code (MethodDict/FnPtr)
    }
}
