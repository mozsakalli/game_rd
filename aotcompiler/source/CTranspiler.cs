using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Language
{
    // Op akisini "derleme-zamani C ifade metni" stack'i uzerinden C koduna katlar (old_dotnet_compiler'daki
    // PlainEmitter/CVal fikrinden ilham). VM.Run ile AYNI switch iskeleti; fark: deger yerine C metni tasinir.
    // Kapsam: Model (struct), FixedArray alan, GetIndex/SetIndex, New, Call, Label/Br/Brtrue/Brfalse, Ceq/Cgt/Clt,
    // aritmetik. Generic template'ler ve Unresolved kayitlar atlanir (yalniz somut/concrete emit edilir).
    public static partial class CTranspiler
    {
        class CVal
        {
            public string Expr;
            public Primitive Type;
            // Pointer degerleri icin: isaret edilen bellegin sahibi heap nesnesi (C ifadesi; GCHeader*'a
            // cast edilebilir). AddrField(class)/AddrIndex kurar, pointer uzerinden AddrField tasir.
            // null = stack/static/bilinmeyen (ref parametre) -> iç yazimda barrier sahibi yok.
            public string Owner;
            public CVal(string expr, Primitive type) { Expr = expr; Type = type; }
            public CVal(string expr, Primitive type, string owner) { Expr = expr; Type = type; Owner = owner; }
        }

        // string literal havuzu (interning): ayni metin = ayni _strpool_<hash> (GC_IMMORTAL, GC hic dokunmaz).
        // TranspileProgram basinda on-taramayla toplanir, Literal() referans verir.
        static readonly Dictionary<string, string> strPool = new Dictionary<string, string>();
        static Context EmitCtx; // finalize zinciri gibi ctx sorgulari icin (TranspileProgram atar)
        static readonly Dictionary<Primitive, int> typeIndex = new Dictionary<Primitive, int>(); // descriptor'u olan tipler (deger: digitoyengine_types[] sirasi; descriptor'a YAZILMAZ - Type.rootid uretilen tiplerde 0)
        static readonly Dictionary<string, Primitive> reflectedArrays = new Dictionary<string, Primitive>();
        static readonly Dictionary<string, Primitive> reflectedValueStructs = new Dictionary<string, Primitive>();
        static readonly Dictionary<string, Primitive> reflectedGenericDefs = new Dictionary<string, Primitive>(); // acik generic sablonlar (typeof(List<>), GetGenericTypeDefinition)
        static int nextTypeIndex;

        // MethodInfo gosterim adi: C# sozdizimi ("Demo12.App12.Deep(int[], int)"); IR kodlari fallback
        public static string CodeDisplay(Code c) =>
            (c.Owner != null ? c.Owner.Display + "." : "") + (c.DisplayName ?? c.Name);

        static void CollectStrings(Context ctx)
        {
            strPool.Clear();
            strSymText.Clear();
            typeIndex.Clear();
            reflectedArrays.Clear();
            reflectedValueStructs.Clear();
            reflectedGenericDefs.Clear();
            structArrayTypes.Clear(); // ayni surecte ikinci TranspileProgram (interp host) onceki programin dizi tiplerini gormesin
            nextTypeIndex = 0;
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c) && c.NativeBody == null)
                {
                    if (!c.IsExternal)
                    {
                        PoolAdd(CodeDisplay(c)); // MethodInfo.name
                        if (!string.IsNullOrEmpty(c.SourceFile)) PoolAdd(c.SourceFile); // MethodInfo.file
                    }
                    foreach (var op in c.Operations)
                    {
                        if (op.Type == OpType.Push && op.Value is string s)
                            PoolAdd(s);
                        if (op.Type == OpType.NewArray)
                            StructArrayTypeSym(op.PrimitiveRef); // ref tasiyan struct dizisi -> uretilecek dizi Type'i kaydi
                        if (op.Type == OpType.TypeOf && op.PrimitiveRef.Type == PrimitiveType.Array)
                        {
                            var key = CName(op.PrimitiveRef.Name);
                            if (!reflectedArrays.ContainsKey(key))
                            {
                                reflectedArrays[key] = op.PrimitiveRef;
                                PoolAdd(op.PrimitiveRef.Display);
                                typeIndex[op.PrimitiveRef] = nextTypeIndex++;
                            }
                        }
                        // Kullanici struct descriptor'i: typeof + kutulama/kutudan cikarma hedefleri
                        // (Box/Unbox/UnboxOrDefault/AsType/CastClass/IsType) — box helper'i &X_type ister.
                        bool structRef = op.PrimitiveRef != null && op.PrimitiveRef.Type == PrimitiveType.Model && op.PrimitiveRef.IsStruct && !op.PrimitiveRef.IsGenericParameter;
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
                if (p.Type == PrimitiveType.Model && p.IsStruct && p.GenericTemplate == null && p.GenericParameters.Count == 0
                    && !p.IsGenericParameter && !HasOpenTypeArgs(p) && p != Primitive.ValueType)
                {
                    var key = CName(p.Name);
                    if (!reflectedValueStructs.ContainsKey(key))
                    {
                        reflectedValueStructs[key] = p;
                        PoolAdd(p.Display);
                        typeIndex[p] = nextTypeIndex++;
                    }
                    StructArrayTypeSym(p); // ref tasiyan struct: T[] tahsis tipi her zaman var (Array.CreateInstance -> Type.array_of)
                }
            var seen = new HashSet<Primitive>(); // Type.name'ler: EmitTraceAndTypeOrdered gezisiyle birebir
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p) && !p.IsStruct)
                    CollectTypeNames(p, seen);
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                {
                    foreach (var f in ReflectionFields(p)) { PoolAdd(ReflectionFieldName(f)); PoolAttrStrings(f.Attributes); }
                    foreach (var prop in p.Properties) { PoolAdd(prop.Name); PoolAttrStrings(prop.Attributes); }
                    PoolAttrStrings(p.Attributes);
                }
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c)) PoolAttrStrings(c.Attributes);
            foreach (var p in ctx.AllPrimitives) // enum'lar: descriptor + ToString uye adlari
                if (p.IsEnum && !typeIndex.ContainsKey(p))
                {
                    PoolAdd(p.Display);
                    foreach (var m in p.EnumMembers.Keys) PoolAdd(m);
                    typeIndex[p] = nextTypeIndex++;
                }
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
            CollectMethods(ctx); // metot kayitlari (ad/dosya havuzu dahil) — typeIndex hazir olduktan sonra
        }

        static void CollectTypeNames(Primitive p, HashSet<Primitive> done)
        {
            if (p == Primitive.Object || p == Primitive.String || !done.Add(p)) return;
            if (p.Parent != null && !p.Parent.IsStruct) CollectTypeNames(p.Parent, done);
            foreach (var iface in p.Interfaces) CollectTypeNames(iface, done);
            PoolAdd(p.Display); // Type.name: C# sozdizimli (dotted; generic: Box<int>)
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
            return sb.ToString();
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
        static readonly HashSet<string> CReservedWords = new HashSet<string>
        {
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else",
            "enum", "extern", "float", "for", "goto", "if", "inline", "int", "long", "register",
            "restrict", "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef",
            "union", "unsigned", "void", "volatile", "while", "main"
        };

        // kaynak adini gecerli+benzersiz C tanimlayicisina indirger; adsiz/uygunsuz -> fallback (a{i}/l{i})
        static string CIdent(string name, string fallback, HashSet<string> used)
        {
            if (string.IsNullOrEmpty(name) || name.StartsWith("__")) name = fallback; // __: codegen rezervi (__try/__r)
            else if (name == "this") name = "self"; // C'de gecerli ama C++ derleyici olasiligi icin
            else
            {
                name = CName(name);
                if (char.IsDigit(name[0])) name = "_" + name;
                if (CReservedWords.Contains(name)) name += "_";
            }
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

        // yerel/parametre/donus tipi olarak kullanilan C tip adi. IsStruct=true -> gercek deger tipi
        // (GCHeader yok, pointer yok, kopyalanarak gecer); IsStruct=false -> GC heap'te referans (pointer).
        internal static string CType(Primitive t)
        {
            if (t == Primitive.Object) return "VmObject*"; // runtime koku (struct uretilmez)
            if (t == Primitive.String) return "VmString*"; // runtime string (struct uretilmez)
            if (t == Primitive.ValueType) return "VmObject*"; // boxed value type runtime koku
            if (t.IsDelegate) return "VmDelegate*";        // runtime delegate (struct uretilmez)
            if (t.Type == PrimitiveType.Model) return t.IsStruct ? $"struct {CName(t.Name)}" : $"struct {CName(t.Name)}*";
            if (t.Type == PrimitiveType.Array) return "VmArray*"; // GC'li dinamik dizi (len tasir, data ayri tampon)
            if (t.Type == PrimitiveType.FixedArray) return ScalarCType(t.ElementType) + "*";
            if (t.Type == PrimitiveType.Pointer) return CType(t.ElementType) + "*"; // ham pointer, GC disi
            return ScalarCType(t);
        }

        static string ArrayIndexSetup(CVal array, List<CVal> indices, string arrayName)
        {
            var sb = new StringBuilder();
            sb.Append($"VmArray* {arrayName} = {array.Expr}; ");
            for (int i = 0; i < indices.Count; i++) sb.Append($"int {arrayName}i{i} = {indices[i].Expr}; ");
            sb.Append($"DIGITOYENGINE_NULLCHECK({arrayName}); if ({arrayName}->rank != {indices.Count}) DIGITOYENGINE_throw_bounds({indices.Count}, {arrayName}->rank); int {arrayName}off = 0; ");
            for (int i = 0; i < indices.Count; i++)
                sb.Append($"DIGITOYENGINE_BOUNDS({arrayName}i{i}, {arrayName}->dims[{i}]); {arrayName}off = {arrayName}off * {arrayName}->dims[{i}] + {arrayName}i{i}; ");
            return sb.ToString();
        }

        static string FieldDecl(PrimitiveField f)
        {
            if (f.Type.Type == PrimitiveType.FixedArray)
                return $"{ScalarCType(f.Type.ElementType)} {CName(f.Name)}[{f.Type.FixedSize}];";
            return $"{CType(f.Type)} {CName(f.Name)};";
        }

        static bool IsEmittableModel(Primitive p) => p.Type == PrimitiveType.Model && p.GenericTemplate == null && p.GenericParameters.Count == 0 && !HasOpenTypeArgs(p) && p != Primitive.Object && p != Primitive.String && p != Primitive.ValueType && p.Name != WellKnown.ValueType; // corelib ValueType.cs = runtime vmvaluetype_type
        // Acik tip argumanli ornek (Task<T>, T = generic METHOD parametresi): generic method sablonunun
        // govdesinden sizar; somut cagri ayrica monomorfize edilir. Emit edilmez.
        static bool HasOpenTypeArgs(Primitive p)
        {
            foreach (var a in p.TypeArguments)
                if (a.IsGenericParameter || HasOpenTypeArgs(a) || (a.ElementType != null && (a.ElementType.IsGenericParameter || HasOpenTypeArgs(a.ElementType))))
                    return true;
            return false;
        }
        // iface methodlari govdesizdir; generic template'ler ve uyeleri yalniz klon kaynagi - ikisi de emit edilmez
        static bool IsEmittableCode(Code c) => !c.Unresolved && c.GenericParameters.Count == 0 && !(c.Owner != null && (c.Owner.IsInterface || c.Owner.IsGeneric || HasOpenTypeArgs(c.Owner)));

        // TranspileProgram / TranspileFiles: CTranspiler.Units.cs (birim tabanli emisyon)

        static string StaticSym(PrimitiveField f) => $"{CName(f.Owner.Name)}_s_{CName(f.Name)}";

        static void EmitStructOrdered(StringBuilder sb, Primitive p, HashSet<Primitive> emitted)
        {
            if (!IsEmittableModel(p) || p.IsDelegate || !emitted.Add(p)) return;
            foreach (var field in p.Fields)
                if (field.Type.Type == PrimitiveType.Model && field.Type.IsStruct)
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
            // Kutu yerlesimi: [GCHeader][struct]. Struct icinde referans varsa trace fn (GC kutunun
            // icindeki nesneleri gormeli), atomic=0; yoksa atomic=1.
            var paths = new List<string>();
            foreach (var field in Hierarchy.AllFields(type))
                CollectGcRefPaths(field.Type, "p->" + CName(field.Name), paths);
            string traceFn = "0";
            if (paths.Count > 0)
            {
                traceFn = $"trace_box_{cn}";
                sb.Append($"static void {traceFn}(GCHeader* o) {{\n");
                sb.Append($"    struct {cn}* p = (struct {cn}*)((char*)o + sizeof(GCHeader));\n");
                foreach (var path in paths)
                    sb.Append($"    gc_shade((GCHeader*){path});\n");
                sb.Append("}\n");
            }
            sb.Append($"Type {cn}_type = {{ {traceFn}, 0, sizeof(GCHeader) + sizeof(struct {cn}), {(paths.Count > 0 ? 0 : 1)}, &vmvaluetype_type, &{strPool[type.Display]}, 0, 0, 0, 0, 0, 0, {(HasReflectionMembers(type) ? cn + "_members" : "0")}, {ReflectionMemberCount(type)}, {genericArgs}, {genericArgCount}{TypeMetaTail(type)}{(StructArrayTypeSym(type) is string ats ? $", .array_of = &{ats}" : "")} }};\n");
            return sb.ToString();
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

        // delegate descriptor'i: paylasimli VmDelegate yerlesimi, trace runtime'da; C# zinciri MulticastDelegate->Delegate->Object
        static string EmitDelegateType(Primitive p) =>
            $"Type {CName(p.Name)}_type = {{ digitoyengine_delegate_trace, 0, sizeof(VmDelegate), 0, &vmmulticastdelegate_type, &{strPool[p.Display]}, 0, 0, 0, 0, 0{TypeMetaTail(p)} }};\n";

        // enum descriptor'i: ToString uye adi dondurur (dotnet Enum.ToString; tanimsiz deger -> sayi),
        // hash/eq ortak govdeler (corelib.c), alias = underlying int (unbox denkligi).
        // Tanimli uyelerin box'lari ONURETILIR (immortal, sifir alloc); tanimsiz deger alloc fallback.
        static string EmitEnumReflection(Primitive p)
        {
            var cn = CName(p.Name);
            var sb = new StringBuilder();
            sb.Append($"static int digitoyengine_enumparse_{cn}(VmString *value, int *result) {{\n");
            foreach (var m in p.EnumMembers)
                sb.Append($"    if (System_String_op_eq_System_String_System_String(value, &{strPool[m.Key]})) {{ *result = {m.Value}; return 1; }}\n");
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

        // Ref tasiyan struct eleman dizileri: eleman tipi -> uretilen dizi Type sembolu (yoksa null).
        // Pre-pass (TranspileProgram) NewArray op''larini tarayip structArrayTypes''i doldurur.
        static readonly Dictionary<string, Primitive> structArrayTypes = new Dictionary<string, Primitive>();
        static string StructArrayTypeSym(Primitive elem)
        {
            if (elem == null || elem.Type != PrimitiveType.Model || !elem.IsStruct) return null;
            var paths = new List<string>();
            CollectGcRefPaths(elem, "e", paths);
            if (paths.Count == 0) return null;
            var key = CName(elem.Name);
            structArrayTypes[key] = elem;
            return "arr_" + key + "_type";
        }
        static string EmitStructArrayType(Primitive elem)
        {
            var cn = CName(elem.Name);
            var paths = new List<string>();
            CollectGcRefPaths(elem, "e[i]", paths);
            var sb = new StringBuilder();
            sb.Append($"static void trace_arr_{cn}(GCHeader* o) {{\n    VmArray* a = (VmArray*)o; struct {cn}* e = (struct {cn}*)a->data;\n");
            sb.Append($"    for (int i = 0; i < a->len; i++) {{ ");
            foreach (var pth in paths) sb.Append($"gc_shade((GCHeader*){pth}); ");
            sb.Append("}\n}\n");
            sb.Append($"Type arr_{cn}_type = {{ trace_arr_{cn}, finalize_vmarray, sizeof(VmArray), 0, {ArrayBaseSym()} }};\n");
            return sb.ToString();
        }

        static string WithPostCallRemember(string call, Primitive ret, CVal[] values) => call; // tek artimli GC: remembered set yok, cagri sonrasi is yok

        // Write barrier ifadeleri: owner (GCHeader*) ve yazilan slot'un icindeki TUM referans yollari
        // (duz ref/dizi alani: tek yol; struct alani: icindeki ref alanlar). Ref icermeyen tip: bos.
        // AOT_NOBARRIER=1: bariyer emisyonu kapali (yalniz Demo51'in hatayi yakaladigini kanitlamak icin).
        static readonly bool NoBarrier = Environment.GetEnvironmentVariable("AOT_NOBARRIER") == "1";

        static string WriteBarrier(string ownerExpr, string slotExpr, Primitive slotType)
        {
            if (NoBarrier) return "";
            var paths = new List<string>();
            CollectGcRefPaths(slotType, slotExpr, paths);
            if (paths.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var p in paths)
                sb.Append($" gc_write_barrier((GCHeader*){ownerExpr}, (GCHeader*){p});");
            return sb.ToString();
        }

        // Kullanici struct kutulama: deger gecici'ye alinir, payload kopyalanir (statement-expression).
        static string BoxStruct(Primitive st, string expr)
        {
            var cn = CName(st.Name);
            return $"({{ struct {cn} __bx = {expr}; (VmObject*)digitoyengine_box_struct(&{cn}_type, &__bx, (int)sizeof(struct {cn})); }})";
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
                                 : $"DIGITOYENGINE_is({typeExpr}, &{TypeSym(target)})";

        // digitoyengine_init: (1) class-ref static'leri GC koku olarak kaydet, (2) cctor'lari kayit sirasiyla kos.
        // Host main'i baska bir sey yapmadan ONCE cagirmali.
        static string EmitInit(Context ctx)
        {
            var sb = new StringBuilder();
            var refStatics = new List<string>();
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                    foreach (var f in p.StaticFields)
                        CollectGcRefPaths(f.Type, StaticSym(f), refStatics);
            if (refStatics.Count > 0)
            {
                sb.Append("static void digitoyengine_statics_trace(void* _) {\n");
                foreach (var path in refStatics)
                    sb.Append($"    gc_shade((GCHeader*){path});\n");
                sb.Append("}\n");
            }
            sb.Append("void digitoyengine_init(void) {\n");
            if (refStatics.Count > 0)
                sb.Append("    gc_add_frame_root((void*)1, digitoyengine_statics_trace);\n");
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
            var exMsgCtor = ctx.AllCodes.FirstOrDefault(c => c.EncodeName() == "System.Exception$ctor_System_String");
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
                    if (ctx.TryGetCode(kt.Name + "$ctor", out var kctor)) // kind ctor'u C# default mesajini atar
                        sb.Append($"    DIGITOYENGINE_ex_kind_ctor[{k}] = (void (*)(GCHeader*))&{CName(kctor.EncodeName())};\n");
                }
            // cctor sirasi: .NET'te beforefieldinit statik alan erisiminde tetiklenir; burada hepsi eager
            // kosar. Bir cctor baska tipin statik alanini okuyorsa (dogrudan ya da cagirdigi statik
            // metodlar uzerinden) o tipin cctor'u ONCE kosmali — ornegin AnimRegistry..cctor lambdalari
            // <>c.<>9 singleton'u uzerinden delegate yapar; <>9 null kalirsa delegate target'siz dogar
            // ve cagri ABI'si kayar (crash). Dongu -> kayit sirasi korunur.
            var cctors = ctx.AllCodes.Where(c => IsEmittableCode(c) && !c.IsExternal && c.Name == "cctor").ToList();
            var cctorOf = new Dictionary<Primitive, Code>();
            foreach (var cc in cctors)
                cctorOf[cc.Owner] = cc;
            HashSet<Primitive> StaticDeps(Code root)
            {
                var owners = new HashSet<Primitive>();
                var seen = new HashSet<Code>();
                var stack = new Stack<Code>();
                stack.Push(root);
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
                sb.Append($"    {CName(cc.EncodeName())}();\n");
            }
            foreach (var cc in cctors)
                EmitCctor(cc);
            sb.Append("}\n");
            return sb.ToString();
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
            sb.Append($"typedef struct {CName(model.Name)} {{\n");
            if (!model.IsStruct)
                sb.Append("    GCHeader gc;\n");
            foreach (var f in Hierarchy.AllFields(model))
                sb.Append("    " + FieldDecl(f) + "\n");
            sb.Append($"}} {CName(model.Name)};\n");
            return sb.ToString();
        }

        // sadece skaler/FixedArray alanli struct'lar "atomic" (GC hic taramaz, trace yok).
        // Model-pointer alani olanlar icin trace fonksiyonu her pointer alani gc_shade eder.
        // Root kaydi (add_root/frame_root) BILINCLI OLARAK YOK: GC yalniz oyun dongusundeki
        // guvenli noktada (hicbir transpile edilmis cagri C stack'inde degilken) tetikleniyor,
        // o an local'ler zaten olu; sadece kalici/root'tan ulasilan nesneler canli sayiliyor.
        // GC'ye gorunen alanlar: dogrudan class/array referanslari ve struct icindeki tum referans yolları.
        static void CollectGcRefPaths(Primitive type, string path, List<string> paths)
        {
            if ((type.Type == PrimitiveType.Model && !type.IsStruct) || type.Type == PrimitiveType.Array)
            {
                paths.Add(path);
                return;
            }
            if (type.Type != PrimitiveType.Model || !type.IsStruct) return;
            foreach (var field in Hierarchy.AllFields(type))
                CollectGcRefPaths(field.Type, path + "." + CName(field.Name), paths);
        }

        // finalizer bildiren en yakin tip (self dahil zincir); ~X yoksa null
        static Primitive FindFinalizerDeclarer(Context ctx, Primitive model)
        {
            for (var t = model; t != null && !t.IsStruct; t = t.Parent)
                if (t != Primitive.Object && t != Primitive.String && ctx.TryGetCode(t.Name + "$finalize", out _))
                    return t;
            return null;
        }

        static string EmitTraceAndType(Primitive model)
        {
            var cn = CName(model.Name);
            // interface: instantiate edilmez -> ciplak Type. Adresi hem iface KIMLIGI (DIGITOYENGINE_implements/DIGITOYENGINE_itable
            // karsilastirmasi) hem is/as hedefi olarak kullanilir.
            if (model.IsInterface)
                return $"Type {cn}_type = {{ 0, 0, 0, 1, 0, &{strPool[model.Display]}, 0, 0, 0, 0, 0{TypeMetaTail(model)} }};\n";
            var pointerPaths = new List<string>();
            foreach (var field in Hierarchy.AllFields(model))
                CollectGcRefPaths(field.Type, "p->" + CName(field.Name), pointerPaths);
            var sb = new StringBuilder();
            string traceFn = "0";
            if (pointerPaths.Count > 0)
            {
                traceFn = $"trace_{cn}";
                sb.Append($"static void {traceFn}(GCHeader* o) {{\n");
                sb.Append($"    struct {cn}* p = (struct {cn}*)o;\n");
                foreach (var path in pointerPaths)
                    sb.Append($"    gc_shade((GCHeader*){path});\n");
                sb.Append("}\n");
            }
            string vtable = "0", nvtable = "0";
            if (model.VTable.Count > 0)
            {
                sb.Append($"static const void* const {cn}_vtable[] = {{ ");
                sb.Append(string.Join(", ", model.VTable.Select(m => $"(const void*)&{CName(m.EncodeName())}")));
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
                    sb.Append(string.Join(", ", model.ITables[iface].Select(m => $"(const void*)&{CName(m.EncodeName())}")));
                    sb.Append(" };\n");
                }
                sb.Append($"static const IfaceImpl {cn}_itables[] = {{ ");
                sb.Append(string.Join(", ", allIfaces.Select(i => $"{{ &{CName(i.Name)}_type, {cn}_it_{CName(i.Name)} }}")));
                sb.Append(" };\n");
                itables = cn + "_itables";
                nitables = allIfaces.Count.ToString();
            }
            int atomic = pointerPaths.Count == 0 ? 1 : 0;
            // finalizer zinciri: C# semantigi - turemisin ~'si once, sonra base'inki (erken return dahil,
            // cunku zincir methodun DISINDA thunk'ta). Bildirmeyen tip en yakin atanin thunk'ini kullanir.
            string finFn = "0";
            var declarer = FindFinalizerDeclarer(EmitCtx, model);
            if (declarer == model)
            {
                var baseDecl = model.Parent != null ? FindFinalizerDeclarer(EmitCtx, model.Parent) : null;
                sb.Append($"void digitoyengine_fin_{cn}(GCHeader* o) {{\n");
                sb.Append($"    {CName(model.Name + "$finalize")}((struct {cn}*)o);\n");
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
            sb.Append($"Type {cn}_type = {{ {traceFn}, {finFn}, sizeof(struct {cn}), {atomic}, {baseType}, &{strPool[model.Display]}, {vtable}, {nvtable}, {itables}, {nitables}, 0, 0, {members}, {ReflectionMemberCount(model)}, {genericArgs}, {genericArgCount}{TypeMetaTail(model)} }};\n");
            return sb.ToString();
        }

        // Reflection uyeleri: tum bildirilen alanlar (backing field'lar dahil; .NET GetFields(NonPublic) gibi) + property'ler.
        // Struct'lar dahil (kutu uzerinden erisim); interface/delegate'in alani yok.
        static IEnumerable<PrimitiveField> ReflectionFields(Primitive model) =>
            model.Fields.Concat(model.StaticFields);
        // Reflection adi: auto-property backing field'i IR'de "X__bk"; .NET adi "<X>k__BackingField" (GetField uyumu). Hash IR adindadir.
        static string ReflectionFieldName(PrimitiveField f) =>
            f.Name.EndsWith("__bk", StringComparison.Ordinal) ? "<" + f.Name.Substring(0, f.Name.Length - 4) + ">k__BackingField" : f.Name;

        static int ReflectionMemberCount(Primitive model) => ReflectionFields(model).Count() + model.Properties.Count;
        static bool HasReflectionMembers(Primitive p) => IsEmittableModel(p) && !p.IsInterface && !p.IsDelegate && ReflectionMemberCount(p) > 0;

        static string ReflectionTypeSym(Primitive type) =>
            (type.Type == PrimitiveType.Array || type.Type == PrimitiveType.FixedArray || type.Type == PrimitiveType.Pointer) ? "0" :
            (type.Type == PrimitiveType.Model && type.IsStruct) ? (reflectedValueStructs.ContainsKey(CName(type.Name)) ? "&" + TypeSym(type) : "0") :
            "&" + TypeSym(type);

        // Struct alanlari: kutu kopyasi (descriptor varsa); FieldInfo.GetValue .NET'te de kutular. Struct ifadesi rvalue
        // olabilir (property getter cagrisi) -> geciciye alinir.
        static string ReflectGetValue(Primitive type, string expr) =>
            type.Type == PrimitiveType.FixedArray || type.Type == PrimitiveType.Pointer ? "digitoyengine_reflect_unsupported()" :
            type.Type == PrimitiveType.Model && type.IsStruct ? (reflectedValueStructs.ContainsKey(CName(type.Name)) ? $"({{ {CType(type)} __rv = {expr}; (GCHeader*)digitoyengine_box_struct(&{TypeSym(type)}, &__rv, sizeof({CType(type)})); }})" : "digitoyengine_reflect_unsupported()") :
            type.IsEnum ? $"(GCHeader*)digitoyengine_ebox_{CName(type.Name)}({expr})" :
            BoxHelperFor(type) != null ? $"(GCHeader*){BoxHelperFor(type)}({expr})" :
            $"(GCHeader*)({expr})";

        static string ReflectSetValue(Primitive type, string value) =>
            BoxHelperFor(type) != null || type.IsEnum
                ? $"*({ScalarCType(type)}*)digitoyengine_unbox({value}, &{TypeSym(type)})"
                : type.Type == PrimitiveType.Model && type.IsStruct ? $"*({CType(type)}*)digitoyengine_unbox({value}, &{TypeSym(type)})"
                : type.Type == PrimitiveType.Array ? $"({CType(type)}){value}"
                : $"({CType(type)})digitoyengine_reflect_ref({value}, &{TypeSym(type)})";

        static string ReflectSetStatement(string access, Primitive type, string value) =>
            type.Type == PrimitiveType.FixedArray || type.Type == PrimitiveType.Pointer || (type.Type == PrimitiveType.Model && type.IsStruct && !reflectedValueStructs.ContainsKey(CName(type.Name)))
                ? "digitoyengine_reflect_unsupported();"
                : $"{access} = {ReflectSetValue(type, value)};";

        static Code ReflectionAccessor(Primitive model, string name) =>
            EmitCtx.TryGetCode(model.Name + "$" + name, out var code) ? code : null;

        // target: GCHeader* (class nesnesi ya da struct kutusu). self: C imzasinin bekledigi struct pointer'i.
        static string ReflectSelf(Primitive model) =>
            model.IsStruct ? $"((struct {CName(model.Name)}*)((char*)target + sizeof(GCHeader)))" : $"((struct {CName(model.Name)}*)target)";

        static string PropertyCall(Primitive model, Code accessor, string value = null)
        {
            var args = value == null ? "" : ", " + value;
            if (accessor.IsStatic)
                return $"{CName(accessor.EncodeName())}({(value ?? "")})";
            if (accessor.IsVirtual && !model.IsStruct)
            {
                int slot = model.VTable.IndexOf(accessor);
                var signature = value == null
                    ? $"{CType(accessor.ReturnType)}(*)(struct {CName(model.Name)}*)"
                    : $"void(*)(struct {CName(model.Name)}*, {CType(accessor.Arguments[1].Type)})";
                return $"(({signature})target->type->vtable[{slot}])({ReflectSelf(model)}{args})";
            }
            return $"{CName(accessor.EncodeName())}({ReflectSelf(model)}{args})";
        }

        static string EmitReflectionMembers(Primitive model)
        {
            var cn = CName(model.Name);
            var sb = new StringBuilder();
            var entries = new List<string>();
            int index = 0;
            foreach (var field in ReflectionFields(model))
            {
                var suffix = $"{cn}_{index++}";
                var access = field.IsStatic ? StaticSym(field) : $"{ReflectSelf(model)}->{CName(field.Name)}";
                sb.Append($"static GCHeader* digitoyengine_refget_{suffix}(GCHeader* target) {{ return {ReflectGetValue(field.Type, access)}; }}\n");
                string set = "0";
                if (!field.IsReadonly || !field.IsStatic) // .NET: readonly instance alanina SetValue serbest; static readonly yazilamaz
                {
                    sb.Append($"static void digitoyengine_refset_{suffix}(GCHeader* target, GCHeader* value) {{ {ReflectSetStatement(access, field.Type, "value")} }}\n");
                    set = $"digitoyengine_refset_{suffix}";
                }
                sb.Append(EmitAttrTable($"{cn}_fattrs_{CName(field.Name)}", field.Attributes));
                entries.Add($"{{ &{strPool[ReflectionFieldName(field)]}, &{cn}_type, {ReflectionTypeSym(field.Type)}, digitoyengine_refget_{suffix}, {set}, 0, DIGITOYENGINE_MEMBER_FIELD, {Convert.ToInt32(field.IsStatic)}, {Convert.ToInt32(field.IsReadonly)}{MemberMetaTail(model, field)} }}");
            }
            foreach (var property in model.Properties)
            {
                var suffix = $"{cn}_{index++}";
                var getter = property.HasGet ? ReflectionAccessor(model, "get_" + property.Name) : null;
                var setter = property.HasSet ? ReflectionAccessor(model, "set_" + property.Name + "_" + property.Type.Name.Replace('.', '_')) : null;
                string get = "0", set = "0";
                if (getter != null)
                {
                    sb.Append($"static GCHeader* digitoyengine_refget_{suffix}(GCHeader* target) {{ return {ReflectGetValue(property.Type, PropertyCall(model, getter))}; }}\n");
                    get = $"digitoyengine_refget_{suffix}";
                }
                if (setter != null)
                {
                    sb.Append($"static void digitoyengine_refset_{suffix}(GCHeader* target, GCHeader* value) {{ {PropertyCall(model, setter, ReflectSetValue(property.Type, "value"))}; }}\n");
                    set = $"digitoyengine_refset_{suffix}";
                }
                var pattrs = $"{cn}_pattrs_{CName(property.Name)}";
                sb.Append(EmitAttrTable(pattrs, property.Attributes));
                entries.Add($"{{ &{strPool[property.Name]}, &{cn}_type, {ReflectionTypeSym(property.Type)}, {get}, {set}, 0, DIGITOYENGINE_MEMBER_PROPERTY, {Convert.ToInt32(property.IsStatic)}, 0, .tag = '{ExportTag(property.Type)}'{AttrTail(pattrs, property.Attributes)}, .cilattrs = {property.CilAttributes} }}");
            }
            sb.Append($"static DigitoyEngineMember {cn}_members[] = {{\n    {string.Join(",\n    ", entries)}\n}};\n\n");
            return sb.ToString();
        }

        // struct basina gc_alloc tabanli constructor: fixed-array alanlar struct icine gomulu oldugu icin
        // ayrica allocate edilmesine gerek yok (New op'unun karsiligi).
        static string EmitConstructor(Primitive model)
        {
            var cn = CName(model.Name);
            return $"static inline struct {cn}* New_{cn}(void) {{ return (struct {cn}*)gc_alloc(&{cn}_type); }}\n";
        }

        static string Prototype(Code code)
        {
            var an = CArgNames(code, new HashSet<string>());
            var pars = string.Join(", ", code.Arguments.Select((a, i) => $"{ParamCType(a)} {an[i]}"));
            if (code.Arguments.Count == 0) pars = "void";
            return $"{CType(code.ReturnType)} {CSym(code)}({pars})";
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
            t.Type == PrimitiveType.Model && t.IsStruct ? 'v' :
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

        static bool IsNumericVal(Primitive t) =>
            t != null && t.Type != PrimitiveType.Model && t.Type != PrimitiveType.Array &&
            t.Type != PrimitiveType.FixedArray && t.Type != PrimitiveType.Pointer && t.Type != PrimitiveType.Void;

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
                if (float.IsNaN(f)) return new CVal("(0.0f / 0.0f)", Primitive.Float);
                return new CVal(FloatLiteralText(f.ToString("R")) + "f", Primitive.Float);
            }
            if (value is double d)
            {
                if (double.IsPositiveInfinity(d)) return new CVal("(1.0 / 0.0)", Primitive.Double);
                if (double.IsNegativeInfinity(d)) return new CVal("(-1.0 / 0.0)", Primitive.Double);
                if (double.IsNaN(d)) return new CVal("(0.0 / 0.0)", Primitive.Double);
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
            if (code.Arguments.Count == 0) pars = "void";
            var sb = new StringBuilder();
            // inline ipucu: tek cagiran sarmalayici -> -O2'de cagri kaybolur. always_inline BILEREK yok
            // (setjmp'li govdeleri zorla inline etmek tanimsiz davranisa acilir; karar clang'in)
            sb.Append($"static inline {CType(code.ReturnType)} {fn}__impl({pars}) {{\n");
            for (int i = 0; i < code.Locals.Count; i++)
                sb.Append($"    {CType(code.Locals[i])}{(hasTry ? " volatile" : "")} {ln[i]};\n"); // setjmp kurali: longjmp sonrasi okunan locals volatile olmali
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
            // dallanma oncesi bekleyen ifadeleri C temp'ine dok: jump hedefinde her iki yol AYNI
            // temp adlarini gorur (ternary/kisa-devre degerleri alici beklerken emit edilebilir).
            // Alt-stack once hesaplanir = C# soldan-saga degerlendirme sirasiyla da tutarli.
            void SpillStack()
            {
                for (int si = 0; si < stack.Count; si++)
                {
                    var ex = stack[si].Expr;
                    if (ex.StartsWith("__s") || LiteralExpr(ex)) continue; // spill'li ya da saf sabit: tasima gereksiz
                    var t = $"__s{tempCounter++}";
                    sb.Append($"    {CType(stack[si].Type)} {t} = {ex};\n");
                    stack[si] = new CVal(t, stack[si].Type, stack[si].Owner); // sahip bilgisi spill'de korunur
                }
            }
            // yan etkisiz/degismez ifade: sayi, char, string havuzu adresi (IDENT DEGIL - mutasyon bayatligi riski)
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
            // tam sayi bolme/mod: bolen sifir denetimi (C#: DivideByZeroException; float'ta yok)
            CVal DivCheck(CVal b, Op op)
            {
                if (b.Type != null && (b.Type.Type == PrimitiveType.Float || b.Type.Type == PrimitiveType.Double)) return b;
                EmitLine(op);
                var t = $"__t{tempCounter++}";
                sb.Append($"    {CType(b.Type ?? Primitive.Int)} {t} = {b.Expr};\n");
                sb.Append($"    DIGITOYENGINE_DIVCHECK({t});\n");
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
                    if (v.Type == Primitive.Void) return $"(struct {CName(target.Name)}){{0}}";
                    return $"(struct {CName(target.Name)}){{ 1, ({CType(nullableValue)})({v.Expr}) }}";
                }
                if (target != null && target.Type == PrimitiveType.Model && !target.IsStruct && v.Type != null && v.Type != target)
                {
                    if (v.Type.IsEnum) return $"(({CType(target)})digitoyengine_ebox_{CName(v.Type.Name)}({v.Expr}))";
                    var bh = BoxHelperFor(v.Type);
                    if (bh != null) return $"(({CType(target)}){bh}({v.Expr}))";
                    if (v.Type.Type == PrimitiveType.Model && v.Type.IsStruct)
                    {
                        // Ortuk kutulama (generic T=struct: object parametresi / IList<object> gibi yollar).
                        // Descriptor pre-pass'te toplanmamis olabilir -> burada kaydet (EmitValueStructType sonra yazar).
                        var key = CName(v.Type.Name);
                        if (!reflectedValueStructs.ContainsKey(key))
                            throw new Exception($"struct kutulama descriptor'suz: {v.Type.Name} (pre-pass kacirdi)");
                        return $"(({CType(target)}){BoxStruct(v.Type, v.Expr)})";
                    }
                    return $"({CType(target)})({v.Expr})";
                }
                // unsafe: pointer <-> native int (IntPtr=Long/ULong) serbest cast (ayni boyut; C -Wint-conversion susar).
                // Yalniz native-int genisligi; Int'e truncate ETME (pointer'i 32-bit'e kirpardi).
                if (target?.Type == PrimitiveType.Pointer && (v.Type?.Type == PrimitiveType.Long || v.Type?.Type == PrimitiveType.ULong))
                    return $"(({CType(target)})(size_t)({v.Expr}))";
                if ((target?.Type == PrimitiveType.Long || target?.Type == PrimitiveType.ULong) && v.Type?.Type == PrimitiveType.Pointer)
                    return $"(({CType(target)})(size_t)({v.Expr}))";
                return v.Expr;
            }

            bool NullableValueType(Primitive type, out Primitive valueType)
            {
                valueType = null;
                if (type == null || type.Type != PrimitiveType.Model || !type.IsStruct || !type.Name.StartsWith(WellKnown.Nullable + "`1<")) return false;
                var field = type.Fields.FirstOrDefault(f => f.Name == "value");
                if (field == null) return false;
                valueType = field.Type;
                return true;
            }

            var openTries = new List<int>(); // compile-time TryBegin/TryEnd eslesmesi (LIFO)
            int lastLine = 0; // DIGITOYENGINE_LINE dedup: ayni satiri arka arkaya yazma
            int lastStep = 0; // DIGITOYENGINE_STEP dedup (paketli konum degisiminde bir kez)
            void EmitLine(Op o) // firlatabilen op'lardan once: trace'teki satir dogru kalsin
            {
                if (o.Line > 0 && o.Line != lastLine)
                {
                    sb.Append($"    DIGITOYENGINE_LINE({o.Line});\n"); // Op.Line zaten paketli: (satir<<10)|kolon
                    lastLine = o.Line;
                }
            }

            for (int i = 0; i < code.Operations.Count; i++)
            {
                if (jumpTargets.Contains(i))
                {
                    foreach (var pv in stack) // bekleyenler yalniz spill temp'i ya da saf sabit olabilir (SpillStack sozlesmesi)
                        if (!pv.Expr.StartsWith("__s") && !LiteralExpr(pv.Expr))
                            throw new Exception($"{code.EncodeName()}: jump hedefinde spill edilmemis ifade var (well-formed olmayan Op dizisi)");
                    sb.Append($"L{i}: ;\n"); // handler girisinde otomatik push YOK: ExIs/ExBind okur (DIGITOYENGINE_ex_* globalleri)
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
                            var inst = Pop();
                            if (inst.Type.Type == PrimitiveType.Pointer) // struct lvalue zinciri: p->alan
                                Push(new CVal($"(({inst.Expr})->{CName(op.Field.Name)})", op.Field.Type));
                            else if (inst.Type.IsStruct)
                                Push(new CVal($"{inst.Expr}.{CName(op.Field.Name)}", op.Field.Type));
                            else
                            {
                                EmitLine(op);
                                var t = $"__o{tempCounter++}";
                                Push(new CVal($"(({{ {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK({t}); {t}->{CName(op.Field.Name)}; }}))", op.Field.Type));
                            }
                            break;
                        }
                    case OpType.SetField:
                        {
                            SpillStack(); // C# sira garantisi: bekleyen okumalar mutasyondan ONCE degerlenir
                            var v = Pop(); var inst = Pop();
                            if (inst.Type.Type == PrimitiveType.Pointer)
                            {
                                var pt = $"__p{tempCounter++}";
                                var barrier = WriteBarrier(inst.Owner ?? "0", $"{pt}->{CName(op.Field.Name)}", op.Field.Type);
                                sb.Append($"    {{ {CType(inst.Type)} {pt} = {inst.Expr}; {pt}->{CName(op.Field.Name)} = {Coerce(v, op.Field.Type)};{barrier} }}\n");
                            }
                            else if (inst.Type.IsStruct)
                                sb.Append($"    {inst.Expr}.{CName(op.Field.Name)} = {Coerce(v, op.Field.Type)};\n");
                            else
                            {
                                EmitLine(op);
                                var t = $"__o{tempCounter++}";
                                // Kusakli GC write barrier: eski nesneye genc referans yazisi remembered set'e
                                // (gc_write_barrier: obj tenured && val genc -> gc_remember). Yalniz referans tasiyan alanlar.
                                var barrier = WriteBarrier(t, $"{t}->{CName(op.Field.Name)}", op.Field.Type);
                                sb.Append($"    {{ {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK({t}); {t}->{CName(op.Field.Name)} = {Coerce(v, op.Field.Type)};{barrier} }}\n");
                            }
                            break;
                        }
                    case OpType.AddrField:
                        {
                            var inst = Pop();
                            bool fixedBuf = op.Field.Type.Type == PrimitiveType.FixedArray; // fixed buffer: adres = dizi datasi (float*), decay
                            var resType = Primitive.PointerOf(fixedBuf ? op.Field.Type.ElementType : op.Field.Type);
                            if (inst.Type.Type == PrimitiveType.Pointer)
                                Push(new CVal(fixedBuf ? $"(({inst.Expr})->{CName(op.Field.Name)})" : $"(&(({inst.Expr})->{CName(op.Field.Name)}))", resType, inst.Owner));
                            else // class alici: null check + adres; sahip temp'i ic yazimlarin barrier'i icin tasinir
                            {
                                SpillStack(); // alici simdi degerlenir: bekleyen ifadeler once (sira korunur)
                                EmitLine(op);
                                var t = $"__o{tempCounter++}";
                                sb.Append($"    {CType(inst.Type)} {t} = {inst.Expr}; DIGITOYENGINE_NULLCHECK({t});\n");
                                var access = fixedBuf ? $"{t}->{CName(op.Field.Name)}" : $"(&({t}->{CName(op.Field.Name)}))";
                                Push(new CVal(access, resType, t));
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
                            EmitLine(op);
                            if (arr.Type.Type == PrimitiveType.Array)
                            {
                                // dizi sahibi temp'e (ic yazimlarin barrier'i icin), indeksleme statement-expression'da
                                SpillStack();
                                var ao = $"__o{tempCounter++}";
                                sb.Append($"    {CType(arr.Type)} {ao} = {arr.Expr};\n");
                                var a = $"__a{tempCounter++}";
                                Push(new CVal($"(({{ {ArrayIndexSetup(new CVal(ao, arr.Type), indices, a)}&((({CType(elem)}*){a}->data)[{a}off]); }}))", Primitive.PointerOf(elem), ao));
                            }
                            else // FixedArray
                            {
                                var i2 = $"__i{tempCounter++}";
                                Push(new CVal($"(({{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS({i2}, {arr.Type.FixedSize}); &({arr.Expr}[{i2}]); }}))", Primitive.PointerOf(elem)));
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
                    case OpType.AddrStatic: Push(new CVal($"(&{StaticSym(op.Field)})", Primitive.PointerOf(op.Field.Type))); break;

                    case OpType.GetIndex:
                        {
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var indices = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) indices.Insert(0, Pop());
                            var arr = Pop();
                            var elem = arr.Type.ElementType;
                            EmitLine(op);
                            if (arr.Type.Type == PrimitiveType.Array) // VmArray: null + bounds check'li
                            {
                                var a = $"__a{tempCounter++}";
                                Push(new CVal($"(({{ {ArrayIndexSetup(arr, indices, a)}(({CType(elem)}*){a}->data)[{a}off]; }}))", elem));
                                break;
                            }
                            if (arr.Type.Type == PrimitiveType.FixedArray) // gomulu dizi: boyut sabit, null yok
                            {
                                var i2 = $"__i{tempCounter++}";
                                Push(new CVal($"(({{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS({i2}, {arr.Type.FixedSize}); {arr.Expr}[{i2}]; }}))", elem));
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
                            EmitLine(op);
                            if (arr.Type.Type == PrimitiveType.Array)
                            {
                                var a = $"__a{tempCounter++}";
                                var slot = $"(({CType(arr.Type.ElementType)}*){a}->data)[{a}off]";
                                var barrier = WriteBarrier(a, slot, arr.Type.ElementType);
                                sb.Append($"    {{ {ArrayIndexSetup(arr, indices, a)}{slot} = {Coerce(v, arr.Type.ElementType)};{barrier} }}\n");
                            }
                            else if (arr.Type.Type == PrimitiveType.FixedArray)
                            {
                                var i2 = $"__i{tempCounter++}";
                                sb.Append($"    {{ int {i2} = {indices[0].Expr}; DIGITOYENGINE_BOUNDS({i2}, {arr.Type.FixedSize}); {arr.Expr}[{i2}] = {v.Expr}; }}\n");
                            }
                            else
                                sb.Append($"    {arr.Expr}[{indices[0].Expr}] = {v.Expr};\n");
                            break;
                        }

                    case OpType.NewArray:
                        {
                            int rank = op.Slot > 0 ? op.Slot : 1;
                            var dims = new List<CVal>();
                            for (int ii = 0; ii < rank; ii++) dims.Insert(0, Pop());
                            var elem = op.PrimitiveRef;
                            int isref = (elem.Type == PrimitiveType.Model && !elem.IsStruct) || elem.Type == PrimitiveType.Array ? 1 : 0;
                            var dn = $"__dims{tempCounter++}";
                            // ref tasiyan struct eleman: uretilen dizi Type'i (GC eleman eleman tarar); yoksa runtime ref/atomic tipleri.
                            // Eleman descriptor'u (is T[] / GetType / Array.GetValue): ExportTypeSym; jagged/pointer eleman -> 0 (bilinmiyor).
                            string allocType = StructArrayTypeSym(elem) is string ats ? $"&{ats}" : (isref == 1 ? "&vmarray_ref_type" : "&vmarray_val_type");
                            string alloc = $"vmarray_new_rank_te({rank}, {dn}, sizeof({CType(elem)}), {allocType}, {ExportTypeSym(elem)})";
                            Push(new CVal($"(({{ int {dn}[{rank}] = {{ {string.Join(", ", dims.Select(d => d.Expr))} }}; {alloc}; }}))", Primitive.ArrayOf(elem, rank)));
                            break;
                        }
                    case OpType.ArrayLength:
                        {
                            var arr = Pop();
                            EmitLine(op);
                            var a = $"__a{tempCounter++}";
                            Push(new CVal($"(({{ VmArray* {a} = {arr.Expr}; DIGITOYENGINE_NULLCHECK({a}); {a}->len; }}))", Primitive.Int));
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
                        { var v = Pop(); Push(new CVal($"(({CType(op.PrimitiveRef)})({v.Expr}))", op.PrimitiveRef)); break; }
                    case OpType.IsType:
                        {
                            var o = Pop(); // alici iki kez gecer (null test + tip zinciri) -> temp
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            Push(new CVal($"(({t} && {TypeTest(op.PrimitiveRef, $"{t}->gc.type", t)}) ? 1 : 0)", Primitive.Int));
                            break;
                        }
                    case OpType.IsValueType:
                        {
                            var value = Pop();
                            var sourceType = op.PrimitiveRef;
                            if (sourceType.IsGenericParameter)
                                throw new Exception($"IsValueType somutlastirilmamis tip parametresi: {sourceType.Name}");
                            bool isValueType = IsNumericVal(sourceType) || (sourceType.Type == PrimitiveType.Model && sourceType.IsStruct);
                            Push(new CVal($"((void)({value.Expr}), {(isValueType ? 1 : 0)})", Primitive.Bool));
                            break;
                        }
                    case OpType.AsType:
                        {
                            var o = Pop();
                            if (o.Type.IsGenericParameter)
                                throw new Exception($"AsType somutlastirilmamis tip parametresi: {o.Type.Name}");
                            bool sourceIsValueType = IsNumericVal(o.Type) || (o.Type.Type == PrimitiveType.Model && o.Type.IsStruct);
                            if (sourceIsValueType)
                            {
                                if (op.PrimitiveRef == Primitive.Object || op.PrimitiveRef == Primitive.ValueType)
                                {
                                    string boxed;
                                    if (o.Type.IsEnum)
                                        boxed = $"digitoyengine_ebox_{CName(o.Type.Name)}({o.Expr})";
                                    else if (BoxHelperFor(o.Type) is string helper)
                                        boxed = $"{helper}({o.Expr})";
                                    else if (o.Type.Type == PrimitiveType.Model && o.Type.IsStruct)
                                        boxed = BoxStruct(o.Type, o.Expr);
                                    else
                                        throw new Exception($"as boxing desteklenmeyen tip: {o.Type.Name}");
                                    Push(new CVal($"(({CType(op.PrimitiveRef)}){boxed})", op.PrimitiveRef));
                                }
                                else
                                    Push(new CVal($"((void)({o.Expr}), ({CType(op.PrimitiveRef)})0)", op.PrimitiveRef));
                                break;
                            }
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            bool targetIsValueType = IsNumericVal(op.PrimitiveRef) || op.PrimitiveRef.IsEnum || (op.PrimitiveRef.Type == PrimitiveType.Model && op.PrimitiveRef.IsStruct);
                            if (targetIsValueType)
                            {
                                // CIL isinst deger-tip: boxed referans tip tutarsa aynen (null test/unbox.any devaminda), yoksa null
                                Push(new CVal($"(({t} && {TypeTest(op.PrimitiveRef, $"{t}->gc.type")}) ? (VmObject*){t} : (VmObject*)0)", Primitive.Object));
                                break;
                            }
                            Push(new CVal($"(({t} && {TypeTest(op.PrimitiveRef, $"{t}->gc.type", t)}) ? ({CType(op.PrimitiveRef)})(void*){t} : 0)", op.PrimitiveRef));
                            break;
                        }
                    case OpType.CastClass:
                        {
                            // Generic sablonda `(T)obj` = unbox.any !T -> frontend T'yi referans sayip CastClass uretir; T deger
                            // tipine somutlasinca (int/enum/struct) bu bir UNBOX'tir (pointer->int cast degil).
                            if (IsNumericVal(op.PrimitiveRef) || op.PrimitiveRef.IsEnum || (op.PrimitiveRef.Type == PrimitiveType.Model && op.PrimitiveRef.IsStruct))
                            {
                                EmitLine(op);
                                var ov = Pop();
                                var ut = op.PrimitiveRef;
                                string uct = ut.Type == PrimitiveType.Model && ut.IsStruct ? $"struct {CName(ut.Name)}" : ScalarCType(ut);
                                Push(new CVal($"(*({uct}*)digitoyengine_unbox((GCHeader*)({ov.Expr}), &{TypeSym(ut)}))", ut));
                                break;
                            }
                            var o = Pop();
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
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
                            var zx = zt.Type == PrimitiveType.Model && zt.IsStruct ? $"(struct {CName(zt.Name)}){{0}}" : $"(({CType(zt)})0)";
                            Push(new CVal(zx, zt));
                            break;
                        }
                    case OpType.TypeOf:
                        {
                            EmitCtx.TryGetPrimitive("System.Type", out var st);
                            Push(new CVal($"(({CType(st)})digitoyengine_type_wrapper(&{TypeSym(op.PrimitiveRef)}))", st));
                            break;
                        }
                    case OpType.Box:
                        {
                            var v = Pop();
                            var sourceType = op.PrimitiveRef;
                            string bx;
                            if (sourceType.IsEnum)
                                bx = $"digitoyengine_ebox_{CName(sourceType.Name)}({v.Expr})";
                            else if (BoxHelperFor(sourceType) is string helper)
                                bx = $"{helper}({v.Expr})";
                            else if (sourceType.Type == PrimitiveType.Array || (sourceType.Type == PrimitiveType.Model && !sourceType.IsStruct))
                            {
                                // referans tipte box = kimlik (C# ox !!T, T sinif kisitli: alan erisimi oncesi).
                                // Statik tipi KORU: ardindan gelen ldfld/call dogru struct'a gitsin.
                                Push(new CVal(v.Expr, sourceType));
                                break;
                            }
                            else if (sourceType.Type == PrimitiveType.Model && sourceType.IsStruct)
                                bx = BoxStruct(sourceType, v.Expr);
                            else
                                throw new Exception($"boxing desteklenmeyen tip: {sourceType.Name}");
                            Push(new CVal(bx, Primitive.Object));
                            break;
                        }
                    case OpType.Unbox:
                        {
                            EmitLine(op);
                            var o = Pop();
                            var ut = op.PrimitiveRef;
                            string ct = ut.Type == PrimitiveType.Model && ut.IsStruct ? $"struct {CName(ut.Name)}" : ScalarCType(ut);
                            Push(new CVal($"(*({ct}*)digitoyengine_unbox((GCHeader*)({o.Expr}), &{TypeSym(ut)}))", ut));
                            break;
                        }
                    case OpType.UnboxOrDefault:
                        {
                            var o = Pop(); // alici iki kez gecer -> temp (IsType deseni)
                            var t = $"__t{tempCounter++}";
                            sb.Append($"    {CType(o.Type)} {t} = {o.Expr};\n");
                            var ut = op.PrimitiveRef;
                            if (ut.Type == PrimitiveType.Model && ut.IsStruct)
                            {
                                var sct = $"struct {CName(ut.Name)}";
                                Push(new CVal($"(({t} && {t}->gc.type == &{TypeSym(ut)}) ? *({sct}*)((char*){t} + sizeof(GCHeader)) : ({sct}){{0}})", ut));
                                break;
                            }
                            var sct2 = ScalarCType(ut);
                            Push(new CVal($"(({t} && {t}->gc.type == &{TypeSym(ut)}) ? *({sct2}*)((char*){t} + sizeof(GCHeader)) : ({sct2})0)", ut));
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
                                Push(new CVal($"(struct {CName(result.Name)}){{ ({lPresent} && {rPresent}), (({CType(valueType)})({lValue}) {opText} ({CType(valueType)})({rValue})) }}", result));
                            }
                            break;
                        }
                    case OpType.DelegateNew:
                        {
                            EmitLine(op);
                            var dt = op.PrimitiveRef;
                            var m = op.Code;
                            if (m.IsStatic)
                            {
                                Push(new CVal($"digitoyengine_delegate_new(&{TypeSym(dt)}, (const void*)&{CName(m.EncodeName())}, 0)", dt));
                                break;
                            }
                            var recv = Pop();
                            var rt2 = $"__t{tempCounter++}";
                            sb.Append($"    {CType(recv.Type)} {rt2} = {recv.Expr};\n");
                            sb.Append($"    DIGITOYENGINE_NULLCHECK({rt2});\n"); // C#: null alicidan delegate yaratmak NRE
                            var dfn = op.Slot >= 0 ? $"{rt2}->gc.type->vtable[{op.Slot}]" : $"(const void*)&{CName(m.EncodeName())}";
                            Push(new CVal($"digitoyengine_delegate_new(&{TypeSym(dt)}, {dfn}, (GCHeader*){rt2})", dt));
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
                            sb.Append($"    DIGITOYENGINE_NULLCHECK({d});\n");
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
                    case OpType.New:
                        {
                            if (op.PrimitiveRef == Primitive.Object) // runtime koku: New_ fonksiyonu uretilmez
                            {
                                Push(new CVal("((VmObject*)gc_alloc(&vmobject_type))", Primitive.Object));
                                break;
                            }
                            var cn = CName(op.PrimitiveRef.Name);
                            // IsStruct: heap yok, sifirlanmis C99 compound literal (deger tipi, kopyalanarak akar)
                            var expr = op.PrimitiveRef.IsStruct ? $"(struct {cn}){{0}}" : $"New_{cn}()";
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
                            Push(new CVal(t, dupType, top.Owner));
                            Push(new CVal(t, dupType, top.Owner));
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
                                sb.Append($"    return {(code.ReturnType.IsStruct ? $"(struct {CName(code.ReturnType.Name)}){{0}}" : $"({CType(code.ReturnType)})0")};\n");
                            else
                                sb.Append($"    return {Coerce(Pop(), code.ReturnType)};\n");
                        }
                        else sb.Append("    return;\n");
                        break;

                    case OpType.Call:
                        {
                            EmitLine(op);
                            var values = new CVal[op.Code.Arguments.Count];
                            for (int a = values.Length - 1; a >= 0; a--) values[a] = Pop();
                            SpillStack(); // cagri yan etkileri: alt-stack okumalari once degerlensin
                            var args = new string[values.Length];
                            for (int a = 0; a < args.Length; a++) // C# soldan-saga arguman sirasi (C'de belirsiz!)
                                args[a] = MaterializeCallArg(values[a], op.Code.Arguments[a]);
                            var call = WithPostCallRemember($"{CSym(op.Code)}({string.Join(", ", args)})", op.Code.ReturnType, values);
                            if (op.Code.ReturnType != Primitive.Void) Push(new CVal(call, op.Code.ReturnType));
                            else sb.Append($"    {call};\n");
                            break;
                        }

                    case OpType.CallVirtual:
                        {
                            EmitLine(op);
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
                                if (op.Code.Name == "GetHashCode" && vArgc == 1)
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
                                if (op.Code.Name.StartsWith("Equals") && vArgc == 2 && stack[stack.Count - 1].Type == vRecv.Type)
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
                            sb.Append($"    DIGITOYENGINE_NULLCHECK({recv});\n"); // wasm: explicit; native: MMU+VEH yakalar (makro bos)
                            args[0] = recv;
                            for (int a = 1; a < args.Length; a++)
                                args[a] = MaterializeArg(args[a], op.Code.Arguments[a]);
                            var fnSig = $"{CType(op.Code.ReturnType)}(*)({string.Join(", ", op.Code.Arguments.Select(a => (a.IsRef || a.IsOut) ? CType(a.Type) + "*" : CType(a.Type)))})";
                            var table = op.Code.Owner.IsInterface
                                ? $"DIGITOYENGINE_itable({recv}->gc.type, &{CName(op.Code.Owner.Name)}_type)"
                                : $"{recv}->gc.type->vtable";
                            var call = WithPostCallRemember($"(({fnSig}){table}[{op.Slot}])({string.Join(", ", args)})", op.Code.ReturnType, vvals);
                            if (op.Code.ReturnType != Primitive.Void) Push(new CVal(call, op.Code.ReturnType));
                            else sb.Append($"    {call};\n");
                            break;
                        }

                    case OpType.GetStatic: Push(new CVal(StaticSym(op.Field), op.Field.Type)); break;
                    case OpType.SetStatic: { SpillStack(); var v = Pop(); sb.Append($"    {StaticSym(op.Field)} = {Coerce(v, op.Field.Type)};{WriteBarrier("0", StaticSym(op.Field), op.Field.Type)}\n"); break; }

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
                    case OpType.ExIs:
                        {
                            var e = $"__e{tempCounter++}";
                            Push(new CVal($"(({{ GCHeader* {e} = DIGITOYENGINE_ex_current(); ({e} && {TypeTest(op.PrimitiveRef, $"{e}->type")}) ? 1 : 0; }}))", Primitive.Int));
                            break;
                        }
                    case OpType.ExBind:
                        {
                            var e = $"__ex{tempCounter++}";
                            sb.Append($"    GCHeader* {e}h = DIGITOYENGINE_ex_current();\n");
                            sb.Append($"    if (!{e}h || !{TypeTest(op.PrimitiveRef, $"{e}h->type")}) DIGITOYENGINE_rethrow();\n"); // uymayan tip: dis try'a devret
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
                                sb.Append($"    {{ {CType(et)}* {pt} = ({CType(et)}*)({p.Expr}); *({pt}) = {Coerce(v, et)};{WriteBarrier("0", $"(*{pt})", et)} }}\n");
                                break;
                            }
                            // ic isaretci yazimi: sahip biliniyorsa write barrier (struct alani/elemani icindeki ref'ler dahil)
                            var barrier = et != null ? WriteBarrier(p.Owner ?? "0", $"(*{pt})", et) : "";
                            sb.Append($"    {{ {CType(p.Type)} {pt} = {p.Expr}; *({pt}) = {(p.Type?.Type == PrimitiveType.Pointer ? Coerce(v, p.Type.ElementType) : v.Expr)};{barrier} }}\n");
                            break;
                        }

                    case OpType.Add: { var b = Pop(); var a = Pop(); Push(NumBin("+", a, b)); break; }
                    case OpType.Sub: { var b = Pop(); var a = Pop(); Push(NumBin("-", a, b)); break; }
                    case OpType.Mul: { var b = Pop(); var a = Pop(); Push(NumBin("*", a, b)); break; }
                    case OpType.Div: { var b = Pop(); var a = Pop(); a = MaterializeVal(a); Push(NumBin("/", a, DivCheck(b, op))); break; }
                    case OpType.Mod: { var b = Pop(); var a = Pop(); a = MaterializeVal(a); Push(NumBin("%", a, DivCheck(b, op))); break; }
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
                    case OpType.Shl: { var b = Pop(); var a = Pop(); Push(new CVal($"((({ScalarCType(a.Type ?? Primitive.Int)})({a.Expr})) << ({b.Expr}))", a.Type ?? Primitive.Int)); break; }
                    case OpType.Shr: { var b = Pop(); var a = Pop(); Push(new CVal($"((({ScalarCType(a.Type ?? Primitive.Int)})({a.Expr})) >> ({b.Expr}))", a.Type ?? Primitive.Int)); break; }
                    case OpType.Not: { var a = Pop(); Push(new CVal($"(!({a.Expr}))", Primitive.Int)); break; }

                    default: throw new NotImplementedException($"CTranspiler icin henuz desteklenmeyen opcode: {op.Type}");
                }
            }
            sb.Append("}\n");

            // sarmalayici: asil adla, push + impl cagrisi + pop (impl'deki throw'lar RtTry.sp
            // restore'uyla zaten dogru unwind eder; deger cikisi tek noktadan). Trace kaydi = tek meta tablosundaki MethodInfo.
            var callArgs = string.Join(", ", an);
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
