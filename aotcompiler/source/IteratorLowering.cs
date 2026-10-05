using System.Collections.Generic;

namespace DigitoyEngine.Language
{
    // Iterator (yield) lowering: FRONT-END pass'i, Resolver'dan ONCE cagrilir (EmitWhile gibi
    // kaynak-seviyesi bir donusum - Resolver/VM/CTranspiler yield'den habersizdir).
    //
    // Yield iceren bir Code su uc parcaya acilir (eski derleyicideki iterFrame cozumunun
    // temiz, bagimsiz-pass hali):
    //   1) Frame class'i: pc + current + a0..aN (argumanlar) + l0..lM (locals) alan olur
    //   2) {Ad}_MoveNext(frame) -> Int : govde; switch(pc) yerine Ceq/Brtrue dispatch zinciri,
    //      GetLocal/GetArg'lar frame alan erisimine cevrilir, Yield -> current/pc yaz + 1 dondur,
    //      Return (yield break) -> pc=-1 + 0 dondur
    //   3) {Ad}_Create(args) -> Frame : frame'i kurar (pc=0, argumanlar kopyalanir)
    public static class IteratorLowering
    {
        public static (Primitive frame, Code create, Code moveNext) Lower(Context ctx, Code iterator, Primitive elementType)
        {
            foreach (var a in iterator.Arguments)
                if (a.Type == null || a.Type.Unresolved) throw new System.Exception($"IteratorLowering resolve edilmis arg tipleri ister: {iterator.Name}.{a.Name}");
            foreach (var l in iterator.Locals)
                if (l == null || l.Unresolved) throw new System.Exception($"IteratorLowering resolve edilmis local tipleri ister: {iterator.Name}");

            string baseName = iterator.EncodeName().Replace('$', '_');

            var frame = new Primitive { Name = baseName + "_Frame", Type = PrimitiveType.Model };
            frame.AddField(new PrimitiveField { Name = "pc", Type = Primitive.Int });
            frame.AddField(new PrimitiveField { Name = "current", Type = elementType });
            for (int i = 0; i < iterator.Arguments.Count; i++)
                frame.AddField(new PrimitiveField { Name = "a" + i, Type = iterator.Arguments[i].Type });
            for (int i = 0; i < iterator.Locals.Count; i++)
                frame.AddField(new PrimitiveField { Name = "l" + i, Type = iterator.Locals[i] });
            var pcField = frame.Fields[0];
            var currentField = frame.Fields[1];
            PrimitiveField ArgField(int i) => frame.Fields[2 + i];
            PrimitiveField LocalField(int i) => frame.Fields[2 + iterator.Arguments.Count + i];

            // --- MoveNext ---
            var moveNext = new Code { Name = baseName + "_MoveNext", ReturnType = Primitive.Int };
            moveNext.Arguments.Add(new Argument { Name = "frame", Type = frame });
            var ops = moveNext.Operations;

            // SetField "instance ALTTA, deger USTTE" ister; eldeki deger stack tepesinde oldugundan
            // tipe gore bir scratch local uzerinden siralama duzeltilir
            var scratchByType = new Dictionary<Primitive, int>();
            int ScratchFor(Primitive t)
            {
                if (!scratchByType.TryGetValue(t, out var slot))
                {
                    slot = moveNext.Locals.Count;
                    moveNext.Locals.Add(t);
                    scratchByType[t] = slot;
                }
                return slot;
            }
            void EmitStoreField(PrimitiveField field) // stack: [.., deger] -> frame alanina yaz
            {
                int s = ScratchFor(field.Type);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = s });
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = s });
                ops.Add(new Op { Type = OpType.SetField, Field = field });
            }
            void EmitYieldBreak() // pc=-1, 0 dondur
            {
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                ops.Add(new Op { Type = OpType.Push, Value = -1 });
                ops.Add(new Op { Type = OpType.SetField, Field = pcField });
                ops.Add(new Op { Type = OpType.Push, Value = 0 });
                ops.Add(new Op { Type = OpType.Return });
            }

            // dispatch zinciri once uretilir: pc==k -> Rk (k yield sayisina gore 1'den baslar)
            int yieldCount = 0;
            foreach (var op in iterator.Operations)
                if (op.Type == OpType.Yield) yieldCount++;
            var resumeLabels = new List<Label>();
            for (int k = 1; k <= yieldCount; k++)
            {
                var label = new Label { Name = "__resume" + k };
                resumeLabels.Add(label);
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                ops.Add(new Op { Type = OpType.GetField, Field = pcField });
                ops.Add(new Op { Type = OpType.Push, Value = k });
                ops.Add(new Op { Type = OpType.Ceq });
                ops.Add(new Op { Type = OpType.Brtrue, Label = label });
            }

            int yieldSeen = 0;
            foreach (var op in iterator.Operations)
            {
                switch (op.Type)
                {
                    case OpType.GetLocal:
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        ops.Add(new Op { Type = OpType.GetField, Field = LocalField(op.Slot) });
                        break;
                    case OpType.SetLocal:
                        EmitStoreField(LocalField(op.Slot));
                        break;
                    case OpType.GetArg:
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        ops.Add(new Op { Type = OpType.GetField, Field = ArgField(op.Slot) });
                        break;
                    case OpType.SetArg:
                        EmitStoreField(ArgField(op.Slot));
                        break;
                    case OpType.Yield:
                        yieldSeen++;
                        EmitStoreField(currentField);
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        ops.Add(new Op { Type = OpType.Push, Value = yieldSeen });
                        ops.Add(new Op { Type = OpType.SetField, Field = pcField });
                        ops.Add(new Op { Type = OpType.Push, Value = 1 });
                        ops.Add(new Op { Type = OpType.Return });
                        ops.Add(new Op { Type = OpType.Label, Label = resumeLabels[yieldSeen - 1] });
                        break;
                    case OpType.Return: // iterator icinde Return = yield break (degerli Return gecersiz)
                        EmitYieldBreak();
                        break;
                    case OpType.AddrLocal:
                    case OpType.AddrArg:
                        throw new System.Exception($"iterator icinde {op.Type} desteklenmiyor (frame alanlarinin adresi alinamaz)");
                    default:
                        ops.Add(op); // aritmetik/kontrol akisi/alan erisimi oldugu gibi gecer (Label objeleri korunur)
                        break;
                }
            }
            EmitYieldBreak(); // govde sonundan dusme = iterasyon bitti

            // --- Create ---
            var create = new Code { Name = baseName + "_Create", ReturnType = frame };
            foreach (var a in iterator.Arguments)
                create.Arguments.Add(new Argument { Name = a.Name, Type = a.Type });
            var cops = create.Operations;
            cops.Add(new Op { Type = OpType.New, PrimitiveRef = frame });
            cops.Add(new Op { Type = OpType.Dup });
            cops.Add(new Op { Type = OpType.Push, Value = 0 });
            cops.Add(new Op { Type = OpType.SetField, Field = pcField });
            for (int i = 0; i < iterator.Arguments.Count; i++)
            {
                cops.Add(new Op { Type = OpType.Dup });
                cops.Add(new Op { Type = OpType.GetArg, Slot = i });
                cops.Add(new Op { Type = OpType.SetField, Field = ArgField(i) });
            }
            cops.Add(new Op { Type = OpType.Return });

            ctx.RegisterPrimitive(frame);
            ctx.RegisterCode(create);
            ctx.RegisterCode(moveNext);
            return (frame, create, moveNext);
        }
    }
}
