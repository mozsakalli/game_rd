using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DigitoyEngine.Language
{
    // Dinamik modul yazici (docs/modules.md Faz B): cozulmus/monomorfize IR'in modul kismini `.dmod`'a serilestirir.
    // Kapsam karari TANIMLAYAN ASSEMBLY'ye gore (sabit ad yok):
    //   bundled assembly'nin tipi/metodu            -> yerel (yorumlanir)
    //   provided sablonun generic somutlamasi        -> yerel, preferHost=1 (host'ta varsa load'da atilir)
    //   geri kalan                                   -> dis referans (isim; vmint load'da parent-first cozer)
    // Govdesi C'de olan (extern/NativeBody) kodlar yerel OLAMAZ -> hep dis referans (host'ta yoksa load hatasi).
    //
    // FORMAT (little-endian; tum indeksler 0-tabanli, -1 = yok):
    //   u32 magic 'DMOD' | u32 version | u32 nStr | u32 nType | u32 nField | u32 nMethod | u32 entryMethod(-1) | i32 infoStr
    //   STR[nStr]   : u32 byteLen, utf8 bytes, u8 0
    //   TYPE[nType] : u8 kind (bkz. TK_*), i32 nameStr, i32 displayStr
    //       TK_SCALAR  : u8 tag ('i','u','l','q','h','H','b','z','c','B','f','d','V')
    //       TK_ARRAY   : i32 elemType, u8 rank          TK_FIXED: i32 elemType, i32 size     TK_POINTER: i32 elemType
    //       TK_EXTERN  : u8 flags (1=struct 2=iface 4=delegate 8=enum); delegate ise i32 retType, i32 nParams, i32 param[]
    //       TK_ENUM    : (yerel enum) i32 nMembers, { i32 nameStr, i32 value }[]
    //       TK_CLASS/TK_STRUCT/TK_IFACE/TK_DELEGATE (yerel):
    //           u8 preferHost, i32 parentType, i32 nIfaces, i32 iface[], 
    //           i32 nFields, { i32 nameStr, i32 type, u8 isStatic }[]       (yalniz BILDIRILEN alanlar)
    //           i32 nVSlots, { i32 declMethod (slotu acan kok bildiren), i32 implMethod }[]   (tam vtable; kok adiyla host slotuna eslenir)
    //           i32 nITables, { i32 ifaceType, i32 n, { i32 ifaceMethod, i32 implMethod }[] }[]
    //           delegate: i32 retType, i32 nParams, i32 param[]
    //   FIELD[nField]  : i32 ownerType, i32 nameStr, u8 isStatic, i32 fieldType   (op.Field referanslari)
    //   METHOD[nMethod]: u8 kind (0=extern 1=local), i32 nameStr (EncodeName), i32 ownerType(-1), u8 flags (1=static 2=virtual/override 4=preferHost 8=ctor)
    //       i32 retType, i32 nArgs, { i32 type, u8 flags (1=ref 2=out) }[]
    //       local: i32 displayStr, i32 fileStr, i32 stubReasonStr(-1), i32 nLocals, i32 localType[], i32 nOps, OP[nOps]
    //   OP: u8 opcode (OpType), i32 slot, i32 line, i32 field, i32 code, i32 prim, u8 valTag + payload
    //       valTag: 0 yok | 1 i32 | 2 f32 | 3 f64 | 4 i64 | 5 str(i32) | 6 bool(u8) | 7 char(u16) | 8 i16 | 9 u8 | 10 u32 | 11 u64 | 12 u16 | 13 i8
    public static class ModuleWriter
    {
        public const uint Magic = 0x444F4D44; // "DMOD"
        public const uint Version = 2; // v2: baslikta infoStr (uretici + engine surumu; tani)

        public const byte TK_SCALAR = 0, TK_ARRAY = 1, TK_FIXED = 2, TK_POINTER = 3, TK_EXTERN = 4, TK_ENUM = 5,
                          TK_CLASS = 6, TK_STRUCT = 7, TK_IFACE = 8, TK_DELEGATE = 9;

        public sealed class Report
        {
            public List<string> LocalTypes = new List<string>();
            public List<string> EmbeddedTypes = new List<string>(); // preferHost generic somutlamalar
            public List<string> ExternTypes = new List<string>();
            public List<string> LocalMethods = new List<string>();
            public List<string> EmbeddedMethods = new List<string>();
            public List<string> ExternMethods = new List<string>();
            public List<(Code code, string via)> ReachableStubs = new List<(Code, string)>();
            public int Strings, Fields, Bytes;
            public override string ToString() =>
                $"tip: {LocalTypes.Count} yerel + {EmbeddedTypes.Count} gomulu + {ExternTypes.Count} dis; " +
                $"metot: {LocalMethods.Count} yerel + {EmbeddedMethods.Count} gomulu + {ExternMethods.Count} dis; alan ref {Fields}; string {Strings}; {Bytes} bayt";
        }

        sealed class W
        {
            public readonly Context ctx;
            public readonly HashSet<string> bundled;
            public readonly Report report = new Report();
            public readonly List<string> strs = new List<string>();
            readonly Dictionary<string, int> strIdx = new Dictionary<string, int>();
            public readonly List<Primitive> types = new List<Primitive>();
            readonly Dictionary<string, int> typeIdx = new Dictionary<string, int>(); // Name kimlik (array/pointer intern degil)
            public readonly List<Code> methods = new List<Code>();
            readonly Dictionary<Code, int> methodIdx = new Dictionary<Code, int>();
            public readonly List<PrimitiveField> fields = new List<PrimitiveField>();
            readonly Dictionary<PrimitiveField, int> fieldIdx = new Dictionary<PrimitiveField, int>();
            readonly Queue<object> work = new Queue<object>();
            readonly Dictionary<Code, string> via = new Dictionary<Code, string>();

            public W(Context ctx, HashSet<string> bundled) { this.ctx = ctx; this.bundled = bundled; }

            public int Str(string s)
            {
                if (s == null) return -1;
                if (!strIdx.TryGetValue(s, out var i)) { i = strs.Count; strs.Add(s); strIdx[s] = i; }
                return i;
            }

            bool InBundle(string asm) => asm != null && bundled.Contains(asm);

            // --- siniflandirma ---
            public bool IsLocalType(Primitive p, out bool preferHost)
            {
                preferHost = false;
                if (p == null || p == Primitive.Object || p == Primitive.String || p == Primitive.ValueType) return false;
                if (p.Type != PrimitiveType.Model && !p.IsEnum) return false; // skaler/array/pointer: yapisal
                if (p.IsGenericParameter || p.GenericTemplate != null || p.IsGeneric) return false; // sablon/acik: asla
                if (InBundle(p.Assembly)) return true;
                if (p.TypeArguments.Count > 0) { preferHost = true; return true; } // provided sablonun somutlamasi
                return false;
            }

            public bool IsLocalCode(Code c, out bool preferHost)
            {
                preferHost = false;
                if (c == null || c.Unresolved || c.IsExternal || c.NativeBody != null) return false; // govde C'de -> host'ta olmali
                if (c.GenericParameters.Count > 0 || (c.Owner != null && (c.Owner.IsGeneric || c.Owner.GenericTemplate != null))) return false;
                if (c.Owner != null && IsLocalType(c.Owner, out var ownerPrefer))
                {
                    preferHost = ownerPrefer;
                    return true;
                }
                if (InBundle(c.Assembly)) return true;
                if (c.Template != null) { preferHost = true; return true; } // provided generic metodun somutlamasi
                return false;
            }

            // --- erisilebilirlik ---
            public int Type(Primitive p)
            {
                if (p == null) return -1;
                if (typeIdx.TryGetValue(p.Name, out var i)) return i;
                i = types.Count; types.Add(p); typeIdx[p.Name] = i;
                work.Enqueue(p);
                return i;
            }
            public int Method(Code c, string from)
            {
                if (c == null) return -1;
                if (methodIdx.TryGetValue(c, out var i)) return i;
                i = methods.Count; methods.Add(c); methodIdx[c] = i;
                if (!via.ContainsKey(c)) via[c] = from;
                work.Enqueue(c);
                return i;
            }
            public int Field(PrimitiveField f)
            {
                if (f == null) return -1;
                if (fieldIdx.TryGetValue(f, out var i)) return i;
                i = fields.Count; fields.Add(f); fieldIdx[f] = i;
                Str(f.Name);
                Type(f.Owner);
                Type(f.Type);
                return i;
            }

            public void Drain()
            {
                while (work.Count > 0)
                {
                    var item = work.Dequeue();
                    if (item is Primitive p) VisitType(p);
                    else VisitCode((Code)item);
                }
            }

            void VisitType(Primitive p)
            {
                Str(p.Name); Str(p.Display);
                if (p.Type == PrimitiveType.Array || p.Type == PrimitiveType.FixedArray || p.Type == PrimitiveType.Pointer) { Type(p.ElementType); return; }
                if (!IsLocalType(p, out var prefer))
                {
                    if (p.Type == PrimitiveType.Model || p.IsEnum) report.ExternTypes.Add(p.Name);
                    if (p.IsDelegate) { Type(p.DelegateReturn); foreach (var dp in p.DelegateParams) Type(dp); }
                    return;
                }
                (prefer ? report.EmbeddedTypes : report.LocalTypes).Add(p.Name);
                if (p.IsEnum) { foreach (var m in p.EnumMembers.Keys) Str(m); return; }
                Type(p.Parent);
                foreach (var i in p.Interfaces) Type(i);
                foreach (var f in p.Fields) { Str(f.Name); Type(f.Type); }
                foreach (var f in p.StaticFields) { Str(f.Name); Type(f.Type); }
                if (p.IsDelegate) { Type(p.DelegateReturn); foreach (var dp in p.DelegateParams) Type(dp); }
                // yerel tipin TUM uyeleri (host sanal/iface uzerinden her birine girebilir; ctor/statikler modul kodundan)
                foreach (var c in ctx.AllCodes)
                    if (c.Owner == p && c.GenericParameters.Count == 0)
                        Method(c, "uye:" + p.Name);
                foreach (var m in p.VTable) Method(m, "vtable:" + p.Name);
                for (int i = 0; i < p.VTable.Count; i++) // slot kok bildirenleri (WriteType ile ayni yuruyus; parent host olsa da ref gerekir)
                    for (var t = p.Parent; t != null && !t.Unresolved && i < t.VTable.Count; t = t.Parent)
                        Method(t.VTable[i], "vtable-decl:" + p.Name);
                foreach (var kv in p.ITables) { Type(kv.Key); foreach (var m in kv.Value) Method(m, "itable:" + p.Name); foreach (var im in kv.Key.VTable) Method(im, "iface:" + kv.Key.Name); }
            }

            void VisitCode(Code c)
            {
                Str(c.EncodeName()); Type(c.Owner); Type(c.ReturnType);
                foreach (var a in c.Arguments) Type(a.Type);
                if (!IsLocalCode(c, out var prefer))
                {
                    report.ExternMethods.Add(c.EncodeName());
                    return;
                }
                (prefer ? report.EmbeddedMethods : report.LocalMethods).Add(c.EncodeName());
                if (c.UntranslatableReason != null)
                    report.ReachableStubs.Add((c, via.TryGetValue(c, out var v) ? v : "?"));
                Str(CTranspiler.CodeDisplay(c)); Str(c.SourceFile); Str(c.UntranslatableReason);
                foreach (var l in c.Locals) Type(l);
                foreach (var op in c.Operations)
                {
                    if (op.Type == OpType.Yield) throw new Exception($"Yield op'u lowering sonrasi var olamaz: {c.EncodeName()}");
                    if (op.Field != null) Field(op.Field);
                    if (op.Code != null) Method(op.Code, "op:" + c.EncodeName());
                    if (op.PrimitiveRef != null) Type(op.PrimitiveRef);
                    if (op.Value is string s) Str(s);
                }
            }
        }

        static byte ScalarTag(Primitive t)
        {
            switch (t.Type)
            {
                case PrimitiveType.Int: return (byte)'i';
                case PrimitiveType.UInt: return (byte)'u';
                case PrimitiveType.Long: return (byte)'l';
                case PrimitiveType.ULong: return (byte)'q';
                case PrimitiveType.Short: return (byte)'h';
                case PrimitiveType.UShort: return (byte)'H';
                case PrimitiveType.Byte: return (byte)'b';
                case PrimitiveType.SByte: return (byte)'z';
                case PrimitiveType.Char: return (byte)'c';
                case PrimitiveType.Bool: return (byte)'B';
                case PrimitiveType.Float: return (byte)'f';
                case PrimitiveType.Double: return (byte)'d';
                case PrimitiveType.Void: return (byte)'V';
                default: throw new Exception("skaler degil: " + t.Name);
            }
        }

        // roots: bundled assembly'lerin tum somut tipleri ve uyeleri. info: uretici/engine surumu metni (yukleme hatalarinda basilir).
        public static byte[] Write(Context ctx, IEnumerable<string> bundledAssemblies, Code entry, string info, out Report report)
        {
            var w = new W(ctx, new HashSet<string>(bundledAssemblies));
            w.Str(info ?? "");
            foreach (var p in ctx.AllPrimitives)
                if (w.IsLocalType(p, out var prefer) && !prefer)
                    w.Type(p);
            foreach (var c in ctx.AllCodes)
                if (w.IsLocalCode(c, out var prefer) && !prefer)
                    w.Method(c, "kok");
            if (entry != null) w.Method(entry, "entry");
            w.Drain();
            report = w.report;

            var ms = new MemoryStream();
            var b = new BinaryWriter(ms, Encoding.UTF8);
            // string havuzu ONCE kapanmali: tip/metot yazimi sirasinda yeni string eklenmemeli -> on-tarama Drain'de yapildi;
            // yine de yazimda Str() cagrilari ayni dizine duser (zaten var).
            int nStr = w.strs.Count, nType = w.types.Count, nField = w.fields.Count, nMethod = w.methods.Count;
            b.Write(Magic); b.Write(Version);
            b.Write(nStr); b.Write(nType); b.Write(nField); b.Write(nMethod);
            b.Write(entry != null ? w.Method(entry, "entry") : -1);
            b.Write(w.Str(info ?? ""));
            foreach (var s in w.strs)
            {
                var bytes = Encoding.UTF8.GetBytes(s);
                b.Write(bytes.Length); b.Write(bytes); b.Write((byte)0);
            }
            for (int i = 0; i < nType; i++) WriteType(w, b, w.types[i]);
            for (int i = 0; i < nField; i++) { var f = w.fields[i]; b.Write(w.Type(f.Owner)); b.Write(w.Str(f.Name)); b.Write((byte)(f.IsStatic ? 1 : 0)); b.Write(w.Type(f.Type)); }
            for (int i = 0; i < nMethod; i++) WriteMethod(w, b, w.methods[i]);
            if (w.strs.Count != nStr || w.types.Count != nType || w.fields.Count != nField || w.methods.Count != nMethod)
                throw new Exception($"ModuleWriter: yazim sirasinda tablo buyudu (on-tarama eksik): str {nStr}->{w.strs.Count} tip {nType}->{w.types.Count} alan {nField}->{w.fields.Count} metot {nMethod}->{w.methods.Count}");
            b.Flush();
            report.Strings = nStr; report.Fields = w.fields.Count; report.Bytes = (int)ms.Length;
            return ms.ToArray();
        }

        static void WriteType(W w, BinaryWriter b, Primitive p)
        {
            byte kind;
            bool prefer = false;
            if (p.Type == PrimitiveType.Array) kind = TK_ARRAY;
            else if (p.Type == PrimitiveType.FixedArray) kind = TK_FIXED;
            else if (p.Type == PrimitiveType.Pointer) kind = TK_POINTER;
            else if (!w.IsLocalType(p, out prefer)) kind = (p.Type == PrimitiveType.Model || p.IsEnum) ? TK_EXTERN : TK_SCALAR;
            else if (p.IsEnum) kind = TK_ENUM;
            else if (p.IsInterface) kind = TK_IFACE;
            else if (p.IsDelegate) kind = TK_DELEGATE;
            else if (p.IsStruct) kind = TK_STRUCT;
            else kind = TK_CLASS;
            b.Write(kind); b.Write(w.Str(p.Name)); b.Write(w.Str(p.Display));
            switch (kind)
            {
                case TK_SCALAR: b.Write(ScalarTag(p)); return;
                case TK_ARRAY: b.Write(w.Type(p.ElementType)); b.Write((byte)p.ArrayRank); return;
                case TK_FIXED: b.Write(w.Type(p.ElementType)); b.Write(p.FixedSize); return;
                case TK_POINTER: b.Write(w.Type(p.ElementType)); return;
                case TK_EXTERN:
                    b.Write((byte)((p.IsStruct ? 1 : 0) | (p.IsInterface ? 2 : 0) | (p.IsDelegate ? 4 : 0) | (p.IsEnum ? 8 : 0)));
                    if (p.IsDelegate) { b.Write(w.Type(p.DelegateReturn)); b.Write(p.DelegateParams.Count); foreach (var dp in p.DelegateParams) b.Write(w.Type(dp)); } // CallIndirect imzasi
                    return;
                case TK_ENUM:
                    b.Write(p.EnumMembers.Count);
                    foreach (var m in p.EnumMembers) { b.Write(w.Str(m.Key)); b.Write(m.Value); }
                    return;
            }
            b.Write((byte)(prefer ? 1 : 0));
            b.Write(w.Type(p.Parent));
            b.Write(p.Interfaces.Count);
            foreach (var i in p.Interfaces) b.Write(w.Type(i));
            var fields = p.Fields.Concat(p.StaticFields).ToList();
            b.Write(fields.Count);
            foreach (var f in fields) { b.Write(w.Str(f.Name)); b.Write(w.Type(f.Type)); b.Write((byte)(f.IsStatic ? 1 : 0)); }
            // vtable: her slot icin (kok bildiren, bu tipteki impl). Kok = slotu ilk acan virtual (parent zincirinde ayni isim).
            b.Write(p.VTable.Count);
            for (int i = 0; i < p.VTable.Count; i++)
            {
                var decl = p.VTable[i];
                for (var t = p.Parent; t != null && !t.Unresolved && i < t.VTable.Count; t = t.Parent)
                    decl = t.VTable[i];
                b.Write(w.Method(decl, "vtable-decl")); b.Write(w.Method(p.VTable[i], "vtable"));
            }
            var itables = p.IsInterface ? new List<KeyValuePair<Primitive, List<Code>>>() : p.ITables.ToList();
            b.Write(itables.Count);
            foreach (var kv in itables)
            {
                b.Write(w.Type(kv.Key)); b.Write(kv.Value.Count);
                for (int i = 0; i < kv.Value.Count; i++) { b.Write(w.Method(kv.Key.VTable[i], "iface")); b.Write(w.Method(kv.Value[i], "itable")); }
            }
            if (kind == TK_DELEGATE)
            {
                b.Write(w.Type(p.DelegateReturn)); b.Write(p.DelegateParams.Count);
                foreach (var dp in p.DelegateParams) b.Write(w.Type(dp));
            }
        }

        static void WriteMethod(W w, BinaryWriter b, Code c)
        {
            bool local = w.IsLocalCode(c, out var prefer);
            b.Write((byte)(local ? 1 : 0));
            b.Write(w.Str(c.EncodeName())); b.Write(w.Type(c.Owner));
            b.Write((byte)((c.IsStatic ? 1 : 0) | (c.IsVirtual || c.IsOverride ? 2 : 0) | (prefer ? 4 : 0) | (c.Name == "ctor" || c.Name.StartsWith("ctor_") ? 8 : 0) | (c.UntranslatableReason != null ? 16 : 0)));
            b.Write(w.Type(c.ReturnType)); b.Write(c.Arguments.Count);
            foreach (var a in c.Arguments) { b.Write(w.Type(a.Type)); b.Write((byte)((a.IsRef ? 1 : 0) | (a.IsOut ? 2 : 0))); }
            if (!local) return;
            b.Write(w.Str(CTranspiler.CodeDisplay(c))); b.Write(w.Str(c.SourceFile));
            b.Write(w.Str(c.UntranslatableReason)); // stub nedeni (-1 yok); interpreter ilk cagrida NotImplementedException firlatir
            b.Write(c.Locals.Count);
            foreach (var l in c.Locals) b.Write(w.Type(l));
            b.Write(c.Operations.Count);
            foreach (var op in c.Operations)
            {
                b.Write((byte)op.Type); b.Write(op.Slot); b.Write(op.Line);
                b.Write(w.Field(op.Field)); b.Write(w.Method(op.Code, "op")); b.Write(w.Type(op.PrimitiveRef));
                WriteValue(w, b, op.Value);
            }
        }

        static void WriteValue(W w, BinaryWriter b, object v)
        {
            switch (v)
            {
                case null: b.Write((byte)0); break;
                case int i: b.Write((byte)1); b.Write(i); break;
                case float f: b.Write((byte)2); b.Write(f); break;
                case double d: b.Write((byte)3); b.Write(d); break;
                case long l: b.Write((byte)4); b.Write(l); break;
                case string s: b.Write((byte)5); b.Write(w.Str(s)); break;
                case bool bo: b.Write((byte)6); b.Write((byte)(bo ? 1 : 0)); break;
                case char c: b.Write((byte)7); b.Write((ushort)c); break;
                case short sh: b.Write((byte)8); b.Write(sh); break;
                case byte by: b.Write((byte)9); b.Write(by); break;
                case uint ui: b.Write((byte)10); b.Write(ui); break;
                case ulong ul: b.Write((byte)11); b.Write(ul); break;
                case ushort us: b.Write((byte)12); b.Write(us); break;
                case sbyte sb: b.Write((byte)13); b.Write(sb); break;
                default: throw new Exception("ModuleWriter: desteklenmeyen literal tipi: " + v.GetType().Name);
            }
        }
    }
}
