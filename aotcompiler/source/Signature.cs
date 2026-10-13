using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // Uye imzasi karsilastirmasinin TEK yeri (CoreCLR MethodTableBuilder / CompareMethodSigs karsiligi).
    // Tipler intern'li oldugundan (Context.Apply, Primitive.ArrayOf/PointerOf) tip esitligi referans esitligidir.
    // Kullanicilar: vtable override slotu, itable impl eslesmesi, MemberRef -> Code cozumu (CilFrontend.FindCode).
    public static class Signature
    {
        // .NET override kurali: ad + parametre tipleri (+ generic arity). DONUS TIPI DAHIL DEGIL (covariant return serbest).
        // `this` argumani atlanir (static/instance farki da ayrica degerlendirilmez: sanal uyeler zaten instance'tir).
        public static bool Matches(Code a, Code b)
        {
            if (a.SimpleName != b.SimpleName) return false;
            if (a.GenericParameters.Count != b.GenericParameters.Count) return false;
            if (a.ParamCount != b.ParamCount) return false;
            int ao = a.IsStatic ? 0 : 1, bo = b.IsStatic ? 0 : 1;
            for (int i = 0; i < a.ParamCount; i++)
            {
                var pa = a.Arguments[i + ao].Type; var pb = b.Arguments[i + bo].Type;
                if (pa != pb && !SameUnderGenericMethodParams(pa, pb, a.GenericParameters, b.GenericParameters)) return false;
            }
            return true;
        }

        // Iki generic METOT sablonunun parametreleri farkli nesnelerdir (Base<T>.Pair<U>(T,U) vs IntSer.Pair<V>(int,V)):
        // konumsal esleme (U_i == V_i) ile esit sayilir.
        static bool SameUnderGenericMethodParams(Primitive a, Primitive b, List<Primitive> ga, List<Primitive> gb)
        {
            if (ga.Count == 0) return false;
            if (a == null || b == null) return false;
            int ia = ga.IndexOf(a), ib = gb.IndexOf(b);
            if (ia >= 0 || ib >= 0) return ia == ib;
            if (a.Definition != b.Definition || a.Type != b.Type) return false;
            if (a.TypeArguments.Count != b.TypeArguments.Count) return false;
            if (a.TypeArguments.Count > 0)
            {
                for (int i = 0; i < a.TypeArguments.Count; i++)
                    if (!SameUnderGenericMethodParams(a.TypeArguments[i], b.TypeArguments[i], ga, gb)) return false;
                return true;
            }
            if (a.ElementType != null && b.ElementType != null && a.ArrayRank == b.ArrayRank && a.FixedSize == b.FixedSize)
                return SameUnderGenericMethodParams(a.ElementType, b.ElementType, ga, gb);
            return false;
        }

        // Cagri tarafi: basit ad + somut parametre tipleri (MemberRef imzasi) sablonla eslesiyor mu?
        // Generic metotta sablon parametreleri joker: unify ile baglanir (map doldurulur).
        public static bool MatchesCall(Code candidate, string simpleName, IReadOnlyList<Primitive> paramTypes, int genericArity, Dictionary<Primitive, Primitive> map)
        {
            if (candidate.SimpleName != simpleName || candidate.ExplicitInterface != null) return false;
            if (candidate.GenericParameters.Count != genericArity) return false;
            if (candidate.ParamCount != paramTypes.Count) return false;
            int off = candidate.IsStatic ? 0 : 1;
            map?.Clear();
            for (int i = 0; i < paramTypes.Count; i++)
            {
                var t = candidate.Arguments[i + off].Type;
                if (t == paramTypes[i]) continue;
                if (genericArity == 0 || !Unify(t, paramTypes[i], candidate.GenericParameters, map)) return false;
            }
            return true;
        }

        // sablon tipi (generic metot parametreleri joker) somut tiple birlestir
        public static bool Unify(Primitive template, Primitive concrete, List<Primitive> generics, Dictionary<Primitive, Primitive> map)
        {
            if (template == null || concrete == null) return template == concrete;
            if (generics.Contains(template))
            {
                if (map == null) return true;
                return map.TryGetValue(template, out var bound) ? bound == concrete : (map[template] = concrete) != null;
            }
            if (template.TypeArguments.Count > 0 || concrete.TypeArguments.Count > 0)
            {
                if (template.Definition != concrete.Definition || template.TypeArguments.Count != concrete.TypeArguments.Count) return false;
                for (int i = 0; i < template.TypeArguments.Count; i++)
                    if (!Unify(template.TypeArguments[i], concrete.TypeArguments[i], generics, map)) return false;
                return true;
            }
            if (template.Type == PrimitiveType.Array && concrete.Type == PrimitiveType.Array && template.ArrayRank == concrete.ArrayRank)
                return Unify(template.ElementType, concrete.ElementType, generics, map);
            if (template.Type == PrimitiveType.Pointer && concrete.Type == PrimitiveType.Pointer)
                return Unify(template.ElementType, concrete.ElementType, generics, map);
            return template == concrete;
        }
    }
}
