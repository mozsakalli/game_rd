using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DigitoyEngine.Language;

namespace DigitoyEngine.Frontend
{
    public sealed class CsCompilationResult
    {
        public readonly List<Code> Codes = new List<Code>();
        public readonly List<CsError> Diagnostics = new List<CsError>();

        public List<Code> RequireSuccess()
        {
            if (Diagnostics.Count == 0) return Codes;
            throw new CsCompilationException(Diagnostics);
        }
    }

    public sealed class CsCompilationException : System.Exception
    {
        public readonly IReadOnlyList<CsError> Diagnostics;

        public CsCompilationException(IReadOnlyList<CsError> diagnostics)
            : base($"Compilation failed with {diagnostics.Count} error(s):\n" +
                string.Join("\n", diagnostics.Select(error => error.Message)))
        {
            Diagnostics = diagnostics;
        }
    }

    // MiniCs -> IR derleyicisi. Uc acik faz (eski derleyicinin ortuk sira sozlesmelerinin aksine):
    //   1) DeclareTypes: tum class/struct'lar FQ adla Primitive olarak kaydedilir (once bos, sonra
    //      parent+alan doldurulur -> karsilikli referans sirasiz calisir)
    //   2) DeclareMethods: imzalar kaydedilir; method adi "ad_ArgTip1_ArgTip2" olarak MANGLE edilir
    //      (overload'lar dahil BASTAN unique; Owner FQ oldugundan C sembolu da unique)
    //   3) EmitBodies: AST -> Op; her ifade emisyonu statik tipini dondurur (tip cozumu = emisyon)
    // Kontrol akisi dogrudan Label/Br'ye lower edilir; && ve || KISA DEVRE calisir (temp local ile,
    // jump hedefinde stack bos kalir - CTranspiler kurali).
    public class CsCompiler
    {
        readonly Context ctx;
        string file;
        readonly Dictionary<string, Primitive> declared = new Dictionary<string, Primitive>(); // FQ ad -> tip
        List<string> usings = new List<string>(); // dosyanin using direktifleri (ResolveType arama sirasi)
        List<string> staticUsingNames = new List<string>();
        List<Primitive> staticUsings = new List<Primitive>();
        readonly Dictionary<(Primitive elem, int rank), Primitive> arrayCache = new Dictionary<(Primitive, int), Primitive>(); // elem+rank -> T[]/T[,]
        // gorunurdeki tip parametreleri (sinif + method); ResolveType 'T' adini burada cozer
        List<Primitive> typeParamScope = new List<Primitive>();
        // Apply node kimlik cache'i: ayni template+arguman -> AYNI node (esitlik/atanabilirlik referansla)
        readonly List<(Primitive tmpl, List<Primitive> args, Primitive node)> applyNodes = new List<(Primitive, List<Primitive>, Primitive)>();
        readonly Dictionary<Primitive, Primitive> ptrCache = new Dictionary<Primitive, Primitive>(); // struct lvalue zinciri tip etiketi
        public Primitive Ptr(Primitive t)
        {
            if (!ptrCache.TryGetValue(t, out var p)) ptrCache[t] = p = Primitive.PointerOf(t);
            return p;
        }
        // iterator method -> lowering ciktilari (foreach tuketimi icin); anahtar: OwnerFQ$mangled
        readonly Dictionary<string, (Primitive frame, Code create, Code moveNext, Primitive elem)> iterators
            = new Dictionary<string, (Primitive, Code, Code, Primitive)>();
        readonly List<Code> extraCodes = new List<Code>(); // sentezlenen lambda govdeleri
        int lambdaCounter;

        public static List<Code> Compile(Context ctx, string source, string fileName = "")
        {
            source = CsPreprocess.Run(source, fileName); // #if/#define: satir numarasi koruyan (bosluk doldurma)
            var classes = MergePartials(FlattenNestedTypes(MergePartials(CsParser.Parse(source, fileName, out var usings, out var staticUsings), fileName)), fileName);
            var c = new CsCompiler(ctx, fileName);
            c.usings = usings;
            c.staticUsingNames = staticUsings;
            c.DesugarProperties(classes);
            c.SynthesizeInitializerCtors(classes);
            c.DeclareTypes(classes);
            c.ResolveStaticUsings();
            var methods = c.DeclareMethods(classes);
            c.EmitBodies(methods);
            var result = new List<Code>();
            foreach (var (_, _, code, isIterator) in methods)
            {
                if (isIterator)
                {
                    var info = c.iterators[code.Owner.Name + "$" + code.Name];
                    result.Add(info.create);
                    result.Add(info.moveNext);
                }
                else result.Add(code);
            }
            result.AddRange(c.extraCodes); // lambda govdeleri de emisyona girsin
            return result;
        }

        public static CsCompilationResult CompileAll(Context ctx, IEnumerable<(string source, string fileName)> units)
        {
            var result = new CsCompilationResult();
            var allClasses = new List<CsClass>();
            var allUsings = new HashSet<string>();
            var allStaticUsings = new HashSet<string>();
            foreach (var (source, fileName) in units)
            {
                try
                {
                    var prepared = CsPreprocess.Run(source, fileName);
                    var parsed = MergePartials(FlattenNestedTypes(MergePartials(CsParser.Parse(prepared, fileName, out var usings, out var staticUsings), fileName)), fileName);
                    allClasses.AddRange(parsed);
                    foreach (var u in usings)
                        allUsings.Add(u);
                    foreach (var u in staticUsings)
                        allStaticUsings.Add(u);
                }
                catch (CsError error)
                {
                    result.Diagnostics.Add(error);
                }
            }

            try
            {
                // Dosya ici partial merge parse sirasinda yapilir; burada ise tum compilation unit'leri
                // bir araya geldigi icin cross-file partial parcalari tek tipe birlestirilir.
                allClasses = MergePartials(allClasses, "");

                var c = new CsCompiler(ctx, "");
                c.usings = allUsings.ToList();
                c.staticUsingNames = allStaticUsings.ToList();
                c.DesugarProperties(allClasses);
                c.SynthesizeInitializerCtors(allClasses);
                c.DeclareTypes(allClasses);
                c.ResolveStaticUsings();
                var methods = c.DeclareMethods(allClasses);
                c.EmitBodies(methods, result.Diagnostics);

                foreach (var (_, _, code, isIterator) in methods)
                {
                    if (isIterator)
                    {
                        if (!c.iterators.TryGetValue(code.Owner.Name + "$" + code.Name, out var info)) continue;
                        result.Codes.Add(info.create);
                        result.Codes.Add(info.moveNext);
                    }
                    else result.Codes.Add(code);
                }
                result.Codes.AddRange(c.extraCodes);
            }
            catch (CsError error)
            {
                result.Diagnostics.Add(error);
            }
            return result;
        }

        // ---- faz 0: property desugar (C# modeli: get_X/set_X methodlari + auto ise backing field) ----
        // Erisim cozumu BodyEmitter'da: alan bulunamazsa get_X/set_X aranir -> virtual dispatch,
        // interface, generic ve kalitim mevcut method mekaniginden bedavaya gelir.
        void DesugarProperties(List<CsClass> classes)
        {
            foreach (var cls in classes)
                foreach (var prop in cls.Properties)
                {
                    if (!prop.HasGet && !prop.HasSet)
                        throw new CsError(file, prop.Line, $"property en az bir accessor ister: {prop.Name}");
                    if (prop.IndexParam != null) // indexer -> get_Item(K)/set_Item(K, value) (C# lowering)
                    {
                        if (prop.IsStatic || prop.Init != null) throw new CsError(file, prop.Line, "indexer static/baslatici olamaz");
                        bool ifaceIx = cls.IsInterface;
                        if (!ifaceIx && ((prop.HasGet && prop.GetBody == null) || (prop.HasSet && prop.SetBody == null)))
                            throw new CsError(file, prop.Line, "indexer accessor'lari govdeli olmali (auto yok)");
                        if (prop.HasGet)
                            cls.Methods.Add(new CsMethod { Name = "get_Item", ReturnTypeName = prop.TypeName, IsVirtual = prop.IsVirtual, IsOverride = prop.IsOverride, Params = { prop.IndexParam }, Body = ifaceIx ? new List<Stmt>() : prop.GetBody, Line = prop.Line });
                        if (prop.HasSet)
                            cls.Methods.Add(new CsMethod { Name = "set_Item", ReturnTypeName = "void", IsVirtual = prop.IsVirtual, IsOverride = prop.IsOverride, Params = { prop.IndexParam, new CsParam { TypeName = prop.TypeName, Name = "value" } }, Body = ifaceIx ? new List<Stmt>() : prop.SetBody, Line = prop.Line });
                        continue;
                    }
                    if (prop.IsStatic && (prop.IsVirtual || prop.IsOverride))
                        throw new CsError(file, prop.Line, $"static property sanal olamaz: {prop.Name}");
                    bool anyBody = prop.GetBody != null || prop.SetBody != null;
                    if (prop.IsExtern) // native property: accessor'lar corelib.c/intrinsics'te, backing field YOK
                    {
                        if (anyBody || prop.Init != null)
                            throw new CsError(file, prop.Line, $"extern property govde/baslatici alamaz: {prop.Name}");
                        if (prop.HasGet)
                            cls.Methods.Add(new CsMethod { Name = "get_" + prop.Name, ReturnTypeName = prop.TypeName, IsStatic = prop.IsStatic, IsVirtual = prop.IsVirtual, IsOverride = prop.IsOverride, IsExtern = true, Line = prop.Line });
                        if (prop.HasSet)
                            cls.Methods.Add(new CsMethod { Name = "set_" + prop.Name, ReturnTypeName = "void", IsStatic = prop.IsStatic, IsVirtual = prop.IsVirtual, IsOverride = prop.IsOverride, IsExtern = true, Params = { new CsParam { TypeName = prop.TypeName, Name = "value" } }, Line = prop.Line });
                        continue;
                    }
                    if (cls.IsInterface)
                    {
                        if (anyBody) throw new CsError(file, prop.Line, $"interface property'si govdesiz olmali: {prop.Name}");
                        if (prop.Init != null) throw new CsError(file, prop.Line, $"interface property'si deger alamaz: {prop.Name}");
                    }
                    else if (anyBody)
                    {
                        if ((prop.HasGet && prop.GetBody == null) || (prop.HasSet && prop.SetBody == null))
                            throw new CsError(file, prop.Line, $"accessor'lar ya hep govdeli ya hep auto olmali: {prop.Name}");
                        if (prop.Init != null)
                            throw new CsError(file, prop.Line, $"baslatici yalniz auto property'de: {prop.Name}");
                    }
                    else // auto: backing field + trivial govdeler
                    {
                        var bk = prop.Name + "__bk";
                        cls.Fields.Add(new CsField { TypeName = prop.TypeName, Name = bk, IsStatic = prop.IsStatic, Init = prop.Init });
                        prop.GetBody = new List<Stmt> { new SReturn { Line = prop.Line, E = new EName { Line = prop.Line, Name = bk } } };
                        prop.SetBody = new List<Stmt> { new SAssign { Line = prop.Line, Target = new EName { Line = prop.Line, Name = bk }, Value = new EName { Line = prop.Line, Name = "value" } } };
                    }
                    if (prop.HasGet)
                        cls.Methods.Add(new CsMethod
                        {
                            Name = "get_" + prop.Name,
                            ReturnTypeName = prop.TypeName,
                            IsStatic = prop.IsStatic,
                            IsVirtual = prop.IsVirtual,
                            IsOverride = prop.IsOverride,
                            ExplicitInterface = prop.ExplicitInterface,
                            Body = cls.IsInterface ? new List<Stmt>() : prop.GetBody,
                            Line = prop.Line
                        });
                    if (prop.HasSet)
                        cls.Methods.Add(new CsMethod
                        {
                            Name = "set_" + prop.Name,
                            ReturnTypeName = "void",
                            IsStatic = prop.IsStatic,
                            IsVirtual = prop.IsVirtual,
                            IsOverride = prop.IsOverride,
                            ExplicitInterface = prop.ExplicitInterface,
                            Params = { new CsParam { TypeName = prop.TypeName, Name = "value" } },
                            Body = cls.IsInterface ? new List<Stmt>() : prop.SetBody,
                            Line = prop.Line
                        });
                }
        }

        // partial parcalari TEK CsClass'a birlestirilir (sonraki tum fazlar tek sinif gorur:
        // ctor sentezi tum parcalarin alan init'lerini tek ctor'da toplar - C# semantigi).
        // SINIR: parcalar ayni dosyada olmali (birimler ayri derlenir; cross-file icin pipeline degisir).
        static List<CsClass> MergePartials(List<CsClass> classes, string file)
        {
            var byName = new Dictionary<string, CsClass>();
            var result = new List<CsClass>();
            foreach (var cls in classes)
            {
                if (!byName.TryGetValue(RegName(cls), out var first)) // arity ayri tip: Action vs Action<T>
                {
                    byName[RegName(cls)] = cls;
                    result.Add(cls);
                    continue;
                }
                if (!first.IsPartial || !cls.IsPartial)
                    throw new CsError(file, 0, $"tip zaten tanimli (iki parca da 'partial' olmali): {cls.FullName}");
                if (first.IsStruct != cls.IsStruct || first.IsInterface != cls.IsInterface || first.IsEnum || cls.IsEnum)
                    throw new CsError(file, 0, $"partial parcalari ayni tur olmali (class/struct/interface): {cls.FullName}");
                if (string.Join(",", first.GenericParams) != string.Join(",", cls.GenericParams))
                    throw new CsError(file, 0, $"partial parcalarinin tip parametreleri ayni olmali: {cls.FullName}");
                foreach (var b in cls.BaseNames) // base/iface listesi birlesir; cifte base class'i DeclareTypes yakalar
                    if (!first.BaseNames.Contains(b)) first.BaseNames.Add(b);
                first.Fields.AddRange(cls.Fields);
                first.Properties.AddRange(cls.Properties);
                first.Methods.AddRange(cls.Methods);
            }
            return result;
        }

        static List<CsClass> FlattenNestedTypes(List<CsClass> classes)
        {
            var result = new List<CsClass>();
            void Add(CsClass cls)
            {
                result.Add(cls);
                foreach (var nested in cls.NestedTypes)
                    Add(nested);
            }
            foreach (var cls in classes) Add(cls);
            return result;
        }

        // alan baslaticilari ctor/cctor govdesine enjekte edilir; hic ctor yoksa sentezlenir
        void SynthesizeInitializerCtors(List<CsClass> classes)
        {
            foreach (var cls in classes)
            {
                if (cls.IsInterface) continue;
                if (cls.IsStruct)
                {
                    if (cls.Fields.Exists(f => !f.IsStatic && f.Init != null && !f.IsConst)) throw new CsError(file, 0, $"struct instance alan baslaticisi desteklenmiyor (C# klasik kurali): {cls.FullName}");
                    continue; // ctor'lara izin var (this by-ref); alan init'i yok -> sentez gereksiz
                }
                if (cls.Fields.Exists(f => !f.IsStatic && f.Init != null && !f.IsConst) && !cls.Methods.Exists(m => m.IsCtor && m.Params.Count == 0))
                    cls.Methods.Add(new CsMethod { Name = "ctor", ReturnTypeName = "void", IsCtor = true });
                if (cls.Fields.Exists(f => f.IsStatic && f.Init != null && !f.IsConst) && !cls.Methods.Exists(m => m.IsCctor))
                    cls.Methods.Add(new CsMethod { Name = "cctor", ReturnTypeName = "void", IsCctor = true, IsStatic = true });
            }
        }

        // ---- faz 1: tipler ----
        // generic tipler arity-suffix'li kaydedilir (CLR gibi: Action`1, Action`2 ayri) - ayni ad farkli arite
        static string RegName(CsClass cls) => cls.GenericParams.Count > 0 ? cls.FullName + "`" + cls.GenericParams.Count : cls.FullName;

        void DeclareTypes(List<CsClass> classes)
        {
            foreach (var cls in classes)
            {
                file = cls.SourceFile;
                if (cls.IsEnum) // alt tip int; uyeler derleme zamani sabitleri
                {
                    var ep = new Primitive { Name = cls.FullName, Type = PrimitiveType.Int, IsEnum = true, EnumMembers = new Dictionary<string, int>() };
                    int next = 0;
                    foreach (var m in cls.EnumMembers)
                    {
                        if (m.Value != null)
                            next = ConstInt(m.Value) ?? throw new CsError(file, m.Line, $"enum degeri sabit tam sayi olmali: {m.Name}");
                        if (ep.EnumMembers.ContainsKey(m.Name)) throw new CsError(file, m.Line, $"tekrarlanan enum uyesi: {m.Name}");
                        ep.EnumMembers[m.Name] = next++;
                    }
                    declared[cls.FullName] = ep;
                    ctx.RegisterPrimitive(ep);
                    continue;
                }
                var root = WellKnown.RuntimeRootFor(cls.FullName); // Object/String/primitive: uye bagla, tip yaratma
                if (root != null)
                {
                    if (cls.IsInterface || cls.GenericParams.Count > 0)
                        throw new CsError(file, 0, $"{cls.Name} bildirimi yalniz method icerebilir (runtime tipi)");
                    declared[cls.FullName] = root;
                    ctx.RegisterPrimitiveAlias(cls.FullName, root);
                    continue;
                }
                if (cls.IsDelegate) // referans tipi; imza 2. geciste cozulur (ileri tip referanslari)
                {
                    var dp = new Primitive { Name = RegName(cls), Type = PrimitiveType.Model, IsDelegate = true };
                    foreach (var gp in cls.GenericParams) // Action<T>/Func<T,R> gibi generic delegate sablonlari
                        dp.GenericParameters.Add(new Primitive { Name = gp, Type = PrimitiveType.Model, IsGenericParameter = true });
                    ApplyReferenceConstraints(dp.GenericParameters, cls.GenericConstraints);
                    declared[RegName(cls)] = dp;
                    ctx.RegisterPrimitive(dp);
                    continue;
                }
                var p = new Primitive { Name = RegName(cls), Type = PrimitiveType.Model, IsStruct = cls.IsStruct, IsInterface = cls.IsInterface };
                if (ctx.TryGetPrimitive(RegName(cls), out _)) // birimler arasi cift bildirim sessizce ezilmesin
                    throw new CsError(file, 0, $"tip baska bir dosyada zaten tanimli: {cls.FullName} (partial parcalari ayni dosyada olmali - v1 siniri)");
                foreach (var gp in cls.GenericParams) // tip parametresi Model sayilir: uyesiz opak referans (kisit yok)
                    p.GenericParameters.Add(new Primitive { Name = gp, Type = PrimitiveType.Model, IsGenericParameter = true });
                ApplyReferenceConstraints(p.GenericParameters, cls.GenericConstraints);
                declared[RegName(cls)] = p;
                ctx.RegisterPrimitive(p);
            }
            foreach (var cls in classes)
            {
                file = cls.SourceFile;
                if (cls.IsEnum) continue;
                if (cls.IsDelegate)
                {
                    var dp = declared[RegName(cls)];
                    typeParamScope = dp.GenericParameters; // imza T'leri gorebilsin
                    dp.DelegateReturn = ResolveType(cls.DelegateSig.ReturnTypeName, cls.FullName, cls.Line);
                    foreach (var pr in cls.DelegateSig.Params)
                        dp.DelegateParams.Add(ResolveType(pr.TypeName, cls.FullName, cls.Line));
                    typeParamScope = new List<Primitive>();
                    continue;
                }
                var p = declared[RegName(cls)];
                typeParamScope = p.GenericParameters;
                ApplyGenericConstraints(p.GenericParameters, cls.GenericConstraints, cls.FullName, cls.Line);
                foreach (var bn in cls.BaseNames) // base listesi: en fazla 1 class + n interface (parser tipsiz, ayrim burada)
                {
                    var b = ResolveType(bn, cls.FullName, cls.Line);
                    if (b.IsInterface)
                    {
                        if (cls.IsStruct) throw new CsError(file, 0, $"v1: struct interface implement edemez: {cls.FullName}");
                        p.Interfaces.Add(b);
                    }
                    else if (p.Parent != null) throw new CsError(file, 0, $"birden fazla base class: {cls.FullName}");
                    else p.Parent = b;
                }
                if (p.Parent == null && !cls.IsStruct && !cls.IsInterface && p != Primitive.Object)
                    p.Parent = Primitive.Object; // C#: base'siz class ortuk olarak object'ten turer
                foreach (var f in cls.Fields)
                {
                    Primitive ft;
                    if (f.IsConst) // depolama yok: derleme zamani sabiti (folding gorur)
                    {
                        var ctype = ResolveType(f.TypeName, cls.FullName, f.Line);
                        var cval = ConstValue(f.Init, p) ?? throw new CsError(file, f.Line, $"const degeri sabit ifade olmali: {cls.FullName}.{f.Name}");
                        if (p.Constants == null) p.Constants = new Dictionary<string, (Primitive, object)>();
                        p.Constants[f.Name] = (ctype, cval);
                        continue;
                    }
                    if (f.FixedCount > 0)
                    {
                        var elem = ResolveType(f.TypeName, cls.FullName, f.Line);
                        if (elem.Type == PrimitiveType.Model || elem.Type == PrimitiveType.Array)
                            throw new CsError(file, f.Line, $"fixed eleman tipi skaler olmali: {cls.FullName}.{f.Name}");
                        ft = Primitive.FixedArrayOf(elem, f.FixedCount);
                    }
                    else ft = ResolveType(f.TypeName, cls.FullName, f.Line);
                    p.AddField(new PrimitiveField { Name = f.Name, Type = ft, IsStatic = f.IsStatic, IsReadonly = f.IsReadonly });
                }
                foreach (var prop in cls.Properties)
                    if (prop.IndexParam == null) // get_Item/set_Item indexer'ları reflection property değildir
                        p.Properties.Add(new PrimitiveProperty
                        {
                            Name = prop.Name,
                            Type = ResolveType(prop.TypeName, cls.FullName, prop.Line),
                            IsStatic = prop.IsStatic,
                            HasGet = prop.HasGet,
                            HasSet = prop.HasSet
                        });
                typeParamScope = new List<Primitive>();
            }
        }

        Primitive ResolveType(string name, string ns, int line)
        {
            var t = ResolveTypeAllowTemplate(name, ns, line);
            if (t.IsGeneric && t.GenericTemplate == null)
                throw new CsError(file, line, $"generic tip arguman ister: {t.Name}<...>");
            return t;
        }

        void ResolveStaticUsings()
        {
            staticUsings.Clear();
            foreach (var name in staticUsingNames)
            {
                var type = ResolveType(name, "", 0);
                if (!staticUsings.Contains(type)) staticUsings.Add(type);
            }
        }

        // sabit tam sayi ifadesi (enum degerleri): literal ya da -literal
        static int? ConstInt(Expr e)
        {
            if (e is ELit l && l.Tag == "int") return int.Parse(l.Value);
            if (e is EUn u && u.Op == "-" && u.E is ELit il && il.Tag == "int") return -int.Parse(il.Value);
            if (e is EUn un && un.Op == "~")
            {
                var value = ConstInt(un.E);
                return value != null ? ~value.Value : null;
            }
            if (e is EBin bin)
            {
                var left = ConstInt(bin.L);
                var right = ConstInt(bin.R);
                if (left == null || right == null) return null;
                unchecked
                {
                    switch (bin.Op)
                    {
                        case "+": return left.Value + right.Value;
                        case "-": return left.Value - right.Value;
                        case "*": return left.Value * right.Value;
                        case "/": return right.Value != 0 ? left.Value / right.Value : (int?)null;
                        case "%": return right.Value != 0 ? left.Value % right.Value : (int?)null;
                        case "<<": return left.Value << (right.Value & 31);
                        case ">>": return left.Value >> (right.Value & 31);
                        case "&": return left.Value & right.Value;
                        case "|": return left.Value | right.Value;
                        case "^": return left.Value ^ right.Value;
                    }
                }
            }
            return null;
        }

        // const alan degeri: onceki const alanlar + literal/unary/binary sabit ifadeleri.
        // Declaration sirasiyla degerlendirilir; ileri referans C# gibi gecersiz kalir.
        object ConstValue(Expr e, Primitive owner)
        {
            if (e is EName name)
            {
                var constant = FindConstant(owner, name.Name);
                return constant?.value;
            }
            if (e is EField field && field.Obj is EName typeName)
            {
                if (typeName.Name == "int")
                    return field.Name == "MaxValue" ? int.MaxValue : field.Name == "MinValue" ? int.MinValue : null;
                if (typeName.Name == "uint")
                    return field.Name == "MaxValue" ? uint.MaxValue : field.Name == "MinValue" ? uint.MinValue : null;
                if (typeName.Name == "long")
                    return field.Name == "MaxValue" ? long.MaxValue : field.Name == "MinValue" ? long.MinValue : null;
                if (typeName.Name == "ulong")
                    return field.Name == "MaxValue" ? ulong.MaxValue : field.Name == "MinValue" ? ulong.MinValue : null;
                if (typeName.Name == "short")
                    return field.Name == "MaxValue" ? (int)short.MaxValue : field.Name == "MinValue" ? (int)short.MinValue : null;
                if (typeName.Name == "byte")
                    return field.Name == "MaxValue" ? (int)byte.MaxValue : field.Name == "MinValue" ? (int)byte.MinValue : null;
            }
            if (e is EUn u)
            {
                var inner = ConstValue(u.E, owner);
                if (u.Op == "-" && inner is int i) return -i;
                if (u.Op == "-" && inner is float f) return -f;
                if (u.Op == "-" && inner is double dd) return -dd;
                if (u.Op == "~" && inner is int bits) return ~bits;
                return null;
            }
            if (e is EBin binary)
            {
                var left = ConstValue(binary.L, owner);
                var right = ConstValue(binary.R, owner);
                if (left is string ls && right is string rs && binary.Op == "+") return string.Intern(ls + rs);
                if (left is int li && right is int ri)
                    unchecked
                    {
                        return binary.Op switch
                        {
                            "+" => li + ri,
                            "-" => li - ri,
                            "*" => li * ri,
                            "/" => ri != 0 ? li / ri : null,
                            "%" => ri != 0 ? li % ri : null,
                            "<<" => li << (ri & 31),
                            ">>" => li >> (ri & 31),
                            "&" => li & ri,
                            "|" => li | ri,
                            "^" => li ^ ri,
                            _ => null
                        };
                    }
                if (left is float lf && right is float rf)
                    return binary.Op switch { "+" => lf + rf, "-" => lf - rf, "*" => lf * rf, "/" => lf / rf, "%" => lf % rf, _ => null };
                if (left is float lff && right is int rfi)
                    return binary.Op switch { "+" => lff + rfi, "-" => lff - rfi, "*" => lff * rfi, "/" => lff / rfi, "%" => lff % rfi, _ => null };
                if (left is int lif && right is float rff)
                    return binary.Op switch { "+" => lif + rff, "-" => lif - rff, "*" => lif * rff, "/" => lif / rff, "%" => lif % rff, _ => null };
                if (left is double ld && right is double rd)
                    return binary.Op switch { "+" => ld + rd, "-" => ld - rd, "*" => ld * rd, "/" => ld / rd, "%" => ld % rd, _ => null };
                if (left is double ldf && right is int rdi)
                    return binary.Op switch { "+" => ldf + rdi, "-" => ldf - rdi, "*" => ldf * rdi, "/" => ldf / rdi, "%" => ldf % rdi, _ => null };
                if (left is int lid && right is double rdf)
                    return binary.Op switch { "+" => lid + rdf, "-" => lid - rdf, "*" => lid * rdf, "/" => lid / rdf, "%" => lid % rdf, _ => null };
                return null;
            }
            if (!(e is ELit l)) return null;
            return l.Tag switch
            {
                "int" => int.Parse(l.Value),
                "uint" => uint.Parse(l.Value),
                "long" => long.Parse(l.Value),
                "ulong" => ulong.Parse(l.Value),
                "float" => float.Parse(l.Value, CultureInfo.InvariantCulture),
                "double" => double.Parse(l.Value, CultureInfo.InvariantCulture),
                "bool" => l.Value == "true",
                "str" => string.Intern(l.Value),
                "chr" => l.Value[0],
                _ => null
            };
        }

        // const arama: sinif zinciri boyunca (kalitilan const'lar gorunur)
        public (Primitive type, object value)? FindConstant(Primitive cls, string name)
        {
            for (var t = cls; t != null; t = ParentView(t))
            {
                var tpl = t.GenericTemplate ?? t;
                if (tpl.Constants != null && tpl.Constants.TryGetValue(name, out var cv)) return cv;
            }
            return null;
        }

        Primitive ResolveTypeAllowTemplate(string name, string ns, int line)
        {
            if (name.EndsWith("*"))
                return Ptr(ResolveType(name.Substring(0, name.Length - 1), ns, line));
            int arrayOpen = name.LastIndexOf('[');
            if (arrayOpen >= 0 && name.EndsWith("]")) // dizi: eleman+rank basina TEK Primitive (referans esitligi korunur)
            {
                var suffix = name.Substring(arrayOpen + 1, name.Length - arrayOpen - 2);
                if (suffix.Any(ch => ch != ',')) throw new CsError(file, line, $"bozuk dizi tipi: {name}");
                var elem = ResolveType(name.Substring(0, arrayOpen), ns, line);
                return ArrayType(elem, suffix.Length + 1); // struct elemanlar dahil (GC-ref alanli struct dizisi ENewArray'de engellenir)
            }
            int lt = name.IndexOf('<');
            if (lt >= 0) // generic uygulama: "Box<int>", "Pair<int,Box<float>>"
            {
                var argNames = SplitTypeArgs(name.Substring(lt + 1, name.Length - lt - 2), line);
                Primitive template;
                try { template = ResolveTypeAllowTemplate(name.Substring(0, lt) + "`" + argNames.Count, ns, line); } // arity-suffix'li kayit (Action`1 vs Action`2)
                catch (CsError) { template = ResolveTypeAllowTemplate(name.Substring(0, lt), ns, line); }
                if (!template.IsGeneric)
                    throw new CsError(file, line, $"generic olmayan tipe arguman verilemez: {name}");
                if (argNames.Count != template.GenericParameters.Count)
                    throw new CsError(file, line, $"tip argumani sayisi uyusmuyor: {name} ({template.GenericParameters.Count} bekleniyordu)");
                var args = argNames.Select(a => ResolveType(a, ns, line)).ToList();
                return ApplyType(template, args);
            }
            foreach (var gp in typeParamScope) // gorunurdeki T/U parametreleri her seyden once
                if (gp.Name == name) return gp;
            foreach (var u in usings)
            {
                int equal = u.IndexOf('=');
                if (equal >= 0 && name == u.Substring(0, equal))
                    return ResolveType(u.Substring(equal + 1), ns, line);
            }
            switch (name)
            {
                case "int": return Primitive.Int;
                case "uint": return Primitive.UInt;
                case "float": return Primitive.Float;
                case "double": return Primitive.Double;
                case "long": return Primitive.Long;
                case "ulong": return Primitive.ULong;
                case "short": return Primitive.Short;
                case "ushort": return Primitive.UShort;
                case "byte": return Primitive.Byte;
                case "sbyte": return Primitive.SByte;
                case "char": return Primitive.Char;
                case "bool": return Primitive.Bool;
                case "object": return Primitive.Object;
                case "string": return Primitive.String;
                case "void": return Primitive.Void;
            }
            if (declared.TryGetValue(name, out var p)) return p; // FQ yazilmis
            for (var scan = ns; scan.Length > 0; scan = scan.Contains('.') ? scan.Substring(0, scan.LastIndexOf('.')) : "")
            {
                if (declared.TryGetValue(scan + "." + name, out p)) return p; // ayni namespace + ust namespace'ler
                if (ctx.TryGetPrimitive(scan + "." + name, out p)) return p;
            }
            // C# nested type lookup: Derived icinde bare `DirectionEnum`, base class'in
            // `UISprite.DirectionEnum` nested uyesine de baglanabilir. Tum tipler faz 1'de
            // kayitli oldugu icin burada kaynak sirasi bagimliligi yoktur.
            if (ctx.TryGetPrimitive(ns, out var owner))
                for (var baseType = owner.Parent; baseType != null; baseType = ParentView(baseType))
                    if (ctx.TryGetPrimitive(baseType.Name + "." + name, out p)) return p;
            Primitive staticImportedType = null;
            foreach (var importedOwner in staticUsings)
                if (ctx.TryGetPrimitive(importedOwner.Name + "." + name, out var candidate))
                {
                    if (staticImportedType != null && staticImportedType != candidate)
                        throw new CsError(file, line, $"using static nested tip belirsiz: {name}");
                    staticImportedType = candidate;
                }
            if (staticImportedType != null) return staticImportedType;
            foreach (var u in usings) // using direktifleri (C# arama sirasi)
            {
                if (u.IndexOf('=') >= 0) continue;
                if (declared.TryGetValue(u + "." + name, out p)) return p;
                if (ctx.TryGetPrimitive(u + "." + name, out p)) return p;
            }
            if (ctx.TryGetPrimitive(name, out p)) return p; // onceden kayitli (baska derleme birimi)
            throw new CsError(file, line, $"bilinmeyen tip: {name}");
        }

        // "int,Box<float>,T" ust-duzey virgullerden boler (ic ice <> derinligi sayilir)
        List<string> SplitTypeArgs(string s, int line)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) { parts.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            if (start >= s.Length) throw new CsError(file, line, $"bozuk tip argumani listesi: {s}");
            parts.Add(s.Substring(start));
            return parts;
        }

        Primitive ArrayType(Primitive elem, int rank = 1)
        {
            var key = (elem, rank);
            if (!arrayCache.TryGetValue(key, out var arr))
                arrayCache[key] = arr = Primitive.ArrayOf(elem, rank);
            return arr;
        }

        public Primitive ApplyType(Primitive template, List<Primitive> args)
        {
            foreach (var (t, a, n) in applyNodes)
                if (t == template && a.Count == args.Count && !a.Where((x, i) => x != args[i]).Any())
                    return n;
            var node = Primitive.Apply(template, args.ToArray());
            applyNodes.Add((template, new List<Primitive>(args), node));
            return node;
        }

        // ---- Apply gorunumu: template uyelerinin tiplerini arguman haritasiyla degistirir.
        // INSTANTIATE ETMEZ (o Resolver'in isi) - ic ice generic'ler yine Apply node olur.
        public Dictionary<Primitive, Primitive> MapOf(Primitive apply)
        {
            var map = new Dictionary<Primitive, Primitive>();
            for (int i = 0; i < apply.GenericTemplate.GenericParameters.Count; i++)
                map[apply.GenericTemplate.GenericParameters[i]] = apply.TypeArguments[i];
            return map;
        }

        public Primitive SubstView(Primitive t, Dictionary<Primitive, Primitive> map)
        {
            if (t == null || map == null) return t;
            if (map.TryGetValue(t, out var m)) return m;
            if (t.GenericTemplate != null)
                return ApplyType(t.GenericTemplate, t.TypeArguments.Select(a => SubstView(a, map)).ToList());
            if (t.Type == PrimitiveType.Array && t.ElementType != null)
            {
                var e = SubstView(t.ElementType, map);
                return e == t.ElementType ? t : ArrayType(e, t.ArrayRank);
            }
            return t;
        }

        // kalitim zinciri gorunumu: Apply dugumunun parent'i template parent'inin substitusyonu
        public Primitive ParentView(Primitive t) =>
            t.GenericTemplate != null ? SubstView(t.GenericTemplate.Parent, MapOf(t)) : t.Parent;

        // alan aramasi zincir gorunumuyle: bulundugu dugumu de dondurur (Apply ise op TypeArguments alir)
        public (PrimitiveField f, Primitive owner) FindFieldView(Primitive cls, string name, bool isStatic)
        {
            for (var t = cls; t != null; t = ParentView(t))
            {
                var tpl = t.GenericTemplate ?? t;
                foreach (var f in isStatic ? tpl.StaticFields : tpl.Fields)
                    if (f.Name == name) return (f, t);
            }
            return (null, null);
        }

        public Primitive FieldTypeFor(PrimitiveField f, Primitive ownerView) =>
            ownerView != null && ownerView.GenericTemplate != null ? SubstView(f.Type, MapOf(ownerView)) : f.Type;

        // ---- faz 2: imzalar ----
        // Cagri cozumu icin tablo: mangled ctx anahtarindan bagimsiz, basit ad + parametre bilgisi.
        class MethodEntry
        {
            public string SimpleName;
            public Code Code;
            public CsMethod Ast; // Default ifadeler icin (null = elle kurulmus)
            public List<Primitive> ParamTypes = new List<Primitive>(); // this haric
            public List<CsParam> Params;
        }
        readonly Dictionary<Primitive, List<MethodEntry>> methodTable;

        CsCompiler(Context ctx, string file)
        {
            this.ctx = ctx;
            this.file = file;
            // method cozum tablosu derleme birimleri ARASI paylasilir (prelude/onceki dosyalarin
            // methodlari sonraki birimlerden cagrilabilsin); Context'te opak tasinir
            methodTable = ctx.FrontendState as Dictionary<Primitive, List<MethodEntry>>;
            if (methodTable == null)
                ctx.FrontendState = methodTable = new Dictionary<Primitive, List<MethodEntry>>();
        }

        static string Mangle(string methodName, IEnumerable<Primitive> paramTypes) => Code.Mangle(methodName, paramTypes);

        static string MangleWithMods(string methodName, List<CsParam> ps, List<Primitive> types)
        {
            var sb = new System.Text.StringBuilder(methodName);
            for (int i = 0; i < types.Count; i++)
            {
                sb.Append('_');
                if (ps[i].IsRef) sb.Append("ref_");
                else if (ps[i].IsOut) sb.Append("out_");
                sb.Append(types[i].Name.Replace('.', '_'));
            }
            return sb.ToString();
        }

        List<(CsClass cls, CsMethod m, Code code, bool isIterator)> DeclareMethods(List<CsClass> classes)
        {
            var result = new List<(CsClass, CsMethod, Code, bool)>();
            foreach (var cls in classes)
            {
                file = cls.SourceFile;
                var owner = declared[RegName(cls)];
                foreach (var m in cls.Methods)
                {
                    bool isIterator = HasYield(m.Body);
                    if (isIterator && (m.IsVirtual || m.IsOverride || m.IsCtor || m.IsCctor || m.IsFinalizer))
                        throw new CsError(file, m.Line, $"iterator method virtual/override/ctor/finalizer olamaz: {m.Name}");
                    if (isIterator && (cls.GenericParams.Count > 0 || m.GenericParams.Count > 0))
                        throw new CsError(file, m.Line, $"iterator generic baglamda desteklenmiyor (frame sinifi tasarimi acik - gerekirse tartisalim): {m.Name}");
                    if (isIterator && m.ReturnTypeName == "void")
                        throw new CsError(file, m.Line, $"iterator method donus tipi eleman tipi olmali (void degil): {m.Name}");
                    // Gercek C# imzasi: IEnumerable<T>/IEnumerator<T> doner -> ic konvansiyonumuz eleman tipidir, T'yi ac.
                    var returnTypeName = m.ReturnTypeName;
                    if (isIterator)
                    {
                        var rtn = returnTypeName;
                        var lt = rtn.IndexOf('<');
                        if (lt > 0 && rtn.EndsWith(">"))
                        {
                            var head = rtn.Substring(0, lt);
                            var shortHead = head.Substring(head.LastIndexOf('.') + 1);
                            if (shortHead == "IEnumerable" || shortHead == "IEnumerator")
                                returnTypeName = rtn.Substring(lt + 1, rtn.Length - lt - 2);
                        }
                    }
                    var mgps = m.GenericParams.Select(n => new Primitive { Name = n, Type = PrimitiveType.Model, IsGenericParameter = true }).ToList();
                    typeParamScope = owner.GenericParameters.Concat(mgps).ToList();
                    ApplyGenericConstraints(mgps, m.GenericConstraints, cls.FullName, m.Line);
                    if (owner.IsStruct && m.IsVirtual)
                        throw new CsError(file, m.Line, $"struct methodu sanal olamaz (boxing yok): {m.Name}");
                    // A value type can override Object members in C# (for example Color.ToString).
                    // Emit it as a direct struct method: no boxed value enters an object vtable here.
                    bool structDirectOverride = owner.IsStruct && m.IsOverride;
                    var paramTypes = m.Params.Select(pp => ResolveType(pp.TypeName, cls.FullName, m.Line)).ToList();
                    if (m.Params.Exists(p => p.IsParams) && (!m.Params[m.Params.Count - 1].IsParams || paramTypes[paramTypes.Count - 1].Type != PrimitiveType.Array))
                        throw new CsError(file, m.Line, "params yalniz son dizi parametresi olabilir");
                    var simpleName = m.IsCtor ? "ctor" : m.IsCctor ? "cctor" : m.IsFinalizer ? "finalize" : m.Name;
                    var dispName = m.IsCtor ? ".ctor" : m.IsCctor ? ".cctor" : m.IsFinalizer ? "Finalize" : m.Name;
                    var explicitInterface = m.ExplicitInterface != null
                        ? ResolveType(m.ExplicitInterface, cls.FullName, m.Line)
                        : null;
                    var mangledName = MangleWithMods(simpleName, m.Params, paramTypes);
                    var code = new Code
                    {
                        Owner = owner,
                        Name = explicitInterface == null
                            ? mangledName
                            : "__iface_" + explicitInterface.Name.Replace('.', '_') + "__" + mangledName,
                        IsStatic = m.IsStatic,
                        IsVirtual = m.IsVirtual || cls.IsInterface, // iface methodu ortuk virtual: iface VTable'inda slot acar
                        IsOverride = m.IsOverride && !structDirectOverride,
                        IsExternal = m.IsExtern, // C: extern prototip (govde c_runtime/corelib.c'de); VM: intrinsics tablosu
                        SourceFile = file,
                        DisplayName = dispName + "(" + string.Join(", ", m.Params.Select((pp, pi) =>
                            (pp.IsRef ? "ref " : pp.IsOut ? "out " : "") + Primitive.CsDisplay(paramTypes[pi]))) + ")",
                        ExplicitInterface = explicitInterface,
                        ReturnType = ResolveType(returnTypeName, cls.FullName, m.Line) // iterator'da = eleman tipi (Lower kullanir; IEnumerable<T> acildi)
                    };
                    if (m.IsExtern && m.GenericParams.Count > 0)
                        throw new CsError(file, m.Line, $"extern method generic olamaz: {m.Name}");
                    code.GenericParameters.AddRange(mgps);
                    typeParamScope = new List<Primitive>();
                    if (!m.IsStatic)
                        code.Arguments.Add(new Argument { Name = "this", Type = owner, IsRef = owner.IsStruct }); // struct this = C# ref (mutasyon gorunur)
                    for (int i = 0; i < m.Params.Count; i++)
                    {
                        if (isIterator && (m.Params[i].IsRef || m.Params[i].IsOut))
                            throw new CsError(file, m.Line, $"iterator parametresi ref/out olamaz: {m.Name}");
                        code.Arguments.Add(new Argument { Name = m.Params[i].Name, Type = paramTypes[i], IsRef = m.Params[i].IsRef, IsOut = m.Params[i].IsOut });
                    }
                    if (!isIterator)
                        ctx.RegisterCode(code); // iterator orijinali kaydedilmez: Yield op'u VM/CTranspiler'a ulasmamali
                    if (!methodTable.TryGetValue(owner, out var entries))
                        methodTable[owner] = entries = new List<MethodEntry>();
                    if (!m.IsFinalizer && explicitInterface == null) // explicit interface uyesi class receiver'iyla dogrudan cagrilamaz
                        entries.Add(new MethodEntry { SimpleName = simpleName, Code = code, Ast = m, ParamTypes = paramTypes, Params = m.Params });
                    if (m.Params.Count > 0 && m.Params[0].IsThis) // extension: alici tipli static (cozum registry'den)
                    {
                        if (!m.IsStatic) throw new CsError(file, m.Line, "extension method static olmali");
                        var reg = ctx.FrontendExtensions as List<(Primitive, string)>;
                        if (reg == null) ctx.FrontendExtensions = reg = new List<(Primitive, string)>();
                        reg.Add((owner, simpleName));
                    }
                    result.Add((cls, m, code, isIterator));
                }
            }
            return result;
        }

        static bool HasYield(List<Stmt> body)
        {
            foreach (var s in body)
                switch (s)
                {
                    case SYield _: case SYieldBreak _: return true;
                    case SIf f: if (HasYield(f.Then) || HasYield(f.Else)) return true; break;
                    case SWhile w: if (HasYield(w.Body)) return true; break;
                    case SForeach fe: if (HasYield(fe.Body)) return true; break;
                    case SFor fr: if (HasYield(fr.Body)) return true; break;
                    case SSwitch sw:
                        foreach (var sec in sw.Sections)
                            if (HasYield(sec.Body)) return true;
                        break;
                }
            return false;
        }

        // ---- faz 3: govdeler (once iterator'lar: tuketici foreach'ler create/moveNext'i bulabilsin) ----
        void EmitBodies(List<(CsClass cls, CsMethod m, Code code, bool isIterator)> methods, List<CsError> diagnostics = null)
        {
            foreach (var (cls, m, code, isIterator) in methods)
            {
                file = cls.SourceFile;
                if (!isIterator) continue;
                try
                {
                    new BodyEmitter(this, declared[RegName(cls)], cls.FullName, code, true).Emit(m.Body);
                    var (frame, create, moveNext) = IteratorLowering.Lower(ctx, code, code.ReturnType);
                    create.SourceFile = file;
                    moveNext.SourceFile = file;
                    iterators[code.Owner.Name + "$" + code.Name] = (frame, create, moveNext, code.ReturnType);
                }
                catch (CsError error) when (diagnostics != null)
                {
                    diagnostics.Add(error);
                }
            }
            foreach (var (cls, m, code, isIterator) in methods)
            {
                file = cls.SourceFile;
                if (isIterator || cls.IsInterface || m.IsExtern) continue; // govdesiz bildirimler
                try
                {
                    var owner = declared[RegName(cls)];
                    typeParamScope = owner.GenericParameters.Concat(code.GenericParameters).ToList();
                    var be = new BodyEmitter(this, owner, cls.FullName, code, false);
                    // C# sirasi: alan baslaticilar -> base/this ctor -> govde. ": this(...)" zincirinde
                    // baslaticilar CALISMAZ (zincirlenen ctor zaten calistirir - cift calisma olmaz).
                    if (m.IsCtor && !(m.ChainArgs != null && m.ChainToThis)) be.EmitInstanceInits(cls, owner);
                    if (m.IsCtor) be.EmitCtorChain(m, owner);
                    if (m.IsCctor) be.EmitStaticInits(cls, owner);
                    be.Emit(m.Body);
                }
                catch (CsError error) when (diagnostics != null)
                {
                    diagnostics.Add(error);
                }
                finally
                {
                    typeParamScope = new List<Primitive>();
                }
            }
        }

        static void ApplyReferenceConstraints(List<Primitive> parameters, List<CsGenericConstraint> constraints)
        {
            foreach (var constraint in constraints)
                if (constraint.Bounds.Contains("class"))
                {
                    var parameter = parameters.FirstOrDefault(value => value.Name == constraint.Parameter);
                    if (parameter != null) parameter.Parent = Primitive.Object;
                }
        }

        void ApplyGenericConstraints(List<Primitive> parameters, List<CsGenericConstraint> constraints, string scope, int line)
        {
            ApplyReferenceConstraints(parameters, constraints);
            foreach (var constraint in constraints)
            {
                var parameter = parameters.FirstOrDefault(value => value.Name == constraint.Parameter);
                if (parameter == null) continue;
                foreach (var boundName in constraint.Bounds)
                {
                    if (boundName == "class" || boundName == "struct" || boundName == "new()") continue;
                    var bound = ResolveType(boundName, scope, line);
                    if (bound.IsInterface)
                    {
                        if (!parameter.Interfaces.Contains(bound)) parameter.Interfaces.Add(bound);
                    }
                    else if (parameter.Parent == null || parameter.Parent == Primitive.Object)
                        parameter.Parent = bound;
                    else if (parameter.Parent != bound)
                        throw new CsError(file, line, $"generic parametre birden fazla class constraint alamaz: {parameter.Name}");
                }
            }
        }

        // Tek method govdesinin emisyon durumu (scope/locals). Ifade emisyonu statik tip dondurur.
        class BodyEmitter
        {
            public class HoistedCell
            {
                public string Name;
                public Primitive Type;
                public int Slot;
                public bool IsLocal;
                public bool IsRef;
                public bool IsOut;
                public Primitive TypeModel;
                public PrimitiveField ValueField;
                public int Local = -1;
            }

            public class CaptureSource
            {
                public HoistedCell Cell;
                public PrimitiveField Field;
                public PrimitiveField ParentField;
            }

            class CompiledLambda
            {
                public Code Code;
                public Primitive Closure;
                public Dictionary<string, CaptureSource> Captures;
            }

            readonly CsCompiler c;
            Primitive currentClass;
            string ns;
            readonly Code code;
            readonly bool isIterator;
            readonly Dictionary<string, CaptureSource> captureSources;
            readonly Dictionary<string, HoistedCell> hoistedCells = new Dictionary<string, HoistedCell>();
            readonly Dictionary<string, int> localSlot = new Dictionary<string, int>();
            List<Op> cur; // emisyon hedefi (arg on-emisyonu icin gecici listeye yonlendirilebilir)
            List<Op> ops => cur;

            public BodyEmitter(CsCompiler c, Primitive currentClass, string ns, Code code, bool isIterator,
                Dictionary<string, CaptureSource> captureSources = null)
            {
                this.c = c; this.currentClass = currentClass; this.ns = ns; this.code = code; this.isIterator = isIterator;
                this.captureSources = captureSources;
                cur = code.Operations;
            }

            Primitive EmitCapture(CaptureSource capture)
            {
                EnsureCaptureField(capture);
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                ops.Add(new Op { Type = OpType.GetField, Field = capture.Field });
                ops.Add(new Op { Type = OpType.GetField, Field = capture.Cell.ValueField });
                return capture.Cell.Type;
            }

            void EnsureCaptureField(CaptureSource capture)
            {
                if (capture.Field != null) return;
                EnsureCell(capture.Cell, code.Owner);
                capture.Field = new PrimitiveField { Name = capture.Cell.Name, Type = capture.Cell.TypeModel };
                code.Owner.AddField(capture.Field);
            }

            void EnsureCell(HoistedCell cell, Primitive closure)
            {
                if (cell.TypeModel != null) return;
                cell.TypeModel = new Primitive
                {
                    Name = closure.Name + "$cell" + closure.Fields.Count,
                    Type = PrimitiveType.Model,
                    Parent = Primitive.Object
                };
                cell.ValueField = new PrimitiveField { Name = "value", Type = cell.Type };
                cell.TypeModel.AddField(cell.ValueField);
                c.ctx.RegisterPrimitive(cell.TypeModel);
            }

            Primitive EmitHoistedRead(HoistedCell cell)
            {
                ops.Add(new Op { Type = OpType.GetLocal, Slot = cell.Local });
                ops.Add(new Op { Type = OpType.GetField, Field = cell.ValueField });
                return cell.Type;
            }

            // ctor'un basina instance alan baslaticilari (C# semantigi: govdeden once)
            public void EmitInstanceInits(CsClass cls, Primitive owner)
            {
                foreach (var f in cls.Fields)
                {
                    if (f.IsStatic || f.Init == null || f.IsConst) continue;
                    var field = FindField(owner, f.Name);
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    if (!(field.Type.IsDelegate && TryEmitDelegateNew(f.Init, field.Type)))
                        EmitExpr(f.Init);
                    ops.Add(new Op { Type = OpType.SetField, Field = field });
                }
            }

            public void EmitStaticInits(CsClass cls, Primitive owner)
            {
                foreach (var f in cls.Fields)
                {
                    if (!f.IsStatic || f.Init == null || f.IsConst) continue;
                    var field = FindStaticField(owner, f.Name);
                    if (!(field.Type.IsDelegate && TryEmitDelegateNew(f.Init, field.Type)))
                        EmitExpr(f.Init);
                    ops.Add(new Op { Type = OpType.SetStatic, Field = field });
                }
            }

            // ctor zinciri: ": base(args)" / ": this(args)"; zincir yazilmamissa ve base'te ctor
            // varsa C# gibi ORTUK parametresiz base() cagrilir (yoksa acik ": base(...)" zorunlu).
            public void EmitCtorChain(CsMethod m, Primitive owner)
            {
                if (m.ChainArgs != null)
                {
                    var target = m.ChainToThis ? owner : owner.Parent
                        ?? throw Err(m.Line, $"base ctor cagrisi icin base class yok: {owner.Name}");
                    var argOps = new List<Op>();
                    var argInfos = EmitArgsTo(argOps, m.ChainArgs);
                    var rc = ResolveMethod(target, "ctor", argInfos);
                    if (rc == null && !m.ChainToThis && m.ChainArgs.Count == 0)
                        return; // ": base()" ve base'te ctor yok (object dahil) -> C# no-op ctor'u
                    if (rc == null)
                        throw Err(m.Line, $"uygun ctor yok: {target.Name}({string.Join(",", argInfos.Select(x => x.Type.Name))})");
                    if (m.ChainToThis && rc.Entry.Code.Owner != owner)
                        throw Err(m.Line, $"this(...) ayni siniftaki ctor'a gitmeli: {owner.Name}");
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    ops.AddRange(argOps);
                    EmitDefaults(rc.Entry, argInfos.Count, m.Line);
                    EmitInvoke(rc);
                    return;
                }
                if (owner.Parent == null || !HasAnyCtor(owner.Parent)) return; // cagrilacak base ctor yok
                var implicitRc = ResolveMethod(owner.Parent, "ctor", new List<ArgInfo>())
                    ?? throw Err(m.Line, $"base'in parametresiz ctor'u yok, \": base(...)\" yazilmali: {owner.Name}");
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                EmitDefaults(implicitRc.Entry, 0, m.Line);
                EmitInvoke(implicitRc);
            }

            bool HasAnyCtor(Primitive cls)
            {
                for (var t = cls; t != null; t = c.ParentView(t))
                    if (c.methodTable.TryGetValue(t.GenericTemplate ?? t, out var entries) && entries.Exists(e => e.SimpleName == "ctor"))
                        return true;
                return false;
            }

            CsError Err(int line, string msg) => new CsError(c.file, line, msg);

            // break/continue hedefleri: en yakin cevreleyen dongu/switch (C# baglama kurali)
            readonly List<Label> breakTargets = new List<Label>();
            readonly List<Label> continueTargets = new List<Label>();
            readonly List<int> breakDepths = new List<int>();    // hedef kayit anindaki tryRegions derinligi
            readonly List<int> continueDepths = new List<int>();
            int catchDepth; // `throw;` yalniz catch govdesinde gecerli

            // acik try bolgeleri (finally'siz catch bolgeleri dahil): erken cikislar bunlari gecerken
            // TryUnwind + finally govdesi kosar. C# leave semantigi.
            class TryRegion { public List<Stmt> Finally; }
            readonly List<TryRegion> tryRegions = new List<TryRegion>();
            readonly List<int> finallyFloors = new List<int>(); // finally govdesi icindeyiz: kontrol cikisi yasak (CS0157)

            // finally govdesi yeniden emit edilebilir (normal + istisna + her erken cikis yolu - Roslyn da coklar).
            // Local snapshot: her emisyon kendi slotlarini alir (ayni ad yeniden bildirilebilir).
            void EmitFinallyBody(List<Stmt> fin)
            {
                var savedLocals = new Dictionary<string, int>(localSlot);
                finallyFloors.Add(breakTargets.Count);
                foreach (var st in fin) EmitStmt(st);
                finallyFloors.RemoveAt(finallyFloors.Count - 1);
                localSlot.Clear();
                foreach (var kv in savedLocals) localSlot[kv.Key] = kv.Value;
            }

            void EmitScoped(List<Stmt> body)
            {
                var savedLocals = new Dictionary<string, int>(localSlot);
                foreach (var statement in body) EmitStmt(statement);
                localSlot.Clear();
                foreach (var binding in savedLocals) localSlot[binding.Key] = binding.Value;
            }

            // hedef derinlige KADAR bolgeleri terk et: her seviye icin runtime zinciri sar + finally kos
            void EmitExitThrough(int targetDepth)
            {
                for (int i = tryRegions.Count - 1; i >= targetDepth; i--)
                {
                    ops.Add(new Op { Type = OpType.TryUnwind, Slot = tryRegions.Count - i }); // 1 = en icteki
                    if (tryRegions[i].Finally != null) EmitFinallyBody(tryRegions[i].Finally);
                }
            }

            // try/catch lowering: TryBegin/TryEnd + handler'da ExIs dispatch zinciri + ExBind.
            // Uymayan tip Rethrow ile dis try'a devreder (trace korunur).
            // finally: catch varsa C# semantigiyle DESUGAR (finally, try/catch'i sarar); salt finally
            // catch-all handler olarak lower edilir (normal + istisna yolunda govde ikilemesi).
            void EmitTry(STry tr)
            {
                if (tr.Finally != null && tr.Catches.Count > 0)
                {
                    var inner = new STry { Line = tr.Line, Body = tr.Body, Catches = tr.Catches };
                    EmitTry(new STry { Line = tr.Line, Body = new List<Stmt> { inner }, Finally = tr.Finally });
                    return;
                }
                if (tr.Finally != null)
                {
                    var fh = new Label();
                    var fend = new Label();
                    ops.Add(new Op { Type = OpType.TryBegin, PrimitiveRef = c.ResolveType(WellKnown.Exception, ns, tr.Line), Label = fh });
                    tryRegions.Add(new TryRegion { Finally = tr.Finally });
                    foreach (var st in tr.Body) EmitStmt(st);
                    tryRegions.RemoveAt(tryRegions.Count - 1);
                    ops.Add(new Op { Type = OpType.TryEnd });
                    EmitFinallyBody(tr.Finally); // normal yol
                    ops.Add(new Op { Type = OpType.Br, Label = fend });
                    ops.Add(new Op { Type = OpType.Label, Label = fh });
                    EmitFinallyBody(tr.Finally); // istisna yolu (bekleyen exception korunur; finally throw ederse onu degistirir - C#)
                    ops.Add(new Op { Type = OpType.Rethrow });
                    ops.Add(new Op { Type = OpType.Label, Label = fend });
                    return;
                }
                var handler = new Label();
                var end = new Label();
                var firstType = tr.Catches[0].TypeName != null ? c.ResolveType(tr.Catches[0].TypeName, ns, tr.Line) : c.ResolveType(WellKnown.Exception, ns, tr.Line);
                ops.Add(new Op { Type = OpType.TryBegin, PrimitiveRef = firstType, Label = handler });
                tryRegions.Add(new TryRegion()); // catch-only: break/return gecisi de zinciri sarmali
                foreach (var st in tr.Body) EmitStmt(st);
                tryRegions.RemoveAt(tryRegions.Count - 1);
                ops.Add(new Op { Type = OpType.TryEnd });
                ops.Add(new Op { Type = OpType.Br, Label = end });
                ops.Add(new Op { Type = OpType.Label, Label = handler });

                var excType = c.ResolveType(WellKnown.Exception, ns, tr.Line);
                var secLabels = new List<Label>();
                bool catchAll = false;
                foreach (var cc in tr.Catches)
                {
                    var lSec = new Label();
                    secLabels.Add(lSec);
                    if (cc.TypeName == null)
                    {
                        ops.Add(new Op { Type = OpType.Br, Label = lSec });
                        catchAll = true;
                        break; // parser zaten sonlandiriyor
                    }
                    var ct = c.ResolveType(cc.TypeName, ns, cc.Line);
                    if (!IsAssignable(ct, excType)) throw Err(cc.Line, $"catch tipi Exception turevi olmali: {ct.Name}");
                    ops.Add(new Op { Type = OpType.ExIs, PrimitiveRef = ct });
                    ops.Add(new Op { Type = OpType.Brtrue, Label = lSec });
                }
                if (!catchAll)
                    ops.Add(new Op { Type = OpType.Rethrow }); // hicbir catch uymadi

                for (int i = 0; i < tr.Catches.Count; i++)
                {
                    var cc = tr.Catches[i];
                    var ct = cc.TypeName != null ? c.ResolveType(cc.TypeName, ns, cc.Line) : excType;
                    ops.Add(new Op { Type = OpType.Label, Label = secLabels[i] });
                    ops.Add(new Op { Type = OpType.ExBind, PrimitiveRef = ct });
                    if (cc.VarName != null)
                    {
                        int slot = DeclareLocal(cc.VarName, ct, cc.Line);
                        ops.Add(new Op { Type = OpType.SetLocal, Slot = slot });
                    }
                    else ops.Add(new Op { Type = OpType.Pop });
                    catchDepth++;
                    foreach (var st in cc.Body) EmitStmt(st);
                    catchDepth--;
                    if (cc.VarName != null) localSlot.Remove(cc.VarName);
                    ops.Add(new Op { Type = OpType.Br, Label = end });
                }
                ops.Add(new Op { Type = OpType.Label, Label = end });
            }

            public void Emit(List<Stmt> body)
            {
                foreach (var s in body) EmitStmt(s);
                var last = ops.Count > 0 ? ops[ops.Count - 1].Type : (OpType)(-1);
                if (ops.Count == 0 || (last != OpType.Return && last != OpType.Throw && last != OpType.Rethrow))
                    ops.Add(new Op { Type = OpType.Return }); // void govde sonu (throw ile biten govdeye olu Return ekleme)
            }

            int DeclareLocal(string name, Primitive type, int line)
            {
                if (localSlot.ContainsKey(name)) throw Err(line, $"local zaten tanimli: {name}");
                int slot = code.Locals.Count;
                code.Locals.Add(type);
                code.LocalNames.Add(name);
                localSlot[name] = slot;
                return slot;
            }

            int DeclareTemp(Primitive type)
            {
                int slot = code.Locals.Count;
                code.Locals.Add(type);
                code.LocalNames.Add(null);
                return slot;
            }

            int DeclareChainTemp(Primitive type)
            {
                int slot = DeclareTemp(type);
                localSlot["\u0001chain" + slot] = slot; // yalniz sentezlenen EName gorur; kullanici adiyle cakisma yok
                return slot;
            }

            void EmitStmt(Stmt s)
            {
                int start = ops.Count;
                EmitStmtCore(s);
                for (int i = start; i < ops.Count; i++) // ic stmt'ler kendi satirini yazdi (Line!=0), disi atla
                    if (ops[i].Line == 0) ops[i].Line = s.Line;
            }

            void EmitStmtCore(Stmt s)
            {
                switch (s)
                {
                    case SVar v:
                        {
                            if (v.TypeName == "var") // tip baslaticidan cikar (C# var)
                            {
                                if (v.Init == null) throw Err(v.Line, "var bildirimine baslatici gerekli");
                                var it = EmitExpr(v.Init);
                                if (it.Type == PrimitiveType.Pointer && IsStructVal(it.ElementType)) { ops.Add(new Op { Type = OpType.LoadInd }); it = it.ElementType; } // struct lvalue kopyasi
                                if (it == Primitive.Void) throw Err(v.Line, "var'a null/void atanamaz (C#)");
                                int vslot = DeclareLocal(v.Name, it, v.Line);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = vslot });
                                break;
                            }
                            var t = c.ResolveType(v.TypeName, ns, v.Line);
                            int slot = DeclareLocal(v.Name, t, v.Line);
                            if (v.Init != null)
                            {
                                if (!(t.IsDelegate && TryEmitDelegateNew(v.Init, t)))
                                    EmitExpr(v.Init);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = slot });
                            }
                            break;
                        }
                    case SVarGroup group:
                        foreach (var variable in group.Variables) EmitStmtCore(variable);
                        break;
                    case SBlock block:
                        EmitScoped(block.Body);
                        break;
                    case SUnchecked uncheckedBlock:
                        // DIGITOYENGINE arithmetic checked overflow uretmez; normal emisyon unchecked C# baglamiyla aynidir.
                        foreach (var statement in uncheckedBlock.Body) EmitStmt(statement);
                        break;
                    case SFixed fixedStatement:
                        EmitFixed(fixedStatement);
                        break;
                    case SLock locked:
                        EmitLock(locked);
                        break;
                    case SAssign a: EmitAssign(a); break;
                    case SExpr e:
                        {
                            var t = e.E is ENullConditionalCall nullCall
                                ? EmitNullConditionalCall(nullCall, discardResult: true)
                                : EmitExpr(e.E);
                            if (t != Primitive.Void)
                                ops.Add(new Op { Type = OpType.Pop }); // kullanilmayan deger
                            break;
                        }
                    case SReturn r:
                        {
                            if (isIterator && r.E != null) throw Err(r.Line, "iterator icinde degerli return kullanilamaz (yield return / yield break)");
                            if (finallyFloors.Count > 0) throw Err(r.Line, "finally govdesinden return yasak (C# CS0157)");
                            if (tryRegions.Count > 0) // once deger (C# sirasi), sonra finally'ler, sonra return
                            {
                                int tmp = -1;
                                if (r.E != null)
                                {
                                    if (!(code.ReturnType != null && code.ReturnType.IsDelegate && TryEmitDelegateNew(r.E, code.ReturnType)))
                                        EmitExpr(r.E);
                                    tmp = DeclareTemp(code.ReturnType);
                                    ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                                }
                                EmitExitThrough(0);
                                if (tmp >= 0) ops.Add(new Op { Type = OpType.GetLocal, Slot = tmp });
                                ops.Add(new Op { Type = OpType.Return });
                                break;
                            }
                            if (r.E != null && !(code.ReturnType != null && code.ReturnType.IsDelegate && TryEmitDelegateNew(r.E, code.ReturnType)))
                                EmitExpr(r.E);
                            ops.Add(new Op { Type = OpType.Return });
                            break;
                        }
                    case SYield y:
                        {
                            if (!isIterator) throw Err(y.Line, "yield yalniz iterator method icinde");
                            EmitExpr(y.E);
                            ops.Add(new Op { Type = OpType.Yield });
                            break;
                        }
                    case SYieldBreak yb:
                        {
                            if (!isIterator) throw Err(yb.Line, "yield break yalniz iterator method icinde");
                            ops.Add(new Op { Type = OpType.Return }); // IteratorLowering: iterator'da Return = yield break
                            break;
                        }
                    case SForeach fe: EmitForeach(fe); break;
                    case SBreak _:
                        if (breakTargets.Count == 0) throw Err(s.Line, "break yalniz dongu ya da switch icinde");
                        if (finallyFloors.Count > 0 && breakTargets.Count <= finallyFloors[finallyFloors.Count - 1])
                            throw Err(s.Line, "finally govdesinden kontrol cikisi yasak (C# CS0157)");
                        EmitExitThrough(breakDepths[breakDepths.Count - 1]);
                        ops.Add(new Op { Type = OpType.Br, Label = breakTargets[breakTargets.Count - 1] });
                        break;
                    case SContinue _:
                        if (continueTargets.Count == 0) throw Err(s.Line, "continue yalniz dongu icinde");
                        if (finallyFloors.Count > 0 && breakTargets.Count <= finallyFloors[finallyFloors.Count - 1])
                            throw Err(s.Line, "finally govdesinden kontrol cikisi yasak (C# CS0157)");
                        EmitExitThrough(continueDepths[continueDepths.Count - 1]);
                        ops.Add(new Op { Type = OpType.Br, Label = continueTargets[continueTargets.Count - 1] });
                        break;
                    case SSwitch sw: EmitSwitch(sw); break;
                    case STry tr: EmitTry(tr); break;
                    case SThrow th:
                        {
                            if (th.E == null) // `throw;` = rethrow (trace korunur)
                            {
                                if (catchDepth == 0) throw Err(th.Line, "throw; yalniz catch icinde");
                                ops.Add(new Op { Type = OpType.Rethrow });
                                break;
                            }
                            var et = EmitExpr(th.E);
                            var excType = c.ResolveType(WellKnown.Exception, ns, th.Line);
                            if (!IsAssignable(et, excType))
                                throw Err(th.Line, $"yalniz Exception turevleri firlatilabilir: {et.Name}");
                            ops.Add(new Op { Type = OpType.Throw });
                            break;
                        }
                    case SFor f:
                        {
                            if (f.Init != null) EmitStmt(f.Init);
                            var lStart = new Label();
                            var lIncr = new Label(); // continue hedefi: kosula degil INCREMENT'e atlar (C# semantigi)
                            var lEnd = new Label();
                            ops.Add(new Op { Type = OpType.Label, Label = lStart });
                            if (f.Cond != null)
                            {
                                EmitExpr(f.Cond);
                                ops.Add(new Op { Type = OpType.Brfalse, Label = lEnd });
                            }
                            breakTargets.Add(lEnd); continueTargets.Add(lIncr); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count);
                            EmitScoped(f.Body);
                            breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                            ops.Add(new Op { Type = OpType.Label, Label = lIncr });
                            if (f.Incr != null) EmitStmt(f.Incr);
                            ops.Add(new Op { Type = OpType.Br, Label = lStart });
                            ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                            if (f.Init is SVar fv) localSlot.Remove(fv.Name); // dongu degiskeni scope'u
                            else if (f.Init is SVarGroup fg)
                                foreach (var variable in fg.Variables) localSlot.Remove(variable.Name);
                            break;
                        }
                    case SIf f:
                        {
                            var lElse = new Label();
                            var lEnd = new Label();
                            EmitExpr(f.Cond);
                            ops.Add(new Op { Type = OpType.Brfalse, Label = lElse });
                            EmitScoped(f.Then);
                            ops.Add(new Op { Type = OpType.Br, Label = lEnd });
                            ops.Add(new Op { Type = OpType.Label, Label = lElse });
                            EmitScoped(f.Else);
                            ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                            break;
                        }
                    case SWhile w:
                        {
                            var lStart = new Label();
                            var lEnd = new Label();
                            ops.Add(new Op { Type = OpType.Label, Label = lStart });
                            EmitExpr(w.Cond);
                            ops.Add(new Op { Type = OpType.Brfalse, Label = lEnd });
                            breakTargets.Add(lEnd); continueTargets.Add(lStart); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count);
                            EmitScoped(w.Body);
                            breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                            ops.Add(new Op { Type = OpType.Br, Label = lStart });
                            ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                            break;
                        }
                    case SDoWhile dw:
                        {
                            var lStart = new Label();
                            var lCond = new Label(); // continue kosula atlar (C# semantigi)
                            var lEnd = new Label();
                            ops.Add(new Op { Type = OpType.Label, Label = lStart });
                            breakTargets.Add(lEnd); continueTargets.Add(lCond); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count);
                            EmitScoped(dw.Body);
                            breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                            ops.Add(new Op { Type = OpType.Label, Label = lCond });
                            EmitExpr(dw.Cond);
                            ops.Add(new Op { Type = OpType.Brtrue, Label = lStart });
                            ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                            break;
                        }
                    default: throw Err(s.Line, $"desteklenmeyen statement: {s.GetType().Name}");
                }
            }

            void EmitLock(SLock statement)
            {
                var targetType = EmitExpr(statement.Target);
                if (targetType.Type != PrimitiveType.Array && (targetType.Type != PrimitiveType.Model || targetType.IsStruct))
                    throw Err(statement.Line, $"lock hedefi reference type olmali: {Primitive.CsDisplay(targetType)}");
                int targetSlot = DeclareTemp(targetType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = targetSlot });
                string targetName = "\u0001lock" + targetSlot;
                localSlot[targetName] = targetSlot;
                ECall MonitorCall(string name) => new ECall
                {
                    Line = statement.Line,
                    Target = new EName { Line = statement.Line, Name = "System.Threading.Monitor" },
                    Name = name,
                    Args = new List<Expr> { new EName { Line = statement.Line, Name = targetName } }
                };
                EmitExpr(MonitorCall("Enter"));
                EmitTry(new STry
                {
                    Line = statement.Line,
                    Body = statement.Body,
                    Finally = new List<Stmt> { new SExpr { Line = statement.Line, E = MonitorCall("Exit") } }
                });
                localSlot.Remove(targetName);
            }

            // switch lowering: deger temp'e alinir, etiketler sirayla Ceq+Brtrue dispatch, sonra
            // section govdeleri. C# kurallari: sabit etiket, duplicate yasak, fall-through yasak.
            void EmitSwitch(SSwitch sw)
            {
                var t = EmitExpr(sw.E);
                bool stringSwitch = t == Primitive.String;
                bool ok = stringSwitch || t == Primitive.Bool || (IsScalar(t) && t.Type != PrimitiveType.Float && t.Type != PrimitiveType.Double);
                if (!ok) throw Err(sw.Line, $"switch degeri string/tam sayi/char/bool olmali: {t.Name}");
                int tmp = DeclareTemp(t);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });

                var lEnd = new Label();
                Label lDefault = null;
                var secLabels = new List<Label>();
                var seen = new HashSet<object>();
                foreach (var sec in sw.Sections)
                {
                    var lSec = new Label();
                    secLabels.Add(lSec);
                    if (sec.HasDefault)
                    {
                        if (lDefault != null) throw Err(sw.Line, "birden fazla default");
                        lDefault = lSec;
                    }
                    foreach (var le in sec.Labels)
                    {
                        var v = CaseConst(le);
                        if (!seen.Add(v)) throw Err(le.Line, $"tekrarlanan case etiketi: {v}");
                        ops.Add(new Op { Type = OpType.GetLocal, Slot = tmp });
                        ops.Add(new Op { Type = OpType.Push, Value = v });
                        if (stringSwitch) ApplyBin("==", Primitive.String, Primitive.String, le.Line);
                        else ops.Add(new Op { Type = OpType.Ceq });
                        ops.Add(new Op { Type = OpType.Brtrue, Label = lSec });
                    }
                }
                ops.Add(new Op { Type = OpType.Br, Label = lDefault ?? lEnd });

                for (int i = 0; i < sw.Sections.Count; i++)
                {
                    var sec = sw.Sections[i];
                    if (sec.Body.Count == 0 || !EndsControl(sec.Body[sec.Body.Count - 1]))
                        throw Err(sw.Line, "case sonu break ya da return ister (C# fall-through yasagi)");
                    ops.Add(new Op { Type = OpType.Label, Label = secLabels[i] });
                    breakTargets.Add(lEnd);
                    breakDepths.Add(tryRegions.Count);
                    foreach (var st in sec.Body) EmitStmt(st);
                    breakTargets.RemoveAt(breakTargets.Count - 1);
                    breakDepths.RemoveAt(breakDepths.Count - 1);
                }
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
            }

            object CaseConst(Expr e)
            {
                if (e is ELit lit)
                    switch (lit.Tag)
                    {
                        case "int": return int.Parse(lit.Value);
                        case "chr": return lit.Value[0];
                        case "bool": return lit.Value == "true";
                        case "str": return string.Intern(lit.Value);
                    }
                if (e is EUn u && u.Op == "-" && u.E is ELit il && il.Tag == "int") return -int.Parse(il.Value);
                if (e is EField fe && TryResolveTypeName(fe.Obj, out var et))
                {
                    if (et.IsEnum && et.EnumMembers.TryGetValue(fe.Name, out var ev)) return ev; // case Renk.Kirmizi:
                    var qualified = c.FindConstant(et, fe.Name);
                    if (qualified != null) return qualified.Value.value; // case Locale.ENGLISH:
                }
                if (e is EName cn)
                {
                    var cv = c.FindConstant(currentClass, cn.Name);
                    if (cv != null && cv.Value.value is int civ) return civ; // case SABIT:
                }
                throw Err(e.Line, "case etiketi sabit olmali (string/int/char/bool/enum/const)");
            }

            // section sonu kontrol devri: break/return/yield break, ya da her iki dali biten if/else
            static bool EndsControl(Stmt s) =>
                s is SBreak || s is SReturn || s is SThrow || s is SYieldBreak ||
                (s is SIf f && f.Then.Count > 0 && f.Else.Count > 0 &&
                 EndsControl(f.Then[f.Then.Count - 1]) && EndsControl(f.Else[f.Else.Count - 1])) ||
                (s is SSwitch sw && sw.Sections.Any(section => section.HasDefault) &&
                 sw.Sections.All(section => section.Body.Count > 0 && EndsControl(section.Body[section.Body.Count - 1])));

            void EmitNestedObjectInitializer(Expr target, ENestedObjectInitializer nested)
            {
                var targetType = EmitExpr(target);
                if (targetType.IsStruct)
                    throw Err(nested.Line, "struct nested object initializer desteklenmiyor");
                int temp = DeclareChainTemp(targetType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                var instance = new EName { Line = nested.Line, Name = "\u0001chain" + temp };
                foreach (var init in nested.Initializers)
                {
                    var member = new EField { Line = init.Line, Obj = instance, Name = init.Name };
                    if (init.Value is ENestedObjectInitializer child)
                        EmitNestedObjectInitializer(member, child);
                    else
                        EmitAssign(new SAssign { Line = init.Line, Target = member, Value = init.Value });
                }
            }

            void EmitAssign(SAssign a)
            {
                if (a.Op != null) { EmitCompound(a); return; }
                if (a.ChainTargets != null && a.ChainTargets.Count > 1)
                {
                    var valueType = EmitExpr(a.Value);
                    int temp = DeclareChainTemp(valueType);
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                    var tempValue = new EName { Line = a.Line, Name = "\u0001chain" + temp };
                    for (int i = a.ChainTargets.Count - 1; i >= 0; i--)
                        EmitAssign(new SAssign { Line = a.Line, Target = a.ChainTargets[i], Value = tempValue });
                    return;
                }
                if (a.Target is EName hoistedName && hoistedCells.TryGetValue(hoistedName.Name, out var hoisted))
                {
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = hoisted.Local });
                    EmitExpr(a.Value);
                    ops.Add(new Op { Type = OpType.SetField, Field = hoisted.ValueField });
                    return;
                }
                switch (a.Target)
                {
                    case EName n when localSlot.TryGetValue(n.Name, out var slot):
                        if (!(code.Locals[slot].IsDelegate && TryEmitDelegateNew(a.Value, code.Locals[slot])))
                            EmitExpr(a.Value);
                        ops.Add(new Op { Type = OpType.SetLocal, Slot = slot });
                        return;
                    case EName n when ArgSlot(n.Name) >= 0:
                        {
                            int slot = ArgSlot(n.Name);
                            var arg = code.Arguments[slot];
                            if (arg.IsRef || arg.IsOut) // pointer'in gosterdigine yaz (CIL stind)
                            {
                                ops.Add(new Op { Type = OpType.GetArg, Slot = slot });
                                EmitExpr(a.Value);
                                ops.Add(new Op { Type = OpType.StoreInd });
                            }
                            else
                            {
                                EmitExpr(a.Value);
                                ops.Add(new Op { Type = OpType.SetArg, Slot = slot });
                            }
                            return;
                        }
                    case EName n when captureSources != null && captureSources.TryGetValue(n.Name, out var captured):
                        if (captured.Field == null) EmitCapture(captured);
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        ops.Add(new Op { Type = OpType.GetField, Field = captured.Field });
                        EmitExpr(a.Value);
                        ops.Add(new Op { Type = OpType.SetField, Field = captured.Cell.ValueField });
                        return;
                    case EName n: // bare ad -> this alani, static alan ya da property
                        {
                            var (field, fOwner) = c.FindFieldView(currentClass, n.Name, false);
                            if (field != null)
                            {
                                CheckFieldWrite(field, n.Line);
                                EmitImplicitThis(n.Line);
                                EmitExpr(a.Value);
                                AddFieldOp(OpType.SetField, field, fOwner);
                                return;
                            }
                            var (sfield, sOwner) = c.FindFieldView(currentClass, n.Name, true);
                            if (sfield != null)
                            {
                                CheckFieldWrite(sfield, n.Line);
                                EmitExpr(a.Value);
                                AddFieldOp(OpType.SetStatic, sfield, sOwner);
                                return;
                            }
                            var argOps = new List<Op>();
                            var argInfos = EmitArgsTo(argOps, new List<Expr> { a.Value });
                            var src = ResolveMethod(currentClass, "set_" + n.Name, argInfos); // kendi sinifin property setter'i
                            if (src != null)
                            {
                                if (!src.Entry.Code.IsStatic)
                                {
                                    EmitImplicitThis(n.Line);
                                }
                                ops.AddRange(argOps);
                                EmitInvoke(src);
                                return;
                            }
                            for (var owner = currentClass; owner != null; owner = c.ParentView(owner))
                            {
                                var template = owner.GenericTemplate ?? owner;
                                var property = template.Properties.FirstOrDefault(value => value.Name == n.Name && !value.IsStatic);
                                if (property == null) continue;
                                var targetType = owner.GenericTemplate != null ? c.SubstView(property.Type, c.MapOf(owner)) : property.Type;
                                if (!property.HasSet) throw Err(n.Line, $"property setter yok: {n.Name}");
                                throw Err(n.Line, $"property atama tipi uyusmuyor: {n.Name} ({Primitive.CsDisplay(argInfos[0].Type)} -> {Primitive.CsDisplay(targetType)})");
                            }
                            throw Err(n.Line, $"atanamaz hedef: {n.Name}");
                        }
                    case EField fe:
                        {
                            if (TryResolveTypeName(fe.Obj, out var stType)) // Tip.Ad = deger
                            {
                                var (ssf, ssOwner) = c.FindFieldView(stType, fe.Name, true);
                                if (ssf != null)
                                {
                                    CheckFieldWrite(ssf, fe.Line);
                                    if (!(ssf.Type.IsDelegate && TryEmitDelegateNew(a.Value, ssf.Type)))
                                        EmitExpr(a.Value);
                                    AddFieldOp(OpType.SetStatic, ssf, ssOwner);
                                    return;
                                }
                                var sArgOps = new List<Op>();
                                var sArgInfos = EmitArgsTo(sArgOps, new List<Expr> { a.Value });
                                var sset = ResolveMethod(stType, "set_" + fe.Name, sArgInfos);
                                if (sset != null && sset.Entry.Code.IsStatic)
                                {
                                    ops.AddRange(sArgOps);
                                    EmitInvoke(sset);
                                    return;
                                }
                                throw Err(fe.Line, $"bilinmeyen static alan/property: {stType.Name}.{fe.Name}");
                            }
                            var objType = EmitMemberObj(fe.Obj, true, fe.Line);
                            var (field, fOwner) = c.FindFieldView(objType, fe.Name, false);
                            if (field != null)
                            {
                                CheckFieldWrite(field, fe.Line);
                                if (!(field.Type.IsDelegate && TryEmitDelegateNew(a.Value, field.Type)))
                                    EmitExpr(a.Value);
                                AddFieldOp(OpType.SetField, field, fOwner);
                                return;
                            }
                            var argOps2 = new List<Op>();
                            var argInfos2 = EmitArgsTo(argOps2, new List<Expr> { a.Value });
                            var pset = ResolveMethod(objType, "set_" + fe.Name, argInfos2); // property setter (alici stack'te)
                            if (pset != null && !pset.Entry.Code.IsStatic)
                            {
                                ops.AddRange(argOps2);
                                EmitInvoke(pset);
                                return;
                            }
                            throw Err(fe.Line, $"bilinmeyen alan/property: {objType.Name}.{fe.Name}");
                        }
                    case EIndex ix:
                        {
                            bool isBaseIndexer = ix.Obj is EName { Name: "base", IsEscaped: false };
                            var arrType = isBaseIndexer ? EmitMemberObj(ix.Obj, true, ix.Line) : EmitExpr(ix.Obj);
                            if (arrType.Type == PrimitiveType.Model) // kullanici indexer'i: set_Item (C# lowering)
                            {
                                var sixOps = new List<Op>();
                                var sixInfos = EmitArgsTo(sixOps, ix.Indices.Concat(new[] { a.Value }).ToList());
                                var sixc = ResolveMethod(arrType, "set_Item", sixInfos) ?? throw Err(ix.Line, $"atanabilir indexer yok: {arrType.Name}");
                                ops.AddRange(sixOps);
                                if (isBaseIndexer) EmitBaseInvoke(sixc); else EmitInvoke(sixc);
                                return;
                            }
                            if (arrType.Type != PrimitiveType.Array && arrType.Type != PrimitiveType.FixedArray && arrType.Type != PrimitiveType.Pointer)
                                throw Err(ix.Line, $"indekslenebilir tip degil: {arrType.Name}");
                            EmitArrayIndices(ix.Indices, arrType, ix.Line);
                            var elemT = arrType.ElementType;
                            if (!(elemT != null && elemT.IsDelegate && TryEmitDelegateNew(a.Value, elemT)))
                                EmitExpr(a.Value);
                            ops.Add(new Op { Type = OpType.SetIndex, Slot = ix.Indices.Count });
                            return;
                        }
                    default: throw Err(a.Line, $"atanamaz hedef: {a.Target.GetType().Name}");
                }
            }

            int ArgSlot(string name)
            {
                for (int i = 0; i < code.Arguments.Count; i++)
                    if (code.Arguments[i].Name == name) return i;
                return -1;
            }

            // bilesik atama (x op= v): hedef TEK KEZ degerlendirilir (C# kurali; alici Dup/temp ile tutulur),
            // operator cozumu ApplyBin'den (op_xxx overload'lari ve string + dahil calisir)
            void EmitCompound(SAssign a)
            {
                if (a.Target is EName hoistedName && hoistedCells.TryGetValue(hoistedName.Name, out var hoisted))
                {
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = hoisted.Local });
                    ops.Add(new Op { Type = OpType.Dup });
                    ops.Add(new Op { Type = OpType.GetField, Field = hoisted.ValueField });
                    var right = EmitCompoundValue(a.Value, hoisted.Type, a.Op);
                    var result = ApplyBin(a.Op, hoisted.Type, right, a.Line);
                    RequireCompoundAssignable(result, hoisted.Type, a.Line);
                    ops.Add(new Op { Type = OpType.SetField, Field = hoisted.ValueField });
                    return;
                }
                if (a.Target is EName capturedName && captureSources != null && captureSources.TryGetValue(capturedName.Name, out var captured))
                {
                    if (captured.Field == null) EmitCapture(captured);
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    ops.Add(new Op { Type = OpType.GetField, Field = captured.Field });
                    ops.Add(new Op { Type = OpType.Dup });
                    ops.Add(new Op { Type = OpType.GetField, Field = captured.Cell.ValueField });
                    var right = EmitCompoundValue(a.Value, captured.Cell.Type, a.Op);
                    var result = ApplyBin(a.Op, captured.Cell.Type, right, a.Line);
                    RequireCompoundAssignable(result, captured.Cell.Type, a.Line);
                    ops.Add(new Op { Type = OpType.SetField, Field = captured.Cell.ValueField });
                    return;
                }
                switch (a.Target)
                {
                    case EName n when localSlot.TryGetValue(n.Name, out var slot):
                        {
                            var lt = code.Locals[slot];
                            ops.Add(new Op { Type = OpType.GetLocal, Slot = slot });
                            var rt = EmitCompoundValue(a.Value, lt, a.Op);
                            var res = ApplyBin(a.Op, lt, rt, a.Line);
                            RequireCompoundAssignable(res, lt, a.Line);
                            ops.Add(new Op { Type = OpType.SetLocal, Slot = slot });
                            return;
                        }
                    case EName n when ArgSlot(n.Name) >= 0:
                        {
                            int slot = ArgSlot(n.Name);
                            var arg = code.Arguments[slot];
                            if (arg.IsRef || arg.IsOut) // ptr; Dup ile adres bir kez
                            {
                                ops.Add(new Op { Type = OpType.GetArg, Slot = slot });
                                ops.Add(new Op { Type = OpType.Dup });
                                ops.Add(new Op { Type = OpType.LoadInd });
                                var rt2 = EmitCompoundValue(a.Value, arg.Type, a.Op);
                                var res2 = ApplyBin(a.Op, arg.Type, rt2, a.Line);
                                RequireCompoundAssignable(res2, arg.Type, a.Line);
                                ops.Add(new Op { Type = OpType.StoreInd });
                                return;
                            }
                            ops.Add(new Op { Type = OpType.GetArg, Slot = slot });
                            var rt3 = EmitCompoundValue(a.Value, arg.Type, a.Op);
                            var res3 = ApplyBin(a.Op, arg.Type, rt3, a.Line);
                            RequireCompoundAssignable(res3, arg.Type, a.Line);
                            ops.Add(new Op { Type = OpType.SetArg, Slot = slot });
                            return;
                        }
                    case EName n:
                        {
                            var (field, fOwner) = c.FindFieldView(currentClass, n.Name, false);
                            if (field != null)
                            {
                                var ft = c.FieldTypeFor(field, fOwner);
                                EmitImplicitThis(n.Line);
                                ops.Add(new Op { Type = OpType.Dup });
                                AddFieldOp(OpType.GetField, field, fOwner);
                                var rt = EmitCompoundValue(a.Value, ft, a.Op);
                                var res = ApplyBin(a.Op, ft, rt, a.Line);
                                RequireCompoundAssignable(res, ft, a.Line);
                                AddFieldOp(OpType.SetField, field, fOwner);
                                return;
                            }
                            var (sfield, sOwner) = c.FindFieldView(currentClass, n.Name, true);
                            if (sfield != null)
                            {
                                var ft = c.FieldTypeFor(sfield, sOwner);
                                AddFieldOp(OpType.GetStatic, sfield, sOwner);
                                var rt = EmitCompoundValue(a.Value, ft, a.Op);
                                var res = ApplyBin(a.Op, ft, rt, a.Line);
                                RequireCompoundAssignable(res, ft, a.Line);
                                AddFieldOp(OpType.SetStatic, sfield, sOwner);
                                return;
                            }
                            EmitCompoundProperty(null, currentClass, n.Name, a); // kendi sinifin property'si
                            return;
                        }
                    case EField fe:
                        {
                            if (TryResolveTypeName(fe.Obj, out var stType)) // Tip.Ad op= v
                            {
                                var (ssf, ssOwner) = c.FindFieldView(stType, fe.Name, true);
                                if (ssf != null)
                                {
                                    var ft = c.FieldTypeFor(ssf, ssOwner);
                                    AddFieldOp(OpType.GetStatic, ssf, ssOwner);
                                    var rt = EmitCompoundValue(a.Value, ft, a.Op);
                                    var res = ApplyBin(a.Op, ft, rt, a.Line);
                                    RequireCompoundAssignable(res, ft, a.Line);
                                    AddFieldOp(OpType.SetStatic, ssf, ssOwner);
                                    return;
                                }
                                EmitCompoundProperty(stType, stType, fe.Name, a);
                                return;
                            }
                            var objType = EmitMemberObj(fe.Obj, true, fe.Line);
                            ops.Add(new Op { Type = OpType.Dup }); // alici TEK degerlendirme
                            var (field2, fOwner2) = c.FindFieldView(objType, fe.Name, false);
                            if (field2 != null)
                            {
                                var ft = c.FieldTypeFor(field2, fOwner2);
                                AddFieldOp(OpType.GetField, field2, fOwner2);
                                var rt = EmitCompoundValue(a.Value, ft, a.Op);
                                var res = ApplyBin(a.Op, ft, rt, a.Line);
                                RequireCompoundAssignable(res, ft, a.Line);
                                AddFieldOp(OpType.SetField, field2, fOwner2);
                                return;
                            }
                            // instance property: alici dup'lu -> get, op, set
                            var g = ResolveMethod(objType, "get_" + fe.Name, new List<ArgInfo>());
                            if (g == null || g.Entry.Code.IsStatic) throw Err(fe.Line, $"bilinmeyen alan/property: {objType.Name}.{fe.Name}");
                            EmitInvoke(g);
                            var rtp = EmitCompoundValue(a.Value, g.ReturnType, a.Op);
                            var resp = ApplyBin(a.Op, g.ReturnType, rtp, a.Line);
                            var st2 = ResolveMethod(objType, "set_" + fe.Name, new List<ArgInfo> { new ArgInfo { Type = resp } });
                            if (st2 == null || st2.Entry.Code.IsStatic) throw Err(fe.Line, $"property setter yok: {objType.Name}.{fe.Name}");
                            EmitInvoke(st2);
                            return;
                        }
                    case EIndex ix:
                        {
                            var arrType = EmitExpr(ix.Obj);
                            if (arrType.Type != PrimitiveType.Array && arrType.Type != PrimitiveType.FixedArray)
                                throw Err(ix.Line, $"indekslenebilir tip degil: {arrType.Name}");
                            var indexTypes = EmitArrayIndices(ix.Indices, arrType, ix.Line);
                            var indexTemps = new int[indexTypes.Count];
                            for (int i = indexTypes.Count - 1; i >= 0; i--) { indexTemps[i] = DeclareTemp(indexTypes[i]); ops.Add(new Op { Type = OpType.SetLocal, Slot = indexTemps[i] }); }
                            int tObj = DeclareTemp(arrType);
                            ops.Add(new Op { Type = OpType.SetLocal, Slot = tObj });
                            ops.Add(new Op { Type = OpType.GetLocal, Slot = tObj });
                            foreach (var temp in indexTemps) ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                            ops.Add(new Op { Type = OpType.GetIndex, Slot = indexTemps.Length });
                            var rt = EmitCompoundValue(a.Value, arrType.ElementType, a.Op);
                            var res = ApplyBin(a.Op, arrType.ElementType, rt, a.Line);
                            RequireCompoundAssignable(res, arrType.ElementType, a.Line);
                            int tVal = DeclareTemp(res);
                            ops.Add(new Op { Type = OpType.SetLocal, Slot = tVal });
                            ops.Add(new Op { Type = OpType.GetLocal, Slot = tObj });
                            foreach (var temp in indexTemps) ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                            ops.Add(new Op { Type = OpType.GetLocal, Slot = tVal });
                            ops.Add(new Op { Type = OpType.SetIndex, Slot = indexTemps.Length });
                            return;
                        }
                    default: throw Err(a.Line, "atanamaz hedef");
                }
            }

            // property op= (staticType null = this alicili): alici stack'te hazirlanir, get -> op -> set
            void EmitCompoundProperty(Primitive staticType, Primitive owner, string name, SAssign a)
            {
                var g = ResolveMethod(owner, "get_" + name, new List<ArgInfo>());
                if (g == null) throw Err(a.Line, $"atanamaz hedef: {name}");
                bool inst = !g.Entry.Code.IsStatic;
                if (staticType != null && inst) throw Err(a.Line, $"instance property tip adi uzerinden kullanilamaz: {name}");
                if (inst)
                {
                    EmitImplicitThis(a.Line);
                    ops.Add(new Op { Type = OpType.Dup });
                }
                EmitInvoke(g);
                var rt = EmitCompoundValue(a.Value, g.ReturnType, a.Op);
                var res = ApplyBin(a.Op, g.ReturnType, rt, a.Line);
                var s = ResolveMethod(owner, "set_" + name, new List<ArgInfo> { new ArgInfo { Type = res } });
                if (s == null || s.Entry.Code.IsStatic != !inst) throw Err(a.Line, $"property setter yok: {name}");
                EmitInvoke(s);
            }

            Primitive EmitCompoundValue(Expr value, Primitive targetType, string op)
            {
                if ((op == "+" || op == "-") && targetType.IsDelegate && TryEmitDelegateNew(value, targetType))
                    return targetType;
                return EmitExpr(value);
            }

            void RequireCompoundAssignable(Primitive result, Primitive target, int line)
            {
                if (result == target || IsAssignable(result, target)) return;
                bool resInt = IsScalar(result) && result.Type != PrimitiveType.Float && result.Type != PrimitiveType.Double;
                bool tgtFlt = target.Type == PrimitiveType.Float || target.Type == PrimitiveType.Double;
                if (IsScalar(result) && IsScalar(target) && (resInt || tgtFlt)) return; // C#: sonuc hedefe ortuk geri-cast (byte+=1); float->int YASAK
                throw Err(line, $"bilesik atama sonucu hedefe uymuyor: {result.Name} -> {target.Name}");
            }

            List<Primitive> EmitArrayIndices(List<Expr> indices, Primitive arrayType, int line)
            {
                int rank = arrayType.Type == PrimitiveType.Array ? arrayType.ArrayRank : 1;
                if (indices.Count != rank)
                    throw Err(line, $"dizi rank'i {rank}, {indices.Count} indeks verildi: {arrayType.Name}");
                var types = new List<Primitive>();
                foreach (var index in indices)
                {
                    var type = EmitExpr(index);
                    RequireIntLike(type, index.Line);
                    types.Add(type);
                }
                return types;
            }

            // alan op'u: sahibi Apply gorunumundeyse (Box<int> zinciri) TypeArguments eklenir (Resolver remap eder)
            void AddFieldOp(OpType t, PrimitiveField f, Primitive ownerView)
            {
                var op = new Op { Type = t, Field = f };
                if (ownerView != null && ownerView.GenericTemplate != null)
                    op.TypeArguments.AddRange(ownerView.TypeArguments);
                ops.Add(op);
            }

            Primitive PushConst((Primitive type, object value) cv)
            {
                ops.Add(new Op { Type = OpType.Push, Value = cv.value });
                return cv.type;
            }

            // readonly: yalniz kendi sinifinin ctor'unda (static ise cctor'unda) yazilabilir
            void CheckFieldWrite(PrimitiveField f, int line)
            {
                if (f == null || !f.IsReadonly) return;
                bool inCtor = f.IsStatic ? code.Name == "cctor" : (code.Name == "ctor" || code.Name.StartsWith("ctor_"));
                if (!inCtor || f.Owner != (currentClass.GenericTemplate ?? currentClass))
                    throw Err(line, $"readonly alana yalniz kendi ctor'unda yazilabilir: {f.Name}");
            }

            static PrimitiveField FindField(Primitive cls, string name)
            {
                for (var t = cls; t != null; t = t.Parent)
                    foreach (var f in t.Fields)
                        if (f.Name == name) return f;
                return null;
            }

            static PrimitiveField FindStaticField(Primitive cls, string name)
            {
                for (var t = cls; t != null; t = t.Parent)
                    foreach (var f in t.StaticFields)
                        if (f.Name == name) return f;
                return null;
            }

            void RequireThis(int line)
            {
                if (code.IsStatic) throw Err(line, "static method icinde instance uyesi kullanilamaz");
            }

            Primitive EmitImplicitThis(int line)
            {
                if (captureSources != null && captureSources.TryGetValue("this", out var capturedThis))
                    return EmitCapture(capturedThis);
                RequireThis(line);
                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                return currentClass;
            }

            static bool IsStructVal(Primitive t) => t.Type == PrimitiveType.Model && t.IsStruct;

            // uye erisimi alicisi: class -> ptr (deger emisyonu), struct lvalue -> ADRES (mutasyon dogrudan),
            // struct rvalue -> deger (okuma serbest; forWrite ise C# kurali geregi hata)
            Primitive EmitMemberObj(Expr obj, bool forWrite, int line)
            {
                if (obj is EName { Name: "base", IsEscaped: false })
                {
                    RequireThis(line);
                    if (currentClass.Parent == null) throw Err(line, "base class yok");
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    return currentClass.Parent;
                }
                var probeOps = new List<Op>();
                var saved = cur; cur = probeOps;
                var t = EmitExpr(obj);
                cur = saved;
                if (t.Type == PrimitiveType.Pointer) { ops.AddRange(probeOps); return t.ElementType; } // this vb. zaten adres
                if (IsStructVal(t))
                {
                    if (TryEmitStructAddr(obj, out var elem)) return elem;
                    if (forWrite) throw Err(line, "gecici struct degerine yazilamaz (C#: not a variable)");
                    ops.AddRange(probeOps);
                    return t; // rvalue okuma: deger uzerinden (kopya)
                }
                ops.AddRange(probeOps);
                return t;
            }

            // struct lvalue zincirinin ADRESINI emit eder (basarisizsa HIC emisyon yapmaz)
            bool TryEmitStructAddr(Expr e, out Primitive elem)
            {
                elem = null;
                switch (e)
                {
                    case EName n when !n.IsEscaped && n.Name == "this" && currentClass.IsStruct && !code.IsStatic:
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        elem = currentClass;
                        return true;
                    case EName n when localSlot.TryGetValue(n.Name, out var slot):
                        if (!IsStructVal(code.Locals[slot])) return false;
                        ops.Add(new Op { Type = OpType.AddrLocal, Slot = slot });
                        elem = code.Locals[slot];
                        return true;
                    case EName n when ArgSlot(n.Name) >= 0:
                        {
                            int slot = ArgSlot(n.Name);
                            var arg = code.Arguments[slot];
                            if (!IsStructVal(arg.Type)) return false;
                            ops.Add(new Op { Type = (arg.IsRef || arg.IsOut) ? OpType.GetArg : OpType.AddrArg, Slot = slot });
                            elem = arg.Type;
                            return true;
                        }
                    case EName n:
                        {
                            var (field, fOwner) = c.FindFieldView(currentClass, n.Name, false);
                            if (field != null && IsStructVal(c.FieldTypeFor(field, fOwner)) && !code.IsStatic)
                            {
                                ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                                AddFieldOp(OpType.AddrField, field, fOwner);
                                elem = c.FieldTypeFor(field, fOwner);
                                return true;
                            }
                            var (sf, sOwner) = c.FindFieldView(currentClass, n.Name, true);
                            if (sf != null && IsStructVal(c.FieldTypeFor(sf, sOwner)))
                            {
                                AddFieldOp(OpType.AddrStatic, sf, sOwner);
                                elem = c.FieldTypeFor(sf, sOwner);
                                return true;
                            }
                            return false;
                        }
                    case EField fe:
                        {
                            var addressOps = new List<Op>();
                            var outer = cur;
                            cur = addressOps;
                            Primitive addressType = null;
                            try
                            {
                                if (TryResolveTypeName(fe.Obj, out var stType))
                                {
                                    var (ssf, ssOwner) = c.FindFieldView(stType, fe.Name, true);
                                    if (ssf == null || !IsStructVal(c.FieldTypeFor(ssf, ssOwner))) return false;
                                    AddFieldOp(OpType.AddrStatic, ssf, ssOwner);
                                    addressType = c.FieldTypeFor(ssf, ssOwner);
                                }
                                else
                                {
                                    // alici: class-ptr / struct-ptr / struct lvalue zinciri
                                    var probeOps = new List<Op>();
                                    var saved = cur; cur = probeOps;
                                    var ot = EmitExpr(fe.Obj);
                                    cur = saved;
                                    Primitive ownerType;
                                    if (ot.Type == PrimitiveType.Pointer) { ops.AddRange(probeOps); ownerType = ot.ElementType; }
                                    else if (IsStructVal(ot))
                                    {
                                        if (!TryEmitStructAddr(fe.Obj, out ownerType)) return false; // rvalue zincir: adreslenemez
                                    }
                                    else if (ot.Type == PrimitiveType.Model) { ops.AddRange(probeOps); ownerType = ot; }
                                    else return false;
                                    var (fld, fOwn) = c.FindFieldView(ownerType, fe.Name, false);
                                    if (fld == null || !IsStructVal(c.FieldTypeFor(fld, fOwn))) return false;
                                    AddFieldOp(OpType.AddrField, fld, fOwn);
                                    addressType = c.FieldTypeFor(fld, fOwn);
                                }
                            }
                            finally { cur = outer; }
                            ops.AddRange(addressOps);
                            elem = addressType;
                            return true;
                        }
                    case EIndex ix:
                        {
                            var probeOps = new List<Op>();
                            var saved = cur; cur = probeOps;
                            var at = EmitExpr(ix.Obj);
                            cur = saved;
                            if ((at.Type != PrimitiveType.Array && at.Type != PrimitiveType.FixedArray) || !IsStructVal(at.ElementType)) return false;
                            ops.AddRange(probeOps);
                            EmitArrayIndices(ix.Indices, at, ix.Line);
                            ops.Add(new Op { Type = OpType.AddrElement, Slot = ix.Indices.Count });
                            elem = at.ElementType;
                            return true;
                        }
                    default: return false;
                }
            }

            // method cagrisi alicisi: struct lvalue -> adres; struct rvalue -> temp kopya adresi (C# semantigi)
            Primitive EmitReceiver(Expr target)
            {
                if (target is EName { Name: "base", IsEscaped: false })
                {
                    RequireThis(target.Line);
                    if (currentClass.Parent == null) throw Err(target.Line, "base class yok");
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    return currentClass.Parent;
                }
                var probeOps = new List<Op>();
                var saved = cur; cur = probeOps;
                var t = EmitExpr(target);
                cur = saved;
                if (t.Type == PrimitiveType.Pointer) { ops.AddRange(probeOps); return t.ElementType; }
                if (IsStructVal(t))
                {
                    if (TryEmitStructAddr(target, out var elem)) return elem;
                    int tmp = DeclareTemp(t); // rvalue: kopya uzerinde cagri, mutasyon kaybolur (C# ayni)
                    ops.AddRange(probeOps);
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                    ops.Add(new Op { Type = OpType.AddrLocal, Slot = tmp });
                    return t;
                }
                ops.AddRange(probeOps);
                return t;
            }

            Primitive EmitExpr(Expr e)
            {
                switch (e)
                {
                    case ELit lit: return EmitLit(lit);
                    case EInterpolated interpolated: return EmitInterpolated(interpolated);
                    case EInvoke invoke: return EmitDelegateExpressionInvoke(invoke);
                    case EThrow thrown: return EmitThrowExpression(thrown);
                    case EName n: return EmitName(n);
                    case ENullConditionalCall call: return EmitNullConditionalCall(call);
                    case ENullConditionalMember member: return EmitNullConditionalMember(member);
                    case ECoalesce coalesce: return EmitCoalesce(coalesce);
                    case EAssign assign:
                        {
                            if (assign.Op != null)
                            {
                                EmitCompound(new SAssign { Line = assign.Line, Target = assign.Target, Value = assign.Value, Op = assign.Op });
                                return EmitExpr(assign.Target);
                            }
                            var valueType = EmitExpr(assign.Value);
                            int temp = DeclareChainTemp(valueType);
                            ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                            EmitAssign(new SAssign
                            {
                                Line = assign.Line,
                                Target = assign.Target,
                                Value = new EName { Line = assign.Line, Name = "\u0001chain" + temp }
                            });
                            ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                            return valueType;
                        }
                    case EUn u:
                        {
                            var t = EmitExpr(u.E);
                            if (u.Op == "+")
                            {
                                if (!IsScalar(t) || t == Primitive.Bool) throw Err(u.Line, $"unary + sayisal tip ister: {t.Name}");
                                return t;
                            }
                            if (u.Op == "~") // C#: tumleme; x ^ -1 olarak lower edilir (C tam esdegeri)
                            {
                                RequireIntLike(t, u.Line);
                                ops.Add(new Op { Type = OpType.Push, Value = -1 });
                                ops.Add(new Op { Type = OpType.Xor });
                                return t;
                            }
                            ops.Add(new Op { Type = u.Op == "-" ? OpType.Neg : OpType.Not });
                            if (u.Op == "!") return Primitive.Bool;
                            if (u.Op == "~") return t;
                            if (t.Type == PrimitiveType.UInt) return Primitive.Long; // C#: -uint = long
                            if (t.Type == PrimitiveType.ULong) throw Err(u.Line, "ulong negatiflenemez (C#)");
                            return t;
                        }
                    case ETernary tern: return EmitTernary(tern);
                    case EInc inc: return EmitIncDec(inc);
                    case EBin b: return EmitBin(b);
                    case ENew nw:
                        {
                            var t = c.ResolveType(nw.TypeName, ns, nw.Line);
                            if (t == Primitive.String || t == Primitive.Object && nw.Args.Count > 0)
                                throw Err(nw.Line, $"bu tip new ile kurulamaz: {t.Name}"); // C#'ta da parametresiz string ctor'u yok
                            var newOp = new Op { Type = OpType.New, PrimitiveRef = t.GenericTemplate ?? t };
                            if (t.GenericTemplate != null) newOp.TypeArguments.AddRange(t.TypeArguments); // Resolver somutlar
                            ops.Add(newOp);
                            var argOps = new List<Op>();
                            var argInfos = EmitArgsTo(argOps, nw.Args);
                            var rc = ResolveMethod(t, "ctor", argInfos);
                            if (rc == null && nw.Args.Count > 0)
                                throw Err(nw.Line, $"uygun ctor yok: {t.Name}({string.Join(",", argInfos.Select(x => x.Type != null ? x.Type.Name : "out var"))})");
                            if (rc != null)
                            {
                                PatchOutVars(argInfos, rc);
                                PackParams(rc, argOps, argInfos, nw.Line);
                                if (t.IsStruct) // sifirlanmis temp'in ADRESINDE ctor kos, degeri birak
                                {
                                    int tmp = DeclareTemp(t);
                                    ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                                    ops.Add(new Op { Type = OpType.AddrLocal, Slot = tmp });
                                    ops.AddRange(argOps);
                                    EmitDefaults(rc.Entry, argInfos.Count, nw.Line);
                                    EmitInvoke(rc);
                                    ops.Add(new Op { Type = OpType.GetLocal, Slot = tmp });
                                    return t;
                                }
                                // ctor cagrisi: New, Dup, args, Call ctor(void) -> stack'te instance kalir
                                ops.Add(new Op { Type = OpType.Dup });
                                ops.AddRange(argOps);
                                EmitDefaults(rc.Entry, argInfos.Count, nw.Line);
                                EmitInvoke(rc);
                            }
                            if (nw.Initializers.Count > 0 || nw.CollectionInitializers.Count > 0)
                            {
                                int temp = DeclareChainTemp(t);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                                var instance = new EName { Line = nw.Line, Name = "\u0001chain" + temp };
                                foreach (var init in nw.Initializers)
                                {
                                    var target = new EField { Line = init.Line, Obj = instance, Name = init.Name };
                                    if (init.Value is ENestedObjectInitializer nested)
                                        EmitNestedObjectInitializer(target, nested);
                                    else
                                        EmitAssign(new SAssign { Line = init.Line, Target = target, Value = init.Value });
                                }
                                foreach (var init in nw.CollectionInitializers)
                                {
                                    if (init.Index != null)
                                    {
                                        EmitAssign(new SAssign
                                        {
                                            Line = init.Line,
                                            Target = new EIndex { Line = init.Line, Obj = instance, Indices = new List<Expr> { init.Index } },
                                            Value = init.Value
                                        });
                                        continue;
                                    }
                                    if (t.IsStruct) throw Err(init.Line, "struct collection initializer desteklenmiyor");
                                    ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                                    var collectionArgOps = new List<Op>();
                                    var collectionArgs = init.Args ?? new List<Expr> { init.Value };
                                    var collectionArgInfos = EmitArgsTo(collectionArgOps, collectionArgs);
                                    var add = ResolveMethod(t, "Add", collectionArgInfos) ?? throw Err(init.Line, $"collection initializer icin Add bulunamadi: {t.Name}");
                                    if (add.Entry.Code.IsStatic) throw Err(init.Line, $"collection initializer Add instance method olmali: {t.Name}");
                                    PatchOutVars(collectionArgInfos, add);
                                    PatchMethodGroups(collectionArgInfos, add, collectionArgOps);
                                    ops.AddRange(collectionArgOps);
                                    EmitInvoke(add);
                                }
                                ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                            }
                            return t;
                        }
                    case EField fe:
                        {
                            bool resolvedStaticType = TryResolveTypeName(fe.Obj, out var stType);
                            if (!resolvedStaticType && TryResolveTypeName(fe.Obj, out var shadowedType, allowValueShadow: true))
                            {
                                var shadowedGetter = ResolveMethod(shadowedType, "get_" + fe.Name, new List<ArgInfo>());
                                resolvedStaticType = shadowedType.IsEnum && shadowedType.EnumMembers.ContainsKey(fe.Name)
                                    || c.FindConstant(shadowedType, fe.Name) != null
                                    || c.FindFieldView(shadowedType, fe.Name, true).f != null
                                    || shadowedGetter != null && shadowedGetter.Entry.Code.IsStatic;
                                if (resolvedStaticType) stType = shadowedType;
                            }
                            if (resolvedStaticType) // Tip.Ad: enum uyesi / const / static alan / static property
                            {
                                if (stType.IsEnum)
                                {
                                    if (!stType.EnumMembers.TryGetValue(fe.Name, out var ev))
                                        throw Err(fe.Line, $"bilinmeyen enum uyesi: {stType.Name}.{fe.Name}");
                                    ops.Add(new Op { Type = OpType.Push, Value = ev });
                                    ops.Add(new Op { Type = OpType.Conv, PrimitiveRef = stType }); // IR'de enum tipini tasi (boxing dogru descriptor'i secsin)
                                    return stType;
                                }
                                var kc = c.FindConstant(stType, fe.Name);
                                if (kc != null) return PushConst(kc.Value);
                                var (sf, sOwner) = c.FindFieldView(stType, fe.Name, true);
                                if (sf != null) { AddFieldOp(OpType.GetStatic, sf, sOwner); return c.FieldTypeFor(sf, sOwner); }
                                var prc = ResolveMethod(stType, "get_" + fe.Name, new List<ArgInfo>());
                                if (prc != null && prc.Entry.Code.IsStatic) { EmitInvoke(prc); return prc.ReturnType; }
                                throw Err(fe.Line, $"bilinmeyen static alan/property: {stType.Name}.{fe.Name}");
                            }
                            var objType = EmitMemberObj(fe.Obj, false, fe.Line);
                            if (objType.Type == PrimitiveType.Array && fe.Name == "Length")
                            {
                                ops.Add(new Op { Type = OpType.ArrayLength });
                                return Primitive.Int;
                            }
                            var (field, fOwner) = c.FindFieldView(objType, fe.Name, false);
                            if (field != null)
                            {
                                AddFieldOp(OpType.GetField, field, fOwner);
                                return c.FieldTypeFor(field, fOwner);
                            }
                            var rc = ResolveMethod(objType, "get_" + fe.Name, new List<ArgInfo>()); // property getter (alici zaten stack'te)
                            if (rc != null && !rc.Entry.Code.IsStatic) { EmitInvoke(rc); return rc.ReturnType; }
                            throw Err(fe.Line, $"bilinmeyen alan/property: {objType.Name}.{fe.Name}");
                        }
                    case EIndex ix:
                        {
                            bool isBaseIndexer = ix.Obj is EName { Name: "base", IsEscaped: false };
                            var arrType = isBaseIndexer ? EmitMemberObj(ix.Obj, false, ix.Line) : EmitExpr(ix.Obj);
                            if (arrType == Primitive.String) // C# indexer: s[i] -> get_Chars(i) (Roslyn da boyle lower eder)
                            {
                                var argOps = new List<Op>();
                                var argInfos = EmitArgsTo(argOps, ix.Indices);
                                var rc = ResolveMethod(Primitive.String, "get_Chars", argInfos) ?? throw Err(ix.Line, "String.get_Chars corlib'te yok");
                                ops.AddRange(argOps);
                                EmitInvoke(rc);
                                return rc.ReturnType;
                            }
                            if (arrType.Type != PrimitiveType.Array && arrType.Type != PrimitiveType.FixedArray && arrType.Type != PrimitiveType.Pointer)
                            {
                                if (arrType.Type == PrimitiveType.Model) // kullanici indexer'i: get_Item (C# lowering)
                                {
                                    var ixOps = new List<Op>();
                                    var ixInfos = EmitArgsTo(ixOps, ix.Indices);
                                    var ixc = ResolveMethod(arrType, "get_Item", ixInfos) ?? throw Err(ix.Line, $"indekslenebilir tip degil: {arrType.Name}");
                                    ops.AddRange(ixOps);
                                    if (isBaseIndexer) EmitBaseInvoke(ixc); else EmitInvoke(ixc);
                                    return ixc.ReturnType;
                                }
                                throw Err(ix.Line, $"indekslenebilir tip degil: {arrType.Name}");
                            }
                            EmitArrayIndices(ix.Indices, arrType, ix.Line);
                            ops.Add(new Op { Type = OpType.GetIndex, Slot = ix.Indices.Count });
                            return arrType.ElementType;
                        }
                    case ENewArray na:
                        {
                            var elem = c.ResolveType(na.ElemTypeName, ns, na.Line);
                            if (elem.Type == PrimitiveType.Model && elem.IsStruct)
                                foreach (var f in Hierarchy.AllFields(elem))
                                    if ((f.Type.Type == PrimitiveType.Model && !f.Type.IsStruct) || f.Type.Type == PrimitiveType.Array)
                                        throw Err(na.Line, $"GC-referansli alan iceren struct dizisi desteklenmiyor (GC dizi icini taramaz - bilincli sinir): {elem.Name}.{f.Name}");
                            if (na.Rows != null) // new T[,] { { e1, e2 }, ... }
                            {
                                int columns = na.Rows.Count > 0 ? na.Rows[0].Count : 0;
                                foreach (var row in na.Rows)
                                    if (row.Count != columns) throw Err(na.Line, "rectangular dizi baslaticisindaki tum satirlar ayni uzunlukta olmali");
                                ops.Add(new Op { Type = OpType.Push, Value = na.Rows.Count });
                                ops.Add(new Op { Type = OpType.Push, Value = columns });
                                ops.Add(new Op { Type = OpType.NewArray, PrimitiveRef = elem, Slot = 2 });
                                for (int row = 0; row < na.Rows.Count; row++)
                                    for (int column = 0; column < columns; column++)
                                    {
                                        ops.Add(new Op { Type = OpType.Dup });
                                        ops.Add(new Op { Type = OpType.Push, Value = row });
                                        ops.Add(new Op { Type = OpType.Push, Value = column });
                                        var itemType = EmitExpr(na.Rows[row][column]);
                                        if (itemType != elem && !IsAssignable(itemType, elem) && !(IsScalar(itemType) && IsScalar(elem)))
                                            throw Err(na.Rows[row][column].Line, $"dizi baslaticisi eleman tipine uymuyor: {itemType.Name} -> {elem.Name}");
                                        ops.Add(new Op { Type = OpType.SetIndex, Slot = 2 });
                                    }
                                return c.ArrayType(elem, 2);
                            }
                            if (na.Items != null) // new T[] { e1, e2, ... }
                            {
                                ops.Add(new Op { Type = OpType.Push, Value = na.Items.Count });
                                ops.Add(new Op { Type = OpType.NewArray, PrimitiveRef = elem });
                                for (int ii = 0; ii < na.Items.Count; ii++)
                                {
                                    ops.Add(new Op { Type = OpType.Dup });
                                    ops.Add(new Op { Type = OpType.Push, Value = ii });
                                    var it = EmitExpr(na.Items[ii]);
                                    if (it != elem && !IsAssignable(it, elem) && !(IsScalar(it) && IsScalar(elem)))
                                        throw Err(na.Items[ii].Line, $"dizi baslaticisi eleman tipine uymuyor: {it.Name} -> {elem.Name}");
                                    ops.Add(new Op { Type = OpType.SetIndex });
                                }
                                return c.ResolveType(na.ElemTypeName + "[]", ns, na.Line);
                            }
                            foreach (var size in na.Sizes) EmitExpr(size);
                            if (na.Sizes.Count == 0) throw Err(na.Line, "dizi en az bir boyut ister");
                            ops.Add(new Op { Type = OpType.NewArray, PrimitiveRef = elem, Slot = na.Sizes.Count });
                            return c.ArrayType(elem, na.Sizes.Count);
                        }
                    case ECall call: return EmitCall(call);
                    case ECast ct:
                        {
                            var src = EmitExpr(ct.Expr);
                            var target = c.ResolveType(ct.TypeName, ns, ct.Line);
                            if (src == Primitive.Void &&
                                ((target.Type == PrimitiveType.Model && !target.IsStruct) || target.Type == PrimitiveType.Array))
                                return target; // (SomeReference)null: ayni null pointer, runtime islemi yok
                            if (target == Primitive.Object && src.IsGenericParameter)
                            {
                                ops.Add(new Op { Type = OpType.Box, PrimitiveRef = src });
                                return target;
                            }
                            if (target.IsGenericParameter && src.Type == PrimitiveType.Model && !src.IsStruct)
                            {
                                ops.Add(new Op { Type = OpType.GenericCast, PrimitiveRef = target });
                                return target;
                            }
                            if (target.IsGenericParameter || src.IsGenericParameter)
                                throw Err(ct.Line, "kisitsiz tip parametresi cast edilemez (boxing yok - bilincli mimari karar)");
                            if (target == src) return target; // no-op
                            if (IsNullable(src, out var nullableValue) && IsScalar(target))
                            {
                                int nullableTemp = DeclareTemp(src);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = nullableTemp });
                                ops.Add(new Op { Type = OpType.AddrLocal, Slot = nullableTemp });
                                var getter = ResolveMethod(src, "get_Value", new List<ArgInfo>())
                                    ?? throw Err(ct.Line, $"Nullable Value getter bulunamadi: {src.Name}");
                                EmitInvoke(getter);
                                src = nullableValue;
                                if (target == src) return target;
                            }
                            if (target == Primitive.Object && Boxable(src)) // (object)deger: acik boxing
                            {
                                ops.Add(new Op { Type = OpType.Box, PrimitiveRef = src });
                                return target;
                            }
                            if (target == Primitive.Object && src.Type == PrimitiveType.Array)
                                return target; // array bir managed reference'tir; object upcast boxing gerektirmez
                            if (Boxable(target) && src.Type == PrimitiveType.Model && !src.IsStruct) // unbox: exact tip (C#)
                            {
                                if (src != Primitive.Object)
                                    throw Err(ct.Line, $"unbox yalniz object'ten: {src.Name} -> {target.Name} (iface kaynagi struct boxing diliminde)");
                                ops.Add(new Op { Type = OpType.Unbox, PrimitiveRef = target });
                                return target;
                            }
                            if (target.Type == PrimitiveType.Model)
                            {
                                if (target.IsStruct || src.Type != PrimitiveType.Model || src.IsStruct)
                                    throw Err(ct.Line, $"class cast yalniz class'lar arasinda: {src.Name} -> {target.Name}");
                                ops.Add(new Op { Type = OpType.CastClass, PrimitiveRef = target });
                                return target;
                            }
                            if (target.Type == PrimitiveType.Array && src.Type == PrimitiveType.Model && !src.IsStruct)
                            {
                                // Tum managed diziler VmArray yerlesimini paylasir. Element-Type RTTI henuz
                                // descriptor'da olmadigi icin bu yol yalniz referans reinterpretation yapar.
                                ops.Add(new Op { Type = OpType.CastClass, PrimitiveRef = target });
                                return target;
                            }
                            if (IsScalar(target) && IsScalar(src))
                            {
                                ops.Add(new Op { Type = OpType.Conv, PrimitiveRef = target });
                                return target;
                            }
                            if ((target.Type == PrimitiveType.Pointer && (src == Primitive.Long || src == Primitive.ULong)) ||
                                ((target == Primitive.Long || target == Primitive.ULong) && src.Type == PrimitiveType.Pointer))
                            {
                                ops.Add(new Op { Type = OpType.Conv, PrimitiveRef = target });
                                return target;
                            }
                            throw Err(ct.Line, $"desteklenmeyen cast: {src.Name} -> {target.Name}");
                        }
                    case EDefault ed:
                        {
                            var dt2 = c.ResolveType(ed.TypeName, ns, ed.Line);
                            ops.Add(new Op { Type = OpType.Default, PrimitiveRef = dt2 }); // T ise klonlamada somutlanir
                            return dt2;
                        }
                    case ETypeOf tof:
                        {
                            var tt = c.ResolveType(tof.TypeName, ns, tof.Line);
                            bool valuePrim = tt.Type != PrimitiveType.Model && tt.Type != PrimitiveType.Array
                                && tt.Type != PrimitiveType.FixedArray && tt.Type != PrimitiveType.Pointer && tt.Type != PrimitiveType.Void;
                            if (!tt.IsGenericParameter && !valuePrim && tt.Type != PrimitiveType.Array && tt.Type != PrimitiveType.Model)
                                throw Err(tof.Line, "typeof yalniz class/interface/primitive/enum/array/struct destekliyor");
                            ops.Add(new Op { Type = OpType.TypeOf, PrimitiveRef = tt });
                            return c.ResolveType("System.Type", ns, tof.Line);
                        }
                    case EIs eis:
                        {
                            var src = EmitExpr(eis.Expr);
                            if (eis.TypeName == "null")
                            {
                                bool reference = (src.Type == PrimitiveType.Model && !src.IsStruct) || src.Type == PrimitiveType.Array;
                                if (!reference)
                                    throw Err(eis.Line, $"is null yalniz referans tiplerinde destekleniyor: {src.Name}");
                                ops.Add(new Op { Type = OpType.Default, PrimitiveRef = src });
                                ops.Add(new Op { Type = OpType.Ceq });
                                return Primitive.Bool;
                            }
                            var target = c.ResolveType(eis.TypeName, ns, eis.Line);
                            if (target == Primitive.ValueType)
                            {
                                if (eis.VarName != null)
                                    throw Err(eis.Line, "ValueType pattern degiskeni boxing gerektirir ve desteklenmiyor");
                                ops.Add(new Op { Type = OpType.IsValueType, PrimitiveRef = src });
                                return Primitive.Bool;
                            }
                            if ((src.IsGenericParameter && src.Parent == null) || (target.IsGenericParameter && target.Parent == null))
                                throw Err(eis.Line, "kisitsiz tip parametresi uzerinde is kullanilamaz (boxing yok)");
                            if (Boxable(target) && src.Type == PrimitiveType.Model && !src.IsStruct) // box tip testi: o is int [i]
                            {
                                if (eis.VarName != null) // eslesirse payload'i degiskene bagla (tutmazsa 0 - okunmaz)
                                {
                                    ops.Add(new Op { Type = OpType.Dup });
                                    ops.Add(new Op { Type = OpType.UnboxOrDefault, PrimitiveRef = target });
                                    int bs = DeclareLocal(eis.VarName, target, eis.Line);
                                    ops.Add(new Op { Type = OpType.SetLocal, Slot = bs });
                                }
                                ops.Add(new Op { Type = OpType.IsType, PrimitiveRef = target });
                                return Primitive.Bool;
                            }
                            if (src.Type != PrimitiveType.Model || src.IsStruct || target.Type != PrimitiveType.Model || target.IsStruct)
                                throw Err(eis.Line, "is yalniz class tipleri arasinda");
                            if (eis.VarName != null) // pattern: eslesirse cast'i degiskene bagla (x is T ad)
                            {
                                ops.Add(new Op { Type = OpType.Dup });
                                ops.Add(new Op { Type = OpType.AsType, PrimitiveRef = target });
                                int ps = DeclareLocal(eis.VarName, target, eis.Line);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = ps });
                            }
                            ops.Add(new Op { Type = OpType.IsType, PrimitiveRef = target });
                            return Primitive.Bool;
                        }
                    case EAs eas:
                        {
                            var src = EmitExpr(eas.Expr);
                            var target = c.ResolveType(eas.TypeName, ns, eas.Line);
                            if (target.IsGenericParameter && target.Parent == null)
                                throw Err(eas.Line, "kisitsiz tip parametresi hedefinde as kullanilamaz");
                            if (src.Type != PrimitiveType.Model || src.IsStruct || target.Type != PrimitiveType.Model || target.IsStruct)
                                throw Err(eas.Line, "as yalniz class tipleri arasinda");
                            ops.Add(new Op { Type = OpType.AsType, PrimitiveRef = target });
                            return target;
                        }
                    default: throw Err(e.Line, $"desteklenmeyen ifade: {e.GetType().Name}");
                }
            }

            static bool IsScalar(Primitive t) =>
                t.Type == PrimitiveType.Int || t.Type == PrimitiveType.Float || t.Type == PrimitiveType.Double ||
                t.Type == PrimitiveType.Long || t.Type == PrimitiveType.Short || t.Type == PrimitiveType.Byte ||
                t.Type == PrimitiveType.Char || t.Type == PrimitiveType.UInt || t.Type == PrimitiveType.ULong ||
                t.Type == PrimitiveType.UShort || t.Type == PrimitiveType.SByte;

            // object'e kutulanabilir deger tipleri (enum dahil - kendi descriptor'iyla; struct dilim D'de)
            static bool Boxable(Primitive t) => IsScalar(t) || t.Type == PrimitiveType.Bool;

            Primitive EmitLit(ELit lit)
            {
                switch (lit.Tag)
                {
                    case "int": ops.Add(new Op { Type = OpType.Push, Value = int.Parse(lit.Value) }); return Primitive.Int;
                    case "uint": ops.Add(new Op { Type = OpType.Push, Value = uint.Parse(lit.Value) }); return Primitive.UInt;
                    case "long": ops.Add(new Op { Type = OpType.Push, Value = long.Parse(lit.Value) }); return Primitive.Long;
                    case "ulong": ops.Add(new Op { Type = OpType.Push, Value = ulong.Parse(lit.Value) }); return Primitive.ULong;
                    case "str": ops.Add(new Op { Type = OpType.Push, Value = string.Intern(lit.Value) }); return Primitive.String; // interning: ayni metin = ayni nesne (C k_strN paritesi)
                    case "null": ops.Add(new Op { Type = OpType.Push, Value = null }); return Primitive.Void; // tipsiz null (yalniz ==/!= karsilastirmasi)
                    case "float": ops.Add(new Op { Type = OpType.Push, Value = float.Parse(lit.Value, CultureInfo.InvariantCulture) }); return Primitive.Float;
                    case "double": ops.Add(new Op { Type = OpType.Push, Value = double.Parse(lit.Value, CultureInfo.InvariantCulture) }); return Primitive.Double;
                    case "bool": ops.Add(new Op { Type = OpType.Push, Value = lit.Value == "true" }); return Primitive.Bool;
                    case "chr": ops.Add(new Op { Type = OpType.Push, Value = lit.Value[0] }); return Primitive.Char;
                    default: throw Err(lit.Line, $"desteklenmeyen literal: {lit.Tag} (string corlib bekliyor)");
                }
            }

            Primitive EmitName(EName n)
            {
                if (!n.IsEscaped && n.Name == "this")
                {
                    if (captureSources != null && captureSources.TryGetValue(n.Name, out var capturedThis))
                        return EmitCapture(capturedThis);
                    RequireThis(n.Line);
                    ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    return currentClass.IsStruct ? c.Ptr(currentClass) : currentClass; // struct this = adres
                }
                if (localSlot.TryGetValue(n.Name, out var slot))
                {
                    if (hoistedCells.TryGetValue(n.Name, out var hoistedLocal))
                        return EmitHoistedRead(hoistedLocal);
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = slot });
                    return code.Locals[slot];
                }
                int a = ArgSlot(n.Name);
                if (a >= 0)
                {
                    if (hoistedCells.TryGetValue(n.Name, out var hoistedArg))
                        return EmitHoistedRead(hoistedArg);
                    var arg = code.Arguments[a];
                    ops.Add(new Op { Type = OpType.GetArg, Slot = a });
                    if (arg.IsRef || arg.IsOut) ops.Add(new Op { Type = OpType.LoadInd }); // pointer'in gosterdigini oku
                    return arg.Type;
                }
                if (captureSources != null && captureSources.TryGetValue(n.Name, out var capture))
                    return EmitCapture(capture);
                var conz = c.FindConstant(currentClass, n.Name);
                if (conz != null) return PushConst(conz.Value);
                var field = FindField(currentClass, n.Name);
                if (field != null)
                {
                    EmitImplicitThis(n.Line);
                    ops.Add(new Op { Type = OpType.GetField, Field = field });
                    return field.Type;
                }
                var (vfield, vOwner) = c.FindFieldView(currentClass, n.Name, false);
                if (vfield != null) // generic base zincirinden gelen alan
                {
                    EmitImplicitThis(n.Line);
                    AddFieldOp(OpType.GetField, vfield, vOwner);
                    return c.FieldTypeFor(vfield, vOwner);
                }
                var sfield = FindStaticField(currentClass, n.Name);
                if (sfield != null)
                {
                    ops.Add(new Op { Type = OpType.GetStatic, Field = sfield });
                    return sfield.Type;
                }
                var (vsfield, vsOwner) = c.FindFieldView(currentClass, n.Name, true);
                if (vsfield != null)
                {
                    AddFieldOp(OpType.GetStatic, vsfield, vsOwner);
                    return c.FieldTypeFor(vsfield, vsOwner);
                }
                var prc = ResolveMethod(currentClass, "get_" + n.Name, new List<ArgInfo>()); // kendi sinifin property'si
                if (prc != null)
                {
                    if (!prc.Entry.Code.IsStatic)
                    {
                        EmitImplicitThis(n.Line);
                    }
                    EmitInvoke(prc);
                    return prc.ReturnType;
                }
                throw Err(n.Line, $"bilinmeyen isim: {n.Name}");
            }

            static Primitive Promote(Primitive a, Primitive b) => Primitive.PromoteNumeric(a, b);

            Primitive EmitBin(EBin b)
            {
                if (b.Op == "&&" || b.Op == "||")
                    return EmitShortCircuit(b);

                var folded = FoldConst(b); // C# sabit ifade katlamasi
                if (folded != null) return EmitLit(folded);

                var lt = EmitExpr(b.L);
                var rt = EmitExpr(b.R);
                return ApplyBin(b.Op, lt, rt, b.Line);
            }

            // $"a/{x}": parcalar soldan saga String.operator + zincirine iner.
            // Runtime birlestirmesi scratch-buffer kullandigi icin ara string tahsisi yapmaz.
            Primitive EmitInterpolated(EInterpolated interpolated)
            {
                EmitLit(new ELit { Line = interpolated.Line, Tag = "str", Value = "" });
                foreach (var part in interpolated.Parts)
                {
                    var type = part.Expr != null
                        ? EmitExpr(part.Expr)
                        : EmitLit(new ELit { Line = interpolated.Line, Tag = "str", Value = part.Text });
                    ApplyBin("+", Primitive.String, type, interpolated.Line);
                }
                return Primitive.String;
            }

            Primitive EmitThrowExpression(EThrow thrown)
            {
                var exceptionType = EmitExpr(thrown.Value);
                var requiredType = c.ResolveType(WellKnown.Exception, ns, thrown.Line);
                if (!IsAssignable(exceptionType, requiredType))
                    throw Err(thrown.Line, $"throw ifadesi Exception turevi ister: {exceptionType.Name}");
                ops.Add(new Op { Type = OpType.Throw });
                return Primitive.Void; // control flow geri donmez
            }

            // operandlar STACK'TE varsayilir; operator cozumu + op emisyonu (EmitBin ve bilesik atama ortak)
            Primitive ApplyBin(string op, Primitive lt, Primitive rt, int line)
            {
                bool leftIsNullable = IsNullable(lt, out var leftValue);
                bool rightIsNullable = IsNullable(rt, out var rightValue);
                if (leftIsNullable || rightIsNullable)
                {
                    var lv = leftValue ?? lt;
                    var rv = rightValue ?? rt;
                    bool comparison = op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";
                    bool nullableNullEquality = (op == "==" || op == "!=") && (lv == Primitive.Void || rv == Primitive.Void);
                    if ((!IsScalar(lv) || !IsScalar(rv)) && !nullableNullEquality)
                        throw Err(line, $"lifted operator yalniz sayisal Nullable<T> icin destekleniyor: {lt.Name} {op} {rt.Name}");
                    if (op != "+" && op != "-" && op != "*" && op != "/" && op != "%" && !comparison)
                        throw Err(line, $"Nullable<T> icin desteklenmeyen operator: {op}");
                    var result = comparison ? Primitive.Bool : NullableOf(Promote(lv, rv));
                    ops.Add(new Op { Type = OpType.NullableBinary, PrimitiveRef = result, Value = op });
                    return result;
                }

                if (lt.IsDelegate && rt == lt && (op == "+" || op == "-"))
                {
                    ops.Add(new Op { Type = op == "+" ? OpType.DelegateCombine : OpType.DelegateRemove, PrimitiveRef = lt });
                    return lt;
                }

                bool IntegralOffset(Primitive type) => IsScalar(type) && type != Primitive.Bool && type.Type != PrimitiveType.Float && type.Type != PrimitiveType.Double;
                if (lt.Type == PrimitiveType.Pointer && (op == "+" || op == "-") && IntegralOffset(rt))
                {
                    ops.Add(new Op { Type = op == "+" ? OpType.Add : OpType.Sub });
                    return lt;
                }
                if (rt.Type == PrimitiveType.Pointer && op == "+" && IntegralOffset(lt))
                {
                    ops.Add(new Op { Type = OpType.Add });
                    return rt;
                }

                // operator overloading: operand Model ise op_xxx static methodu cozulur (once sol, sonra sag sinif).
                // null karsilastirmasi HARIC (x == null / x != null dogrudan Ceq'e duser)
                bool nullCompare = (op == "==" || op == "!=") && (lt == Primitive.Void || rt == Primitive.Void);
                if ((lt.Type == PrimitiveType.Model || rt.Type == PrimitiveType.Model) && !nullCompare)
                {
                    var opName = CsParser.OperatorMethodName(op) ?? throw Err(line, $"model tipinde desteklenmeyen operator: {op}");
                    var argInfos = new List<ArgInfo> { new ArgInfo { Type = lt }, new ArgInfo { Type = rt } };
                    var rc = (lt.Type == PrimitiveType.Model ? ResolveMethod(lt, opName, argInfos) : null)
                          ?? (rt.Type == PrimitiveType.Model ? ResolveMethod(rt, opName, argInfos) : null);
                    if (rc == null)
                    {
                        // C#: referans tiplerde tanimsiz ==/!= REFERANS esitligidir (operator gerekmez)
                        bool refCompare = (op == "==" || op == "!=") &&
                            lt.Type == PrimitiveType.Model && !lt.IsStruct && rt.Type == PrimitiveType.Model && !rt.IsStruct;
                        if (!refCompare) throw Err(line, $"operator {op} tanimli degil: {lt.Name}, {rt.Name}");
                        ops.Add(new Op { Type = op == "==" ? OpType.Ceq : OpType.Cne });
                        return Primitive.Bool;
                    }
                    EmitInvoke(rc); // operandlar zaten stack'te
                    return rc.ReturnType;
                }

                // operandlar skaler bolgede: enum'lar yalniz ayni-tip karsilastirma ve [Flags] bit islerine izinli (C#)
                if (lt.IsEnum || rt.IsEnum)
                {
                    bool same = lt == rt;
                    bool cmp = op == "==" || op == "!=" || op == "<" || op == ">" || op == "<=" || op == ">=";
                    bool bit = op == "&" || op == "|" || op == "^";
                    if (!same || !(cmp || bit))
                        throw Err(line, $"enum aritmetigi cast ister: {lt.Name} {op} {rt.Name}");
                }
                // C#: ulong ile isaretli tip ayni ifadede olamaz (shift HARIC: sayac hep int)
                bool Signed(Primitive t) => t.Type == PrimitiveType.Int || t.Type == PrimitiveType.Long || t.Type == PrimitiveType.Short || t.Type == PrimitiveType.SByte;
                if (op != "<<" && op != ">>" &&
                    ((lt.Type == PrimitiveType.ULong && Signed(rt)) || (rt.Type == PrimitiveType.ULong && Signed(lt))))
                    throw Err(line, $"ulong isaretli tiple karisamaz (C#): {lt.Name} {op} {rt.Name}");

                switch (op)
                {
                    case "+": ops.Add(new Op { Type = OpType.Add }); return Promote(lt, rt);
                    case "-": ops.Add(new Op { Type = OpType.Sub }); return Promote(lt, rt);
                    case "*": ops.Add(new Op { Type = OpType.Mul }); return Promote(lt, rt);
                    case "/": ops.Add(new Op { Type = OpType.Div }); return Promote(lt, rt);
                    case "%": ops.Add(new Op { Type = OpType.Mod }); return Promote(lt, rt);
                    case "==": ops.Add(new Op { Type = OpType.Ceq }); return Primitive.Bool;
                    case "!=": ops.Add(new Op { Type = OpType.Cne }); return Primitive.Bool;
                    case "<": ops.Add(new Op { Type = OpType.Clt }); return Primitive.Bool;
                    case ">": ops.Add(new Op { Type = OpType.Cgt }); return Primitive.Bool;
                    case "<=": ops.Add(new Op { Type = OpType.Cle }); return Primitive.Bool;
                    case ">=": ops.Add(new Op { Type = OpType.Cge }); return Primitive.Bool;
                    case "<<": RequireIntLike(lt, line); ops.Add(new Op { Type = OpType.Shl }); return lt; // C#: sonuc sol operand tipi
                    case ">>": RequireIntLike(lt, line); ops.Add(new Op { Type = OpType.Shr }); return lt;
                    case "&": ops.Add(new Op { Type = OpType.And }); return BitType(lt, rt, line);
                    case "|": ops.Add(new Op { Type = OpType.Or }); return BitType(lt, rt, line);
                    case "^": ops.Add(new Op { Type = OpType.Xor }); return BitType(lt, rt, line);
                    default: throw Err(line, $"desteklenmeyen operator: {op}");
                }
            }

            void RequireIntLike(Primitive t, int line)
            {
                if (t.Type == PrimitiveType.Float || t.Type == PrimitiveType.Double || !IsScalar(t))
                    throw Err(line, $"tam sayi tipi gerekli: {t.Name}");
            }

            // & | ^: bool&bool = bool (kisa devresiz mantik, C# gibi); tam sayilar Promote
            Primitive BitType(Primitive lt, Primitive rt, int line)
            {
                if (lt == Primitive.Bool && rt == Primitive.Bool) return Primitive.Bool;
                RequireIntLike(lt, line);
                RequireIntLike(rt, line);
                return Promote(lt, rt);
            }

            // Sabit katlamasi. str+str ZORUNLU C# semantigi: derleyici sabit birlestirmeyi derleme
            // zamaninda yapar ve sonuc intern edilir ("a"+"b" ile "ab" AYNI nesnedir - referans
            // esitligi dahil; C tarafinda ayni k_str). int katlamasi optimizasyon (unchecked wrap
            // runtime Add/Sub/Mul ile birebir ayni). Bolme/mod sifirsa katlanmaz (runtime firlatir).
            // const alanlar da katlanir (golgelemeyen adlar).
            ELit FoldConst(Expr e)
            {
                if (e is ELit l) return (l.Tag == "str" || l.Tag == "int") ? l : null;
                if (e is EName n && !localSlot.ContainsKey(n.Name) && ArgSlot(n.Name) < 0)
                {
                    var cv = c.FindConstant(currentClass, n.Name);
                    if (cv != null && cv.Value.value is int iv) return new ELit { Tag = "int", Value = iv.ToString(), Line = e.Line };
                    if (cv != null && cv.Value.value is string sv) return new ELit { Tag = "str", Value = sv, Line = e.Line };
                    return null;
                }
                if (e is EField cf && cf.Obj is EName && TryResolveTypeName(cf.Obj, out var ct) && !ct.IsEnum)
                {
                    var cv = c.FindConstant(ct, cf.Name);
                    if (cv != null && cv.Value.value is int iv) return new ELit { Tag = "int", Value = iv.ToString(), Line = e.Line };
                    if (cv != null && cv.Value.value is string sv) return new ELit { Tag = "str", Value = sv, Line = e.Line };
                    return null;
                }
                if (!(e is EBin b)) return null;
                var lf = FoldConst(b.L);
                if (lf == null) return null;
                var rf = FoldConst(b.R);
                if (rf == null) return null;
                if (lf.Tag == "str" && rf.Tag == "str" && b.Op == "+")
                    return new ELit { Tag = "str", Value = lf.Value + rf.Value, Line = b.Line };
                if (lf.Tag == "int" && rf.Tag == "int")
                {
                    int x = int.Parse(lf.Value), y = int.Parse(rf.Value);
                    long? v;
                    switch (b.Op)
                    {
                        case "+": v = (long)x + y; break;
                        case "-": v = (long)x - y; break;
                        case "*": v = (long)x * y; break;
                        case "/": v = y != 0 ? x / y : (long?)null; break;
                        case "%": v = y != 0 ? x % y : (long?)null; break;
                        case "&": v = x & y; break;
                        case "|": v = x | y; break;
                        case "^": v = x ^ y; break;
                        case "<<": v = x << (y & 31); break;
                        case ">>": v = x >> (y & 31); break;
                        default: v = null; break;
                    }
                    if (v != null)
                        return new ELit { Tag = "int", Value = unchecked((int)v.Value).ToString(), Line = b.Line };
                }
                return null;
            }

            // kisa devre: jump hedeflerinde stack bos kalmali (CTranspiler kurali) -> sonuc temp local'de tasinir
            Primitive EmitShortCircuit(EBin b)
            {
                int tmp = DeclareTemp(Primitive.Bool);
                var lShort = new Label();
                var lEnd = new Label();
                EmitExpr(b.L);
                ops.Add(new Op { Type = b.Op == "&&" ? OpType.Brfalse : OpType.Brtrue, Label = lShort });
                EmitExpr(b.R);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                ops.Add(new Op { Type = OpType.Br, Label = lEnd });
                ops.Add(new Op { Type = OpType.Label, Label = lShort });
                ops.Add(new Op { Type = OpType.Push, Value = b.Op == "&&" ? 0 : 1 });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = tmp });
                return Primitive.Bool;
            }

            struct ArgInfo { public Primitive Type; public bool IsRef; public bool IsOut; public bool IsOutVar; public bool HasIntegralConstant; public decimal IntegralConstant; public Op PatchOp; public string VarName; public int Line; public Primitive MgOwner; public string MgName; public bool MgBare; public bool IsMethodGroup; public int OpIndex; public ELambda MgLambda; }

            // ternary: dallarin sonucu temp'te tasinir (jump hedefinde stack bos kurali).
            // Temp tipi once then-tipiyle acilir, else'ten sonra ortak tipe GUNCELLENIR.
            Primitive EmitTernary(ETernary t)
            {
                var lElse = new Label();
                var lEnd = new Label();
                EmitExpr(t.Cond);
                ops.Add(new Op { Type = OpType.Brfalse, Label = lElse });
                var t1 = EmitExpr(t.Then);
                int tmp = DeclareTemp(t1);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                ops.Add(new Op { Type = OpType.Br, Label = lEnd });
                ops.Add(new Op { Type = OpType.Label, Label = lElse });
                var t2 = EmitExpr(t.Else);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tmp });
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = tmp });
                var join = JoinType(t1, t2, t.Line);
                code.Locals[tmp] = join;
                return join;
            }

            // left ?? right: left bir kez okunur; null degilse right'a hic girilmez.
            // Nullable value type yuzeyi olmadigindan yalniz referans/dizi tipleri kapsamdadir.
            Primitive EmitCoalesce(ECoalesce coalesce)
            {
                var leftType = EmitExpr(coalesce.Left);
                if (IsNullable(leftType, out var valueType))
                {
                    int nullableTemp = DeclareTemp(leftType);
                    int resultTemp = DeclareTemp(valueType);
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = nullableTemp });
                    var nullableUseRight = new Label();
                    var nullableEnd = new Label();
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = nullableTemp });
                    ops.Add(new Op { Type = OpType.NullableHasValue });
                    ops.Add(new Op { Type = OpType.Brfalse, Label = nullableUseRight });
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = nullableTemp });
                    ops.Add(new Op { Type = OpType.NullableValue });
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = resultTemp });
                    ops.Add(new Op { Type = OpType.Br, Label = nullableEnd });
                    ops.Add(new Op { Type = OpType.Label, Label = nullableUseRight });
                    var nullableRightType = EmitExpr(coalesce.Right);
                    if (nullableRightType != valueType && !IsAssignable(nullableRightType, valueType) && !ImplicitNumeric(nullableRightType, valueType))
                        throw Err(coalesce.Line, $"?? sag tarafi Nullable deger tipine atanabilir olmali: {nullableRightType.Name} -> {valueType.Name}");
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = resultTemp });
                    ops.Add(new Op { Type = OpType.Label, Label = nullableEnd });
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = resultTemp });
                    return valueType;
                }
                bool nullableRef = (leftType.Type == PrimitiveType.Model && !leftType.IsStruct) || leftType.Type == PrimitiveType.Array;
                if (!nullableRef) throw Err(coalesce.Line, $"?? sol tarafi nullable referans olmali: {leftType.Name}");
                int temp = DeclareTemp(leftType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                var useRight = new Label();
                var end = new Label();
                ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                ops.Add(new Op { Type = OpType.Brfalse, Label = useRight });
                ops.Add(new Op { Type = OpType.Br, Label = end });
                ops.Add(new Op { Type = OpType.Label, Label = useRight });
                var rightType = EmitExpr(coalesce.Right);
                if (rightType != leftType && !IsAssignable(rightType, leftType))
                    throw Err(coalesce.Line, $"?? sag tarafi sola atanabilir olmali: {rightType.Name} -> {leftType.Name}");
                ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                ops.Add(new Op { Type = OpType.Label, Label = end });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                return leftType;
            }

            Primitive JoinType(Primitive a, Primitive b, int line)
            {
                if (a == b) return a;
                if (IsScalar(a) && IsScalar(b)) return Promote(a, b);
                if (a == Primitive.Void && b.Type == PrimitiveType.Model && !b.IsStruct) return b; // null ?: ref
                if (b == Primitive.Void && a.Type == PrimitiveType.Model && !a.IsStruct) return a;
                if (IsAssignable(a, b)) return b;
                if (IsAssignable(b, a)) return a;
                if (a == Primitive.Object && Boxable(b)) return a; // ternary dali kutulanir (Coerce)
                if (b == Primitive.Object && Boxable(a)) return b;
                throw Err(line, $"ternary dallari ortak tipe indirgenemiyor: {a.Name} / {b.Name}");
            }

            // ++/--: hedef bir kez degerlendirilir; postfix eski, prefix yeni degeri birakir
            Primitive EmitIncDec(EInc inc)
            {
                string op = inc.Delta > 0 ? "+" : "-";
                Primitive ReadOldPrepared(out System.Action<int> store)
                {
                    if (inc.Target is EName hoistedName && hoistedCells.TryGetValue(hoistedName.Name, out var hoisted))
                    {
                        ops.Add(new Op { Type = OpType.GetLocal, Slot = hoisted.Local });
                        ops.Add(new Op { Type = OpType.Dup });
                        ops.Add(new Op { Type = OpType.GetField, Field = hoisted.ValueField });
                        store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); ops.Add(new Op { Type = OpType.SetField, Field = hoisted.ValueField }); };
                        return hoisted.Type;
                    }
                    if (inc.Target is EName capturedName && captureSources != null && captureSources.TryGetValue(capturedName.Name, out var captured))
                    {
                        if (captured.Field == null) EmitCapture(captured);
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        ops.Add(new Op { Type = OpType.GetField, Field = captured.Field });
                        ops.Add(new Op { Type = OpType.Dup });
                        ops.Add(new Op { Type = OpType.GetField, Field = captured.Cell.ValueField });
                        store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); ops.Add(new Op { Type = OpType.SetField, Field = captured.Cell.ValueField }); };
                        return captured.Cell.Type;
                    }
                    switch (inc.Target)
                    {
                        case EName n when localSlot.TryGetValue(n.Name, out var slot):
                            {
                                ops.Add(new Op { Type = OpType.GetLocal, Slot = slot });
                                store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); ops.Add(new Op { Type = OpType.SetLocal, Slot = slot }); };
                                return code.Locals[slot];
                            }
                        case EName n when ArgSlot(n.Name) >= 0 && (code.Arguments[ArgSlot(n.Name)].IsRef || code.Arguments[ArgSlot(n.Name)].IsOut):
                            {
                                int slot = ArgSlot(n.Name);
                                ops.Add(new Op { Type = OpType.GetArg, Slot = slot });
                                ops.Add(new Op { Type = OpType.Dup });
                                ops.Add(new Op { Type = OpType.LoadInd });
                                store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); ops.Add(new Op { Type = OpType.StoreInd }); };
                                return code.Arguments[slot].Type;
                            }
                        case EName n when ArgSlot(n.Name) >= 0 && !code.Arguments[ArgSlot(n.Name)].IsRef && !code.Arguments[ArgSlot(n.Name)].IsOut:
                            {
                                int slot = ArgSlot(n.Name);
                                ops.Add(new Op { Type = OpType.GetArg, Slot = slot });
                                store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); ops.Add(new Op { Type = OpType.SetArg, Slot = slot }); };
                                return code.Arguments[slot].Type;
                            }
                        case EName n:
                            {
                                var (field, fOwner) = c.FindFieldView(currentClass, n.Name, false);
                                if (field != null)
                                {
                                    EmitImplicitThis(n.Line);
                                    ops.Add(new Op { Type = OpType.Dup });
                                    AddFieldOp(OpType.GetField, field, fOwner);
                                    store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); AddFieldOp(OpType.SetField, field, fOwner); };
                                    return c.FieldTypeFor(field, fOwner);
                                }
                                var (sf, sOwner) = c.FindFieldView(currentClass, n.Name, true);
                                if (sf != null)
                                {
                                    AddFieldOp(OpType.GetStatic, sf, sOwner);
                                    store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); AddFieldOp(OpType.SetStatic, sf, sOwner); };
                                    return c.FieldTypeFor(sf, sOwner);
                                }
                                var getter = ResolveMethod(currentClass, "get_" + n.Name, new List<ArgInfo>());
                                if (getter != null)
                                {
                                    var setter = ResolveMethod(currentClass, "set_" + n.Name,
                                        new List<ArgInfo> { new ArgInfo { Type = getter.ReturnType } });
                                    if (setter == null) throw Err(n.Line, $"++/-- hedef property setter ister: {n.Name}");
                                    if (getter.Entry.Code.IsStatic != setter.Entry.Code.IsStatic)
                                        throw Err(n.Line, $"property getter/setter static uyusmazligi: {n.Name}");
                                    if (!getter.Entry.Code.IsStatic)
                                    {
                                        EmitImplicitThis(n.Line);
                                        ops.Add(new Op { Type = OpType.Dup });
                                    }
                                    EmitInvoke(getter);
                                    store = tv =>
                                    {
                                        ops.Add(new Op { Type = OpType.GetLocal, Slot = tv });
                                        EmitInvoke(setter);
                                    };
                                    return getter.ReturnType;
                                }
                                throw Err(n.Line, $"++/-- hedefi degisken/alan olmali: {n.Name}");
                            }
                        case EField fe when !TryResolveTypeName(fe.Obj, out _):
                            {
                                var objType = EmitMemberObj(fe.Obj, true, fe.Line);
                                ops.Add(new Op { Type = OpType.Dup });
                                var (field, fOwner) = c.FindFieldView(objType, fe.Name, false);
                                if (field == null) throw Err(fe.Line, $"++/-- hedefi alan olmali: {objType.Name}.{fe.Name}");
                                AddFieldOp(OpType.GetField, field, fOwner);
                                store = tv => { ops.Add(new Op { Type = OpType.GetLocal, Slot = tv }); AddFieldOp(OpType.SetField, field, fOwner); };
                                return c.FieldTypeFor(field, fOwner);
                            }
                        case EIndex ix:
                            {
                                var arrType = EmitExpr(ix.Obj);
                                if (arrType.Type != PrimitiveType.Array && arrType.Type != PrimitiveType.FixedArray)
                                    throw Err(ix.Line, $"indekslenebilir tip degil: {arrType.Name}");
                                var indexTypes = EmitArrayIndices(ix.Indices, arrType, ix.Line);
                                var indexTemps = new int[indexTypes.Count];
                                for (int i = indexTypes.Count - 1; i >= 0; i--) { indexTemps[i] = DeclareTemp(indexTypes[i]); ops.Add(new Op { Type = OpType.SetLocal, Slot = indexTemps[i] }); }
                                int tObj = DeclareTemp(arrType);
                                ops.Add(new Op { Type = OpType.SetLocal, Slot = tObj });
                                ops.Add(new Op { Type = OpType.GetLocal, Slot = tObj });
                                foreach (var temp in indexTemps) ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                                ops.Add(new Op { Type = OpType.GetIndex, Slot = indexTemps.Length });
                                store = tv =>
                                {
                                    ops.Add(new Op { Type = OpType.GetLocal, Slot = tObj });
                                    foreach (var temp in indexTemps) ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                                    ops.Add(new Op { Type = OpType.GetLocal, Slot = tv });
                                    ops.Add(new Op { Type = OpType.SetIndex, Slot = indexTemps.Length });
                                };
                                return arrType.ElementType;
                            }
                        default: throw Err(inc.Line, "++/-- hedefi degisken/alan/dizi elemani olmali");
                    }
                }

                var t = ReadOldPrepared(out var storeFrom);
                if (!(IsScalar(t) && t.Type != PrimitiveType.Model))
                    throw Err(inc.Line, $"++/-- sayisal tip ister: {t.Name}");
                // stack: [alici?..., old]
                int tOld = -1;
                if (!inc.IsPrefix)
                {
                    tOld = DeclareTemp(t);
                    ops.Add(new Op { Type = OpType.Dup });
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = tOld });
                }
                ops.Add(new Op { Type = OpType.Push, Value = 1 });
                ops.Add(new Op { Type = inc.Delta > 0 ? OpType.Add : OpType.Sub });
                int tNew = DeclareTemp(t);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = tNew });
                storeFrom(tNew); // alici (varsa) stack'te bekliyor; store tuketir
                ops.Add(new Op { Type = OpType.GetLocal, Slot = inc.IsPrefix ? tNew : tOld });
                return t;
            }

            // argumanlari hedef listeye emit eder; ref/out icin adres uretir (AddrLocal/AddrArg/pointer iletme)
            List<ArgInfo> EmitArgsTo(List<Op> target, List<Expr> args)
            {
                var infos = new List<ArgInfo>();
                var saved = cur;
                cur = target;
                foreach (var a in args)
                {
                    if (a is ERef r)
                    {
                        if (r.DeclareVar)
                        {
                            var vn = ((EName)r.Target).Name;
                            if (r.DeclType != null) // out T x: hemen bildir
                            {
                                var dt = c.ResolveType(r.DeclType, ns, r.Line);
                                int slot = DeclareLocal(vn, dt, r.Line);
                                ops.Add(new Op { Type = OpType.AddrLocal, Slot = slot });
                                infos.Add(new ArgInfo { Type = dt, IsRef = !r.IsOut, IsOut = r.IsOut });
                            }
                            else // out var x: tip cagri cozumunden sonra PatchOutVars baglar
                            {
                                var ph = new Op { Type = OpType.AddrLocal, Slot = -1 };
                                ops.Add(ph);
                                infos.Add(new ArgInfo { IsOut = true, IsOutVar = true, PatchOp = ph, VarName = vn, Line = r.Line });
                            }
                        }
                        else infos.Add(new ArgInfo { Type = EmitAddrOf(r), IsRef = !r.IsOut, IsOut = r.IsOut });
                    }
                    else if (a is ELambda alam)
                        infos.Add(new ArgInfo { IsMethodGroup = true, MgLambda = alam, OpIndex = target.Count, Line = a.Line }); // cozumden sonra derlenir
                    else if (IsArgMethodGroup(a, out var mgo, out var mgn, out var mgb))
                        infos.Add(new ArgInfo { IsMethodGroup = true, MgOwner = mgo, MgName = mgn, MgBare = mgb, OpIndex = target.Count, Line = a.Line }); // op'lar cozumden sonra eklenir (PatchMethodGroups)
                    else
                    {
                        int opIndex = target.Count;
                        var type = EmitExpr(a);
                        bool hasConstant = TryIntegralConstant(a, out var constant);
                        infos.Add(new ArgInfo { Type = type, OpIndex = opIndex, HasIntegralConstant = hasConstant, IntegralConstant = constant });
                    }
                }
                cur = saved;
                return infos;
            }

            static bool TryIntegralConstant(Expr expression, out decimal value)
            {
                bool negative = expression is EUn { Op: "-" };
                if (negative) expression = ((EUn)expression).E;
                if (expression is ELit literal &&
                    (literal.Tag == "int" || literal.Tag == "uint" || literal.Tag == "long" || literal.Tag == "ulong"))
                {
                    value = decimal.Parse(literal.Value, CultureInfo.InvariantCulture);
                    if (negative) value = -value;
                    return true;
                }
                value = 0;
                return false;
            }

            // ---- delegate destegi: method group donusumu + dolayli cagri (multicast/lambda YOK - bilincli sinir) ----
            bool MethodExists(Primitive cls, string name)
            {
                for (var t = cls; t != null; t = c.ParentView(t))
                    if (c.methodTable.TryGetValue(t.GenericTemplate ?? t, out var es) && es.Exists(en => en.SimpleName == name))
                        return true;
                return false;
            }

            // arguman pozisyonunda method group mu? (bare ad ya da Tip.Metod; obj.Metod arguman olarak v1'de yok)
            bool IsArgMethodGroup(Expr e, out Primitive owner, out string name, out bool bare)
            {
                owner = null; name = null; bare = false;
                if (e is EName n)
                {
                    if (localSlot.ContainsKey(n.Name) || ArgSlot(n.Name) >= 0) return false;
                    var (f1, _) = c.FindFieldView(currentClass, n.Name, false);
                    var (f2, _) = c.FindFieldView(currentClass, n.Name, true);
                    if (f1 != null || f2 != null || c.FindConstant(currentClass, n.Name) != null) return false;
                    if (!MethodExists(currentClass, n.Name)) return false;
                    owner = currentClass; name = n.Name; bare = true;
                    return true;
                }
                if (e is EField fe && TryResolveTypeName(fe.Obj, out var st))
                {
                    if (st.IsEnum || (st.Constants != null && st.Constants.ContainsKey(fe.Name))) return false;
                    var (sf, _) = c.FindFieldView(st, fe.Name, true);
                    if (sf != null || !MethodExists(st, fe.Name)) return false;
                    owner = st; name = fe.Name;
                    return true;
                }
                return false;
            }

            // delegate imzasi view uzerinden: Apply node'da (Func<int,int>) template imzasi T haritasiyla cozulur
            Primitive DelegateRet(Primitive dt) =>
                dt.GenericTemplate != null ? c.SubstView(dt.GenericTemplate.DelegateReturn, c.MapOf(dt)) : dt.DelegateReturn;
            List<Primitive> DelegateArgTypes(Primitive dt) =>
                dt.GenericTemplate != null
                    ? dt.GenericTemplate.DelegateParams.Select(p2 => c.SubstView(p2, c.MapOf(dt))).ToList()
                    : dt.DelegateParams;

            CompiledLambda CompileLambda(ELambda lam, Primitive dt)
            {
                var dps = DelegateArgTypes(dt);
                var ret = DelegateRet(dt);
                if (lam.Params.Count != dps.Count)
                    throw Err(lam.Line, $"lambda parametre sayisi delegate ile uyusmuyor: {dt.Name}");
                int lambdaId = c.lambdaCounter++;
                var closure = new Primitive
                {
                    Name = currentClass.Name + "$closure" + lambdaId,
                    Type = PrimitiveType.Model,
                    Parent = Primitive.Object
                };
                c.ctx.RegisterPrimitive(closure);
                var lc = new Code
                {
                    Owner = closure,
                    Name = "__lambda",
                    IsStatic = false,
                    ReturnType = ret,
                    SourceFile = code.SourceFile,
                    DisplayName = "lambda(" + string.Join(", ", dps.Select(Primitive.CsDisplay)) + ")"
                };
                lc.Arguments.Add(new Argument { Name = "this", Type = closure });
                for (int i = 0; i < lam.Params.Count; i++)
                {
                    if (lam.Params[i].TypeName != null) // acik tipli parametre delegate ile birebir olmali (C#)
                    {
                        var pt = c.ResolveType(lam.Params[i].TypeName, ns, lam.Line);
                        if (pt != dps[i]) throw Err(lam.Line, $"lambda parametre tipi delegate ile uyusmuyor: {lam.Params[i].Name}");
                    }
                    lc.Arguments.Add(new Argument { Name = lam.Params[i].Name, Type = dps[i] });
                }
                var captures = new Dictionary<string, CaptureSource>();
                foreach (var argument in code.Arguments.Select((value, slot) => (value, slot)))
                {
                    if (argument.value.Name == "this" && captureSources != null && captureSources.ContainsKey("this"))
                        continue;
                    captures[argument.value.Name] = new CaptureSource
                    {
                        Cell = hoistedCells.TryGetValue(argument.value.Name, out var existing) ? existing : new HoistedCell
                        {
                            Name = argument.value.Name,
                            Type = argument.value.Type,
                            Slot = argument.slot,
                            IsRef = argument.value.IsRef,
                            IsOut = argument.value.IsOut
                        }
                    };
                }
                foreach (var local in localSlot)
                    captures[local.Key] = new CaptureSource
                    {
                        Cell = hoistedCells.TryGetValue(local.Key, out var existing) ? existing : new HoistedCell
                        {
                            Name = local.Key,
                            Type = code.Locals[local.Value],
                            Slot = local.Value,
                            IsLocal = true
                        }
                    };
                if (captureSources != null)
                    foreach (var enclosing in captureSources)
                    {
                        if (captures.ContainsKey(enclosing.Key)) continue;
                        EnsureCaptureField(enclosing.Value);
                        captures[enclosing.Key] = new CaptureSource
                        {
                            Cell = enclosing.Value.Cell,
                            ParentField = enclosing.Value.Field
                        };
                    }
                var body = lam.BlockBody ?? (ret == Primitive.Void
                    ? new List<Stmt> { new SExpr { Line = lam.Line, E = lam.Body } }
                    : new List<Stmt> { new SReturn { Line = lam.Line, E = lam.Body } });
                try { new BodyEmitter(c, currentClass, ns, lc, false, captures).Emit(body); }
                catch (CsError ex)
                {
                    throw new CsError(c.file, lam.Line, $"lambda govdesi derlenemedi: {ex.Message}");
                }
                c.ctx.RegisterCode(lc);
                c.extraCodes.Add(lc);
                return new CompiledLambda { Code = lc, Closure = closure, Captures = captures };
            }

            void EmitLambdaNew(CompiledLambda lambda, Primitive delegateType, List<Op> target)
            {
                target.Add(new Op { Type = OpType.New, PrimitiveRef = lambda.Closure });
                foreach (var field in lambda.Closure.Fields)
                {
                    var capture = lambda.Captures.Values.First(value => value.Field == field);
                    var cell = capture.Cell;
                    if (capture.ParentField != null)
                    {
                        target.Add(new Op { Type = OpType.Dup });
                        target.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        target.Add(new Op { Type = OpType.GetField, Field = capture.ParentField });
                        target.Add(new Op { Type = OpType.SetField, Field = field });
                        continue;
                    }
                    if (cell.Local < 0)
                    {
                        cell.Local = DeclareTemp(cell.TypeModel);
                        target.Add(new Op { Type = OpType.New, PrimitiveRef = cell.TypeModel });
                        target.Add(new Op { Type = OpType.Dup });
                        target.Add(new Op { Type = cell.IsLocal ? OpType.GetLocal : OpType.GetArg, Slot = cell.Slot });
                        if (!cell.IsLocal && (cell.IsRef || cell.IsOut))
                            target.Add(new Op { Type = OpType.LoadInd });
                        target.Add(new Op { Type = OpType.SetField, Field = cell.ValueField });
                        target.Add(new Op { Type = OpType.SetLocal, Slot = cell.Local });
                        hoistedCells[cell.Name] = cell;
                    }
                    target.Add(new Op { Type = OpType.Dup });
                    target.Add(new Op { Type = OpType.GetLocal, Slot = cell.Local });
                    target.Add(new Op { Type = OpType.SetField, Field = field });
                }
                target.Add(new Op { Type = OpType.DelegateNew, Code = lambda.Code, PrimitiveRef = delegateType, Slot = -1 });
            }

            // delegate imzasina EXACT uyan method (v1: varyans yok, generic hedef yok)
            ResolvedCall FindDelegateTarget(Primitive owner, string name, Primitive dt)
            {
                var fake = DelegateArgTypes(dt).Select(pt => new ArgInfo { Type = pt }).ToList();
                var rc = ResolveMethodCore(owner, name, fake, null, 0);
                return rc != null && rc.ReturnType == DelegateRet(dt) && rc.MethodArgs.Count == 0 ? rc : null;
            }

            void EmitDelegateNewOp(ResolvedCall rc, Primitive dt)
            {
                var m = rc.Entry.Code;
                ops.Add(new Op { Type = OpType.DelegateNew, Code = m, PrimitiveRef = dt, Slot = (m.IsVirtual || m.IsOverride) ? -2 : -1 }); // -2: Resolver vtable slotunu doldurur (yaratimda cozum, C# gibi)
            }

            // method group / lambda -> delegate (atama/init/return baglamlari). true = ops uretildi.
            bool TryEmitDelegateNew(Expr e, Primitive dt)
            {
                if (dt == null || !dt.IsDelegate) return false;
                if (e is ETernary conditional)
                {
                    var useElse = new Label();
                    var end = new Label();
                    int temp = DeclareTemp(dt);
                    EmitExpr(conditional.Cond);
                    ops.Add(new Op { Type = OpType.Brfalse, Label = useElse });
                    EmitDelegateBranch(conditional.Then, dt);
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                    ops.Add(new Op { Type = OpType.Br, Label = end });
                    ops.Add(new Op { Type = OpType.Label, Label = useElse });
                    EmitDelegateBranch(conditional.Else, dt);
                    ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                    ops.Add(new Op { Type = OpType.Label, Label = end });
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                    return true;
                }
                if (e is ELambda lam)
                {
                    EmitLambdaNew(CompileLambda(lam, dt), dt, ops);
                    return true;
                }
                if (e is EName n)
                {
                    if (localSlot.ContainsKey(n.Name) || ArgSlot(n.Name) >= 0) return false;
                    var rc = FindDelegateTarget(currentClass, n.Name, dt);
                    if (rc == null) return false;
                    if (!rc.Entry.Code.IsStatic)
                    {
                        EmitImplicitThis(e.Line);
                    }
                    EmitDelegateNewOp(rc, dt);
                    return true;
                }
                if (e is EField fe)
                {
                    if (TryResolveTypeName(fe.Obj, out var st))
                    {
                        var rc = FindDelegateTarget(st, fe.Name, dt);
                        if (rc == null || !rc.Entry.Code.IsStatic) return false;
                        EmitDelegateNewOp(rc, dt);
                        return true;
                    }
                    var probe = new List<Op>(); // alici tipi icin gecici emit; uymazsa iz birakmaz
                    var saved = cur;
                    cur = probe;
                    Primitive rt;
                    try { rt = EmitExpr(fe.Obj); }
                    catch (CsError) { cur = saved; return false; }
                    cur = saved;
                    if (rt.Type != PrimitiveType.Model || rt.IsStruct) return false;
                    var rc2 = FindDelegateTarget(rt, fe.Name, dt);
                    if (rc2 == null || rc2.Entry.Code.IsStatic) return false;
                    ops.AddRange(probe);
                    EmitDelegateNewOp(rc2, dt); // sanal ise yaratimda alicinin vtable'i okunur
                    return true;
                }
                return false;
            }

            void EmitDelegateBranch(Expr expression, Primitive delegateType)
            {
                if (TryEmitDelegateNew(expression, delegateType)) return;
                var branchType = EmitExpr(expression);
                if (branchType != delegateType && branchType != Primitive.Void && !IsAssignable(branchType, delegateType))
                    throw Err(expression.Line, $"ternary dali delegate tipine atanamiyor: {branchType.Name} -> {delegateType.Name}");
            }

            // cozumden sonra method-group argumanlarinin op'larini kendi pozisyonlarina enjekte eder
            void PatchMethodGroups(List<ArgInfo> infos, ResolvedCall rc, List<Op> argOps, int parameterOffset = 0)
            {
                for (int i = infos.Count - 1; i >= 0; i--) // sondan basa: onceki OpIndex'ler kaymasin
                {
                    if (!infos[i].IsMethodGroup) continue;
                    var pdt = rc.Entry.ParamTypes[i + parameterOffset];
                    var substitutions = new Dictionary<Primitive, Primitive>();
                    if (rc.OwnerView != null && rc.OwnerView.GenericTemplate != null)
                        foreach (var pair in c.MapOf(rc.OwnerView)) substitutions[pair.Key] = pair.Value;
                    for (int genericIndex = 0; genericIndex < rc.Entry.Code.GenericParameters.Count; genericIndex++)
                        substitutions[rc.Entry.Code.GenericParameters[genericIndex]] = rc.MethodArgs[genericIndex];
                    pdt = c.SubstView(pdt, substitutions);
                    var inject = new List<Op>();
                    if (infos[i].MgLambda != null)
                    {
                        EmitLambdaNew(CompileLambda(infos[i].MgLambda, pdt), pdt, inject);
                        argOps.InsertRange(infos[i].OpIndex, inject);
                        continue;
                    }
                    var tgt = FindDelegateTarget(infos[i].MgOwner, infos[i].MgName, pdt) ?? throw Err(infos[i].Line, $"method group delegate imzasina uymuyor: {infos[i].MgName}");
                    if (!tgt.Entry.Code.IsStatic)
                    {
                        if (!infos[i].MgBare) throw Err(infos[i].Line, "instance method grubu dogrudan arguman olamaz (once degiskene ata - v1 siniri)");
                        RequireThis(infos[i].Line);
                        inject.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                    }
                    var m = tgt.Entry.Code;
                    inject.Add(new Op { Type = OpType.DelegateNew, Code = m, PrimitiveRef = pdt, Slot = (m.IsVirtual || m.IsOverride) ? -2 : -1 });
                    argOps.InsertRange(infos[i].OpIndex, inject);
                }
            }

            // delegate degeri cagrisi: f(args) -> arglar + CallIndirect
            Primitive EmitDelegateInvoke(Primitive dt, List<Op> argOps, List<ArgInfo> argInfos, int line)
            {
                var dps = DelegateArgTypes(dt);
                if (argInfos.Count != dps.Count)
                    throw Err(line, $"delegate {dt.Name} {dps.Count} arguman ister ({argInfos.Count} verildi)");
                for (int i = 0; i < argInfos.Count; i++)
                    if (argInfos[i].IsMethodGroup || argInfos[i].IsRef || argInfos[i].IsOut ||
                                                !(argInfos[i].Type == dps[i] || IsAssignable(argInfos[i].Type, dps[i]) || ImplicitNumeric(argInfos[i].Type, dps[i]) ||
                                                    dps[i] == Primitive.Object && Boxable(argInfos[i].Type) ||
                                                    argInfos[i].Type == Primitive.Void && (dps[i].Type == PrimitiveType.Array || dps[i].Type == PrimitiveType.Model && !dps[i].IsStruct)))
                        throw Err(line, $"delegate argumani parametreye uymuyor: {dt.Name} ({i + 1}. arguman)");
                for (int i = argInfos.Count - 1; i >= 0; i--)
                    if (dps[i] == Primitive.Object && Boxable(argInfos[i].Type))
                    {
                        int insertAt = i + 1 < argInfos.Count ? argInfos[i + 1].OpIndex : argOps.Count;
                        argOps.Insert(insertAt, new Op { Type = OpType.Box, PrimitiveRef = argInfos[i].Type });
                    }
                ops.AddRange(argOps);
                ops.Add(new Op { Type = OpType.CallIndirect, PrimitiveRef = dt });
                return DelegateRet(dt);
            }

            Primitive EmitDelegateExpressionInvoke(EInvoke invoke)
            {
                var targetType = EmitExpr(invoke.Target);
                if (!targetType.IsDelegate)
                    throw Err(invoke.Line, $"cagrilabilir ifade delegate olmali: {targetType.Name}");
                var argOps = new List<Op>();
                var argInfos = EmitArgsTo(argOps, invoke.Args);
                return EmitDelegateInvoke(targetType, argOps, argInfos, invoke.Line);
            }

            // receiver?.Member: receiver bir kez okunur; null dalinda Member hic degerlenmez.
            // Sonuc C# kurali geregi nullable referans/array olmali (value-type nullable ayri lowering ister).
            Primitive EmitNullConditionalMember(ENullConditionalMember member)
            {
                var receiverType = EmitExpr(member.Target);
                if (receiverType.Type != PrimitiveType.Model || receiverType.IsStruct)
                    throw Err(member.Line, $"null-conditional alici referans tipi olmali: {receiverType.Name}");
                int receiver = DeclareChainTemp(receiverType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = receiver });
                var whenNull = new Label();
                var end = new Label();
                ops.Add(new Op { Type = OpType.GetLocal, Slot = receiver });
                ops.Add(new Op { Type = OpType.Brfalse, Label = whenNull });
                var memberType = EmitExpr(new EField
                {
                    Line = member.Line,
                    Obj = new EName { Line = member.Line, Name = "\u0001chain" + receiver },
                    Name = member.Name
                });
                bool nullableResult = (memberType.Type == PrimitiveType.Model && !memberType.IsStruct) || memberType.Type == PrimitiveType.Array;
                if (!nullableResult)
                    throw Err(member.Line, $"null-conditional value-type uye Nullable sonucu ister: {member.Name}");
                int result = DeclareTemp(memberType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = result });
                ops.Add(new Op { Type = OpType.Br, Label = end });
                ops.Add(new Op { Type = OpType.Label, Label = whenNull });
                ops.Add(new Op { Type = OpType.Push, Value = null });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = result });
                ops.Add(new Op { Type = OpType.Label, Label = end });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = result });
                return memberType;
            }

            // action?.Invoke(args): alici once tek kez okunur; null ise args HIC degerlenmez.
            // Nullable<T> yuzeyi henuz olmadigindan yalniz void delegate sonucu desteklenir.
            Primitive EmitNullConditionalCall(ENullConditionalCall call, bool discardResult = false)
            {
                var receiverType = EmitExpr(call.Target);
                if (receiverType.Type != PrimitiveType.Model || receiverType.IsStruct)
                    throw Err(call.Line, $"null-conditional alici referans tipi olmali: {receiverType.Name}");
                bool delegateInvoke = call.Name == "Invoke" && receiverType.IsDelegate;
                int temp = DeclareTemp(receiverType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = temp });
                var skip = new Label();
                ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                ops.Add(new Op { Type = OpType.Brfalse, Label = skip });
                Primitive returnType;
                if (delegateInvoke)
                {
                    var argOps = new List<Op>();
                    var argInfos = EmitArgsTo(argOps, call.Args);
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                    returnType = EmitDelegateInvoke(receiverType, argOps, argInfos, call.Line);
                }
                else
                {
                    var argOps = new List<Op>();
                    var argInfos = EmitArgsTo(argOps, call.Args);
                    var method = ResolveMethod(receiverType, call.Name, argInfos)
                        ?? throw Err(call.Line, $"bilinmeyen method: {receiverType.Name}.{call.Name}");
                    if (method.Entry.Code.IsStatic)
                        throw Err(call.Line, $"static method instance uzerinden cagrilamaz: {call.Name}");
                    PatchOutVars(argInfos, method);
                    PatchMethodGroups(argInfos, method, argOps);
                    PackParams(method, argOps, argInfos, call.Line);
                    ops.Add(new Op { Type = OpType.GetLocal, Slot = temp });
                    ops.AddRange(argOps);
                    EmitDefaults(method.Entry, argInfos.Count, call.Line);
                    EmitInvoke(method);
                    returnType = method.ReturnType;
                }
                if (returnType == Primitive.Void)
                {
                    ops.Add(new Op { Type = OpType.Label, Label = skip });
                    return Primitive.Void;
                }
                if (discardResult)
                {
                    ops.Add(new Op { Type = OpType.Pop });
                    ops.Add(new Op { Type = OpType.Label, Label = skip });
                    return Primitive.Void;
                }
                bool referenceResult = returnType.Type == PrimitiveType.Array ||
                    returnType.Type == PrimitiveType.Model && !returnType.IsStruct;
                if (!referenceResult)
                    throw Err(call.Line, "deger donduren null-conditional cagri Nullable sonucu ister (henuz desteklenmiyor)");
                int result = DeclareTemp(returnType);
                var end = new Label();
                ops.Add(new Op { Type = OpType.SetLocal, Slot = result });
                ops.Add(new Op { Type = OpType.Br, Label = end });
                ops.Add(new Op { Type = OpType.Label, Label = skip });
                ops.Add(new Op { Type = OpType.Push, Value = null });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = result });
                ops.Add(new Op { Type = OpType.Label, Label = end });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = result });
                return returnType;
            }

            Primitive EmitAddrOf(ERef r)
            {
                if (TryEmitStructAddr(r.Target, out var structType))
                    return structType;
                if (r.Target is EField fieldTarget)
                {
                    if (TryResolveTypeName(fieldTarget.Obj, out var staticType))
                    {
                        var (targetStaticField, targetStaticOwner) = c.FindFieldView(staticType, fieldTarget.Name, true);
                        if (targetStaticField == null) throw Err(r.Line, $"ref/out static field bulunamadi: {fieldTarget.Name}");
                        AddFieldOp(OpType.AddrStatic, targetStaticField, targetStaticOwner);
                        return c.FieldTypeFor(targetStaticField, targetStaticOwner);
                    }
                    var ownerType = EmitMemberObj(fieldTarget.Obj, true, r.Line);
                    var (field, owner) = c.FindFieldView(ownerType, fieldTarget.Name, false);
                    if (field == null) throw Err(r.Line, $"ref/out field bulunamadi: {fieldTarget.Name}");
                    AddFieldOp(OpType.AddrField, field, owner);
                    return c.FieldTypeFor(field, owner);
                }
                if (r.Target is EIndex indexTarget)
                {
                    var arrayType = EmitExpr(indexTarget.Obj);
                    if (arrayType.Type != PrimitiveType.Array && arrayType.Type != PrimitiveType.FixedArray)
                        throw Err(r.Line, "ref/out index hedefi array olmali");
                    EmitArrayIndices(indexTarget.Indices, arrayType, indexTarget.Line);
                    ops.Add(new Op { Type = OpType.AddrElement, Slot = indexTarget.Indices.Count });
                    return arrayType.ElementType;
                }
                if (!(r.Target is EName n)) throw Err(r.Line, "ref/out hedefi adreslenebilir bir degisken olmali");
                if (localSlot.TryGetValue(n.Name, out var slot))
                {
                    ops.Add(new Op { Type = OpType.AddrLocal, Slot = slot });
                    return code.Locals[slot];
                }
                int a = ArgSlot(n.Name);
                if (a >= 0)
                {
                    var arg = code.Arguments[a];
                    if (arg.IsRef || arg.IsOut) ops.Add(new Op { Type = OpType.GetArg, Slot = a }); // pointer'i AYNEN ilet
                    else ops.Add(new Op { Type = OpType.AddrArg, Slot = a });
                    return arg.Type;
                }
                if (!code.IsStatic)
                {
                    var (field, owner) = c.FindFieldView(currentClass, n.Name, false);
                    if (field != null)
                    {
                        ops.Add(new Op { Type = OpType.GetArg, Slot = 0 });
                        AddFieldOp(OpType.AddrField, field, owner);
                        return c.FieldTypeFor(field, owner);
                    }
                }
                var (staticField, staticOwner) = c.FindFieldView(currentClass, n.Name, true);
                if (staticField != null)
                {
                    AddFieldOp(OpType.AddrStatic, staticField, staticOwner);
                    return c.FieldTypeFor(staticField, staticOwner);
                }
                throw Err(r.Line, $"ref/out hedefi local ya da parametre olmali: {n.Name}");
            }

            bool IsAssignable(Primitive arg, Primitive param)
            {
                // While compiling a generic template body, `this` is recorded as the open
                // template (Task<T>), while a delegate signature can carry its equivalent
                // constructed view (Task`1<T>). They denote the same type in that scope.
                if (OpenGenericEquivalent(arg, param)) return true;
                if (IsNullable(param, out var valueType) && (arg == Primitive.Void || arg == valueType || ImplicitNumeric(arg, valueType)))
                    return true;
                if (param == Primitive.Object && arg.Type == PrimitiveType.Array)
                    return true;
                if (param == Primitive.Object && arg.Type == PrimitiveType.Model && !arg.IsStruct)
                    return true; // her referans tipi (iface dahil) object'e atanabilir
                for (var t = arg; t != null; t = c.ParentView(t))
                {
                    if (t == param) return true;
                    var tpl = t.GenericTemplate ?? t;
                    if (param.IsInterface)
                    {
                        var map = t.GenericTemplate != null ? c.MapOf(t) : null;
                        foreach (var iface in tpl.Interfaces)
                            if (c.SubstView(iface, map) == param) return true; // constructed class -> kapali iface gorunumu
                    }
                }
                return false;
            }

            bool OpenGenericEquivalent(Primitive left, Primitive right)
            {
                if (left == null || right == null) return false;
                return IsOpenViewOf(left, right) || IsOpenViewOf(right, left);
            }

            bool IsOpenViewOf(Primitive template, Primitive view)
            {
                if (!template.IsGeneric || view.GenericTemplate != template ||
                    view.TypeArguments.Count != template.GenericParameters.Count) return false;
                for (int i = 0; i < template.GenericParameters.Count; i++)
                    if (view.TypeArguments[i] != template.GenericParameters[i]) return false;
                return true;
            }

            bool IsNullable(Primitive type, out Primitive valueType)
            {
                valueType = null;
                if (type == null || type.Type != PrimitiveType.Model || !type.IsStruct) return false;
                var template = type.GenericTemplate;
                if (template != null && template.Name == WellKnown.Nullable + "`1" && type.TypeArguments.Count == 1)
                {
                    valueType = type.TypeArguments[0];
                    return true;
                }
                if (type.Name.StartsWith(WellKnown.Nullable + "`1<") && type.Fields.Count == 2)
                {
                    valueType = type.Fields[1].Type;
                    return true;
                }
                return false;
            }

            Primitive NullableOf(Primitive valueType)
            {
                var template = c.ResolveTypeAllowTemplate(WellKnown.Nullable + "`1", ns, 0);
                return c.ApplyType(template, new List<Primitive> { valueType });
            }

            // cozulmus cagri: hedef + bulundugu zincir dugumu (Apply olabilir) + baglanan method tip argumanlari.
            // op.TypeArguments = [sinif argumanlari (ownerView Apply ise)] + [method argumanlari] (Resolver sozlesmesi).
            class ResolvedCall
            {
                public MethodEntry Entry;
                public Primitive OwnerView;
                public List<Primitive> MethodArgs = new List<Primitive>();
                public Primitive ReturnType;
                public bool IsExtension; // alici, static cagrinin ilk argumani olarak stack'te
                public bool ParamsExpanded;
            }

            // extension cozumu: kayitli (owner, ad) ciftlerinde alici+arglar imzasiyla arama.
            // C# sapmasi (bilincli): using kapsamina bakilmaz - tum extension'lar gorunur.
            ResolvedCall ResolveExtension(Primitive recv, string name, List<ArgInfo> args, List<Primitive> explicitArgs)
            {
                if (!(c.ctx.FrontendExtensions is List<(Primitive owner, string name)> reg)) return null;
                foreach (var a in args)
                    if (a.IsOutVar || a.IsRef || a.IsOut)
                        return null; // v1 siniri: extension cagrisinda out var / ref yok
                var full = new List<ArgInfo> { new ArgInfo { Type = recv } };
                full.AddRange(args);
                for (int pass = 0; pass <= 2; pass++)
                    foreach (var (owner, n) in reg)
                        if (n == name)
                        {
                            var rc = ResolveMethodCore(owner, name, full, explicitArgs, pass);
                            if (rc != null) { rc.IsExtension = true; return rc; }
                        }
                return null;
            }

            // overload cozumu UC GECIS: kesin -> ortuk sayisal genisletme -> boxing (deger -> object)
            // (C# better-match'in pratik yaklasigi: daha spesifik aday varken sonraki gecis secilmez)
            ResolvedCall ResolveMethod(Primitive cls, string simpleName, List<ArgInfo> args, List<Primitive> explicitMethodArgs = null)
                => ResolveMethodCore(cls, simpleName, args, explicitMethodArgs, 0)
                ?? ResolveMethodCore(cls, simpleName, args, explicitMethodArgs, 1)
                ?? ResolveMethodCore(cls, simpleName, args, explicitMethodArgs, 2);

            ResolvedCall ResolveMethodCore(Primitive cls, string simpleName, List<ArgInfo> args, List<Primitive> explicitMethodArgs, int pass)
            {
                // arama zinciri: parent zinciri ONCE (sinif uyeleri oncelikli), sonra taban arayuz
                // kapanisi (arayuz kalitimi: IEnumerator<T> : IEnumerator gibi inherited uyeler bulunsun).
                var chain = new List<Primitive>();
                var seenChain = new HashSet<Primitive>();
                for (var pt = cls; pt != null; pt = c.ParentView(pt)) if (seenChain.Add(pt.GenericTemplate ?? pt)) chain.Add(pt);
                for (int ci = 0; ci < chain.Count; ci++)
                {
                    var ct = chain[ci];
                    var ctpl = ct.GenericTemplate ?? ct;
                    foreach (var bi in ctpl.Interfaces)
                    {
                        var bv = ct.GenericTemplate != null ? c.SubstView(bi, c.MapOf(ct)) : bi;
                        if (bv != null && seenChain.Add(bv.GenericTemplate ?? bv)) chain.Add(bv);
                    }
                }
                foreach (var t in chain)
                {
                    var tpl = t.GenericTemplate ?? t;
                    if (!c.methodTable.TryGetValue(tpl, out var entries)) continue;
                    var classMap = t.GenericTemplate != null ? c.MapOf(t) : null;
                    foreach (var e in entries)
                    {
                        bool hasParams = e.Params.Count > 0 && e.Params[e.Params.Count - 1].IsParams;
                        int fixedCount = e.ParamTypes.Count - (hasParams ? 1 : 0);
                        bool paramsExpanded = hasParams && !(args.Count == e.ParamTypes.Count &&
                            args[fixedCount].Type != null && args[fixedCount].Type.Type == PrimitiveType.Array);
                        if (e.SimpleName != simpleName || (!hasParams && e.ParamTypes.Count < args.Count)) continue;
                        var gps = e.Code.GenericParameters;
                        if (explicitMethodArgs != null && explicitMethodArgs.Count != gps.Count) continue;
                        if (explicitMethodArgs == null && gps.Count > 0 && args.Count == 0) continue; // cikarim icin arguman lazim
                        var bind = new Dictionary<Primitive, Primitive>();
                        if (explicitMethodArgs != null)
                            for (int i = 0; i < gps.Count; i++) bind[gps[i]] = explicitMethodArgs[i];
                        bool ok = true;
                        for (int i = 0; i < args.Count && ok; i++)
                        {
                            int paramIndex = hasParams && i >= fixedCount ? fixedCount : i;
                            var pp = e.Params[paramIndex];
                            var paramType = paramsExpanded && i >= fixedCount ? e.ParamTypes[fixedCount].ElementType : e.ParamTypes[paramIndex];
                            if (args[i].IsMethodGroup) // parametre delegate + imzaya uyan method/lambda var mi
                            {
                                var pdt = c.SubstView(c.SubstView(paramType, classMap), bind);
                                if (pp.IsRef || pp.IsOut || pdt == null || !pdt.IsDelegate) { ok = false; continue; }
                                ok = args[i].MgLambda != null
                                    ? args[i].MgLambda.Params.Count == DelegateArgTypes(pdt).Count // govde derlemesi patch'te
                                    : FindDelegateTarget(args[i].MgOwner, args[i].MgName, pdt) != null;
                                continue;
                            }
                            if (pp.IsRef != args[i].IsRef || pp.IsOut != args[i].IsOut) { ok = false; continue; }
                            if (args[i].IsOutVar) ok = pp.IsOut; // out var: tipi parametreden alir
                            else if (pp.IsRef || pp.IsOut)
                            {
                                ok = Unify(paramType, args[i].Type, classMap, bind, gps);
                                if (ok) ok = c.SubstView(c.SubstView(paramType, classMap), bind) == args[i].Type;
                            }
                            else
                            {
                                ok = Unify(paramType, args[i].Type, classMap, bind, gps, pass);
                                if (!ok && pass >= 1)
                                {
                                    var resolvedParam = c.SubstView(c.SubstView(paramType, classMap), bind);
                                    ok = ImplicitIntegralConstant(args[i], resolvedParam);
                                }
                            }
                        }
                        for (int i = args.Count; i < fixedCount && ok; i++)
                            if (e.Params[i].Default == null) ok = false;
                        if (ok)
                            foreach (var gp in gps)
                                if (!bind.ContainsKey(gp)) ok = false; // tum method parametreleri baglanmali (C# CS0411)
                        if (!ok) continue;
                        var full = new Dictionary<Primitive, Primitive>(bind);
                        if (classMap != null) foreach (var kv in classMap) full[kv.Key] = kv.Value;
                        return new ResolvedCall
                        {
                            Entry = e,
                            OwnerView = t,
                            MethodArgs = gps.Select(gp => bind[gp]).ToList(),
                            ReturnType = c.SubstView(e.Code.ReturnType, full),
                            ParamsExpanded = paramsExpanded
                        };
                    }
                }
                return null;
            }

            // tip cikarimi: bildirilen parametre tipi ile gercek arguman tipini birlestirir.
            // Method tip parametresi gorulen yere baglanir; Apply'lar ayni template + eleman eslesmesi ister.
            bool Unify(Primitive decl, Primitive actual, Dictionary<Primitive, Primitive> classMap, Dictionary<Primitive, Primitive> bind, List<Primitive> gps, int pass = 0)
            {
                if (classMap != null && classMap.TryGetValue(decl, out var cm)) decl = cm;
                if (IsNullable(decl, out var nullableValue) &&
                    (actual == Primitive.Void || actual == nullableValue || (pass >= 1 && ImplicitNumeric(actual, nullableValue))))
                    return true;
                if (actual == Primitive.Void && !decl.IsGenericParameter &&
                    (decl.Type == PrimitiveType.Array || (decl.Type == PrimitiveType.Model && !decl.IsStruct)))
                    return true; // tipsiz null somut referans tipine ortuk donusur; null'dan T cikarilmaz
                if (decl.IsGenericParameter && gps.Contains(decl))
                {
                    if (bind.TryGetValue(decl, out var b)) return b == actual || IsAssignable(actual, b);
                    bind[decl] = actual;
                    return true;
                }
                if (IsAssignable(actual, decl)) return true;
                if (decl.GenericTemplate != null)
                {
                    // template govdesinde `this` acik template olarak gorunur (Task`1): kendi generic
                    // parametreleri tip argumanlari sayilir (Task`1 == Task<T> o kapsamda ayni tip).
                    var actualArgs = actual.GenericTemplate == decl.GenericTemplate ? actual.TypeArguments
                        : actual == decl.GenericTemplate ? actual.GenericParameters
                        : null;
                    if (actualArgs == null) return false;
                    for (int i = 0; i < decl.TypeArguments.Count; i++)
                        if (!Unify(decl.TypeArguments[i], actualArgs[i], classMap, bind, gps, pass)) return false;
                    return true;
                }
                if (decl.Type == PrimitiveType.Array && actual.Type == PrimitiveType.Array &&
                    decl.ArrayRank == actual.ArrayRank && decl.ElementType != null && actual.ElementType != null)
                    return Unify(decl.ElementType, actual.ElementType, classMap, bind, gps, pass);
                if (pass >= 1 && ImplicitNumeric(actual, decl)) return true;
                if (pass >= 2 && actual.IsGenericParameter && decl == Primitive.Object) return true;
                return pass >= 2 && decl == Primitive.Object && Boxable(actual); // boxing donusumu (Coerce kutular)
            }

            // C# ortuk sayisal donusumleri (ECMA 10.2.3) - enum'lar haric
            static bool ImplicitNumeric(Primitive a, Primitive p)
            {
                if (a.IsEnum || p.IsEnum) return false;
                bool To(params PrimitiveType[] ts) { foreach (var t in ts) if (p.Type == t) return true; return false; }
                switch (a.Type)
                {
                    case PrimitiveType.SByte: return To(PrimitiveType.Short, PrimitiveType.Int, PrimitiveType.Long, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Byte: return To(PrimitiveType.Short, PrimitiveType.UShort, PrimitiveType.Int, PrimitiveType.UInt, PrimitiveType.Long, PrimitiveType.ULong, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Short: return To(PrimitiveType.Int, PrimitiveType.Long, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.UShort: return To(PrimitiveType.Int, PrimitiveType.UInt, PrimitiveType.Long, PrimitiveType.ULong, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Int: return To(PrimitiveType.Long, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.UInt: return To(PrimitiveType.Long, PrimitiveType.ULong, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Long: return To(PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.ULong: return To(PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Char: return To(PrimitiveType.UShort, PrimitiveType.Int, PrimitiveType.UInt, PrimitiveType.Long, PrimitiveType.ULong, PrimitiveType.Float, PrimitiveType.Double);
                    case PrimitiveType.Float: return p.Type == PrimitiveType.Double;
                    default: return false;
                }
            }

            static bool ImplicitIntegralConstant(ArgInfo argument, Primitive target)
            {
                if (!argument.HasIntegralConstant || argument.Type.IsEnum || target.IsEnum) return false;
                decimal value = argument.IntegralConstant;
                if (argument.Type == Primitive.Int)
                {
                    switch (target.Type)
                    {
                        case PrimitiveType.SByte: return value >= sbyte.MinValue && value <= sbyte.MaxValue;
                        case PrimitiveType.Byte: return value >= byte.MinValue && value <= byte.MaxValue;
                        case PrimitiveType.Short: return value >= short.MinValue && value <= short.MaxValue;
                        case PrimitiveType.UShort: return value >= ushort.MinValue && value <= ushort.MaxValue;
                        case PrimitiveType.UInt: return value >= uint.MinValue && value <= uint.MaxValue;
                        case PrimitiveType.ULong: return value >= ulong.MinValue && value <= ulong.MaxValue;
                    }
                }
                return argument.Type == Primitive.Long && target == Primitive.ULong && value >= ulong.MinValue && value <= ulong.MaxValue;
            }

            void EmitDefaults(MethodEntry e, int provided, int line)
            {
                if (e.Params.Count > 0 && e.Params[e.Params.Count - 1].IsParams) return; // PackParams default'lari ve diziyi sirali uretti
                for (int i = provided; i < e.ParamTypes.Count; i++)
                {
                    if (e.Params[i].IsParams) continue; // PackParams bos dizi argumanini zaten uretir
                    var value = DefaultValue(e.Params[i].Default);
                    if (value == null)
                        throw Err(line, $"default deger literal olmali: {e.SimpleName} parametre {e.Params[i].Name}");
                    EmitDefault(e, value);
                }
            }

            void EmitDefault(MethodEntry entry, Expr value)
            {
                var callerClass = currentClass;
                var callerNs = ns;
                currentClass = entry.Code.Owner;
                ns = entry.Code.Owner.Name;
                try { EmitExpr(value); }
                finally { currentClass = callerClass; ns = callerNs; }
            }

            static Expr DefaultValue(Expr expression)
            {
                if (expression is ELit || expression is EField || expression is EName) return expression;
                if (expression is EUn { Op: "-", E: ELit numeric } &&
                    (numeric.Tag == "int" || numeric.Tag == "float"))
                    return new ELit { Line = numeric.Line, Tag = numeric.Tag, Value = "-" + numeric.Value };
                return null;
            }

            void PackParams(ResolvedCall rc, List<Op> argOps, List<ArgInfo> args, int line)
            {
                var parameters = rc.Entry.Params;
                if (parameters.Count == 0 || !parameters[parameters.Count - 1].IsParams || !rc.ParamsExpanded) return;
                int extensionOffset = parameters[0].IsThis ? 1 : 0;
                int firstExpanded = parameters.Count - 1 - extensionOffset;
                var saved = cur;
                cur = argOps;
                for (int i = args.Count; i < firstExpanded; i++)
                {
                    var defaultValue = DefaultValue(parameters[i + extensionOffset].Default);
                    if (defaultValue == null)
                        throw Err(line, $"params oncesi parametre default deger ister: {parameters[i + extensionOffset].Name}");
                    EmitDefault(rc.Entry, defaultValue);
                }
                cur = saved;
                int count = args.Count - firstExpanded;
                if (count < 0) count = 0;
                var classMap = rc.OwnerView != null && rc.OwnerView.GenericTemplate != null ? c.MapOf(rc.OwnerView) : null;
                var arrayType = c.SubstView(rc.Entry.ParamTypes[parameters.Count - 1], classMap);
                var elementType = arrayType.ElementType;
                var values = new int[count];
                for (int i = count - 1; i >= 0; i--)
                {
                    var info = args[firstExpanded + i];
                    if (info.IsMethodGroup || info.IsRef || info.IsOut || info.Type == null)
                        throw Err(line, "params argumani method group/ref/out olamaz");
                    values[i] = DeclareTemp(info.Type);
                    argOps.Add(new Op { Type = OpType.SetLocal, Slot = values[i] });
                }
                argOps.Add(new Op { Type = OpType.Push, Value = count });
                argOps.Add(new Op { Type = OpType.NewArray, PrimitiveRef = elementType });
                for (int i = 0; i < count; i++)
                {
                    argOps.Add(new Op { Type = OpType.Dup });
                    argOps.Add(new Op { Type = OpType.Push, Value = i });
                    argOps.Add(new Op { Type = OpType.GetLocal, Slot = values[i] });
                    argOps.Add(new Op { Type = OpType.SetIndex });
                }
            }

            void EmitInvoke(ResolvedCall rc)
            {
                var target = rc.Entry.Code;
                bool isVirt = target.IsVirtual || target.IsOverride; // iface methodlari ortuk virtual (DeclareMethods)
                var op = new Op { Type = isVirt ? OpType.CallVirtual : OpType.Call, Code = target };
                if (rc.OwnerView != null && rc.OwnerView.GenericTemplate != null)
                    op.TypeArguments.AddRange(rc.OwnerView.TypeArguments); // sinif argumanlari (Resolver sozlesmesi: once bunlar)
                op.TypeArguments.AddRange(rc.MethodArgs);
                ops.Add(op);
            }

            void EmitBaseInvoke(ResolvedCall rc)
            {
                var op = new Op { Type = OpType.Call, Code = rc.Entry.Code };
                if (rc.OwnerView != null && rc.OwnerView.GenericTemplate != null)
                    op.TypeArguments.AddRange(rc.OwnerView.TypeArguments);
                op.TypeArguments.AddRange(rc.MethodArgs);
                ops.Add(op);
            }

            // out var x: cozulen parametre tipiyle local'i bildir, placeholder AddrLocal'i patch'le
            void PatchOutVars(List<ArgInfo> infos, ResolvedCall rc)
            {
                for (int i = 0; i < infos.Count; i++)
                {
                    if (!infos[i].IsOutVar) continue;
                    var classMap = rc.OwnerView != null && rc.OwnerView.GenericTemplate != null ? c.MapOf(rc.OwnerView) : null;
                    var bind = new Dictionary<Primitive, Primitive>();
                    var gps = rc.Entry.Code.GenericParameters;
                    for (int g = 0; g < gps.Count; g++) bind[gps[g]] = rc.MethodArgs[g];
                    var pt = c.SubstView(c.SubstView(rc.Entry.ParamTypes[i], classMap), bind);
                    infos[i].PatchOp.Slot = DeclareLocal(infos[i].VarName, pt, infos[i].Line);
                }
            }

            Primitive EmitCall(ECall call)
            {
                // argumanlar tip tespiti icin ayri listeye emit edilir (alici once puslenmeli)
                var argOps = new List<Op>();
                var argInfos = EmitArgsTo(argOps, call.Args);
                var explicitArgs = call.TypeArgs?.Select(tn => c.ResolveType(tn, ns, call.Line)).ToList();
                string ArgsText() => string.Join(",", argInfos.Select(t => t.Type != null ? t.Type.Name :
                    t.IsMethodGroup ? (t.MgLambda != null ? "lambda" : "method group") : t.IsOutVar ? "out var" : "?"));
                bool isBaseCall = call.Target is EName { Name: "base", IsEscaped: false };

                if (call.Target == null) // bare cagri: delegate degisken/alan mi, degilse mevcut sinif zinciri
                {
                    if (localSlot.TryGetValue(call.Name, out var dSlot) && code.Locals[dSlot].IsDelegate)
                    {
                        ops.Add(new Op { Type = OpType.GetLocal, Slot = dSlot });
                        return EmitDelegateInvoke(code.Locals[dSlot], argOps, argInfos, call.Line);
                    }
                    int dArg = ArgSlot(call.Name);
                    if (dArg >= 0 && code.Arguments[dArg].Type.IsDelegate)
                    {
                        ops.Add(new Op { Type = OpType.GetArg, Slot = dArg });
                        return EmitDelegateInvoke(code.Arguments[dArg].Type, argOps, argInfos, call.Line);
                    }
                    var (df, dfOwner) = c.FindFieldView(currentClass, call.Name, false);
                    if (df != null && df.Type.IsDelegate)
                    {
                        EmitImplicitThis(call.Line);
                        AddFieldOp(OpType.GetField, df, dfOwner);
                        return EmitDelegateInvoke(df.Type, argOps, argInfos, call.Line);
                    }
                    var (dsf, dsfOwner) = c.FindFieldView(currentClass, call.Name, true);
                    if (dsf != null && dsf.Type.IsDelegate)
                    {
                        AddFieldOp(OpType.GetStatic, dsf, dsfOwner);
                        return EmitDelegateInvoke(dsf.Type, argOps, argInfos, call.Line);
                    }
                    var rc = ResolveMethod(currentClass, call.Name, argInfos, explicitArgs);
                    for (var enclosingName = currentClass.Name; rc == null && enclosingName.Contains('.');)
                    {
                        enclosingName = enclosingName.Substring(0, enclosingName.LastIndexOf('.'));
                        if (!c.ctx.TryGetPrimitive(enclosingName, out var enclosingType)) continue;
                        var candidate = ResolveMethod(enclosingType, call.Name, argInfos, explicitArgs);
                        if (candidate != null && candidate.Entry.Code.IsStatic) rc = candidate;
                    }
                    if (rc == null)
                    {
                        foreach (var importedType in c.staticUsings)
                        {
                            var candidate = ResolveMethod(importedType, call.Name, argInfos, explicitArgs);
                            if (candidate == null) continue;
                            if (!candidate.Entry.Code.IsStatic)
                                throw Err(call.Line, $"using static ile instance method cagrilamaz: {importedType.Name}.{call.Name}");
                            if (rc != null)
                                throw Err(call.Line, $"using static method cagrisi belirsiz: {call.Name}({ArgsText()})");
                            rc = candidate;
                        }
                    }
                    if (rc == null) throw Err(call.Line, $"bilinmeyen method: {call.Name}({ArgsText()})");
                    PatchOutVars(argInfos, rc);
                    PatchMethodGroups(argInfos, rc, argOps);
                    PackParams(rc, argOps, argInfos, call.Line);
                    if (!rc.Entry.Code.IsStatic)
                    {
                        EmitImplicitThis(call.Line);
                    }
                    ops.AddRange(argOps);
                    EmitDefaults(rc.Entry, argInfos.Count, call.Line);
                    EmitInvoke(rc);
                    return rc.ReturnType;
                }

                // Tip adi uzerinden static cagri: Demo.Math2.Yap(...) / Math2.Yap(...) / Box<int>.Say(...)
                ResolvedCall staticCall = null;
                Primitive staticType = null;
                if (TryResolveTypeName(call.Target, out staticType, allowValueShadow: true))
                    staticCall = ResolveMethod(staticType, call.Name, argInfos, explicitArgs);
                if (staticCall != null && staticCall.Entry.Code.IsStatic)
                {
                    var (sdf, sdfOwner) = c.FindFieldView(staticType, call.Name, true);
                    if (sdf != null && sdf.Type.IsDelegate)
                    {
                        AddFieldOp(OpType.GetStatic, sdf, sdfOwner);
                        return EmitDelegateInvoke(sdf.Type, argOps, argInfos, call.Line);
                    }
                    var rc = staticCall;
                    PatchOutVars(argInfos, rc);
                    PatchMethodGroups(argInfos, rc, argOps);
                    PackParams(rc, argOps, argInfos, call.Line);
                    ops.AddRange(argOps);
                    EmitDefaults(rc.Entry, argInfos.Count, call.Line);
                    EmitInvoke(rc);
                    return rc.ReturnType;
                }
                if (TryResolveTypeName(call.Target, out var unshadowedStaticType))
                {
                    if (staticCall == null)
                        throw Err(call.Line, $"bilinmeyen static method: {unshadowedStaticType.Name}.{call.Name}({ArgsText()})");
                    throw Err(call.Line, $"instance method tip uzerinden cagrilamaz: {unshadowedStaticType.Name}.{call.Name}");
                }

                // instance cagri: alici ifadesi (struct lvalue -> adres)
                var recvType = EmitReceiver(call.Target);
                if (recvType.IsDelegate && call.Name == "Invoke")
                {
                    if (explicitArgs != null && explicitArgs.Count > 0)
                        throw Err(call.Line, "delegate Invoke generic tip argumani alamaz");
                    return EmitDelegateInvoke(recvType, argOps, argInfos, call.Line);
                }
                ResolvedCall im = null;
                if (recvType.IsGenericParameter) // C#: kisitsiz T'de object uyeleri cagirilabilir (constrained.callvirt esdegeri)
                {
                    if (call.Name == "GetHashCode" || call.Name == "Equals" || call.Name == "ToString")
                    {
                        im = ResolveMethod(Primitive.Object, call.Name, argInfos, explicitArgs)
                            ?? throw Err(call.Line, $"object uyesi cozulemedi: {call.Name}");
                        ops.AddRange(argOps);
                        EmitInvoke(im); // CallVirtual; klon somut tipte transpiler deger-receiver hizli yolunu secer
                        return im.ReturnType;
                    }
                    im = ResolveExtension(recvType, call.Name, argInfos, explicitArgs);
                    if (im == null)
                        throw Err(call.Line, $"kisitsiz tip parametresinde yalniz object uyeleri: {call.Name}");
                }
                if (im == null && recvType.Type == PrimitiveType.Model)
                {
                    var (idf, idfOwner) = c.FindFieldView(recvType, call.Name, false);
                    if (idf != null && idf.Type.IsDelegate) // obj.alan(...) delegate cagrisi
                    {
                        AddFieldOp(OpType.GetField, idf, idfOwner);
                        return EmitDelegateInvoke(idf.Type, argOps, argInfos, call.Line);
                    }
                    im = ResolveMethod(recvType, call.Name, argInfos, explicitArgs);
                }
                if (im == null && recvType.Type != PrimitiveType.Pointer && recvType.Type != PrimitiveType.Array && recvType.Type != PrimitiveType.FixedArray)
                    im = ResolveMethod(recvType, call.Name, argInfos, explicitArgs);
                if (im == null && !recvType.IsStruct && recvType.Type != PrimitiveType.Pointer)
                    im = ResolveExtension(recvType, call.Name, argInfos, explicitArgs); // alici, ilk arguman olur (boxing dahil)
                if (im == null)
                    throw Err(call.Line, $"bilinmeyen method: {recvType.Name}.{call.Name}({ArgsText()})");
                if (im.IsExtension) // static cagri: [alici][arglar] zaten dogru sirada
                {
                    PatchMethodGroups(argInfos, im, argOps, 1);
                    PackParams(im, argOps, argInfos, call.Line);
                    ops.AddRange(argOps);
                    EmitDefaults(im.Entry, argInfos.Count + 1, call.Line);
                    EmitInvoke(im);
                    return im.ReturnType;
                }
                PatchOutVars(argInfos, im);
                PatchMethodGroups(argInfos, im, argOps);
                PackParams(im, argOps, argInfos, call.Line);
                if (im.Entry.Code.IsStatic) throw Err(call.Line, $"static method instance uzerinden cagrilamaz: {call.Name}");
                ops.AddRange(argOps);
                EmitDefaults(im.Entry, argInfos.Count, call.Line);
                if (isBaseCall) EmitBaseInvoke(im);
                else EmitInvoke(im);
                return im.ReturnType;
            }

            void EmitForeach(SForeach fe)
            {
                if (TryEmitIteratorForeach(fe)) return; // eski yield iterator'lari GetEnumerator yuzeyi tasimaz
                EmitEnumeratorForeach(fe);
            }

            // foreach (T x in Iter(args)) -> it = Iter_Create(args); while (Iter_MoveNext(it)) { x = it.current; body }
            bool TryEmitIteratorForeach(SForeach fe)
            {
                if (!(fe.Coll is ECall coll)) return false;
                var argOps = new List<Op>();
                var argTypes = new List<Primitive>();
                var saved = cur;
                cur = argOps;
                foreach (var arg in coll.Args) argTypes.Add(EmitExpr(arg));
                cur = saved;
                var mangled = Mangle(coll.Name, argTypes);

                (Primitive frame, Code create, Code moveNext, Primitive elem) info = default;
                bool found;
                List<Op> recvOps = null;
                if (coll.Target == null)
                {
                    found = FindIterator(currentClass, mangled, out info);
                    if (found && info.create.Arguments.Count > argTypes.Count) // instance iterator: this gerekli
                    {
                        RequireThis(fe.Line);
                        recvOps = new List<Op> { new Op { Type = OpType.GetArg, Slot = 0 } };
                    }
                }
                else if (TryResolveTypeName(coll.Target, out var staticType))
                    found = FindIterator(staticType, mangled, out info);
                else
                {
                    recvOps = new List<Op>();
                    cur = recvOps;
                    var recvType = EmitExpr(coll.Target);
                    cur = saved;
                    found = FindIterator(recvType, mangled, out info);
                }
                if (!found) return false;

                if (recvOps != null) ops.AddRange(recvOps);
                ops.AddRange(argOps);
                ops.Add(new Op { Type = OpType.Call, Code = info.create });
                int itSlot = DeclareTemp(info.frame);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = itSlot });

                int varSlot = DeclareLocal(fe.Name, fe.TypeName == "var" ? info.elem : c.ResolveType(fe.TypeName, ns, fe.Line), fe.Line);
                var lStart = new Label();
                var lEnd = new Label();
                ops.Add(new Op { Type = OpType.Label, Label = lStart });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = itSlot });
                ops.Add(new Op { Type = OpType.Call, Code = info.moveNext });
                ops.Add(new Op { Type = OpType.Brfalse, Label = lEnd });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = itSlot });
                ops.Add(new Op { Type = OpType.GetField, Field = info.frame.Fields[1] }); // [1] = current (IteratorLowering yerlesimi)
                ops.Add(new Op { Type = OpType.SetLocal, Slot = varSlot });
                breakTargets.Add(lEnd); continueTargets.Add(lStart); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count); // continue = sonraki MoveNext
                EmitScoped(fe.Body);
                breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                ops.Add(new Op { Type = OpType.Br, Label = lStart });
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                localSlot.Remove(fe.Name); // scope: dongu degiskeni disari sizmasin
                return true;
            }

            // C# foreach pattern: collection bir kez -> GetEnumerator(), sonra MoveNext()/Current.
            // ListEnumerator<T> struct'tur; bu yol steady-state heap tahsisi yapmaz.
            void EmitEnumeratorForeach(SForeach fe)
            {
                var collectionType = EmitExpr(fe.Coll);
                if (collectionType.Type == PrimitiveType.Array)
                {
                    EmitArrayForeach(fe, collectionType);
                    return;
                }
                if (collectionType.Type != PrimitiveType.Model)
                    throw Err(fe.Line, $"foreach icin GetEnumerator bulunamadi: {collectionType.Name}");
                int collectionSlot = DeclareTemp(collectionType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = collectionSlot });

                ops.Add(new Op { Type = collectionType.IsStruct ? OpType.AddrLocal : OpType.GetLocal, Slot = collectionSlot });
                var getEnumerator = ResolveMethod(collectionType, "GetEnumerator", new List<ArgInfo>())
                    ?? throw Err(fe.Line, $"foreach icin GetEnumerator bulunamadi: {collectionType.Name}");
                if (getEnumerator.Entry.Code.IsStatic) throw Err(fe.Line, $"foreach GetEnumerator instance method olmali: {collectionType.Name}");
                EmitInvoke(getEnumerator);
                var enumeratorType = getEnumerator.ReturnType;
                int enumeratorSlot = DeclareTemp(enumeratorType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = enumeratorSlot });

                var moveNext = ResolveMethod(enumeratorType, "MoveNext", new List<ArgInfo>())
                    ?? throw Err(fe.Line, $"foreach enumerator MoveNext eksik: {enumeratorType.Name}");
                var current = ResolveMethod(enumeratorType, "get_Current", new List<ArgInfo>())
                    ?? throw Err(fe.Line, $"foreach enumerator Current eksik: {enumeratorType.Name}");
                if (moveNext.Entry.Code.IsStatic || current.Entry.Code.IsStatic)
                    throw Err(fe.Line, $"foreach enumerator uyeleri instance method olmali: {enumeratorType.Name}");
                if (moveNext.ReturnType != Primitive.Bool)
                    throw Err(fe.Line, $"foreach MoveNext bool donmeli: {enumeratorType.Name}");

                var elementType = current.ReturnType;
                int varSlot = DeclareLocal(fe.Name, fe.TypeName == "var" ? elementType : c.ResolveType(fe.TypeName, ns, fe.Line), fe.Line);
                var lStart = new Label();
                var lEnd = new Label();
                ops.Add(new Op { Type = OpType.Label, Label = lStart });
                ops.Add(new Op { Type = enumeratorType.IsStruct ? OpType.AddrLocal : OpType.GetLocal, Slot = enumeratorSlot });
                EmitInvoke(moveNext);
                ops.Add(new Op { Type = OpType.Brfalse, Label = lEnd });
                ops.Add(new Op { Type = enumeratorType.IsStruct ? OpType.AddrLocal : OpType.GetLocal, Slot = enumeratorSlot });
                EmitInvoke(current);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = varSlot });
                breakTargets.Add(lEnd); continueTargets.Add(lStart); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count);
                EmitScoped(fe.Body);
                breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                ops.Add(new Op { Type = OpType.Br, Label = lStart });
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                localSlot.Remove(fe.Name);
            }

            void EmitArrayForeach(SForeach fe, Primitive arrayType)
            {
                if (arrayType.ArrayRank != 1)
                    throw Err(fe.Line, "cok boyutlu dizi foreach henuz desteklenmiyor");
                int collectionSlot = DeclareTemp(arrayType);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = collectionSlot });
                int indexSlot = DeclareTemp(Primitive.Int);
                ops.Add(new Op { Type = OpType.Push, Value = 0 });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = indexSlot });

                int varSlot = DeclareLocal(fe.Name, fe.TypeName == "var" ? arrayType.ElementType : c.ResolveType(fe.TypeName, ns, fe.Line), fe.Line);
                var lStart = new Label();
                var lIncrement = new Label();
                var lEnd = new Label();
                ops.Add(new Op { Type = OpType.Label, Label = lStart });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = indexSlot });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = collectionSlot });
                ops.Add(new Op { Type = OpType.ArrayLength });
                ops.Add(new Op { Type = OpType.Clt });
                ops.Add(new Op { Type = OpType.Brfalse, Label = lEnd });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = collectionSlot });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = indexSlot });
                ops.Add(new Op { Type = OpType.GetIndex, Slot = 1 });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = varSlot });
                breakTargets.Add(lEnd); continueTargets.Add(lIncrement); breakDepths.Add(tryRegions.Count); continueDepths.Add(tryRegions.Count);
                EmitScoped(fe.Body);
                breakTargets.RemoveAt(breakTargets.Count - 1); continueTargets.RemoveAt(continueTargets.Count - 1); breakDepths.RemoveAt(breakDepths.Count - 1); continueDepths.RemoveAt(continueDepths.Count - 1);
                ops.Add(new Op { Type = OpType.Label, Label = lIncrement });
                ops.Add(new Op { Type = OpType.GetLocal, Slot = indexSlot });
                ops.Add(new Op { Type = OpType.Push, Value = 1 });
                ops.Add(new Op { Type = OpType.Add });
                ops.Add(new Op { Type = OpType.SetLocal, Slot = indexSlot });
                ops.Add(new Op { Type = OpType.Br, Label = lStart });
                ops.Add(new Op { Type = OpType.Label, Label = lEnd });
                localSlot.Remove(fe.Name);
            }

            void EmitFixed(SFixed statement)
            {
                var pointerType = c.ResolveType(statement.TypeName, ns, statement.Line);
                if (pointerType.Type != PrimitiveType.Pointer)
                    throw Err(statement.Line, "fixed local pointer tipinde olmali");
                var sourceType = EmitExpr(statement.Init);
                if ((sourceType.Type != PrimitiveType.Array || sourceType.ArrayRank != 1) && sourceType.Type != PrimitiveType.FixedArray)
                    throw Err(statement.Line, "fixed initializer tek boyutlu dizi ya da fixed buffer olmali");
                if (sourceType.ElementType != pointerType.ElementType)
                    throw Err(statement.Line, $"fixed eleman tipi uyusmuyor: {sourceType.ElementType.Name} -> {pointerType.ElementType.Name}");
                ops.Add(new Op { Type = OpType.ArrayDataAddr });
                int slot = DeclareLocal(statement.Name, pointerType, statement.Line);
                ops.Add(new Op { Type = OpType.SetLocal, Slot = slot });
                EmitScoped(statement.Body);
                localSlot.Remove(statement.Name);
            }

            bool FindIterator(Primitive cls, string mangled, out (Primitive frame, Code create, Code moveNext, Primitive elem) info)
            {
                for (var t = cls; t != null; t = t.Parent)
                    if (c.iterators.TryGetValue(t.Name + "$" + mangled, out info))
                        return true;
                info = default;
                return false;
            }

            // EName/EField zinciri bir TIP adina mi cozuluyor? (once local/param/alan degil olmali)
            bool TryResolveTypeName(Expr e, out Primitive type, bool allowValueShadow = false)
            {
                type = null;
                string name = FlattenName(e);
                if (name == null) return false;
                if (name.IndexOf('<') >= 0) // generic tip adi: Box<int>.Uye - degerle golgelenemez
                {
                    try { type = c.ResolveType(name, ns, 0); return type.Type == PrimitiveType.Model; }
                    catch (CsError) { return false; }
                }
                if (!allowValueShadow && (localSlot.ContainsKey(name) || ArgSlot(name) >= 0 || FindField(currentClass, name) != null || FindStaticField(currentClass, name) != null))
                    return false; // deger golgeliyor
                try { type = c.ResolveType(name, ns, 0); } // tek cozum yolu (declared/ns/usings/ctx)
                catch (CsError) { type = null; return false; }
                return type.Type == PrimitiveType.Model || type.IsEnum
                    || IsScalar(type) || type.Type == PrimitiveType.Bool; // primitive static yuzey: double.TryParse vb.
            }

            static string FlattenName(Expr e) =>
                e is EName n ? n.Name :
                e is EField f && FlattenName(f.Obj) is string inner ? inner + "." + f.Name : null;
        }
    }
}
