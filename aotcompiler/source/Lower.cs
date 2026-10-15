using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // CANON LOWERING (docs/engine-notes.md): "bu op exact tip ister" kararinin TEK sahibi. Canon govdedeki T'ye bagli op'lar
    // sozluk slotu ister ve ACIK IR'a cevrilir; emitter ve interpreter "canon/sozluk" kavramini bilmez — T'ye bagli her bilgi yiginda
    // bir degerdir.
    //  - Rt op'lari (NewRt, BoxRt, IsTypeRt...): tip descriptor'u (Type*) yiginin tepesinden gelir; onune LdDictSlot(TypeHandle) konur.
    //  - Call/CallVirtual: callee gizli sozluk parametresi istiyorsa (NeedsDictParam) sozluk degeri son gizli operand olarak push edilir
    //    (LdDictSelf / LdDictSlot(TypeDict|MethodDict) / LdDictConst). Iface cagrisinda itable anahtari (iface Type*) HER ZAMAN operanddir
    //    (statik: LdType, canon: LdDictSlot) -> emitter tek yol.
    //  - DelegateNew: delegate tipi ve fn isaretcisi yigindan (LdType|LdDictSlot, LdFnPtr|LdDictSlot(FnPtr)).
    // SOZLESME: gizli operandlar tuketicinin HEMEN ONCESINDEDIR (bitisik): Trim/vmint `HiddenSlot` ile okur. Ekleme sonrasi etiketler yeniden linklenir.
    // Iki asama: (1) Resolver sabit noktasinda canon govdeler (slot talebi yeni ornek dogurabilir), (2) sabit nokta sonrasi kalan govdeler
    // (yalniz sabit operandlar: LdDictConst / LdType / LdFnPtr).
    public static class Lower
    {
        static readonly HashSet<Code> lowered = new HashSet<Code>();

        // (1) Canon govde: slot talepleri + yeniden yazim. Donus: yeni slot eklendi mi (sabit nokta surer).
        public static bool LowerCanonCode(Context ctx, Code code, DictLayout layout)
        {
            if (!lowered.Add(code)) return false;
            bool added = false;
            Rewrite(code, (op, result) => added |= Emit(ctx, code, layout, op, result));
            return added;
        }

        // (2) Kalan govdeler (exact/canon olmayan): slot gerektirmeyen gizli operandlar.
        public static void LowerRest(Context ctx)
        {
            foreach (var code in ctx.AllCodes)
                if (code.Operations.Count > 0 && !lowered.Contains(code))
                {
                    lowered.Add(code);
                    Rewrite(code, (op, result) => Emit(ctx, code, null, op, result));
                }
        }

        static void Rewrite(Code code, System.Action<Op, List<Op>> emit)
        {
            var ops = code.Operations;
            var result = new List<Op>(ops.Count + 8);
            bool changed = false;
            foreach (var op in ops)
            {
                int before = result.Count;
                emit(op, result);
                if (result.Count != before) changed = true;
                result.Add(op);
            }
            if (!changed) return;
            code.Operations = result;
            Linker.LinkLabels(code); // gizli operandlar indeksleri kaydirdi
        }

        // Tek op: gerekiyorsa gizli operand(lar) uretir ve op'u Rt bicimine cevirir. layout null = canon degil (slot istenemez).
        static bool Emit(Context ctx, Code code, DictLayout layout, Op op, List<Op> result)
        {
            bool added = false, a;
            bool canon = layout != null;
            switch (op.Type)
            {
                case OpType.TypeOf: case OpType.Box: case OpType.Unbox: case OpType.UnboxOrDefault: case OpType.CastClass:
                case OpType.AsType: case OpType.IsType: case OpType.ExIs: case OpType.ExBind: case OpType.New:
                    if (canon && Canon.ContainsCanon(op.PrimitiveRef))
                    {
                        if (op.PrimitiveRef == Primitive.Canon && op.Type == OpType.Box) break; // ref kutulama no-op (kimlik)
                        var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik bicim yok: {code.EncodeName()} {op}");
                        var slot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                        // ref kutusu (kimlik) ve struct `new` (sifir blok) descriptor kullanmaz; operand yine de gider: Trim sozluk hedefini
                        // (exact tipin canliligi) bu op uzerinden gorur; emitter pop edip yok sayar.
                        PushSlot(result, op, slot, DictSlotKind.TypeHandle);
                        op.Type = RtOf(op.Type);
                    }
                    break;
                case OpType.NullableWrap:
                    // Nullable<T> sarma yerlesimle calisir, descriptor istemez; slot talebi hedefin canliligi icin (sozluk hedefi exact tip)
                    if (canon && Canon.ContainsCanon(op.PrimitiveRef) && op.PrimitiveRef != Primitive.Canon)
                    {
                        var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik bicim yok: {code.EncodeName()} {op}");
                        layout.Request(DictSlotKind.TypeHandle, open, out a); added |= a;
                    }
                    break;
                case OpType.NewArray:
                    if (canon && Canon.ContainsCanon(op.PrimitiveRef))
                    {
                        var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik bicim yok: {code.EncodeName()} {op}");
                        var slot = layout.Request(DictSlotKind.TypeHandle, Primitive.ArrayOf(open, op.Slot > 0 ? op.Slot : 1), out a).Index; added |= a;
                        PushSlot(result, op, slot, DictSlotKind.TypeHandle);
                        op.Type = OpType.NewArrayRt;
                    }
                    break;
                case OpType.GetStatic: case OpType.SetStatic: case OpType.AddrStatic:
                    if (canon && op.Field != null && Canon.ContainsCanon(op.Field.Owner))
                    {
                        var def = op.Field.Owner.Definition;
                        var open = ctx.Apply(def, op.TypeArguments.GetRange(0, def.GenericParameters.Count));
                        var slot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                        PushSlot(result, op, slot, DictSlotKind.TypeHandle);
                        op.Type = RtOf(op.Type);
                    }
                    break;
                case OpType.Call: case OpType.CallVirtual:
                    {
                        var cc = op.Code.Shared;
                        if (op.Type == OpType.CallVirtual && op.Code.Owner != null && op.Code.Owner.IsInterface)
                        {
                            if (canon && Canon.ContainsCanon(op.Code.Owner))
                            {
                                var idef = op.Code.Owner.Definition;
                                var iopen = ctx.Apply(idef, op.TypeArguments.GetRange(0, idef.GenericParameters.Count));
                                var slot = layout.Request(DictSlotKind.TypeHandle, iopen, out a).Index; added |= a;
                                PushSlot(result, op, slot, DictSlotKind.TypeHandle);
                            }
                            else result.Add(new Op { Type = OpType.LdType, PrimitiveRef = op.Code.Owner, Line = op.Line });
                        }
                        if (!cc.NeedsDictParam) break;
                        if (op.Type == OpType.CallVirtual && op.Code.IsSharedExact) break; // exact GVM slotu: girisler stub (ctx'siz) — CTranspiler.VirtualCallTakesDict ile ayni kural
                        if (canon)
                        {
                            var calleeOwner = Context.DictOwnerOf(cc);
                            if (calleeOwner == layout.Owner) { result.Add(new Op { Type = OpType.LdDictSelf, Line = op.Line }); break; } // ayni sozluk
                            int slot;
                            if (cc.HasMethodDict)
                                slot = layout.Request(DictSlotKind.MethodDict, GenericInstantiator.RootTemplate(op.Code), new List<Primitive>(op.TypeArguments), out a).Index;
                            else
                            {
                                var def = cc.Owner.Definition;
                                var open = ctx.Apply(def, op.TypeArguments.GetRange(0, def.GenericParameters.Count));
                                slot = layout.Request(DictSlotKind.TypeDict, open, out a).Index;
                            }
                            added |= a;
                            PushSlot(result, op, slot, cc.HasMethodDict ? DictSlotKind.MethodDict : DictSlotKind.TypeDict);
                        }
                        else
                        {
                            if (!op.Code.IsSharedExact) throw new System.Exception($"canon cagri hedefine exact sozluk yok: {op.Code.EncodeName()} <- {code.EncodeName()}");
                            result.Add(new Op { Type = OpType.LdDictConst, Code = op.Code, Line = op.Line });
                        }
                        break;
                    }
                case OpType.DelegateNew:
                    {
                        var cc = op.Code.Shared;
                        bool dynType = canon && Canon.ContainsCanon(op.PrimitiveRef);
                        bool dynFn = canon && cc.NeedsDictParam;
                        if (!dynType && !dynFn) break; // statik: op oldugu gibi (sembol + stub)
                        if (dynType)
                        {
                            var open = op.OpenRef ?? throw new System.Exception($"canon govdede acik delegate tipi yok: {code.EncodeName()} {op}");
                            var slot = layout.Request(DictSlotKind.TypeHandle, open, out a).Index; added |= a;
                            PushSlot(result, op, slot, DictSlotKind.TypeHandle);
                        }
                        else result.Add(new Op { Type = OpType.LdType, PrimitiveRef = op.PrimitiveRef, Line = op.Line });
                        if (dynFn)
                        {
                            var slot = layout.Request(DictSlotKind.FnPtr, GenericInstantiator.RootTemplate(op.Code), new List<Primitive>(op.TypeArguments), out a).Index; added |= a;
                            PushSlot(result, op, slot, DictSlotKind.FnPtr);
                        }
                        else result.Add(new Op { Type = OpType.LdFnPtr, Code = op.Code, Line = op.Line });
                        op.Type = OpType.DelegateNewRt;
                        break;
                    }
            }
            return added;
        }

        static void PushSlot(List<Op> result, Op op, int slot, DictSlotKind kind) =>
            result.Add(new Op { Type = OpType.LdDictSlot, Slot = slot, Value = kind, Line = op.Line });

        static OpType RtOf(OpType t) => t switch
        {
            OpType.TypeOf => OpType.TypeOfRt, OpType.Box => OpType.BoxRt, OpType.Unbox => OpType.UnboxRt,
            OpType.UnboxOrDefault => OpType.UnboxOrDefaultRt, OpType.CastClass => OpType.CastRt, OpType.AsType => OpType.AsTypeRt,
            OpType.IsType => OpType.IsTypeRt, OpType.ExIs => OpType.ExIsRt, OpType.ExBind => OpType.ExBindRt, OpType.New => OpType.NewRt,
            OpType.GetStatic => OpType.GetStaticRt, OpType.SetStatic => OpType.SetStaticRt, OpType.AddrStatic => OpType.AddrStaticRt,
            _ => throw new System.Exception("Rt karsiligi yok: " + t)
        };

        // Tuketici op'un (indeks i) gizli slotu: bitisik LdDictSlot (DelegateNewRt: [type][fn] -> fn i-1, type i-2). -1 = yok.
        public static int HiddenSlot(List<Op> ops, int i, DictSlotKind kind)
        {
            for (int k = i - 1; k >= 0 && k >= i - 2 && ops[k].IsHiddenOperand; k--)
                if (ops[k].Type == OpType.LdDictSlot && (DictSlotKind)ops[k].Value == kind) return ops[k].Slot;
            return -1;
        }
    }
}
