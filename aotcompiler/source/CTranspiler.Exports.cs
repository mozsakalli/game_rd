using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Language
{
    // Dinamik modul destegi (docs/modules.md, Faz A): host'un "parent classloader" export tablolari.
    // Uretilen C, programdaki TUM tipleri (alan offset'leri, vtable/iface slot adlari), metotlari (fn ptr +
    // imza-sekli thunk'i) ve statik alanlari (adres) isim-hash'iyle disa verir; vmint.c modul yukleyicisi
    // dis referanslari load zamaninda bunlara baglar. Bayrak kapaliyken bos tablolar uretilir (vmrt.c
    // extern'leri her build'de linklenir, selftest/engine ciktisi buyumez).
    public static partial class CTranspiler
    {
        public static bool EmitModuleExports;

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

        // alan/arguman etiketi (vmrt.h DeFieldExport.tag sozlesmesi)
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
            if (t.IsEnum) return typeIndex.ContainsKey(t) ? $"&{CName(t.Name)}_type" : "&vmint32_type";
            var prim = PrimTypeSym(t);
            if (prim != null) return "&" + prim;
            if (t.Type != PrimitiveType.Model || !IsEmittableModel(t)) return "0";
            if (t.IsStruct) return reflectedValueStructs.ContainsKey(CName(t.Name)) ? $"&{CName(t.Name)}_type" : "0";
            return $"&{CName(t.Name)}_type";
        }

        static string EmitExports(Context ctx)
        {
            if (!EmitModuleExports)
                return "// modul export tablolari kapali (CTranspiler.EmitModuleExports=false)\n" +
                       "const DeTypeExport de_host_types[1] = { {0} }; const int de_host_ntypes = 0;\n" +
                       "const DeMethodExport de_host_methods[1] = { {0} }; const int de_host_nmethods = 0;\n" +
                       "const DeFieldExport de_host_statics[1] = { {0} }; const int de_host_nstatics = 0;\n" +
                       "const DeThunk de_host_thunks[1] = { 0 }; const char *const de_host_shapes[1] = { 0 }; const int de_host_nthunks = 0;\n" +
                       "const DeMethodExport de_host_vtramps[1] = { {0} }; const int de_host_nvtramps = 0;\n" +
                       "const DeMethodExport de_host_dtramps[1] = { {0} }; const int de_host_ndtramps = 0;\n";

            var sb = new StringBuilder();
            sb.Append("// ---- modul host export tablolari (docs/modules.md Faz A) ----\n");

            // --- tipler ---
            var types = new List<Primitive> { Primitive.Object, Primitive.String, Primitive.ValueType };
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p) || (p.IsEnum && typeIndex.ContainsKey(p)))
                    types.Add(p);
            var typeHashes = new Dictionary<ulong, string>();
            var typeRows = new List<string>();
            var seenTypeNames = new HashSet<string>();
            foreach (var p in types)
            {
                if (!seenTypeNames.Add(p.Name)) continue;
                var h = Fnv64(p.Name);
                if (typeHashes.TryGetValue(h, out var other))
                    throw new Exception($"modul export: tip adi hash cakismasi: '{p.Name}' vs '{other}'");
                typeHashes[h] = p.Name;
                var cn = CName(p.Name);
                bool runtime = p == Primitive.Object || p == Primitive.String || p == Primitive.ValueType;
                string structName = p == Primitive.Object ? "VmObject" : p == Primitive.String ? "VmString" : cn;
                bool hasStruct = !p.IsInterface && !p.IsDelegate && !p.IsEnum && p != Primitive.ValueType; // struct tanimi var -> offsetof/sizeof kullanilabilir

                // alanlar: yalnizca bu tipin BILDIRDIGI alanlar (base'inkiler base export'unda; loader zinciri yurur)
                string fieldsSym = "0";
                int nfields = 0;
                if (hasStruct && !runtime && p.Fields.Count > 0)
                {
                    fieldsSym = $"de_f_{cn}";
                    sb.Append($"static const DeFieldExport {fieldsSym}[] = {{\n");
                    foreach (var f in p.Fields)
                    {
                        var fn = CName(f.Name);
                        sb.Append($"    {{ {Fnv64(p.Name + "$" + f.Name)}ull, {CStringLiteral(p.Name + "$" + f.Name)}, {ExportTypeSym(f.Type)}, 0, " +
                                  $"(unsigned short)offsetof(struct {structName}, {fn}), (unsigned short)sizeof(((struct {structName}*)0)->{fn}), '{ExportTag(f.Type)}', 0 }},\n");
                        nfields++;
                    }
                    sb.Append("};\n");
                }

                // sanal slotlar: class -> vtable (slot'taki impl'in adi), interface -> iface slot sirasi
                string slotsSym = "0";
                int nslots = 0;
                if (!p.IsStruct && !p.IsDelegate && !p.IsEnum && p.VTable.Count > 0)
                {
                    slotsSym = $"de_v_{cn}";
                    sb.Append($"static const DeSlotExport {slotsSym}[] = {{\n");
                    for (int i = 0; i < p.VTable.Count; i++)
                    {
                        var m = p.VTable[i];
                        sb.Append($"    {{ {Fnv64(m.EncodeName())}ull, {CStringLiteral(m.EncodeName())}, {i} }},\n");
                        nslots++;
                    }
                    sb.Append("};\n");
                }

                string size = hasStruct ? $"(unsigned short)sizeof(struct {structName})" : "0";
                typeRows.Add($"    {{ {h}ull, {CStringLiteral(p.Name)}, {ExportTypeSym(p)}, {fieldsSym}, {slotsSym}, {nfields}, {nslots}, {size}, " +
                             $"{(p.IsStruct ? 1 : 0)}, {(p.IsInterface ? 1 : 0)}, {(p.IsDelegate ? 1 : 0)}, {(p.IsEnum ? 1 : 0)} }},");
            }
            sb.Append("const DeTypeExport de_host_types[] = {\n");
            foreach (var row in typeRows) sb.Append(row).Append('\n');
            sb.Append($"}};\nconst int de_host_ntypes = {typeRows.Count};\n");

            // --- statik alanlar ---
            var staticRows = new List<string>();
            var staticHashes = new HashSet<ulong>();
            foreach (var p in ctx.AllPrimitives)
                if (IsEmittableModel(p))
                    foreach (var f in p.StaticFields)
                    {
                        var key = p.Name + "$" + f.Name;
                        var h = Fnv64(key);
                        if (!staticHashes.Add(h)) throw new Exception($"modul export: statik alan hash cakismasi: {key}");
                        staticRows.Add($"    {{ {h}ull, {CStringLiteral(key)}, {ExportTypeSym(f.Type)}, (void*)&{StaticSym(f)}, 0, (unsigned short)sizeof({StaticSym(f)}), '{ExportTag(f.Type)}', 1 }},");
                    }
            if (staticRows.Count == 0)
                sb.Append("const DeFieldExport de_host_statics[1] = { {0} }; const int de_host_nstatics = 0;\n");
            else
            {
                sb.Append("const DeFieldExport de_host_statics[] = {\n");
                foreach (var row in staticRows) sb.Append(row).Append('\n');
                sb.Append($"}};\nconst int de_host_nstatics = {staticRows.Count};\n");
            }

            // --- imza-sekli thunk'lari + metotlar ---
            // Extern (govdesi baska yerde) metotlar: host kodu referans ediyorsa guclu sembol; etmiyorsa prototipi ZAYIF
            // (TranspileProgram) -> tanimi yoksa adres 0, modul load'da "uygulanmamis extern" hatasi (link kirilmaz).
            // P/Invoke marshal sarmalayicisi olan referanssiz extern'ler export edilmez (sarmalayici native sembolu guclu ister).
            var referenced = ComputeReferenced(ctx);
            var shapes = new Dictionary<string, int>(); // sekil metni -> indeks
            var shapeBodies = new List<string>();
            var methodRows = new List<string>();
            var methodHashes = new Dictionary<ulong, string>();
            foreach (var c in ctx.AllCodes)
            {
                if (!IsEmittableCode(c)) continue;
                if (c.IsExternal && NeedsPInvokeMarshal(c) && !referenced.Contains(c)) continue;
                var name = c.EncodeName();
                var h = Fnv64(name);
                if (methodHashes.TryGetValue(h, out var other))
                {
                    if (other == name) continue; // ayni Code iki kez listelenmis
                    throw new Exception($"modul export: metot adi hash cakismasi: '{name}' vs '{other}'");
                }
                methodHashes[h] = name;
                var shapeKey = ShapeKey(c);
                if (!shapes.TryGetValue(shapeKey, out var shapeId))
                {
                    shapeId = shapes.Count;
                    shapes[shapeKey] = shapeId;
                    shapeBodies.Add(EmitShapeThunk(c, shapeId));
                }
                methodRows.Add($"    {{ {h}ull, {CStringLiteral(name)}, (const void*)&{CSym(c)}, {shapeId}, {(c.IsStatic ? 1 : 0)}, {(c.IsVirtual || c.IsOverride ? 1 : 0)} }},");
            }
            foreach (var body in shapeBodies) sb.Append(body);
            sb.Append("const DeThunk de_host_thunks[] = { ");
            sb.Append(string.Join(", ", Enumerable.Range(0, shapes.Count).Select(i => $"de_thunk_{i}")));
            sb.Append(" };\n");
            sb.Append("const char *const de_host_shapes[] = { ");
            sb.Append(string.Join(", ", shapes.OrderBy(kv => kv.Value).Select(kv => CStringLiteral(kv.Key))));
            sb.Append(" };\n");
            sb.Append($"const int de_host_nthunks = {shapes.Count};\n");
            sb.Append("const DeMethodExport de_host_methods[] = {\n");
            foreach (var row in methodRows) sb.Append(row).Append('\n');
            sb.Append($"}};\nconst int de_host_nmethods = {methodRows.Count};\n");
            sb.Append(EmitTrampolines(ctx));
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

        static string EmitTrampolines(Context ctx)
        {
            var sb = new StringBuilder();
            sb.Append("// ---- host -> modul trampoline'leri ----\n");
            sb.Append("void vmint_enter_virtual(GCHeader *self, unsigned long long rootHash, DeSlot *a, DeSlot *r);\n");
            sb.Append("void vmint_enter_delegate(GCHeader *closure, DeSlot *a, DeSlot *r);\n");
            var vrows = new List<string>();
            var seen = new HashSet<ulong>();
            foreach (var c in ctx.AllCodes)
            {
                // host'un slot acan sanallari + iface metotlari (govdesiz; IsEmittableCode disi ama slot kimligi)
                bool ifaceDecl = c.Owner != null && c.Owner.IsInterface && IsEmittableModel(c.Owner) && c.GenericParameters.Count == 0 && !c.Unresolved;
                if (!(IsEmittableCode(c) || ifaceDecl) || !c.IsVirtual || c.IsStatic || c.Arguments.Count == 0) continue;
                var h = Fnv64(c.EncodeName());
                if (!seen.Add(h)) continue;
                var an = CArgNames(c, new HashSet<string>());
                var pars = string.Join(", ", c.Arguments.Select((a, i) => $"{ParamCType(a)} {an[i]}"));
                var fn = $"de_tramp_v_{CName(c.EncodeName())}";
                sb.Append($"static {CType(c.ReturnType)} {fn}({pars}) {{{TrampolineBody(c, an, $"vmint_enter_virtual((GCHeader*){an[0]}, {h}ull, a, &r);")} }}\n");
                vrows.Add($"    {{ {h}ull, {CStringLiteral(c.EncodeName())}, (const void*)&{fn}, 0, 0, 1 }},");
            }
            sb.Append("const DeMethodExport de_host_vtramps[] = {\n");
            foreach (var r in vrows) sb.Append(r).Append('\n');
            if (vrows.Count == 0) sb.Append("    {0}\n");
            sb.Append($"}};\nconst int de_host_nvtramps = {vrows.Count};\n");

            var drows = new List<string>();
            foreach (var p in ctx.AllPrimitives)
            {
                if (!IsEmittableModel(p) || !p.IsDelegate || p.DelegateReturn == null) continue;
                // imza: (closure, params...) - CallIndirect'in instance yolu (fn(target, args))
                var sig = new Code { ReturnType = p.DelegateReturn };
                sig.Arguments.Add(new Argument { Name = "closure", Type = Primitive.Object });
                for (int i = 0; i < p.DelegateParams.Count; i++) sig.Arguments.Add(new Argument { Name = "p" + i, Type = p.DelegateParams[i] });
                var an = sig.Arguments.Select(a => a.Name).ToList();
                var pars = string.Join(", ", sig.Arguments.Select((a, i) => $"{(i == 0 ? "void*" : ParamCType(a))} {an[i]}"));
                var fn = $"de_tramp_d_{CName(p.Name)}";
                sb.Append($"static {CType(p.DelegateReturn)} {fn}({pars}) {{{TrampolineBody(sig, an, "vmint_enter_delegate((GCHeader*)closure, a + 1, &r);")} }}\n");
                drows.Add($"    {{ {Fnv64(p.Name)}ull, {CStringLiteral(p.Name)}, (const void*)&{fn}, 0, 0, 0 }},");
            }
            sb.Append("const DeMethodExport de_host_dtramps[] = {\n");
            foreach (var r in drows) sb.Append(r).Append('\n');
            if (drows.Count == 0) sb.Append("    {0}\n");
            sb.Append($"}};\nconst int de_host_ndtramps = {drows.Count};\n");
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

        static string EmitShapeThunk(Code c, int id)
        {
            var pars = c.Arguments.Count == 0 ? "void" : string.Join(", ", c.Arguments.Select(a => ShapeCType(ParamCType(a))));
            var args = string.Join(", ", c.Arguments.Select(SlotLoad));
            var call = $"(({ShapeCType(CType(c.ReturnType))}(*)({pars}))fn)({args})";
            return $"static void de_thunk_{id}(const void *fn, DeSlot *a, DeSlot *r) {{ (void)a; (void)r; {SlotStore(c.ReturnType, call)} }}\n";
        }
    }
}
