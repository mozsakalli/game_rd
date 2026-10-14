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
                if (Canon.Enabled && ProcessDictionaries(ctx, done)) more = true; // sozluk talepleri yeni exact/canon ornekler uretebilir
                // Concretize/Hierarchy yeni kod kaydetmis olabilir (alan tipi somutlamasi, generic sanal ornekleme) -> tekrar tur
                if (!more && ctx.AllCodes.All(done.Contains)) break;
            }
            AssignVirtualSlots(ctx);
        }

        // ---- CANON SOZLUKLERI (Canon.cs): canon govdelerdeki T'ye bagli op'lar slot ister; her exact ornek slotlari cozer ----
        static readonly HashSet<Code> dictScanned = new HashSet<Code>();

        static bool ProcessDictionaries(Context ctx, HashSet<Code> resolved)
        {
            bool changed = false;
            foreach (var code in ctx.AllCodes.ToList())
            {
                if (!code.IsCanonCode || code.IsSharedExact || !resolved.Contains(code) || !dictScanned.Add(code)) continue;
                var layout = ctx.LayoutOf(Context.DictOwnerOf(code));
                foreach (var op in code.Operations)
                    if (RequestSlots(ctx, code, layout, op)) changed = true;
            }
            // exact ornekler: eksik slotlar tamamlanir (layout buyudukce / yeni exact geldikce; append-only)
            foreach (var exact in ctx.SharedExactOrder.ToList())
                if (ctx.TryGetLayout(exact.Canonical, out var layout) && ResolveDict(ctx, exact, layout, exact.TypeArguments, null)) changed = true;
            foreach (var exact in ctx.SharedExactCodeOrder.ToList())
            {
                var canon = exact.Canonical;
                if (!canon.HasMethodDict) continue; // sozluk sahibi tip: exact tipin sozlugu yeter
                if (!ctx.TryGetLayout(canon, out var layout)) continue;
                var typeArgs = exact.Owner != null && exact.Owner.InstantiatedFrom != null ? exact.Owner.TypeArguments : null;
                if (ResolveDict(ctx, exact, layout, typeArgs, exact.MethodTypeArguments)) changed = true;
            }
            return changed;
        }

        // Slot anahtari her zaman ACIK bicimdir (marker'li). Cozulmus tip __Canon iceriyorsa ama acik bicim yoksa frontend/Resolver hatasi.
        static bool RequestSlots(Context ctx, Code code, DictLayout layout, Op op)
        {
            bool added = false, a;
            switch (op.Type)
            {
                case OpType.TypeOf: case OpType.Box: case OpType.Unbox: case OpType.UnboxOrDefault: case OpType.CastClass:
                case OpType.AsType: case OpType.IsType: case OpType.ExIs: case OpType.ExBind: case OpType.New: case OpType.NullableWrap:
                    if (Canon.ContainsCanon(op.PrimitiveRef))
                    {
                        if (op.PrimitiveRef == Primitive.Canon && (op.Type == OpType.Box || op.Type == OpType.NullableWrap)) break; // ref kutulama no-op
                        var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik bicim yok: {code.EncodeName()} {op}");
                        op.DictSlot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                    }
                    break;
                case OpType.NewArray:
                    if (Canon.ContainsCanon(op.PrimitiveRef))
                    {
                        var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik bicim yok: {code.EncodeName()} {op}");
                        op.DictSlot = layout.Request(DictSlotKind.TypeHandle, Primitive.ArrayOf(open, op.Slot > 0 ? op.Slot : 1), out a).Index; added |= a;
                    }
                    break;
                case OpType.GetStatic: case OpType.SetStatic: case OpType.AddrStatic:
                    if (op.Field != null && Canon.ContainsCanon(op.Field.Owner))
                    {
                        var def = op.Field.Owner.Definition;
                        var open = ctx.Apply(def, op.TypeArguments.GetRange(0, def.GenericParameters.Count));
                        op.DictSlot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                    }
                    break;
                case OpType.Call: case OpType.CallVirtual:
                    {
                        var cc = op.Code.Shared;
                        if (op.Type == OpType.CallVirtual && op.Code.Owner != null && op.Code.Owner.IsInterface && Canon.ContainsCanon(op.Code.Owner))
                        {
                            var idef = op.Code.Owner.Definition;
                            var iopen = ctx.Apply(idef, op.TypeArguments.GetRange(0, idef.GenericParameters.Count));
                            op.DictSlotIface = layout.Request(DictSlotKind.TypeHandle, iopen, out a).Index; added |= a;
                        }
                        if (!cc.NeedsDictParam) break;
                        if (op.Type == OpType.CallVirtual && op.Code.IsSharedExact) break; // exact GVM slotu: girisler stub (ctx'siz)
                        var calleeOwner = Context.DictOwnerOf(cc);
                        if (calleeOwner == layout.Owner) { op.DictSlot = -2; break; } // ayni sozluk: kendi __dict
                        if (cc.HasMethodDict)
                            op.DictSlot = layout.Request(DictSlotKind.MethodDict, GenericInstantiator.RootTemplate(op.Code), new List<Primitive>(op.TypeArguments), out a).Index;
                        else
                        {
                            var def = cc.Owner.Definition;
                            var open = ctx.Apply(def, op.TypeArguments.GetRange(0, def.GenericParameters.Count));
                            op.DictSlot = layout.Request(DictSlotKind.TypeDict, open, out a).Index;
                        }
                        added |= a;
                        break;
                    }
                case OpType.DelegateNew:
                    {
                        if (Canon.ContainsCanon(op.PrimitiveRef))
                        {
                            var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik delegate tipi yok: {code.EncodeName()} {op}");
                            op.DictSlot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                        }
                        var cc = op.Code.Shared;
                        if (cc.NeedsDictParam)
                        {
                            op.DictSlotFn = layout.Request(DictSlotKind.FnPtr, GenericInstantiator.RootTemplate(op.Code), new List<Primitive>(op.TypeArguments), out a).Index; added |= a;
                        }
                        break;
                    }
            }
            return added;
        }

        // Exact ornegin sozlugunu layout'a gore tamamlar (append-only: yalniz eksik slotlar). Hedef tipler/metotlar burada ORNEKLENIR.
        static bool ResolveDict(Context ctx, object exact, DictLayout layout, IReadOnlyList<Primitive> typeArgs, IReadOnlyList<Primitive> methodArgs)
        {
            var d = ctx.DictInstanceOf(exact);
            d.Layout = layout;
            bool changed = false;
            for (int i = d.Targets.Count; i < layout.Slots.Count; i++)
            {
                var s = layout.Slots[i];
                object target;
                if (s.Kind == DictSlotKind.TypeHandle || s.Kind == DictSlotKind.TypeDict)
                {
                    var t = Concretize(ctx, Canon.Demarker(ctx, s.OpenType, typeArgs, methodArgs));
                    if (Canon.Depth(t) > Canon.MaxDepth) throw new System.Exception($"canon sozluk talebi cok derin ({Canon.Depth(t)}): {t.Name} <- {Describe(exact)}");
                    if (Canon.ContainsCanon(t)) throw new System.Exception($"sozluk hedefi exact degil: {t.Name} <- {Describe(exact)}");
                    target = t;
                }
                else
                {
                    var args = s.OpenTypeArgs.Select(x => Concretize(ctx, Canon.Demarker(ctx, x, typeArgs, methodArgs))).ToList();
                    var c = GenericInstantiator.ResolveCallTarget(ctx, s.OpenCode, args);
                    if (s.Kind == DictSlotKind.FnPtr) ctx.Stubs.Add(c);
                    target = c;
                }
                d.Targets.Add(target);
                changed = true;
            }
            return changed;
        }

        static string Describe(object exact) => exact is Primitive p ? p.Name : ((Code)exact).EncodeName();

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
                        // CANON: acik bicim (marker'li) korunur -> sozluk slot anahtari (ProcessDictionaries)
                        bool open = op.TypeArguments.Any(Canon.ContainsMarker);
                        if (open && op.OpenRef == null && op.PrimitiveRef != null && op.Code == null && op.PrimitiveRef.Definition.IsGeneric)
                            op.OpenRef = ctx.Apply(op.PrimitiveRef.Definition, op.TypeArguments.GetRange(0, op.PrimitiveRef.Definition.GenericParameters.Count));
                        if (open) resolvedArgs = new List<Primitive>(op.TypeArguments); // Instantiate marker'lari canon'a indirger
                        if (op.Code != null)
                        {
                            op.Code = GenericInstantiator.ResolveCallTarget(ctx, op.Code, resolvedArgs);
                            if (op.PrimitiveRef != null)
                            {
                                if (Canon.ContainsMarker(op.PrimitiveRef)) op.OpenRef = op.PrimitiveRef;
                                op.PrimitiveRef = Concretize(ctx, op.PrimitiveRef); // DelegateNew: delegate tipi
                            }
                        }
                        else if (op.PrimitiveRef != null)
                        {
                            if (Canon.ContainsMarker(op.PrimitiveRef)) op.OpenRef = op.PrimitiveRef;
                            op.PrimitiveRef = op.PrimitiveRef.Definition.IsGeneric
                                ? GenericInstantiator.InstantiatePrimitive(ctx, op.PrimitiveRef.Definition, resolvedArgs.GetRange(0, op.PrimitiveRef.Definition.GenericParameters.Count))
                                : Concretize(ctx, op.PrimitiveRef);
                        }
                        if (op.Field != null && op.Field.Owner != null && op.Field.Owner.IsGeneric)
                        {
                            int classArgCount = op.Field.Owner.GenericParameters.Count;
                            var inst = GenericInstantiator.InstantiatePrimitive(ctx, op.Field.Owner, resolvedArgs.GetRange(0, classArgCount));
                            op.Field = FindField(inst, op.Field.Name);
                        }
                        // NOT: TypeArguments temizlenmez — frontend New+Call(ctor) op'lari AYNI listeyi paylasir; kod zaten bir kez islenir.
                    }
                    else if (op.PrimitiveRef != null)
                    {
                        if (Canon.ContainsMarker(op.PrimitiveRef)) op.OpenRef = op.PrimitiveRef;
                        op.PrimitiveRef = Concretize(ctx, op.PrimitiveRef); // acik uygulama dugumu (orn. is/as Box<int>) somutlanir
                    }
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
            if (p.IsCanonMarker) return Primitive.Canon; // canon govde: marker yalniz acik bicimde (TypeArguments/OpenRef) yasar
            if (p.GenericTemplate != null)
            {
                if (GenericInstantiator.ContainsGenericParameter(p)) return p;
                var args = new List<Primitive>();
                foreach (var a in p.TypeArguments)
                    args.Add(Concretize(ctx, a));
                return GenericInstantiator.InstantiatePrimitive(ctx, p.GenericTemplate, args);
            }
            if (p.ElementType != null && (p.ElementType.GenericTemplate != null || Canon.ContainsMarker(p.ElementType)))
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
