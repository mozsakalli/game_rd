using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using DigitoyEngine.Language;

namespace DigitoyEngine.Cil
{
    // CIL frontend dilim 1a: derlenmis .NET assembly'sini (portable PDB'siyle) bizim IR'a cevirir.
    // Mimari karar: System.* referanslari BIZIM corelib yuzeyine REMAP edilir (CoreLib IL'i alinmaz);
    // yuzeyde olmayan uye ACIK HATA verir. PDB sequence point'leri paketli konuma ((satir<<10)|kolon)
    // ve SourceFile'a akar -> stack trace'ler CIL yolunda da kaynak satirli.
    // KAPSAM DISI (dilim 1b/2, hepsi acik hatayla): try/catch/finally bolgeleri (1b), generic'ler,
    // struct copy-semantigi ops'lari (initobj/ldobj), ref/out parametreler, filter, unsafe/ptr.
    public static class CilFrontend
    {
        // Son Compile cagrisinin atladigi/stub'ladigi ogeler (desteklenmeyen ozellikler; surucu raporlar).
        public static List<string> LastDiagnostics = new List<string>();

        public static List<Code> Compile(Context ctx, string dllPath, out Code entryPoint)
        {
            using var pe = new PEReader(File.OpenRead(dllPath));
            var md = pe.GetMetadataReader();
            var pdb = OpenPdb(pe, dllPath, out var pdbProvider);
            using var _pdbDispose = pdbProvider;

            var t = new Loader { ctx = ctx, md = md, pdb = pdb, PE = pe };
            t.LoadTypes();
            t.LoadMembers();
            var codes = t.LoadBodies();
            LastDiagnostics = t.diag;

            entryPoint = null;
            int ep = pe.PEHeaders.CorHeader.EntryPointTokenOrRelativeVirtualAddress;
            if (ep != 0 && t.methodMap.TryGetValue(MetadataTokens.MethodDefinitionHandle(ep), out var e))
                entryPoint = e;
            return codes;
        }

        static MetadataReader OpenPdb(PEReader pe, string dllPath, out IDisposable provider)
        {
            provider = null;
            foreach (var de in pe.ReadDebugDirectory()) // embedded PDB oncelikli
                if (de.Type == DebugDirectoryEntryType.EmbeddedPortablePdb)
                {
                    var p = pe.ReadEmbeddedPortablePdbDebugDirectoryData(de);
                    provider = p;
                    return p.GetMetadataReader();
                }
            var pdbPath = Path.ChangeExtension(dllPath, ".pdb");
            if (File.Exists(pdbPath))
            {
                var p = MetadataReaderProvider.FromPortablePdbStream(File.OpenRead(pdbPath));
                provider = p;
                return p.GetMetadataReader();
            }
            return null; // PDB yok: satir bilgisiz devam (trace'ler satirsiz)
        }

        class Loader
        {
            public Context ctx;
            public MetadataReader md;
            public MetadataReader pdb;
            public readonly Dictionary<TypeDefinitionHandle, Primitive> prims = new Dictionary<TypeDefinitionHandle, Primitive>();
            public readonly Dictionary<FieldDefinitionHandle, PrimitiveField> fieldMap = new Dictionary<FieldDefinitionHandle, PrimitiveField>();
            public readonly Dictionary<MethodDefinitionHandle, Code> methodMap = new Dictionary<MethodDefinitionHandle, Code>();
            public readonly Dictionary<MethodDefinitionHandle, MethodDefinition> methodDefs = new Dictionary<MethodDefinitionHandle, MethodDefinition>();
            public readonly List<string> diag = new List<string>(); // atlanan/stub ogeler (dilim 1b/2 bosluklari)
            SigTypes sig;

            string FullName(TypeDefinition td)
            {
                var nm = md.GetString(td.Name);
                // nested tip: kapsayan tipin adiyla nitele (yoksa Demo27.<>c ve Demo28.<>c cakisir)
                if (td.IsNested)
                {
                    var enc = md.GetTypeDefinition(td.GetDeclaringType());
                    return FullName(enc) + "/" + nm;
                }
                var ns = md.GetString(td.Namespace);
                return ns.Length > 0 ? ns + "." + nm : nm;
            }

            static bool SkipType(string full) =>
                full.StartsWith("<>y__InlineArray") || full.StartsWith("<Module>") || full.StartsWith("Microsoft.CodeAnalysis") || full.StartsWith("System.Runtime.CompilerServices");

            // TypeRef/TypeDef handle -> tam ad (base siniflandirmasi + corelib remap icin)
            public string RefName(EntityHandle h)
            {
                if (h.IsNil) return null;
                if (h.Kind == HandleKind.TypeDefinition) return FullName(md.GetTypeDefinition((TypeDefinitionHandle)h));
                if (h.Kind == HandleKind.TypeReference)
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)h);
                    var nm = md.GetString(tr.Name);
                    // nested TypeRef: ResolutionScope kapsayan tip -> "Enclosing/Nested" (FullName ile ayni sekil)
                    if (tr.ResolutionScope.Kind == HandleKind.TypeReference)
                        return RefName(tr.ResolutionScope) + "/" + nm;
                    var ns = md.GetString(tr.Namespace);
                    return ns.Length > 0 ? ns + "." + nm : nm;
                }
                throw new Exception($"CIL: beklenmeyen tip handle turu: {h.Kind} (generic'ler dilim 2)");
            }

            // Enum sabiti underlying tipe gore okunur (enum : byte/short/long olabilir); int'e sigdirilir.
            int ReadEnumConstant(Constant c)
            {
                var br = md.GetBlobReader(c.Value);
                return c.TypeCode switch
                {
                    ConstantTypeCode.SByte => br.ReadSByte(),
                    ConstantTypeCode.Byte => br.ReadByte(),
                    ConstantTypeCode.Int16 => br.ReadInt16(),
                    ConstantTypeCode.UInt16 => br.ReadUInt16(),
                    ConstantTypeCode.UInt32 => (int)br.ReadUInt32(),
                    ConstantTypeCode.Int64 => (int)br.ReadInt64(),
                    ConstantTypeCode.UInt64 => (int)br.ReadUInt64(),
                    _ => br.ReadInt32(),
                };
            }

            static int ScalarSize(Primitive t) => t?.Type switch
            {
                PrimitiveType.Bool or PrimitiveType.SByte or PrimitiveType.Byte => 1,
                PrimitiveType.Short or PrimitiveType.UShort or PrimitiveType.Char => 2,
                PrimitiveType.Int or PrimitiveType.UInt or PrimitiveType.Float => 4,
                PrimitiveType.Long or PrimitiveType.ULong or PrimitiveType.Double => 8,
                _ => 0
            };

            // corelib'de modellenmis (whitelist) arayuzler: DLL tipleri bunlari implement edince itable kurulur
            static readonly HashSet<string> ModeledIfaces = new HashSet<string>
            {
                "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IEnumerator`1",
                "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IList`1",
                "System.Collections.Generic.IDictionary`2",
                "System.Collections.IEnumerable", "System.Collections.IEnumerator", "System.IDisposable",
                "System.Runtime.CompilerServices.IAsyncStateMachine",
            };
            static bool IsModeledInterface(Primitive tmpl) => tmpl != null && tmpl.IsInterface && ModeledIfaces.Contains(tmpl.Name);

            // Class'a terfi ettirilen struct'lar (async SM): govde cevirisinde ldloca/initobj ozel islenir.
            public readonly HashSet<string> promotedStructs = new HashSet<string>();

            bool IsAsyncStateMachineStruct(TypeDefinition td)
            {
                bool implements = false;
                foreach (var ih in td.GetInterfaceImplementations())
                {
                    var i = md.GetInterfaceImplementation(ih).Interface;
                    if (i.Kind != HandleKind.TypeSpecification && RefName(i) == "System.Runtime.CompilerServices.IAsyncStateMachine")
                        implements = true;
                }
                return implements && md.GetString(td.Name).Contains("d__"); // Roslyn adlandirmasi: <Method>d__N
            }

            // ad -> Primitive: once assembly'nin kendi tipleri, sonra corelib yuzeyi (remap)
            public Primitive ResolveName(string full)
            {
                switch (full) // corelib primitive adlari -> bizim singletonlar
                {
                    case "System.Object": return Primitive.Object;
                    case "System.String": return Primitive.String;
                    case "System.Int32": return Primitive.Int;
                    case "System.UInt32": return Primitive.UInt;
                    case "System.Int64": return Primitive.Long;
                    case "System.UInt64": return Primitive.ULong;
                    case "System.Int16": return Primitive.Short;
                    case "System.UInt16": return Primitive.UShort;
                    case "System.SByte": return Primitive.SByte;
                    case "System.Byte": return Primitive.Byte;
                    case "System.Char": return Primitive.Char;
                    case "System.Boolean": return Primitive.Bool;
                    case "System.Single": return Primitive.Float;
                    case "System.Double": return Primitive.Double;
                    // .NET nested enumerator struct'lari -> bizim duzlestirilmis corelib tiplerimiz
                    case "System.Collections.Generic.List`1/Enumerator": full = "System.Collections.Generic.ListEnumerator`1"; break;
                    case "System.Collections.Generic.HashSet`1/Enumerator": full = "System.Collections.Generic.HashSetEnumerator`1"; break;
                    case "System.Collections.Generic.Dictionary`2/Enumerator": full = "System.Collections.Generic.DictionaryEnumerator`2"; break;
                }
                foreach (var kv in prims) // az tip: lineer arama yeterli
                    if (kv.Value.Name == full) return kv.Value;
                if (ctx.TryGetPrimitive(full, out var p)) return p;
                throw new Exception($"CIL: corelib yuzeyinde olmayan tip: {full} (gerekiyorsa corelib'e ekleyelim)");
            }

            public void LoadTypes()
            {
                sig = new SigTypes { loader = this };
                foreach (var h in md.TypeDefinitions)
                {
                    var td = md.GetTypeDefinition(h);
                    var full = FullName(td);
                    if (SkipType(full)) continue;
                    Primitive p;
                    try
                    {
                        // generic tip: base TypeSpec olabilir (Derived<T>:Base<T>) -> siniflandirmada Model say, base LoadMembers'da cozulur
                        var baseName = td.BaseType.Kind == HandleKind.TypeSpecification ? null : RefName(td.BaseType);
                        if (baseName == "System.Enum")
                        {
                            p = new Primitive { Name = full, Type = PrimitiveType.Int, IsEnum = true, EnumMembers = new Dictionary<string, int>() };
                        }
                        else if (baseName == "System.ValueType")
                        {
                            // Roslyn Release async state machine'i STRUCT uretir (ilk await'te builder kutular,
                            // omru heap'te gecer). Biz basindan class'a terfi ettiririz: Debug IL sekliyle ayni,
                            // corelib Async.cs (Start -> sm.MoveNext, Schedule(sm)) tek model gorur.
                            bool asyncSm = IsAsyncStateMachineStruct(td);
                            p = new Primitive { Name = full, Type = PrimitiveType.Model, IsStruct = !asyncSm };
                            if (asyncSm) promotedStructs.Add(full);
                        }
                        else if (baseName == "System.MulticastDelegate")
                        {
                            p = new Primitive { Name = full, Type = PrimitiveType.Model, IsDelegate = true };
                        }
                        else if ((td.Attributes & TypeAttributes.Interface) != 0)
                        {
                            p = new Primitive { Name = full, Type = PrimitiveType.Model, IsInterface = true };
                        }
                        else
                            p = new Primitive { Name = full, Type = PrimitiveType.Model };
                    }
                    catch (Exception ex)
                    {
                        diag.Add($"skip type: {full}: {ex.Message}");
                        continue;
                    }
                    foreach (var gph in td.GetGenericParameters()) // generic tip parametreleri (Box<T> -> [T]); imza decode'u referans verir
                        p.GenericParameters.Add(new Primitive { Name = md.GetString(md.GetGenericParameter(gph).Name), Type = PrimitiveType.Model, IsGenericParameter = true });
                    prims[h] = p;
                    ctx.RegisterPrimitive(p);
                }
            }

            public void LoadMembers()
            {
                foreach (var kv in prims)
                {
                    var td = md.GetTypeDefinition(kv.Key);
                    var p = kv.Value;
                    if (!p.IsEnum && !p.IsStruct && !p.IsInterface && !p.IsDelegate)
                    {
                        try
                        {
                            var baseName = RefName(td.BaseType);
                            p.Parent = baseName == "System.Object" || baseName == null ? Primitive.Object : ResolveName(baseName);
                        }
                        catch (Exception ex) { diag.Add($"skip base of {p.Name}: {ex.Message}"); p.Parent = Primitive.Object; }
                        foreach (var ih in td.GetInterfaceImplementations())
                            try
                            {
                                var ihandle = md.GetInterfaceImplementation(ih).Interface;
                                // generic arayuz ornegi (IEnumerable<int>) TypeSpec gelir -> Apply node coz
                                var iface = ihandle.Kind == HandleKind.TypeSpecification
                                    ? DecodeTypeSpecHandle((TypeSpecificationHandle)ihandle, new GenCtx { TypeParams = p.GenericParameters })
                                    : ResolveName(RefName(ihandle));
                                var tmpl = iface.GenericTemplate ?? iface;
                                // DLL'de tanimli VEYA corelib'de modellenmis (whitelist) arayuzler itable'a girer;
                                // digerleri dusurulur (yoksa Hierarchy tum uyeleri arar -> fatal).
                                if (!prims.ContainsValue(tmpl) && !IsModeledInterface(tmpl))
                                {
                                    diag.Add($"drop iface {iface.Name} on {p.Name}");
                                    continue;
                                }
                                p.Interfaces.Add(iface);
                            }
                            catch (Exception ex) { diag.Add($"skip iface of {p.Name}: {ex.Message}"); }
                    }
                    foreach (var fh in td.GetFields())
                    {
                        var fd = md.GetFieldDefinition(fh);
                        var fname = md.GetString(fd.Name);
                        if (p.IsEnum)
                        {
                            if (fname == "value__") continue;
                            var dv = fd.GetDefaultValue();
                            if (dv.IsNil) continue;
                            p.EnumMembers[fname] = ReadEnumConstant(md.GetConstant(dv));
                            continue;
                        }
                        try
                        {
                            var ft = fd.DecodeSignature(sig, new GenCtx { TypeParams = p.GenericParameters });
                            var pf = new PrimitiveField { Name = fname, Type = ft, IsStatic = (fd.Attributes & FieldAttributes.Static) != 0 };
                            p.AddField(pf);
                            fieldMap[fh] = pf;
                        }
                        catch (Exception ex) { diag.Add($"skip field {p.Name}.{fname}: {ex.Message}"); }
                    }
                    // fixed buffer (`fixed float m[4]`): Roslyn `<m>e__FixedBuffer` struct'i tek `FixedElementField`
                    // + explicit ClassLayout size ile uretir. Tek alani FixedArray'e cevir (dogru boyut + C `float m[N]`).
                    try
                    {
                        var layout = td.GetLayout();
                        if (!layout.IsDefault && layout.Size > 0 && p.Fields.Count == 1 && !p.Fields[0].IsStatic)
                        {
                            int es = ScalarSize(p.Fields[0].Type);
                            if (es > 0 && layout.Size > es && layout.Size % es == 0)
                                p.Fields[0].Type = Primitive.FixedArrayOf(p.Fields[0].Type, layout.Size / es);
                        }
                    }
                    catch { /* layout yoksa dokunma */ }
                }
                // methodlar: delegate Invoke imzasi + normal Code kayitlari
                foreach (var kv in prims)
                {
                    var td = md.GetTypeDefinition(kv.Key);
                    var p = kv.Value;
                    // MethodImpl tablosu: acik arayuz implementasyonlari (System.Collections.Generic.IEnumerable<int>.GetEnumerator gibi)
                    var explicitImpls = new Dictionary<MethodDefinitionHandle, (Primitive iface, string simple)>();
                    foreach (var mih in td.GetMethodImplementations())
                    {
                        try
                        {
                            var mi = md.GetMethodImplementation(mih);
                            if (mi.MethodBody.Kind != HandleKind.MethodDefinition) continue;
                            var decl = mi.MethodDeclaration;
                            EntityHandle declParent; string declName;
                            if (decl.Kind == HandleKind.MemberReference)
                            {
                                var mr = md.GetMemberReference((MemberReferenceHandle)decl);
                                declParent = mr.Parent; declName = md.GetString(mr.Name);
                            }
                            else if (decl.Kind == HandleKind.MethodDefinition)
                            {
                                var dmd = md.GetMethodDefinition((MethodDefinitionHandle)decl);
                                declParent = dmd.GetDeclaringType(); declName = md.GetString(dmd.Name);
                            }
                            else continue;
                            var iface = declParent.Kind == HandleKind.TypeSpecification
                                ? DecodeTypeSpecHandle((TypeSpecificationHandle)declParent, new GenCtx { TypeParams = p.GenericParameters })
                                : ResolveName(RefName(declParent));
                            var tmpl = iface.GenericTemplate ?? iface;
                            if (prims.ContainsValue(tmpl) || IsModeledInterface(tmpl)) // yalniz tuttugumuz arayuzler
                                explicitImpls[(MethodDefinitionHandle)mi.MethodBody] = (iface, declName);
                        }
                        catch { /* cozulemeyen MethodImpl: normal method olarak yukle */ }
                    }
                    foreach (var mh in td.GetMethods())
                    {
                        var m = md.GetMethodDefinition(mh);
                        var mname = md.GetString(m.Name);
                        if (p.IsEnum) continue;
                        if (p.IsDelegate && mname != "Invoke") continue; // ctor/BeginInvoke runtime'da; imza cozmeye kalkma
                        if (mname == "Finalize") { diag.Add($"skip finalizer: {p.Name}.Finalize"); continue; } // destructor: GC modeli disi
                        // generic method parametrelerini once olustur (imza !!0 ile refere eder)
                        var mgps = new List<Primitive>();
                        foreach (var gph in m.GetGenericParameters())
                            mgps.Add(new Primitive { Name = md.GetString(md.GetGenericParameter(gph).Name), Type = PrimitiveType.Model, IsGenericParameter = true });
                        var mgc = new GenCtx { TypeParams = p.GenericParameters, MethodParams = mgps };
                        MethodSignature<Primitive> s;
                        try { s = m.DecodeSignature(sig, mgc); }
                        catch (Exception ex) { diag.Add($"skip method {p.Name}.{mname}: imza: {ex.Message}"); continue; }
                        if (p.IsDelegate)
                        {
                            p.DelegateReturn = s.ReturnType;
                            p.DelegateParams.AddRange(s.ParameterTypes);
                            continue;
                        }
                        bool isStatic = (m.Attributes & MethodAttributes.Static) != 0;
                        bool isVirtualAttr = (m.Attributes & MethodAttributes.Virtual) != 0;
                        bool isNewSlot = (m.Attributes & MethodAttributes.NewSlot) != 0;
                        var simple = mname == ".ctor" ? "ctor" : mname == ".cctor" ? "cctor" : mname;
                        explicitImpls.TryGetValue(mh, out var expl);
                        var simpleDisp = expl.iface != null ? expl.simple : simple; // acik impl: gosterim/eslesme basit ad
                        var code = new Code
                        {
                            Owner = p,
                            Name = expl.iface != null
                                ? "__iface_" + expl.iface.Name.Replace('.', '_') + "__" + Code.Mangle(expl.simple, s.ParameterTypes)
                                : Code.Mangle(simple, s.ParameterTypes),
                            IsStatic = isStatic,
                            ReturnType = s.ReturnType,
                            // iface uyeleri MiniCs'teki gibi ortuk virtual sayilir (itable dispatch)
                            IsVirtual = (isVirtualAttr && isNewSlot) || p.IsInterface,
                            IsOverride = isVirtualAttr && !isNewSlot,
                            ExplicitInterface = expl.iface,
                            DisplayName = (simpleDisp == ".ctor" ? ".ctor" : simpleDisp == ".cctor" ? ".cctor" : simpleDisp)
                                          + "(" + string.Join(", ", s.ParameterTypes.Select(Primitive.CsDisplay)) + ")"
                        };
                        if (!isStatic)
                            code.Arguments.Add(new Argument { Name = "this", Type = p, IsRef = p.IsStruct }); // struct this = managed pointer (mutasyon gorunur)
                        code.GenericParameters.AddRange(mgps); // generic method sablonu (Pick<T>) -> monomorfize edilir
                        var paramNames = new Dictionary<int, string>();
                        foreach (var ph in m.GetParameters())
                        {
                            var pr = md.GetParameter(ph);
                            if (pr.SequenceNumber > 0) paramNames[pr.SequenceNumber - 1] = md.GetString(pr.Name);
                        }
                        for (int i = 0; i < s.ParameterTypes.Length; i++)
                            code.Arguments.Add(new Argument { Name = paramNames.TryGetValue(i, out var pn) ? pn : $"a{i}", Type = s.ParameterTypes[i] });
                        // P/Invoke ([DllImport]): govde yok; C sembolu = EntryPoint (kutuphane adi AOT'ta yok sayilir,
                        // gercek .NET host'ta platform native lib'inden yuklenir). Mangling yerine bu ad emit edilir.
                        if ((m.Attributes & MethodAttributes.PinvokeImpl) != 0)
                        {
                            var imp = m.GetImport();
                            code.IsExternal = true;
                            code.ExternalSymbol = imp.Name.IsNil ? simple : md.GetString(imp.Name);
                        }
                        ctx.RegisterCode(code);
                        methodMap[mh] = code;
                        methodDefs[mh] = m;
                    }
                }
            }

            public List<Code> LoadBodies()
            {
                var result = new List<Code>();
                foreach (var kv in methodMap)
                {
                    var m = methodDefs[kv.Key];
                    var code = kv.Value;
                    result.Add(code);
                    if (m.RelativeVirtualAddress == 0)
                    {
                        // [UnsafeAccessor(Field)] extern ref-thunk (CatalogWriter): govde yok, biz
                        // sentezleriz: ldarg.0; ldflda <ad>; ret. Erisim denetimi AOT'de yoktur.
                        try { SynthesizeUnsafeAccessor(m, code); }
                        catch (Exception ex) { StubBody(code, "UnsafeAccessor: " + ex.Message); }
                        continue; // abstract/iface: govdesiz
                    }
                    try
                    {
                        var body = PE.GetMethodBody(m.RelativeVirtualAddress);
                        new BodyTranslator { loader = this, md = md, pdb = pdb, code = code, mh = kv.Key }.Translate(body);
                    }
                    catch (Exception ex)
                    {
                        // Cevrilemeyen govde: derlenebilir stub (yalniz ilgili case uyusmaz, digerleri calisir).
                        StubBody(code, ex.Message);
                    }
                }
                return result;
            }

            // Govde cevrilemediginde: op'lari temizle, CTranspiler tipe uygun sifir stub uretsin.
            void StubBody(Code code, string reason)
            {
                var msg = reason.Replace("\r", " ").Replace("\n", " ");
                diag.Add($"stub body {code.EncodeName()}: {msg}");
                code.Operations.Clear();
                code.NativeBody = null;
                code.UntranslatableReason = msg;
            }

            // [UnsafeAccessor(UnsafeAccessorKind.Field, Name="x")] static extern ref T F(Owner o)
            // -> { return ref o.x; }. Sahip struct ise parametre zaten ref (Pointer) gelir.
            void SynthesizeUnsafeAccessor(MethodDefinition m, Code code)
            {
                string fieldName = null;
                bool isAccessor = false;
                foreach (var ah in m.GetCustomAttributes())
                {
                    var a = md.GetCustomAttribute(ah);
                    string attrType = AttributeTypeName(a);
                    if (attrType != "System.Runtime.CompilerServices.UnsafeAccessorAttribute")
                        continue;
                    isAccessor = true;
                    // blob: prolog(2) + i32 kind + named args: u16 count, (u8 kind, u8 type, SerString name, SerString value)
                    var br = md.GetBlobReader(a.Value);
                    br.ReadUInt16();
                    int kind = br.ReadInt32();
                    if (kind != 3) throw new Exception($"yalniz Field accessor desteklenir (kind {kind})");
                    int named = br.ReadUInt16();
                    for (int i = 0; i < named; i++)
                    {
                        br.ReadByte(); // 0x54 property
                        br.ReadByte(); // 0x0E string
                        var pname = br.ReadSerializedString();
                        var pval = br.ReadSerializedString();
                        if (pname == "Name") fieldName = pval;
                    }
                }
                if (!isAccessor) return;
                if (fieldName == null || code.Arguments.Count != 1)
                    throw new Exception("beklenen bicim: Name=... ve tek sahip parametresi");
                var owner = code.Arguments[0].Type;
                if (owner.Type == PrimitiveType.Pointer) owner = owner.ElementType; // ref struct sahibi
                PrimitiveField field = null;
                for (var t = owner; t != null && field == null; t = t.Parent)
                    foreach (var f in t.Fields)
                        if (f.Name == fieldName) { field = f; break; }
                if (field == null) throw new Exception($"alan yok: {owner.Name}.{fieldName}");
                code.Operations.Clear();
                code.Operations.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                code.Operations.Add(new Op { Type = OpType.AddrField, Field = field, TypeArguments = new List<Primitive>() });
                code.Operations.Add(new Op { Type = OpType.Return });
            }

            string AttributeTypeName(CustomAttribute a)
            {
                EntityHandle ctorOwner;
                if (a.Constructor.Kind == HandleKind.MemberReference)
                    ctorOwner = md.GetMemberReference((MemberReferenceHandle)a.Constructor).Parent;
                else
                    ctorOwner = md.GetMethodDefinition((MethodDefinitionHandle)a.Constructor).GetDeclaringType();
                return ctorOwner.Kind == HandleKind.TypeReference || ctorOwner.Kind == HandleKind.TypeDefinition
                    ? RefName(ctorOwner) : null;
            }

            public PEReader PE;

            // <PrivateImplementationDetails> array-literal init verisi: field RVA'sindan ham bayt oku
            public byte[] FieldInitData(FieldDefinitionHandle h, int byteCount)
            {
                var fd = md.GetFieldDefinition(h);
                int rva = fd.GetRelativeVirtualAddress();
                if (rva == 0) return null;
                var reader = PE.GetSectionData(rva).GetReader();
                return reader.ReadBytes(byteCount);
            }

            // token'in sahibi tipin tam adi (MemberRef parent'i ya da MethodDef'in bildirim tipi)
            public string OwnerNameOf(EntityHandle h)
            {
                if (h.Kind == HandleKind.MemberReference)
                    return RefName(md.GetMemberReference((MemberReferenceHandle)h).Parent);
                if (h.Kind == HandleKind.MethodDefinition)
                    return FullName(md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)h).GetDeclaringType()));
                throw new Exception($"CIL: beklenmeyen uye handle: {h.Kind}");
            }

            // sahip tipi Primitive olarak coz (generic ornek -> Apply node dondurur; Concretize somutlar)
            public Primitive OwnerPrimOf(EntityHandle h, GenCtx gc)
            {
                if (h.Kind == HandleKind.MethodSpecification)
                    return OwnerPrimOf(md.GetMethodSpecification((MethodSpecificationHandle)h).Method, gc);
                if (h.Kind == HandleKind.MemberReference)
                {
                    var parent = md.GetMemberReference((MemberReferenceHandle)h).Parent;
                    if (parent.Kind == HandleKind.TypeSpecification)
                        return DecodeTypeSpecHandle((TypeSpecificationHandle)parent, gc); // Apply node
                    return ResolveName(RefName(parent));
                }
                if (h.Kind == HandleKind.MethodDefinition)
                    return methodMap.TryGetValue((MethodDefinitionHandle)h, out var c) ? c.Owner : ResolveName(OwnerNameOf(h));
                throw new Exception($"CIL: beklenmeyen uye handle: {h.Kind}");
            }
            // Apply node ya da somut tip icin delegate mi? (sablona bak)
            public static bool IsDelegateType(Primitive p) => (p.GenericTemplate ?? p).IsDelegate;

            public Primitive DecodeTypeSpecHandle(TypeSpecificationHandle h, GenCtx gc)
                => md.GetTypeSpecification(h).DecodeSignature(sig, gc);

            // template generic parametreleri -> somut tip argumanlari haritasi ([sinif]+[method] sozlesme)
            Dictionary<Primitive, Primitive> BuildMap(Code tmpl, List<Primitive> typeArgs)
            {
                var map = new Dictionary<Primitive, Primitive>();
                int ci = 0;
                if (tmpl.Owner != null)
                    foreach (var gp in tmpl.Owner.GenericParameters) { if (ci < typeArgs.Count) map[gp] = typeArgs[ci++]; }
                foreach (var gp in tmpl.GenericParameters) { if (ci < typeArgs.Count) map[gp] = typeArgs[ci++]; }
                return map;
            }
            // template arg tipini somuta cevir (temp/stack tiplemesi icin); typeArgs bossa oldugu gibi
            public Primitive ConcreteType(Code tmpl, List<Primitive> typeArgs, Primitive t)
                => typeArgs.Count == 0 ? t : GenericInstantiator.Substitute(ctx, t, BuildMap(tmpl, typeArgs));

            // Apply node owner'in generic parametreleriyle tipi somutla (delegate donus/param tipi icin)
            public Primitive ConcreteTypeForOwner(Primitive appliedOwner, Primitive t)
            {
                if (appliedOwner.GenericTemplate == null) return t;
                var map = new Dictionary<Primitive, Primitive>();
                var tps = appliedOwner.GenericTemplate.GenericParameters;
                for (int i = 0; i < tps.Count && i < appliedOwner.TypeArguments.Count; i++) map[tps[i]] = appliedOwner.TypeArguments[i];
                return GenericInstantiator.Substitute(ctx, t, map);
            }

            // generic alan tipini somutla (Box<int>.Value : T -> int)
            public Primitive FieldTypeConcrete(PrimitiveField f, List<Primitive> classArgs)
            {
                if (classArgs.Count == 0 || f.Owner == null) return f.Type;
                var map = new Dictionary<Primitive, Primitive>();
                var tps = f.Owner.GenericParameters;
                for (int i = 0; i < tps.Count && i < classArgs.Count; i++) map[tps[i]] = classArgs[i];
                return GenericInstantiator.Substitute(ctx, f.Type, map);
            }

            // ---- cagri/alan referans cozumu (MemberRef -> corelib remap / MethodDef -> Code) ----
            public Code ResolveMethod(EntityHandle h) => ResolveMethod(h, GenCtx.None, out _);
            public Code ResolveMethod(EntityHandle h, GenCtx gc, out List<Primitive> typeArgs)
            {
                typeArgs = new List<Primitive>();
                if (h.Kind == HandleKind.MethodSpecification)
                {
                    var msp = md.GetMethodSpecification((MethodSpecificationHandle)h);
                    var methodArgs = msp.DecodeSignature(sig, gc); // !!0 -> somut method tip argumanlari
                    // referans edilen generic method imzasi !!0..!!N tasir -> somut method arglarini
                    // MethodParams olarak ver (yoksa "generic method parametresi baglam disi").
                    var innerGc = new GenCtx { TypeParams = gc.TypeParams, MethodParams = methodArgs };
                    var baseCode = ResolveMethod(msp.Method, innerGc, out var classArgs);
                    typeArgs.AddRange(classArgs);   // sozlesme: [sinif args] + [method args]
                    typeArgs.AddRange(methodArgs);
                    return baseCode;
                }
                if (h.Kind == HandleKind.MethodDefinition)
                    return methodMap.TryGetValue((MethodDefinitionHandle)h, out var c) ? c
                        : throw new Exception("CIL: govdesi yuklenmemis MethodDef");
                if (h.Kind != HandleKind.MemberReference)
                    throw new Exception($"CIL: beklenmeyen cagri hedefi: {h.Kind}");
                var mr = md.GetMemberReference((MemberReferenceHandle)h);
                var name = md.GetString(mr.Name);
                if (mr.Parent.Kind == HandleKind.TypeSpecification) // generic tip ornegi uyesi (Box<int>.Get)
                {
                    var applied = DecodeTypeSpecHandle((TypeSpecificationHandle)mr.Parent, gc);
                    var template = applied.GenericTemplate ?? applied;
                    typeArgs.AddRange(applied.TypeArguments);
                    // uye imzasini sablonun kendi generic parametreleriyle coz (mangle eslesmesi icin)
                    var s2 = mr.DecodeMethodSignature(sig, new GenCtx { TypeParams = template.GenericParameters });
                    var simple2 = name == ".ctor" ? "ctor" : OperatorRemap(name);
                    return FindCode(template, simple2, s2.ParameterTypes);
                }
                var ownerName = RefName(mr.Parent);
                var s = mr.DecodeMethodSignature(sig, gc);
                var owner = ResolveName(ownerName);
                // ozel remap'ler: Roslyn'in urettigi corelib cagrilari bizim yuzeye
                if (ownerName == "System.String" && name == "Concat" && s.ParameterTypes.Length == 2
                    && s.ParameterTypes[0] == Primitive.String && s.ParameterTypes[1] == Primitive.String)
                    return FindCode(Primitive.String, "op_add", s.ParameterTypes);
                var simple = name == ".ctor" ? "ctor" : OperatorRemap(name);
                return FindCode(owner, simple, s.ParameterTypes);
            }

            // Roslyn'in operator metot adlarini (op_Equality vb.) bizim MiniCs adlarina (op_eq vb.) esle
            public static string OperatorRemap(string name) => name switch
            {
                "op_Equality" => "op_eq",
                "op_Inequality" => "op_ne",
                "op_Addition" => "op_add",
                "op_Subtraction" => "op_sub",
                "op_Multiply" => "op_mul",
                "op_Division" => "op_div",
                "op_Modulus" => "op_mod",
                "op_LessThan" => "op_lt",
                "op_GreaterThan" => "op_gt",
                "op_LessThanOrEqual" => "op_le",
                "op_GreaterThanOrEqual" => "op_ge",
                _ => name
            };

            public Code FindCode(Primitive owner, string simple, ImmutableArray<Primitive> ps)
            {
                // aday mangle'lar: IL byref (Pointer) ref/out ayrimini korumaz; corelib ise
                // `ref_`/`out_` + eleman-adi ile mangle'lar. Her Pointer param icin {*Name, out_Name, ref_Name}
                // varyantlarini uret, kalitim zincirinde ara.
                foreach (var mangled in MangleCandidates(simple, ps))
                    for (var t = owner; t != null; t = t.Parent)
                        if (ctx.TryGetCode(t.Name + "$" + mangled, out var c)) return c;
                // generic METHOD (method-duzeyi generic'ler, orn. Enumerable.FirstOrDefault<T>(IEnumerable<T>,...)):
                // exact mangle tutmaz (IEnumerable<Int> vs IEnumerable<T>) -> template'i unify ile bul.
                var gm = FindGenericMethod(owner, simple, ps);
                if (gm != null) return gm;
                throw new Exception($"CIL: corelib yuzeyinde olmayan uye: {owner.Name}.{simple}({string.Join(",", ps.Select(x => x.Name))}) (gerekiyorsa corelib'e ekleyelim)"
                    + NearMissReport(owner, simple, ps));
            }

            // near-miss raporu: denenen anahtarlar + owner zincirinde ayni ada sahip adaylar ve eleme sebepleri
            string NearMissReport(Primitive owner, string simple, ImmutableArray<Primitive> ps)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("\n  denenen anahtarlar: ").Append(string.Join(", ", MangleCandidates(simple, ps).Take(6)));
                var near = new List<string>();
                for (var t = owner; t != null && near.Count < 8; t = t.Parent)
                    foreach (var c in ctx.AllCodes)
                    {
                        if (c.Owner != t) continue;
                        var disp = c.DisplayName ?? c.Name;
                        int par = disp.IndexOf('(');
                        var sn = par >= 0 ? disp.Substring(0, par) : disp;
                        if (sn != simple && !c.Name.StartsWith(simple + "_") && c.Name != simple) continue;
                        int thisOff = c.IsStatic ? 0 : 1;
                        string neden = c.Arguments.Count - thisOff != ps.Length
                            ? $"arg sayisi {c.Arguments.Count - thisOff} != {ps.Length}"
                            : c.GenericParameters.Count > 0 ? "generic unify tutmadi" : "imza tipi uyusmadi";
                        near.Add($"{t.Name}${c.Name} ({neden})");
                        if (near.Count >= 8) break;
                    }
                if (near.Count > 0) sb.Append("\n  yakin adaylar: ").Append(string.Join("; ", near));
                else sb.Append($"\n  yakin aday yok: '{simple}' adinda uye {owner.Name} zincirinde tanimli degil");
                return sb.ToString();
            }

            // template param'lari (kendi generic'leri joker) somut cagri param'lariyla unify eden esleyici
            Code FindGenericMethod(Primitive owner, string simple, ImmutableArray<Primitive> ps)
            {
                for (var t = owner; t != null && !t.Unresolved; t = t.Parent)
                    foreach (var c in ctx.AllCodes)
                    {
                        if (c.Owner != t || c.GenericParameters.Count == 0) continue;
                        int thisOff = c.IsStatic ? 0 : 1; // instance metotta Arguments[0]=this, ps disinda
                        if (c.Arguments.Count - thisOff != ps.Length) continue;
                        var disp = c.DisplayName ?? "";
                        int par = disp.IndexOf('(');
                        var sn = par >= 0 ? disp.Substring(0, par) : disp;
                        if (sn != simple) continue;
                        var map = new Dictionary<Primitive, Primitive>();
                        bool ok = true;
                        for (int i = 0; i < ps.Length && ok; i++) ok = UnifyType(c.Arguments[i + thisOff].Type, ps[i], c.GenericParameters, map);
                        if (ok && map.Count == c.GenericParameters.Count) return c;
                    }
                return null;
            }
            static bool UnifyType(Primitive template, Primitive concrete, List<Primitive> generics, Dictionary<Primitive, Primitive> map)
            {
                if (template == null || concrete == null) return template == concrete;
                if (generics.Contains(template))
                    return map.TryGetValue(template, out var bound) ? bound == concrete : (map[template] = concrete) != null;
                if (template.GenericTemplate != null && concrete.GenericTemplate != null)
                {
                    if (template.GenericTemplate != concrete.GenericTemplate || template.TypeArguments.Count != concrete.TypeArguments.Count) return false;
                    for (int i = 0; i < template.TypeArguments.Count; i++)
                        if (!UnifyType(template.TypeArguments[i], concrete.TypeArguments[i], generics, map)) return false;
                    return true;
                }
                if (template.Type == PrimitiveType.Array && concrete.Type == PrimitiveType.Array)
                    return UnifyType(template.ElementType, concrete.ElementType, generics, map);
                if (template.Type == PrimitiveType.Pointer && concrete.Type == PrimitiveType.Pointer) // ref/out T[] vs *Component[]
                    return UnifyType(template.ElementType, concrete.ElementType, generics, map);
                if (concrete.Type == PrimitiveType.Pointer && template.Type != PrimitiveType.Pointer) // corelib `ref T[]` = IsRef arg (tip Array), IL tarafi byref Pointer
                    return UnifyType(template, concrete.ElementType, generics, map);
                return template == concrete;
            }

            // param-basi token varyantlarinin kartezyen carpimi (Pointer param -> 3 form; digerleri -> 1 form)
            static IEnumerable<string> MangleCandidates(string simple, ImmutableArray<Primitive> ps)
            {
                var perParam = new List<string[]>();
                foreach (var t in ps)
                {
                    if (t.Type == PrimitiveType.Pointer)
                    {
                        var elem = t.ElementType.Name.Replace('.', '_');
                        perParam.Add(new[] { t.Name.Replace('.', '_'), "out_" + elem, "ref_" + elem });
                    }
                    else perParam.Add(new[] { t.Name.Replace('.', '_') });
                }
                foreach (var combo in Cartesian(perParam))
                    yield return simple + string.Concat(combo.Select(x => "_" + x));
            }

            static IEnumerable<string[]> Cartesian(List<string[]> options)
            {
                var idx = new int[options.Count];
                while (true)
                {
                    var row = new string[options.Count];
                    for (int i = 0; i < options.Count; i++) row[i] = options[i][idx[i]];
                    yield return row;
                    int k = options.Count - 1;
                    while (k >= 0 && ++idx[k] >= options[k].Length) { idx[k] = 0; k--; }
                    if (k < 0) yield break;
                }
            }


            public PrimitiveField ResolveField(EntityHandle h) => ResolveField(h, GenCtx.None, out _);

            // generic alan erisimi: MemberRef + TypeSpec parent -> sablon alani + sinif tip argumanlari.
            // Dis assembly alani (player -> engine, engine -> corelib): MemberRef + TypeRef parent ->
            // tip ctx'ten (onceden yuklenmis assembly / corelib) adla cozulur, alan adla bulunur.
            public PrimitiveField ResolveField(EntityHandle h, GenCtx gc, out List<Primitive> typeArgs)
            {
                typeArgs = new List<Primitive>();
                if (h.Kind == HandleKind.FieldDefinition)
                    return fieldMap[(FieldDefinitionHandle)h];
                if (h.Kind == HandleKind.MemberReference)
                {
                    var mr = md.GetMemberReference((MemberReferenceHandle)h);
                    var fname = md.GetString(mr.Name);
                    Primitive owner;
                    if (mr.Parent.Kind == HandleKind.TypeSpecification)
                    {
                        var applied = DecodeTypeSpecHandle((TypeSpecificationHandle)mr.Parent, gc);
                        owner = applied.GenericTemplate ?? applied;
                        typeArgs.AddRange(applied.TypeArguments);
                    }
                    else
                        owner = ResolveName(RefName(mr.Parent));
                    for (var t = owner; t != null; t = t.Parent)
                    {
                        foreach (var f in t.Fields) if (f.Name == fname) return f;
                        foreach (var f in t.StaticFields) if (f.Name == fname) return f;
                    }
                    throw new Exception($"CIL: tipte alan yok: {owner.Name}.{fname}");
                }
                throw new Exception($"CIL: beklenmeyen alan handle turu: {h.Kind}");
            }

            public Primitive DecodeStandaloneLocals(StandaloneSignatureHandle h, List<Primitive> into, GenCtx gc)
            {
                if (h.IsNil) return null;
                var ss = md.GetStandaloneSignature(h);
                foreach (var lt in ss.DecodeLocalSignature(sig, gc)) into.Add(lt);
                return null;
            }

            public SigTypes Sig => sig;
        }

        // imza cozumunde generic baglam: siniftan gelen tip parametreleri (!0) + method'tan gelenler (!!0)
        public struct GenCtx
        {
            public IReadOnlyList<Primitive> TypeParams;   // sinif generic parametreleri (!0, !1, ...)
            public IReadOnlyList<Primitive> MethodParams; // method generic parametreleri (!!0, !!1, ...)
            public static readonly GenCtx None = new GenCtx();
        }

        // imza cozucu: metadata tip kodlari -> bizim Primitive'ler (generic Apply node / ptr byref)
        class SigTypes : ISignatureTypeProvider<Primitive, GenCtx>
        {
            public Loader loader;
            static readonly Dictionary<Primitive, Primitive> arrayCache = new Dictionary<Primitive, Primitive>();

            public Primitive GetPrimitiveType(PrimitiveTypeCode code) => code switch
            {
                PrimitiveTypeCode.Void => Primitive.Void,
                PrimitiveTypeCode.Boolean => Primitive.Bool,
                PrimitiveTypeCode.Char => Primitive.Char,
                PrimitiveTypeCode.SByte => Primitive.SByte,
                PrimitiveTypeCode.Byte => Primitive.Byte,
                PrimitiveTypeCode.Int16 => Primitive.Short,
                PrimitiveTypeCode.UInt16 => Primitive.UShort,
                PrimitiveTypeCode.Int32 => Primitive.Int,
                PrimitiveTypeCode.UInt32 => Primitive.UInt,
                PrimitiveTypeCode.Int64 => Primitive.Long,
                PrimitiveTypeCode.UInt64 => Primitive.ULong,
                PrimitiveTypeCode.Single => Primitive.Float,
                PrimitiveTypeCode.Double => Primitive.Double,
                PrimitiveTypeCode.String => Primitive.String,
                PrimitiveTypeCode.Object => Primitive.Object,
                PrimitiveTypeCode.IntPtr => Primitive.Long, // ldftn ara degeri (delegate deseni)
                _ => throw new Exception($"CIL: desteklenmeyen primitive tip: {code}")
            };

            public Primitive GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
            {
                if (loader.prims.TryGetValue(handle, out var p)) return p;
                var td = reader.GetTypeDefinition(handle);
                throw new Exception("CIL: bilinmeyen TypeDef: " + reader.GetString(td.Namespace) + "." + reader.GetString(td.Name));
            }
            public Primitive GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
                => loader.ResolveName(loader.RefName(handle));
            public Primitive GetTypeFromSpecification(MetadataReader reader, GenCtx ctx2, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, ctx2); // ic imzayi ayni baglamla coz
            public Primitive GetSZArrayType(Primitive elementType)
            {
                if (!arrayCache.TryGetValue(elementType, out var a)) arrayCache[elementType] = a = Primitive.ArrayOf(elementType);
                return a;
            }
            public Primitive GetArrayType(Primitive elementType, ArrayShape shape) => throw new Exception("CIL: cok boyutlu dizi yok (bilinen sinir)");
            public Primitive GetByReferenceType(Primitive elementType) => Primitive.PointerOf(elementType); // ref/out = managed pointer (IL: ldarg->ptr, ldind->deref)
            public Primitive GetPointerType(Primitive elementType) => Primitive.PointerOf(elementType); // unsafe T* : byte-adresli pointer (ldind/stind eleman tipiyle deref)
            public Primitive GetFunctionPointerType(MethodSignature<Primitive> signature) => throw new Exception("CIL: fnptr tipi yok");
            public Primitive GetGenericInstantiation(Primitive genericType, ImmutableArray<Primitive> typeArguments) => Primitive.Apply(genericType, typeArguments.ToArray());
            public Primitive GetGenericMethodParameter(GenCtx ctx2, int index) => ctx2.MethodParams != null && index < ctx2.MethodParams.Count ? ctx2.MethodParams[index] : throw new Exception("CIL: generic method parametresi baglam disi");
            public Primitive GetGenericTypeParameter(GenCtx ctx2, int index) => ctx2.TypeParams != null && index < ctx2.TypeParams.Count ? ctx2.TypeParams[index] : throw new Exception("CIL: generic tip parametresi baglam disi");
            public Primitive GetModifiedType(Primitive modifier, Primitive unmodifiedType, bool isRequired) => unmodifiedType;
            // pinned local = duz local: GC nesne TASIMAZ ve yalniz managed frame yokken kosar -> pin gereksiz.
            // (byref pinned -> Pointer, array/string pinned -> ayni tip; `fixed` sonundaki null atamasi oldugu gibi gecer)
            public Primitive GetPinnedType(Primitive elementType) => elementType;
        }

        // ---- govde cevirisi: IL -> bizim op'lar; dal hedeflerinde tip-simulasyonlu spill ----
        class BodyTranslator
        {
            public Loader loader;
            public MetadataReader md;
            public MetadataReader pdb;
            public Code code;
            public MethodDefinitionHandle mh;

            readonly List<Op> ops = new List<Op>();
            readonly List<Primitive> stack = new List<Primitive>();
            Dictionary<int, Label> labels = new Dictionary<int, Label>();
            Dictionary<int, List<int>> mergeSlots = new Dictionary<int, List<int>>(); // hedef -> spill local slotlari
            readonly Dictionary<int, int> seqPoints = new Dictionary<int, int>(); // IL offset -> paketli konum
            byte[] ilBytes; // finally govdesini yeniden decode etmek icin
            readonly HashSet<int> finallySkip = new HashSet<int>(); // ana donguce atlanacak finally handler offsetleri
            Code pendingFtn; // ldftn/ldvirtftn -> newobj DelegateCtor deseni
            bool pendingFtnVirtual;
            Primitive pendingConstrained; // constrained. onki: sonraki callvirt kisitli tipe gore dispatch eder
            FieldDefinitionHandle pendingArrayField; // ldtoken <PrivateImpl>.field -> InitializeArray deseni
            int lastNewArrLen; // son newarr uzunlugu (array-literal init byte sayisi icin)
            int curLine;
            bool unreachable;
            GenCtx gc; // bu govdenin generic baglami (sinif + method tip parametreleri)

            // istisna bolgeleri (try/catch): IL EH -> yapisal TryBegin/TryEnd/ExIs/ExBind/Rethrow modeli
            class EhCatch { public Primitive type; public int handlerOff; public Label sec; }
            class EhGroup
            {
                public int tryStart, tryLen;
                public int TryEndOff => tryStart + tryLen;
                public readonly List<EhCatch> catches = new List<EhCatch>();
                public Label dispatch; // TryBegin hedefi (setjmp buraya atlar)
                public bool isFinally;
                public int finStart, finLen;
                public int FinEnd => finStart + finLen;
            }
            readonly List<EhGroup> ehGroups = new List<EhGroup>();
            readonly Dictionary<int, List<EhGroup>> tryBeginAt = new Dictionary<int, List<EhGroup>>();  // TryOffset -> gruplar (dis->ic)
            readonly Dictionary<int, EhGroup> dispatchAt = new Dictionary<int, EhGroup>();        // ilk handler offset -> grup
            readonly Dictionary<int, (EhGroup g, int idx)> handlerAt = new Dictionary<int, (EhGroup, int)>(); // handler offset -> (grup, catch#)
            readonly List<EhGroup> openStack = new List<EhGroup>();                                // CTranspiler openTries yansimasi

            Label LabelAt(int off)
            {
                if (!labels.TryGetValue(off, out var l)) labels[off] = l = new Label();
                return l;
            }

            void Push(Primitive t) => stack.Add(t);
            Primitive Pop()
            {
                var t = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                return t;
            }
            Primitive Top => stack[stack.Count - 1];

            void Emit(OpType t) => EmitOp(new Op { Type = t });
            // Terfi etmis struct (async SM): Apply node ise sablon adina bakilir.
            bool IsPromoted(Primitive t) => t != null && t.Type == PrimitiveType.Model && !t.IsStruct
                && loader.promotedStructs.Contains((t.GenericTemplate ?? t).Name);
            void EmitOp(Op o)
            {
                o.Line = curLine;
                ops.Add(o);
            }

            int NewLocal(Primitive t)
            {
                code.Locals.Add(t);
                code.LocalNames.Add(null);
                return code.Locals.Count - 1;
            }

            void LoadSeqPoints()
            {
                if (pdb == null) return;
                var dih = mh.ToDebugInformationHandle();
                var di = pdb.GetMethodDebugInformation(dih);
                if (di.SequencePointsBlob.IsNil) return;
                foreach (var sp in di.GetSequencePoints())
                {
                    if (sp.IsHidden) continue;
                    seqPoints[sp.Offset] = (sp.StartLine << 10) | (sp.StartColumn & 1023);
                    if (code.SourceFile == null && !sp.Document.IsNil)
                        code.SourceFile = Path.GetFileName(pdb.GetDocument(sp.Document).Name.IsNil ? "" : pdb.GetString(pdb.GetDocument(sp.Document).Name));
                }
            }

            // dal aninda stack'i hedefin spill slotlarina indir (tipler simulasyondan);
            // hedefe ilk gelen slotlari belirler, sonrakiler ayni yerlesimi kullanir
            List<int> SlotsFor(int target)
            {
                if (!mergeSlots.TryGetValue(target, out var slots))
                {
                    slots = new List<int>();
                    foreach (var t in stack) slots.Add(NewLocal(t));
                    mergeSlots[target] = slots;
                }
                return slots;
            }
            void SpillTo(List<int> slots)
            {
                for (int i = slots.Count - 1; i >= 0; i--) // ustten alta
                {
                    // Slot tipi hedefe ILK gelenden alinir; ilk gelen `null` literal (Object) ise sonraki
                    // gelenin somut referans tipi slot'u inceltir (byte[] ?? null gibi `?.`/`??` lowering'leri).
                    var incoming = stack[stack.Count - 1];
                    var slotType = code.Locals[slots[i]];
                    if (slotType == Primitive.Object && incoming != null && incoming != Primitive.Object
                        && (incoming.Type == PrimitiveType.Array || (incoming.Type == PrimitiveType.Model && !incoming.IsStruct)))
                        code.Locals[slots[i]] = incoming;
                    // `ldc.i4.0; conv.u` null-pointer deyimi ilk gelirse slot tam sayi kalir; sonraki gelen
                    // gercek pointer slot'u pointer yapar (src?._pixels == null lowering'i). 0 atamasi C'de gecerli.
                    else if (incoming != null && incoming.Type == PrimitiveType.Pointer && slotType != null
                        && slotType.Type != PrimitiveType.Pointer && (slotType == Primitive.Int || slotType == Primitive.UInt || slotType == Primitive.Long || slotType == Primitive.ULong))
                        code.Locals[slots[i]] = incoming;
                    EmitOp(new Op { Type = OpType.SetLocal, Slot = slots[i] });
                    Pop();
                }
            }
            void ReloadFrom(List<int> slots)
            {
                foreach (var s in slots)
                {
                    EmitOp(new Op { Type = OpType.GetLocal, Slot = s });
                    Push(code.Locals[s]);
                }
            }

            void BuildEh(MethodBodyBlock body)
            {
                var byRange = new Dictionary<(int, int), EhGroup>();
                foreach (var r in body.ExceptionRegions)
                {
                    if (r.Kind != ExceptionRegionKind.Catch && r.Kind != ExceptionRegionKind.Finally)
                        throw new Exception($"CIL: filter/fault EH dilim 1c: {code.EncodeName()}");
                    var key = (r.TryOffset, r.TryLength);
                    if (!byRange.TryGetValue(key, out var g))
                    {
                        g = new EhGroup { tryStart = r.TryOffset, tryLen = r.TryLength, dispatch = new Label() };
                        byRange[key] = g;
                        ehGroups.Add(g);
                    }
                    if (r.Kind == ExceptionRegionKind.Finally)
                    {
                        g.isFinally = true;
                        g.finStart = r.HandlerOffset;
                        g.finLen = r.HandlerLength;
                    }
                    else
                        g.catches.Add(new EhCatch { type = ResolveTypeHandle(r.CatchType), handlerOff = r.HandlerOffset, sec = new Label() });
                }
                foreach (var g in ehGroups)
                {
                    if (g.isFinally)
                    {
                        // try/catch/finally: ayni grupta hem catch hem finally olamaz (IL farkli try-araligi verir) -> ayri gruplar
                        dispatchAt[g.finStart] = g;
                        AddTryBegin(g);
                        for (int off = g.finStart; off < g.FinEnd; off++) finallySkip.Add(off);
                        continue;
                    }
                    g.catches.Sort((a, b) => a.handlerOff.CompareTo(b.handlerOff));
                    AddTryBegin(g);
                    dispatchAt[g.catches[0].handlerOff] = g;
                    for (int i = 0; i < g.catches.Count; i++) handlerAt[g.catches[i].handlerOff] = (g, i);
                }
                // ayni offset'te birden fazla try baslarsa dis (buyuk aralik) once acilir
                foreach (var lst in tryBeginAt.Values)
                    lst.Sort((a, b) => b.tryLen.CompareTo(a.tryLen));
            }

            void AddTryBegin(EhGroup g)
            {
                if (!tryBeginAt.TryGetValue(g.tryStart, out var lst))
                    tryBeginAt[g.tryStart] = lst = new List<EhGroup>();
                lst.Add(g);
            }

            Primitive ResolveTypeHandle(EntityHandle h)
            {
                if (h.IsNil) return loader.ResolveName("System.Exception");
                if (h.Kind == HandleKind.TypeDefinition) return loader.prims[(TypeDefinitionHandle)h];
                if (h.Kind == HandleKind.TypeReference) return loader.ResolveName(loader.RefName(h));
                if (h.Kind == HandleKind.TypeSpecification) return loader.DecodeTypeSpecHandle((TypeSpecificationHandle)h, gc);
                throw new Exception("CIL: beklenmeyen catch tip handle: " + h.Kind);
            }

            // IL leave: try/handler bolgesinden cikis. Cikilan her try icin yapisal TryEnd (lexical son) ya da
            // erken cikista TryUnwind (runtime zinciri geri sar), sonra hedefe Br. leave stack'i temizler.
            void DoLeave(int at, int nextOff, int target)
            {
                stack.Clear();
                var exited = ehGroups
                    .Where(g => g.tryStart <= at && at < g.TryEndOff && !(g.tryStart <= target && target < g.TryEndOff))
                    .OrderByDescending(g => g.tryStart).ToList();
                foreach (var g in exited)
                {
                    if (nextOff == g.TryEndOff && openStack.Count > 0 && openStack[openStack.Count - 1] == g)
                    {
                        EmitOp(new Op { Type = OpType.TryEnd });
                        openStack.RemoveAt(openStack.Count - 1);
                    }
                    else
                    {
                        int idx = openStack.LastIndexOf(g);
                        EmitOp(new Op { Type = OpType.TryUnwind, Slot = openStack.Count - idx });
                    }
                    if (g.isFinally) EmitFinallyInline(g); // normal cikis: finally govdesi
                }
                EmitOp(new Op { Type = OpType.Br, Label = LabelAt(target) });
                unreachable = true;
            }

            // finally govdesini [finStart,FinEnd) yeniden decode ederek satir-ici emit et (normal ve istisna yolunda ayri kopya).
            // Taze label/mergeSlots ad-alani: her kopya kendi Label nesnelerini alir.
            void EmitFinallyInline(EhGroup g)
            {
                var savedLabels = labels;
                var savedMerge = mergeSlots;
                var savedStack = new List<Primitive>(stack);
                labels = new Dictionary<int, Label>();
                mergeSlots = new Dictionary<int, List<int>>();
                stack.Clear();
                CollectTargetsRange(g.finStart, g.FinEnd);
                bool savedUnreach = unreachable;
                unreachable = false;
                int p = g.finStart;
                while (p < g.FinEnd)
                {
                    int at = p;
                    if (seqPoints.TryGetValue(at, out var pk)) curLine = pk;
                    if (labels.TryGetValue(at, out var lab))
                    {
                        if (!unreachable && stack.Count > 0) SpillTo(SlotsFor(at));
                        EmitOp(new Op { Type = OpType.Label, Label = lab });
                        stack.Clear();
                        if (mergeSlots.TryGetValue(at, out var slots)) ReloadFrom(slots);
                        unreachable = false;
                    }
                    var (op, sz) = ReadOp(ilBytes, p);
                    p += sz;
                    if (op == ILOpCode.Endfinally) break;
                    if (unreachable) { p += BranchAwareOperandSize(op, ilBytes, ref p); continue; }
                    p = Exec(op, ilBytes, p, at);
                }
                labels = savedLabels;
                mergeSlots = savedMerge;
                stack.Clear();
                stack.AddRange(savedStack);
                unreachable = savedUnreach;
            }

            void CollectTargetsRange(int start, int end)
            {
                int p = start;
                while (p < end)
                {
                    var (op, sz) = ReadOp(ilBytes, p);
                    p += sz;
                    switch (op)
                    {
                        case ILOpCode.Br_s:
                        case ILOpCode.Brtrue_s:
                        case ILOpCode.Brfalse_s:
                        case ILOpCode.Beq_s:
                        case ILOpCode.Bge_s:
                        case ILOpCode.Bgt_s:
                        case ILOpCode.Ble_s:
                        case ILOpCode.Blt_s:
                        case ILOpCode.Bne_un_s:
                        case ILOpCode.Bge_un_s:
                        case ILOpCode.Bgt_un_s:
                        case ILOpCode.Ble_un_s:
                        case ILOpCode.Blt_un_s:
                        case ILOpCode.Leave_s:
                            LabelAt(p + 1 + (sbyte)ilBytes[p]); p += 1; break;
                        case ILOpCode.Br:
                        case ILOpCode.Brtrue:
                        case ILOpCode.Brfalse:
                        case ILOpCode.Beq:
                        case ILOpCode.Bge:
                        case ILOpCode.Bgt:
                        case ILOpCode.Ble:
                        case ILOpCode.Blt:
                        case ILOpCode.Bne_un:
                        case ILOpCode.Bge_un:
                        case ILOpCode.Bgt_un:
                        case ILOpCode.Ble_un:
                        case ILOpCode.Blt_un:
                        case ILOpCode.Leave:
                            LabelAt(p + 4 + BitConverter.ToInt32(ilBytes, p)); p += 4; break;
                        case ILOpCode.Switch:
                            {
                                int n = BitConverter.ToInt32(ilBytes, p);
                                int e = p + 4 + n * 4;
                                for (int i = 0; i < n; i++) LabelAt(e + BitConverter.ToInt32(ilBytes, p + 4 + i * 4));
                                p = e; break;
                            }
                        default: p += OperandSize(op, ilBytes, p); break;
                    }
                }
            }

            public void Translate(MethodBodyBlock body)
            {
                gc = new GenCtx
                {
                    TypeParams = code.Owner != null ? code.Owner.GenericParameters : null,
                    MethodParams = code.GenericParameters.Count > 0 ? code.GenericParameters : null
                };
                if (body.ExceptionRegions.Length > 0)
                    BuildEh(body);
                loader.DecodeStandaloneLocals(body.LocalSignature, code.Locals, gc);
                for (int i = 0; i < code.Locals.Count; i++) code.LocalNames.Add(null);
                LoadSeqPoints();

                // Class'a terfi etmis struct (async SM) local'leri: IL `ldloca sm; ... Start(ref sm)` ile
                // stack'teki struct'i doldurur; biz referans tasidigimizdan nesne method girisinde ayrilir.
                for (int i = 0; i < code.Locals.Count; i++)
                    if (IsPromoted(code.Locals[i]))
                    {
                        EmitOp(new Op { Type = OpType.New, PrimitiveRef = code.Locals[i], TypeArguments = new List<Primitive>(code.Locals[i].TypeArguments) });
                        EmitOp(new Op { Type = OpType.SetLocal, Slot = i });
                    }

                var il = body.GetILBytes();
                ilBytes = il;
                CollectTargets(il);
                Decode(il);
                code.Operations = ops; // Label cozumu Resolver.LinkLabels'ta (MiniCs ile ayni yol)
                var present = new HashSet<Label>();
                foreach (var o in ops) if (o.Type == OpType.Label) present.Add(o.Label);
                if (System.Environment.GetEnvironmentVariable("DUMPOPS") == code.EncodeName())
                {
                    var lines = new List<string>();
                    foreach (var o in ops)
                    {
                        string labOff = o.Label != null ? (labels.FirstOrDefault(kv => kv.Value == o.Label).Key.ToString("X4")) : "";
                        lines.Add($"{o.Type} L={labOff} slot={o.Slot} prim={o.PrimitiveRef?.Name}");
                    }
                    File.WriteAllText("obj/dump-ops.txt", string.Join("\n", lines));
                }
                foreach (var o in ops)
                    if ((o.Type == OpType.Br || o.Type == OpType.Brtrue || o.Type == OpType.Brfalse || o.Type == OpType.TryBegin) && o.Label != null && !present.Contains(o.Label))
                    {
                        var off = labels.FirstOrDefault(kv2 => kv2.Value == o.Label).Key;
                        throw new Exception($"CIL: cozulmeyen dal hedefi: {code.EncodeName()} IL_{off:X4}");
                    }
            }

            // 1. gecis: dal hedeflerini topla (Label op'lari icin)
            void CollectTargets(byte[] il)
            {
                int p = 0;
                while (p < il.Length)
                {
                    var (op, sz) = ReadOp(il, p);
                    p += sz;
                    switch (op)
                    {
                        case ILOpCode.Br_s:
                        case ILOpCode.Brtrue_s:
                        case ILOpCode.Brfalse_s:
                        case ILOpCode.Beq_s:
                        case ILOpCode.Bge_s:
                        case ILOpCode.Bgt_s:
                        case ILOpCode.Ble_s:
                        case ILOpCode.Blt_s:
                        case ILOpCode.Bne_un_s:
                        case ILOpCode.Bge_un_s:
                        case ILOpCode.Bgt_un_s:
                        case ILOpCode.Ble_un_s:
                        case ILOpCode.Blt_un_s:
                        case ILOpCode.Leave_s:
                            LabelAt(p + 1 + (sbyte)il[p]); p += 1; break;
                        case ILOpCode.Br:
                        case ILOpCode.Brtrue:
                        case ILOpCode.Brfalse:
                        case ILOpCode.Beq:
                        case ILOpCode.Bge:
                        case ILOpCode.Bgt:
                        case ILOpCode.Ble:
                        case ILOpCode.Blt:
                        case ILOpCode.Bne_un:
                        case ILOpCode.Bge_un:
                        case ILOpCode.Bgt_un:
                        case ILOpCode.Ble_un:
                        case ILOpCode.Blt_un:
                        case ILOpCode.Leave:
                            LabelAt(p + 4 + BitConverter.ToInt32(il, p)); p += 4; break;
                        case ILOpCode.Switch:
                            {
                                int n = BitConverter.ToInt32(il, p);
                                int end = p + 4 + n * 4;
                                for (int i = 0; i < n; i++) LabelAt(end + BitConverter.ToInt32(il, p + 4 + i * 4));
                                p = end;
                                break;
                            }
                        default:
                            p += OperandSize(op, il, p);
                            break;
                    }
                }
            }

            static (ILOpCode, int) ReadOp(byte[] il, int p)
            {
                byte b = il[p];
                return b == 0xFE ? ((ILOpCode)(0xFE00 | il[p + 1]), 2) : ((ILOpCode)b, 1);
            }

            // operand uzunlugu (dallar haric - onlari cagiran isler)
            static int OperandSize(ILOpCode op, byte[] il, int p) => op switch
            {
                ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s
                    or ILOpCode.Stloc_s or ILOpCode.Ldc_i4_s => 1,
                ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => 2,
                ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj
                    or ILOpCode.Ldstr or ILOpCode.Ldfld or ILOpCode.Stfld or ILOpCode.Ldsfld or ILOpCode.Stsfld
                    or ILOpCode.Ldflda or ILOpCode.Ldsflda or ILOpCode.Newarr or ILOpCode.Box or ILOpCode.Unbox_any or ILOpCode.Unbox
                    or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
                    or ILOpCode.Ldelem or ILOpCode.Stelem or ILOpCode.Ldelema or ILOpCode.Initobj or ILOpCode.Ldobj or ILOpCode.Stobj
                    or ILOpCode.Constrained or ILOpCode.Sizeof or ILOpCode.Jmp or ILOpCode.Calli => 4,
                ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => 8,
                _ => 0
            };

            void Decode(byte[] il)
            {
                int p = 0;
                while (p < il.Length)
                {
                    int at = p;
                    if (seqPoints.TryGetValue(at, out var pk)) curLine = pk;
                    if (dispatchAt.TryGetValue(at, out var dg) && dg.isFinally) // finally handler: govde + Rethrow, sonra bolgeyi atla
                    {
                        stack.Clear();
                        EmitOp(new Op { Type = OpType.Label, Label = dg.dispatch });
                        EmitFinallyInline(dg);                      // istisna yolu: finally govdesi
                        EmitOp(new Op { Type = OpType.Rethrow });
                        p = dg.FinEnd;                              // finally IL'i satir-ici emit edildi; ana dongude atla
                        unreachable = true;
                        continue;
                    }
                    if (dispatchAt.TryGetValue(at, out dg)) // ilk handler girisi: setjmp dispatch + tip zinciri
                    {
                        stack.Clear();
                        EmitOp(new Op { Type = OpType.Label, Label = dg.dispatch });
                        foreach (var cc in dg.catches)
                        {
                            EmitOp(new Op { Type = OpType.ExIs, PrimitiveRef = cc.type });
                            EmitOp(new Op { Type = OpType.Brtrue, Label = cc.sec });
                        }
                        EmitOp(new Op { Type = OpType.Rethrow }); // hicbir catch uymadi -> dis try'a devret
                        var first = dg.catches[0];               // ilk handler govdesine dus
                        EmitOp(new Op { Type = OpType.Label, Label = first.sec });
                        EmitOp(new Op { Type = OpType.ExBind, PrimitiveRef = first.type });
                        Push(first.type);
                        unreachable = false;
                    }
                    else if (handlerAt.TryGetValue(at, out var hh)) // sonraki catch handler girisi
                    {
                        stack.Clear();
                        var cc = hh.g.catches[hh.idx];
                        EmitOp(new Op { Type = OpType.Label, Label = cc.sec });
                        EmitOp(new Op { Type = OpType.ExBind, PrimitiveRef = cc.type });
                        Push(cc.type);
                        unreachable = false;
                    }
                    else if (labels.TryGetValue(at, out var lab)) // dal hedefi: stack sozlesmesi slotlardan yuklenir
                    {
                        if (!unreachable && stack.Count > 0) SpillTo(SlotsFor(at)); // dusen yol da ayni yerlesime iner
                        EmitOp(new Op { Type = OpType.Label, Label = lab });
                        stack.Clear();
                        if (mergeSlots.TryGetValue(at, out var slots)) ReloadFrom(slots);
                        unreachable = false;
                    }
                    if (tryBeginAt.TryGetValue(at, out var tgs)) // try govde basi: TryBegin (dis->ic sirali)
                    {
                        foreach (var tg in tgs)
                        {
                            var handlerType = tg.isFinally ? loader.ResolveName("System.Exception") : tg.catches[0].type;
                            EmitOp(new Op { Type = OpType.TryBegin, PrimitiveRef = handlerType, Label = tg.dispatch });
                            openStack.Add(tg);
                        }
                    }
                    var (op, sz) = ReadOp(il, p);
                    p += sz;
                    if (unreachable) { p += BranchAwareOperandSize(op, il, ref p); continue; } // olu kod atla
                    p = Exec(op, il, p, at);
                }
            }

            int BranchAwareOperandSize(ILOpCode op, byte[] il, ref int p)
            {
                switch (op)
                {
                    case ILOpCode.Br_s:
                    case ILOpCode.Brtrue_s:
                    case ILOpCode.Brfalse_s:
                    case ILOpCode.Beq_s:
                    case ILOpCode.Bge_s:
                    case ILOpCode.Bgt_s:
                    case ILOpCode.Ble_s:
                    case ILOpCode.Blt_s:
                    case ILOpCode.Bne_un_s:
                    case ILOpCode.Bge_un_s:
                    case ILOpCode.Bgt_un_s:
                    case ILOpCode.Ble_un_s:
                    case ILOpCode.Blt_un_s:
                    case ILOpCode.Leave_s: return 1;
                    case ILOpCode.Br:
                    case ILOpCode.Brtrue:
                    case ILOpCode.Brfalse:
                    case ILOpCode.Beq:
                    case ILOpCode.Bge:
                    case ILOpCode.Bgt:
                    case ILOpCode.Ble:
                    case ILOpCode.Blt:
                    case ILOpCode.Bne_un:
                    case ILOpCode.Bge_un:
                    case ILOpCode.Bgt_un:
                    case ILOpCode.Ble_un:
                    case ILOpCode.Blt_un:
                    case ILOpCode.Leave: return 4;
                    case ILOpCode.Switch: return 4 + BitConverter.ToInt32(il, p) * 4;
                    default: return OperandSize(op, il, p);
                }
            }

            EntityHandle Tok(byte[] il, int p) => MetadataTokens.EntityHandle(BitConverter.ToInt32(il, p));

            void Branch(int target)
            {
                if (stack.Count > 0) // hedefe deger tasiniyor: slotlara indir, dusen yol geri yukler
                {
                    var slots = SlotsFor(target);
                    var saved = new List<Primitive>(stack);
                    SpillTo(slots);
                    EmitOp(new Op { Type = OpType.Br, Label = LabelAt(target) });
                    // NOT: kosulsuz dalda geri yukleme olmaz; cagiran unreachable isaretler
                    stack.AddRange(saved); // kosullu dal icin: cagiran gerekirse temizler
                    for (int i = 0; i < slots.Count; i++) { } // yerlesim kaydedildi
                    return;
                }
                EmitOp(new Op { Type = OpType.Br, Label = LabelAt(target) });
            }

            void CondBranch(int target)
            {
                var cond = Pop(); // kosul degeri lazy CVal olarak Brtrue'ya gider
                if (stack.Count > 0)
                {
                    // kosul ALTINDA kalan degerler: hedef + dusen yol ayni slotlari kullanir
                    var condSlot = NewLocal(cond);
                    EmitOp(new Op { Type = OpType.SetLocal, Slot = condSlot });
                    var slots = SlotsFor(target);
                    SpillTo(slots);
                    EmitOp(new Op { Type = OpType.GetLocal, Slot = condSlot });
                    EmitOp(new Op { Type = OpType.Brtrue, Label = LabelAt(target) });
                    ReloadFrom(slots);
                    return;
                }
                EmitOp(new Op { Type = OpType.Brtrue, Label = LabelAt(target) });
            }

            void Cmp(OpType t)
            {
                // IL ceq/cgt/clt operandlari 32-bit HAM; C# uint literali `ldc.i4` ile isaretli gelir
                // (4000000000u -> 0xEE6B2800 = -294967296). Karsi operand unsigned ise sabiti unsigned
                // bit desenine yeniden tiple; yoksa transpiler long'a terfi edip isaret genisletir (yanlis).
                if (stack.Count >= 2)
                {
                    var a = stack[stack.Count - 2];
                    var b = stack[stack.Count - 1];
                    if (IsUnsignedInt(a) && b == Primitive.Int) RetypeConstOperand(ops.Count - 1, a);
                    else if (IsUnsignedInt(b) && a == Primitive.Int) RetypeConstOperand(ops.Count - 2, b);
                }
                Pop(); Pop();
                Emit(t);
                Push(Primitive.Int);
            }
            static bool IsUnsignedInt(Primitive t) => t != null &&
                (t.Type == PrimitiveType.UInt || t.Type == PrimitiveType.UShort || t.Type == PrimitiveType.Byte);
            // Type + Reflection member wrapper'lari kimlik-cache'li (ayni oge = ayni nesne); == / !=
            // MemberInfo operator'leri referans karsilastirmasiyla dogru sonuc verir.
            static bool IsIdentityRefType(string owner) =>
                owner == "System.Type" || owner == "System.Reflection.MemberInfo" ||
                owner == "System.Reflection.FieldInfo" || owner == "System.Reflection.PropertyInfo" ||
                owner == "System.Reflection.MethodInfo"; void RetypeConstOperand(int opIndex, Primitive target)
            {
                if (opIndex < 0 || opIndex >= ops.Count) return;
                var o = ops[opIndex];
                if (o.Type != OpType.Push || o.Value is not int iv) return;
                o.Value = target.Type switch
                {
                    PrimitiveType.UInt => (object)unchecked((uint)iv),
                    PrimitiveType.UShort => (object)unchecked((ushort)iv),
                    PrimitiveType.Byte => (object)unchecked((byte)iv),
                    _ => o.Value
                };
            }
            void Bin()
            {
                var b = Pop(); var a = Pop();
                // unsafe isaretci aritmetigi (p + n, p - n): sonuc isaretci tipi (spill slot'u int'e dusmesin)
                if (a?.Type == PrimitiveType.Pointer) { Push(a); return; }
                if (b?.Type == PrimitiveType.Pointer) { Push(b); return; }
                Push(Primitive.PromoteNumeric(a, b));
            }

            int Exec(ILOpCode op, byte[] il, int p, int at)
            {
                switch (op)
                {
                    case ILOpCode.Nop: case ILOpCode.Break: return p;
                    case ILOpCode.Ldarg_0:
                    case ILOpCode.Ldarg_1:
                    case ILOpCode.Ldarg_2:
                    case ILOpCode.Ldarg_3:
                        {
                            int i = op - ILOpCode.Ldarg_0;
                            EmitOp(new Op { Type = OpType.GetArg, Slot = i });
                            Push(code.Arguments[i].Type);
                            return p;
                        }
                    case ILOpCode.Ldarg_s: EmitOp(new Op { Type = OpType.GetArg, Slot = il[p] }); Push(code.Arguments[il[p]].Type); return p + 1;
                    case ILOpCode.Ldarg: { int i = BitConverter.ToUInt16(il, p); EmitOp(new Op { Type = OpType.GetArg, Slot = i }); Push(code.Arguments[i].Type); return p + 2; }
                    case ILOpCode.Starg_s: EmitOp(new Op { Type = OpType.SetArg, Slot = il[p] }); Pop(); return p + 1;
                    case ILOpCode.Starg: EmitOp(new Op { Type = OpType.SetArg, Slot = BitConverter.ToUInt16(il, p) }); Pop(); return p + 2;
                    case ILOpCode.Ldloc_0:
                    case ILOpCode.Ldloc_1:
                    case ILOpCode.Ldloc_2:
                    case ILOpCode.Ldloc_3:
                        {
                            int i = op - ILOpCode.Ldloc_0;
                            EmitOp(new Op { Type = OpType.GetLocal, Slot = i });
                            Push(code.Locals[i]);
                            return p;
                        }
                    case ILOpCode.Ldloc_s: EmitOp(new Op { Type = OpType.GetLocal, Slot = il[p] }); Push(code.Locals[il[p]]); return p + 1;
                    case ILOpCode.Ldloc: { int i = BitConverter.ToUInt16(il, p); EmitOp(new Op { Type = OpType.GetLocal, Slot = i }); Push(code.Locals[i]); return p + 2; }
                    case ILOpCode.Stloc_0:
                    case ILOpCode.Stloc_1:
                    case ILOpCode.Stloc_2:
                    case ILOpCode.Stloc_3:
                        EmitOp(new Op { Type = OpType.SetLocal, Slot = op - ILOpCode.Stloc_0 });
                        Pop();
                        return p;
                    case ILOpCode.Stloc_s: EmitOp(new Op { Type = OpType.SetLocal, Slot = il[p] }); Pop(); return p + 1;
                    case ILOpCode.Stloc: EmitOp(new Op { Type = OpType.SetLocal, Slot = BitConverter.ToUInt16(il, p) }); Pop(); return p + 2;

                    // adres yukleme (struct alici / ref-out / initobj): AddrLocal/AddrArg pointer push eder
                    case ILOpCode.Ldloca_s:
                    case ILOpCode.Ldloca:
                        {
                            int i = op == ILOpCode.Ldloca_s ? il[p] : BitConverter.ToUInt16(il, p);
                            int sz = op == ILOpCode.Ldloca_s ? 1 : 2;
                            if (IsPromoted(code.Locals[i]))
                            {
                                // terfi etmis SM: "adres" = nesnenin kendisi (ldflda/call this olarak kullanilir)
                                EmitOp(new Op { Type = OpType.GetLocal, Slot = i });
                                Push(code.Locals[i]);
                                return p + sz;
                            }
                            EmitOp(new Op { Type = OpType.AddrLocal, Slot = i });
                            Push(Primitive.PointerOf(code.Locals[i]));
                            return p + sz;
                        }
                    case ILOpCode.Ldarga_s: EmitOp(new Op { Type = OpType.AddrArg, Slot = il[p] }); Push(Primitive.PointerOf(code.Arguments[il[p]].Type)); return p + 1;
                    case ILOpCode.Ldarga: { int i = BitConverter.ToUInt16(il, p); EmitOp(new Op { Type = OpType.AddrArg, Slot = i }); Push(Primitive.PointerOf(code.Arguments[i].Type)); return p + 2; }
                    case ILOpCode.Ldflda:
                        {
                            var f = loader.ResolveField(Tok(il, p), gc, out var fta);
                            Pop();
                            EmitOp(new Op { Type = OpType.AddrField, Field = f, TypeArguments = fta });
                            var ft = loader.FieldTypeConcrete(f, fta);
                            // fixed buffer alani (FixedArray): adres = ilk eleman pointer'i (float*), IL byte aritmetigi bekler
                            Push(Primitive.PointerOf(ft.Type == PrimitiveType.FixedArray ? ft.ElementType : ft));
                            return p + 4;
                        }
                    case ILOpCode.Ldsflda:
                        {
                            var f = loader.ResolveField(Tok(il, p), gc, out var fta);
                            EmitOp(new Op { Type = OpType.AddrStatic, Field = f, TypeArguments = fta });
                            Push(Primitive.PointerOf(loader.FieldTypeConcrete(f, fta)));
                            return p + 4;
                        }
                    case ILOpCode.Initobj:
                        {
                            var t = DecodeTypeTok(il, p);
                            if (IsPromoted(t))
                            {
                                Emit(OpType.Pop); Pop(); // nesne girişte ayrildi, alanlar zaten sifir
                                return p + 4;
                            }
                            EmitOp(new Op { Type = OpType.Default, PrimitiveRef = t }); // adres zaten stack'te: *adr = default(T)
                            Push(t);
                            EmitOp(new Op { Type = OpType.StoreInd });
                            Pop(); Pop(); // deger + adres
                            return p + 4;
                        }
                    case ILOpCode.Ldobj:
                        {
                            var t = DecodeTypeTok(il, p);
                            Pop();
                            EmitOp(new Op { Type = OpType.LoadInd });
                            Push(t);
                            return p + 4;
                        }
                    case ILOpCode.Stobj:
                        {
                            DecodeTypeTok(il, p);
                            Pop(); Pop();
                            EmitOp(new Op { Type = OpType.StoreInd });
                            return p + 4;
                        }
                    case ILOpCode.Ldind_i1:
                    case ILOpCode.Ldind_u1:
                    case ILOpCode.Ldind_i2:
                    case ILOpCode.Ldind_u2:
                    case ILOpCode.Ldind_i4:
                    case ILOpCode.Ldind_u4:
                    case ILOpCode.Ldind_i8:
                    case ILOpCode.Ldind_i:
                    case ILOpCode.Ldind_r4:
                    case ILOpCode.Ldind_r8:
                    case ILOpCode.Ldind_ref:
                        {
                            var addr = Pop();
                            // IL ldind.X HER ZAMAN X tipiyle okur: C# pointer cast'i ((float*)p) IL'de iz birakmaz, stack'teki
                            // isaretci tipi (byte*) yaniltici. Yalniz ldind.ref/ldind.i (tipsiz) isaretcinin kendi tipine guvenir.
                            var it = IndType(op);
                            bool useIl = op != ILOpCode.Ldind_ref && op != ILOpCode.Ldind_i || addr.ElementType == null || addr.ElementType == Primitive.Void;
                            EmitOp(new Op { Type = OpType.LoadInd, PrimitiveRef = useIl ? it : null });
                            Push(useIl ? it : addr.ElementType);
                            return p;
                        }
                    case ILOpCode.Stind_i1:
                    case ILOpCode.Stind_i2:
                    case ILOpCode.Stind_i4:
                    case ILOpCode.Stind_i8:
                    case ILOpCode.Stind_r4:
                    case ILOpCode.Stind_r8:
                    case ILOpCode.Stind_ref:
                    case ILOpCode.Stind_i:
                        {
                            Pop(); var addr2 = Pop();
                            bool useIl = op != ILOpCode.Stind_ref && op != ILOpCode.Stind_i || addr2.ElementType == null || addr2.ElementType == Primitive.Void;
                            EmitOp(new Op { Type = OpType.StoreInd, PrimitiveRef = useIl ? IndType(op) : null });
                            return p;
                        }

                    case ILOpCode.Ldc_i4_m1:
                    case ILOpCode.Ldc_i4_0:
                    case ILOpCode.Ldc_i4_1:
                    case ILOpCode.Ldc_i4_2:
                    case ILOpCode.Ldc_i4_3:
                    case ILOpCode.Ldc_i4_4:
                    case ILOpCode.Ldc_i4_5:
                    case ILOpCode.Ldc_i4_6:
                    case ILOpCode.Ldc_i4_7:
                    case ILOpCode.Ldc_i4_8:
                        EmitOp(new Op { Type = OpType.Push, Value = (int)op - (int)ILOpCode.Ldc_i4_0 }); // ushort enum farki -1'i 65535 yapar: once int'e cevir
                        Push(Primitive.Int);
                        return p;
                    case ILOpCode.Ldc_i4_s: EmitOp(new Op { Type = OpType.Push, Value = (int)(sbyte)il[p] }); Push(Primitive.Int); return p + 1;
                    case ILOpCode.Ldc_i4: EmitOp(new Op { Type = OpType.Push, Value = BitConverter.ToInt32(il, p) }); Push(Primitive.Int); return p + 4;
                    case ILOpCode.Ldc_i8: EmitOp(new Op { Type = OpType.Push, Value = BitConverter.ToInt64(il, p) }); Push(Primitive.Long); return p + 8;
                    case ILOpCode.Ldc_r4: EmitOp(new Op { Type = OpType.Push, Value = BitConverter.ToSingle(il, p) }); Push(Primitive.Float); return p + 4;
                    case ILOpCode.Ldc_r8: EmitOp(new Op { Type = OpType.Push, Value = BitConverter.ToDouble(il, p) }); Push(Primitive.Double); return p + 8;
                    case ILOpCode.Ldstr:
                        EmitOp(new Op { Type = OpType.Push, Value = md.GetUserString(MetadataTokens.UserStringHandle(BitConverter.ToInt32(il, p))) });
                        Push(Primitive.String);
                        return p + 4;
                    case ILOpCode.Ldnull: EmitOp(new Op { Type = OpType.Push, Value = null }); Push(Primitive.Object); return p;

                    case ILOpCode.Dup: Emit(OpType.Dup); Push(Top); return p;
                    case ILOpCode.Pop: Emit(OpType.Pop); Pop(); return p;

                    case ILOpCode.Add: Emit(OpType.Add); Bin(); return p;
                    case ILOpCode.Sub: Emit(OpType.Sub); Bin(); return p;
                    case ILOpCode.Mul: Emit(OpType.Mul); Bin(); return p;
                    case ILOpCode.Div: case ILOpCode.Div_un: Emit(OpType.Div); Bin(); return p;
                    case ILOpCode.Rem: case ILOpCode.Rem_un: Emit(OpType.Mod); Bin(); return p;
                    case ILOpCode.And: Emit(OpType.And); Bin(); return p;
                    case ILOpCode.Or: Emit(OpType.Or); Bin(); return p;
                    case ILOpCode.Xor: Emit(OpType.Xor); Bin(); return p;
                    case ILOpCode.Shl: Emit(OpType.Shl); Pop(); return p; // sonuc = sol operandin tipi (sim: pop shift miktari)
                    case ILOpCode.Shr: case ILOpCode.Shr_un: Emit(OpType.Shr); Pop(); return p;
                    case ILOpCode.Neg: Emit(OpType.Neg); return p;
                    case ILOpCode.Not: Emit(OpType.Not); return p;

                    case ILOpCode.Ceq: Cmp(OpType.Ceq); return p;
                    case ILOpCode.Cgt: case ILOpCode.Cgt_un: Cmp(OpType.Cgt); return p;
                    case ILOpCode.Clt: case ILOpCode.Clt_un: Cmp(OpType.Clt); return p;

                    case ILOpCode.Br_s: { int t = p + 1 + (sbyte)il[p]; Branch(t); stack.Clear(); unreachable = true; return p + 1; }
                    case ILOpCode.Br: { int t = p + 4 + BitConverter.ToInt32(il, p); Branch(t); stack.Clear(); unreachable = true; return p + 4; }
                    case ILOpCode.Leave_s: DoLeave(at, p + 1, p + 1 + (sbyte)il[p]); return p + 1;
                    case ILOpCode.Leave: DoLeave(at, p + 4, p + 4 + BitConverter.ToInt32(il, p)); return p + 4;
                    case ILOpCode.Rethrow: Emit(OpType.Rethrow); stack.Clear(); unreachable = true; return p;
                    case ILOpCode.Endfinally: stack.Clear(); unreachable = true; return p; // finally stub'landi
                    case ILOpCode.Brtrue_s: { int t = p + 1 + (sbyte)il[p]; CondBranch(t); return p + 1; }
                    case ILOpCode.Brtrue: { int t = p + 4 + BitConverter.ToInt32(il, p); CondBranch(t); return p + 4; }
                    case ILOpCode.Brfalse_s: { int t = p + 1 + (sbyte)il[p]; EmitOp(new Op { Type = OpType.Push, Value = 0 }); Push(Primitive.Int); Cmp(OpType.Ceq); CondBranch(t); return p + 1; }
                    case ILOpCode.Brfalse: { int t = p + 4 + BitConverter.ToInt32(il, p); EmitOp(new Op { Type = OpType.Push, Value = 0 }); Push(Primitive.Int); Cmp(OpType.Ceq); CondBranch(t); return p + 4; }

                    case ILOpCode.Beq_s: return MacroBranch(OpType.Ceq, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Beq: return MacroBranch(OpType.Ceq, p + 4 + BitConverter.ToInt32(il, p), p + 4);
                    case ILOpCode.Bne_un_s: return MacroBranch(OpType.Cne, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Bne_un: return MacroBranch(OpType.Cne, p + 4 + BitConverter.ToInt32(il, p), p + 4);
                    case ILOpCode.Bge_s: case ILOpCode.Bge_un_s: return MacroBranch(OpType.Cge, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Bge: case ILOpCode.Bge_un: return MacroBranch(OpType.Cge, p + 4 + BitConverter.ToInt32(il, p), p + 4);
                    case ILOpCode.Bgt_s: case ILOpCode.Bgt_un_s: return MacroBranch(OpType.Cgt, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Bgt: case ILOpCode.Bgt_un: return MacroBranch(OpType.Cgt, p + 4 + BitConverter.ToInt32(il, p), p + 4);
                    case ILOpCode.Ble_s: case ILOpCode.Ble_un_s: return MacroBranch(OpType.Cle, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Ble: case ILOpCode.Ble_un: return MacroBranch(OpType.Cle, p + 4 + BitConverter.ToInt32(il, p), p + 4);
                    case ILOpCode.Blt_s: case ILOpCode.Blt_un_s: return MacroBranch(OpType.Clt, p + 1 + (sbyte)il[p], p + 1);
                    case ILOpCode.Blt: case ILOpCode.Blt_un: return MacroBranch(OpType.Clt, p + 4 + BitConverter.ToInt32(il, p), p + 4);

                    case ILOpCode.Switch:
                        {
                            int n = BitConverter.ToInt32(il, p);
                            int end = p + 4 + n * 4;
                            int tmp = NewLocal(Pop());
                            EmitOp(new Op { Type = OpType.SetLocal, Slot = tmp });
                            for (int i = 0; i < n; i++)
                            {
                                int t = end + BitConverter.ToInt32(il, p + 4 + i * 4);
                                EmitOp(new Op { Type = OpType.GetLocal, Slot = tmp });
                                EmitOp(new Op { Type = OpType.Push, Value = i });
                                Emit(OpType.Ceq);
                                EmitOp(new Op { Type = OpType.Brtrue, Label = LabelAt(t) });
                            }
                            return end;
                        }

                    case ILOpCode.Conv_i1: return Conv(Primitive.SByte, p);
                    case ILOpCode.Conv_i2: return Conv(Primitive.Short, p);
                    case ILOpCode.Conv_i4: return Conv(Primitive.Int, p);
                    case ILOpCode.Conv_i8: return Conv(Primitive.Long, p);
                    case ILOpCode.Conv_u1: return Conv(Primitive.Byte, p);
                    case ILOpCode.Conv_u2: return Conv(Primitive.UShort, p);
                    case ILOpCode.Conv_u4: return Conv(Primitive.UInt, p);
                    case ILOpCode.Conv_u8: return Conv(Primitive.ULong, p);
                    case ILOpCode.Conv_r4: return Conv(Primitive.Float, p);
                    case ILOpCode.Conv_r8: case ILOpCode.Conv_r_un: return Conv(Primitive.Double, p);
                    // native int (IntPtr): unsafe pointer aritmetigi byte-tabanli; sabit indeksi oldugu gibi birak
                    // (IL zaten *sizeof ile carpar; NumBin pointer+int byte aritmetigi yapar).
                    case ILOpCode.Conv_i: case ILOpCode.Conv_u: return p;

                    case ILOpCode.Constrained: // sonraki callvirt kisitli tipe gore dispatch eder
                        pendingConstrained = DecodeTypeTok(il, p);
                        return p + 4;

                    case ILOpCode.Call:
                    case ILOpCode.Callvirt:
                        {
                            var h = Tok(il, p);
                            // constrained. + callvirt: deger tipinde (struct) uye dogrudan cagrilir (boxing yok);
                            // this = ldloca'dan gelen ptr. Referans tipinde deref + normal sanal dispatch.
                            if (pendingConstrained != null && op == ILOpCode.Callvirt && h.Kind == HandleKind.MemberReference)
                            {
                                var cmr = md.GetMemberReference((MemberReferenceHandle)h);
                                var cname = md.GetString(cmr.Name);
                                var csig = cmr.DecodeMethodSignature(loader.Sig, gc);
                                var ct = pendingConstrained;
                                pendingConstrained = null;
                                if (ct.IsStruct || ct.GenericTemplate?.IsStruct == true)
                                {
                                    var ctpl = ct.GenericTemplate ?? ct;
                                    var ctArgs = ct.TypeArguments != null ? new List<Primitive>(ct.TypeArguments) : new List<Primitive>();
                                    var cm = loader.FindCode(ctpl, cname == ".ctor" ? "ctor" : Loader.OperatorRemap(cname), csig.ParameterTypes);
                                    for (int i = 0; i < cm.Arguments.Count; i++) Pop(); // this(ptr) + arglar
                                    var cop = new Op { Type = OpType.Call, Code = cm };
                                    if (ctArgs.Count > 0) cop.TypeArguments = ctArgs;
                                    EmitOp(cop);
                                    if (cm.ReturnType != Primitive.Void) Push(loader.ConcreteType(cm, ctArgs, cm.ReturnType));
                                    return p + 4;
                                }
                                // referans tip: ptr'i deref et (ldloca ptr -> ref), sonra normal callvirt yoluna dus
                                EmitOp(new Op { Type = OpType.LoadInd });
                                var derefed = Pop(); Push(ct);
                            }
                            if (h.Kind == HandleKind.MemberReference)
                            {
                                var mr = md.GetMemberReference((MemberReferenceHandle)h);
                                var mrName = md.GetString(mr.Name);
                                if (mrName == "GetTypeFromHandle") // ldtoken zaten TypeOf uretti
                                    return p + 4;
                                if (mrName == "InitializeArray" && pendingArrayField != default) // array-literal init idiyomu
                                    return EmitArrayInit(p);
                                if (mr.Parent.Kind != HandleKind.TypeSpecification) // generic ornek uyesi: asagida ResolveMethod cozer
                                {
                                    var mrOwner = loader.RefName(mr.Parent);
                                    if (IsIdentityRefType(mrOwner) && (mrName == "op_Equality" || mrName == "op_Inequality"))
                                    {
                                        Cmp(mrName == "op_Equality" ? OpType.Ceq : OpType.Cne); // Type/Reflection wrapper'lari kimlik-cache'li: referans karsilastirma yeterli
                                        return p + 4;
                                    }
                                    if (mrName == ".ctor" && mrOwner == "System.Object")
                                    {
                                        Emit(OpType.Pop); // object ctor'u no-op (MiniCs ile ayni kural); alici atilir
                                        Pop();
                                        return p + 4;
                                    }
                                }
                            }
                            // async builder remap'leri: Roslyn state machine cagrilarini corelib esdegerlerine indir.
                            // Start<TSM>(ref TSM) = ilk senkron kosum -> dogrudan sm.MoveNext();
                            // AwaitUnsafeOnCompleted<TA,TSM>(ref TA, ref TSM) -> awaiter.Schedule(sm) (Async.cs).
                            if (h.Kind == HandleKind.MethodSpecification)
                            {
                                var msp = md.GetMethodSpecification((MethodSpecificationHandle)h);
                                string gname = msp.Method.Kind == HandleKind.MemberReference
                                    ? md.GetString(md.GetMemberReference((MemberReferenceHandle)msp.Method).Name) : null;
                                if (gname == "Start" || gname == "AwaitUnsafeOnCompleted")
                                {
                                    var asyncOwner = loader.OwnerPrimOf(msp.Method, gc);
                                    var asyncTmplName = (asyncOwner?.GenericTemplate ?? asyncOwner)?.Name ?? "";
                                    if (asyncTmplName.StartsWith("System.Runtime.CompilerServices.AsyncTaskMethodBuilder"))
                                    {
                                        var gargs = msp.DecodeSignature(loader.Sig, gc);
                                        var smType = gargs[gargs.Length - 1];
                                        var asmIface = loader.ResolveName("System.Runtime.CompilerServices.IAsyncStateMachine");
                                        var moveNext = loader.FindCode(asmIface, "MoveNext", ImmutableArray<Primitive>.Empty);
                                        // stack tepesi: ref TSM -> deref edip temp'e al (terfi etmis SM: zaten referans)
                                        if (!IsPromoted(smType))
                                            EmitOp(new Op { Type = OpType.LoadInd });
                                        Pop(); Push(smType);
                                        int tmpSm = NewLocal(smType);
                                        EmitOp(new Op { Type = OpType.SetLocal, Slot = tmpSm }); Pop();
                                        if (gname == "Start")
                                        {
                                            Emit(OpType.Pop); Pop(); // builder adresi kullanilmaz
                                            EmitOp(new Op { Type = OpType.GetLocal, Slot = tmpSm }); Push(smType);
                                            EmitOp(new Op { Type = OpType.CallVirtual, Code = moveNext }); Pop();
                                            return p + 4;
                                        }
                                        var awaiterType = gargs[0];
                                        int tmpAw = NewLocal(Primitive.PointerOf(awaiterType));
                                        EmitOp(new Op { Type = OpType.SetLocal, Slot = tmpAw }); Pop(); // ref TA
                                        Emit(OpType.Pop); Pop(); // builder adresi
                                        EmitOp(new Op { Type = OpType.GetLocal, Slot = tmpAw }); Push(Primitive.PointerOf(awaiterType));
                                        EmitOp(new Op { Type = OpType.GetLocal, Slot = tmpSm }); Push(smType);
                                        var awTmpl = awaiterType.GenericTemplate ?? awaiterType;
                                        var sched = loader.FindCode(awTmpl, "Schedule", ImmutableArray.Create(asmIface));
                                        var sop = new Op { Type = OpType.Call, Code = sched };
                                        if (awaiterType.TypeArguments != null && awaiterType.TypeArguments.Count > 0)
                                            sop.TypeArguments = new List<Primitive>(awaiterType.TypeArguments);
                                        EmitOp(sop); Pop(); Pop();
                                        return p + 4;
                                    }
                                }
                            }
                            var ownerOfCall = loader.OwnerPrimOf(h, gc);
                            if (TrySpanCall(h, ownerOfCall)) return p + 4; // Span/ReadOnlySpan/MemoryExtensions intrinsic'leri
                            if (Loader.IsDelegateType(ownerOfCall) && (h.Kind != HandleKind.MemberReference ? "Invoke" : md.GetString(md.GetMemberReference((MemberReferenceHandle)h).Name)) == "Invoke")
                            {
                                var delTmpl = ownerOfCall.GenericTemplate ?? ownerOfCall; // imza sablondan (Apply node bos)
                                for (int i = 0; i < delTmpl.DelegateParams.Count + 1; i++) Pop(); // arglar + delegate
                                EmitOp(new Op { Type = OpType.CallIndirect, PrimitiveRef = ownerOfCall });
                                if (delTmpl.DelegateReturn != Primitive.Void)
                                    Push(loader.ConcreteTypeForOwner(ownerOfCall, delTmpl.DelegateReturn));
                                return p + 4;
                            }
                            var target = loader.ResolveMethod(h, gc, out var callTypeArgs);
                            bool virt = op == ILOpCode.Callvirt && (target.IsVirtual || target.IsOverride);
                            for (int i = 0; i < target.Arguments.Count; i++) Pop();
                            var callOp = new Op { Type = virt ? OpType.CallVirtual : OpType.Call, Code = target };
                            if (callTypeArgs.Count > 0) callOp.TypeArguments = callTypeArgs; // monomorfize: [sinif]+[method] args
                            EmitOp(callOp);
                            if (target.ReturnType != Primitive.Void) Push(loader.ConcreteType(target, callTypeArgs, target.ReturnType));
                            return p + 4;
                        }
                    case ILOpCode.Newobj:
                        {
                            var h = Tok(il, p);
                            var ownerPrim = loader.OwnerPrimOf(h, gc);
                            if (TrySpanNewobj(h, ownerPrim)) return p + 4; // new Span<T>(void*,int) / (T[]) / (T[],int,int)
                            if (ownerPrim == Primitive.Object) // new object() -> ctor'suz New (Object..ctor no-op)
                            {
                                EmitOp(new Op { Type = OpType.New, PrimitiveRef = Primitive.Object });
                                Push(Primitive.Object);
                                return p + 4;
                            }
                            if (ownerPrim != null && Loader.IsDelegateType(ownerPrim)) // ldftn/ldvirtftn + newobj Del::.ctor(obj, fnptr)
                            {
                                if (pendingFtn == null) throw new Exception("CIL: delegate ctor'u ldftn'siz");
                                Pop(); Pop(); // fnptr marker + target
                                if (pendingFtnVirtual && ops.Count > 0 && ops[ops.Count - 1].Type == OpType.Dup)
                                {
                                    ops.RemoveAt(ops.Count - 1); // dup;ldvirtftn deseni: fazla kopyayi at
                                    Pop(); // Dup op'u silindi: DelegateNew tek alici tuketir, artan kopyayi stack modelinden de dus
                                }
                                if (!pendingFtnVirtual && pendingFtn.IsStatic)
                                    Emit(OpType.Pop); // ldnull'un pusladigi bos target
                                EmitOp(new Op
                                {
                                    Type = OpType.DelegateNew,
                                    Code = pendingFtn,
                                    PrimitiveRef = ownerPrim,
                                    Slot = pendingFtnVirtual ? -2 : -1
                                });
                                pendingFtn = null;
                                Push(ownerPrim);
                                return p + 4;
                            }
                            var ctor = loader.ResolveMethod(h, gc, out var ctorTypeArgs);
                            var t2 = ctor.Owner;
                            var ownerConcrete = ctorTypeArgs.Count > 0 ? Primitive.Apply(t2, ctorTypeArgs.ToArray()) : t2; // Box<int>
                            // CIL: arglar stack'te, nesne YOK -> arglari temp'e indir
                            int n = ctor.Arguments.Count - 1;
                            var tmps = new int[n];
                            for (int i = n - 1; i >= 0; i--)
                            {
                                tmps[i] = NewLocal(loader.ConcreteType(ctor, ctorTypeArgs, ctor.Arguments[i + 1].Type));
                                EmitOp(new Op { Type = OpType.SetLocal, Slot = tmps[i] });
                                Pop();
                            }
                            if (t2.IsStruct)
                            {
                                // struct: sifirli temp'in ADRESINDE ctor kos, degeri birak (MiniCs ile ayni)
                                int sv = NewLocal(ownerConcrete);
                                EmitOp(new Op { Type = OpType.Default, PrimitiveRef = t2, TypeArguments = ctorTypeArgs });
                                EmitOp(new Op { Type = OpType.SetLocal, Slot = sv });
                                EmitOp(new Op { Type = OpType.AddrLocal, Slot = sv });
                                for (int i = 0; i < n; i++) EmitOp(new Op { Type = OpType.GetLocal, Slot = tmps[i] });
                                EmitOp(new Op { Type = OpType.Call, Code = ctor, TypeArguments = ctorTypeArgs });
                                EmitOp(new Op { Type = OpType.GetLocal, Slot = sv });
                                Push(ownerConcrete);
                                return p + 4;
                            }
                            // class: New;Dup;arglar;Call ctor
                            EmitOp(new Op { Type = OpType.New, PrimitiveRef = t2, TypeArguments = ctorTypeArgs });
                            Emit(OpType.Dup);
                            for (int i = 0; i < n; i++) EmitOp(new Op { Type = OpType.GetLocal, Slot = tmps[i] });
                            EmitOp(new Op { Type = OpType.Call, Code = ctor, TypeArguments = ctorTypeArgs });
                            Push(ownerConcrete);
                            return p + 4;
                        }
                    case ILOpCode.Ldftn:
                        pendingFtn = loader.ResolveMethod(Tok(il, p));
                        pendingFtnVirtual = false;
                        Push(Primitive.Long); // fnptr yer tutucu (op EMIT EDILMEZ)
                        return p + 4;
                    case ILOpCode.Ldvirtftn:
                        pendingFtn = loader.ResolveMethod(Tok(il, p));
                        pendingFtnVirtual = true;
                        Push(Primitive.Long);
                        return p + 4;

                    case ILOpCode.Ret:
                        if (code.ReturnType != Primitive.Void && stack.Count > 0) Pop();
                        Emit(OpType.Return);
                        stack.Clear();
                        unreachable = true;
                        return p;

                    case ILOpCode.Ldfld:
                        {
                            var f = loader.ResolveField(Tok(il, p), gc, out var fta);
                            Pop();
                            EmitOp(new Op { Type = OpType.GetField, Field = f, TypeArguments = fta });
                            Push(loader.FieldTypeConcrete(f, fta));
                            return p + 4;
                        }
                    case ILOpCode.Stfld:
                        {
                            var f = loader.ResolveField(Tok(il, p), gc, out var fta);
                            Pop(); Pop();
                            EmitOp(new Op { Type = OpType.SetField, Field = f, TypeArguments = fta });
                            return p + 4;
                        }
                    case ILOpCode.Ldsfld:
                        {
                            var h = Tok(il, p);
                            // IntPtr.Zero / UIntPtr.Zero: corelib IntPtr = native int (Long); sabit 0.
                            if (h.Kind == HandleKind.MemberReference)
                            {
                                var mr0 = md.GetMemberReference((MemberReferenceHandle)h);
                                var on = mr0.Parent.Kind == HandleKind.TypeReference ? loader.RefName(mr0.Parent) : null;
                                if ((on == "System.IntPtr" || on == "System.UIntPtr") && md.GetString(mr0.Name) == "Zero")
                                {
                                    EmitOp(new Op { Type = OpType.Push, Value = 0L });
                                    Push(Primitive.Long);
                                    return p + 4;
                                }
                            }
                            var f = loader.ResolveField(h, gc, out var fta);
                            EmitOp(new Op { Type = OpType.GetStatic, Field = f, TypeArguments = fta });
                            Push(loader.FieldTypeConcrete(f, fta));
                            return p + 4;
                        }
                    case ILOpCode.Stsfld:
                        {
                            var f = loader.ResolveField(Tok(il, p), gc, out var fta);
                            Pop();
                            EmitOp(new Op { Type = OpType.SetStatic, Field = f, TypeArguments = fta });
                            return p + 4;
                        }

                    case ILOpCode.Newarr:
                        {
                            var et = DecodeTypeTok(il, p);
                            if (ops.Count > 0 && ops[ops.Count - 1].Type == OpType.Push && ops[ops.Count - 1].Value is int nlen)
                                lastNewArrLen = nlen; // array-literal init icin uzunlugu hatirla
                            Pop();
                            EmitOp(new Op { Type = OpType.NewArray, PrimitiveRef = et });
                            Push(Primitive.ArrayOf(et));
                            return p + 4;
                        }
                    case ILOpCode.Ldlen: Pop(); Emit(OpType.ArrayLength); Push(Primitive.Int); return p;
                    case ILOpCode.Localloc: Pop(); Emit(OpType.StackAlloc); Push(Primitive.PointerOf(Primitive.Void)); return p; // stackalloc: C alloca (sifirlanir)
                    case ILOpCode.Ldelem_i1:
                    case ILOpCode.Ldelem_u1:
                    case ILOpCode.Ldelem_i2:
                    case ILOpCode.Ldelem_u2:
                    case ILOpCode.Ldelem_i4:
                    case ILOpCode.Ldelem_u4:
                    case ILOpCode.Ldelem_i8:
                    case ILOpCode.Ldelem_r4:
                    case ILOpCode.Ldelem_r8:
                    case ILOpCode.Ldelem_ref:
                        {
                            Pop(); // index
                            var arr = Pop();
                            Emit(OpType.GetIndex);
                            Push(arr.ElementType ?? Primitive.Int);
                            return p;
                        }
                    case ILOpCode.Ldelem: { Pop(); var arr = Pop(); Emit(OpType.GetIndex); Push(arr.ElementType ?? DecodeTypeTok(il, p)); return p + 4; }
                    case ILOpCode.Ldelema:
                        {
                            var et = DecodeTypeTok(il, p);
                            Pop(); // index
                            var arr = Pop();
                            EmitOp(new Op { Type = OpType.AddrElement, Slot = 1 });
                            Push(Primitive.PointerOf(arr.ElementType ?? et));
                            return p + 4;
                        }
                    case ILOpCode.Stelem_i1:
                    case ILOpCode.Stelem_i2:
                    case ILOpCode.Stelem_i4:
                    case ILOpCode.Stelem_i8:
                    case ILOpCode.Stelem_r4:
                    case ILOpCode.Stelem_r8:
                    case ILOpCode.Stelem_ref:
                        Pop(); Pop(); Pop();
                        Emit(OpType.SetIndex);
                        return p;
                    case ILOpCode.Stelem: Pop(); Pop(); Pop(); Emit(OpType.SetIndex); return p + 4;

                    case ILOpCode.Box:
                        {
                            var bt = DecodeTypeTok(il, p);
                            Pop();
                            EmitOp(new Op { Type = OpType.Box, PrimitiveRef = bt });
                            Push(Primitive.Object);
                            return p + 4;
                        }
                    case ILOpCode.Unbox_any:
                        {
                            var ut = DecodeTypeTok(il, p);
                            Pop();
                            if (ut.Type == PrimitiveType.Model && !ut.IsStruct) // unbox.any ref tipte = castclass
                                EmitOp(new Op { Type = OpType.CastClass, PrimitiveRef = ut });
                            else
                                EmitOp(new Op { Type = OpType.Unbox, PrimitiveRef = ut });
                            Push(ut);
                            return p + 4;
                        }
                    case ILOpCode.Castclass:
                        {
                            var ct = DecodeTypeTok(il, p);
                            Pop();
                            EmitOp(new Op { Type = OpType.CastClass, PrimitiveRef = ct });
                            Push(ct);
                            return p + 4;
                        }
                    case ILOpCode.Isinst:
                        {
                            var it = DecodeTypeTok(il, p);
                            Pop();
                            EmitOp(new Op { Type = OpType.AsType, PrimitiveRef = it }); // C# is/as: null testi IL'de devaminda
                            // deger tipi isinst: boxed referans (veya null) birakir -> Object; devaminda null-check / unbox.any
                            Push(it.Type == PrimitiveType.Model && !it.IsStruct ? it : Primitive.Object);
                            return p + 4;
                        }
                    case ILOpCode.Ldtoken:
                        {
                            var htok = Tok(il, p);
                            if (htok.Kind == HandleKind.FieldDefinition) // array-literal init: ldtoken <PrivateImpl>.field
                            {
                                pendingArrayField = (FieldDefinitionHandle)htok;
                                Push(Primitive.Object); // yalniz model: InitializeArray'de yutulur (runtime op yok)
                                return p + 4;
                            }
                            var tt = DecodeTypeTok(il, p);
                            EmitOp(new Op { Type = OpType.TypeOf, PrimitiveRef = tt }); // GetTypeFromHandle cagrisi asagida yutulur
                            Push(loader.ResolveName("System.Type"));
                            return p + 4;
                        }

                    case ILOpCode.Sizeof:
                        {
                            var st = DecodeTypeTok(il, p);
                            EmitOp(new Op { Type = OpType.SizeOf, PrimitiveRef = st });
                            Push(Primitive.Int); // C#'ta uint; stack'te int32 — kullanim hep aritmetik
                            return p + 4;
                        }

                    case ILOpCode.Throw:
                        Pop();
                        Emit(OpType.Throw);
                        stack.Clear();
                        unreachable = true;
                        return p;

                    default:
                        throw new Exception($"CIL: desteklenmeyen opcode: {op} ({code.EncodeName()}, IL_{at:X4})");
                }
            }

            int MacroBranch(OpType cmp, int target, int next)
            {
                Cmp(cmp);
                CondBranch(target);
                return next;
            }

            // ---- Span/ReadOnlySpan intrinsic'leri (corelib Span.cs: {long _ptr; int _len}) ----
            // MiniCs'te pointer/ref-donus yok; eleman adresi, dilimleme, kopyalama ve dizi/dizgi->span
            // donusumleri burada op dizisine acilir. GC tasimaz -> ham adres guvenli (fixed ile ayni gerekce).
            static bool IsSpanTemplate(Primitive t)
            {
                var n = (t?.GenericTemplate ?? t)?.Name;
                return n == "System.Span`1" || n == "System.ReadOnlySpan`1";
            }
            Primitive SpanOf(bool readOnly, Primitive elem)
                => Primitive.Apply(loader.ResolveName(readOnly ? "System.ReadOnlySpan`1" : "System.Span`1"), new[] { elem });
            void EmitSizeOf(Primitive t) { EmitOp(new Op { Type = OpType.SizeOf, PrimitiveRef = t }); Push(Primitive.Int); }
            // stack: Pointer(span) -> alan degeri (_ptr: Long, _len: Int)
            void EmitSpanField(Primitive span, string name)
            {
                var tmpl = span.GenericTemplate ?? span;
                PrimitiveField f = null;
                foreach (var x in tmpl.Fields) if (x.Name == name) f = x;
                if (f == null) throw new Exception("CIL: Span alani yok: " + name);
                Pop();
                EmitOp(new Op { Type = OpType.AddrField, Field = f, TypeArguments = new List<Primitive>(span.TypeArguments) });
                EmitOp(new Op { Type = OpType.LoadInd });
                Push(name == "_ptr" ? Primitive.Long : Primitive.Int);
            }
            void EmitConv(Primitive t) { EmitOp(new Op { Type = OpType.Conv, PrimitiveRef = t }); Pop(); Push(t); }
            // stack: Pointer(elem) -> Pointer(elem) + idx*sizeof(elem)
            void EmitOffset(int idxLocal, Primitive elem)
            {
                EmitOp(new Op { Type = OpType.GetLocal, Slot = idxLocal }); Push(Primitive.Int);
                EmitSizeOf(elem);
                Emit(OpType.Mul); Pop(); Pop(); Push(Primitive.Int);
                Emit(OpType.Add); Pop(); Pop(); Push(Primitive.PointerOf(elem));
            }
            int SpillLocal(Primitive t) { int l = NewLocal(t); EmitOp(new Op { Type = OpType.SetLocal, Slot = l }); Pop(); return l; }
            // yeni span degeri: ctor(long ptr, int len); pushPtr Long, pushLen Int birakmali
            void EmitMakeSpan(Primitive span, Action pushPtr, Action pushLen)
            {
                var tmpl = span.GenericTemplate ?? span;
                var targs = new List<Primitive>(span.TypeArguments);
                int sv = NewLocal(span);
                EmitOp(new Op { Type = OpType.Default, PrimitiveRef = tmpl, TypeArguments = targs });
                EmitOp(new Op { Type = OpType.SetLocal, Slot = sv });
                EmitOp(new Op { Type = OpType.AddrLocal, Slot = sv }); Push(Primitive.PointerOf(span));
                pushPtr(); pushLen();
                var ctor = loader.FindCode(tmpl, "ctor", ImmutableArray.Create(Primitive.Long, Primitive.Int));
                EmitOp(new Op { Type = OpType.Call, Code = ctor, TypeArguments = targs });
                Pop(); Pop(); Pop();
                EmitOp(new Op { Type = OpType.GetLocal, Slot = sv }); Push(span);
            }
            void PushArrayDataLong(int arrLocal, Primitive elem, int startLocal)
            {
                EmitOp(new Op { Type = OpType.GetLocal, Slot = arrLocal }); Push(Primitive.ArrayOf(elem));
                Emit(OpType.ArrayDataAddr); Pop(); Push(Primitive.PointerOf(elem));
                if (startLocal >= 0) EmitOffset(startLocal, elem);
                EmitConv(Primitive.Long);
            }
            void PushStringDataLong(int strLocal, int startLocal)
            {
                EmitOp(new Op { Type = OpType.GetLocal, Slot = strLocal }); Push(Primitive.String);
                Emit(OpType.ArrayDataAddr); Pop(); Push(Primitive.PointerOf(Primitive.Char));
                if (startLocal >= 0) EmitOffset(startLocal, Primitive.Char);
                EmitConv(Primitive.Long);
            }
            void PushArrayLen(int arrLocal, Primitive elem) { EmitOp(new Op { Type = OpType.GetLocal, Slot = arrLocal }); Push(Primitive.ArrayOf(elem)); Emit(OpType.ArrayLength); Pop(); Push(Primitive.Int); }
            void PushStringLen(int strLocal)
            {
                EmitOp(new Op { Type = OpType.GetLocal, Slot = strLocal }); Push(Primitive.String);
                var len = loader.FindCode(Primitive.String, "get_Length", ImmutableArray<Primitive>.Empty);
                EmitOp(new Op { Type = OpType.Call, Code = len }); Pop(); Push(Primitive.Int);
            }
            void PushLocal(int l, Primitive t) { EmitOp(new Op { Type = OpType.GetLocal, Slot = l }); Push(t); }
            void PushSub(int aLocal, int bLocal)
            {
                PushLocal(aLocal, Primitive.Int); PushLocal(bLocal, Primitive.Int);
                Emit(OpType.Sub); Pop(); Pop(); Push(Primitive.Int);
            }
            // uye adi + parametre tipleri (TypeSpec sahipli uye: sablon generic'leriyle cozulur) + method type args
            (string name, ImmutableArray<Primitive> ps, List<Primitive> margs) CallSigOf(EntityHandle h)
            {
                var margs = new List<Primitive>();
                if (h.Kind == HandleKind.MethodSpecification)
                {
                    var msp = md.GetMethodSpecification((MethodSpecificationHandle)h);
                    margs.AddRange(msp.DecodeSignature(loader.Sig, gc));
                    h = msp.Method;
                }
                if (h.Kind != HandleKind.MemberReference) return (null, default, margs);
                var mr = md.GetMemberReference((MemberReferenceHandle)h);
                var name = md.GetString(mr.Name);
                MethodSignature<Primitive> s;
                if (mr.Parent.Kind == HandleKind.TypeSpecification)
                {
                    var applied = loader.DecodeTypeSpecHandle((TypeSpecificationHandle)mr.Parent, gc);
                    var template = applied.GenericTemplate ?? applied;
                    s = mr.DecodeMethodSignature(loader.Sig, new GenCtx { TypeParams = template.GenericParameters });
                }
                else s = mr.DecodeMethodSignature(loader.Sig, margs.Count > 0 ? new GenCtx { TypeParams = gc.TypeParams, MethodParams = margs } : gc); // generic method: !!T -> somut arg
                return (name, s.ParameterTypes, margs);
            }
            bool TrySpanCall(EntityHandle h, Primitive owner)
            {
                var ownerName = (owner?.GenericTemplate ?? owner)?.Name;
                bool isSpanOwner = IsSpanTemplate(owner);
                if (!isSpanOwner && ownerName != "System.MemoryExtensions" && ownerName != "System.Buffer") return false;
                var (name, ps, margs) = CallSigOf(h);
                if (name == null) return false;
                if (ownerName == "System.Buffer")
                {
                    if (name != "MemoryCopy" || ps.Length != 4) return false; // (src, dst, dstSize, srcSize) -> SpanOps.Copy(dst, src, (int)srcSize)
                    int tn = SpillLocal(Primitive.Long); // srcSize (stack tepesi)
                    SpillLocal(Primitive.Long);          // dstSize: kullanilmaz (C#'ta yalniz tasma kontrolu)
                    var dstT = Top; int tdst = SpillLocal(dstT);
                    var srcT = Top; int tsrcp = SpillLocal(srcT);
                    PushLocal(tdst, dstT); EmitConv(Primitive.Long);
                    PushLocal(tsrcp, srcT); EmitConv(Primitive.Long);
                    PushLocal(tn, Primitive.Long); EmitConv(Primitive.Int);
                    var copy = loader.FindCode(loader.ResolveName("System.SpanOps"), "Copy", ImmutableArray.Create(Primitive.Long, Primitive.Long, Primitive.Int));
                    EmitOp(new Op { Type = OpType.Call, Code = copy }); Pop(); Pop(); Pop();
                    return true;
                }
                if (isSpanOwner)
                {
                    bool ro = ownerName == "System.ReadOnlySpan`1";
                    var T = owner.TypeArguments[0];
                    switch (name)
                    {
                        case "get_Item": // [this*, idx] -> &elem
                            {
                                int ti = SpillLocal(Primitive.Int);
                                EmitSpanField(owner, "_ptr"); EmitConv(Primitive.PointerOf(T)); EmitOffset(ti, T);
                                return true;
                            }
                        case "GetPinnableReference": // [this*] -> T*
                            EmitSpanField(owner, "_ptr"); EmitConv(Primitive.PointerOf(T));
                            return true;
                        case "Slice": // [this*, start(, len)]
                            {
                                int tl = ps.Length == 2 ? SpillLocal(Primitive.Int) : -1;
                                int ts = SpillLocal(Primitive.Int);
                                int tt = SpillLocal(Primitive.PointerOf(owner));
                                EmitMakeSpan(owner,
                                    () => { PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_ptr"); EmitConv(Primitive.PointerOf(T)); EmitOffset(ts, T); EmitConv(Primitive.Long); },
                                    () => { if (tl >= 0) PushLocal(tl, Primitive.Int); else { PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_len"); PushLocal(ts, Primitive.Int); Emit(OpType.Sub); Pop(); Pop(); Push(Primitive.Int); } });
                                return true;
                            }
                        case "CopyTo": // [this*, dstSpan(value)] -> SpanOps.Copy(dst._ptr, this._ptr, this._len*sizeof)
                            {
                                var dstT = Top; int td = SpillLocal(dstT);
                                int tt = SpillLocal(Primitive.PointerOf(owner));
                                EmitOp(new Op { Type = OpType.AddrLocal, Slot = td }); Push(Primitive.PointerOf(dstT)); EmitSpanField(dstT, "_ptr");
                                PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_ptr");
                                PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_len"); EmitSizeOf(T); Emit(OpType.Mul); Pop(); Pop(); Push(Primitive.Int);
                                var copy = loader.FindCode(loader.ResolveName("System.SpanOps"), "Copy", ImmutableArray.Create(Primitive.Long, Primitive.Long, Primitive.Int));
                                EmitOp(new Op { Type = OpType.Call, Code = copy }); Pop(); Pop(); Pop();
                                return true;
                            }
                        case "ToArray": // [this*] -> T[]
                            {
                                int tt = SpillLocal(Primitive.PointerOf(owner));
                                PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_len");
                                Pop(); EmitOp(new Op { Type = OpType.NewArray, PrimitiveRef = T }); Push(Primitive.ArrayOf(T));
                                int ta = SpillLocal(Primitive.ArrayOf(T));
                                PushArrayDataLong(ta, T, -1);
                                PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_ptr");
                                PushLocal(tt, Primitive.PointerOf(owner)); EmitSpanField(owner, "_len"); EmitSizeOf(T); Emit(OpType.Mul); Pop(); Pop(); Push(Primitive.Int);
                                var copy = loader.FindCode(loader.ResolveName("System.SpanOps"), "Copy", ImmutableArray.Create(Primitive.Long, Primitive.Long, Primitive.Int));
                                EmitOp(new Op { Type = OpType.Call, Code = copy }); Pop(); Pop(); Pop();
                                PushLocal(ta, Primitive.ArrayOf(T));
                                return true;
                            }
                        case "op_Implicit": // T[] -> span | Span<T> -> ReadOnlySpan<T>
                            {
                                var arg = Top;
                                if (arg.Type == PrimitiveType.Array)
                                {
                                    int ta = SpillLocal(arg);
                                    EmitMakeSpan(owner, () => PushArrayDataLong(ta, T, -1), () => PushArrayLen(ta, T));
                                    return true;
                                }
                                if (IsSpanTemplate(arg)) // Span<T> -> ReadOnlySpan<T> (operator Span<T> uzerinde tanimli): ayni yerlesim, yeniden yorumla
                                {
                                    var target = SpanOf(true, T);
                                    int tv = SpillLocal(arg);
                                    EmitOp(new Op { Type = OpType.AddrLocal, Slot = tv }); Push(Primitive.PointerOf(arg));
                                    EmitConv(Primitive.PointerOf(target));
                                    EmitOp(new Op { Type = OpType.LoadInd }); Pop(); Push(target);
                                    return true;
                                }
                                return false;
                            }
                        default: return false; // Length/IsEmpty/Empty: corelib govdesi (normal cagri yolu)
                    }
                }
                // MemoryExtensions.AsSpan(T[]) / (T[],int) / (T[],int,int) / (string) / (string,int) / (string,int,int)
                if (name != "AsSpan" || ps.Length < 1) return false;
                bool str = ps[0] == Primitive.String;
                if (!str && ps[0].Type != PrimitiveType.Array) return false;
                var elem = str ? Primitive.Char : (margs.Count > 0 ? margs[0] : ps[0].ElementType);
                var span = SpanOf(str, elem);
                int tlen = ps.Length == 3 ? SpillLocal(Primitive.Int) : -1;
                int tst = ps.Length >= 2 ? SpillLocal(Primitive.Int) : -1;
                int tsrc = SpillLocal(str ? Primitive.String : Primitive.ArrayOf(elem));
                EmitMakeSpan(span,
                    () => { if (str) PushStringDataLong(tsrc, tst); else PushArrayDataLong(tsrc, elem, tst); },
                    () =>
                    {
                        if (tlen >= 0) { PushLocal(tlen, Primitive.Int); return; }
                        if (str) PushStringLen(tsrc); else PushArrayLen(tsrc, elem);
                        if (tst >= 0) { PushLocal(tst, Primitive.Int); Emit(OpType.Sub); Pop(); Pop(); Push(Primitive.Int); }
                    });
                return true;
            }
            bool TrySpanNewobj(EntityHandle h, Primitive owner)
            {
                if (!IsSpanTemplate(owner)) return false;
                var (name, ps, _) = CallSigOf(h);
                if (name != ".ctor") return false;
                var T = owner.TypeArguments[0];
                if (ps.Length == 2 && ps[0].Type == PrimitiveType.Pointer) // (void*, int)
                {
                    int tl = SpillLocal(Primitive.Int);
                    var pt = Top; int tp = SpillLocal(pt);
                    EmitMakeSpan(owner, () => { PushLocal(tp, pt); EmitConv(Primitive.Long); }, () => PushLocal(tl, Primitive.Int));
                    return true;
                }
                if (ps.Length == 1 && ps[0].Type == PrimitiveType.Array) // (T[])
                {
                    int ta = SpillLocal(Primitive.ArrayOf(T));
                    EmitMakeSpan(owner, () => PushArrayDataLong(ta, T, -1), () => PushArrayLen(ta, T));
                    return true;
                }
                if (ps.Length == 3 && ps[0].Type == PrimitiveType.Array) // (T[], start, len)
                {
                    int tl = SpillLocal(Primitive.Int);
                    int ts = SpillLocal(Primitive.Int);
                    int ta = SpillLocal(Primitive.ArrayOf(T));
                    EmitMakeSpan(owner, () => PushArrayDataLong(ta, T, ts), () => PushLocal(tl, Primitive.Int));
                    return true;
                }
                return false;
            }

            static Primitive IndType(ILOpCode op) => op switch
            {
                ILOpCode.Ldind_i1 or ILOpCode.Stind_i1 => Primitive.SByte,
                ILOpCode.Ldind_u1 => Primitive.Byte,
                ILOpCode.Ldind_i2 or ILOpCode.Stind_i2 => Primitive.Short,
                ILOpCode.Ldind_u2 => Primitive.UShort,
                ILOpCode.Ldind_i4 or ILOpCode.Stind_i4 => Primitive.Int,
                ILOpCode.Ldind_u4 => Primitive.UInt,
                ILOpCode.Ldind_i8 or ILOpCode.Stind_i8 => Primitive.Long,
                ILOpCode.Ldind_r4 or ILOpCode.Stind_r4 => Primitive.Float,
                ILOpCode.Ldind_r8 or ILOpCode.Stind_r8 => Primitive.Double,
                ILOpCode.Ldind_ref or ILOpCode.Stind_ref => Primitive.Object,
                _ => Primitive.Long, // ldind.i/stind.i: native int
            };

            int Conv(Primitive t, int p)
            {
                EmitOp(new Op { Type = OpType.Conv, PrimitiveRef = t });
                Pop();
                Push(t);
                return p;
            }

            // array-literal init idiyomu: newarr;dup;ldtoken field;call InitializeArray
            // -> dup'lanan diziyi temp'e al, ham veriden eleman-eleman doldur.
            int EmitArrayInit(int p)
            {
                Pop();                       // handle yer tutucu (ldtoken'in pusladigi)
                var arrType = Top;           // dup'lanan dizi
                var et = arrType.ElementType ?? Primitive.Int;
                int esize = ElemSize(et);
                int tmp = NewLocal(arrType);
                EmitOp(new Op { Type = OpType.SetLocal, Slot = tmp });
                Pop();                       // dup'lanan diziyi tukettik (orijinal stack'te kalir)
                var data = loader.FieldInitData(pendingArrayField, lastNewArrLen * esize);
                pendingArrayField = default;
                if (data != null)
                    for (int i = 0; i < lastNewArrLen; i++)
                    {
                        EmitOp(new Op { Type = OpType.GetLocal, Slot = tmp });
                        EmitOp(new Op { Type = OpType.Push, Value = i });
                        EmitOp(new Op { Type = OpType.Push, Value = DecodeElem(et, data, i * esize) });
                        Emit(OpType.SetIndex);
                    }
                return p + 4;
            }

            static int ElemSize(Primitive t) =>
                t == Primitive.SByte || t == Primitive.Byte || t == Primitive.Bool ? 1
                : t == Primitive.Short || t == Primitive.UShort || t == Primitive.Char ? 2
                : t == Primitive.Long || t == Primitive.ULong || t == Primitive.Double ? 8
                : 4; // Int/UInt/Float ve digerleri

            static object DecodeElem(Primitive t, byte[] d, int off)
            {
                if (t == Primitive.SByte) return (int)(sbyte)d[off];
                if (t == Primitive.Byte || t == Primitive.Bool) return (int)d[off];
                if (t == Primitive.Short) return (int)BitConverter.ToInt16(d, off);
                if (t == Primitive.UShort || t == Primitive.Char) return (int)BitConverter.ToUInt16(d, off);
                if (t == Primitive.Long) return BitConverter.ToInt64(d, off);
                if (t == Primitive.ULong) return (long)BitConverter.ToUInt64(d, off);
                if (t == Primitive.Float) return BitConverter.ToSingle(d, off);
                if (t == Primitive.Double) return BitConverter.ToDouble(d, off);
                if (t == Primitive.UInt) return (int)BitConverter.ToUInt32(d, off);
                return BitConverter.ToInt32(d, off); // Int ve digerleri
            }

            Primitive DecodeTypeTok(byte[] il, int p)
            {
                var h = Tok(il, p);
                if (h.Kind == HandleKind.TypeDefinition) return loader.prims[(TypeDefinitionHandle)h];
                if (h.Kind == HandleKind.TypeReference) return loader.ResolveName(loader.RefName(h));
                if (h.Kind == HandleKind.TypeSpecification) return loader.DecodeTypeSpecHandle((TypeSpecificationHandle)h, gc);
                throw new Exception("CIL: beklenmeyen tip token: " + h.Kind);
            }
        }
    }
}
