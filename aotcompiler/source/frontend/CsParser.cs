using System.Collections.Generic;

namespace DigitoyEngine.Frontend
{
    // MiniCs parser: recursive descent, oncelik merdiveni || > && > ==/!= > karsilastirma > +- > */% > unary > postfix.
    // v1 kapsami: namespace, class/struct (tek base), alan, method (static/virtual/override),
    // if/while/return/var-decl/atama/ifade-stmt, new T(), obj.alan, obj.Metod(...), bare cagri.
    public class CsParser
    {
        readonly List<Tok> toks;
        readonly string file;
        int p;

        CsParser(List<Tok> toks, string file) { this.toks = toks; this.file = file; }

        public static List<CsClass> Parse(string source, string fileName = "")
            => Parse(source, fileName, out _);

        public static List<CsClass> Parse(string source, string fileName, out List<string> usings)
            => Parse(source, fileName, out usings, out _);

        public static List<CsClass> Parse(string source, string fileName, out List<string> usings, out List<string> staticUsings)
        {
            var ps = new CsParser(CsLexer.Tokenize(source, fileName), fileName);
            return ps.ParseUnit(out usings, out staticUsings);
        }

        Tok Peek() => toks[p];
        Tok Next() => toks[p++];
        bool IsOp(string v) => toks[p].Kind == "op" && toks[p].Value == v;
        bool IsId(string v) => toks[p].Kind == "id" && toks[p].Value == v;
        int Line => (toks[p].Line << 10) | (toks[p].Col & 1023); // PAKETLI konum (eski derleyici sozlesmesi)

        void Expect(string op)
        {
            if (!IsOp(op)) throw new CsError(file, Line, $"'{op}' bekleniyordu, '{Peek().Value}' bulundu");
            Next();
        }

        string ExpectId()
        {
            if (Peek().Kind != "id") throw new CsError(file, Line, $"tanimlayici bekleniyordu, '{Peek().Value}' bulundu");
            return Next().Value;
        }

        // TODO: Attribute metadata'yi AST/reflection'a tasiyip runtime davranisina bagla.
        // Simdilik parser kaynak uyumlulugu icin [Attr] / [Attr(args)] listelerini yutar.
        void SkipAttributes()
        {
            while (IsOp("["))
            {
                int depth = 0;
                do
                {
                    if (IsOp("[")) depth++;
                    else if (IsOp("]")) depth--;
                    Next();
                } while (depth > 0 && Peek().Kind != "eof");
                if (depth != 0) throw new CsError(file, Line, "kapanmayan attribute listesi");
            }
        }

        List<CsClass> ParseUnit(out List<string> usings, out List<string> staticUsings)
        {
            var classes = new List<CsClass>();
            usings = new List<string>();
            staticUsings = new List<string>();
            string ns = "";
            var nsStack = new List<string>();
            while (Peek().Kind != "eof")
            {
                SkipAttributes();
                if (IsId("using")) // dosya kapsamli sayilir (C#'in namespace-ici scope inceligi es gecilir)
                {
                    Next();
                    bool isStatic = IsId("static");
                    if (isStatic) Next();
                    var u = ExpectId();
                    string alias = null;
                    if (IsOp("="))
                    {
                        if (isStatic) throw new CsError(file, Line, "using static alias desteklenmiyor");
                        alias = u;
                        Next();
                        u = ExpectId();
                    }
                    while (IsOp(".")) { Next(); u += "." + ExpectId(); }
                    Expect(";");
                    if (isStatic) staticUsings.Add(u);
                    else usings.Add(alias != null ? alias + "=" + u : u);
                    continue;
                }
                if (IsId("namespace"))
                {
                    Next();
                    var name = ExpectId();
                    while (IsOp(".")) { Next(); name += "." + ExpectId(); }
                    Expect("{");
                    nsStack.Add(ns);
                    ns = ns.Length > 0 ? ns + "." + name : name;
                    continue;
                }
                if (IsOp("}") && nsStack.Count > 0)
                {
                    Next();
                    ns = nsStack[nsStack.Count - 1];
                    nsStack.RemoveAt(nsStack.Count - 1);
                    continue;
                }
                classes.Add(ParseClass(ns));
            }
            return classes;
        }

        CsClass ParseClass(string ns, bool modifiersRead = false, bool initialStatic = false, bool initialPartial = false)
        {
            int declarationLine = Line;
            bool isPartial = initialPartial;
            while (!modifiersRead && (IsId("public") || IsId("internal") || IsId("static") || IsId("partial") || IsId("unsafe") || IsId("abstract"))) // unsafe/abstract C targette syntax-only
            {
                if (IsId("partial")) isPartial = true;
                Next();
            }
            bool isStruct = false, isInterface = false;
            if (IsId("delegate")) // delegate R Ad(T a, U b); / delegate R Ad<T1,T2>(...)
            {
                Next();
                var sig = new CsMethod { ReturnTypeName = ParseTypeName() };
                var dn = ExpectId();
                var dcls = new CsClass { Namespace = ns, Name = dn, IsDelegate = true, DelegateSig = sig, SourceFile = file, Line = declarationLine };
                if (IsOp("<"))
                {
                    Next();
                    dcls.GenericParams.Add(ExpectId());
                    while (IsOp(",")) { Next(); dcls.GenericParams.Add(ExpectId()); }
                    Expect(">");
                }
                Expect("(");
                while (!IsOp(")"))
                {
                    sig.Params.Add(new CsParam { TypeName = ParseTypeName(), Name = ExpectId() });
                    if (IsOp(",")) Next();
                }
                Next();
                Expect(";");
                return dcls;
            }
            if (IsId("enum")) // enum Renk { A, B = 3, C }
            {
                Next();
                var en = new CsClass { Namespace = ns, Name = ExpectId(), IsEnum = true, SourceFile = file, Line = declarationLine };
                if (IsOp(":"))
                {
                    Next();
                    var underlyingType = ParseTypeName();
                    if (underlyingType != "int")
                        throw new CsError(file, Line, $"enum underlying type henuz yalniz int olabilir: {underlyingType}");
                }
                Expect("{");
                while (!IsOp("}"))
                {
                    var m = new CsEnumMember { Line = Line, Name = ExpectId() };
                    if (IsOp("=")) { Next(); m.Value = ParseExpr(); }
                    en.EnumMembers.Add(m);
                    if (IsOp(",")) Next();
                }
                Next();
                return en;
            }
            if (IsId("class")) { Next(); }
            else if (IsId("struct")) { isStruct = true; Next(); }
            else if (IsId("interface")) { isInterface = true; Next(); }
            else throw new CsError(file, Line, "class/struct/interface bekleniyordu");

            var cls = new CsClass { Namespace = ns, Name = ExpectId(), IsStruct = isStruct, IsInterface = isInterface, IsPartial = isPartial, SourceFile = file, Line = declarationLine };
            if (IsOp("<")) // class Box<T, U> / interface IDictionary<K, V>
            {
                Next();
                cls.GenericParams.Add(ExpectId());
                while (IsOp(",")) { Next(); cls.GenericParams.Add(ExpectId()); }
                Expect(">");
            }
            ParseGenericConstraints(cls.GenericConstraints);
            if (IsOp(":"))
            {
                Next();
                do
                {
                    cls.BaseNames.Add(ParseTypeName()); // dotted + generic (IDictionary<K,V>) adlar
                } while (IsOp(",") && Next() != null);
            }
            Expect("{");
            while (!IsOp("}"))
                ParseMember(cls);
            Next();
            return cls;
        }

        void ParseMember(CsClass cls)
        {
            SkipAttributes();
            bool isStatic = false, isPartial = false, isVirtual = false, isOverride = false, isExtern = false, isConst = false, isReadonly = false;
            while (Peek().Kind == "id")
            {
                var v = Peek().Value;
                if (v == "public" || v == "private" || v == "protected" || v == "internal") { Next(); continue; }
                if (v == "new") { Next(); continue; }
                if (v == "unsafe") { Next(); continue; }
                if (v == "abstract") { Next(); continue; }
                if (v == "partial") { isPartial = true; Next(); continue; }
                if (v == "static") { isStatic = true; Next(); continue; }
                if (v == "virtual") { isVirtual = true; Next(); continue; }
                if (v == "override") { isOverride = true; Next(); continue; }
                if (v == "extern") { isExtern = true; Next(); continue; }
                if (v == "const") { isConst = true; Next(); continue; }
                if (v == "readonly") { isReadonly = true; Next(); continue; }
                break;
            }
            int line = Line;
            if (IsId("class") || IsId("struct") || IsId("interface") || IsId("enum") || IsId("delegate"))
            {
                if (isExtern || isConst || isReadonly || isVirtual || isOverride)
                    throw new CsError(file, line, "nested type icin gecersiz modifier");
                cls.NestedTypes.Add(ParseClass(cls.FullName, true, isStatic, isPartial));
                return;
            }
            // finalizer: ~SinifAdi() { ... } (modifier'siz, parametresiz - C# kurali)
            if (IsOp("~"))
            {
                if (isStatic || isVirtual || isOverride || isExtern || isConst || isReadonly)
                    throw new CsError(file, line, "finalizer modifier alamaz (C#)");
                if (cls.IsStruct || cls.IsInterface) throw new CsError(file, line, "finalizer yalniz class'ta (C#)");
                Next();
                var fname = ExpectId();
                if (fname != cls.Name) throw new CsError(file, line, $"finalizer adi sinif adiyla ayni olmali: ~{cls.Name}()");
                Expect("("); Expect(")");
                if (cls.Methods.Exists(mm => mm.IsFinalizer)) throw new CsError(file, line, "birden fazla finalizer");
                var fin = new CsMethod { Name = "finalize", ReturnTypeName = "void", IsFinalizer = true, Line = line };
                Expect("{");
                while (!IsOp("}"))
                    fin.Body.Add(ParseStmt());
                Next();
                cls.Methods.Add(fin);
                return;
            }
            // fixed buffer alani: fixed float m[16]; (gomulu dizi, GC'siz - Mat4 gibi hizli yapilar icin)
            if (IsId("fixed"))
            {
                Next();
                var ft = ParseTypeName();
                var fn = ExpectId();
                Expect("[");
                if (Peek().Kind != "num") throw new CsError(file, Line, "fixed boyutu sabit sayi olmali");
                int count = int.Parse(Next().Value);
                Expect("]");
                Expect(";");
                cls.Fields.Add(new CsField { TypeName = ft, Name = fn, IsStatic = isStatic, FixedCount = count, Line = line });
                return;
            }
            // ctor/cctor: uye adi == sinif adi ve hemen '(' geliyor
            if (Peek().Kind == "id" && Peek().Value == cls.Name && toks[p + 1].Kind == "op" && toks[p + 1].Value == "(")
            {
                Next();
                var ctor = new CsMethod { Name = isStatic ? "cctor" : "ctor", ReturnTypeName = "void", IsStatic = isStatic, IsCtor = !isStatic, IsCctor = isStatic, Line = line };
                ParseParams(ctor.Params);
                if (isStatic && ctor.Params.Count > 0) throw new CsError(file, line, "static ctor parametre alamaz");
                if (IsOp(":")) // ctor zinciri: ": base(args)" / ": this(args)"
                {
                    if (isStatic) throw new CsError(file, Line, "static ctor zincirlenemez");
                    Next();
                    if (IsId("base")) ctor.ChainToThis = false;
                    else if (IsId("this")) ctor.ChainToThis = true;
                    else throw new CsError(file, Line, "base ya da this bekleniyordu");
                    Next();
                    ctor.ChainArgs = new List<Expr>();
                    ParseArgs(ctor.ChainArgs);
                }
                if (IsOp("=>")) { ctor.Body = ParseArrowBody(true); cls.Methods.Add(ctor); return; }
                Expect("{");
                while (!IsOp("}"))
                    ctor.Body.Add(ParseStmt());
                Next();
                cls.Methods.Add(ctor);
                return;
            }
            var typeName = ParseTypeName();
            // operator overload: static T operator +(T a, T b)
            if (IsId("operator"))
            {
                Next();
                if (Peek().Kind != "op") throw new CsError(file, Line, "operator sembolu bekleniyordu");
                var sym = Next().Value;
                var opName = OperatorMethodName(sym) ?? throw new CsError(file, line, $"desteklenmeyen operator: {sym}");
                if (!isStatic) throw new CsError(file, line, "operator static olmali");
                var om = new CsMethod { Name = opName, ReturnTypeName = typeName, IsStatic = true, IsExtern = isExtern, Line = line };
                ParseParams(om.Params);
                if (om.Params.Count != 2) throw new CsError(file, line, "v1: yalniz ikili (binary) operator overload");
                if (isExtern) { Expect(";"); cls.Methods.Add(om); return; } // govde corelib.c'de
                if (IsOp("=>")) { om.Body = ParseArrowBody(false); cls.Methods.Add(om); return; }
                Expect("{");
                while (!IsOp("}"))
                    om.Body.Add(ParseStmt());
                Next();
                cls.Methods.Add(om);
                return;
            }
            string name;
            string explicitInterface = null;
            CsParam indexParam = null;
            if (IsId("this")) // indexer: V this[K key] { get ... set ... }
            {
                Next();
                Expect("[");
                indexParam = new CsParam { TypeName = ParseTypeName(), Name = ExpectId() };
                Expect("]");
                name = "Item"; // C# indexer'in metadata adi
            }
            else
            {
                name = ExpectId();
                var parts = new List<string> { name };
                while (true)
                {
                    if (IsOp("<") && IsTypeArgsAhead(p) && PeekAfterTypeArgs(p) == ".")
                    {
                        Next();
                        string applied = "<" + ParseTypeName();
                        while (IsOp(",")) { Next(); applied += "," + ParseTypeName(); }
                        Expect(">");
                        parts[parts.Count - 1] += applied + ">";
                    }
                    if (!IsOp(".")) break;
                    Next();
                    parts.Add(ExpectId());
                }
                if (parts.Count > 1)
                {
                    name = parts[parts.Count - 1];
                    parts.RemoveAt(parts.Count - 1);
                    explicitInterface = string.Join(".", parts);
                }
            }
            if (indexParam == null && IsOp("=>")) // expression-bodied property: T Ad => expr;  (salt get)
            {
                var eprop = new CsProperty { TypeName = typeName, Name = name, ExplicitInterface = explicitInterface, IsStatic = isStatic, IsVirtual = isVirtual, IsOverride = isOverride, Line = line, HasGet = true };
                eprop.GetBody = ParseArrowBody(false);
                cls.Properties.Add(eprop);
                return;
            }
            if (IsOp("{")) // property: T Ad { get ... set ... } [= init;]
            {
                var prop = new CsProperty { TypeName = typeName, Name = name, ExplicitInterface = explicitInterface, IndexParam = indexParam, IsStatic = isStatic, IsVirtual = isVirtual, IsOverride = isOverride, IsExtern = isExtern, Line = line };
                Next();
                while (!IsOp("}"))
                {
                    while (IsId("public") || IsId("private") || IsId("protected") || IsId("internal")) Next();
                    bool isGet = IsId("get"), isSet = IsId("set");
                    if (!isGet && !isSet) throw new CsError(file, Line, "get ya da set bekleniyordu");
                    Next();
                    List<Stmt> body = null;
                    if (IsOp(";")) Next(); // auto/extern accessor
                    else if (IsOp("=>")) // expression-bodied accessor: get => e; / set => stmt;
                    {
                        if (isExtern) throw new CsError(file, Line, "extern accessor govde alamaz");
                        body = ParseArrowBody(isSet);
                    }
                    else
                    {
                        if (isExtern) throw new CsError(file, Line, "extern accessor govde alamaz");
                        Expect("{");
                        body = new List<Stmt>();
                        while (!IsOp("}")) body.Add(ParseStmt());
                        Next();
                    }
                    if (isGet)
                    {
                        if (prop.HasGet) throw new CsError(file, line, $"get zaten tanimli: {name}");
                        prop.HasGet = true; prop.GetBody = body;
                    }
                    else
                    {
                        if (prop.HasSet) throw new CsError(file, line, $"set zaten tanimli: {name}");
                        prop.HasSet = true; prop.SetBody = body;
                    }
                }
                Next();
                if (IsOp("=")) { Next(); prop.Init = ParseExpr(); Expect(";"); } // { get; set; } = deger;
                cls.Properties.Add(prop);
                return;
            }
            if (IsOp("<") && IsTypeArgsAhead(p) && PeekAfterTypeArgs(p) == "(") // generic method: T Max<T>(...)
            {
                var m = new CsMethod { Name = name, ReturnTypeName = typeName, IsStatic = isStatic, IsVirtual = isVirtual, IsOverride = isOverride, Line = line };
                if (isVirtual || isOverride) throw new CsError(file, line, "generic method sanal olamaz (monomorphization acik uclu sanal dispatch'i kapatamaz - AOT siniri)");
                if (cls.IsInterface) throw new CsError(file, line, "interface'te generic method desteklenmiyor");
                Next();
                m.GenericParams.Add(ExpectId());
                while (IsOp(",")) { Next(); m.GenericParams.Add(ExpectId()); }
                Expect(">");
                ParseParams(m.Params);
                ParseGenericConstraints(m.GenericConstraints);
                if (IsOp("=>")) { m.Body = ParseArrowBody(typeName == "void"); cls.Methods.Add(m); return; }
                Expect("{");
                while (!IsOp("}"))
                    m.Body.Add(ParseStmt());
                Next();
                cls.Methods.Add(m);
                return;
            }
            if (IsOp("("))
            {
                var m = new CsMethod { Name = name, ExplicitInterface = explicitInterface, ReturnTypeName = typeName, IsStatic = isStatic, IsVirtual = isVirtual, IsOverride = isOverride, IsExtern = isExtern, Line = line };
                ParseParams(m.Params);
                if (cls.IsInterface) // iface uyesi: govdesiz imza "T Ad(params);"
                {
                    if (isStatic || isVirtual || isOverride) throw new CsError(file, line, "interface methodu modifier alamaz");
                    Expect(";");
                    cls.Methods.Add(m);
                    return;
                }
                if (isExtern) { Expect(";"); cls.Methods.Add(m); return; } // govde corelib.c'de + VM intrinsics'te
                if (IsOp("=>")) { m.Body = ParseArrowBody(typeName == "void"); cls.Methods.Add(m); return; }
                Expect("{");
                while (!IsOp("}"))
                    m.Body.Add(ParseStmt());
                Next();
                cls.Methods.Add(m);
            }
            else
            {
                if (cls.IsInterface) throw new CsError(file, line, "interface alan iceremez");
                ParseFieldDeclarators(cls, typeName, name, isStatic, isConst, isReadonly, line);
                Expect(";");
            }
        }

        // Syntax-only for now. Constraint semantics remain a deliberate TODO on the AST.
        void ParseGenericConstraints(List<CsGenericConstraint> into)
        {
            while (IsId("where"))
            {
                Next();
                var constraint = new CsGenericConstraint { Parameter = ExpectId() };
                Expect(":");
                while (true)
                {
                    if (IsId("class") || IsId("struct")) constraint.Bounds.Add(Next().Value);
                    else if (IsId("new"))
                    {
                        Next(); Expect("("); Expect(")");
                        constraint.Bounds.Add("new()");
                    }
                    else constraint.Bounds.Add(ParseTypeName());
                    if (!IsOp(",")) break;
                    Next();
                }
                into.Add(constraint);
            }
        }

        void ParseFieldDeclarators(CsClass cls, string typeName, string firstName, bool isStatic, bool isConst, bool isReadonly, int line)
        {
            string name = firstName;
            while (true)
            {
                var field = new CsField { TypeName = typeName, Name = name, IsStatic = isStatic, IsConst = isConst, IsReadonly = isReadonly, Line = line };
                if (IsOp("="))
                {
                    Next();
                    field.Init = IsArrayTypeName(typeName) && IsOp("{")
                        ? ParseImplicitArrayInitializer(typeName, line)
                        : ParseExpr();
                }
                if (isConst && field.Init == null) throw new CsError(file, line, $"const alan deger ister: {name}");
                cls.Fields.Add(field);
                if (!IsOp(",")) return;
                Next();
                name = ExpectId();
            }
        }

        // "(" [ref|out] Tip ad [= literal] {"," ...} ")"
        void ParseParams(List<CsParam> into)
        {
            Expect("(");
            while (!IsOp(")"))
            {
                var prm = new CsParam();
                if (IsId("params")) { prm.IsParams = true; Next(); }
                if (IsId("this") && into.Count == 0) { prm.IsThis = true; Next(); } // extension method alicisi
                if (IsId("ref")) { prm.IsRef = true; Next(); }
                else if (IsId("out")) { prm.IsOut = true; Next(); }
                prm.TypeName = ParseTypeName();
                prm.Name = ExpectId();
                if (IsOp("="))
                {
                    Next();
                    prm.Default = ParseExpr(); // derleyici literal olmasini dogrular
                    if (prm.IsRef || prm.IsOut) throw new CsError(file, Line, "ref/out parametre default deger alamaz");
                }
                if (prm.IsParams && (prm.IsRef || prm.IsOut || !prm.TypeName.EndsWith("[]")))
                    throw new CsError(file, Line, "params son parametre olarak ref/out olmayan dizi tipi ister");
                if (prm.IsParams && !IsOp(")"))
                    throw new CsError(file, Line, "params methodun son parametresi olmali");
                into.Add(prm);
                if (IsOp(",")) Next();
            }
            Next();
        }

        public static string OperatorMethodName(string sym) => sym switch
        {
            "+" => "op_add",
            "-" => "op_sub",
            "*" => "op_mul",
            "/" => "op_div",
            "%" => "op_mod",
            "==" => "op_eq",
            "!=" => "op_ne",
            "<" => "op_lt",
            ">" => "op_gt",
            "<=" => "op_le",
            ">=" => "op_ge",
            _ => null
        };

        string ParseTypeName(bool allowNullable = true)
        {
            var t = ExpectId();
            while (IsOp(".")) { Next(); t += "." + ExpectId(); }
            if (IsOp("<") && IsTypeArgsAhead(p)) // generic uygulama: Box<int>, Pair<int, Box<float>>
            {
                Next();
                t += "<" + ParseTypeName();
                while (IsOp(",")) { Next(); t += "," + ParseTypeName(); }
                Expect(">");
                t += ">";
            }
            if (allowNullable && IsOp("?"))
            {
                Next();
                t = "System.Nullable<" + t + ">";
            }
            while (IsOp("*"))
            {
                Next();
                t += "*";
            }
            // dizi tipi: [] ya da [,] (new T[n] boyut ifadesiyle karismaz)
            if (IsOp("[") && (toks[p + 1].Kind == "op" && toks[p + 1].Value == "]" || toks[p + 1].Kind == "op" && toks[p + 1].Value == ","))
            {
                Next();
                int commas = 0;
                while (IsOp(",")) { commas++; Next(); }
                Expect("]");
                t += "[" + new string(',', commas) + "]";
            }
            return t;
        }

        static bool IsArrayTypeName(string name) => name.EndsWith("]") && name.LastIndexOf('[') >= 0;

        // q '<' uzerinde: dengeli tip-arguman listesi mi? (a < b gibi karsilastirmalardan ayirt eder)
        bool IsTypeArgsAhead(int q) => ScanTypeArgs(ref q);

        // '<' konumundan tarar; tip-arguman listesiyse kapatan '>' SONRASINA ilerletip true doner
        bool ScanTypeArgs(ref int q)
        {
            int s = q;
            if (!(toks[q].Kind == "op" && toks[q].Value == "<")) return false;
            q++;
            while (true)
            {
                if (!ScanTypeRef(ref q)) { q = s; return false; }
                if (toks[q].Kind == "op" && toks[q].Value == ",") { q++; continue; }
                if (toks[q].Kind == "op" && toks[q].Value == ">") { q++; return true; }
                q = s; return false;
            }
        }

        bool ScanTypeRef(ref int q)
        {
            if (toks[q].Kind != "id") return false;
            q++;
            while (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "." && toks[q + 1].Kind == "id") q += 2;
            if (toks[q].Kind == "op" && toks[q].Value == "<" && !ScanTypeArgs(ref q)) return false;
            if (toks[q].Kind == "op" && toks[q].Value == "?") q++;
            while (toks[q].Kind == "op" && toks[q].Value == "*") q++;
            if (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "[")
            {
                q++;
                while (toks[q].Kind == "op" && toks[q].Value == ",") q++;
                if (toks[q].Kind != "op" || toks[q].Value != "]") return false;
                q++;
            }
            return true;
        }

        // '<' konumundaki tip-arguman listesinin kapanisindan SONRAKI token degeri (yoksa null)
        string PeekAfterTypeArgs(int q) => ScanTypeArgs(ref q) ? (toks[q].Kind == "op" ? toks[q].Value : null) : null;

        List<Stmt> ParseBlockOrSingle()
        {
            var r = new List<Stmt>();
            if (IsOp("{"))
            {
                Next();
                while (!IsOp("}")) r.Add(ParseStmt());
                Next();
            }
            else r.Add(ParseStmt());
            return r;
        }

        Stmt ParseStmt()
        {
            int line = Line;
            if (IsOp("{"))
            {
                Next();
                var block = new SBlock { Line = line };
                while (!IsOp("}")) block.Body.Add(ParseStmt());
                Next();
                return block;
            }
            if (IsId("unchecked"))
            {
                Next();
                if (!IsOp("{")) throw new CsError(file, Line, "unchecked statement icin '{' bekleniyordu");
                var uncheckedBlock = new SUnchecked { Line = line };
                Next();
                while (!IsOp("}")) uncheckedBlock.Body.Add(ParseStmt());
                Next();
                return uncheckedBlock;
            }
            if (IsId("fixed"))
            {
                Next(); Expect("(");
                var statement = new SFixed { Line = line, TypeName = ParseTypeName(), Name = ExpectId() };
                Expect("=");
                statement.Init = ParseExpr();
                Expect(")");
                statement.Body = ParseBlockOrSingle();
                return statement;
            }
            if (IsId("lock"))
            {
                Next(); Expect("(");
                var statement = new SLock { Line = line, Target = ParseExpr() };
                Expect(")");
                statement.Body = ParseBlockOrSingle();
                return statement;
            }
            if (IsId("if"))
            {
                Next(); Expect("(");
                var s = new SIf { Line = line, Cond = ParseExpr() };
                Expect(")");
                s.Then = ParseBlockOrSingle();
                if (IsId("else")) { Next(); s.Else = ParseBlockOrSingle(); }
                return s;
            }
            if (IsId("while"))
            {
                Next(); Expect("(");
                var s = new SWhile { Line = line, Cond = ParseExpr() };
                Expect(")");
                s.Body = ParseBlockOrSingle();
                return s;
            }
            if (IsId("do"))
            {
                Next();
                var s = new SDoWhile { Line = line };
                s.Body = ParseBlockOrSingle();
                if (!IsId("while")) throw new CsError(file, Line, "do sonrasi while bekleniyordu");
                Next(); Expect("(");
                s.Cond = ParseExpr();
                Expect(")"); Expect(";");
                return s;
            }
            if (IsId("return"))
            {
                Next();
                var s = new SReturn { Line = line };
                if (!IsOp(";")) s.E = ParseExpr();
                Expect(";");
                return s;
            }
            if (IsId("yield"))
            {
                Next();
                if (IsId("break")) { Next(); Expect(";"); return new SYieldBreak { Line = line }; }
                if (!IsId("return")) throw new CsError(file, line, "yield sonrasi return/break bekleniyordu");
                Next();
                var s = new SYield { Line = line, E = ParseExpr() };
                Expect(";");
                return s;
            }
            if (IsId("foreach"))
            {
                Next(); Expect("(");
                var tn = ParseTypeName();
                var vn = ExpectId();
                if (!IsId("in")) throw new CsError(file, Line, "'in' bekleniyordu");
                Next();
                var coll = ParseExpr();
                Expect(")");
                var s = new SForeach { Line = line, TypeName = tn, Name = vn, Coll = coll };
                s.Body = ParseBlockOrSingle();
                return s;
            }
            if (IsId("for"))
            {
                Next(); Expect("(");
                var s = new SFor { Line = line };
                if (!IsOp(";")) s.Init = ParseForPart();
                Expect(";");
                if (!IsOp(";")) s.Cond = ParseExpr();
                Expect(";");
                if (!IsOp(")")) s.Incr = ParseForPart();
                Expect(")");
                s.Body = ParseBlockOrSingle();
                return s;
            }
            if (IsId("switch"))
            {
                Next(); Expect("(");
                var sw = new SSwitch { Line = line, E = ParseExpr() };
                Expect(")"); Expect("{");
                while (!IsOp("}"))
                {
                    var sec = new SwitchSection();
                    while (true) // ardisik etiketler ayni section'a (case 1: case 2: ...)
                    {
                        if (IsId("case")) { Next(); sec.Labels.Add(ParseExpr()); Expect(":"); }
                        else if (IsId("default")) { Next(); Expect(":"); sec.HasDefault = true; }
                        else break;
                    }
                    if (sec.Labels.Count == 0 && !sec.HasDefault) throw new CsError(file, Line, "case ya da default bekleniyordu");
                    while (!IsId("case") && !IsId("default") && !IsOp("}"))
                        sec.Body.Add(ParseStmt());
                    sw.Sections.Add(sec);
                }
                Next();
                return sw;
            }
            if (IsId("break")) { Next(); Expect(";"); return new SBreak { Line = line }; }
            if (IsId("continue")) { Next(); Expect(";"); return new SContinue { Line = line }; }
            if (IsId("throw"))
            {
                Next();
                var s = new SThrow { Line = line };
                if (!IsOp(";")) s.E = ParseExpr(); // yoksa rethrow (`throw;`)
                Expect(";");
                return s;
            }
            if (IsId("try"))
            {
                Next();
                var s = new STry { Line = line };
                Expect("{");
                while (!IsOp("}")) s.Body.Add(ParseStmt());
                Next();
                while (IsId("catch"))
                {
                    int cl = Line;
                    Next();
                    var cc = new CsCatch { Line = cl };
                    if (IsOp("("))
                    {
                        Next();
                        cc.TypeName = ParseTypeName();
                        if (!IsOp(")")) cc.VarName = ExpectId();
                        Expect(")");
                    }
                    Expect("{");
                    while (!IsOp("}")) cc.Body.Add(ParseStmt());
                    Next();
                    s.Catches.Add(cc);
                    if (cc.TypeName == null) break; // tipsiz catch her seyi yakalar: sonrakiler erisilmez (C# kurali)
                }
                if (IsId("finally"))
                {
                    Next();
                    s.Finally = new List<Stmt>();
                    Expect("{");
                    while (!IsOp("}")) s.Finally.Add(ParseStmt());
                    Next();
                }
                if (s.Catches.Count == 0 && s.Finally == null) throw new CsError(file, line, "try en az bir catch ya da finally ister");
                return s;
            }
            if (IsId("const"))
            {
                Next();
                var typeName = ParseTypeName();
                var constant = ParseLocalDeclarators(typeName, line);
                if (constant is SVar single) single.IsConst = true;
                else if (constant is SVarGroup group)
                    foreach (var variable in group.Variables) variable.IsConst = true;
                bool HasInitializer(SVar variable) => variable.Init != null;
                if (constant is SVar one && !HasInitializer(one) ||
                    constant is SVarGroup many && many.Variables.Exists(variable => !HasInitializer(variable)))
                    throw new CsError(file, line, "const local deger ister");
                Expect(";");
                return constant;
            }
            // decl heuristigi: 'id id' (Tip isim) ya da 'id.id.. id'
            if (Peek().Kind == "id" && LooksLikeDecl())
            {
                var typeName = ParseTypeName();
                var s = ParseLocalDeclarators(typeName, line);
                Expect(";");
                return s;
            }
            var target = ParseExpr(false);
            if (IsOp("="))
                return ParseAssignment(target, line, true);
            var aop = TryAssignOp();
            if (aop != null)
            {
                var s = new SAssign { Line = line, Target = target, Value = ParseExpr(), Op = aop };
                Expect(";");
                return s;
            }
            Expect(";");
            return new SExpr { Line = line, E = target };
        }

        // bilesik atama operatoru: "+=" -> "+" (">>=" lexer'da '>' + '>=' bitisik cifti)
        string TryAssignOp()
        {
            foreach (var c in new[] { "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "<<=" })
                if (IsOp(c)) { Next(); return c.Substring(0, c.Length - 1); }
            if (IsOp(">") && toks[p + 1].Kind == "op" && toks[p + 1].Value == ">=" && toks[p + 1].Pos == toks[p].Pos + 1)
            { Next(); Next(); return ">>"; }
            return null;
        }

        // for init/incr: ';' tuketmeyen kisitli stmt (decl | atama | ifade)
        Stmt ParseSimpleStmt()
        {
            int line = Line;
            if (Peek().Kind == "id" && LooksLikeDecl())
            {
                var typeName = ParseTypeName();
                return ParseLocalDeclarators(typeName, line);
            }
            var target = ParseExpr(false);
            if (IsOp("="))
                return ParseAssignment(target, line, false);
            var aop = TryAssignOp();
            if (aop != null)
                return new SAssign { Line = line, Target = target, Value = ParseExpr(), Op = aop };
            return new SExpr { Line = line, E = target };
        }

        // for (a = 0, b = 0; ...; a++, b += 2): C# soldan saga statement-expression listesi.
        Stmt ParseForPart()
        {
            var first = ParseSimpleStmt();
            if (!IsOp(",")) return first;
            var block = new SBlock { Line = first.Line, Body = new List<Stmt> { first } };
            while (IsOp(","))
            {
                Next();
                block.Body.Add(ParseSimpleStmt());
            }
            return block;
        }

        Stmt ParseLocalDeclarators(string typeName, int line)
        {
            var group = new SVarGroup { Line = line };
            while (true)
            {
                var variable = new SVar { Line = line, TypeName = typeName, Name = ExpectId() };
                if (IsOp("="))
                {
                    Next();
                    variable.Init = IsArrayTypeName(typeName) && IsOp("{")
                        ? ParseImplicitArrayInitializer(typeName, line)
                        : ParseExpr();
                }
                if (typeName == "var" && variable.Init == null)
                    throw new CsError(file, line, "var bildirimi baslatici ister");
                group.Variables.Add(variable);
                if (!IsOp(",")) break;
                Next();
            }
            return group.Variables.Count == 1 ? group.Variables[0] : group;
        }

        ENewArray ParseImplicitArrayInitializer(string arrayTypeName, int line)
        {
            var array = new ENewArray
            {
                Line = line,
                ElemTypeName = arrayTypeName.Substring(0, arrayTypeName.LastIndexOf('['))
            };
            int rank = arrayTypeName.Length - arrayTypeName.LastIndexOf('[') - 1;
            return ParseArrayInitializer(array, rank, line);
        }

        ENewArray ParseArrayInitializer(ENewArray array, int rank, int line)
        {
            Expect("{");
            if (IsOp("{"))
            {
                if (rank != 2) throw new CsError(file, line, "ic ice dizi baslaticisi su an yalniz rank-2 rectangular dizi icin destekleniyor");
                array.Rows = new List<List<Expr>>();
                while (!IsOp("}"))
                {
                    Expect("{");
                    var row = new List<Expr>();
                    while (!IsOp("}"))
                    {
                        row.Add(ParseExpr());
                        if (IsOp(",")) Next();
                        else if (!IsOp("}")) throw new CsError(file, Line, "dizi satirinda ',' ya da '}' bekleniyordu");
                    }
                    Next();
                    array.Rows.Add(row);
                    if (IsOp(",")) Next();
                    else if (!IsOp("}")) throw new CsError(file, Line, "dizi baslaticisinda ',' ya da '}' bekleniyordu");
                }
                Next();
                return array;
            }
            if (rank != 1) throw new CsError(file, line, "rectangular dizi baslaticisi satirlar halinde '{ { ... }, { ... } }' yazilmali");
            array.Items = new List<Expr>();
            while (!IsOp("}"))
            {
                array.Items.Add(ParseExpr());
                if (IsOp(",")) Next();
                else if (!IsOp("}")) throw new CsError(file, Line, "dizi baslaticisinda ',' ya da '}' bekleniyordu");
            }
            Next();
            return array;
        }

        // C# atamasi sag-birlesir: a = b = Make() once Make'i, sonra b'yi, sonra a'yi yazar.
        // AST hedef zincirini tutar; BodyEmitter RHS'i gizli temp'e bir kez degerlendirir.
        SAssign ParseAssignment(Expr firstTarget, int line, bool consumeSemicolon)
        {
            var targets = new List<Expr> { firstTarget };
            while (true)
            {
                Expect("=");
                var value = ParseExpr();
                if (!IsOp("="))
                {
                    if (consumeSemicolon) Expect(";");
                    return new SAssign { Line = line, Target = firstTarget, Value = value, ChainTargets = targets };
                }
                targets.Add(value);
            }
        }

        // 'T x' / 'A.B.C x' / 'T[] x' / 'Box<int> x' bildirimi mi, ifade mi? tip kismini gecip sonrasinda id + (=|;) varsa decl
        bool LooksLikeDecl()
        {
            int q = p;
            if (toks[q].Kind != "id") return false;
            q++;
            while (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "." && toks[q + 1].Kind == "id") q += 2;
            if (toks[q].Kind == "op" && toks[q].Value == "<" && !ScanTypeArgs(ref q)) return false;
            if (toks[q].Kind == "op" && toks[q].Value == "?") q++;
            while (toks[q].Kind == "op" && toks[q].Value == "*") q++;
            if (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "[")
            {
                q++;
                while (toks[q].Kind == "op" && toks[q].Value == ",") q++;
                if (toks[q].Kind != "op" || toks[q].Value != "]") return false;
                q++;
            }
            if (toks[q].Kind != "id") return false;
            q++;
            return toks[q].Kind == "op" && (toks[q].Value == "=" || toks[q].Value == ";" || toks[q].Value == ",");
        }

        // lambda mi? "x =>" ya da "( ... ) =>" (kapanan parantezden sonra =>)
        bool IsLambdaAhead()
        {
            var t = Peek();
            if (t.Kind == "id" && p + 1 < toks.Count && toks[p + 1].Kind == "op" && toks[p + 1].Value == "=>") return true;
            if (t.Kind == "op" && t.Value == "(")
            {
                int i = p + 1, depth = 1;
                while (i < toks.Count && depth > 0)
                {
                    if (toks[i].Kind == "op" && toks[i].Value == "(") depth++;
                    else if (toks[i].Kind == "op" && toks[i].Value == ")") depth--;
                    i++;
                }
                return i < toks.Count && toks[i].Kind == "op" && toks[i].Value == "=>";
            }
            return false;
        }

        Expr ParseLambda()
        {
            int line = Line;
            var lam = new ELambda { Line = line };
            if (IsOp("("))
            {
                Next();
                while (!IsOp(")"))
                {
                    int save = p; // "T ad" | "ad": tip adindan sonra id geliyorsa tipli
                    string tn = null, nm;
                    try
                    {
                        var t0 = ParseTypeName();
                        if (Peek().Kind == "id") { tn = t0; nm = ExpectId(); }
                        else { p = save; nm = ExpectId(); }
                    }
                    catch (CsError) { p = save; nm = ExpectId(); }
                    lam.Params.Add(new CsParam { TypeName = tn, Name = nm });
                    if (IsOp(",")) Next();
                }
                Next();
            }
            else lam.Params.Add(new CsParam { Name = ExpectId() });
            Expect("=>");
            if (IsOp("{"))
            {
                lam.BlockBody = new List<Stmt>();
                Next();
                while (!IsOp("}")) lam.BlockBody.Add(ParseStmt());
                Next();
            }
            else lam.Body = ParseExpr();
            return lam;
        }

        // expression-bodied uye govdesi (C# semantigi): non-void '=> e;' = { return e; };
        // void/set/ctor '=> stmt-expr;' = { stmt; } (atama/cagri/++ gibi ifade-deyimler)
        List<Stmt> ParseArrowBody(bool isVoid)
        {
            Next(); // '=>'
            if (isVoid)
            {
                var st = ParseStmt();
                if (!(st is SExpr || st is SAssign))
                    throw new CsError(file, Line, "expression-bodied govde ifade-deyim olmali (cagri/atama)");
                return new List<Stmt> { st };
            }
            int line = Line;
            var e = ParseExpr();
            Expect(";");
            return new List<Stmt> { new SReturn { Line = line, E = e } };
        }

        Expr ParseExpr(bool allowAssignment = true)
        {
            if (IsLambdaAhead()) return ParseLambda();
            Expr c = ParseCoalesce();
            if (IsOp("?")) // ternary (sag-birlesir: a ? b : c ? d : e)
            {
                int line = Line;
                Next();
                var t = ParseExpr();
                Expect(":");
                c = new ETernary { Line = line, Cond = c, Then = t, Else = ParseExpr() };
            }
            if (allowAssignment && IsOp("="))
            {
                int line = Line;
                Next();
                return new EAssign { Line = line, Target = c, Value = ParseExpr() };
            }
            if (allowAssignment)
            {
                var op = TryAssignOp();
                if (op != null)
                    return new EAssign { Line = Line, Target = c, Value = ParseExpr(), Op = op };
            }
            return c;
        }

        // ??, ?: operatorunden yuksek; sag-birlesir: a ?? b ?? c.
        Expr ParseCoalesce()
        {
            var left = ParseOr();
            if (!IsOp("??")) return left;
            int line = Line;
            Next();
            return new ECoalesce { Line = line, Left = left, Right = ParseCoalesce() };
        }

        Expr ParseOr()
        {
            var l = ParseAnd();
            while (IsOp("||")) { int line = Line; Next(); l = new EBin { Line = line, Op = "||", L = l, R = ParseAnd() }; }
            return l;
        }

        Expr ParseAnd()
        {
            var l = ParseBitOr();
            while (IsOp("&&")) { int line = Line; Next(); l = new EBin { Line = line, Op = "&&", L = l, R = ParseBitOr() }; }
            return l;
        }

        // C# oncelik: esitlik > & > ^ > | > &&
        Expr ParseBitOr()
        {
            var l = ParseBitXor();
            while (IsOp("|")) { int line = Line; Next(); l = new EBin { Line = line, Op = "|", L = l, R = ParseBitXor() }; }
            return l;
        }

        Expr ParseBitXor()
        {
            var l = ParseBitAnd();
            while (IsOp("^")) { int line = Line; Next(); l = new EBin { Line = line, Op = "^", L = l, R = ParseBitAnd() }; }
            return l;
        }

        Expr ParseBitAnd()
        {
            var l = ParseEquality();
            while (IsOp("&")) { int line = Line; Next(); l = new EBin { Line = line, Op = "&", L = l, R = ParseEquality() }; }
            return l;
        }

        Expr ParseEquality()
        {
            var l = ParseRelational();
            while (IsOp("==") || IsOp("!="))
            {
                int line = Line; var op = Next().Value;
                l = new EBin { Line = line, Op = op, L = l, R = ParseRelational() };
            }
            return l;
        }

        Expr ParseRelational()
        {
            var l = ParseShift();
            while (true)
            {
                if (IsOp("<") || IsOp(">") || IsOp("<=") || IsOp(">="))
                {
                    if (IsOp(">") && (IsAdjacentGtGt() || IsAdjacentGtGe())) break; // '>>' shift / '>>=' bilesik atama
                    int line = Line; var op = Next().Value;
                    l = new EBin { Line = line, Op = op, L = l, R = ParseShift() };
                }
                else if (IsId("is"))
                {
                    int line = Line; Next();
                    var eis = new EIs { Line = line, Expr = l, TypeName = ParseTypeName(false) };
                    if (Peek().Kind == "id" && !IsId("is") && !IsId("as")) eis.VarName = ExpectId(); // pattern: x is T ad
                    l = eis;
                }
                else if (IsId("as")) { int line = Line; Next(); l = new EAs { Line = line, Expr = l, TypeName = ParseTypeName() }; }
                else break;
            }
            return l;
        }

        // lexer '>>' uretmez (generic kapanisi); ifadede bitisik '>' '>' shift sayilir
        bool IsAdjacentGtGt() =>
            toks[p].Kind == "op" && toks[p].Value == ">" &&
            toks[p + 1].Kind == "op" && toks[p + 1].Value == ">" && toks[p + 1].Pos == toks[p].Pos + 1;

        bool IsAdjacentGtGe() => // '>>=' = '>' + '>=' bitisik cifti
            toks[p].Kind == "op" && toks[p].Value == ">" &&
            toks[p + 1].Kind == "op" && toks[p + 1].Value == ">=" && toks[p + 1].Pos == toks[p].Pos + 1;

        Expr ParseShift()
        {
            var l = ParseAdditive();
            while (true)
            {
                if (IsOp("<<")) { int line = Line; Next(); l = new EBin { Line = line, Op = "<<", L = l, R = ParseAdditive() }; }
                else if (IsAdjacentGtGt()) { int line = Line; Next(); Next(); l = new EBin { Line = line, Op = ">>", L = l, R = ParseAdditive() }; }
                else break;
            }
            return l;
        }

        Expr ParseAdditive()
        {
            var l = ParseMultiplicative();
            while (IsOp("+") || IsOp("-"))
            {
                int line = Line; var op = Next().Value;
                l = new EBin { Line = line, Op = op, L = l, R = ParseMultiplicative() };
            }
            return l;
        }

        Expr ParseMultiplicative()
        {
            var l = ParseUnary();
            while (IsOp("*") || IsOp("/") || IsOp("%"))
            {
                int line = Line; var op = Next().Value;
                l = new EBin { Line = line, Op = op, L = l, R = ParseUnary() };
            }
            return l;
        }

        Expr ParseUnary()
        {
            if (IsId("unchecked"))
            {
                Next();
                Expect("(");
                var expression = ParseExpr();
                Expect(")");
                return expression;
            }
            if (IsOp("(") && IsCastAhead())
            {
                int line = Line;
                Next();
                var tn = ParseTypeName();
                Expect(")");
                return new ECast { Line = line, TypeName = tn, Expr = ParseUnary() };
            }
            if (IsOp("+")) { int line = Line; Next(); return new EUn { Line = line, Op = "+", E = ParseUnary() }; }
            if (IsOp("-")) { int line = Line; Next(); return new EUn { Line = line, Op = "-", E = ParseUnary() }; }
            if (IsOp("!")) { int line = Line; Next(); return new EUn { Line = line, Op = "!", E = ParseUnary() }; }
            if (IsOp("~")) { int line = Line; Next(); return new EUn { Line = line, Op = "~", E = ParseUnary() }; }
            if (IsOp("++")) { int line = Line; Next(); return new EInc { Line = line, Target = ParseUnary(), Delta = 1, IsPrefix = true }; }
            if (IsOp("--")) { int line = Line; Next(); return new EInc { Line = line, Target = ParseUnary(), Delta = -1, IsPrefix = true }; }
            return ParsePostfix();
        }

        // "(TipAdi) <primary-baslangici>" mi? (parser tipsiz -> sozdizimsel heuristik;
        // '(x) - y' gibi binary devamlar cast SAYILMAZ; builtin tip adlarinda belirsizlik yok,
        // '(int)-x' gibi unary devamlar da cast kabul edilir)
        static readonly HashSet<string> BuiltinTypeNames = new HashSet<string>
        { "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte", "char", "bool", "float", "double", "object", "string" };

        bool IsCastAhead()
        {
            int q = p + 1;
            if (toks[q].Kind != "id") return false;
            bool builtin = BuiltinTypeNames.Contains(toks[q].Value);
            q++;
            while (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "." && toks[q + 1].Kind == "id") q += 2;
            if (toks[q].Kind == "op" && toks[q].Value == "<" && !ScanTypeArgs(ref q)) return false;
            while (toks[q].Kind == "op" && toks[q].Value == "*") q++;
            if (q + 1 < toks.Count && toks[q].Kind == "op" && toks[q].Value == "[" && toks[q + 1].Kind == "op" && toks[q + 1].Value == "]") q += 2;
            if (!(toks[q].Kind == "op" && toks[q].Value == ")")) return false;
            q++;
            var t = toks[q];
            if (t.Kind == "id" || t.Kind == "num" || t.Kind == "str" || t.Kind == "chr" || (t.Kind == "op" && t.Value == "(")) return true;
            return builtin && t.Kind == "op" && (t.Value == "-" || t.Value == "~" || t.Value == "!");
        }

        // C# tam sayi literal tipi: suffix + buyukluk (int -> uint -> long -> ulong)
        static string NumTag(Tok t)
        {
            if (t.IsFloat) return t.NumSuffix == "f" ? "float" : "double"; // suffix'siz ondalik C#'ta double
            ulong v = ulong.Parse(t.Value);
            switch (t.NumSuffix)
            {
                case "u": return v <= uint.MaxValue ? "uint" : "ulong";
                case "l": return v <= long.MaxValue ? "long" : "ulong";
                case "ul": return "ulong";
                default:
                    return v <= int.MaxValue ? "int" : v <= uint.MaxValue ? "uint" : v <= long.MaxValue ? "long" : "ulong";
            }
        }

        Expr ParsePostfix()
        {
            var e = ParsePrimary();
            while (true)
            {
                if (IsOp("("))
                {
                    var invoke = new EInvoke { Line = Line, Target = e };
                    ParseArgs(invoke.Args);
                    e = invoke;
                }
                else if (IsOp("."))
                {
                    Next();
                    var name = ExpectId();
                    if (IsOp("<") && IsTypeArgsAhead(p) && PeekAfterTypeArgs(p) == "(") // obj.Yap<int>(...)
                    {
                        var typeArgs = ParseTypeArgList();
                        var gcall = new ECall { Line = Line, Target = e, Name = name, TypeArgs = typeArgs };
                        ParseArgs(gcall.Args);
                        e = gcall;
                    }
                    else if (IsOp("("))
                    {
                        var call = new ECall { Line = Line, Target = e, Name = name };
                        ParseArgs(call.Args);
                        e = call;
                    }
                    else e = new EField { Line = Line, Obj = e, Name = name };
                }
                else if (IsOp("?."))
                {
                    int line = Line;
                    Next();
                    var name = ExpectId();
                    if (IsOp("("))
                    {
                        var call = new ENullConditionalCall { Line = line, Target = e, Name = name };
                        ParseArgs(call.Args);
                        e = call;
                    }
                    else e = new ENullConditionalMember { Line = line, Target = e, Name = name };
                }
                else if (IsOp("[")) // dizi indeksleme
                {
                    Next();
                    var indices = new List<Expr> { ParseExpr() };
                    while (IsOp(",")) { Next(); indices.Add(ParseExpr()); }
                    Expect("]");
                    e = new EIndex { Line = Line, Obj = e, Indices = indices };
                }
                else if (IsOp("++")) { int line = Line; Next(); e = new EInc { Line = line, Target = e, Delta = 1, IsPrefix = false }; }
                else if (IsOp("--")) { int line = Line; Next(); e = new EInc { Line = line, Target = e, Delta = -1, IsPrefix = false }; }
                else break;
            }
            return e;
        }

        void ParseArgs(List<Expr> into)
        {
            Expect("(");
            while (!IsOp(")"))
            {
                if (IsId("ref")) { int l = Line; Next(); into.Add(new ERef { Line = l, Target = ParseExpr(), IsOut = false }); }
                else if (IsId("out"))
                {
                    int l = Line; Next();
                    if (IsId("var") && toks[p + 1].Kind == "id") // out var x: tip cagri cozumunden gelir
                    {
                        Next();
                        into.Add(new ERef { Line = l, Target = new EName { Line = l, Name = ExpectId() }, IsOut = true, DeclareVar = true });
                    }
                    else if (IsTypedOutDeclarationAhead()) // out T x / out T[] x / out Box<T> x
                    {
                        var tn = ParseTypeName();
                        into.Add(new ERef { Line = l, Target = new EName { Line = l, Name = ExpectId() }, IsOut = true, DeclareVar = true, DeclType = tn });
                    }
                    else into.Add(new ERef { Line = l, Target = ParseExpr(), IsOut = true });
                }
                else into.Add(ParseExpr());
                if (IsOp(",")) Next();
            }
            Next();
        }

        bool IsTypedOutDeclarationAhead()
        {
            int q = p;
            return ScanTypeRef(ref q) && toks[q].Kind == "id";
        }

        Expr ParsePrimary()
        {
            int line = Line;
            var t = Peek();
            if (t.Kind == "num")
            {
                Next();
                return new ELit { Line = line, Tag = NumTag(t), Value = t.Value };
            }
            if (t.Kind == "str") { Next(); return new ELit { Line = line, Tag = "str", Value = t.Value }; }
            if (t.Kind == "istr") { Next(); return ParseInterpolatedString(t.Value, line); }
            if (t.Kind == "chr") { Next(); return new ELit { Line = line, Tag = "chr", Value = t.Value }; }
            if (IsId("true")) { Next(); return new ELit { Line = line, Tag = "bool", Value = "true" }; }
            if (IsId("false")) { Next(); return new ELit { Line = line, Tag = "bool", Value = "false" }; }
            if (IsId("null")) { Next(); return new ELit { Line = line, Tag = "null", Value = "" }; }
            if (IsId("throw"))
            {
                Next();
                return new EThrow { Line = line, Value = ParseExpr() };
            }
            if (IsId("typeof"))
            {
                Next(); Expect("(");
                var tn = ParseTypeName();
                Expect(")");
                return new ETypeOf { Line = line, TypeName = tn };
            }
            if (IsId("default"))
            {
                Next(); Expect("(");
                var dn = ParseTypeName();
                Expect(")");
                return new EDefault { Line = line, TypeName = dn };
            }
            if (IsId("delegate"))
            {
                Next();
                var lambda = new ELambda { Line = line, BlockBody = new List<Stmt>() };
                if (IsOp("("))
                {
                    Next();
                    while (!IsOp(")"))
                    {
                        var parameter = new CsParam { TypeName = ParseTypeName(), Name = ExpectId() };
                        lambda.Params.Add(parameter);
                        if (IsOp(",")) Next();
                    }
                    Next();
                }
                Expect("{");
                while (!IsOp("}")) lambda.BlockBody.Add(ParseStmt());
                Next();
                return lambda;
            }
            if (IsId("new"))
            {
                Next();
                var tn = ExpectId();
                while (IsOp(".")) { Next(); tn += "." + ExpectId(); }
                if (IsOp("<") && IsTypeArgsAhead(p)) // new Box<int>(...)
                {
                    Next();
                    tn += "<" + ParseTypeName();
                    while (IsOp(",")) { Next(); tn += "," + ParseTypeName(); }
                    Expect(">");
                    tn += ">";
                }
                if (IsOp("[")) // new T[n] | new T[n,m] | new T[] { e1, e2, ... }
                {
                    Next();
                    if (IsOp("]")) // new T[] { e1, e2, ... }
                    {
                        Next(); Expect("{");
                        var na = new ENewArray { Line = line, ElemTypeName = tn, Items = new List<Expr>() };
                        while (!IsOp("}"))
                        {
                            na.Items.Add(ParseExpr());
                            if (IsOp(",")) Next();
                        }
                        Next();
                        return na;
                    }
                    if (IsOp(",")) // new T[,] { { e1, e2 }, ... }
                    {
                        int rank = 1;
                        while (IsOp(",")) { rank++; Next(); }
                        Expect("]");
                        if (!IsOp("{")) throw new CsError(file, Line, "rectangular dizi boyutlari ya da baslaticisi bekleniyordu");
                        return ParseArrayInitializer(new ENewArray { Line = line, ElemTypeName = tn }, rank, line);
                    }
                    var sizes = new List<Expr> { ParseExpr() };
                    while (IsOp(",")) { Next(); sizes.Add(ParseExpr()); }
                    Expect("]");
                    return new ENewArray { Line = line, ElemTypeName = tn, Sizes = sizes };
                }
                var nw = new ENew { Line = line, TypeName = tn };
                if (IsOp("(")) ParseArgs(nw.Args);
                else if (!IsOp("{")) throw new CsError(file, Line, "new ifadesinden sonra '(' ya da '{' bekleniyordu");
                if (IsOp("{"))
                {
                    Next();
                    bool objectInitializer = Peek().Kind == "id" && toks[p + 1].Kind == "op" && toks[p + 1].Value == "=";
                    while (!IsOp("}"))
                    {
                        if (objectInitializer)
                        {
                            var init = new CsObjectInitializer { Line = Line, Name = ExpectId() };
                            Expect("=");
                            init.Value = IsOp("{") ? ParseNestedObjectInitializer() : ParseExpr();
                            nw.Initializers.Add(init);
                        }
                        else
                        {
                            var init = new CsCollectionInitializer { Line = Line };
                            if (IsOp("{")) // element initializer: { key, value } -> Add(key, value)
                            {
                                Next();
                                init.Args = new List<Expr>();
                                while (!IsOp("}"))
                                {
                                    init.Args.Add(ParseExpr());
                                    if (IsOp(",")) Next();
                                    else if (!IsOp("}")) throw new CsError(file, Line, "collection elemaninda ',' ya da '}' bekleniyordu");
                                }
                                Next();
                            }
                            else if (IsOp("["))
                            {
                                Next();
                                init.Index = ParseExpr();
                                Expect("]");
                                Expect("=");
                            }
                            if (init.Args == null) init.Value = ParseExpr();
                            nw.CollectionInitializers.Add(init);
                        }
                        if (IsOp(",")) Next();
                        else if (!IsOp("}")) throw new CsError(file, Line, "initializer'da ',' ya da '}' bekleniyordu");
                    }
                    Next();
                }
                return nw;
            }
            if (IsOp("("))
            {
                Next();
                var e = ParseExpr();
                Expect(")");
                return e;
            }
            if (t.Kind == "id")
            {
                Next();
                // acik tip argumanli cagri Max<int>(...) ya da generic tip adi Box<int>.Uye devami.
                // sadece '(' (cagri) veya '.' (uye erisimi) izliyorsa tip argumanidir; 'a < b' boyle kalir.
                if (IsOp("<") && IsTypeArgsAhead(p) && (PeekAfterTypeArgs(p) == "(" || PeekAfterTypeArgs(p) == "."))
                {
                    var typeArgs = ParseTypeArgList();
                    if (IsOp("("))
                    {
                        var gcall = new ECall { Line = line, Name = t.Value, TypeArgs = typeArgs };
                        ParseArgs(gcall.Args);
                        return gcall;
                    }
                    return new EName { Line = line, Name = t.Value + "<" + string.Join(",", typeArgs) + ">", IsEscaped = t.IsEscapedIdentifier }; // Box<int>.Count() gibi tip adi
                }
                if (IsOp("(")) // bare cagri: ayni sinifin methodu
                {
                    var call = new ECall { Line = line, Name = t.Value };
                    ParseArgs(call.Args);
                    return call;
                }
                return new EName { Line = line, Name = t.Value, IsEscaped = t.IsEscapedIdentifier };
            }
            throw new CsError(file, line, $"ifade bekleniyordu, '{t.Value}' bulundu");
        }

        ENestedObjectInitializer ParseNestedObjectInitializer()
        {
            var nested = new ENestedObjectInitializer { Line = Line };
            Expect("{");
            while (!IsOp("}"))
            {
                var init = new CsObjectInitializer { Line = Line, Name = ExpectId() };
                Expect("=");
                init.Value = IsOp("{") ? ParseNestedObjectInitializer() : ParseExpr();
                nested.Initializers.Add(init);
                if (IsOp(",")) Next();
                else if (!IsOp("}")) throw new CsError(file, Line, "nested initializer'da ',' ya da '}' bekleniyordu");
            }
            Next();
            return nested;
        }

        Expr ParseInterpolatedString(string text, int line)
        {
            var result = new EInterpolated { Line = line };
            var literal = new System.Text.StringBuilder();
            void FlushLiteral()
            {
                if (literal.Length > 0)
                {
                    result.Parts.Add(new CsInterpolationPart { Text = literal.ToString() });
                    literal.Clear();
                }
            }
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    if (i + 1 < text.Length && text[i + 1] == '{') { literal.Append('{'); i++; continue; }
                    FlushLiteral();
                    int start = ++i, depth = 1;
                    while (i < text.Length && depth > 0)
                    {
                        if (text[i] == '{') depth++;
                        else if (text[i] == '}') depth--;
                        if (depth > 0) i++;
                    }
                    if (depth != 0) throw new CsError(file, line, "kapanmayan interpolated expression");
                    var embedded = new CsParser(CsLexer.Tokenize(text.Substring(start, i - start), file), file);
                    var expression = embedded.ParseExpr();
                    if (embedded.Peek().Kind != "eof") throw new CsError(file, line, "interpolated expression sonu gecersiz");
                    result.Parts.Add(new CsInterpolationPart { Expr = expression });
                    continue;
                }
                if (text[i] == '}')
                {
                    if (i + 1 < text.Length && text[i + 1] == '}') { literal.Append('}'); i++; continue; }
                    throw new CsError(file, line, "interpolated string icinde eslesmeyen '}'");
                }
                literal.Append(text[i]);
            }
            FlushLiteral();
            if (result.Parts.Count == 0) result.Parts.Add(new CsInterpolationPart { Text = "" });
            return result;
        }

        List<string> ParseTypeArgList()
        {
            Expect("<");
            var args = new List<string> { ParseTypeName() };
            while (IsOp(",")) { Next(); args.Add(ParseTypeName()); }
            Expect(">");
            return args;
        }
    }
}
