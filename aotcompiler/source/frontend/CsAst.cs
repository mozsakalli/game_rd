using System.Collections.Generic;

namespace DigitoyEngine.Frontend
{
    // MiniCs AST: kucuk, tipsiz (tipler emisyon sirasinda cozulur - CsCompiler EmitExpr tip dondurur).
    public abstract class Expr { public int Line; }
    public class ELit : Expr { public string Tag; public string Value; }      // Tag: int|float|bool|str|chr
    public class EInterpolated : Expr { public List<CsInterpolationPart> Parts = new List<CsInterpolationPart>(); }
    public class EThrow : Expr { public Expr Value; }
    public class EName : Expr { public string Name; public bool IsEscaped; } // local/param/alan/tip adi (cozum emisyonda)
    public class EBin : Expr { public string Op; public Expr L, R; }
    public class ECoalesce : Expr { public Expr Left, Right; }
    public class EAssign : Expr { public Expr Target, Value; public string Op; } // assignment expression: target [Op]= value (value sonucunu dondurur)
    public class EUn : Expr { public string Op; public Expr E; }             // - ! ~
    public class ETernary : Expr { public Expr Cond, Then, Else; }           // c ? a : b
    public class EInc : Expr { public Expr Target; public int Delta; public bool IsPrefix; } // x++ / --x (Delta +-1)
    public class ECall : Expr { public Expr Target; public string Name; public List<string> TypeArgs; public List<Expr> Args = new List<Expr>(); } // TypeArgs: "Max<int>(...)" acik tip argumanlari (null = yok) // Target null = bare cagri
    public class EInvoke : Expr { public Expr Target; public List<Expr> Args = new List<Expr>(); }
    public class ENullConditionalCall : Expr { public Expr Target; public string Name; public List<Expr> Args = new List<Expr>(); }
    public class ENullConditionalMember : Expr { public Expr Target; public string Name; }
    public class EField : Expr { public Expr Obj; public string Name; }
    public class EIndex : Expr { public Expr Obj; public List<Expr> Indices = new List<Expr>(); }
    public class ENew : Expr { public string TypeName; public List<Expr> Args = new List<Expr>(); public List<CsObjectInitializer> Initializers = new List<CsObjectInitializer>(); public List<CsCollectionInitializer> CollectionInitializers = new List<CsCollectionInitializer>(); } // ctor cagrisi (bos = sifir-init)
    public class ENestedObjectInitializer : Expr { public List<CsObjectInitializer> Initializers = new List<CsObjectInitializer>(); } // Property = { Member = value }
    public class ENewArray : Expr { public string ElemTypeName; public List<Expr> Sizes = new List<Expr>(); public List<Expr> Items; public List<List<Expr>> Rows; } // new T[n] | new T[n,m] | new T[] { ... }
    public class ERef : Expr { public Expr Target; public bool IsOut; public bool DeclareVar; public string DeclType; } // cagri argumani: ref x / out x / out var x / out T x (DeclType null = var)
    public class ECast : Expr { public string TypeName; public Expr Expr; } // (T)x: skaler donusum ya da class downcast
    public class EIs : Expr { public Expr Expr; public string TypeName; public string VarName; }   // x is T [ad] -> bool (+pattern degiskeni)
    public class ETypeOf : Expr { public string TypeName; } // typeof(T) -> System.Type (kimlik esitligi)
    public class EDefault : Expr { public string TypeName; } // default(T): tipin sifir degeri
    // lambda: x => e | (a, b) => e | (int x) => e | x => { ... }  (capture'siz - parametre + static uyeler)
    public class ELambda : Expr { public List<CsParam> Params = new List<CsParam>(); public Expr Body; public List<Stmt> BlockBody; }
    public class EAs : Expr { public Expr Expr; public string TypeName; }   // x as T -> T ya da null

    public abstract class Stmt { public int Line; }
    public class SVar : Stmt { public string TypeName; public string Name; public Expr Init; public bool IsConst; }
    public class SVarGroup : Stmt { public List<SVar> Variables = new List<SVar>(); }
    public class SAssign : Stmt { public Expr Target; public Expr Value; public string Op; public List<Expr> ChainTargets; } // ChainTargets: a=b=value icin [a,b], Op != null: bilesik atama
    public class SBlock : Stmt { public List<Stmt> Body = new List<Stmt>(); }
    public class SUnchecked : Stmt { public List<Stmt> Body = new List<Stmt>(); }
    public class SFixed : Stmt { public string TypeName; public string Name; public Expr Init; public List<Stmt> Body = new List<Stmt>(); }
    public class SLock : Stmt { public Expr Target; public List<Stmt> Body = new List<Stmt>(); }
    public class SDoWhile : Stmt { public Expr Cond; public List<Stmt> Body = new List<Stmt>(); }
    public class SExpr : Stmt { public Expr E; }
    public class SIf : Stmt { public Expr Cond; public List<Stmt> Then = new List<Stmt>(); public List<Stmt> Else = new List<Stmt>(); }
    public class SWhile : Stmt { public Expr Cond; public List<Stmt> Body = new List<Stmt>(); }
    public class SReturn : Stmt { public Expr E; } // E null = void
    public class SYield : Stmt { public Expr E; }      // yield return e; (method iterator olur)
    public class SYieldBreak : Stmt { }                // yield break;
    public class SForeach : Stmt { public string TypeName; public string Name; public Expr Coll; public List<Stmt> Body = new List<Stmt>(); }
    public class SFor : Stmt { public Stmt Init; public Expr Cond; public Stmt Incr; public List<Stmt> Body = new List<Stmt>(); } // Init/Incr null olabilir
    public class SwitchSection { public List<Expr> Labels = new List<Expr>(); public bool HasDefault; public List<Stmt> Body = new List<Stmt>(); } // Labels: sabit ifadeler (derleyici dogrular)
    public class SSwitch : Stmt { public Expr E; public List<SwitchSection> Sections = new List<SwitchSection>(); }
    public class SBreak : Stmt { }    // en yakin dongu/switch'ten cik
    public class SContinue : Stmt { } // en yakin dongunun sonraki adimina
    public class CsCatch { public string TypeName; public string VarName; public List<Stmt> Body = new List<Stmt>(); public int Line; } // TypeName null = catch { } (tumu)
    public class STry : Stmt { public List<Stmt> Body = new List<Stmt>(); public List<CsCatch> Catches = new List<CsCatch>(); public List<Stmt> Finally; }
    public class SThrow : Stmt { public Expr E; } // E null = `throw;` (rethrow, yalniz catch icinde)
    public class CsObjectInitializer { public string Name; public Expr Value; public int Line; }
    public class CsCollectionInitializer { public Expr Index; public Expr Value; public List<Expr> Args; public int Line; } // Index null = Add(Args), dolu = this[Index] = Value
    public class CsInterpolationPart { public string Text; public Expr Expr; }

    public class CsParam { public string TypeName; public string Name; public bool IsRef; public bool IsOut; public bool IsThis; public bool IsParams; public Expr Default; } // Default: yalniz literal
    public class CsGenericConstraint { public string Parameter; public List<string> Bounds = new List<string>(); }
    public class CsMethod
    {
        public string Name;
        public string ExplicitInterface; // IEnumerator IEnumerable.GetEnumerator(): "IEnumerable"
        public string ReturnTypeName;
        public bool IsStatic, IsVirtual, IsOverride;
        public bool IsExtern; // govdesiz bildirim: C govdesi c_runtime/corelib.c'de, VM govdesi intrinsics tablosunda
        public bool IsCtor, IsCctor;
        public bool IsFinalizer; // ~X() {} - Type.finalize thunk'ina baglanir (CTranspiler zincirler)
        public List<string> GenericParams = new List<string>(); // "T Max<T>(...)" - method'un kendi tip parametreleri
        public List<CsGenericConstraint> GenericConstraints = new List<CsGenericConstraint>(); // TODO: semantic enforcement/monomorphization
        public List<Expr> ChainArgs; // ctor ": base(...)" / ": this(...)" argumanlari (null = zincir yok)
        public bool ChainToThis;     // true = ": this(...)", false = ": base(...)"
        public List<CsParam> Params = new List<CsParam>();
        public List<Stmt> Body = new List<Stmt>();
        public int Line;
    }
    public class CsField { public string TypeName; public string Name; public bool IsStatic; public Expr Init; public int FixedCount; public bool IsConst; public bool IsReadonly; public int Line; } // FixedCount>0: fixed T ad[N] (gomulu dizi, GC yok)
    public class CsEnumMember { public string Name; public Expr Value; public int Line; } // Value null = onceki + 1
    // property bildirimi; CsCompiler get_X/set_X methodlarina desugar eder (C#'in kendi modeli).
    // GetBody/SetBody null + HasGet/HasSet true = auto accessor ({ get; set; } -> backing field)
    public class CsProperty
    {
        public string TypeName;
        public string Name;
        public CsParam IndexParam; // null degil = indexer (this[K key] -> get_Item/set_Item)
        public bool IsStatic, IsVirtual, IsOverride;
        public bool IsExtern; // accessor'lar govdesiz native (backing field YOK)
        public bool HasGet, HasSet;
        public string ExplicitInterface; // "IEnumerator.Current" gibi acik arayuz property implementasyonu
        public List<Stmt> GetBody, SetBody;
        public Expr Init; // { get; set; } = deger;
        public int Line;
    }
    public class CsClass
    {
        public string SourceFile;
        public int Line;
        public string Namespace = ""; // FQ ad = Namespace + "." + Name (bos ise sadece Name)
        public string Name;
        public List<string> GenericParams = new List<string>(); // "class Box<T, U>" tip parametreleri
        public List<CsGenericConstraint> GenericConstraints = new List<CsGenericConstraint>(); // TODO: semantic enforcement/monomorphization
        public List<string> BaseNames = new List<string>(); // base class + interface'ler (ayrim CsCompiler'da, parser tipsiz)
        public bool IsStruct;
        public bool IsInterface;
        public bool IsEnum;
        public bool IsPartial; // parcalar ayni dosyada birlestirilir (MergePartials)
        public bool IsDelegate;
        public CsMethod DelegateSig; // delegate R Ad(params): imza (ReturnTypeName + Params)
        public List<CsEnumMember> EnumMembers = new List<CsEnumMember>();
        public List<CsField> Fields = new List<CsField>();
        public List<CsProperty> Properties = new List<CsProperty>();
        public List<CsMethod> Methods = new List<CsMethod>();
        public List<CsClass> NestedTypes = new List<CsClass>();
        public string FullName => Namespace.Length > 0 ? Namespace + "." + Name : Name;
    }
}
