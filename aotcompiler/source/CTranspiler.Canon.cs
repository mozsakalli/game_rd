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

        // ---- GENERIC SANAL METOT (GVM) CANON SLOTU: dispatcher (CoreCLR GVMLookup / IL2CPP GetGenericVirtualMethod karsiligi, STATIK) ----
        // Exact koddan GVM cagrisi exact slota gider (impl'in instantiating stub'u; ek maliyet yok). Canon govdeden cagri canon slota
        // (<__Canon>) gider ve BILDIRILEN exact metodun sozlugunu (mdict_D.M<X>) gizli ilk arguman olarak gecer; bu sozluk callee'nin
        // layout'u DEGILDIR (impl'in kendi sozlugu stub'ta yuklenir). Canon slot girisi bu dispatcher'dir: gelen sozluk isaretcisini
        // slot kokunun (SlotRoot) her exact orneklemesinin sozlugu ile karsilastirir ve o orneklemenin exact slot girisine (impl stub)
        // atlar. Kimlik = sozluk SEMBOLU (exact Code nesnesi) — ad yok; n = orneklemesi sayisi (kucuk), hash/tablo yok. Caller'in
        // sozlugu override zincirindeki herhangi bir bildirimin (Base.M<X>/Derived.M<X>/IFoo.M<X>) exact'i olabilir -> hepsi tanınır.
        static string GvmDispatchSym(Primitive model, Primitive iface, int slot) => $"de_gvm_{CName(model.Name)}_{(iface != null ? CName(iface.Name) + "_" : "")}{slot}";
        static bool IsCanonGvmSlot(Code m) => m.MethodTypeArguments != null && Canon.ContainsCanon(m.MethodTypeArguments) && !Context.IsDictSentinel(m);
        // Exact generic metot kabugunun sozluk sembolu var mi: govdeli canon'a bagli olanlar + IFACE GVM bildirimleri (govdesiz; sozluk
        // yalniz dispatcher icin KIMLIK token'idir, layout bos). Exact'in KENDISI canli olmali (tip sozlugundeki IsEmittableModel ile
        // simetrik): canon govde canli diye olu exact'in sozlugu yazilirsa hedefleri (state-machine tipi vb.) trim'lenmis olur -> strict hata.
        static bool HasMethodDictSymbol(Code c) =>
            c.Shared.HasMethodDict && c.Owner != null && (IsEmittableModel(c.Owner) || c.Owner.IsCanonForm) && TrimPlan.IsCodeLive(c)
            && (IsEmittableCode(c.Shared) || (c.Owner.IsInterface && TrimPlan.IsTypeLive(c.Owner)));

        // Canon GVM slot girisi: dispatcher uretilir (model birimine), adresi doner. table: ayni tablonun girisleri (vtable ya da itable),
        // declared: slotlari bildiren tablo (model.VTable ya da iface.VTable) — exact slotlar oradan eslenir.
        static string GvmSlotRef(Primitive model, Primitive iface, List<Code> declared, List<Code> table, int slot)
        {
            var canon = declared[slot];
            var root = Hierarchy.SlotRootOf(EmitCtx, canon);
            if (root == null) return VSlotRefOf(table[slot]);
            string sym = GvmDispatchSym(model, iface, slot);
            var k = canon.Shared; // canon imza (dispatcher'in imzasi + sozluk)
            var an = CArgNames(k, new HashSet<string>());
            var pars = new List<string> { "const void *const *__gdict" };
            for (int i = 0; i < an.Length; i++) pars.Add($"{ParamCType(k.Arguments[i])} {an[i]}");
            bool isVoid = k.ReturnType == null || k.ReturnType == Primitive.Void;
            string ret = isVoid ? "void" : CType(k.ReturnType);
            var sigArgs = k.Arguments.Select(a => (a.IsRef || a.IsOut) ? CType(a.Type) + "*" : CType(a.Type));
            string fnSig = $"{ret}(*)({string.Join(", ", sigArgs)})";
            var sb = new StringBuilder();
            var externs = new HashSet<string>();
            sb.Append($"{ret} {sym}({string.Join(", ", pars)}) {{\n");
            var callArgs = string.Join(", ", an);
            // bu tablodaki exact slotlar (ayni kok, canon icermeyen argumanlar): o argumanlarla bildirilen TUM exact sozlukler -> impl stub
            for (int s = 0; s < declared.Count; s++)
            {
                var e = declared[s];
                if (e.MethodTypeArguments == null || Canon.ContainsCanon(e.MethodTypeArguments) || Context.IsDictSentinel(e)) continue;
                if (Hierarchy.SlotRootOf(EmitCtx, e) != root) continue;
                var impl = table[s];
                if (!IsEmittableCode(impl.Shared)) continue;
                RequestStub(impl);
                foreach (var d in Hierarchy.ExactDeclarationsOf(EmitCtx, root, e.MethodTypeArguments))
                {
                    if (!HasMethodDictSymbol(d)) continue;
                    externs.Add($"extern const void *const {MethodDictSym(d)}[];\n"); // baska birimin sozlugu: yerel extern (tekrar bildirim serbest)
                    sb.Append($"    if (__gdict == {MethodDictSym(d)}) {(isVoid ? "{ " : "return ")}(({fnSig})&{FnPtrSym(impl)})({callArgs}){(isVoid ? "; return; }" : ";")}\n");
                }
            }
            sb.Append($"    DIGITOYENGINE_throw_notimpl(\"GVM: unknown instantiation dictionary ({sym})\");\n");
            sb.Append("}\n");
            var u = UnitOf(model);
            foreach (var x in externs) u.H.Append(x);
            u.H.Append($"{ret} {sym}({string.Join(", ", pars)});\n");
            u.Pre.Append(sb.ToString());
            return $"(const void*)&{sym}";
        }
    }
}
