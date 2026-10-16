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

        // TANIM DUZEYINDE IKAMELI eslesme (CoreCLR MethodTableBuilder: parent imzasi Substitution ile karsilastirilir).
        // `templ` sablon/tanim uyesi; `maps` sahibine giden kalitim zincirinin ikame haritalari (maps[0] en dis/somut baglam,
        // maps[^1] templ'in sahibinin parametreleri). Haritalar bilesiktir: maps[k][T] icindeki parametreler maps[k-1] ile cozulur
        // (Mid<T> : Base<List<T>>, Leaf : Mid<string> -> U := List<T>, T := string). SAF: ornekleme/intern yan etkisi YOK; tipler
        // yalniz nesne grafiyla (tanim + TypeArguments + ElementType) karsilastirilir, ad/dizgi yok.
        public static bool MatchesUnder(Code concrete, Code templ, IReadOnlyList<Dictionary<Primitive, Primitive>> maps)
        {
            if (concrete.SimpleName != templ.SimpleName) return false; // CLI override kimligi: metadata adi + ikameli imza
            if (concrete.GenericParameters.Count != templ.GenericParameters.Count) return false;
            if (concrete.ParamCount != templ.ParamCount) return false;
            int co = concrete.IsStatic ? 0 : 1, to = templ.IsStatic ? 0 : 1;
            for (int i = 0; i < concrete.ParamCount; i++)
                if (!TypeEqualsUnder(concrete.Arguments[i + co].Type, templ.Arguments[i + to].Type, maps, maps.Count - 1, concrete.GenericParameters, templ.GenericParameters))
                    return false;
            return true;
        }

        // a: somut baglamdaki tip; t: sablon tipi, maps[level] ile ikame edilir (ic haritadan disa). Generic METOT parametreleri konumsal.
        public static bool TypeEqualsUnder(Primitive a, Primitive t, IReadOnlyList<Dictionary<Primitive, Primitive>> maps, int level, List<Primitive> ga, List<Primitive> gt)
        {
            if (a == null || t == null) return a == t;
            for (int k = level; k >= 0; k--)
                if (maps[k].TryGetValue(t, out var mapped)) return TypeEqualsUnder(a, mapped, maps, k - 1, ga, gt);
            if (ga != null && ga.Count > 0)
            {
                int ia = ga.IndexOf(a), it = gt.IndexOf(t);
                if (ia >= 0 || it >= 0) return ia == it;
            }
            if (a == t) return true;
            // generic uygulama (acik dugum ya da somut ornek): ayni tanim + argumanlar ciftler halinde
            var da = DefinitionOf(a); var dt = DefinitionOf(t);
            if (da != null || dt != null)
            {
                if (da == null || dt == null || da != dt) return false;
                if (a.TypeArguments.Count != t.TypeArguments.Count) return false;
                for (int i = 0; i < a.TypeArguments.Count; i++)
                    if (!TypeEqualsUnder(a.TypeArguments[i], t.TypeArguments[i], maps, level, ga, gt)) return false;
                return true;
            }
            if (a.ElementType != null && t.ElementType != null)
                return a.Type == t.Type && a.ArrayRank == t.ArrayRank && a.FixedSize == t.FixedSize
                    && TypeEqualsUnder(a.ElementType, t.ElementType, maps, level, ga, gt);
            return false;
        }

        // IKI TARAFLI ikameli eslesme: her iki uye de TANIM duzeyinde (sablon uyesi) olabilir; a tarafi mapsA, t tarafi mapsT
        // zinciriyle ortak (en turemis tipin tanim) uzayina cozulur. ITable impl eslesmesi (sinif zinciri uyesi vs iface slotu) bunu kullanir.
        public static bool MatchesUnder2(Code a, IReadOnlyList<Dictionary<Primitive, Primitive>> mapsA, Code t, IReadOnlyList<Dictionary<Primitive, Primitive>> mapsT)
        {
            if (a.SimpleName != t.SimpleName) return false; // CLI kimligi: metadata adi + ikameli imza
            if (a.GenericParameters.Count != t.GenericParameters.Count) return false;
            if (a.ParamCount != t.ParamCount) return false;
            int ao = a.IsStatic ? 0 : 1, to = t.IsStatic ? 0 : 1;
            for (int i = 0; i < a.ParamCount; i++)
                if (!TypeEquals2(a.Arguments[i + ao].Type, mapsA, mapsA.Count - 1, t.Arguments[i + to].Type, mapsT, mapsT.Count - 1, a.GenericParameters, t.GenericParameters))
                    return false;
            return true;
        }

        public static bool TypeEquals2(Primitive a, IReadOnlyList<Dictionary<Primitive, Primitive>> mapsA, int la,
                                       Primitive t, IReadOnlyList<Dictionary<Primitive, Primitive>> mapsT, int lt,
                                       List<Primitive> ga, List<Primitive> gt)
        {
            if (a == null || t == null) return a == t;
            for (int k = la; k >= 0; k--)
                if (mapsA[k].TryGetValue(a, out var ma)) return TypeEquals2(ma, mapsA, k - 1, t, mapsT, lt, ga, gt);
            for (int k = lt; k >= 0; k--)
                if (mapsT[k].TryGetValue(t, out var mt)) return TypeEquals2(a, mapsA, la, mt, mapsT, k - 1, ga, gt);
            if (ga != null && ga.Count > 0)
            {
                int ia = ga.IndexOf(a), it = gt.IndexOf(t);
                if (ia >= 0 || it >= 0) return ia == it;
            }
            if (a == t) return true;
            var da = DefinitionOf(a); var dt = DefinitionOf(t);
            if (da != null || dt != null)
            {
                if (da == null || dt == null || da != dt) return false;
                if (a.TypeArguments.Count != t.TypeArguments.Count) return false;
                for (int i = 0; i < a.TypeArguments.Count; i++)
                    if (!TypeEquals2(a.TypeArguments[i], mapsA, la, t.TypeArguments[i], mapsT, lt, ga, gt)) return false;
                return true;
            }
            if (a.ElementType != null && t.ElementType != null)
                return a.Type == t.Type && a.ArrayRank == t.ArrayRank && a.FixedSize == t.FixedSize
                    && TypeEquals2(a.ElementType, mapsA, la, t.ElementType, mapsT, lt, ga, gt);
            return false;
        }

        // Generic uygulamanin tanimi: acik dugum (GenericTemplate) ya da somut/canon ornek (InstantiatedFrom); degilse null.
        public static Primitive DefinitionOf(Primitive p) => p.GenericTemplate ?? p.InstantiatedFrom;

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
