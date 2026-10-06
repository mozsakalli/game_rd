using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

// BCL yuzey envanteri: DLL'lerdeki IL govdelerini tarar, System.* uyelerine yapilan
// cagri/alan/tip referanslarini (cagiranla birlikte) toplar; corelib kaynaginda var mi bakar.
// Kaynak taramasi KABA (regex): "UYE YOK" listesi yanlis pozitif icerebilir (indexer, ozel ad...).
// Son arguman aotcompiler diag.txt ise capraz dogrulama yapilir: cagirani diag'da stub/skip
// OLMAYAN referans zaten cozuluyor demektir -> elenir; kalanlar "dogrulanmis/gizli" eksiklerdir
// (stub'lanmis govdenin arkasinda bekleyen ikinci engeli onceden gosterir).
// Kullanim: bclsurvey <corelibDir> <dll> [dll...] [diag.txt]
var corelibDir = args[0];
var diagPath = args.Length > 2 && args[^1].EndsWith("diag.txt", StringComparison.OrdinalIgnoreCase) ? args[^1] : null;
var dlls = args.Skip(1).Where(a => a != diagPath).ToArray();

// --- corelib yuzeyi (kaba kaynak taramasi): "Namespace.Type" -> uye adlari ---
var corelibTypes = new Dictionary<string, HashSet<string>>();
foreach (var f in Directory.GetFiles(corelibDir, "*.cs"))
{
    var src = File.ReadAllText(f);
    var nsMatches = Regex.Matches(src, @"namespace\s+([\w.]+)");
    string NsAt(int idx) { string ns = ""; foreach (Match nm in nsMatches) if (nm.Index < idx) ns = nm.Groups[1].Value; return ns; }
    // Tip bloklari: (class|struct|interface|enum) Name<...>? { ... } — kaba: tip adindan sonraki metni bir sonraki tip bildirimine kadar al
    var decls = Regex.Matches(src, @"\b(class|struct|interface|enum|delegate\s+[\w<>\[\],. ]+?)\s+(\w+)(<([^>]*)>)?\s*[({:;]");
    for (int i = 0; i < decls.Count; i++)
    {
        var m = decls[i];
        string name = m.Groups[2].Value;
        int arity = m.Groups[4].Success ? m.Groups[4].Value.Split(',').Length : 0;
        string full = NsAt(m.Index) + "." + name + (arity > 0 ? "`" + arity : "");
        int start = m.Index, end = i + 1 < decls.Count ? decls[i + 1].Index : src.Length;
        var body = src.Substring(start, end - start);
        var members = new HashSet<string>();
        if (m.Groups[1].Value.StartsWith("delegate")) { members.Add("Invoke"); members.Add(".ctor"); }
        foreach (Match fm in Regex.Matches(body, @"\b(public|internal)\s+(static\s+|readonly\s+)*[\w<>\[\],]+\s+(\w+)\s*(;|=)")) members.Add(fm.Groups[3].Value);
        foreach (Match mm in Regex.Matches(body, @"\b(\w+)\s*(<[^>]*>)?\s*\("))
            members.Add(mm.Groups[1].Value);
        foreach (Match pm in Regex.Matches(body, @"\b(\w+)\s*\{\s*(get|set)"))
        { members.Add("get_" + pm.Groups[1].Value); members.Add("set_" + pm.Groups[1].Value); }
        foreach (Match om in Regex.Matches(body, @"\boperator\s*(\S+)\s*\("))
            members.Add("op:" + om.Groups[1].Value);
        if (!corelibTypes.TryGetValue(full, out var set)) corelibTypes[full] = set = new HashSet<string>();
        set.UnionWith(members);
    }
}
// Frontend intrinsic/remap'leri (CilFrontend): bunlar corelib kaynaginda gorunmez ama desteklidir.
var intrinsicTypes = new HashSet<string> { "System.Span`1", "System.ReadOnlySpan`1", "System.MemoryExtensions", "System.Buffer",
    "System.Collections.Generic.List`1/Enumerator", "System.Collections.Generic.HashSet`1/Enumerator", "System.Collections.Generic.Dictionary`2/Enumerator",
    "System.Runtime.CompilerServices.RuntimeHelpers", "System.Runtime.CompilerServices.IsReadOnlyAttribute", "System.Runtime.CompilerServices.IsByRefLikeAttribute",
    "System.Runtime.CompilerServices.CompilerGeneratedAttribute", "System.Runtime.CompilerServices.NullableAttribute", "System.Runtime.CompilerServices.NullableContextAttribute",
    "System.Runtime.CompilerServices.ExtensionAttribute", "System.Runtime.CompilerServices.RefSafetyRulesAttribute", "System.Diagnostics.DebuggerBrowsableAttribute",
    "System.Diagnostics.DebuggerBrowsableState", "System.Diagnostics.DebuggerHiddenAttribute", "System.Diagnostics.DebuggerStepThroughAttribute",
    "System.Runtime.CompilerServices.AsyncStateMachineAttribute", "System.Runtime.CompilerServices.ScopedRefAttribute", "System.Runtime.CompilerServices.InlineArrayAttribute",
    "System.Runtime.CompilerServices.PreserveBaseOverridesAttribute", "System.Runtime.CompilerServices.IsUnmanagedAttribute", "System.ParamArrayAttribute",
    "System.ObsoleteAttribute", "System.FlagsAttribute", "System.ThreadStaticAttribute", "System.Runtime.InteropServices.StructLayoutAttribute", "System.Runtime.InteropServices.LayoutKind",
    "System.Runtime.InteropServices.DllImportAttribute", "System.Runtime.InteropServices.CallingConvention", "System.Runtime.InteropServices.FieldOffsetAttribute",
    "System.Runtime.Versioning.TargetFrameworkAttribute", "System.Reflection.AssemblyCompanyAttribute", "System.Reflection.AssemblyConfigurationAttribute",
    "System.Reflection.AssemblyFileVersionAttribute", "System.Reflection.AssemblyInformationalVersionAttribute", "System.Reflection.AssemblyProductAttribute",
    "System.Reflection.AssemblyTitleAttribute", "System.Runtime.CompilerServices.InternalsVisibleToAttribute", "System.Runtime.InteropServices.InAttribute",
    "System.Runtime.CompilerServices.IsVolatile", "System.Runtime.CompilerServices.NullablePublicOnlyAttribute", "System.Runtime.CompilerServices.SkipLocalsInitAttribute",
    "System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute", "System.Runtime.CompilerServices.CallConvCdecl", "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute",
    "System.Runtime.InteropServices.OutAttribute", "System.Runtime.CompilerServices.RequiredMemberAttribute", "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute" };

// opcode tablosu (operand boyutu icin)
var opcodes1 = new Dictionary<int, OpCode>(); var opcodes2 = new Dictionary<int, OpCode>();
foreach (var fi in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
{
    var oc = (OpCode)fi.GetValue(null)!;
    if (oc.Size == 1) opcodes1[(byte)oc.Value] = oc; else opcodes2[oc.Value & 0xFF] = oc;
}
int OperandSize(OpCode oc) => oc.OperandType switch
{
    OperandType.InlineNone => 0,
    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
    OperandType.InlineVar => 2,
    OperandType.InlineI8 or OperandType.InlineR => 8,
    OperandType.InlineSwitch => -1,
    _ => 4,
};

// referans -> cagiranlar
var refs = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
void Add(string key, string caller) { if (!refs.TryGetValue(key, out var s)) refs[key] = s = new(); s.Add(caller); }

foreach (var dll in dlls)
{
    using var pe = new PEReader(File.OpenRead(dll));
    var md = pe.GetMetadataReader();
    string asm = Path.GetFileNameWithoutExtension(dll);
    var prov = new NameProvider(md);

    string RefName(EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeReference:
                {
                    var tr = md.GetTypeReference((TypeReferenceHandle)h);
                    string n = md.GetString(tr.Name);
                    if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return RefName(tr.ResolutionScope) + "/" + n;
                    string ns = md.GetString(tr.Namespace);
                    return ns.Length > 0 ? ns + "." + n : n;
                }
            case HandleKind.TypeDefinition:
                {
                    var td = md.GetTypeDefinition((TypeDefinitionHandle)h);
                    string n = md.GetString(td.Name);
                    var decl = td.GetDeclaringType();
                    if (!decl.IsNil) return RefName(decl) + "/" + n;
                    string ns = md.GetString(td.Namespace);
                    return ns.Length > 0 ? ns + "." + n : n;
                }
            case HandleKind.TypeSpecification:
                return md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(prov, null);
            default: return "?";
        }
    }
    string MemberKey(EntityHandle h)
    {
        if (h.Kind == HandleKind.MethodSpecification)
            return MemberKey(md.GetMethodSpecification((MethodSpecificationHandle)h).Method);
        if (h.Kind != HandleKind.MemberReference) return null!;
        var mr = md.GetMemberReference((MemberReferenceHandle)h);
        string owner = RefName(mr.Parent);
        string name = md.GetString(mr.Name);
        if (!owner.StartsWith("System")) return null!;
        if (mr.GetKind() == MemberReferenceKind.Method)
        {
            var sig = mr.DecodeMethodSignature(prov, null);
            return owner + "::" + name + "(" + string.Join(",", sig.ParameterTypes) + ")";
        }
        return owner + "::" + name + " [field]";
    }
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        string tname = RefName(th);
        // tip duzeyi: base + iface + alan tipleri (System.*)
        if (!td.BaseType.IsNil) { var b = RefName(td.BaseType); if (b.StartsWith("System.") && b != "System.Object" && b != "System.ValueType" && b != "System.Enum") Add(b + " [base]", asm + ":" + tname); }
        foreach (var ih in td.GetInterfaceImplementations()) { var i = RefName(md.GetInterfaceImplementation(ih).Interface); if (i.StartsWith("System.")) Add(i + " [iface]", asm + ":" + tname); }
        foreach (var fh in td.GetFields())
        {
            var fd = md.GetFieldDefinition(fh);
            var ft = fd.DecodeSignature(prov, null);
            foreach (Match m in Regex.Matches(ft, @"System\.[\w.`/]+")) if (!m.Value.StartsWith("System.Int") && !m.Value.StartsWith("System.UInt") && m.Value is not ("System.Object" or "System.String" or "System.Boolean" or "System.Single" or "System.Double" or "System.Byte" or "System.SByte" or "System.Char" or "System.Void")) Add(m.Value + " [fieldtype]", asm + ":" + tname + "." + md.GetString(fd.Name));
        }
        foreach (var mh in td.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            string caller = asm + ":" + tname + "." + md.GetString(m.Name);
            var sig = m.DecodeSignature(prov, null);
            foreach (var p in sig.ParameterTypes.Append(sig.ReturnType))
                foreach (Match mm in Regex.Matches(p, @"System\.[\w.`/]+")) if (mm.Value.Contains("Span") || mm.Value.Contains("Func") || mm.Value.Contains("Action") || mm.Value.Contains("Task") || mm.Value.Contains("Collections") || mm.Value.Contains("IO.") || mm.Value.Contains("Type") || mm.Value.Contains("Attribute") || mm.Value.Contains("Exception") || mm.Value.Contains("Text")) Add(mm.Value + " [sig]", caller);
            if (m.RelativeVirtualAddress == 0) continue;
            var body = pe.GetMethodBody(m.RelativeVirtualAddress);
            var il = body.GetILBytes()!;
            int pos = 0;
            while (pos < il.Length)
            {
                OpCode oc; int code = il[pos++];
                if (code == 0xFE) { oc = opcodes2[il[pos++]]; } else { if (!opcodes1.TryGetValue(code, out oc)) break; }
                int osz = OperandSize(oc);
                if (osz < 0) { int n = BitConverter.ToInt32(il, pos); pos += 4 + 4 * n; continue; }
                if (oc.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType)
                {
                    int tok = BitConverter.ToInt32(il, pos);
                    var h = MetadataTokens.EntityHandle(tok);
                    string? key = null;
                    if (oc.OperandType == OperandType.InlineType || (oc.OperandType == OperandType.InlineTok && h.Kind is HandleKind.TypeReference or HandleKind.TypeSpecification))
                    { var n = RefName(h); if (n.StartsWith("System.")) key = n + " [type]"; }
                    else if (h.Kind is HandleKind.MemberReference or HandleKind.MethodSpecification) key = MemberKey(h);
                    if (key != null) Add(key, caller);
                }
                pos += osz;
            }
        }
    }
}

// --- rapor ---
string Owner(string key) { int i = key.IndexOf("::"); if (i < 0) i = key.IndexOf(" ["); return i < 0 ? key : key[..i]; }
string Member(string key) { int i = key.IndexOf("::"); if (i < 0) return ""; int j = key.IndexOf('(', i); var s = key[(i + 2)..(j < 0 ? key.Length : j)]; int k = s.IndexOf(" ["); return k < 0 ? s : s[..k]; }
string NormOwner(string o) { int g = o.IndexOf('<'); return g < 0 ? o : o[..g]; }
string Callers(SortedSet<string> cs) => string.Join(", ", cs.Take(4)) + (cs.Count > 4 ? $" (+{cs.Count - 4})" : "");

// diag.txt: stub/skip edilen yontemler ("Owner.Method" normalize) — capraz dogrulama icin.
var stubbed = new HashSet<string>(StringComparer.Ordinal);
if (diagPath != null)
    foreach (var d in File.ReadAllLines(diagPath))
    {
        var m = Regex.Match(d, @"(stub body|skip method) ([\w.]+)[$.](\w+?)(_|:|$)");
        if (m.Success) stubbed.Add(m.Groups[2].Value + "." + m.Groups[3].Value);
    }
string NormCaller(string c)
{
    c = Regex.Replace(c, @"^\w+:", "");                       // assembly oneki
    c = Regex.Replace(c, @"/<(\w+)>d__\d+\.MoveNext$", ".$1"); // async state machine -> metot
    c = Regex.Replace(c, @"/<>c\.\.ctor$", "..ctor");
    c = c.Replace("..ctor", ".ctor").Replace("..cctor", ".cctor"); // diag: Owner$ctor
    return c;
}
bool CallerStubbed(SortedSet<string> cs) => cs.Any(c => stubbed.Contains(NormCaller(c)));

var missing = new List<string>(); var partial = new List<string>(); var confirmed = new List<string>(); var ok = 0;
foreach (var (key, callers) in refs)
{
    string owner = NormOwner(Owner(key));
    string member = Member(key);
    if (intrinsicTypes.Contains(owner)) { ok++; continue; }
    bool gap;
    if (!corelibTypes.TryGetValue(owner, out var members)) gap = true;
    else if (member.Length == 0) gap = false;
    else gap = !(members.Contains(member) || (member.StartsWith("op_") && members.Any(x => x.StartsWith("op:")))
        || member == ".ctor" && (members.Contains(".ctor") || members.Contains(owner.Split('.', '/').Last().Split('`')[0])));
    if (!gap) { ok++; continue; }
    string line = $"{key}\n      <- {Callers(callers)}";
    if (diagPath != null)
    {
        if (CallerStubbed(callers)) confirmed.Add(line);
        else ok++; // cagiran derlendi -> referans cozuluyor (kaba taramanin yanlis pozitifi)
    }
    else if (members == null) missing.Add(line);
    else partial.Add(line);
}
if (diagPath != null)
{
    Console.WriteLine($"# toplam referans {refs.Count}, cozulen {ok}, DOGRULANMIS EKSIK {confirmed.Count} (cagirani diag'da stub/skip)\n");
    Console.WriteLine("## EKSIK (corelib/frontend) — stub'lanmis govdelerin ihtiyac duydugu BCL uyeleri");
    foreach (var m in confirmed) Console.WriteLine("  " + m);
}
else
{
    Console.WriteLine($"# toplam referans {refs.Count}, corelib'de var {ok}, TIP YOK {missing.Count}, UYE YOK (tip var) {partial.Count}\n");
    Console.WriteLine("## TIP YOK"); foreach (var m in missing) Console.WriteLine("  " + m);
    Console.WriteLine("\n## UYE YOK (tip var — uye adi kaba eslesme, yanlis pozitif olabilir; diag.txt verin)"); foreach (var m in partial) Console.WriteLine("  " + m);
}

sealed class NameProvider : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader md;
    public NameProvider(MetadataReader md) => this.md = md;
    public string GetPrimitiveType(PrimitiveTypeCode c) => "System." + c switch { PrimitiveTypeCode.Int32 => "Int32", PrimitiveTypeCode.Single => "Single", PrimitiveTypeCode.Boolean => "Boolean", PrimitiveTypeCode.String => "String", PrimitiveTypeCode.Object => "Object", PrimitiveTypeCode.Void => "Void", PrimitiveTypeCode.Byte => "Byte", PrimitiveTypeCode.Char => "Char", PrimitiveTypeCode.Double => "Double", PrimitiveTypeCode.Int64 => "Int64", PrimitiveTypeCode.Int16 => "Int16", PrimitiveTypeCode.UInt16 => "UInt16", PrimitiveTypeCode.UInt32 => "UInt32", PrimitiveTypeCode.UInt64 => "UInt64", PrimitiveTypeCode.SByte => "SByte", PrimitiveTypeCode.IntPtr => "IntPtr", PrimitiveTypeCode.UIntPtr => "UIntPtr", PrimitiveTypeCode.TypedReference => "TypedReference", _ => c.ToString() };
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) { var td = r.GetTypeDefinition(h); var ns = r.GetString(td.Namespace); return (ns.Length > 0 ? ns + "." : "") + r.GetString(td.Name); }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var tr = r.GetTypeReference(h); string n = r.GetString(tr.Name);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return GetTypeFromReference(r, (TypeReferenceHandle)tr.ResolutionScope, k) + "/" + n;
        var ns = r.GetString(tr.Namespace); return (ns.Length > 0 ? ns + "." : "") + n;
    }
    public string GetTypeFromSpecification(MetadataReader r, object? g, TypeSpecificationHandle h, byte k) => r.GetTypeSpecification(h).DecodeSignature(this, g);
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[,]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetPointerType(string e) => e + "*";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? g, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? g, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool req) => u + (req ? " modreq(" : " modopt(") + m + ")";
    public string GetPinnedType(string e) => e;
}
