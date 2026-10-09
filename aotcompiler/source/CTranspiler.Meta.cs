using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Language
{
    // TEK META (docs/modules.md): .NET reflection descriptor'lari (Type / DigitoyEngineMember / MethodInfo) ayni zamanda
    // dinamik modul yukleyicinin (vmint.c) host baglama verisidir. Ayri export tablosu YOK: tip hash'i + metot kayitlari
    // (hash, fn, sekil thunk'i, sanal slot, trampoline) + alan yerlesimi (offset/size/addr) descriptor'larin icindedir.
    // Bu dosya: kayit toplama, sekil thunk'lari, host->modul trampoline'leri, tip/metot tablolari.
    public static partial class CTranspiler
    {
        // FNV-1a 64 (UTF-8): vmrt.c de_hash64 ile birebir.
        public static ulong Fnv64(string s)
        {
            ulong h = 1469598103934665603ul;
            foreach (var b in Encoding.UTF8.GetBytes(s))
            {
                h ^= b;
                h *= 1099511628211ul;
            }
            return h;
        }

        // alan/arguman etiketi (vmrt.h DeSlot etiket sozlesmesi)
        static char ExportTag(Primitive t)
        {
            switch (t.Type)
            {
                case PrimitiveType.Int: return 'i';
                case PrimitiveType.UInt: return 'u';
                case PrimitiveType.Long: return 'l';
                case PrimitiveType.ULong: return 'q';
                case PrimitiveType.Short: return 'h';
                case PrimitiveType.UShort: return 'H';
                case PrimitiveType.Byte: return 'b';
                case PrimitiveType.SByte: return 'z';
                case PrimitiveType.Char: return 'c';
                case PrimitiveType.Bool: return 'B';
                case PrimitiveType.Float: return 'f';
                case PrimitiveType.Double: return 'd';
                case PrimitiveType.Void: return 'V';
                case PrimitiveType.Pointer:
                case PrimitiveType.FixedArray: return 'p';
                case PrimitiveType.Array: return 'o';
                default: return t.IsStruct ? 'v' : 'o';
            }
        }

        // tip descriptor sembolu (export icin): emit edilmemis/descriptor'suz tipler 0
        static string ExportTypeSym(Primitive t)
        {
            if (t == null) return "0";
            if (t == Primitive.Object) return "&vmobject_type";
            if (t == Primitive.String) return "&vmstring_type";
            if (t == Primitive.ValueType) return "&vmvaluetype_type";
            if (t == Primitive.Void || t.Type == PrimitiveType.Void) return "&vmvoid_type"; // MethodInfo.ReturnType == typeof(void) (.NET)
            if (t.IsEnum) return typeIndex.ContainsKey(t) ? $"&{CName(t.Name)}_type" : "&vmint32_type";
            var prim = PrimTypeSym(t);
            if (prim != null) return "&" + prim;
            if (t.Type != PrimitiveType.Model || !IsEmittableModel(t)) return "0";
            if (t.IsStruct) return reflectedValueStructs.ContainsKey(CName(t.Name)) ? $"&{CName(t.Name)}_type" : "0";
            return $"&{CName(t.Name)}_type";
        }

        // ---- Tek meta: metot kayitlari (docs/modules.md) ----
        // Her kayit = trace MethodInfo (name/file) + reflection (System.Reflection.MethodInfo handle) + modul baglama (hash/fn/sekil/slot).
        // Tip basina tablo `{cn}_methods[]` (Type.methods); sahipsiz/runtime-kok sahipli kayitlar digitoyengine_methods_misc[].
        // Dosya icerigine global sira SIZMAZ: kayit = (tablo sembolu, tablo ici indeks).
        static readonly Dictionary<Code, int> methodIndex = new Dictionary<Code, int>();        // tablo ici indeks
        static readonly Dictionary<Code, string> methodTable = new Dictionary<Code, string>();  // tablo sembolu
        static readonly List<Code> methodList = new List<Code>();
        static readonly Dictionary<Primitive, (string sym, int count)> typeMethods = new Dictionary<Primitive, (string, int)>();
        const string MiscMethodsSym = "digitoyengine_methods_misc";
        static string MethodRef(Code c) => $"&{methodTable[c]}[{methodIndex[c]}]";
        static string MethodSym(Code c) => CName(c.EncodeName()); // kayit basina yardimci sembol eki (mp_/mattrs_)
        static readonly Dictionary<string, int> shapeIds = new Dictionary<string, int>(); // sekil metni -> digitoyengine_shapes[] sirasi (yalniz gen_meta tablosu; kayitlar sembolle baglanir)
        static string ThunkSym(string shapeKey) => $"de_thunk_{Fnv64(shapeKey):x16}";              // sekil hash'li thunk sembolu (build sirasindan bagimsiz)
        static readonly List<Code> shapeSamples = new List<Code>();                       // indeks -> ornek Code (thunk govdesi)
        static HashSet<Code> hostReferenced = new HashSet<Code>();

        // iface metot bildirimi: govdesiz ama slot kimligi + sekil icin kayit alir
        static bool IsIfaceDecl(Code c) =>
            c.Owner != null && c.Owner.IsInterface && IsEmittableModel(c.Owner) && c.GenericParameters.Count == 0 && !c.Unresolved;
        // P/Invoke marshal sarmalayicisi olan REFERANSSIZ extern'ler kayit almaz: sarmalayici static'tir, adresi alinirsa
        // native sembolu guclu ister (link kirilir). Diger extern'ler zayif prototiple kayitlanir (fn 0 olabilir).
        static bool HasMethodRecord(Code c) =>
            (IsEmittableCode(c) && !(c.IsExternal && NeedsPInvokeMarshal(c) && !hostReferenced.Contains(c))) || IsIfaceDecl(c);
        // Tip descriptor'i uretilen kod icinde mi (Type.methods baglanabilir)? Runtime kokleri (Object/String) -> sahipsiz grup.
        static bool OwnsMethodTable(Primitive p) =>
            p != null && typeIndex.ContainsKey(p) && (IsEmittableModel(p) || p.IsEnum);

        // CollectStrings sonunda: kayit kumesi, siralama, sekiller, ad havuzu (EmitStringPool'dan ONCE).
        static void CollectMethods(Context ctx)
        {
            methodIndex.Clear(); methodTable.Clear(); methodList.Clear(); typeMethods.Clear(); shapeIds.Clear(); shapeSamples.Clear();
            hostReferenced = ComputeReferenced(ctx);
            var byOwner = new Dictionary<Primitive, List<Code>>();
            var orphan = new List<Code>();
            var hashes = new Dictionary<ulong, string>();
            foreach (var c in ctx.AllCodes)
            {
                if (!HasMethodRecord(c) || methodIndex.ContainsKey(c)) continue;
                methodIndex[c] = -1; // ayni Code iki kez listelenmesin
                var name = c.EncodeName();
                var h = Fnv64(name);
                if (hashes.TryGetValue(h, out var other) && other != name)
                    throw new Exception($"meta: metot adi hash cakismasi: '{name}' vs '{other}'");
                hashes[h] = name;
                if (OwnsMethodTable(c.Owner))
                {
                    if (!byOwner.TryGetValue(c.Owner, out var list)) byOwner[c.Owner] = list = new List<Code>();
                    list.Add(c);
                }
                else orphan.Add(c);
                PoolAdd(CodeDisplay(c));
                if (!string.IsNullOrEmpty(c.SourceFile)) PoolAdd(c.SourceFile);
            }
            foreach (var p in ctx.AllPrimitives)
            {
                if (!byOwner.TryGetValue(p, out var list) || typeMethods.ContainsKey(p)) continue;
                var sym = $"{CName(p.Name)}_methods";
                typeMethods[p] = (sym, list.Count);
                for (int i = 0; i < list.Count; i++) { methodIndex[list[i]] = i; methodTable[list[i]] = sym; }
                methodList.AddRange(list);
            }
            for (int i = 0; i < orphan.Count; i++) { methodIndex[orphan[i]] = i; methodTable[orphan[i]] = MiscMethodsSym; }
            methodList.AddRange(orphan);
            foreach (var c in methodList)
            {
                var key = ShapeKey(c);
                if (!shapeIds.ContainsKey(key))
                {
                    var ts = ThunkSym(key);
                    foreach (var k2 in shapeIds.Keys) if (ThunkSym(k2) == ts) throw new Exception($"meta: sekil hash cakismasi: '{key}' vs '{k2}'");
                    shapeIds[key] = shapeSamples.Count; shapeSamples.Add(c);
                }
            }
        }

        static int MethodSlot(Code c) => c.Owner == null ? -1 : c.Owner.VTable.IndexOf(c);
        static bool IsCtorCode(Code c) => c.Name == "ctor" || c.Name.StartsWith("ctor_", StringComparison.Ordinal);
        // host -> modul sanal trampoline'i alan kayitlar: slot acan sanallar + iface bildirimleri (this'li)
        static bool HasVirtualTramp(Code c) => c.IsVirtual && !c.IsStatic && c.Arguments.Count > 0 && (IsEmittableCode(c) || IsIfaceDecl(c));
        static string VTrampSym(Code c) => $"de_tramp_v_{CName(c.EncodeName())}";
        static string DTrampSym(Primitive p) => $"de_tramp_d_{CName(p.Name)}";

        // Type initializer kuyrugu (designated): hash + metot araligi + bayraklar + delegate trampoline'i.
        static string TypeMetaTail(Primitive p)
        {
            var sb = new StringBuilder();
            sb.Append($", .hash = {Fnv64(p.Name)}ull");
            if (typeMethods.TryGetValue(p, out var mt))
                sb.Append($", .methods = {mt.sym}, .nmethods = {mt.count}");
            int flags = (p.IsStruct ? 1 : 0) | (p.IsInterface ? 2 : 0) | (p.IsDelegate ? 4 : 0) | (p.IsEnum ? 8 : 0);
            if (flags != 0) sb.Append($", .flags = {flags}");
            if (p.IsDelegate && IsEmittableModel(p) && p.DelegateReturn != null) sb.Append($", .delegate_tramp = (const void*)&{DTrampSym(p)}");
            sb.Append(AttrTail($"{CName(p.Name)}_tattrs", p.Attributes));
            if (p.CilAttributes != 0) sb.Append($", .cilattrs = {p.CilAttributes}u");
            if (p.InstantiatedFrom != null && reflectedGenericDefs.ContainsKey(CName(p.InstantiatedFrom.Name)))
                sb.Append($", .generic_def = &{TypeSym(p.InstantiatedFrom)}");
            return sb.ToString();
        }

        // DigitoyEngineMember initializer kuyrugu (designated): alan yerlesimi + hash. Property: yalniz tag.
        static string MemberMetaTail(Primitive owner, PrimitiveField f)
        {
            var cn = CName(owner.Name);
            var fn = CName(f.Name);
            var tag = ExportTag(f.Type);
            var attrs = AttrTail($"{cn}_fattrs_{fn}", f.Attributes) + $", .cilattrs = {f.CilAttributes}";
            if (f.IsStatic)
                return $", .tag = '{tag}', .size = (unsigned short)sizeof({StaticSym(f)}), .addr = (void*)&{StaticSym(f)}, .hash = {Fnv64(f.Name)}ull{attrs}";
            return $", .tag = '{tag}', .offset = (unsigned short)offsetof(struct {cn}, {fn}), .size = (unsigned short)sizeof(((struct {cn}*)0)->{fn}), .hash = {Fnv64(f.Name)}ull{attrs}";
        }

        // ---- custom attribute tablolari (docs/registry-removal.md Faz 4b) ----
        // Emit edilebilir attribute: tipi emit edilen class, ctor'u cozulmus ve arguman sayisi tutan.
        static List<PrimitiveAttribute> EmittableAttrs(List<PrimitiveAttribute> attrs)
        {
            var r = new List<PrimitiveAttribute>();
            if (attrs == null) return r;
            foreach (var a in attrs)
                if (a.Type != null && IsEmittableModel(a.Type) && !a.Type.IsStruct && a.Ctor != null && IsEmittableCode(a.Ctor)
                    && a.Ctor.Arguments.Count == a.FixedArgs.Count + 1 && typeIndex.ContainsKey(a.Type))
                    r.Add(a);
            return r;
        }
        static string AttrTail(string sym, List<PrimitiveAttribute> attrs)
        {
            int n = EmittableAttrs(attrs).Count;
            return n == 0 ? "" : $", .attrs = {sym}, .nattrs = {n}";
        }
        // Attribute argumani: sabit literal / typeof -> Type wrapper / null. Hedef parametre tipine cast.
        static string AttrArgExpr(object v, Primitive target)
        {
            if (v == null) return $"(({CType(target)})0)";
            if (v is Primitive tp)
            {
                EmitCtx.TryGetPrimitive("System.Type", out var st);
                return $"(({CType(st)})digitoyengine_type_wrapper({ExportTypeSym(tp)}))";
            }
            if (v is string s) return $"(({CType(Primitive.String)})&{strPool[s]})";
            return $"(({CType(target)}){Literal(v).Expr})";
        }
        // Lazy kurucu fonksiyonlar + DeAttr tablosu: `static DeAttr {sym}[] = { {&T_type, mk, 0}, ... }`
        static string EmitAttrTable(string sym, List<PrimitiveAttribute> all)
        {
            var attrs = EmittableAttrs(all);
            if (attrs.Count == 0) return "";
            var sb = new StringBuilder();
            var rows = new List<string>();
            for (int i = 0; i < attrs.Count; i++)
            {
                var a = attrs[i];
                var cn = CName(a.Type.Name);
                sb.Append($"static GCHeader* {sym}_mk{i}(void) {{\n    struct {cn}* o = New_{cn}();\n");
                var args = new List<string> { "o" };
                for (int k = 0; k < a.FixedArgs.Count; k++)
                    args.Add(AttrArgExpr(a.FixedArgs[k], a.Ctor.Arguments[k + 1].Type));
                sb.Append($"    {CSym(a.Ctor)}({string.Join(", ", args)});\n");
                foreach (var (name, value, isField) in a.NamedArgs)
                {
                    if (isField)
                    {
                        var f = Hierarchy.AllFields(a.Type).FirstOrDefault(x => x.Name == name);
                        if (f == null) continue;
                        sb.Append($"    o->{CName(f.Name)} = {AttrArgExpr(value, f.Type)};\n");
                    }
                    else
                    {
                        var prop = a.Type.Properties.FirstOrDefault(x => x.Name == name);
                        var setter = prop == null ? null : ReflectionAccessor(a.Type, "set_" + name + "_" + prop.Type.Name.Replace('.', '_'));
                        if (setter == null) continue;
                        sb.Append($"    {CSym(setter)}(o, {AttrArgExpr(value, prop.Type)});\n");
                    }
                }
                sb.Append("    return (GCHeader*)o;\n}\n");
                rows.Add($"{{ &{TypeSym(a.Type)}, {sym}_mk{i}, 0 }}");
            }
            sb.Append($"static DeAttr {sym}[] = {{ {string.Join(", ", rows)} }};\n");
            return sb.ToString();
        }
        static void PoolAttrStrings(List<PrimitiveAttribute> attrs)
        {
            foreach (var a in attrs)
            {
                foreach (var v in a.FixedArgs) if (v is string s) PoolAdd(s);
                foreach (var (_, v, _) in a.NamedArgs) if (v is string s) PoolAdd(s);
            }
        }

        // Trampoline govdeleri birimlere: sanal -> metodun sahibinin .c on-bolumu (metot tablosu adresini alir),
        // delegate -> delegate tipinin .c on-bolumu (descriptor .delegate_tramp). Ikisi de dosyaya ozel (static).
        static void EmitTrampolinesInto(Context ctx, Func<Code, StringBuilder> preOfCode, Func<Primitive, StringBuilder> preOfType)
        {
            foreach (var c in methodList)
            {
                if (!HasVirtualTramp(c)) continue;
                var an = CArgNames(c, new HashSet<string>());
                var pars = string.Join(", ", c.Arguments.Select((a, i) => $"{ParamCType(a)} {an[i]}"));
                preOfCode(c).Append($"static {CType(c.ReturnType)} {VTrampSym(c)}({pars}) {{{TrampolineBody(c, an, $"vmint_enter_virtual((GCHeader*){an[0]}, {Fnv64(c.EncodeName())}ull, a, &r);")} }}\n");
            }
            foreach (var p in ctx.AllPrimitives)
            {
                if (!IsEmittableModel(p) || !p.IsDelegate || p.DelegateReturn == null) continue;
                // imza: (closure, params...) - CallIndirect'in instance yolu (fn(target, args))
                var sig = new Code { ReturnType = p.DelegateReturn };
                sig.Arguments.Add(new Argument { Name = "closure", Type = Primitive.Object });
                for (int i = 0; i < p.DelegateParams.Count; i++) sig.Arguments.Add(new Argument { Name = "p" + i, Type = p.DelegateParams[i] });
                var an = sig.Arguments.Select(a => a.Name).ToList();
                var pars = string.Join(", ", sig.Arguments.Select((a, i) => $"{(i == 0 ? "void*" : ParamCType(a))} {an[i]}"));
                preOfType(p).Append($"static {CType(p.DelegateReturn)} {DTrampSym(p)}({pars}) {{{TrampolineBody(sig, an, "vmint_enter_delegate((GCHeader*)closure, a + 1, &r);")} }}\n");
            }
        }

        // Metot tablolari: tip basina `X_methods[]` sahibinin .c'sine (mp_/mattrs_ yardimcilariyla), sahipsizler misc'e.
        static void EmitMethodTablesInto(Func<Code, StringBuilder> bodyOf, StringBuilder misc)
        {
            var tables = new Dictionary<string, (StringBuilder sb, List<string> rows)>();
            foreach (var c in methodList)
            {
                var ms = MethodSym(c);
                var tsym = methodTable[c];
                var sb = tsym == MiscMethodsSym ? misc : bodyOf(c);
                int skip = c.IsStatic ? 0 : 1;
                var pars = c.Arguments.Skip(skip).ToList();
                string ptypes = "0", ptags = "0";
                if (pars.Count > 0)
                {
                    sb.Append($"static const Type* const mp_{ms}[] = {{ {string.Join(", ", pars.Select(a => ExportTypeSym(a.Type)))} }};\n");
                    ptypes = $"mp_{ms}";
                    ptags = $"(const unsigned char*){CStringLiteral(new string(pars.Select(a => (a.IsRef || a.IsOut) ? 'r' : ExportTag(a.Type)).ToArray()))}";
                }
                bool hasBody = !IsIfaceDecl(c);
                string fn = hasBody ? $"(const void*)&{CSym(c)}" : "0";
                string tramp = HasVirtualTramp(c) ? $"(const void*)&{VTrampSym(c)}" : "0";
                string decl = c.Owner == null ? "0" : ExportTypeSym(c.Owner);
                string fileRef = string.IsNullOrEmpty(c.SourceFile) ? "0" : $"&{strPool[c.SourceFile]}";
                int flags = (c.IsStatic ? 1 : 0) | (c.IsVirtual || c.IsOverride ? 2 : 0) | (IsCtorCode(c) ? 4 : 0) | (hasBody ? 0 : 8);
                sb.Append(EmitAttrTable($"mattrs_{ms}", c.Attributes));
                if (!tables.TryGetValue(tsym, out var t)) tables[tsym] = t = (sb, new List<string>());
                t.rows.Add($"    {{ &{strPool[CodeDisplay(c)]}, {fileRef}, -1, {Fnv64(c.EncodeName())}ull, {fn}, {tramp}, {decl}, {ExportTypeSym(c.ReturnType)}, {ptypes}, {ptags}, 0, " +
                           $"{ThunkSym(ShapeKey(c))}, {MethodSlot(c)}, {pars.Count}, '{ExportTag(c.ReturnType)}', {flags}{AttrTail($"mattrs_{ms}", c.Attributes)}, .cilattrs = {c.CilAttributes} }},");
            }
            foreach (var kv in tables)
            {
                if (kv.Key == MiscMethodsSym) continue;
                kv.Value.sb.Append($"MethodInfo {kv.Key}[{kv.Value.rows.Count}] = {{\n");
                foreach (var r in kv.Value.rows) kv.Value.sb.Append(r).Append('\n');
                kv.Value.sb.Append("};\n");
            }
            tables.TryGetValue(MiscMethodsSym, out var mt);
            misc.Append($"MethodInfo {MiscMethodsSym}[] = {{\n");
            if (mt.rows != null) foreach (var r in mt.rows) misc.Append(r).Append('\n');
            if (mt.rows == null || mt.rows.Count == 0) misc.Append("    {0}\n");
            misc.Append($"}};\nconst int digitoyengine_nmethods_misc = {mt.rows?.Count ?? 0};\n");
        }

        // Paylasimli meta (gen_meta.c): sekil thunk'lari + thunk/sekil tablolari + tip tablosu. Her build yeniden yazilir.
        static string EmitMetaTables()
        {
            var sb = new StringBuilder();
            sb.Append("// ---- tek meta: sekil thunk'lari / tip tablosu (docs/modules.md) ----\n");
            foreach (var smp in shapeSamples) sb.Append(EmitShapeThunk(smp));
            sb.Append("const DeThunk digitoyengine_thunks[] = { ");
            sb.Append(shapeSamples.Count == 0 ? "0" : string.Join(", ", shapeSamples.Select(smp => ThunkSym(ShapeKey(smp)))));
            sb.Append(" };\n");
            sb.Append("const char *const digitoyengine_shapes[] = { ");
            sb.Append(shapeSamples.Count == 0 ? "0" : string.Join(", ", shapeIds.OrderBy(kv => kv.Value).Select(kv => CStringLiteral(kv.Key))));
            sb.Append(" };\n");
            sb.Append($"const int digitoyengine_nthunks = {shapeSamples.Count};\n");
            // tip tablosu: runtime kokleri + descriptor'u olan tum uretilen tipler (typeIndex sirasi)
            var types = new List<string> { "&vmobject_type", "&vmstring_type", "&vmvaluetype_type", "&vmenum_type", "&vmdelegate_type", "&vmmulticastdelegate_type",
                "&vmint32_type", "&vmuint32_type", "&vmint64_type", "&vmuint64_type", "&vmint16_type", "&vmuint16_type", "&vmsbyte_type", "&vmbyte_type", "&vmchar_type", "&vmbool_type", "&vmsingle_type", "&vmdouble_type", "&vmvoid_type" };
            foreach (var kv in typeIndex.OrderBy(kv => kv.Value))
                types.Add($"&{TypeSym(kv.Key)}");
            sb.Append($"const Type *const digitoyengine_types[] = {{ {string.Join(", ", types)} }};\n");
            sb.Append($"const int digitoyengine_ntypes = {types.Count};\n");
            return sb.ToString();
        }
        // ---- host -> modul trampoline'leri (vmint.c girisleri) ----
        // Sanal: host'un slot ACAN her virtual metodu (IsVirtual; iface metotlari dahil) icin gercek C imzali bir fonksiyon:
        // argumanlari DeSlot'lara paketler, vmint_enter_virtual(this, kokHash, a, &r) cagirir. Modul yukleyici, host sanalini
        // override eden yerel metot icin vtable/itable slotuna bu fonksiyonu koyar; interpreter kokHash -> yerel metodu bulur.
        // Delegate: host'taki her delegate tipi icin (closure, params) imzali fonksiyon -> vmint_enter_delegate(closure, a, &r).
        static string PackArg(Argument a, string name, int i)
        {
            if (a.IsRef || a.IsOut) return $"a[{i}].p = (void*){name};";
            switch (ExportTag(a.Type))
            {
                case 'i': case 'h': case 'b': case 'z': case 'c': case 'B': return $"a[{i}].i = (cil_int){name};";
                case 'u': case 'H': return $"a[{i}].u = (cil_uint){name};";
                case 'l': return $"a[{i}].l = {name};";
                case 'q': return $"a[{i}].q = {name};";
                case 'f': return $"a[{i}].f = {name};";
                case 'd': return $"a[{i}].d = {name};";
                case 'v': return $"a[{i}].p = (void*)&{name};";
                default: return $"a[{i}].p = (void*){name};";
            }
        }
        static string UnpackRet(Primitive t)
        {
            switch (ExportTag(t))
            {
                case 'V': return "";
                case 'i': case 'h': case 'b': case 'z': case 'c': case 'B': return $"return ({ScalarCType(t)})r.i;";
                case 'u': case 'H': return $"return ({ScalarCType(t)})r.u;";
                case 'l': return "return r.l;";
                case 'q': return "return r.q;";
                case 'f': return "return r.f;";
                case 'd': return "return r.d;";
                case 'v': return "return __rv;";
                default: return $"return ({CType(t)})r.p;";
            }
        }
        static string TrampolineBody(Code sigCode, IEnumerable<string> argNames, string enterCall)
        {
            var sb = new StringBuilder();
            var names = argNames.ToList();
            sb.Append($" DeSlot a[{Math.Max(1, sigCode.Arguments.Count)}]; DeSlot r; (void)a; r.p = 0;");
            if (ExportTag(sigCode.ReturnType) == 'v') sb.Append($" {CType(sigCode.ReturnType)} __rv; memset(&__rv, 0, sizeof __rv); r.p = &__rv;");
            for (int i = 0; i < sigCode.Arguments.Count; i++) sb.Append(' ').Append(PackArg(sigCode.Arguments[i], names[i], i));
            sb.Append(' ').Append(enterCall).Append(' ').Append(UnpackRet(sigCode.ReturnType));
            return sb.ToString();
        }

        // sekil = C imza metni (donus | parametreler); ayni metin = ayni thunk. Pointer tipleri (struct X*, VmString*,
        // ref/out) ABI'de ayirt edilmez -> hepsi void* (sekil sayisi binlerden yuzlere iner). Struct by-value adiyla kalir.
        static string ShapeCType(string ctype) => ctype.EndsWith("*") ? "void*" : ctype;

        // host kodunun (govdeli metotlar + vtable/itable'lar) adresledigi Code kumesi
        static HashSet<Code> ComputeReferenced(Context ctx)
        {
            var referenced = new HashSet<Code>();
            foreach (var c in ctx.AllCodes)
                if (IsEmittableCode(c) && !c.IsExternal && c.NativeBody == null)
                    foreach (var op in c.Operations)
                        if (op.Code != null) referenced.Add(op.Code);
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                {
                    foreach (var m in p.VTable) referenced.Add(m);
                    foreach (var kv in p.ITables) foreach (var m in kv.Value) referenced.Add(m);
                }
            return referenced;
        }
        static string ShapeKey(Code c) =>
            ShapeCType(CType(c.ReturnType)) + "(" + (c.Arguments.Count == 0 ? "void" : string.Join(",", c.Arguments.Select(a => ShapeCType(ParamCType(a))))) + ")";

        // yuva -> C arguman ifadesi (tag sozlesmesi: kucuk tam sayilar .i/.u'da tasinir, struct blob adresi .p'de)
        static string SlotLoad(Argument a, int i)
        {
            if (a.IsRef || a.IsOut) return $"a[{i}].p";
            var t = a.Type;
            switch (ExportTag(t))
            {
                case 'i': case 'h': case 'b': case 'z': case 'c': case 'B': return $"({ScalarCType(t)})a[{i}].i";
                case 'u': case 'H': return $"({ScalarCType(t)})a[{i}].u";
                case 'l': return $"a[{i}].l";
                case 'q': return $"a[{i}].q";
                case 'f': return $"a[{i}].f";
                case 'd': return $"a[{i}].d";
                case 'v': return $"*({CType(t)}*)a[{i}].p";
                default: return $"a[{i}].p"; // o, p: void*
            }
        }

        static string SlotStore(Primitive t, string call)
        {
            switch (ExportTag(t))
            {
                case 'V': return call + ";";
                case 'i': case 'h': case 'b': case 'z': case 'c': case 'B': return $"r->i = (cil_int){call};";
                case 'u': case 'H': return $"r->u = (cil_uint){call};";
                case 'l': return $"r->l = {call};";
                case 'q': return $"r->q = {call};";
                case 'f': return $"r->f = {call};";
                case 'd': return $"r->d = {call};";
                case 'v': return $"*({CType(t)}*)r->p = {call};";
                default: return $"r->p = {call};";
            }
        }

        static string EmitShapeThunk(Code c)
        {
            var pars = c.Arguments.Count == 0 ? "void" : string.Join(", ", c.Arguments.Select(a => ShapeCType(ParamCType(a))));
            var args = string.Join(", ", c.Arguments.Select(SlotLoad));
            var call = $"(({ShapeCType(CType(c.ReturnType))}(*)({pars}))fn)({args})";
            return $"void {ThunkSym(ShapeKey(c))}(const void *fn, DeSlot *a, DeSlot *r) {{ (void)a; (void)r; {SlotStore(c.ReturnType, call)} }}\n";
        }
    }
}
