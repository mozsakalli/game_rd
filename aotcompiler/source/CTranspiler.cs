using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DigitoyEngine.Language.Trim;

namespace DigitoyEngine.Language
{
    // Op akisini "derleme-zamani C ifade metni" stack'i uzerinden C koduna katlar (old_dotnet_compiler'daki
    // PlainEmitter/CVal fikrinden ilham). VM.Run ile AYNI switch iskeleti; fark: deger yerine C metni tasinir.
    // Kapsam: Model (struct), FixedArray alan, GetIndex/SetIndex, New, Call, Label/Br/Brtrue/Brfalse, Ceq/Cgt/Clt,
    // aritmetik. Generic template'ler atlanir (yalniz somut/concrete emit edilir).
    public static partial class CTranspiler
    {
        class CVal
        {
            public string Expr;
            public Primitive Type;
            public bool IsConst; // Lower gizli operandi (descriptor adresi / sozluk / fn isaretcisi): yan etkisiz sabit, spill edilmez
            public CVal(string expr, Primitive type) { Expr = expr; Type = type; }
            public static CVal Const(string expr, Primitive type) => new CVal(expr, type) { IsConst = true };
        }

        // string literal havuzu (interning): ayni metin = ayni _strpool_<hash> (GC_IMMORTAL, GC hic dokunmaz).
        // TranspileProgram basinda on-taramayla toplanir, Literal() referans verir.
        static readonly Dictionary<string, string> strPool = new Dictionary<string, string>();
        static Context EmitCtx; // finalize zinciri gibi ctx sorgulari icin (TranspileProgram atar)
        // Trim karari: emisyon kod/meta/thunk/trampoline sorularini YALNIZ buna sorar (source/Trim). Varsayilan: her sey canli.
        public static TrimResult TrimPlan = TrimResult.Everything;
        static readonly Dictionary<Primitive, int> typeIndex = new Dictionary<Primitive, int>(); // descriptor'u olan tipler (deger: digitoyengine_types[] sirasi; descriptor'a YAZILMAZ - Type.rootid uretilen tiplerde 0)
        static readonly Dictionary<string, Primitive> reflectedArrays = new Dictionary<string, Primitive>();
        static readonly Dictionary<string, Primitive> reflectedValueStructs = new Dictionary<string, Primitive>();
        static readonly Dictionary<string, Primitive> reflectedGenericDefs = new Dictionary<string, Primitive>(); // acik generic sablonlar (typeof(List<>), GetGenericTypeDefinition)
        static int nextTypeIndex;

        // MethodInfo gosterim adi: C# sozdizimi ("Demo12.App12.Deep(int[], int)"); IR kodlari fallback
        public static string CodeDisplay(Code c) =>
            (c.Owner != null ? c.Owner.Display + "." : "") + (c.DisplayName ?? c.Name);
        // MethodInfo.name: YALIN ad ("Deep", ".ctor") — sahip tipi/parametreler kayitta ayri; orneklemeler ayni string'i paylasir.
        public static string CodeBareName(Code c)
        {
            var d = c.DisplayName ?? c.Name;
            int paren = d.IndexOf('(');
            if (paren >= 0) d = d.Substring(0, paren);
            int dot = d.LastIndexOf('.');
            return dot >= 0 && dot < d.Length - 1 && d != ".ctor" ? d.Substring(dot + 1) : d;
        }

        static void CollectStrings(Context ctx)
        {
            strPool.Clear();
            strSymText.Clear();
            cstrPool.Clear();
            cstrSymText.Clear();
            typeNameCandidates.Clear();
            typeIndex.Clear();
            reflectedArrays.Clear();
            reflectedValueStructs.Clear();
            reflectedGenericDefs.Clear();
            nextTypeIndex = 0;
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c) && c.NativeBody == null)
                {
                    if (!c.IsExternal)
                    {
                        CStrAdd(CodeBareName(c)); // MethodInfo.name (UTF-8 havuz)
                        if (!string.IsNullOrEmpty(c.SourceFile)) CStrAdd(c.SourceFile); // MethodInfo.file
                    }
                    foreach (var op in c.Operations)
                    {
                        if (op.Type == OpType.Push && op.Value is string s)
                            PoolAdd(s);
                        if (op.Type == OpType.TypeOf && op.PrimitiveRef.Type == PrimitiveType.Array && !Canon.ContainsCanon(op.PrimitiveRef))
                            RegisterArray(op.PrimitiveRef); // typeof(T[]) hedefi: eleman descriptor'suz olsa da kimlik descriptor'u var
                        // Kullanici struct descriptor'i: typeof + kutulama/kutudan cikarma hedefleri
                        // (Box/Unbox/UnboxOrDefault/AsType/CastClass/IsType) — box helper'i &X_type ister. CANON tipler sozlukten.
                        // Yalniz YERLESIMI OLAN struct (HasLayout): acik sablon (typeof(Nullable<>)) ve acik argumanli ornekler
                        // reflectedGenericDefs'in boyutsuz descriptor'unu alir; burada kaydedilirse sizeof(struct) + cift tanim.
                        bool structRef = op.PrimitiveRef != null && TypeShape.IsStructModel(op.PrimitiveRef) && !op.PrimitiveRef.IsGenericParameter
                            && HasLayout(op.PrimitiveRef) && !Canon.ContainsCanon(op.PrimitiveRef);
                        bool structOp = op.Type == OpType.TypeOf || op.Type == OpType.Box || op.Type == OpType.Unbox || op.Type == OpType.UnboxOrDefault
                            || op.Type == OpType.AsType || op.Type == OpType.CastClass || op.Type == OpType.IsType;
                        if (structRef && structOp)
                        {
                            var key = CName(op.PrimitiveRef.Name);
                            if (!reflectedValueStructs.ContainsKey(key))
                            {
                                reflectedValueStructs[key] = op.PrimitiveRef;
                                PoolAdd(op.PrimitiveRef.Display);
                                typeIndex[op.PrimitiveRef] = nextTypeIndex++;
                            }
                        }
                    }
                }
            // Tum somut kullanici struct'lari kutulanabilir: descriptor hepsine (corelib MiniCs yolu
            // `object o = item` icin acik Box op'u uretmez, Coerce'ta kutular -> &X_type hazir olmali).
            foreach (var p in ctx.AllPrimitives)
                if (TypeShape.IsStructModel(p) && p.GenericTemplate == null && p.GenericParameters.Count == 0
                    && !p.IsGenericParameter && !HasOpenTypeArgs(p) && p != Primitive.ValueType && !Canon.ContainsCanon(p) && TrimPlan.IsTypeLive(p))
                {
                    var key = CName(p.Name);
                    if (!reflectedValueStructs.ContainsKey(key))
                    {
                        reflectedValueStructs[key] = p;
                        typeNameCandidates.Add(p);
                        typeIndex[p] = nextTypeIndex++;
                    }
                }
            var seen = new HashSet<Primitive>(); // Type.name'ler: EmitTraceAndTypeOrdered gezisiyle birebir
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p) && !p.IsStruct)
                    CollectTypeNames(p, seen);
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                {
                    foreach (var f in ReflectionFields(p)) { PoolAdd(ReflectionFieldName(f)); PoolAttrStrings(MemberAttrs(p, f.Attributes)); }
                    foreach (var prop in ReflectionProperties(p)) { PoolAdd(prop.Name); PoolAttrStrings(MemberAttrs(p, prop.Attributes)); }
                    PoolAttrStrings(TypeAttrs(p));
                }
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c)) PoolAttrStrings(MemberAttrs(c.Owner, c.Attributes));
            foreach (var p in ctx.AllPrimitives) // enum'lar: descriptor + ToString uye adlari
                if (p.IsEnum && !typeIndex.ContainsKey(p) && TrimPlan.IsTypeLive(p))
                {
                    PoolAdd(p.Display);
                    foreach (var m in p.EnumMembers.Keys) PoolAdd(m);
                    typeIndex[p] = nextTypeIndex++;
                }
            // Diziler birinci sinif tip: elemani descriptor'lu ve canli olan HER kanonik T[] icin descriptor (kimlik + tahsis).
            // Ic ice (jagged) once icteki: ExportTypeSym(elem) o siradan sonra dolu olur.
            foreach (var arr in Primitive.InternedArrays.OrderBy(ArrayDepth).ToList())
                if (IsEmittableArray(arr))
                    RegisterArray(arr);
            // CANON sozluk hedefleri: exact T[] descriptor'lari (new T[]/typeof(T[]) canon govdeden) kimlik+tahsis icin sart
            foreach (var d in ctx.AllDictInstances)
                foreach (var tgt in d.Targets)
                    if (tgt is Primitive ap && ap.Type == PrimitiveType.Array && !Canon.ContainsCanon(ap))
                        RegisterArray(ap);
            // acik generic sablonlar: her kapali ornegin GenericTemplate'i (+ typeof(List<>) hedefleri) boyutsuz descriptor alir
            foreach (var p in ctx.AllPrimitives)
            {
                var tmpl = IsEmittableModel(p) ? p.InstantiatedFrom : (p.Type == PrimitiveType.Model && p.IsGeneric && p.GenericTemplate == null ? p : null);
                if (tmpl == null || tmpl.Type != PrimitiveType.Model) continue;
                var key = CName(tmpl.Name);
                if (reflectedGenericDefs.ContainsKey(key)) continue;
                reflectedGenericDefs[key] = tmpl;
                PoolAdd(tmpl.Display);
                typeIndex[tmpl] = nextTypeIndex++;
            }
            // Tip adlari EN SON: generic orneklemelerin adi yazilmaz (runtime generic_def + generic_args'tan lazy kurar);
            // karar typeIndex/reflected* tablolari tamamlaninca verilebilir (GenericArgSym).
            foreach (var p in typeNameCandidates)
                if (!LazyTypeName(p)) PoolAdd(p.Display);
            CollectMethods(ctx); // metot kayitlari (ad/dosya havuzu dahil) — typeIndex hazir olduktan sonra
        }

        // Adsiz descriptor: kapali generic class/struct, sablonu descriptor'lu ve TUM argumanlari descriptor'lu (ad kurulabilir).
        // Interface/delegate descriptor'lari generic_args tasimiyor -> adlari yazilir.
        static readonly List<Primitive> typeNameCandidates = new List<Primitive>();

        static int ArrayDepth(Primitive a) { int d = 0; for (var t = a; t != null && t.Type == PrimitiveType.Array; t = t.ElementType) d++; return d; }
        // T[] descriptor'u uretilsin mi: eleman somut (acik generic degil), descriptor'lu (ExportTypeSym != 0) ve canli.
        static bool IsEmittableArray(Primitive arr)
        {
            var e = arr.ElementType;
            if (e == null || e.IsGenericParameter || HasOpenTypeArgs(e) || e.Type == PrimitiveType.Pointer || e.Type == PrimitiveType.FixedArray || e.Type == PrimitiveType.Void) return false;
            if (e.Type == PrimitiveType.Model && e != Primitive.Object && e != Primitive.String && e != Primitive.ValueType && (!IsEmittableModel(e) || !TrimPlan.IsTypeLive(e))) return false;
            // InternedArrays Context'ler arasi statik: bu programa ait olmayan eleman tipi (selftest -> interp host) emit edilmez
            if (e.Type == PrimitiveType.Model && !(EmitCtx.TryGetPrimitive(e.Name, out var own) && own == e)) return false;
            return ExportTypeSym(e) != "0";
        }
        static bool IsReflectedArray(Primitive arr) => arr != null && arr.Type == PrimitiveType.Array && reflectedArrays.ContainsKey(CName(arr.Name));
        static void RegisterArray(Primitive arr)
        {
            var key = CName(arr.Name);
            if (reflectedArrays.ContainsKey(key)) return;
            reflectedArrays[key] = arr;
            PoolAdd(arr.Display);
            typeIndex[arr] = nextTypeIndex++;
        }
        static bool LazyTypeName(Primitive p) =>
            p.InstantiatedFrom != null && p.TypeArguments.Count > 0 && !p.IsInterface && !p.IsDelegate && !p.IsEnum
            && reflectedGenericDefs.ContainsKey(CName(p.InstantiatedFrom.Name))
            && p.TypeArguments.All(a => GenericArgSym(a) != "0");
        static string TypeNameRef(Primitive p) => LazyTypeName(p) || !TrimPlan.NeedsTypeName(p) ? "0" : "&" + strPool[p.Display];

        static void CollectTypeNames(Primitive p, HashSet<Primitive> done)
        {
            if (p == Primitive.Object || p == Primitive.String || !done.Add(p)) return;
            if (p.Parent != null && !p.Parent.IsStruct) CollectTypeNames(p.Parent, done);
            foreach (var iface in p.Interfaces) CollectTypeNames(iface, done);
            typeNameCandidates.Add(p); // Type.name: C# sozdizimli (dotted; generic: Box<int>) — havuza CollectStrings sonunda
            typeIndex[p] = nextTypeIndex++;
        }

        static readonly Dictionary<string, string> strSymText = new Dictionary<string, string>(); // sembol -> metin (hash cakisma kontrolu)
        static string PoolAdd(string s)
        {
            if (!strPool.TryGetValue(s, out var sym))
            {
                // Ad = icerik hash'i: build sirasindan bagimsiz, dosya basina kararli (artimli derleme). Cakisma: sonek.
                var baseSym = $"_strpool_{Fnv64(s):x16}";
                sym = baseSym;
                for (int n = 1; strSymText.TryGetValue(sym, out var other) && other != s; n++) sym = $"{baseSym}_{n}";
                strSymText[sym] = s;
                strPool[s] = sym;
            }
            return sym;
        }

        // gen_strings: havuz nesneleri dis baglantili (VmString), veri dizileri dosyaya ozel. Kullanan .c kendi extern'lerini yazar.
        static string EmitStringPool()
        {
            var sb = new StringBuilder();
            foreach (var kv in strPool)
            {
                var codes = kv.Key.Length > 0
                    ? string.Join(",", kv.Key.Select(ch => ((int)ch).ToString()))
                    : "0"; // C bos dizi yasak; length=0 oldugu icin icerik onemsiz
                sb.Append($"static const unsigned short {kv.Value}_d[] = {{ {codes} }};\n");
                // GCHeader alan sirasi: type,next,version,age,idhash - age=GC_IMMORTAL (sweep/trace gormez)
                sb.Append($"VmString {kv.Value} = {{ {{ &vmstring_type, 0, 0, GC_IMMORTAL, 0 }}, {kv.Key.Length}, {kv.Value}_d }};\n");
            }
            // UTF-8 C-string havuzu (MethodInfo.name/file): VmString basligi yok, yalin adlar tum orneklemelerce paylasilir.
            foreach (var kv in cstrPool)
                sb.Append($"const char {kv.Value}[] = {CUtf8Literal(kv.Key)};\n");
            return sb.ToString();
        }

        // ---- UTF-8 C-string havuzu (`_cs_<hash>`): reflection/trace adlari; icerik hash'li kararli sembol ----
        static readonly Dictionary<string, string> cstrPool = new Dictionary<string, string>();
        static readonly Dictionary<string, string> cstrSymText = new Dictionary<string, string>();
        static string CStrAdd(string s)
        {
            if (!cstrPool.TryGetValue(s, out var sym))
            {
                var baseSym = $"_cs_{Fnv64(s):x16}";
                sym = baseSym;
                for (int n = 1; cstrSymText.TryGetValue(sym, out var other) && other != s; n++) sym = $"{baseSym}_{n}";
                cstrSymText[sym] = s;
                cstrPool[s] = sym;
            }
            return sym;
        }
        // UTF-8 C literal: ASCII yazdirilabilir dogrudan, digerleri oktal kacis (\xHH acgozlu olur; oktal 3 haneyle biter).
        static string CUtf8Literal(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                if (b == '"' || b == '\\') sb.Append('\\').Append((char)b);
                else if (b >= 32 && b < 127) sb.Append((char)b);
                else sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            }
            return sb.Append('"').ToString();
        }
        static string StringExterns(IEnumerable<string> syms) =>
            string.Concat(syms.Distinct().OrderBy(s => s, StringComparer.Ordinal).Select(s => $"extern VmString {s};\n"));

        public static string CName(string name)
        {
            var sb = new StringBuilder();
            foreach (var c in name)
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return sb.ToString();
        }

        // Cagrilabilir C sembolu: extern P/Invoke ise secilen EntryPoint, degilse mangled ad.
        static string CSym(Code code) => code.ExternalSymbol != null && !NeedsPInvokeMarshal(code) ? code.ExternalSymbol : CName(code.EncodeName());

        // P/Invoke ([DllImport], ExternalSymbol dolu) + string parametre/donus: .NET'in LPUTF8Str
        // marshaling'i. Corelib extern'leri (ExternalSymbol yok) VmString* ile dogrudan konusur.
        static bool NeedsPInvokeMarshal(Code code) =>
            code.ExternalSymbol != null &&
            (code.ReturnType == Primitive.String || code.Arguments.Any(a => a.Type == Primitive.String && !a.IsRef && !a.IsOut));

        // Gercek native prototip (const char*) + ayni imzali managed sarmalayici: UTF-16 -> UTF-8 gecici
        // tampon (malloc), cagri, serbest; string donus UTF-8 -> VmString.
        static string EmitPInvokeWrapper(Code code)
        {
            var an = CArgNames(code, new HashSet<string>());
            string NativeType(Argument a) => a.Type == Primitive.String && !a.IsRef && !a.IsOut ? "const char*" : ParamCType(a);
            var nativePars = code.Arguments.Count == 0 ? "void" : string.Join(", ", code.Arguments.Select((a, i) => $"{NativeType(a)} {an[i]}"));
            var nativeRet = code.ReturnType == Primitive.String ? "const char*" : CType(code.ReturnType);
            var sb = new StringBuilder();
            sb.Append($"extern {nativeRet} {code.ExternalSymbol}({nativePars});\n");
            sb.Append($"static inline {Prototype(code)} {{\n");
            var callArgs = new List<string>();
            for (int i = 0; i < code.Arguments.Count; i++)
            {
                var a = code.Arguments[i];
                if (a.Type == Primitive.String && !a.IsRef && !a.IsOut)
                {
                    sb.Append($"    char* __u{i} = digitoyengine_to_utf8({an[i]});\n");
                    callArgs.Add($"__u{i}");
                }
                else
                    callArgs.Add(an[i]);
            }
            bool isVoid = code.ReturnType == null || code.ReturnType == Primitive.Void;
            string call = $"{code.ExternalSymbol}({string.Join(", ", callArgs)})";
            if (isVoid)
                sb.Append($"    {call};\n");
            else if (code.ReturnType == Primitive.String)
                sb.Append($"    VmString* __r = digitoyengine_from_utf8({call});\n");
            else
                sb.Append($"    {CType(code.ReturnType)} __r = {call};\n");
            for (int i = 0; i < code.Arguments.Count; i++)
                if (code.Arguments[i].Type == Primitive.String && !code.Arguments[i].IsRef && !code.Arguments[i].IsOut)
                    sb.Append($"    free(__u{i});\n");
            if (!isVoid)
                sb.Append("    return __r;\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        // ---- degisken adlandirma: uretilen C, kaynak arguman/local adlarini kullanir (a0/l0 degil) ----
        // Kaynak adini gecerli+benzersiz C tanimlayicisina indirger; adsiz -> fallback (a{i}/l{i}).
        // KURAL: kaynaktan gelen her ad `u_` oneki alir -> C anahtar sozcukleri, runtime makrolari (DIGITOYENGINE_*), platform
        // onceden-tanimli makrolari (`unix`/`linux`/`i386`, emscripten -std=gnu*) ve codegen temp'leri (__t/__s/a0/l0) ile
        // cakisma yapisal olarak imkansiz; rezerve liste YOK. `this` -> `self` (uretilen kod/trampoline sozlesmesi).
        static string CIdent(string name, string fallback, HashSet<string> used)
        {
            if (string.IsNullOrEmpty(name)) name = fallback;
            else if (name == "this") name = "self";
            else name = "u_" + CName(name);
            var n = name;
            int k = 2;
            while (!used.Add(n)) n = name + "_" + k++;
            return n;
        }

        // deterministik: ayni Code icin her cagri ayni adlari uretir (impl/sarmalayici/prototip tutarli)
        static string[] CArgNames(Code code, HashSet<string> used)
        {
            var names = new string[code.Arguments.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = CIdent(code.Arguments[i].Name, $"a{i}", used);
            return names;
        }

        static string[] CLocalNames(Code code, HashSet<string> used)
        {
            var names = new string[code.Locals.Count];
            for (int i = 0; i < names.Length; i++)
                names[i] = CIdent(i < code.LocalNames.Count ? code.LocalNames[i] : null, $"l{i}", used);
            return names;
        }

        // CIL skaler -> C: vmrt.h'deki cil_* typedef'leri (eslesme TEK yerde; ham C tipi uretilmez).
        static string ScalarCType(Primitive t)
        {
            switch (t.Type)
            {
                case PrimitiveType.Int: return "cil_int";
                case PrimitiveType.UInt: return "cil_uint";
                case PrimitiveType.Byte: return "cil_byte";
                case PrimitiveType.SByte: return "cil_sbyte";
                case PrimitiveType.Short: return "cil_short";
                case PrimitiveType.UShort: return "cil_ushort";
                case PrimitiveType.Char: return "cil_char";
                case PrimitiveType.Float: return "cil_float";
                case PrimitiveType.Double: return "cil_double";
                case PrimitiveType.Long: return "cil_long";
                case PrimitiveType.ULong: return "cil_ulong";
                case PrimitiveType.Bool: return "cil_bool";
                case PrimitiveType.Void: return "void";
                default: throw new Exception($"skaler olmayan tip: {t.Type}");
            }
        }

        // C struct etiketi: paylasimli exact ornek (List<string>) canon'un struct'ini kullanir (yerlesim ayni; K9). Descriptor sembolu (TypeSym) exact kalir.
        internal static string StructTag(Primitive t) => CName((t.IsSharedExact ? t.Canonical : t).Name);

        // yerel/parametre/donus tipi olarak kullanilan C tip adi. IsStruct=true -> gercek deger tipi
        // (GCHeader yok, pointer yok, kopyalanarak gecer); IsStruct=false -> GC heap'te referans (pointer).
        internal static string CType(Primitive t)
        {
            if (t == Primitive.Object) return "VmObject*"; // runtime koku (struct uretilmez)
            if (t == Primitive.String) return "VmString*"; // runtime string (struct uretilmez)
            if (t == Primitive.ValueType) return "VmObject*"; // boxed value type runtime koku
            if (t.IsCanon) return "VmObject*"; // CANON: ref-tip temsilcisi (runtime koku gibi; ->gc.type erisimi icin VmObject)
            if (t == Primitive.TypeHandle) return "const Type*";          // Lower: yigindaki descriptor
            if (t == Primitive.DictHandle) return "const void *const *";  // Lower: yigindaki sozluk
            if (t == Primitive.FnPtrHandle) return "const void*";         // Lower: yigindaki fn isaretcisi
            switch (TypeShape.Of(t))
            {
                case Shape.Delegate: return "VmDelegate*";                       // runtime delegate (struct uretilmez)
                case Shape.Struct: return $"struct {StructTag(t)}";
                case Shape.Class: case Shape.Interface: return $"struct {StructTag(t)}*";
                case Shape.Array: return "VmArray*";                              // GC'li dinamik dizi (len tasir, data ayri tampon)
                case Shape.FixedArray: return ScalarCType(t.ElementType) + "*";
                case Shape.Pointer: return CType(t.ElementType) + "*";           // ham pointer, GC disi
                case Shape.GenericParam: return $"struct {StructTag(t)}*"; // S1 oncesi: acik T sablon kodunda gorulebilir (emit edilmez)
                default: return ScalarCType(t);
            }
        }

        static string ArrayIndexSetup(CVal array, List<CVal> indices, string arrayName, int line)
        {
            var sb = new StringBuilder();
            sb.Append($"VmArray* {arrayName} = {array.Expr}; ");
            for (int i = 0; i < indices.Count; i++) sb.Append($"int {arrayName}i{i} = {indices[i].Expr}; ");
            sb.Append($"DIGITOYENGINE_NULLCHECK_AT({arrayName}, {line}); if ({arrayName}->rank != {indices.Count}) {{ DIGITOYENGINE_SETLINE({line}); DIGITOYENGINE_throw_bounds({indices.Count}, {arrayName}->rank); }} int {arrayName}off = 0; ");
            for (int i = 0; i < indices.Count; i++)
                sb.Append($"DIGITOYENGINE_BOUNDS_AT({arrayName}i{i}, {arrayName}->dims[{i}], {line}); {arrayName}off = {arrayName}off * {arrayName}->dims[{i}] + {arrayName}i{i}; ");
            return sb.ToString();
        }

        static string FieldDecl(PrimitiveField f)
        {
            if (f.Type.Type == PrimitiveType.FixedArray)
                return $"{ScalarCType(f.Type.ElementType)} {CName(f.Name)}[{f.Type.FixedSize}];";
            return $"{CType(f.Type)} {CName(f.Name)};";
        }

        // Descriptor'u (Type) ve meta'si olan tip. CANON ornegi (List<__Canon>) runtime tipi degildir: yerlesim/kod/trace fn verir
        // ama descriptor'u yoktur -> burada false; yerlesim sorusu HasLayout. Paylasimli exact (List<string>) descriptor'lu, yerlesimsiz.
        // DESCRIPTOR TRIM (lean): olu tipin (TrimPlan.IsTypeLive) descriptor'u/meta'si uretilmez — Common.dll gibi referanssiz
        // plugin'lerin binlerce generic orneklemesi yalniz veri olarak bile ~700 KB tasiyordu. Tip canliligi TrimAnalysis.MarkType.
        static bool IsEmittableModel(Primitive p) => HasLayout(p) && !p.IsCanonForm && TrimPlan.IsTypeLive(p);
        // Statik alan deposu olan tip: emit edilen modeller + runtime-tanimli String (descriptor'suz ama `string.Empty` gibi corelib
        // statigi tasir; cctor'u IsEmittableCode uzerinden zaten emit edilir).
        static bool HasStaticStorage(Primitive p) => IsEmittableModel(p) || (p == Primitive.String && TrimPlan.IsTypeLive(p));
        static bool HasLayout(Primitive p) => p.Type == PrimitiveType.Model && p.GenericTemplate == null && p.GenericParameters.Count == 0 && !HasOpenTypeArgs(p) && p != Primitive.Object && p != Primitive.String && p != Primitive.ValueType && p.Name != WellKnown.ValueType && !p.IsCanon; // corelib ValueType.cs = runtime vmvaluetype_type
        // Acik tip argumanli ornek (Task<T>, T = generic METHOD parametresi): generic method sablonunun
        // govdesinden sizar; somut cagri ayrica monomorfize edilir. Emit edilmez.
        static bool HasOpenTypeArgs(Primitive p)
        {
            foreach (var a in p.TypeArguments)
                if (a.IsGenericParameter || HasOpenTypeArgs(a) || (a.ElementType != null && (a.ElementType.IsGenericParameter || HasOpenTypeArgs(a.ElementType))))
                    return true;
            return false;
        }
        // iface methodlari govdesizdir; generic template'ler ve uyeleri yalniz klon kaynagi - ikisi de emit edilmez.
        // CANON: paylasimli exact kabuk (List<string>.Add) govdesizdir, kodu canon'da (K8).
        static bool IsEmittableCode(Code c) => c.GenericParameters.Count == 0 && !c.IsSharedExact && !(c.Owner != null && (c.Owner.IsInterface || c.Owner.IsGeneric || HasOpenTypeArgs(c.Owner))) && TrimPlan.IsCodeLive(c);

        // ---- CANON LOWERING: T'ye bagli bilgi yigindan gelir (Lower.cs). Asagidaki yardimcilar "T verildi mi" ile calisir; op/sozluk bilmez. ----
        // is/as/cast testi: T (runtime descriptor) verilmisse DIGITOYENGINE_is_any (iface/dizi/class ayrimi runtime), aksi statik
        static string TypeTestT(string T, Primitive target, string typeExpr, string objExpr) =>
            T != null ? $"DIGITOYENGINE_is_any((GCHeader*){objExpr}, {T})" : TypeTest(target, typeExpr, objExpr);
        // kutudan deger: T verilmisse exact descriptor'la runtime unbox (canon struct hedefi), aksi statik
        static string UnboxValueT(string T, Primitive type, string objExpr)
        {
            if (T == null) return UnboxValue(type, objExpr);
            string ct = TypeShape.IsStructModel(type) ? $"struct {StructTag(type)}" : ScalarCType(type);
            return $"(*({ct}*)digitoyengine_unbox((GCHeader*)({objExpr}), {T}))";
        }
        // Statik alan lvalue'su: T verilmisse exact Type.statics blogu uzerinden (K10), aksi dogrudan sembol
        static string StaticRefT(string T, PrimitiveField f) => T != null
            ? $"((struct {StaticsStructTag(f.Owner)}*){T}->statics)->{CName(f.Name)}"
            : StaticSym(f);
        // TranspileProgram / TranspileFiles: CTranspiler.Units.cs (birim tabanli emisyon)

        static string StaticSym(PrimitiveField f)
        {
            if (f.Owner.IsCanonForm) throw new Exception($"canon tipin statigi dogrudan adreslenemez (sozlukten gelmeli): {f.Owner.Name}.{f.Name}");
            return f.Owner.IsSharedExact ? $"{StaticsSym(f.Owner)}.{CName(f.Name)}" : $"{CName(f.Owner.Name)}_s_{CName(f.Name)}";
        }

        static void EmitStructOrdered(StringBuilder sb, Primitive p, HashSet<Primitive> emitted)
        {
            if (!IsEmittableModel(p) || p.IsDelegate || !emitted.Add(p)) return;
            foreach (var field in p.Fields)
                if (TypeShape.IsStructModel(field.Type))
                    EmitStructOrdered(sb, field.Type, emitted);
            sb.Append(EmitStruct(p));
        }

        // generic_args reflection tablosu: descriptor'i OLAN tip argumanlari adreslenir; array/struct/
        // pointer gibi descriptor'suz argumanlar 0 (runtime bunu "bilinmiyor" sayar; link hatasi yerine).
        static string GenericArgSym(Primitive a)
        {
            bool has = a == Primitive.Object || a == Primitive.String || a == Primitive.ValueType
                || PrimTypeSym(a) != null
                || (a.IsEnum && typeIndex.ContainsKey(a))
                || (IsEmittableModel(a) && !a.IsStruct)
                || (a.IsStruct && reflectedValueStructs.ContainsKey(CName(a.Name)))
                || (a.Type == PrimitiveType.Array && reflectedArrays.ContainsKey(CName(a.Name)));
            return has ? $"&{TypeSym(a)}" : "0";
        }

        // Dizi descriptor'larinin base'i: corelib System.Array (emit ediliyorsa), yoksa Object.
        static string ArrayBaseSym() =>
            EmitCtx.TryGetPrimitive("System.Array", out var a) && IsEmittableModel(a) ? $"&{TypeSym(a)}" : "&vmobject_type";

        // Type descriptor sembolu: Object/String/primitive'ler runtime'da, gerisi uretilen {CName}_type
        static string TypeSym(Primitive p) =>
            p == Primitive.Object ? "vmobject_type" :
            p == Primitive.String ? "vmstring_type" :
            p == Primitive.ValueType ? "vmvaluetype_type" :
            p == Primitive.Void ? "vmvoid_type" :
            Canon.ContainsCanon(p) ? throw new Exception($"canon tipin descriptor'u yok (sozlukten gelmeli): {p.Name}") :
            PrimTypeSym(p) ?? CName(p.Name) + "_type";

        static string EmitValueStructType(Primitive type)
        {
            var cn = CName(type.Name);
            var sb = new StringBuilder();
            string genericArgs = "0", genericArgCount = "0";
            if (type.TypeArguments.Count > 0)
            {
                sb.Append($"static const Type* const {cn}_generic_args[] = {{ ");
                sb.Append(string.Join(", ", type.TypeArguments.Select(GenericArgSym)));
                sb.Append(" };\n");
                genericArgs = cn + "_generic_args";
                genericArgCount = type.TypeArguments.Count.ToString();
            }
            // Kutu yerlesimi: [GCHeader][struct]. GC tarama = Type.refmap (payload ofsetleri; kutu trace'i header'i atlar),
            // ref yoksa atomic=1. CANON: paylasimli exact struct canon'un tablosunu gosterir (RefMapSym -> StructTag).
            var refmap = RefMap.Of(type);
            var st = StructTag(type);
            // Kutu vtable'i (Object slot sozlesmesi: 0=GetHashCode 1=Equals 2=ToString): struct override'i varsa kutuyu acan
            // thunk, yoksa ValueType varsayilani (payload memcmp/hash; ToString = tip adi). `((object)s).Equals(x)` ve
            // constrained fallback (kutula + callvirt) buradan dispatch eder.
            var vt = new List<string>();
            for (int i = 0; i < Primitive.Object.VTable.Count; i++)
            {
                var slotMethod = Primitive.Object.VTable[i];
                var slotName = slotMethod.SimpleName;
                var ov = EmitCtx.MembersOf(type).FirstOrDefault(c => c.IsOverride && Signature.Matches(c, slotMethod) && IsEmittableCode(c.Shared));
                if (ov != null)
                {
                    var an = CArgNames(ov, new HashSet<string>());
                    var pars = string.Join("", ov.Arguments.Skip(1).Select((a, k) => $", {ParamCType(a)} {an[k + 1]}"));
                    var args = string.Join("", ov.Arguments.Skip(1).Select((a, k) => $", {an[k + 1]}"));
                    // CANON: exact struct metodu ctx ister -> sozluk ilk arguman (stub'a gerek yok, thunk zaten burada)
                    var dictArg = ov.IsSharedExact && ov.Shared.NeedsDictParam ? DictSymOf(ov) + ", " : "";
                    sb.Append($"static {CType(ov.ReturnType)} {cn}_boxvt{i}(VmObject* o{pars}) {{ return {CSym(ov.Shared)}({dictArg}(struct {st}*)((char*)o + sizeof(GCHeader)){args}); }}\n");
                    vt.Add($"(const void*)&{cn}_boxvt{i}");
                }
                else vt.Add(slotName == "GetHashCode" ? "(const void*)&digitoyengine_valuetype_hash"
                    : slotName == "Equals" ? "(const void*)&digitoyengine_valuetype_eq"
                    : $"(const void*)&{CName(Primitive.Object.VTable[i].EncodeName())}");
            }
            sb.Append($"static const void* const {cn}_vtable[{vt.Count}] = {{ {string.Join(", ", vt)} }};\n");
            sb.Append($"Type {cn}_type = {{ 0, 0, sizeof(GCHeader) + sizeof(struct {st}), {(refmap.IsEmpty ? 1 : 0)}, &vmvaluetype_type, {TypeNameRef(type)}, {cn}_vtable, {vt.Count}, 0, 0, 0, 0, {(HasReflectionMembers(type) ? cn + "_members" : "0")}, {ReflectionMemberCount(type)}, {genericArgs}, {genericArgCount}{TypeMetaTail(type)}{(refmap.IsEmpty ? "" : $", .refmap = {RefMapSym(type)}")} }};\n");
            return sb.ToString();
        }

        // T[] descriptor'u: kimlik + tahsis tipi. Trace ortak (digitoyengine_trace_array: eleman refmap'i), atomic yalniz ref'siz deger elemanda.
        static string EmitArrayType(Primitive array)
        {
            var elem = array.ElementType;
            int atomic = !TypeShape.IsGcRef(elem) && RefMap.Of(elem).IsEmpty ? 1 : 0;
            return $"Type {TypeSym(array)} = {{ digitoyengine_trace_array, finalize_vmarray, sizeof(VmArray), {atomic}, {ArrayBaseSym()}, &{strPool[array.Display]}, vmobject_vtable, 3, 0, 0, 0, .hash = {Fnv64(array.Name)}ull, .flags = DIGITOYENGINE_TYPE_ARRAY, .elem_type = {ExportTypeSym(elem)}, .rank = {Math.Max(1, array.ArrayRank)} }};\n";
        }

        static string PrimTypeSym(Primitive p)
        {
            if (p.IsEnum) return null; // enum Int tabanli ama kendi Type'i olmali (henuz yok - typeof frontend'de reddediyor)
            switch (p.Type)
            {
                case PrimitiveType.Int: return "vmint32_type";
                case PrimitiveType.UInt: return "vmuint32_type";
                case PrimitiveType.Long: return "vmint64_type";
                case PrimitiveType.ULong: return "vmuint64_type";
                case PrimitiveType.Short: return "vmint16_type";
                case PrimitiveType.UShort: return "vmuint16_type";
                case PrimitiveType.SByte: return "vmsbyte_type";
                case PrimitiveType.Byte: return "vmbyte_type";
                case PrimitiveType.Char: return "vmchar_type";
                case PrimitiveType.Bool: return "vmbool_type";
                case PrimitiveType.Float: return "vmsingle_type";
                case PrimitiveType.Double: return "vmdouble_type";
                default: return null;
            }
        }

        // delegate descriptor'i: paylasimli VmDelegate yerlesimi, trace runtime'da; C# zinciri MulticastDelegate->Delegate->Object;
        // vtable = runtime delegate vtable'i (Equals = hedef+metot, GetHashCode, ToString)
        static string EmitDelegateType(Primitive p) =>
            $"Type {CName(p.Name)}_type = {{ digitoyengine_delegate_trace, 0, sizeof(VmDelegate), 0, &vmmulticastdelegate_type, &{strPool[p.Display]}, digitoyengine_delegate_vtable, 3, 0, 0, 0{TypeMetaTail(p)} }};\n";

        // enum descriptor'i: ToString uye adi dondurur (dotnet Enum.ToString; tanimsiz deger -> sayi),
        // hash/eq ortak govdeler (corelib.c), alias = underlying int (unbox denkligi).
        // Tanimli uyelerin box'lari ONURETILIR (immortal, sifir alloc); tanimsiz deger alloc fallback.
        static string EmitEnumReflection(Primitive p)
        {
            var cn = CName(p.Name);
            var sb = new StringBuilder();
            sb.Append($"static int digitoyengine_enumparse_{cn}(VmString *value, int ignoreCase, int *result) {{\n");
            foreach (var m in p.EnumMembers)
                sb.Append($"    if (digitoyengine_string_eq_cmp(value, &{strPool[m.Key]}, ignoreCase)) {{ *result = {m.Value}; return 1; }}\n");
            sb.Append("    *result = 0; return 0;\n}\n");
            sb.Append($"static VmString *digitoyengine_enumstr_{cn}(VmObject *s) {{\n");
            sb.Append("    switch (*(int*)((char*)s + sizeof(GCHeader))) {\n");
            var vals = new List<int>(); // ayni degerli ikinci uye: ilk bildirilen kazanir
            foreach (var m in p.EnumMembers)
                if (!vals.Contains(m.Value))
                {
                    vals.Add(m.Value);
                    sb.Append($"    case {m.Value}: return &{strPool[m.Key]};\n");
                }
            sb.Append("    default: return digitoyengine_int_str(*(int*)((char*)s + sizeof(GCHeader)));\n    }\n}\n");
            sb.Append($"static const void *const {cn}_vtable[3] = {{ (const void*)&digitoyengine_enumbox_hash, (const void*)&digitoyengine_enumbox_eq, (const void*)&digitoyengine_enumstr_{cn} }};\n");
            sb.Append($"Type {cn}_type = {{ 0, 0, sizeof(GCHeader) + 4, 1, &vmenum_type, &{strPool[p.Display]}, {cn}_vtable, 3, 0, 0, 0, &vmint32_type, 0, 0, 0, 0, digitoyengine_enumparse_{cn}{TypeMetaTail(p)} }};\n");
            return sb.ToString();
        }
        // C# kimligi: enum kutulama daima AYRI nesne (cache yok) - (object)E.X == (object)E.X false. Header'da inline (her birim kullanir).
        static string EnumBoxInline(Primitive p) =>
            $"static inline VmObject *digitoyengine_ebox_{CName(p.Name)}(int v) {{ return digitoyengine_box_enum(v, &{CName(p.Name)}_type); }}\n";

        static string WithPostCallRemember(string call, Primitive ret, CVal[] values) => call; // tek artimli GC: remembered set yok, cagri sonrasi is yok

        // WRITE BARRIER (vmrt.h R3/R4): heap slotuna yazimdan SONRA slot adresi verilir; icerigi GC okur. Ref slot -> BARRIER_REF,
        // ref tasiyan struct slot -> BARRIER_MAP(refmap). Ref icermeyen tip: bos. Sahip (owner) hicbir zaman sezilmez.
        // AOT_NOBARRIER=1: bariyer emisyonu kapali (yalniz Demo51/Demo97'nin hatayi yakaladigini kanitlamak icin).
        static readonly bool NoBarrier = Environment.GetEnvironmentVariable("AOT_NOBARRIER") == "1";

        static string Barrier(string slotExpr, Primitive slotType)
        {
            if (NoBarrier || slotType == null) return "";
            if (TypeShape.IsGcRef(slotType)) return $" DIGITOYENGINE_BARRIER_REF(&({slotExpr}));";
            if (TypeShape.IsStructModel(slotType) && !RefMap.Of(slotType).IsEmpty) return $" DIGITOYENGINE_BARRIER_MAP(&({slotExpr}), {RefMapSym(slotType)});";
            return "";
        }

        // GC refmap tablosu sembolu: yerlesim basina bir tablo; paylasimli exact canon'unkini kullanir (StructTag ayni).
        static string RefMapSym(Primitive t) => StructTag(t) + "_refmap";

        // Tablo tanimi: { n, offsetof(struct T, a.b.c)... } — ofsetleri C derleyicisi hesaplar (padding), tek kaynak RefMap.
        static string EmitRefMapDef(Primitive t)
        {
            var map = RefMap.Of(t);
            if (map.IsEmpty) return "";
            var st = StructTag(t);
            var offs = map.Paths.Select(path => $"offsetof(struct {st}, {string.Join(".", path.Select(f => CName(f.Name)))})");
            return $"_Static_assert(sizeof(struct {st}) <= 0xFFFF, \"refmap: struct too large\");\nconst unsigned short {RefMapSym(t)}[] = {{ {map.Count}, {string.Join(", ", offs)} }};\n";
        }
        static string EmitRefMapDecl(Primitive t) => RefMap.Of(t).IsEmpty ? "" : $"extern const unsigned short {RefMapSym(t)}[];\n";

        // Kullanici struct kutulama: deger gecici'ye alinir, payload kopyalanir (statement-expression).
        static string BoxStruct(Primitive st, string expr)
        {
            var cn = CName(st.Name);
            var tag = StructTag(st);
            return $"({{ struct {tag} __bx = {expr}; (VmObject*)digitoyengine_box_struct(&{cn}_type, &__bx, (int)sizeof(struct {tag})); }})";
        }

        // Deger tipi kutulama TEK kapi (enum / skaler helper / struct / Nullable<T>). .NET: Nullable<T> kutusu T kutusudur
        // (HasValue=false -> null); runtime'da Nullable kutusu HIC olmaz.
        static string BoxValue(Primitive type, string expr)
        {
            if (type.IsEnum) return $"digitoyengine_ebox_{CName(type.Name)}({expr})";
            if (BoxHelperFor(type) is string helper) return $"{helper}({expr})";
            if (NullableValueType(type, out var inner))
                return $"({{ {CType(type)} __nb = {expr}; __nb.hasValue ? (VmObject*){BoxValue(inner, "__nb.value")} : (VmObject*)0; }})";
            if (TypeShape.IsStructModel(type)) return BoxStruct(type, expr);
            throw new Exception($"boxing desteklenmeyen tip: {type.Name}");
        }
        // Kutudan deger: Nullable<T> hedefi null kutuyu {0} yapar, T kutusunu sarar.
        static string UnboxValue(Primitive type, string objExpr)
        {
            if (NullableValueType(type, out var inner))
                return $"({{ VmObject* __ub = (VmObject*)({objExpr}); __ub ? ({CType(type)}){{ 1, {UnboxValue(inner, "__ub")} }} : ({CType(type)}){{0}}; }})";
            string ct = TypeShape.IsStructModel(type) ? $"struct {StructTag(type)}" : ScalarCType(type);
            return $"(*({ct}*)digitoyengine_unbox((GCHeader*)({objExpr}), &{TypeSym(type)}))";
        }
        // is/as hedefi deger tipiyse kutunun RUNTIME tipi: Nullable<T> -> T
        static Primitive BoxedTypeOf(Primitive t) => NullableValueType(t, out var inner) ? inner : t;
        // Nullable<T> yerlesim sozlesmesi (corelib Nullable.cs): {hasValue, value}
        static bool NullableValueType(Primitive type, out Primitive valueType)
        {
            valueType = null;
            if (type == null || !TypeShape.IsStructModel(type) || !type.Name.StartsWith(WellKnown.Nullable + "`1<")) return false;
            var field = type.Fields.FirstOrDefault(f => f.Name == "value");
            if (field == null) return false;
            valueType = field.Type;
            return true;
        }

        // deger tipi -> box helper (corelib.c); enum/model/dizi icin null
        static string BoxHelperFor(Primitive p)
        {
            if (p.IsEnum) return null;
            switch (p.Type)
            {
                case PrimitiveType.Int: return "digitoyengine_box_i32";
                case PrimitiveType.UInt: return "digitoyengine_box_u32";
                case PrimitiveType.Long: return "digitoyengine_box_i64";
                case PrimitiveType.ULong: return "digitoyengine_box_u64";
                case PrimitiveType.Short: return "digitoyengine_box_i16";
                case PrimitiveType.UShort: return "digitoyengine_box_u16";
                case PrimitiveType.SByte: return "digitoyengine_box_i8";
                case PrimitiveType.Byte: return "digitoyengine_box_u8";
                case PrimitiveType.Char: return "digitoyengine_box_char";
                case PrimitiveType.Bool: return "digitoyengine_box_bool";
                case PrimitiveType.Float: return "digitoyengine_box_f32";
                case PrimitiveType.Double: return "digitoyengine_box_f64";
                default: return null;
            }
        }

        // is/as/cast runtime testi: class hedefi kalitim zinciri (DIGITOYENGINE_is), iface hedefi itable arama (DIGITOYENGINE_implements).
        // objExpr: GCHeader*/nesne ifadesi (dizi hedefi VmArray.elem'e bakar), typeExpr: nesnenin gc.type'i.
        static string TypeTest(Primitive target, string typeExpr, string objExpr = null) =>
            target.Type == PrimitiveType.Array ? $"digitoyengine_is_array((const GCHeader*){objExpr}, {ExportTypeSym(target.ElementType)}, {Math.Max(1, target.ArrayRank)})"
            : target.IsInterface ? $"DIGITOYENGINE_implements({typeExpr}, &{TypeSym(target)})"
                                 : $"DIGITOYENGINE_is({typeExpr}, &{TypeSym(BoxedTypeOf(target))})"; // Nullable<T> kutusu = T kutusu

        // digitoyengine_init: (1) ref tasiyan static'leri GC kok tablosuna kaydet (adres + refmap; R2), (2) cctor'lari kayit sirasiyla kos.
        // Host main'i baska bir sey yapmadan ONCE cagirmali.
        static string EmitInit(Context ctx)
        {
            var sb = new StringBuilder();
            var roots = new List<string>();
            foreach (var p in ctx.AllPrimitives.Concat(new[] { Primitive.String })) // String AllPrimitives'te yok (runtime tipi)
                if (HasStaticStorage(p))
                    foreach (var f in p.StaticFields)
                    {
                        if (TypeShape.IsGcRef(f.Type)) roots.Add($"{{ (void*)&{StaticSym(f)}, 0 }}");
                        else if (TypeShape.IsStructModel(f.Type) && !RefMap.Of(f.Type).IsEmpty) roots.Add($"{{ (void*)&{StaticSym(f)}, {RefMapSym(f.Type)} }}");
                    }
            if (roots.Count > 0)
                sb.Append($"static const GcStaticRoot digitoyengine_static_roots[{roots.Count}] = {{\n    {string.Join(",\n    ", roots)}\n}};\n");
            sb.Append("void digitoyengine_init(void) {\n");
            if (roots.Count > 0)
                sb.Append($"    gc_add_static_roots(digitoyengine_static_roots, {roots.Count});\n");
            // object/string sanal methodlari: runtime'daki yazilabilir vtable dizilerine impl adresleri
            if (Primitive.Object.VTable.Count > 3 || Primitive.String.VTable.Count > 3)
                throw new Exception("vmobject/vmstring vtable[3] tasti: vmrt.h dizilerini corelib VTable ile buyut");
            for (int i = 0; i < Primitive.Object.VTable.Count; i++)
                sb.Append($"    vmobject_vtable[{i}] = (const void*)&{CName(Primitive.Object.VTable[i].EncodeName())};\n");
            for (int i = 0; i < Primitive.String.VTable.Count; i++)
                sb.Append($"    vmstring_vtable[{i}] = (const void*)&{CName(Primitive.String.VTable[i].EncodeName())};\n");
            // reflection: wrapper descriptor'lari + boxing cache'i
            sb.Append("    digitoyengine_box_init();\n");
            if (ctx.TryGetPrimitive("System.Type", out var sysType) &&
                ctx.TryGetPrimitive("System.Reflection.FieldInfo", out var fieldInfo) &&
                ctx.TryGetPrimitive("System.Reflection.PropertyInfo", out var propertyInfo) &&
                ctx.TryGetPrimitive("System.Reflection.MethodInfo", out var methodInfo) &&
                ctx.TryGetPrimitive("System.Reflection.ConstructorInfo", out var ctorInfo))
                sb.Append($"    digitoyengine_reflect_init(&{TypeSym(sysType)}, &{TypeSym(fieldInfo)}, &{TypeSym(propertyInfo)}, &{TypeSym(methodInfo)}, &{TypeSym(ctorInfo)});\n");
            // diziler: runtime tahsis tipleri System.Array'den turer (is Array / (Array)x / Type.BaseType)
            if (ctx.TryGetPrimitive("System.Array", out var sysArray) && IsEmittableModel(sysArray))
                sb.Append($"    digitoyengine_array_init(&{TypeSym(sysArray)});\n");
            // runtime exception kind'lari: catch aninda TAZE nesne (DIGITOYENGINE_ex_materialize); singleton
            // yeniden kullanimi izi ezip orijini kaybettiriyordu. Firlatma hala alloc'suz; alloc yalniz catch'te.
            var notImpl = ctx.AllPrimitives.FirstOrDefault(p => p.Name == "System.NotImplementedException");
            var exMsgCtor = ctx.TryGetPrimitive("System.Exception", out var sysEx) ? ctx.MembersOf(sysEx).FirstOrDefault(c => c.SimpleName == "ctor" && c.ParamCount == 1 && c.Arguments[1].Type == Primitive.String) : null;
            if (notImpl != null && exMsgCtor != null)
            {
                sb.Append($"    DIGITOYENGINE_notimpl_type = &{TypeSym(notImpl)};\n");
                sb.Append($"    DIGITOYENGINE_exception_ctor_msg = (void (*)(GCHeader*, VmString*))&{CName(exMsgCtor.EncodeName())};\n");
            }
            for (int k = 1; k < ctx.ExceptionKindTypes.Length; k++)
                if (ctx.ExceptionKindTypes[k] != null)
                {
                    var kt = ctx.ExceptionKindTypes[k];
                    sb.Append($"    DIGITOYENGINE_ex_kind_type[{k}] = &{TypeSym(kt)};\n");
                    var kctor = ctx.FindMember(kt, "ctor", 0); // kind ctor'u C# default mesajini atar
                    if (kctor != null)
                        sb.Append($"    DIGITOYENGINE_ex_kind_ctor[{k}] = (void (*)(GCHeader*))&{CName(kctor.EncodeName())};\n");
                }
            // cctor sirasi: .NET'te beforefieldinit statik alan erisiminde tetiklenir; burada hepsi eager
            // kosar. Bir cctor baska tipin statik alanini okuyorsa (dogrudan ya da cagirdigi statik
            // metodlar uzerinden) o tipin cctor'u ONCE kosmali — ornegin AnimRegistry..cctor lambdalari
            // <>c.<>9 singleton'u uzerinden delegate yapar; <>9 null kalirsa delegate target'siz dogar
            // ve cagri ABI'si kayar (crash). Dongu -> kayit sirasi korunur.
            var cctors = InitCctors(ctx);
            var cctorOf = new Dictionary<Primitive, Code>();
            foreach (var cc in cctors)
                cctorOf[cc.Owner] = cc;
            HashSet<Primitive> StaticDeps(Code root)
            {
                var owners = new HashSet<Primitive>();
                var seen = new HashSet<Code>();
                var stack = new Stack<Code>();
                stack.Push(root.Shared); // CANON: exact cctor kabugu govdesiz, bagimliliklar canon govdeden
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    if (!seen.Add(c)) continue;
                    foreach (var op in c.Operations)
                    {
                        if (op.Field != null && (op.Type == OpType.GetStatic || op.Type == OpType.SetStatic || op.Type == OpType.AddrStatic)
                            && op.Field.Owner != null && op.Field.Owner != root.Owner)
                            owners.Add(op.Field.Owner);
                        if (op.Code != null && op.Code.IsStatic && op.Code.Name != "cctor" && !op.Code.IsExternal)
                            stack.Push(op.Code);
                    }
                }
                return owners;
            }
            var emitted = new HashSet<Code>();
            var visiting = new HashSet<Code>();
            void EmitCctor(Code cc)
            {
                if (emitted.Contains(cc) || !visiting.Add(cc)) return;
                foreach (var owner in StaticDeps(cc))
                    if (cctorOf.TryGetValue(owner, out var dep) && dep != cc)
                        EmitCctor(dep);
                visiting.Remove(cc);
                emitted.Add(cc);
                // CANON: exact tipin cctor'u canon govde + exact sozluk (statikler exact bloga yazilir)
                sb.Append(cc.IsSharedExact ? $"    {CSym(cc.Shared)}({DictSymOf(cc)});\n" : $"    {CName(cc.EncodeName())}();\n");
            }
            foreach (var cc in cctors)
                EmitCctor(cc);
            sb.Append("}\n");
            return sb.ToString();
        }

        // EmitInit'in baslangicta eager cagiracagi cctor'lar (trim planina gore canli olanlar). Stub kapisi da bu listeyi kok sayar.
        // CANON: canon cctor (List<__Canon>..cctor) dogrudan kosmaz; her exact ornegin cctor kabugu canon govdeyi kendi sozluguyle kosar.
        static List<Code> InitCctors(Context ctx) =>
            ctx.AllCodes.Where(c => c.SimpleName == "cctor" && !c.IsExternal &&
                (c.IsSharedExact ? (IsEmittableCode(c.Shared) && IsEmittableModel(c.Owner)) : (IsEmittableCode(c) && !c.IsCanonCode))).ToList();

        // Runtime'in cagri grafi disinda dogrudan cagirdigi kokler: eager cctor'lar, finalizer'lar (digitoyengine_fin_X zinciri),
        // exception kind ctor'lari (DIGITOYENGINE_ex_kind_ctor). Program.ReachableStubs (stub kapisi) host girislerine ekler.
        public static IEnumerable<Code> InitRoots(Context ctx)
        {
            foreach (var c in InitCctors(ctx)) yield return c;
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c) && !c.IsExternal && c.SimpleName == "finalize") yield return c;
            for (int k = 1; k < ctx.ExceptionKindTypes.Length; k++)
                if (ctx.ExceptionKindTypes[k] is Primitive kt && ctx.FindMember(kt, "ctor", 0) is Code kctor && IsEmittableCode(kctor)) yield return kctor;
        }

        static void EmitTraceAndTypeOrdered(StringBuilder sb, Primitive p, HashSet<Primitive> done)
        {
            if (p == Primitive.Object || p == Primitive.String || !IsEmittableModel(p) || !done.Add(p)) return; // runtime tipleri (ValueType dahil)
            if (p.IsDelegate)
            {
                sb.Append(EmitDelegateType(p));
                return;
            }
            if (p.Parent != null && !p.Parent.IsStruct)
                EmitTraceAndTypeOrdered(sb, p.Parent, done);
            foreach (var iface in p.Interfaces) // itables &IFoo_type'a isaret eder -> once tanimlanmali
                EmitTraceAndTypeOrdered(sb, iface, done);
            foreach (var argument in p.TypeArguments)
                if (IsEmittableModel(argument) && !argument.IsStruct)
                    EmitTraceAndTypeOrdered(sb, argument, done);
            sb.Append(EmitTraceAndType(p));
        }

        // GCHeader ILK alan olmali (vmrt.h konvansiyonu: struct X* <-> GCHeader* castlenebilir).
        // IsStruct=true modeller GERCEK C value type'i: GCHeader yok, GC'ye hic girmez, kopyalanir.
        // Kalitimda alanlar DUZLESTIRILIR (parent once) -> Derived* prefix-uyumlu Base* (upcast bedava).
        static string EmitStruct(Primitive model)
        {
            var sb = new StringBuilder();
            sb.Append($"typedef struct {StructTag(model)} {{\n");
            if (!model.IsStruct)
                sb.Append("    GCHeader gc;\n");
            foreach (var f in Hierarchy.AllFields(model))
                sb.Append("    " + FieldDecl(f) + "\n");
            sb.Append($"}} {CName(model.Name)};\n");
            return sb.ToString();
        }

        // GC yerlesim bilgisi: RefMap.cs (tek kaynak). Root kaydi (add_root/frame_root) BILINCLI OLARAK YOK: GC yalniz oyun
        // dongusundeki guvenli noktada (hicbir transpile edilmis cagri C stack'inde degilken) tetikleniyor, o an local'ler
        // zaten olu; sadece kalici/root'tan ulasilan nesneler canli sayiliyor.

        // finalizer bildiren en yakin tip (self dahil zincir); ~X yoksa null
        static Primitive FindFinalizerDeclarer(Context ctx, Primitive model)
        {
            for (var t = model; t != null && !t.IsStruct; t = t.Parent)
                if (t != Primitive.Object && t != Primitive.String && ctx.FindMember(t, "finalize", 0) != null)
                    return t;
            return null;
        }

        // vtable/itable girisi: trim'de olu (erisilemez) sanal -> 0 (cagrilirsa analiz hatasi; null cagri aninda cokme). Bkz. VSlotRefOf (canon).

        static string EmitTraceAndType(Primitive model)
        {
            var cn = CName(model.Name);
            var st = StructTag(model);
            // interface: instantiate edilmez -> ciplak Type. Adresi hem iface KIMLIGI (DIGITOYENGINE_implements/DIGITOYENGINE_itable
            // karsilastirmasi) hem is/as hedefi olarak kullanilir.
            if (model.IsInterface)
                return $"Type {cn}_type = {{ 0, 0, 0, 1, 0, &{strPool[model.Display]}, 0, 0, 0, 0, 0{TypeMetaTail(model)} }};\n";
            var refmap = RefMap.Of(model); // GC tarama: Type.refmap (nesne basindan ofsetler); CANON: exact canon'un tablosunu gosterir
            var sb = new StringBuilder();
            string vtable = "0", nvtable = "0";
            if (model.VTable.Count > 0)
            {
                sb.Append($"static const void* const {cn}_vtable[] = {{ ");
                // GVM canon slotu (<__Canon>): dispatcher (CTranspiler.Canon GvmSlotRef); diger slotlar fn/stub/sozluk sentinel'i
                sb.Append(string.Join(", ", model.VTable.Select((m, i) => IsCanonGvmSlot(m) ? GvmSlotRef(model, null, model.VTable, model.VTable, i) : VSlotRef(model, m))));
                sb.Append(" };\n");
                vtable = cn + "_vtable";
                nvtable = model.VTable.Count.ToString();
            }
            string baseType = model.Parent != null && !model.Parent.IsStruct ? $"&{TypeSym(model.Parent)}" : "0";
            string itables = "0", nitables = "0";
            var allIfaces = Hierarchy.AllInterfaces(model); // duzlestirilmis (vmrt.h sozlesmesi: base'inkiler dahil)
            if (allIfaces.Count > 0)
            {
                foreach (var iface in allIfaces)
                {
                    sb.Append($"static const void* const {cn}_it_{CName(iface.Name)}[] = {{ ");
                    var itab = model.ITables[iface];
                    sb.Append(string.Join(", ", itab.Select((m, i) => IsCanonGvmSlot(iface.VTable[i]) ? GvmSlotRef(model, iface, iface.VTable, itab, i) : VSlotRefOf(m))));
                    sb.Append(" };\n");
                }
                sb.Append($"static const IfaceImpl {cn}_itables[] = {{ ");
                sb.Append(string.Join(", ", allIfaces.Select(i => $"{{ &{CName(i.Name)}_type, {cn}_it_{CName(i.Name)} }}")));
                sb.Append(" };\n");
                itables = cn + "_itables";
                nitables = allIfaces.Count.ToString();
            }
            int atomic = refmap.IsEmpty ? 1 : 0;
            // finalizer zinciri: C# semantigi - turemisin ~'si once, sonra base'inki (erken return dahil,
            // cunku zincir methodun DISINDA thunk'ta). Bildirmeyen tip en yakin atanin thunk'ini kullanir.
            string finFn = "0";
            var declarer = FindFinalizerDeclarer(EmitCtx, model);
            if (declarer == model)
            {
                var baseDecl = model.Parent != null ? FindFinalizerDeclarer(EmitCtx, model.Parent) : null;
                sb.Append($"void digitoyengine_fin_{cn}(GCHeader* o) {{\n");
                sb.Append($"    {CSym(EmitCtx.FindMember(model, "finalize", 0).Shared)}((struct {st}*)o);\n"); // finalize = class instance metodu (sozluk this'ten)
                if (baseDecl != null)
                    sb.Append($"    digitoyengine_fin_{CName(baseDecl.Name)}(o);\n");
                sb.Append("}\n");
                finFn = $"digitoyengine_fin_{cn}";
            }
            else if (declarer != null)
                finFn = $"digitoyengine_fin_{CName(declarer.Name)}";
            var members = ReflectionMemberCount(model) > 0 ? cn + "_members" : "0";
            string genericArgs = "0", genericArgCount = "0";
            if (model.TypeArguments.Count > 0)
            {
                sb.Append($"static const Type* const {cn}_generic_args[] = {{ ");
                sb.Append(string.Join(", ", model.TypeArguments.Select(GenericArgSym)));
                sb.Append(" };\n");
                genericArgs = cn + "_generic_args";
                genericArgCount = model.TypeArguments.Count.ToString();
            }
            sb.Append($"Type {cn}_type = {{ 0, {finFn}, sizeof(struct {st}), {atomic}, {baseType}, {TypeNameRef(model)}, {vtable}, {nvtable}, {itables}, {nitables}, 0, 0, {members}, {ReflectionMemberCount(model)}, {genericArgs}, {genericArgCount}{TypeMetaTail(model)}{(refmap.IsEmpty ? "" : $", .refmap = {RefMapSym(model)}")} }};\n");
            return sb.ToString();
        }

        // Reflection uyeleri: tum bildirilen alanlar (backing field'lar dahil; .NET GetFields(NonPublic) gibi) + property'ler.
        // Struct'lar dahil (kutu uzerinden erisim); interface/delegate'in alani yok.
        static IEnumerable<PrimitiveField> ReflectionFields(Primitive model) =>
            TrimPlan.Has(model, MemberKinds.Fields) ? model.Fields.Concat(model.StaticFields) : Enumerable.Empty<PrimitiveField>();
        static IEnumerable<PrimitiveProperty> ReflectionProperties(Primitive model) =>
            TrimPlan.Has(model, MemberKinds.Props) ? model.Properties : Enumerable.Empty<PrimitiveProperty>();
        // Reflection adi: auto-property backing field'i IR'de "X__bk"; .NET adi "<X>k__BackingField" (GetField uyumu). Hash IR adindadir.
        static string ReflectionFieldName(PrimitiveField f) =>
            f.Name.EndsWith("__bk", StringComparison.Ordinal) ? "<" + f.Name.Substring(0, f.Name.Length - 4) + ">k__BackingField" : f.Name;

        static int ReflectionMemberCount(Primitive model) => ReflectionFields(model).Count() + ReflectionProperties(model).Count();
        static bool HasReflectionMembers(Primitive p) => IsEmittableModel(p) && !p.IsInterface && !p.IsDelegate && ReflectionMemberCount(p) > 0;

        static string ReflectionTypeSym(Primitive type) =>
            (type.Type == PrimitiveType.FixedArray || type.Type == PrimitiveType.Pointer) ? "0" :
            type.Type == PrimitiveType.Array ? (IsReflectedArray(type) ? "&" + TypeSym(type) : "0") :
            (TypeShape.IsStructModel(type)) ? (reflectedValueStructs.ContainsKey(CName(type.Name)) ? "&" + TypeSym(type) : "0") :
            "&" + TypeSym(type);

        static string EmitReflectionMembers(Primitive model)
        {
            var cn = CName(model.Name);
            var sb = new StringBuilder();
            var entries = new List<string>();
            // Alan: erisim runtime'da tag/offset/addr ile (digitoyengine_member_get/set); uretilmis get/set fonksiyonu YOK.
            foreach (var field in ReflectionFields(model))
            {
                sb.Append(EmitAttrTable($"{cn}_fattrs_{CName(field.Name)}", MemberAttrs(model, field.Attributes)));
                entries.Add($"{{ &{strPool[ReflectionFieldName(field)]}, &{cn}_type, {ReflectionTypeSym(field.Type)}, 0, 0, 0, DIGITOYENGINE_MEMBER_FIELD, {Convert.ToInt32(field.IsStatic)}, {Convert.ToInt32(field.IsReadonly)}{MemberMetaTail(model, field)} }}");
            }
            // Property: get_/set_ MethodInfo kaydi (tam kayit; Invoke sekil thunk'uyla). Kaydi olmayan erisimci = okunamaz/yazilamaz.
            foreach (var property in ReflectionProperties(model))
            {
                string get = AccessorRef(property.Getter), set = AccessorRef(property.Setter);
                var pattrs = $"{cn}_pattrs_{CName(property.Name)}";
                var propAttrs = MemberAttrs(model, property.Attributes);
                sb.Append(EmitAttrTable(pattrs, propAttrs));
                entries.Add($"{{ &{strPool[property.Name]}, &{cn}_type, {ReflectionTypeSym(property.Type)}, {get}, {set}, 0, DIGITOYENGINE_MEMBER_PROPERTY, {Convert.ToInt32(property.IsStatic)}, 0, .tag = '{ExportTag(property.Type)}'{AttrTail(pattrs, propAttrs)}, .cilattrs = {property.CilAttributes} }}");
            }
            sb.Append($"static DigitoyEngineMember {cn}_members[] = {{\n    {string.Join(",\n    ", entries)}\n}};\n\n");
            return sb.ToString();
        }

        static string AccessorRef(Code accessor) =>
            accessor != null && methodIndex.ContainsKey(accessor) && !traceOnly.Contains(accessor) ? MethodRef(accessor) : "0";

        // struct basina gc_alloc tabanli constructor: fixed-array alanlar struct icine gomulu oldugu icin
        // ayrica allocate edilmesine gerek yok (New op'unun karsiligi).
        static string EmitConstructor(Primitive model)
        {
            var cn = CName(model.Name);
            return $"static inline struct {StructTag(model)}* New_{cn}(void) {{ return (struct {StructTag(model)}*)gc_alloc(&{cn}_type); }}\n";
        }

        static string Prototype(Code code) => Prototype(code, CSym(code), code.NeedsDictParam);
        static string Prototype(Code code, string sym, bool dictParam)
        {
            var an = CArgNames(code, new HashSet<string>());
            var pars = string.Join(", ", code.Arguments.Select((a, i) => $"{ParamCType(a)} {an[i]}"));
            if (dictParam) pars = "const void *const *__dict" + (code.Arguments.Count > 0 ? ", " + pars : ""); // CANON K6: gizli sozluk ilk parametre
            if (pars.Length == 0) pars = "void";
            return $"{CType(code.ReturnType)} {sym}({pars})";
        }

        // ref/out parametre -> C pointer (CIL managed pointer karsiligi); GetArg pointer'i,
        // LoadInd/StoreInd deref eder (AddrLocal/AddrArg cagri noktasinda adres uretir).
        static string ParamCType(Argument a) => (a.IsRef || a.IsOut) ? CType(a.Type) + "*" : CType(a.Type);

        // debugger tip etiketi (RtLocal.tag): protokol degeri nasil okuyacagini bundan bilir
        static char DbgTag(Primitive t) =>
            t.Type == PrimitiveType.Int ? 'i' : t.Type == PrimitiveType.Float ? 'f' :
            t.Type == PrimitiveType.Double ? 'd' : t.Type == PrimitiveType.Long ? 'l' :
            t.Type == PrimitiveType.Char ? 'c' : t.Type == PrimitiveType.Short ? 'h' :
            t.Type == PrimitiveType.Byte ? 'b' : t.Type == PrimitiveType.Bool ? 'B' :
            t.Type == PrimitiveType.UInt ? 'u' : t.Type == PrimitiveType.ULong ? 'q' :
            t.Type == PrimitiveType.UShort ? 'H' : t.Type == PrimitiveType.SByte ? 'z' :
            TypeShape.IsStructModel(t) ? 'v' :
            t.Type == PrimitiveType.Model || t.Type == PrimitiveType.Array ? 'o' : '?';

        // debug local/arg tablosu: #ifdef DIGITOYENGINE_DEBUG blogu (release preprocessing'te tamamen yok olur
        // -> adres alinmaz, register allocation etkilenmez). Adsiz temp'ler tabloya girmez.
        static void EmitDbgLocals(StringBuilder sb, Code code, string[] an, string[] ln)
        {
            var entries = new List<string>();
            for (int i = 0; i < code.Arguments.Count; i++)
            {
                var a = code.Arguments[i];
                var tag = (a.IsRef || a.IsOut) ? 'r' : DbgTag(a.Type);
                entries.Add($"{{ \"{a.Name}\", '{tag}', (void*)&{an[i]} }}");
            }
            for (int i = 0; i < code.Locals.Count; i++)
            {
                var name = i < code.LocalNames.Count ? code.LocalNames[i] : null;
                if (string.IsNullOrEmpty(name)) continue; // derleyici temp'i
                entries.Add($"{{ \"{name}\", '{DbgTag(code.Locals[i])}', (void*)&{ln[i]} }}");
            }
            if (entries.Count == 0) return;
            sb.Append("#ifdef DIGITOYENGINE_DEBUG\n");
            sb.Append($"    RtLocal __dbg[{entries.Count}] = {{ {string.Join(", ", entries)} }};\n");
            sb.Append($"    DIGITOYENGINE_DBG_LOCALS(__dbg, {entries.Count});\n");
            sb.Append("#endif\n");
        }

        static Primitive Promote(Primitive a, Primitive b) => Primitive.PromoteNumeric(a, b);

        static bool IsNumericVal(Primitive t) => TypeShape.IsScalar(t); // skaler/enum (TypeShape tek kapi)

        // sayisal ikili islem: operandlar C# terfi tipine ACIKCA cast edilir (uint+int -> long gibi
        // C'nin kendi terfisiyle AYRISAN durumlar bit dogru kalir; gereksiz cast'i clang atar)
        static CVal NumBin(string op, CVal a, CVal b)
        {
            // unsafe pointer aritmetigi CIL semantigi: BYTE-tabanli (int operand zaten *sizeof carpilmis).
            // C eleman-olcekli aritmetigi devre disi: (T*)((char*)p + offset).
            if (a.Type?.Type == PrimitiveType.Pointer)
                return new CVal($"(({CType(a.Type)})((char*)({a.Expr}) {op} ({b.Expr})))", a.Type);
            if (b.Type?.Type == PrimitiveType.Pointer && op == "+")
                return new CVal($"(({CType(b.Type)})((char*)({b.Expr}) + ({a.Expr})))", b.Type);
            var p = Promote(a.Type ?? Primitive.Int, b.Type ?? Primitive.Int);
            var ct = ScalarCType(p);
            if (op == "%" && (p == Primitive.Float || p == Primitive.Double)) // C'de % tam sayi; C# float % = fmod (isaret: bolunenin)
                return new CVal($"{(p == Primitive.Float ? "fmodf" : "fmod")}((({ct})({a.Expr})), (({ct})({b.Expr})))", p);
            return new CVal($"((({ct})({a.Expr})) {op} (({ct})({b.Expr})))", p);
        }

        // karsilastirma: modeller/pointerlar ham, sayisallar terfi tipinde (isaret tuzaklari kapanir)
        static string Cmp(string op, CVal a, CVal b)
        {
            if (!IsNumericVal(a.Type) || !IsNumericVal(b.Type))
                return $"({a.Expr}) {op} ({b.Expr})";
            var ct = ScalarCType(Promote(a.Type, b.Type));
            return $"(({ct})({a.Expr})) {op} (({ct})({b.Expr}))";
        }

        // C float/double literali ondalik nokta ISTER (16f/16 gecersiz -> 16.0f/16.0 olmali)
        static string FloatLiteralText(string numText) =>
            numText.IndexOfAny(new[] { '.', 'e', 'E' }) >= 0 ? numText : numText + ".0";

        static CVal Literal(object value)
        {
            if (value == null) return new CVal("0", Primitive.Void); // null literal: typed conversion decides ref null versus default Nullable<T>
            if (value is string s) return new CVal($"(&{strPool[s]})", Primitive.String); // interned havuz kaydi
            if (value is int i) return new CVal(i.ToString(), Primitive.Int);
            if (value is uint ui) return new CVal(ui + "u", Primitive.UInt);
            if (value is ulong ul) return new CVal(ul + "ull", Primitive.ULong);
            if (value is float f)
            {
                if (float.IsPositiveInfinity(f)) return new CVal("(1.0f / 0.0f)", Primitive.Float);
                if (float.IsNegativeInfinity(f)) return new CVal("(-1.0f / 0.0f)", Primitive.Float);
                if (float.IsNaN(f)) return new CVal("DIGITOYENGINE_nan_f()", Primitive.Float); // .NET NaN bit deseni 0xFFC00000 (clang 0.0f/0.0f -> 0x7FC00000)
                return new CVal(FloatLiteralText(f.ToString("R")) + "f", Primitive.Float);
            }
            if (value is double d)
            {
                if (double.IsPositiveInfinity(d)) return new CVal("(1.0 / 0.0)", Primitive.Double);
                if (double.IsNegativeInfinity(d)) return new CVal("(-1.0 / 0.0)", Primitive.Double);
                if (double.IsNaN(d)) return new CVal("DIGITOYENGINE_nan_d()", Primitive.Double); // .NET double.NaN = 0xFFF8000000000000
                return new CVal(FloatLiteralText(d.ToString("R")), Primitive.Double);
            }
            if (value is bool b) return new CVal(b ? "1" : "0", Primitive.Bool);
            if (value is long l) return new CVal(l + "LL", Primitive.Long);
            if (value is short sh) return new CVal(sh.ToString(), Primitive.Short);
            if (value is ushort us) return new CVal(us.ToString(), Primitive.UShort);
            if (value is sbyte sb2) return new CVal(sb2.ToString(), Primitive.SByte);
            if (value is byte by) return new CVal(by.ToString(), Primitive.Byte);
            if (value is char c) return new CVal($"((cil_char){(int)c})", Primitive.Char); // sayisal: non-ASCII C literal'i bozulur
            throw new Exception($"transpiler icin desteklenmeyen literal tipi: {value.GetType().Name}");
        }

        // C string literal: yalniz ASCII guvenli (tani metni); kontrol/ASCII disi karakterler '?'
        static string CStringLiteral(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (var ch in s)
            {
                if (ch == '"' || ch == '\\') { sb.Append('\\').Append(ch); }
                else if (ch < 32 || ch > 126) sb.Append('?');
                else sb.Append(ch);
            }
            return sb.Append('"').ToString();
        }

        static string EmitFunction(Code code)
        {
            // [DevOnly] + lean (docs/engine-notes.md TRIM): govdesiz stub; cagirdiklari kirpildi.
            if (TrimPlan.IsStubbed(code))
            {
                string body = code.ReturnType == Primitive.Void ? ""
                    : TrimPlan.CompletedTaskGetter != null && code.ReturnType.Name == "System.Threading.Tasks.Task" ? $"    return {CName(TrimPlan.CompletedTaskGetter.EncodeName())}();\n"
                    : $"    return ({CType(code.ReturnType)}){{0}};\n";
                return $"{Prototype(code)} {{ /* DevOnly stub */\n{body}}}\n";
            }
            // Cevrilemeyen govde (CIL frontend stub'i): SESSIZ sifir donmek YASAK — ilk cagrida
            // NotImplementedException("AOT stub: Tip.Uye — sebep") firlatilir; hata uzakta, alakasiz
            // bir yerde (null deref) degil, tam kaynaginda ve izinde gorunur.
            if (code.UntranslatableReason != null)
            {
                var what = CStringLiteral($"AOT stub: {code.Owner?.Name}.{code.DisplayName ?? code.Name} -- {code.UntranslatableReason}");
                var body = $"    DIGITOYENGINE_throw_notimpl({what});";
                if (code.ReturnType != Primitive.Void)
                    body += $"\n    return ({CType(code.ReturnType)}){{0}}; /* erisilmez */";
                return $"{Prototype(code)} {{\n{body}\n}}\n";
            }

            // NativeBody: Op'lardan uretmek yerine el yazimi C govdesi oldugu gibi kullanilir.
            if (code.NativeBody != null)
                return $"{Prototype(code)} {{\n{code.NativeBody}\n}}\n";

            var fn = CName(code.EncodeName());

            // try on-taramasi: RtTry degiskenleri + handler giris noktalari (CIL gibi handler
            // girisinde exception stack'tedir -> DIGITOYENGINE_ex_obj push edilir)
            var tryIndexOf = new Dictionary<Op, int>(); // TryBegin op -> __tryN sayaci
            var handlerCatch = new Dictionary<int, Primitive>(); // handler ip -> catch tipi
            foreach (var op in code.Operations)
                if (op.Type == OpType.TryBegin)
                {
                    tryIndexOf[op] = tryIndexOf.Count;
                    handlerCatch[op.Slot] = op.PrimitiveRef;
                }
            bool hasTry = tryIndexOf.Count > 0;

            // gercek govde ayri static fonksiyonda ({fn}__impl): Return'ler pop derdi tasimaz.
            // Sarmalayici (asil ad) DIGITOYENGINE_PUSH + cagri + DIGITOYENGINE_POP yapar (old compiler deseni; vtable/itable
            // sarmalayiciya isaret eder -> her giris yolu izlenir).
            var usedNames = new HashSet<string>();
            var an = CArgNames(code, usedNames);
            var ln = CLocalNames(code, usedNames);
            var pars = string.Join(", ", code.Arguments.Select((a, i) => $"{ParamCType(a)} {an[i]}"));
            // CANON K6: gizli sozluk parametresi (static / struct uyesi / canon generic metot); class instance metodu this'ten alir (asagida)
            if (code.NeedsDictParam) pars = "const void *const *__dict" + (code.Arguments.Count > 0 ? ", " + pars : "");
            if (pars.Length == 0) pars = "void";
            curDictUsed = false;
            var sb = new StringBuilder();
            // inline ipucu: tek cagiran sarmalayici -> -O2'de cagri kaybolur. always_inline BILEREK yok
            // (setjmp'li govdeleri zorla inline etmek tanimsiz davranisa acilir; karar clang'in)
            sb.Append($"static inline {CType(code.ReturnType)} {fn}__impl({pars}) {{\n");
            for (int i = 0; i < code.Locals.Count; i++)
                sb.Append($"    {CType(code.Locals[i])}{(hasTry ? " volatile" : "")} {ln[i]};\n"); // setjmp kurali: longjmp sonrasi okunan locals volatile olmali
            const string DictPlaceholder = "    /*__DICT_DECL__*/\n";
            sb.Append(DictPlaceholder);
            EmitDbgLocals(sb, code, an, ln); // debug build: local/arg tablosu (release'te preprocessor yok eder)
            for (int i = 0; i < tryIndexOf.Count; i++)
                sb.Append($"    RtTry __try{i};\n");

            var jumpTargets = new HashSet<int>();
            foreach (var op in code.Operations)
                if (op.Type == OpType.Br || op.Type == OpType.Brtrue || op.Type == OpType.Brfalse || op.Type == OpType.TryBegin)
                    jumpTargets.Add(op.Slot);

            var stack = new List<CVal>();
            int tempCounter = 0;
            CVal Pop() { var v = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1); return v; }
            void Push(CVal v) => stack.Add(v);
            // CANON LOWERING: Rt op'u tip descriptor'unu yiginin tepesinden alir; statik op'ta null (sembol)
            string HiddenType(Op o2, OpType rt) => o2.Type == rt ? Pop().Expr : null;
            // dallanma oncesi bekleyen ifadeleri C temp'ine dok: jump hedefinde her iki yol AYNI
            // temp adlarini gorur (ternary/kisa-devre degerleri alici beklerken emit edilebilir).
            // Alt-stack once hesaplanir = C# soldan-saga degerlendirme sirasiyla da tutarli.
            void SpillStack()
            {
                for (int si = 0; si < stack.Count; si++)
                {
                    var ex = stack[si].Expr;
                    if (ex.StartsWith("__s") || stack[si].IsConst || LiteralExpr(ex)) continue; // spill'li, sabit operand ya da saf sabit: tasima gereksiz
                    var t = $"__s{tempCounter++}";
                    sb.Append($"    {CType(stack[si].Type)} {t} = {ex};\n");
                    stack[si] = new CVal(t, stack[si].Type);
                }
            }
            // yan etkisiz/degismez ifade: sayi, char, string havuzu adresi (IDENT DEGIL - mutasyon bayatligi riski); Lower'in gizli
            // operandlari CVal.IsConst ile isaretlidir (regex degil: kullanici alani `X_s_dict` gibi adlarla karisir)
            bool LiteralExpr(string e2) =>
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^-?[0-9][0-9uUlL.xa-fA-F]*f?$") ||
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^'.'$") ||
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^\(&_strpool_[0-9a-f_]+\)$");
            // trivial olmayan cagri argumanlarini SIRAYLA temp'e al (C arguman sirasi belirsiz, C# soldan-saga)
            bool TrivialArg(string e2) =>
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^\(?&?[A-Za-z_][A-Za-z0-9_]*\)?$") ||
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^-?[0-9][0-9uUlL.xa-fA-F]*f?$") ||
                System.Text.RegularExpressions.Regex.IsMatch(e2, @"^'.'$");
            string MaterializeArg(string e2, Argument decl)
            {
                if (TrivialArg(e2)) return e2;
                var t = $"__t{tempCounter++}";
                sb.Append($"    {ParamCType(decl)} {t} = {e2};\n");
                return t;
            }
            string MaterializeCallArg(CVal value, Argument decl)
            {
                if ((decl.IsRef || decl.IsOut) && value.Type?.Type == PrimitiveType.Pointer && value.Type.ElementType == decl.Type)
                    return MaterializeArg(value.Expr, decl);
                // deger-tip alici adresle geldi (CIL ldflda/ldloca + scalar/struct instance metot) ama parametre by-value: deref
                if (!(decl.IsRef || decl.IsOut) && value.Type?.Type == PrimitiveType.Pointer && value.Type.ElementType == decl.Type)
                    return MaterializeArg($"(*({value.Expr}))", decl);
                var coerced = Coerce(value, decl.Type);
                if (decl.IsRef && decl.Type.IsStruct && value.Type == decl.Type)
                {
                    var t = $"__t{tempCounter++}";
                    sb.Append($"    {CType(decl.Type)} {t} = {coerced};\n");
                    return $"(&{t})";
                }
                return MaterializeArg(coerced, decl);
            }
            // tam sayi bolme/mod: bolen sifir denetimi (C#: DivideByZeroException; float'ta yok); isaretli int/long'da
            // ayrica MIN/-1 -> OverflowException (C idiv trap'i sureci oldururdu)
            CVal DivCheck(CVal a, CVal b, Op op)
            {
                if (b.Type != null && (b.Type.Type == PrimitiveType.Float || b.Type.Type == PrimitiveType.Double)) return b;
                var t = $"__t{tempCounter++}";
                sb.Append($"    {CType(b.Type ?? Primitive.Int)} {t} = {b.Expr};\n");
                var bt = b.Type?.Type ?? PrimitiveType.Int;
                var at = a.Type?.Type ?? PrimitiveType.Int;
                if (bt == PrimitiveType.Long && at == PrimitiveType.Long)
                    sb.Append($"    DIGITOYENGINE_SDIVCHECK_AT({a.Expr}, {t}, (-9223372036854775807LL - 1), {op.Line});\n");
                else if (bt == PrimitiveType.Int && (at == PrimitiveType.Int || at == PrimitiveType.Short || at == PrimitiveType.SByte || at == PrimitiveType.Byte || at == PrimitiveType.UShort || at == PrimitiveType.Char))
                    sb.Append($"    DIGITOYENGINE_SDIVCHECK_AT({a.Expr}, {t}, (-2147483647 - 1), {op.Line});\n");
                else
                    sb.Append($"    DIGITOYENGINE_DIVCHECK_AT({t}, {op.Line});\n");
                return new CVal(t, b.Type);
            }
            CVal MaterializeVal(CVal v) // sira garantisi: sol operand denetim statement'indan once degerlensin
            {
                if (TrivialArg(v.Expr)) return v;
                var t = $"__t{tempCounter++}";
                sb.Append($"    {CType(v.Type ?? Primitive.Int)} {t} = {v.Expr};\n");
                return new CVal(t, v.Type);
            }
            // class pointer upcast'i (Dog* -> Animal* / object): layout duzlestirildigi icin guvenli, C acik cast ister.
            // Deger tipi -> object: ortuk boxing (C# donusumu; kucuk degerler cache'ten, alloc yok)
            string Coerce(CVal v, Primitive target)
            {
                if (NullableValueType(target, out var nullableValue))
                {
                    if (v.Type == target) return v.Expr;
                    if (v.Type?.Type == PrimitiveType.Pointer && v.Type.ElementType == target) return v.Expr;
                    if (v.Type == Primitive.Void) return $"(struct {StructTag(target)}){{0}}";
                    return $"(struct {StructTag(target)}){{ 1, ({CType(nullableValue)})({v.Expr}) }}";
                }
                if (target != null && TypeShape.IsClassModel(target) && v.Type != null && v.Type != target)
                {
                    if (TypeShape.IsValueLike(v.Type))
                    {
                        // Ortuk kutulama (generic T=struct: object parametresi / IList<object> gibi yollar); tek kapi BoxValue
                        // (enum/skaler/struct/Nullable). Descriptor pre-pass'te toplanmis olmali.
                        if (TypeShape.IsStructModel(v.Type) && !reflectedValueStructs.ContainsKey(CName(BoxedTypeOf(v.Type).Name)) && !TypeShape.IsScalar(BoxedTypeOf(v.Type)))
                            throw new Exception($"struct kutulama descriptor'suz: {v.Type.Name} (pre-pass kacirdi)");
                        return $"(({CType(target)}){BoxValue(v.Type, v.Expr)})";
                    }
                    return $"({CType(target)})({v.Expr})";
                }
                // CANON: GCHeader* (T) <-> VmArray*/VmDelegate* gibi referans tipleri arasi acik cast (clang: incompatible pointer = hata)
                if (target != null && v.Type != null && v.Type != target && TypeShape.IsGcRef(target) && TypeShape.IsGcRef(v.Type) && CType(target) != CType(v.Type))
                    return $"(({CType(target)})({v.Expr}))";
                // unsafe: pointer <-> native int (IntPtr=Long/ULong) serbest cast (ayni boyut; C -Wint-conversion susar).
                // Yalniz native-int genisligi; Int'e truncate ETME (pointer'i 32-bit'e kirpardi).
                if (target?.Type == PrimitiveType.Pointer && (v.Type?.Type == PrimitiveType.Long || v.Type?.Type == PrimitiveType.ULong))
                    return $"(({CType(target)})(size_t)({v.Expr}))";
                if ((target?.Type == PrimitiveType.Long || target?.Type == PrimitiveType.ULong) && v.Type?.Type == PrimitiveType.Pointer)
                    return $"(({CType(target)})(size_t)({v.Expr}))";
                return v.Expr;
            }

            // NullableValueType: static helper (BoxValue/UnboxValue da kullanir)

            var openTries = new List<int>(); // compile-time TryBegin/TryEnd eslesmesi (LIFO)
            int publishedLine = 0; // DIGITOYENGINE_SETLINE dedup (jump hedefinde sifirlanir: gelen yol bilinmez)
            int lastStep = 0; // DIGITOYENGINE_STEP dedup (paketli konum degisiminde bir kez)
            // Cagri/firlatma oncesi: cagiran frame'in satiri shadow stack'e. Sicak op'lar (alan/dizi/bolme/null) satir
            // STORE ETMEZ — check makrosunun firlatma dalinda sabit tasir (*_AT). Olcum: NOTRACE ile esit hiz.
            void EmitLine(Op o)
            {
                if (o.Line > 0 && o.Line != publishedLine)
                {
                    sb.Append($"    DIGITOYENGINE_SETLINE({o.Line});\n"); // Op.Line paketli: (satir<<10)|kolon
                    publishedLine = o.Line;
                }
            }

            for (int i = 0; i < code.Operations.Count; i++)
            {
                if (jumpTargets.Contains(i))
                {
                    foreach (var pv in stack) // bekleyenler yalniz spill temp'i ya da saf sabit olabilir (SpillStack sozlesmesi)
                        if (!pv.Expr.StartsWith("__s") && !pv.IsConst && !LiteralExpr(pv.Expr))
                            throw new Exception($"{code.EncodeName()}: jump hedefinde spill edilmemis ifade var (well-formed olmayan Op dizisi)");
                    sb.Append($"L{i}: ;\n"); // handler girisinde otomatik push YOK: ExIs/ExBind okur (DIGITOYENGINE_ex_* globalleri)
                    publishedLine = 0;
                }

                var op = code.Operations[i];
                if (op.Line > 0 && op.Line != lastStep) // debug adim noktasi (release: bos makro)
                {
                    sb.Append($"    DIGITOYENGINE_STEP({op.Line});\n");
                    lastStep = op.Line;
                }
                switch (op.Type)
                {
                    case OpType.GetArg:
                        {
                            var at = code.Arguments[op.Slot];
                            // ref/out (struct this dahil): C degiskeni POINTER'dir; tip etiketi de pointer olur
                            Push(new CVal(an[op.Slot], (at.IsRef || at.IsOut) ? Primitive.PointerOf(at.Type) : at.Type));
                            break;
                        }
                    case OpType.SetArg: { SpillStack(); var v = Pop(); sb.Append($"    {an[op.Slot]} = {Coerce(v, code.Arguments[op.Slot].Type)};\n"); break; }
                    case OpType.GetLocal: Push(new CVal(ln[op.Slot], code.Locals[op.Slot])); break;
                    case OpType.SetLocal: { SpillStack(); var v = Pop(); sb.Append($"    {ln[op.Slot]} = {Coerce(v, code.Locals[op.Slot])};\n"); break; }

                    case OpType.GetField:
                        {
                            var inst = CanonInst(Pop(), op.Field);
                            if (inst.Type.Type == PrimitiveType.Pointer) // struct lvalue zinciri: p->alan
                                Push(new CVal(FieldCast(op.Field, $"(({inst.Expr})->{CName(op.Field.Name)})"), op.Field.Type));
                            else if (inst.Type.IsStruct)
                                Push(new CVal(FieldCast(op.Field, $"{inst.Expr}.{CName(op.Field.Name)}"), op.Field.Type));
                            else
                            {
                                var t = $"__o{tempCounter++}";
                                Push(new CVal(FieldCast(op.Field, $"(({{ {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK_AT({t}, {op.Line}); {t}->{CName(op.Field.Name)}; }}))"), op.Field.Type));
                            }
                            break;
                        }
                    case OpType.SetField:
                        {
                            SpillStack(); // C# sira garantisi: bekleyen okumalar mutasyondan ONCE degerlenir
                            var v = Pop(); var inst = CanonInst(Pop(), op.Field);
                            if (inst.Type.Type == PrimitiveType.Pointer)
                            {
                                var pt = $"__p{tempCounter++}";
                                var barrier = Barrier($"{pt}->{CName(op.Field.Name)}", op.Field.Type);
                                sb.Append($"    {{ {CType(inst.Type)} {pt} = {inst.Expr}; {pt}->{CName(op.Field.Name)} = {FieldStoreCast(op.Field, Coerce(v, op.Field.Type))};{barrier} }}\n");
                            }
                            else if (inst.Type.IsStruct) // local/arg struct degeri (C stack): barrier yok (R4)
                                sb.Append($"    {inst.Expr}.{CName(op.Field.Name)} = {FieldStoreCast(op.Field, Coerce(v, op.Field.Type))};\n");
                            else
                            {
                                var t = $"__o{tempCounter++}";
                                var barrier = Barrier($"{t}->{CName(op.Field.Name)}", op.Field.Type);
                                sb.Append($"    {{ {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK_AT({t}, {op.Line}); {t}->{CName(op.Field.Name)} = {FieldStoreCast(op.Field, Coerce(v, op.Field.Type))};{barrier} }}\n");
                            }
                            break;
                        }
                    case OpType.AddrField:
                        {
                            var inst = CanonInst(Pop(), op.Field);
                            bool fixedBuf = op.Field.Type.Type == PrimitiveType.FixedArray; // fixed buffer: adres = dizi datasi (float*), decay
                            var resType = Primitive.PointerOf(fixedBuf ? op.Field.Type.ElementType : op.Field.Type);
                            if (inst.Type.Type == PrimitiveType.Pointer)
                                Push(new CVal(fixedBuf ? $"(({inst.Expr})->{CName(op.Field.Name)})" : FieldAddrCast(op.Field, $"(&(({inst.Expr})->{CName(op.Field.Name)}))"), resType));
                            else // class alici: null check + adres (alici temp'e: bir kez degerlenir)
                            {
                                SpillStack(); // alici simdi degerlenir: bekleyen ifadeler once (sira korunur)
                                var t = $"__o{tempCounter++}";
                                sb.Append($"    {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK_AT({t}, {op.Line});\n");
                                var access = fixedBuf ? $"{t}->{CName(op.Field.Name)}" : FieldAddrCast(op.Field, $"(&({t}->{CName(op.Field.Name)}))");
                                Push(new CVal(access, resType));
                            }
                            break;
                        }
                    case OpType.AddrElement:
                        {
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var indices = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) indices.Insert(0, Pop());
                            var arr = Pop();
                            var elem = arr.Type.ElementType;
                            if (arr.Type.Type == PrimitiveType.Array)
                            {
                                // dizi temp'e (bir kez degerlenir), indeksleme statement-expression'da
                                SpillStack();
                                var ao = $"__o{tempCounter++}";
                                sb.Append($"    {CType(arr.Type)} {ao} = {arr.Expr};\n");
                                var a = $"__a{tempCounter++}";
                                Push(new CVal($"(({{ {ArrayIndexSetup(new CVal(ao, arr.Type), indices, a, op.Line)}&((({CType(elem)}*){a}->data)[{a}off]); }}))", Primitive.PointerOf(elem)));
                            }
                            else // FixedArray
                            {
                                var i2 = $"__i{tempCounter++}";
                                Push(new CVal($"(({{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS_AT({i2}, {arr.Type.FixedSize}, {op.Line}); &({arr.Expr}[{i2}]); }}))", Primitive.PointerOf(elem)));
                            }
                            break;
                        }
                    case OpType.ArrayDataAddr:
                        {
                            var array = Pop();
                            if (array.Type == Primitive.String) // dizgi verisi (UTF-16): Span<char>/AsSpan
                            {
                                var ts = $"__s{tempCounter++}";
                                Push(new CVal($"(({{ VmString* {ts} = {array.Expr}; {ts} == NULL ? NULL : (unsigned short*){ts}->data; }}))", Primitive.PointerOf(Primitive.Char)));
                                break;
                            }
                            var element = array.Type.ElementType;
                            if (array.Type.Type == PrimitiveType.Array)
                            {
                                var temp = $"__a{tempCounter++}";
                                Push(new CVal($"(({{ {CType(array.Type)} {temp} = {array.Expr}; {temp} == NULL ? NULL : ({CType(element)}*){temp}->data; }}))", Primitive.PointerOf(element)));
                            }
                            else
                                Push(new CVal($"(&({array.Expr}[0]))", Primitive.PointerOf(element)));
                            break;
                        }
                    case OpType.AddrStatic: case OpType.AddrStaticRt:
                        { var T = HiddenType(op, OpType.AddrStaticRt); Push(new CVal($"(&{StaticRefT(T, op.Field)})", Primitive.PointerOf(op.Field.Type))); break; }

                    // ---- CANON LOWERING gizli operandlari (Lower.cs): sabit ifadeler, LiteralExpr spill etmez ----
                    case OpType.LdType: Push(CVal.Const($"&{TypeSym(op.PrimitiveRef)}", Primitive.TypeHandle)); break;
                    case OpType.LdDictSlot:
                        switch ((DictSlotKind)op.Value)
                        {
                            case DictSlotKind.TypeHandle: Push(CVal.Const(DictSlotType(Dict(), op.Slot), Primitive.TypeHandle)); break;
                            case DictSlotKind.FnPtr: Push(CVal.Const($"DIGITOYENGINE_DICT_FN({Dict()}, {op.Slot})", Primitive.FnPtrHandle)); break;
                            default: Push(CVal.Const($"DIGITOYENGINE_DICT_DICT({Dict()}, {op.Slot})", Primitive.DictHandle)); break;
                        }
                        break;
                    case OpType.LdDictSelf: Push(CVal.Const(Dict(), Primitive.DictHandle)); break;
                    case OpType.LdDictConst: Push(CVal.Const(DictSymOf(op.Code), Primitive.DictHandle)); break;
                    case OpType.LdFnPtr: RequestStub(op.Code); Push(CVal.Const($"(const void*)&{FnPtrSym(op.Code)}", Primitive.FnPtrHandle)); break;

                    case OpType.GetIndex:
                        {
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var indices = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) indices.Insert(0, Pop());
                            var arr = Pop();
                            var elem = arr.Type.ElementType;
                            if (arr.Type.Type == PrimitiveType.Array) // VmArray: null + bounds check'li
                            {
                                var a = $"__a{tempCounter++}";
                                Push(new CVal($"(({{ {ArrayIndexSetup(arr, indices, a, op.Line)}(({CType(elem)}*){a}->data)[{a}off]; }}))", elem));
                                break;
                            }
                            if (arr.Type.Type == PrimitiveType.FixedArray) // gomulu dizi: boyut sabit, null yok
                            {
                                var i2 = $"__i{tempCounter++}";
                                Push(new CVal($"(({{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS_AT({i2}, {arr.Type.FixedSize}, {op.Line}); {arr.Expr}[{i2}]; }}))", elem));
                                break;
                            }
                            Push(new CVal($"{arr.Expr}[{indices[0].Expr}]", elem));
                            break;
                        }
                    case OpType.SetIndex:
                        {
                            SpillStack();
                            var v = Pop();
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var indices = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) indices.Insert(0, Pop());
                            var arr = Pop();
                            if (arr.Type.Type == PrimitiveType.Array)
                            {
                                var a = $"__a{tempCounter++}";
                                var slot = $"(({CType(arr.Type.ElementType)}*){a}->data)[{a}off]";
                                var barrier = Barrier(slot, arr.Type.ElementType);
                                sb.Append($"    {{ {ArrayIndexSetup(arr, indices, a, op.Line)}{slot} = {Coerce(v, arr.Type.ElementType)};{barrier} }}\n");
                            }
                            else if (arr.Type.Type == PrimitiveType.FixedArray)
                            {
                                var i2 = $"__i{tempCounter++}";
                                sb.Append($"    {{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS_AT({i2}, {arr.Type.FixedSize}, {op.Line}); {arr.Expr}[{i2}] = {v.Expr}; }}\n");
                            }
                            else
                                sb.Append($"    {arr.Expr}[{indices[0].Expr}] = {v.Expr};\n");
                            break;
                        }

                    case OpType.NewArray: case OpType.NewArrayRt:
                        {
                            EmitLine(op); // negatif boyut: runtime firlatir
                            var T = HiddenType(op, OpType.NewArrayRt);
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var dims = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) dims.Insert(0, Pop());
                            var elem = op.PrimitiveRef;
                            int isref = TypeShape.IsGcRef(elem) ? 1 : 0;
                            var dn = $"__dims{tempCounter++}";
                            var arrType = Primitive.ArrayOf(elem, rank);
                            // Kanonik T[] descriptor'u = tahsis tipi (gc.type == typeof(T[])); descriptor'suz eleman (jagged/pointer,
                            // kirpilmis tip): runtime ref/atomic tahsis tipleri + elem 0 (bilinmiyor). T verilmisse exact T[] descriptor'u odur.
                            string allocType = T ?? (IsReflectedArray(arrType) ? $"&{TypeSym(arrType)}" : (isref == 1 ? "&vmarray_ref_type" : "&vmarray_val_type"));
                            string elemType = T != null ? $"{allocType}->elem_type" : ExportTypeSym(elem);
                            string alloc = $"vmarray_new_rank_te({rank}, {dn}, sizeof({CType(elem)}), {allocType}, {elemType})";
                            Push(new CVal($"(({{ int {dn}[{rank}] = {{ {string.Join(", ", dims.Select(d => d.Expr))} }}; {alloc}; }}))", arrType));
                            break;
                        }
                    case OpType.ArrayLength:
                        {
                            var arr = Pop();
                            var a = $"__a{tempCounter++}";
                            Push(new CVal($"(({{ VmArray* {a} = {arr.Expr}; DIGITOYENGINE_NULLCHECK_AT({a}, {op.Line}); {a}->len; }}))", Primitive.Int));
                            break;
                        }

                    case OpType.StackAlloc:
                        {
                            var n = Pop();
                            var tn = $"__sa{tempCounter++}";
                            Push(new CVal($"(({{ size_t {tn} = (size_t)({n.Expr}); void* {tn}p = __builtin_alloca({tn}); memset({tn}p, 0, {tn}); {tn}p; }}))", Primitive.PointerOf(Primitive.Void))); // void*: hedef T* local'a C ortuk donusumu
                            break;
                        }
                    case OpType.SizeOf:
                        Push(new CVal($"((int)sizeof({CType(op.PrimitiveRef)}))", Primitive.Int)); // derleme zamani sabiti (C layout = gercek)
                        break;

                    case OpType.Conv:
                        {
                            var v = Pop();
                            var tt = op.PrimitiveRef;
                            var vt = v.Type?.Type ?? PrimitiveType.Int;
                            // kayan nokta -> tam sayi: .NET doyurma semantigi (NaN->0, +inf->Max, -inf->Min, aralik disi->sinir);
                            // C cast'i UB (x64'te 0x80000000 "integer indefinite"). Kucuk tipler int32 uzerinden kesilir (.NET gibi).
                            if (vt is PrimitiveType.Float or PrimitiveType.Double && tt.Type is PrimitiveType.Int or PrimitiveType.UInt or PrimitiveType.Long or PrimitiveType.ULong
                                or PrimitiveType.Byte or PrimitiveType.SByte or PrimitiveType.Short or PrimitiveType.UShort or PrimitiveType.Char)
                            {
                                string satFn = tt.Type switch { PrimitiveType.Long => "DIGITOYENGINE_f2l", PrimitiveType.ULong => "DIGITOYENGINE_f2ul", PrimitiveType.UInt => "DIGITOYENGINE_f2u", _ => "DIGITOYENGINE_f2i" };
                                Push(new CVal($"(({CType(tt)}){satFn}((double)({v.Expr})))", tt));
                                break;
                            }
                            Push(new CVal($"(({CType(tt)})({v.Expr}))", tt));
                            break;
                        }
                    case OpType.IsType: case OpType.IsTypeRt:
                        {
                            var T = HiddenType(op, OpType.IsTypeRt);
                            var o = Pop(); // alici iki kez gecer (null test + tip zinciri) -> temp
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            Push(new CVal($"(({t} && {TypeTestT(T, op.PrimitiveRef, $"{t}->gc.type", t)}) ? 1 : 0)", Primitive.Int));
                            break;
                        }
                    case OpType.IsValueType:
                        {
                            var value = Pop();
                            var sourceType = op.PrimitiveRef;
                            if (sourceType.IsGenericParameter)
                                throw new Exception($"IsValueType somutlastirilmamis tip parametresi: {sourceType.Name}");
                            bool isValueType = TypeShape.IsValueLike(sourceType);
                            Push(new CVal($"((void)({value.Expr}), {(isValueType ? 1 : 0)})", Primitive.Bool));
                            break;
                        }
                    case OpType.AsType: case OpType.AsTypeRt:
                        {
                            var T = HiddenType(op, OpType.AsTypeRt);
                            var o = Pop();
                            if (o.Type.IsGenericParameter)
                                throw new Exception($"AsType somutlastirilmamis tip parametresi: {o.Type.Name}");
                            bool sourceIsValueType = TypeShape.IsValueLike(o.Type);
                            if (sourceIsValueType)
                            {
                                if (op.PrimitiveRef == Primitive.Object || op.PrimitiveRef == Primitive.ValueType)
                                    Push(new CVal($"(({CType(op.PrimitiveRef)}){BoxValue(o.Type, o.Expr)})", op.PrimitiveRef));
                                else
                                    Push(new CVal($"((void)({o.Expr}), ({CType(op.PrimitiveRef)})0)", op.PrimitiveRef));
                                break;
                            }
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            bool targetIsValueType = TypeShape.IsValueLike(op.PrimitiveRef);
                            if (targetIsValueType)
                            {
                                // CIL isinst deger-tip: boxed referans tip tutarsa aynen (null test/unbox.any devaminda), yoksa null
                                Push(new CVal($"(({t} && {TypeTestT(T, op.PrimitiveRef, $"{t}->gc.type", t)}) ? (VmObject*){t} : (VmObject*)0)", Primitive.Object));
                                break;
                            }
                            Push(new CVal($"(({t} && {TypeTestT(T, op.PrimitiveRef, $"{t}->gc.type", t)}) ? ({CType(op.PrimitiveRef)})(void*){t} : 0)", op.PrimitiveRef));
                            break;
                        }
                    case OpType.CastClass: case OpType.CastRt:
                        {
                            EmitLine(op); // unbox / cast_fail firlatabilir
                            var T = HiddenType(op, OpType.CastRt);
                            // Generic sablonda `(T)obj` = unbox.any !T -> frontend T'yi referans sayip CastClass uretir; T deger
                            // tipine somutlasinca (int/enum/struct) bu bir UNBOX'tir (pointer->int cast degil).
                            if (TypeShape.IsValueLike(op.PrimitiveRef))
                            {
                                var ov = Pop();
                                Push(new CVal(UnboxValueT(T, op.PrimitiveRef, ov.Expr), op.PrimitiveRef));
                                break;
                            }
                            var o = Pop();
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            if (T != null) // runtime hedef: iface/dizi/class ayrimini runtime yapar
                            {
                                Push(new CVal($"(({CType(op.PrimitiveRef)})((!{t} || DIGITOYENGINE_is_any((GCHeader*){t}, {T})) ? (void*){t} : (DIGITOYENGINE_cast_fail({t}->gc.type, {T}), (void*)0)))", op.PrimitiveRef));
                                break;
                            }
                            if (op.PrimitiveRef.Type == PrimitiveType.Array)
                            {
                                // Dizi cast'i: eleman tipi biliniyorsa (VmArray.elem) denetlenir; bilinmiyorsa (modul interp dizisi,
                                // jagged/pointer eleman) hosgorulu gecer — eski davranis (denetimsiz) korunur.
                                var tgt = op.PrimitiveRef;
                                Push(new CVal($"(({CType(tgt)})((!{t} || !((VmArray*)(void*){t})->elem || {ExportTypeSym(tgt.ElementType)} == 0 || {TypeTest(tgt, $"{t}->gc.type", t)}) ? (void*){t} : (DIGITOYENGINE_cast_fail({t}->gc.type, &vmarray_ref_type), (void*)0)))", tgt));
                                break;
                            }
                            // null gecer; tip tutmazsa DIGITOYENGINE_cast_fail (noreturn) - virgul operatoru ile ifade icinde
                            Push(new CVal($"(({CType(op.PrimitiveRef)})((!{t} || {TypeTest(op.PrimitiveRef, $"{t}->gc.type")}) ? (void*){t} : (DIGITOYENGINE_cast_fail({t}->gc.type, &{TypeSym(op.PrimitiveRef)}), (void*)0)))", op.PrimitiveRef));
                            break;
                        }

                    case OpType.Default:
                        {
                            var zt = op.PrimitiveRef;
                            var zx = TypeShape.IsStructModel(zt) ? $"(struct {StructTag(zt)}){{0}}" : $"(({CType(zt)})0)";
                            Push(new CVal(zx, zt));
                            break;
                        }
                    case OpType.TypeOf: case OpType.TypeOfRt:
                        {
                            var T = HiddenType(op, OpType.TypeOfRt) ?? $"&{TypeSym(op.PrimitiveRef)}";
                            EmitCtx.TryGetPrimitive("System.Type", out var st);
                            Push(new CVal($"(({CType(st)})digitoyengine_type_wrapper({T}))", st));
                            break;
                        }
                    case OpType.Box: case OpType.BoxRt:
                        {
                            var T = HiddenType(op, OpType.BoxRt);
                            var v = Pop();
                            var sourceType = op.PrimitiveRef;
                            string bx;
                            if (!TypeShape.IsGcRef(sourceType))
                            {
                                if (T != null) // runtime descriptor: canon struct kutusu exact descriptor'la (K4)
                                {
                                    var tag = StructTag(sourceType);
                                    Push(new CVal($"({{ struct {tag} __bx = {v.Expr}; (VmObject*)digitoyengine_box_struct({T}, &__bx, (int)sizeof(struct {tag})); }})", Primitive.Object));
                                    break;
                                }
                                Push(new CVal(BoxValue(sourceType, v.Expr), Primitive.Object)); // tek kapi: enum/skaler/struct/Nullable
                                break;
                            }
                            else if (TypeShape.IsGcRef(sourceType))
                            {
                                // referans tipte box = kimlik (C# ox !!T, T sinif kisitli: alan erisimi oncesi).
                                // Statik tipi KORU: ardindan gelen ldfld/call dogru struct'a gitsin.
                                Push(new CVal(v.Expr, sourceType));
                                break;
                            }
                            else if (TypeShape.IsStructModel(sourceType))
                                bx = BoxStruct(sourceType, v.Expr);
                            else
                                throw new Exception($"boxing desteklenmeyen tip: {sourceType.Name}");
                            Push(new CVal(bx, Primitive.Object));
                            break;
                        }
                    case OpType.Unbox: case OpType.UnboxRt:
                        {
                            EmitLine(op);
                            var T = HiddenType(op, OpType.UnboxRt);
                            var o = Pop();
                            var ut = op.PrimitiveRef;
                            Push(new CVal(UnboxValueT(T, ut, o.Expr), ut));
                            break;
                        }
                    case OpType.UnboxOrDefault: case OpType.UnboxOrDefaultRt:
                        {
                            var T = HiddenType(op, OpType.UnboxOrDefaultRt);
                            var o = Pop(); // alici iki kez gecer -> temp (IsType deseni)
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            var ut = op.PrimitiveRef;
                            var zero = TypeShape.IsStructModel(ut) ? $"(struct {StructTag(ut)}){{0}}" : $"({ScalarCType(ut)})0";
                            Push(new CVal($"(({t} && {t}->gc.type == {(T ?? "&" + TypeSym(BoxedTypeOf(ut)))}) ? {UnboxValueT(T, ut, t)} : {zero})", ut));
                            break;
                        }
                    case OpType.NullableWrap:
                        {
                            var value = Pop();
                            Push(new CVal(Coerce(value, op.PrimitiveRef), op.PrimitiveRef));
                            break;
                        }
                    case OpType.NullableHasValue:
                        {
                            var value = Pop();
                            if (!NullableValueType(value.Type, out _)) throw new Exception($"NullableHasValue Nullable<T> ister: {value.Type?.Name}");
                            Push(new CVal($"(({value.Expr}).hasValue ? 1 : 0)", Primitive.Bool));
                            break;
                        }
                    case OpType.NullableValue:
                        {
                            var value = Pop();
                            if (!NullableValueType(value.Type, out var valueType)) throw new Exception($"NullableValue Nullable<T> ister: {value.Type?.Name}");
                            Push(new CVal($"({value.Expr}).value", valueType));
                            break;
                        }
                    case OpType.NullableBinary:
                        {
                            var right = Pop();
                            var left = Pop();
                            bool leftNullable = NullableValueType(left.Type, out var leftValue);
                            bool rightNullable = NullableValueType(right.Type, out var rightValue);
                            var lv = leftValue ?? left.Type;
                            var rv = rightValue ?? right.Type;
                            var valueType = lv == Primitive.Void ? rv : rv == Primitive.Void ? lv : Primitive.PromoteNumeric(lv, rv);
                            var opText = (string)op.Value;
                            bool comparison = opText == "==" || opText == "!=" || opText == "<" || opText == ">" || opText == "<=" || opText == ">=";
                            var leftExpr = leftNullable ? $"__n{tempCounter++}" : null;
                            var rightExpr = rightNullable ? $"__n{tempCounter++}" : null;
                            if (leftNullable) sb.Append($"    {CType(left.Type)} {leftExpr} = {left.Expr};\n");
                            if (rightNullable) sb.Append($"    {CType(right.Type)} {rightExpr} = {right.Expr};\n");
                            var lPresent = leftNullable ? $"{leftExpr}.hasValue" : left.Type == Primitive.Void ? "0" : "1";
                            var rPresent = rightNullable ? $"{rightExpr}.hasValue" : right.Type == Primitive.Void ? "0" : "1";
                            var lValue = leftNullable ? $"{leftExpr}.value" : left.Expr;
                            var rValue = rightNullable ? $"{rightExpr}.value" : right.Expr;
                            if (comparison)
                            {
                                string expression = opText switch
                                {
                                    "==" => $"(!{lPresent} && !{rPresent}) || ({lPresent} && {rPresent} && (({CType(valueType)})({lValue}) == ({CType(valueType)})({rValue})))",
                                    "!=" => $"({lPresent} != {rPresent}) || ({lPresent} && {rPresent} && (({CType(valueType)})({lValue}) != ({CType(valueType)})({rValue})))",
                                    _ => $"{lPresent} && {rPresent} && (({CType(valueType)})({lValue}) {opText} ({CType(valueType)})({rValue}))"
                                };
                                Push(new CVal($"(({expression}) ? 1 : 0)", Primitive.Bool));
                            }
                            else
                            {
                                var result = op.PrimitiveRef;
                                Push(new CVal($"(struct {StructTag(result)}){{ ({lPresent} && {rPresent}), (({CType(valueType)})({lValue}) {opText} ({CType(valueType)})({rValue})) }}", result));
                            }
                            break;
                        }
                    case OpType.DelegateNew: case OpType.DelegateNewRt:
                        {
                            EmitLine(op);
                            var dt = op.PrimitiveRef;
                            var m = op.Code;
                            // Lower: delegate tipi + fn isaretcisi yigindan ([dt][fn]); statik op: sembol + (stub) fn
                            string dta, sfn;
                            if (op.Type == OpType.DelegateNewRt) { sfn = Pop().Expr; dta = Pop().Expr; }
                            else { dta = $"&{TypeSym(dt)}"; RequestStub(m); sfn = $"(const void*)&{FnPtrSym(m)}"; }
                            if (m.IsStatic)
                            {
                                Push(new CVal($"digitoyengine_delegate_new({dta}, {sfn}, 0)", dt));
                                break;
                            }
                            var recv = Pop();
                            var rt2 = $"__t{tempCounter++}";
                            sb.Append($"    {CType(recv.Type)} {rt2} = {recv.Expr};\n");
                            sb.Append($"    DIGITOYENGINE_NULLCHECK_AT({rt2}, {op.Line});\n"); // C#: null alicidan delegate yaratmak NRE
                            var dfn = op.Slot >= 0 ? $"{rt2}->gc.type->vtable[{op.Slot}]" : sfn;
                            Push(new CVal($"digitoyengine_delegate_new({dta}, {dfn}, (GCHeader*){rt2})", dt));
                            break;
                        }
                    case OpType.DelegateCombine:
                    case OpType.DelegateRemove:
                        {
                            var right = Pop();
                            var left = Pop();
                            var helper = op.Type == OpType.DelegateCombine ? "digitoyengine_delegate_combine" : "digitoyengine_delegate_remove";
                            Push(new CVal($"{helper}({left.Expr}, {right.Expr})", op.PrimitiveRef));
                            break;
                        }
                    case OpType.CallIndirect:
                        {
                            EmitLine(op);
                            var dt = op.PrimitiveRef;
                            var dargs = new string[dt.DelegateParams.Count];
                            for (int a = dargs.Length - 1; a >= 0; a--) dargs[a] = Coerce(Pop(), dt.DelegateParams[a]);
                            var dexpr = Pop();
                            SpillStack();
                            var d = $"__t{tempCounter++}";
                            sb.Append($"    VmDelegate* {d} = {dexpr.Expr};\n");
                            sb.Append($"    DIGITOYENGINE_NULLCHECK_AT({d}, {op.Line});\n");
                            for (int a = 0; a < dargs.Length; a++) // C# soldan-saga arguman sirasi
                                if (!TrivialArg(dargs[a]))
                                {
                                    var ta = $"__t{tempCounter++}";
                                    sb.Append($"    {CType(dt.DelegateParams[a])} {ta} = {dargs[a]};\n");
                                    dargs[a] = ta;
                                }
                            var ret = CType(dt.DelegateReturn);
                            var ptypes = string.Join(", ", dt.DelegateParams.Select(CType));
                            var argList = string.Join(", ", dargs);
                            var sigInst = $"{ret}(*)(void*{(dargs.Length > 0 ? ", " + ptypes : "")})";
                            var sigStat = $"{ret}(*)({(dargs.Length > 0 ? ptypes : "void")})";
                            var count = $"__n{tempCounter++}";
                            var index = $"__i{tempCounter++}";
                            var item = $"__d{tempCounter++}";
                            sb.Append($"    int {count} = digitoyengine_delegate_count({d});\n");
                            string result = null;
                            if (dt.DelegateReturn != Primitive.Void)
                            {
                                result = $"__r{tempCounter++}";
                                sb.Append($"    {ret} {result};\n");
                            }
                            sb.Append($"    for (int {index} = 0; {index} < {count}; {index}++) {{\n");
                            sb.Append($"        VmDelegate* {item} = digitoyengine_delegate_at({d}, {index});\n");
                            var callInst = $"(({sigInst}){item}->fn)((void*){item}->target{(dargs.Length > 0 ? ", " + argList : "")})";
                            var callStat = $"(({sigStat}){item}->fn)({argList})";
                            var call = $"({item}->target ? {callInst} : {callStat})";
                            sb.Append(dt.DelegateReturn != Primitive.Void ? $"        {result} = {call};\n" : $"        {call};\n");
                            sb.Append("    }\n");
                            if (dt.DelegateReturn != Primitive.Void) Push(new CVal(result, dt.DelegateReturn));
                            break;
                        }
                    case OpType.New: case OpType.NewRt:
                        {
                            var T = HiddenType(op, OpType.NewRt);
                            if (op.PrimitiveRef == Primitive.Object) // runtime koku: New_ fonksiyonu uretilmez
                            {
                                Push(new CVal("((VmObject*)gc_alloc(&vmobject_type))", Primitive.Object));
                                break;
                            }
                            var cn = CName(op.PrimitiveRef.Name);
                            // IsStruct: heap yok, sifirlanmis C99 compound literal (deger tipi, kopyalanarak akar)
                            // T verilmisse exact tahsis tipi odur (K4); ctor canon fn (class instance -> sozluk this'ten)
                            var expr = op.PrimitiveRef.IsStruct ? $"(struct {StructTag(op.PrimitiveRef)}){{0}}"
                                : T != null ? $"((struct {StructTag(op.PrimitiveRef)}*)gc_alloc({T}))" : $"New_{cn}()";
                            Push(new CVal(expr, op.PrimitiveRef));
                            break;
                        }
                    case OpType.Dup:
                        {
                            var top = Pop();
                            var t = $"__t{tempCounter++}";
                            // null literal (Void tipli "0"): C'de void degisken olmaz; referans olarak tasi
                            var dupType = top.Type == Primitive.Void ? Primitive.Object : top.Type;
                            sb.Append($"    {CType(dupType)} {t} = {top.Expr};\n");
                            Push(new CVal(t, dupType));
                            Push(new CVal(t, dupType));
                            break;
                        }
                    case OpType.Push: Push(Literal(op.Value)); break;
                    case OpType.Pop: { var v = Pop(); sb.Append($"    (void)({v.Expr});\n"); break; }
                    case OpType.Return:
                        if (openTries.Count > 0) // try icinden cikis: kayitlari coz (C# leave semantigi)
                            sb.Append($"    DIGITOYENGINE_try_top = __try{openTries[0]}.prev;\n");
                        if (code.ReturnType != Primitive.Void)
                        {
                            if (stack.Count == 0) // olu yol (tum dallar try icinden dondu): C imzasi icin default
                                sb.Append($"    return {(code.ReturnType.IsStruct ? $"(struct {StructTag(code.ReturnType)}){{0}}" : $"({CType(code.ReturnType)})0")};\n");
                            else
                                sb.Append($"    return {Coerce(Pop(), code.ReturnType)};\n");
                        }
                        else sb.Append("    return;\n");
                        break;

                    case OpType.Call:
                        {
                            EmitLine(op);
                            var callee = op.Code.Shared;
                            // Lower: gizli sozluk argumani yiginin tepesinde (callee ABI: NeedsDictParam)
                            string dictArg = callee.NeedsDictParam ? Pop().Expr : null;
                            var values = new CVal[op.Code.Arguments.Count];
                            for (int a = values.Length - 1; a >= 0; a--) values[a] = Pop();
                            SpillStack(); // cagri yan etkileri: alt-stack okumalari once degerlensin
                            var args = new string[values.Length];
                            for (int a = 0; a < args.Length; a++) // C# soldan-saga arguman sirasi (C'de belirsiz!)
                                args[a] = MaterializeCallArg(values[a], op.Code.Arguments[a]);
                            // CANON K6: hedef canon fn; ctx isterse sozluk ilk arguman. Exact->canon imza farki (VmString* vs GCHeader*): acik cast
                            if (callee != op.Code)
                                for (int a = 0; a < args.Length; a++)
                                    if (CType(callee.Arguments[a].Type) != CType(op.Code.Arguments[a].Type)) args[a] = $"(({ParamCType(callee.Arguments[a])})({args[a]}))";
                            var argList = dictArg != null ? new[] { dictArg }.Concat(args) : args;
                            var callText = $"{CSym(callee)}({string.Join(", ", argList)})";
                            if (callee != op.Code && op.Code.ReturnType != Primitive.Void && CType(callee.ReturnType) != CType(op.Code.ReturnType))
                                callText = $"(({CType(op.Code.ReturnType)}){callText})";
                            var call = WithPostCallRemember(callText, op.Code.ReturnType, values);
                            NoteValueUse(op.Code.ReturnType); // struct donus: sonucu atilsa bile C tam tip ister
                            if (op.Code.ReturnType != Primitive.Void) Push(new CVal(call, op.Code.ReturnType));
                            else sb.Append($"    {call};\n");
                            break;
                        }

                    case OpType.CallVirtual:
                        {
                            EmitLine(op);
                            // Lower: gizli operandlar once pop edilir ([..., args, ifaceT?, dict?]); callee ABI: NeedsDictParam
                            var vcallee = op.Code.Shared;
                            bool vdict = VirtualCallTakesDict(op.Code);
                            string vDictArg = vdict ? Pop().Expr : null;
                            string ifaceAddr = op.Code.Owner.IsInterface ? Pop().Expr : null;
                            // constrained esdegeri: klonlanmis generic'te receiver DEGER tipi olabilir.
                            // GetHashCode/Equals boxing'siz inline; digerleri (ToString) box'lanip normal dispatch.
                            var vArgc = op.Code.Arguments.Count;
                            var vRecv = stack[stack.Count - vArgc];
                            // constrained. callvirt: alici yonetilen pointer (ldloca/ldelema/ldarga) gelir -> deref (deger ya da referans).
                            if (vRecv.Type?.Type == PrimitiveType.Pointer && vRecv.Type.ElementType != null)
                            {
                                vRecv = new CVal($"(*({vRecv.Expr}))", vRecv.Type.ElementType);
                                stack[stack.Count - vArgc] = vRecv;
                            }
                            if (vRecv.Type != null && (IsNumericVal(vRecv.Type) || vRecv.Type.Type == PrimitiveType.Bool) && op.Code.Owner == Primitive.Object)
                            {
                                if (op.Code.SimpleName == "GetHashCode" && vArgc == 1)
                                {
                                    var hv = MaterializeVal(Pop());
                                    string hx = vRecv.Type.Type switch
                                    {
                                        PrimitiveType.Long or PrimitiveType.ULong => $"digitoyengine_valhash_i64((long long)({hv.Expr}))",
                                        PrimitiveType.Float => $"digitoyengine_valhash_f32({hv.Expr})",
                                        PrimitiveType.Double => $"digitoyengine_valhash_f64({hv.Expr})",
                                        PrimitiveType.Char => $"(((int)({hv.Expr})) | (((int)({hv.Expr})) << 16))",
                                        _ => $"((int)({hv.Expr}))"
                                    };
                                    Push(new CVal(hx, Primitive.Int));
                                    break;
                                }
                                if (op.Code.SimpleName == "Equals" && vArgc == 2 && stack[stack.Count - 1].Type == vRecv.Type)
                                {
                                    var eb = MaterializeVal(Pop());
                                    var ea = MaterializeVal(Pop());
                                    string ex = vRecv.Type.Type == PrimitiveType.Float ? $"digitoyengine_valeq_f32({ea.Expr}, {eb.Expr})"
                                        : vRecv.Type.Type == PrimitiveType.Double ? $"digitoyengine_valeq_f64({ea.Expr}, {eb.Expr})"
                                        : $"(({ea.Expr}) == ({eb.Expr}) ? 1 : 0)";
                                    Push(new CVal(ex, Primitive.Bool));
                                    break;
                                }
                                stack[stack.Count - vArgc] = new CVal(Coerce(vRecv, Primitive.Object), Primitive.Object); // fallback: box'la, normal dispatch
                            }
                            // vtable/itable dispatch: alici temp'e alinir (hem slot lookup hem arg0 olarak iki kez gecer)
                            var args = new string[op.Code.Arguments.Count];
                            var vvals = new CVal[args.Length];
                            for (int a = args.Length - 1; a >= 0; a--)
                            {
                                vvals[a] = Pop();
                                // ref/out parametre: deger zaten pointer -> aynen gecer (Coerce pointee tipine cast ederdi)
                                var decl = op.Code.Arguments[a];
                                args[a] = (decl.IsRef || decl.IsOut) && vvals[a].Type?.Type == PrimitiveType.Pointer
                                    ? vvals[a].Expr : Coerce(vvals[a], decl.Type);
                            }
                            SpillStack();
                            var recvType = op.Code.Arguments[0].Type;
                            var recv = $"__t{tempCounter++}";
                            sb.Append($"    {CType(recvType)} {recv} = {args[0]};\n");
                            sb.Append($"    DIGITOYENGINE_NULLCHECK_AT({recv}, {op.Line});\n"); // wasm: explicit; native: MMU+VEH yakalar (makro bos)
                            args[0] = recv;
                            for (int a = 1; a < args.Length; a++)
                                args[a] = MaterializeArg(args[a], op.Code.Arguments[a]);
                            // CANON: sanal hedef canon fn (vtable girisi); generic sanal metot canon ornegi ctx ister -> sozluk ilk arguman;
                            // iface itable anahtari (ifaceAddr) ve sozluk yukarida cozuldu (Lower: yigindan).
                            // Exact GVM slotunun (Pair<string>) girisleri STUB'dir (ctx'siz); yalniz canon slot (Pair<__Canon>) girisleri sozluk alir.
                            var sigArgs = op.Code.Arguments.Select(a => (a.IsRef || a.IsOut) ? CType(a.Type) + "*" : CType(a.Type));
                            if (vdict) sigArgs = new[] { "const void *const *" }.Concat(sigArgs);
                            var fnSig = $"{CType(op.Code.ReturnType)}(*)({string.Join(", ", sigArgs)})";
                            var table = op.Code.Owner.IsInterface
                                ? $"DIGITOYENGINE_itable({recv}->gc.type, {ifaceAddr})"
                                : $"{recv}->gc.type->vtable";
                            var vargs = vdict ? new[] { vDictArg }.Concat(args) : args;
                            var call = WithPostCallRemember($"(({fnSig}){table}[{op.Slot}])({string.Join(", ", vargs)})", op.Code.ReturnType, vvals);
                            NoteValueUse(op.Code.ReturnType);
                            if (op.Code.ReturnType != Primitive.Void) Push(new CVal(call, op.Code.ReturnType));
                            else sb.Append($"    {call};\n");
                            break;
                        }

                    case OpType.GetStatic: case OpType.GetStaticRt: { var T = HiddenType(op, OpType.GetStaticRt); Push(new CVal(StaticRefT(T, op.Field), op.Field.Type)); break; }
                    case OpType.SetStatic: case OpType.SetStaticRt: { var T = HiddenType(op, OpType.SetStaticRt); SpillStack(); var v = Pop(); var sref = StaticRefT(T, op.Field); sb.Append($"    {sref} = {Coerce(v, op.Field.Type)};{Barrier(sref, op.Field.Type)}\n"); break; }

                    case OpType.Label: break;
                    case OpType.Br: SpillStack(); sb.Append($"    goto L{op.Slot};\n"); break;
                    case OpType.Brtrue: { var c = Pop(); SpillStack(); sb.Append($"    if ({c.Expr}) goto L{op.Slot};\n"); break; }
                    case OpType.Brfalse: { var c = Pop(); SpillStack(); sb.Append($"    if (!({c.Expr})) goto L{op.Slot};\n"); break; }
                    case OpType.TryBegin:
                        {
                            int k = tryIndexOf[op];
                            openTries.Add(k);
                            // DIGITOYENGINE_dispatch longjmp'tan ONCE DIGITOYENGINE_try_top/DIGITOYENGINE_sp'yi restore eder -> handler'da ekstra is yok
                            sb.Append($"    __try{k}.sp = DIGITOYENGINE_sp; __try{k}.boundary = 0; __try{k}.prev = DIGITOYENGINE_try_top; DIGITOYENGINE_try_top = &__try{k};\n");
                            sb.Append($"    if (setjmp(__try{k}.buf)) goto L{op.Slot};\n");
                            break;
                        }
                    case OpType.TryEnd:
                        {
                            int k = openTries[openTries.Count - 1];
                            openTries.RemoveAt(openTries.Count - 1);
                            sb.Append($"    DIGITOYENGINE_try_top = __try{k}.prev;\n");
                            break;
                        }
                    case OpType.Throw:
                        {
                            SpillStack();
                            var v = Pop();
                            EmitLine(op);
                            sb.Append($"    DIGITOYENGINE_throw(DIGITOYENGINE_EX_USER, (GCHeader*)({v.Expr}));\n");
                            break;
                        }
                    case OpType.ExIs: case OpType.ExIsRt:
                        {
                            var T = HiddenType(op, OpType.ExIsRt);
                            var e = $"__e{tempCounter++}";
                            Push(new CVal($"(({{ GCHeader* {e} = DIGITOYENGINE_ex_current(); ({e} && {TypeTestT(T, op.PrimitiveRef, $"{e}->type", e)}) ? 1 : 0; }}))", Primitive.Int));
                            break;
                        }
                    case OpType.ExBind: case OpType.ExBindRt:
                        {
                            var T = HiddenType(op, OpType.ExBindRt);
                            var e = $"__ex{tempCounter++}";
                            sb.Append($"    GCHeader* {e}h = DIGITOYENGINE_ex_current();\n");
                            sb.Append($"    if (!{e}h || !{TypeTestT(T, op.PrimitiveRef, $"{e}h->type", e + "h")}) DIGITOYENGINE_rethrow();\n"); // uymayan tip: dis try'a devret
                            sb.Append($"    {CType(op.PrimitiveRef)} {e} = ({CType(op.PrimitiveRef)})(void*){e}h;\n");
                            // Exception turevi: ILK firlatma trace'i gomulu tampona (ExceptionTrace struct: mi[24]/line[24]);
                            // yeniden firlatma (throw ex / Task fault) orijini SILMEZ. Roslyn fixed buffer = tek alanli
                            // `<mi>e__FixedBuffer` struct'i -> dizi alanina (FixedElementField) inilir.
                            var traceField = Hierarchy.AllFields(op.PrimitiveRef).FirstOrDefault(f => f.Name == "trace");
                            var miField = traceField?.Type.Type == PrimitiveType.Model ? Hierarchy.AllFields(traceField.Type).FirstOrDefault(f => f.Name == "mi") : null;
                            var lineField = traceField?.Type.Type == PrimitiveType.Model ? Hierarchy.AllFields(traceField.Type).FirstOrDefault(f => f.Name == "line") : null;
                            if (miField != null && lineField != null)
                            {
                                string FixedPath(PrimitiveField f, out int cap)
                                {
                                    if (f.Type.Type == PrimitiveType.FixedArray) { cap = f.Type.FixedSize; return $"{e}->trace.{CName(f.Name)}"; }
                                    var inner = Hierarchy.AllFields(f.Type).FirstOrDefault(x => x.Type.Type == PrimitiveType.FixedArray);
                                    cap = inner?.Type.FixedSize ?? 0;
                                    return inner != null ? $"{e}->trace.{CName(f.Name)}.{CName(inner.Name)}" : null;
                                }
                                var miPath = FixedPath(miField, out int cap);
                                var linePath = FixedPath(lineField, out _);
                                if (miPath != null && linePath != null)
                                    sb.Append($"    if ({e}->traceCount == 0) DIGITOYENGINE_bind_trace({miPath}, {linePath}, &{e}->traceCount, {cap});\n");
                            }
                            Push(new CVal(e, op.PrimitiveRef));
                            break;
                        }
                    case OpType.Rethrow: sb.Append("    DIGITOYENGINE_rethrow();\n"); break;
                    case OpType.TryUnwind: // dallanma yolunda runtime zinciri geri sar; textual openTries DEGISMEZ
                        {
                            int uk = openTries[openTries.Count - op.Slot];
                            sb.Append($"    DIGITOYENGINE_try_top = __try{uk}.prev;\n");
                            break;
                        }
                    case OpType.Ceq: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp("==", a, b)} ? 1 : 0)", Primitive.Int)); break; }
                    case OpType.Cne: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp("!=", a, b)} ? 1 : 0)", Primitive.Int)); break; }
                    case OpType.Cgt: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp(">", a, b)} ? 1 : 0)", Primitive.Int)); break; }
                    case OpType.Cge: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp(">=", a, b)} ? 1 : 0)", Primitive.Int)); break; }
                    case OpType.Clt: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp("<", a, b)} ? 1 : 0)", Primitive.Int)); break; }
                    case OpType.Cle: { var b = Pop(); var a = Pop(); Push(new CVal($"({Cmp("<=", a, b)} ? 1 : 0)", Primitive.Int)); break; }

                    // ref/out: AddrLocal/AddrArg adres uretir, LoadInd/StoreInd o adres uzerinden okur/yazar
                    case OpType.AddrLocal: Push(new CVal($"(&{ln[op.Slot]})", Primitive.PointerOf(code.Locals[op.Slot]))); break;
                    case OpType.AddrArg: Push(new CVal($"(&{an[op.Slot]})", Primitive.PointerOf(code.Arguments[op.Slot].Type))); break;
                    case OpType.LoadInd:
                        {
                            var p = Pop();
                            var pe = p.Type.Type == PrimitiveType.Pointer ? p.Type.ElementType : p.Type;
                            if (op.PrimitiveRef != null && pe != op.PrimitiveRef) // IL tipi isaretcinin statik tipinden farkli/tipsiz: IL tipiyle oku
                                Push(new CVal($"(*(({CType(op.PrimitiveRef)}*)({p.Expr})))", op.PrimitiveRef));
                            else
                                Push(new CVal($"(*({p.Expr}))", pe));
                            break;
                        }
                    case OpType.StoreInd:
                        {
                            SpillStack(); var v = Pop(); var p = Pop();
                            var pt = $"__p{tempCounter++}";
                            var et = p.Type?.Type == PrimitiveType.Pointer ? p.Type.ElementType : v.Type;
                            if (op.PrimitiveRef != null && et != op.PrimitiveRef) // IL tipi isaretcinin statik tipinden farkli/tipsiz: IL tipiyle yaz
                            {
                                et = op.PrimitiveRef;
                                sb.Append($"    {{ {CType(et)}* {pt} = ({CType(et)}*)({p.Expr}); *({pt}) = {Coerce(v, et)};{Barrier($"*{pt}", et)} }}\n");
                                break;
                            }
                            // isaretci uzerinden yazim (alan/eleman adresi, ref parametre, struct this): hedef heap olabilir -> barrier (R4)
                            var barrier = et != null ? Barrier($"*{pt}", et) : "";
                            sb.Append($"    {{ {CType(p.Type)} {pt} = {p.Expr}; *({pt}) = {(p.Type?.Type == PrimitiveType.Pointer ? Coerce(v, p.Type.ElementType) : v.Expr)};{barrier} }}\n");
                            break;
                        }

                    case OpType.Add: { var b = Pop(); var a = Pop(); Push(NumBin("+", a, b)); break; }
                    case OpType.Sub: { var b = Pop(); var a = Pop(); Push(NumBin("-", a, b)); break; }
                    case OpType.Mul: { var b = Pop(); var a = Pop(); Push(NumBin("*", a, b)); break; }
                    case OpType.Div: { var b = Pop(); var a = Pop(); a = MaterializeVal(a); Push(NumBin("/", a, DivCheck(a, b, op))); break; }
                    case OpType.Mod: { var b = Pop(); var a = Pop(); a = MaterializeVal(a); Push(NumBin("%", a, DivCheck(a, b, op))); break; }
                    case OpType.Neg:
                        {
                            var a = Pop();
                            var rt = a.Type != null && a.Type.Type == PrimitiveType.UInt ? Primitive.Long : a.Type; // C#: -uint = long
                            Push(new CVal($"(-(({ScalarCType(rt ?? Primitive.Int)})({a.Expr})))", rt));
                            break;
                        }
                    case OpType.And: { var b = Pop(); var a = Pop(); Push(NumBin("&", a, b)); break; }
                    case OpType.Or: { var b = Pop(); var a = Pop(); Push(NumBin("|", a, b)); break; }
                    case OpType.Xor: { var b = Pop(); var a = Pop(); Push(NumBin("^", a, b)); break; }
                    case OpType.Shl: { var b = Pop(); var a = Pop(); var st = a.Type ?? Primitive.Int; bool w = st.Type is PrimitiveType.Long or PrimitiveType.ULong; Push(new CVal($"((({ScalarCType(st)})({a.Expr})) << (({b.Expr}) & {(w ? 63 : 31)}))", st)); break; }
                    case OpType.Shr: { var b = Pop(); var a = Pop(); var st = a.Type ?? Primitive.Int; bool w = st.Type is PrimitiveType.Long or PrimitiveType.ULong; Push(new CVal($"((({ScalarCType(st)})({a.Expr})) >> (({b.Expr}) & {(w ? 63 : 31)}))", st)); break; }
                    // IL `not` = BITWISE tersleme (C# ~x); eski emisyon mantiksal `!` idi (~5 -> 0). Kucuk tipler int'e terfi eder (IL yigin).
                    case OpType.Not: { var a = Pop(); var nt = a.Type ?? Primitive.Int; if (nt.Type is PrimitiveType.Byte or PrimitiveType.SByte or PrimitiveType.Short or PrimitiveType.UShort or PrimitiveType.Char or PrimitiveType.Bool) nt = Primitive.Int; Push(new CVal($"(({ScalarCType(nt)})(~(({ScalarCType(nt)})({a.Expr}))))", nt)); break; }

                    default: throw new NotImplementedException($"CTranspiler icin henuz desteklenmeyen opcode: {op.Type}");
                }
            }
            sb.Append("}\n");
            // CANON K5: sozluk kaynagi (gizli param ya da this'in exact tipi) — CTranspiler.Canon.cs
            sb.Replace(DictPlaceholder, curDictUsed ? DictPrologue(code, an) : "");

            // sarmalayici: asil adla, push + impl cagrisi + pop (impl'deki throw'lar RtTry.sp
            // restore'uyla zaten dogru unwind eder; deger cikisi tek noktadan). Trace kaydi = tek meta tablosundaki MethodInfo.
            var callArgs = string.Join(", ", code.NeedsDictParam ? new[] { "__dict" }.Concat(an) : an);
            sb.Append(Prototype(code)).Append(" {\n");
            sb.Append($"    DIGITOYENGINE_PUSH({MethodRef(code)}, 0);\n");
            if (code.ReturnType != Primitive.Void)
            {
                sb.Append($"    {CType(code.ReturnType)} __r = {fn}__impl({callArgs});\n");
                sb.Append("    DIGITOYENGINE_POP();\n");
                sb.Append("    return __r;\n");
            }
            else
            {
                sb.Append($"    {fn}__impl({callArgs});\n");
                sb.Append("    DIGITOYENGINE_POP();\n");
            }
            sb.Append("}\n");
            return sb.ToString();
        }
    }
}
