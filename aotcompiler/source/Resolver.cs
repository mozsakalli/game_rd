using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{
    // Decode edilen graf, isim ile isaret eden "Unresolved" placeholder nesneler icerir
    // (bkz. Primitive.Decode / Code.Decode). Resolver bunlari Context'teki gercek nesnelere baglar.
    // Pass 1: tam tanimli (Unresolved=false) primitive/code'lari Context'e kaydet.
    // Pass 2: butun graf gezilip Unresolved referanslar Context'ten cozulur.
    public static class Resolver
    {
        public static void Register(Context ctx, IEnumerable<Primitive> primitives, IEnumerable<Code> codes)
        {
            foreach (var p in primitives)
                if (!p.Unresolved)
                    ctx.RegisterPrimitive(p);
            foreach (var c in codes)
                if (!c.Unresolved)
                    ctx.RegisterCode(c);
        }

        public static Primitive ResolvePrimitive(Context ctx, Primitive p)
        {
            if (p == null || !p.Unresolved)
                return p;
            if (!ctx.TryGetPrimitive(p.Name, out var resolved))
                throw new System.Exception($"Cozulemeyen primitive: {p.Name}");
            return resolved;
        }

        public static Code ResolveCode(Context ctx, Code c)
        {
            if (c == null || !c.Unresolved)
                return c;
            if (!ctx.TryGetCode(c.EncodeName(), out var resolved))
                throw new System.Exception($"Cozulemeyen code: {c.EncodeName()}");
            return resolved;
        }

        public static void ResolveAll(Context ctx, IEnumerable<Code> codes)
        {
            foreach (var primitive in ctx.AllPrimitives.ToList())
            {
                if (primitive.IsGeneric) continue;
                primitive.Parent = Concretize(ctx, primitive.Parent);
                for (int i = 0; i < primitive.Interfaces.Count; i++)
                    primitive.Interfaces[i] = Concretize(ctx, primitive.Interfaces[i]);
            }
            foreach (var code in codes)
            {
                code.Owner = ResolvePrimitive(ctx, code.Owner);
                // template kodlar (generic method ya da generic sinif uyesi) SOMUTLASTIRILMAZ:
                // tip referanslari T icerir, kopyalari CloneCode somutlastirir. Yalniz isim cozumu + label.
                bool isTemplate = code.GenericParameters.Count > 0 || (code.Owner != null && code.Owner.IsGeneric);
                if (code.ExplicitInterface != null)
                    code.ExplicitInterface = isTemplate ? ResolvePrimitive(ctx, code.ExplicitInterface) : Concretize(ctx, code.ExplicitInterface);
                code.ReturnType = isTemplate ? ResolvePrimitive(ctx, code.ReturnType) : Concretize(ctx, code.ReturnType);
                foreach (var arg in code.Arguments)
                    arg.Type = isTemplate ? ResolvePrimitive(ctx, arg.Type) : Concretize(ctx, arg.Type);
                for (int i = 0; i < code.Locals.Count; i++)
                    code.Locals[i] = isTemplate ? ResolvePrimitive(ctx, code.Locals[i]) : Concretize(ctx, code.Locals[i]);
                try
                {
                    foreach (var op in code.Operations)
                    {
                        if (op.Field != null)
                        {
                            var owner = ResolvePrimitive(ctx, op.Field.Owner);
                            // field'in kendisi de owner uzerinde isimle bulunmali (Index/Type icin)
                            op.Field = FindField(owner, op.Field.Name);
                        }
                        if (op.Code != null)
                            op.Code = ResolveCode(ctx, op.Code);

                        if (isTemplate) continue; // op-duzeyi somutlastirma klon sirasinda

                        // 3. pass (monomorphization): TypeArguments tasiyan op'lar somut hedefe baglanir.
                        // Kural: liste = [sinif argumanlari] + [method argumanlari] (Owner generic ise once sinifinkiler).
                        if (op.TypeArguments.Count > 0)
                        {
                            var resolvedArgs = new List<Primitive>();
                            foreach (var arg in op.TypeArguments)
                                resolvedArgs.Add(Concretize(ctx, arg));
                            if (op.Code != null)
                                op.Code = GenericInstantiator.ResolveCallTarget(ctx, op.Code, resolvedArgs);
                            else if (op.PrimitiveRef != null)
                                op.PrimitiveRef = GenericInstantiator.InstantiatePrimitive(ctx, ResolvePrimitive(ctx, op.PrimitiveRef), resolvedArgs);
                            if (op.Field != null && op.Field.Owner != null && op.Field.Owner.IsGeneric)
                            {
                                int classArgCount = op.Field.Owner.GenericParameters.Count;
                                var inst = GenericInstantiator.InstantiatePrimitive(ctx, op.Field.Owner, resolvedArgs.GetRange(0, classArgCount));
                                op.Field = FindField(inst, op.Field.Name);
                            }
                        }
                        else if (op.PrimitiveRef != null)
                            op.PrimitiveRef = Concretize(ctx, op.PrimitiveRef); // Apply node (orn. is/as Box<int>) somutlanir
                    }
                    Linker.LinkLabels(code); // 4. pass: goto hedefleri instruction index'ine cozulur
                }
                catch (System.Exception ex) when (!isTemplate)
                {
                    // Eksik generic somutlastirma vb.: tum pipeline'i dusurme, kodu stub'la (CTranspiler tipe uygun sifir doner).
                    System.Console.Error.WriteLine($"[resolve-stub] {code.EncodeName()}: {ex.Message}");
                    code.UntranslatableReason = $"resolve: {ex.Message}";
                    code.Operations.Clear();
                }

            }

            // 4.5. pass: somut tiplerin alan/parent tiplerindeki Apply node'lari somutlanir
            // (orn. "class Holder { Box<int> b; }" - Box<int> hic New'lenmese bile tip var olmali).
            foreach (var p in ctx.AllPrimitives.ToList()) // Concretize yeni kayit ekler -> snapshot
            {
                if (p.IsGeneric || p.GenericTemplate != null) continue; // template/Apply dokunulmaz
                p.Parent = Concretize(ctx, p.Parent);
                foreach (var f in p.Fields) f.Type = Concretize(ctx, f.Type);
                foreach (var f in p.StaticFields) f.Type = Concretize(ctx, f.Type);
            }

            // 5. pass: kalitim (alan duzlestirme + vtable/itable), sonra CallVirtual slotlari.
            // Slotlar TUM ctx uzerinden atanir: monomorphization'in urettigi klonlar da kapsansin.
            Hierarchy.Build(ctx);
            // runtime exception kind -> corlib sinifi eslemesi (DIGITOYENGINE_EX_* indeksleri, WellKnown tablosu)
            for (int k = 1; k < WellKnown.ExceptionKinds.Length; k++)
                if (WellKnown.ExceptionKinds[k] != null && ctx.TryGetPrimitive(WellKnown.ExceptionKinds[k], out var kt))
                    ctx.ExceptionKindTypes[k] = kt;
            foreach (var code in ctx.AllCodes)
            {
                if (code.GenericParameters.Count > 0 || (code.Owner != null && code.Owner.IsGeneric))
                    continue; // template: vtable'i yok, klonlari zaten kapsamda
                foreach (var op in code.Operations)
                    if (op.Type == OpType.CallVirtual || (op.Type == OpType.DelegateNew && op.Slot == -2))
                    {
                        if (op.Code.Owner == null)
                            throw new System.Exception($"CallVirtual hedefi Owner'siz: {op.Code.Name}");
                        int slot = Hierarchy.SlotOf(op.Code.Owner.VTable, op.Code.Name);
                        if (slot < 0)
                            throw new System.Exception($"CallVirtual hedefi vtable'da yok (virtual isaretli mi?): {op.Code.EncodeName()}");
                        op.Slot = slot;
                    }
            }
        }

        // Apply node'lari (Box<int>) gercek somutlastirmaya, Unresolved'lari kayitli nesneye cevirir.
        // Diziler eleman uzerinden rekursif (Apply elemanli dizi tipi somut dizi tipine doner).
        public static Primitive Concretize(Context ctx, Primitive p)
        {
            if (p == null) return null;
            if (p.GenericTemplate != null)
            {
                var args = new List<Primitive>();
                foreach (var a in p.TypeArguments)
                    args.Add(Concretize(ctx, a));
                return GenericInstantiator.InstantiatePrimitive(ctx, ResolvePrimitive(ctx, p.GenericTemplate), args);
            }
            if (p.Type == PrimitiveType.Array && p.ElementType != null && p.ElementType.GenericTemplate != null)
                return Primitive.ArrayOf(Concretize(ctx, p.ElementType), p.ArrayRank);
            return ResolvePrimitive(ctx, p);
        }

        static PrimitiveField FindField(Primitive owner, string name)
        {
            for (var t = owner; t != null && !t.Unresolved; t = t.Parent) // kalitilan alanlar parent'ta
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

        // Decode edilmis bir Primitive'in ElementType/Parent/alan tiplerini Context'e gore cozer.
        public static void ResolvePrimitiveTypes(Context ctx, Primitive p)
        {
            p.ElementType = ResolvePrimitive(ctx, p.ElementType);
            p.Parent = ResolvePrimitive(ctx, p.Parent);
            for (int i = 0; i < p.Interfaces.Count; i++)
                p.Interfaces[i] = ResolvePrimitive(ctx, p.Interfaces[i]);
            foreach (var field in p.Fields)
                field.Type = ResolvePrimitive(ctx, field.Type);
        }
    }
}
