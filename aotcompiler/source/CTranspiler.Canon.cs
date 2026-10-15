using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Language
{
    // CANON BAGLAMA KATMANI (docs/engine-notes.md CANON LOWERING P3): paylasimli exact orneklerin LINK artefaktlari — sozluk tablolari
    // (X_dict/mdict_*), instantiating stub'lar (de_inst_*), exact kabuk/canon govde sembol eslemesi, vtable sozluk sentinel'i, exact
    // statik bloklari ve paylasilan C yerlesiminin alan cast kurallari. Op emisyonu (CTranspiler.cs) bunlari yalniz ABI/sembol olarak
    // kullanir; T'ye bagli karar Lower.cs'tedir (yiginda LdDictSlot/LdType...). vtable/itable uretimi nasil "baglama" ise bu da oyledir.
    public static partial class CTranspiler
    {
        // ---- CANON yardimcilari (K6/K7/K10) ----
        // Fonksiyon isaretcisi olarak materyalize edilen hedef: exact kabuk -> canon fn ya da (ctx gerekiyorsa) instantiating stub.
        static string FnPtrSym(Code c) => c.IsSharedExact ? (c.Shared.NeedsDictParam ? StubSym(c) : CSym(c.Shared)) : CSym(c);
        static string StubSym(Code exact) => "de_inst_" + CSym(exact);
        static readonly HashSet<Code> neededStubs = new HashSet<Code>();
        static string TypeDictSym(Primitive exact) => CName(exact.Name) + "_dict";
        static string MethodDictSym(Code exact) => "mdict_" + CSym(exact);
        // exact kabugun (tip uyesi ya da generic metot ornegi) kendi sozluk sembolu: canon metot dict'i ya da exact tipin dict'i
        static string DictSymOf(Code exact)
        {
            if (!exact.IsSharedExact) throw new Exception($"canon cagri hedefine exact sozluk yok: {exact.EncodeName()}");
            return exact.Shared.HasMethodDict ? MethodDictSym(exact) : TypeDictSym(exact.Owner);
        }
        // Sanal cagri ABI'si: canon slot girisi (Pair<__Canon>.M) sozluk alir; exact GVM slotunun (Pair<string>.M) girisleri STUB'dir (ctx'siz).
        // Lower.cs ayni kuralla gizli operand push eder; emitter yalniz pop eder.
        static bool VirtualCallTakesDict(Code target) => !target.IsSharedExact && target.Shared.NeedsDictParam;
        static string StaticsStructTag(Primitive canon) => StructTag(canon) + "_statics";
        static string StaticsSym(Primitive exact) => CName(exact.Name) + "_statics";
        static string DictSlotType(string dict, int slot) => $"DIGITOYENGINE_DICT_TYPE({dict}, {slot})";
        // emisyon sirasinda isleniyor olan fonksiyonun sozluk ifadesi (__dict yerel/param) ve kullanim bayragi
        static bool curDictUsed;
        static string Dict() { curDictUsed = true; return "__dict"; }
        // Sozluk kaynagi (K5): gizli parametre (static/struct/canon generic metot) ise bildirim gerekmez; class instance metodu
        // this'in exact tipinden alir (vtable'daki sentinel slot; tureyen tipte de dogru).
        static string DictPrologue(Code code, string[] argNames)
        {
            if (code.NeedsDictParam) return "";
            int k = code.Owner.VTable.IndexOf(EmitCtx.DictSentinel(code.Owner));
            if (k < 0) throw new Exception($"canon govde sozluk istiyor ama sahibinin dict slotu yok: {code.EncodeName()}");
            return $"    const void *const *__dict = (const void *const *){argNames[0]}->gc.type->vtable[{k}];\n";
        }

        // Paylasimli exact tipin alanini okuma/yazma/adresi: C struct canon'un (alan tipi orn. GCHeader*), exact tip daha ozel -> acik cast (K9)
        static string CanonFieldCType(PrimitiveField f)
        {
            var cf = Hierarchy.AllFields(f.Owner.Canonical).FirstOrDefault(x => x.Name == f.Name) ?? throw new Exception($"canon alani yok: {f.Owner.Name}.{f.Name}");
            return CType(cf.Type);
        }
        static bool FieldNeedsCast(PrimitiveField f) => f.Owner != null && f.Owner.IsSharedExact && TypeShape.IsGcRef(f.Type) && CanonFieldCType(f) != CType(f.Type);
        static string FieldCast(PrimitiveField f, string expr) => FieldNeedsCast(f) ? $"(({CType(f.Type)})({expr}))" : expr;
        static string FieldStoreCast(PrimitiveField f, string expr) => FieldNeedsCast(f) ? $"(({CanonFieldCType(f)})({expr}))" : expr;
        static string FieldAddrCast(PrimitiveField f, string expr) => FieldNeedsCast(f) ? $"(({CType(f.Type)}*)({expr}))" : expr;
        // Canon govdede `where T : Component` alicisi T (VmObject*) uzerinden alan erisimi: alanin sahibine cast (kisit tipi IL'de alan sahibidir)
        static CVal CanonInst(CVal inst, PrimitiveField f) =>
            inst.Type != null && inst.Type.IsCanon && f.Owner != null && TypeShape.IsClassModel(f.Owner) ? new CVal($"(({CType(f.Owner)})({inst.Expr}))", f.Owner) : inst;


        // vtable girisi: sozluk sentinel'i -> zincirde o canon'a karsilik gelen exact tipin sozlugu; aksi halde fn/stub
        static string VSlotRef(Primitive model, Code m)
        {
            if (Context.IsDictSentinel(m))
            {
                for (var t = model; t != null; t = t.Parent)
                    if (t.Canonical == m.Owner) return $"(const void*){TypeDictSym(t)}";
                throw new Exception($"sozluk sentinel'inin exact sahibi yok: {model.Name} <- {m.Owner.Name}");
            }
            return VSlotRefOf(m);
        }


        // CANON K7: instantiating stub — exact imzali, sozlugu yukleyip canon fn'e atlar. Fonksiyon isaretcisi istenen her yerde
        // (vtable/itable, delegate, MethodInfo.fn) bu kullanilir; dogrudan cagri sozlugu inline gecer.
        static string EmitStub(Code exact)
        {
            var k = exact.Shared;
            var an = CArgNames(exact, new HashSet<string>());
            var args = new List<string> { DictSymOf(exact) };
            for (int i = 0; i < an.Length; i++) // exact imza -> canon imza (VmString* -> VmObject*): acik cast
                args.Add(CType(k.Arguments[i].Type) != CType(exact.Arguments[i].Type) ? $"({ParamCType(k.Arguments[i])})({an[i]})" : an[i]);
            var call = $"{CSym(k)}({string.Join(", ", args)})";
            bool isVoid = exact.ReturnType == null || exact.ReturnType == Primitive.Void;
            if (!isVoid && CType(k.ReturnType) != CType(exact.ReturnType)) call = $"({CType(exact.ReturnType)}){call}";
            return $"{Prototype(exact, StubSym(exact), false)} {{ {(isVoid ? "" : "return ")}{call}; }}\n";
        }
        // Stub talebi: sahibin birimine tanim (.c on-bolumu) + prototip (header); bir kez
        static void RequestStub(Code exact)
        {
            if (!exact.IsSharedExact || !exact.Shared.NeedsDictParam) return;
            if (!neededStubs.Add(exact)) return;
            var u = UnitOf(exact.Owner);
            u.H.Append(Prototype(exact, StubSym(exact), false) + ";\n");
            u.Pre.Append(EmitStub(exact));
        }
        static string VSlotRefOf(Code m)
        {
            if (!IsEmittableCode(m.Shared)) return "0";
            RequestStub(m);
            return $"(const void*)&{FnPtrSym(m)}";
        }
        // Exact ornegin sozluk dizisi: layout slot sirasiyla hedef adresleri (bos layout -> tek 0; cagrilan yine isaretci bekler)
        static string EmitDict(Context ctx, object exact, string sym)
        {
            var rows = new List<string>();
            if (ctx.TryGetDictInstance(exact, out var d))
                foreach (var t in d.Targets)
                {
                    if (t is Primitive p)
                    {
                        var slot = d.Layout.Slots[rows.Count];
                        rows.Add(slot.Kind == DictSlotKind.TypeDict ? $"(const void*){TypeDictSym(p)}" : $"(const void*){ExportTypeSymStrict(p)}");
                    }
                    else
                    {
                        var c = (Code)t;
                        var slot = d.Layout.Slots[rows.Count];
                        if (slot.Kind == DictSlotKind.FnPtr) { RequestStub(c); rows.Add($"(const void*)&{FnPtrSym(c)}"); }
                        else rows.Add($"(const void*){MethodDictSym(c)}");
                    }
                }
            if (rows.Count == 0) rows.Add("0");
            return $"const void *const {sym}[{rows.Count}] = {{ {string.Join(", ", rows)} }};\n";
        }
        // Sozluk hedefi exact tip descriptor'u: diziler dahil her hedefin sembolu olmali (0 = analiz hatasi, link degil runtime patlar)
        static string ExportTypeSymStrict(Primitive p)
        {
            var s = ExportTypeSym(p);
            if (s == "0") throw new Exception($"canon sozluk hedefinin descriptor'u yok: {p.Name}");
            return s;
        }


    }
}
