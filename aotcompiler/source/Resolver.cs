using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Baglama pass'i: front-end'in kurdugu grafta kalan ACIK generic uygulamalarini (Box<int> dugumu) somut orneklere
    // cevirir, TypeArguments tasiyan op'lari somut hedefe baglar, etiketleri cozer; sonra kalitim (Hierarchy) ve
    // CallVirtual slotlari. Tum referanslar nesne kimligiyledir; isimle cozulen hicbir sey yoktur.
    public static class Resolver
    {
        public static void ResolveAll(Context ctx, IEnumerable<Code> codes)
        {
            foreach (var primitive in ctx.AllPrimitives.ToList())
            {
                if (primitive.IsGeneric) continue;
                primitive.Parent = Concretize(ctx, primitive.Parent);
                for (int i = 0; i < primitive.Interfaces.Count; i++)
                    primitive.Interfaces[i] = Concretize(ctx, primitive.Interfaces[i]);
            }
            // SABIT NOKTA: monomorphization yeni ornek kodlari (IL'den cevrilmis govdeler) kaydeder; onlar da ayni
            // cozumden gecmeli. Hierarchy de generic sanal metot orneklemesi uretebilir -> hepsi sakinlesene dek dongu.
            var done = new HashSet<Code>();
            while (true)
            {
                var batch = ctx.AllCodes.Where(c => !done.Contains(c)).ToList();
                if (batch.Count > 0)
                {
                    foreach (var code in batch) { done.Add(code); ResolveCodeBody(ctx, code); }
                    continue;
                }
                bool more = FinishHierarchy(ctx);
                // Concretize/Hierarchy yeni kod kaydetmis olabilir (alan tipi somutlamasi, generic sanal ornekleme) -> tekrar tur
                if (!more && ctx.AllCodes.All(done.Contains)) break;
            }
            AssignVirtualSlots(ctx);
        }

        static void ResolveCodeBody(Context ctx, Code code)
        {
            // template kodlar (generic method ya da generic sinif uyesi) SOMUTLASTIRILMAZ: tip referanslari T icerir,
            // ornekleri IL'den somut baglamla cevrilir. Yalniz label.
            bool isTemplate = code.GenericParameters.Count > 0 || (code.Owner != null && code.Owner.IsGeneric);
            if (!isTemplate)
            {
                if (code.ExplicitInterface != null) code.ExplicitInterface = Concretize(ctx, code.ExplicitInterface);
                code.ReturnType = Concretize(ctx, code.ReturnType);
                foreach (var arg in code.Arguments) arg.Type = Concretize(ctx, arg.Type);
                for (int i = 0; i < code.Locals.Count; i++) code.Locals[i] = Concretize(ctx, code.Locals[i]);
            }
            try
            {
                foreach (var op in code.Operations)
                {
                    if (isTemplate) continue; // op-duzeyi somutlastirma ornek cevirisinde

                    // monomorphization: TypeArguments tasiyan op'lar somut hedefe baglanir.
                    // Kural: liste = [sinif argumanlari] + [method argumanlari] (Owner generic ise once sinifinkiler).
                    if (op.TypeArguments.Count > 0)
                    {
                        var resolvedArgs = new List<Primitive>();
                        foreach (var arg in op.TypeArguments)
                            resolvedArgs.Add(Concretize(ctx, arg));
                        if (op.Code != null)
                        {
                            op.Code = GenericInstantiator.ResolveCallTarget(ctx, op.Code, resolvedArgs);
                            if (op.PrimitiveRef != null) op.PrimitiveRef = Concretize(ctx, op.PrimitiveRef); // DelegateNew: delegate tipi
                        }
                        else if (op.PrimitiveRef != null)
                            op.PrimitiveRef = GenericInstantiator.InstantiatePrimitive(ctx, op.PrimitiveRef.Definition, resolvedArgs);
                        if (op.Field != null && op.Field.Owner != null && op.Field.Owner.IsGeneric)
                        {
                            int classArgCount = op.Field.Owner.GenericParameters.Count;
                            var inst = GenericInstantiator.InstantiatePrimitive(ctx, op.Field.Owner, resolvedArgs.GetRange(0, classArgCount));
                            op.Field = FindField(inst, op.Field.Name);
                        }
                        // NOT: TypeArguments temizlenmez — frontend New+Call(ctor) op'lari AYNI listeyi paylasir; kod zaten bir kez islenir.
                    }
                    else if (op.PrimitiveRef != null)
                        op.PrimitiveRef = Concretize(ctx, op.PrimitiveRef); // acik uygulama dugumu (orn. is/as Box<int>) somutlanir
                }
                Linker.LinkLabels(code); // goto hedefleri instruction index'ine cozulur
            }
            catch (System.Exception ex) when (!isTemplate)
            {
                // Eksik generic somutlastirma vb.: tum pipeline'i dusurme, kodu stub'la (CTranspiler tipe uygun sifir doner).
                System.Console.Error.WriteLine($"[resolve-stub] {code.EncodeName()}: {ex.Message}");
                code.UntranslatableReason = $"resolve: {ex.Message}";
                code.Operations.Clear();
            }
        }

        // somut tiplerin alan/parent tiplerindeki acik uygulamalar somutlanir (orn. "class Holder { Box<int> b; }" - Box<int>
        // hic New'lenmese bile tip var olmali), sonra kalitim. Donus: Hierarchy yeni kod ornekledi mi (dongu devam).
        static bool FinishHierarchy(Context ctx)
        {
            foreach (var p in ctx.AllPrimitives.ToList()) // Concretize yeni kayit ekler -> snapshot
            {
                if (p.IsGeneric || p.GenericTemplate != null) continue;
                p.Parent = Concretize(ctx, p.Parent);
                foreach (var f in p.Fields) f.Type = Concretize(ctx, f.Type);
                foreach (var f in p.StaticFields) f.Type = Concretize(ctx, f.Type);
            }
            bool more = Hierarchy.Build(ctx);
            // runtime exception kind -> corlib sinifi eslemesi (DIGITOYENGINE_EX_* indeksleri, WellKnown tablosu)
            for (int k = 1; k < WellKnown.ExceptionKinds.Length; k++)
                if (WellKnown.ExceptionKinds[k] != null && ctx.TryGetPrimitive(WellKnown.ExceptionKinds[k], out var kt))
                    ctx.ExceptionKindTypes[k] = kt;
            return more;
        }

        // CallVirtual slotlari: bildiren metot kendi owner'inin vtable'inda nesne kimligiyle bulunur.
        static void AssignVirtualSlots(Context ctx)
        {
            foreach (var code in ctx.AllCodes)
            {
                if (code.GenericParameters.Count > 0 || (code.Owner != null && code.Owner.IsGeneric))
                    continue; // template: vtable'i yok, klonlari zaten kapsamda
                foreach (var op in code.Operations)
                    if (op.Type == OpType.CallVirtual || (op.Type == OpType.DelegateNew && op.Slot == -2))
                    {
                        if (op.Code.Owner == null)
                            throw new System.Exception($"CallVirtual hedefi Owner'siz: {op.Code.Name}");
                        int slot = op.Code.Owner.VTable.IndexOf(op.Code);
                        if (slot < 0)
                            throw new System.Exception($"CallVirtual hedefi vtable'da yok (virtual isaretli mi?): {op.Code.EncodeName()} (cagiran: {code.EncodeName()}, op TypeArgs=[{string.Join(",", op.TypeArguments.Select(a => a.Name))}], hedef owner generic={op.Code.Owner.IsGeneric} vtable={op.Code.Owner.VTable.Count})");
                        op.Slot = slot;
                    }
            }
        }

        // Acik generic uygulama dugumunu (kapali argumanli) somut ornege cevirir; dizi elemani uzerinden rekursif.
        // Intern sayesinde ayni dugum yerinde ornege donusur; kapali olmayan (T iceren) dugumler oldugu gibi kalir.
        public static Primitive Concretize(Context ctx, Primitive p)
        {
            if (p == null) return null;
            if (p.GenericTemplate != null)
            {
                if (GenericInstantiator.ContainsGenericParameter(p)) return p;
                var args = new List<Primitive>();
                foreach (var a in p.TypeArguments)
                    args.Add(Concretize(ctx, a));
                return GenericInstantiator.InstantiatePrimitive(ctx, p.GenericTemplate, args);
            }
            if (p.ElementType != null && p.ElementType.GenericTemplate != null)
            {
                var elem = Concretize(ctx, p.ElementType);
                if (p.Type == PrimitiveType.Array) return Primitive.ArrayOf(elem, p.ArrayRank);
                if (p.Type == PrimitiveType.Pointer) return Primitive.PointerOf(elem);
                if (p.Type == PrimitiveType.FixedArray) return Primitive.FixedArrayOf(elem, p.FixedSize);
            }
            return p;
        }

        static PrimitiveField FindField(Primitive owner, string name)
        {
            for (var t = owner; t != null; t = t.Parent) // kalitilan alanlar parent'ta
            {
                foreach (var f in t.Fields)
                    if (f.Name == name)
                        return f;
                foreach (var f in t.StaticFields)
                    if (f.Name == name)
                        return f;
            }
            throw new System.Exception($"Cozulemeyen field: {owner.Name}.{name}");
        }
    }
}
