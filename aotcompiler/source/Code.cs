using System.Collections.Generic;
using System.Linq;

namespace DigitoyEngine.Language
{

    public enum OpType
    {
        GetArg,
        SetArg,
        GetLocal,
        SetLocal,
        GetField,
        SetField,
        GetIndex,
        SetIndex,
        New,
        Dup,
        Return,
        Push,
        Pop,
        Mul,
        Div,
        Add,
        Sub,
        Mod,
        Neg,
        And,
        Or,
        Not,
        Xor,
        Shl,
        Shr,
        Call,
        Label,   // jump hedefi (VM'de no-op, Linker tarafindan index'e cozulur)
        Br,      // kosulsuz goto
        Brtrue,  // pop; 0 degilse goto
        Brfalse, // pop; 0 ise goto
        Ceq,     // pop b,a; push a==b ? 1 : 0
        Cne,     // pop b,a; push a!=b ? 1 : 0 (CIL bne ailesinin compare esdegeri)
        Cgt,     // pop b,a; push a>b ? 1 : 0
        Cge,     // pop b,a; push a>=b ? 1 : 0 (NaN'de !(a<b)'den FARKLI - gercek >= semantigi)
        Clt,     // pop b,a; push a<b ? 1 : 0
        Cle,     // pop b,a; push a<=b ? 1 : 0
        AddrLocal, // CIL ldloca: local slotunun adresini push et (ref/out gecirmek icin)
        AddrArg,   // CIL ldarga: arg slotunun adresini push et
        AddrField,   // pop nesne (class-ptr ya da struct-ptr); push &nesne->alan (struct lvalue zinciri)
        AddrElement, // pop idx, dizi; push &eleman (null+bounds check'li; struct dizi elemani mutasyonu)
        ArrayDataAddr, // pop T[]/fixed T[N]; push ilk eleman adresi (fixed lowering, pinning yok)
        AddrStatic,  // op.Field: static deponun adresini push et
        LoadInd,   // CIL ldind: pop adres; push *adres
        StoreInd,  // CIL stind: pop deger, pop adres; *adres = deger
        CallVirtual, // CIL callvirt: arg0'in RUNTIME tipinin vtable'indan dispatch (op.Code = bildiren method, op.Slot = Resolver'in atadigi vtable slotu)
        GetStatic, // op.Field (IsStatic): bildiren tipin static deposundan oku
        SetStatic, // pop deger -> static depoya yaz
        NewArray,    // pop uzunluk; op.PrimitiveRef = ELEMAN tipi; yeni dizi push (C: vmarray_new, VM: object[])
        ArrayLength, // pop dizi; uzunlugu push (C: ->len)
        StackAlloc,  // CIL localloc: pop bayt sayisi; push byte* (C alloca, sifirlanmis)
        SizeOf,      // CIL sizeof: PrimitiveRef tipinin C bayt boyutunu push (int; struct -> sizeof(struct X), referans -> pointer)
        Conv,      // pop deger; op.PrimitiveRef = hedef SKALER tip; donusturup push (CIL conv.*, C cast truncation semantigi)
        IsType,    // pop instance; op.PrimitiveRef = model; tip zincirinde mi -> 1/0 (null -> 0)
        IsValueType, // pop deger; PrimitiveRef = kaynak tip; monomorphization sonrasi value type mi -> 1/0
        AsType,    // pop instance; uyuyorsa instance, uymuyorsa null push (CIL isinst)
        CastClass, // pop instance; uymuyorsa HATA (CIL castclass; null gecer)
        GenericCast, // generic template'te object -> T; klonlamada CastClass/Unbox'a donusur
        TypeOf,    // op.PrimitiveRef'in System.Type wrapper'ini push et (typeof(T); kimlik esitligi)
        Box,            // PrimitiveRef kaynak tipi: deger -> object kutusu; referans -> object no-op
        Unbox,          // object -> deger, exact tip (PrimitiveRef = hedef; null->NullRef, uyusmazlik->InvalidCast)
        UnboxOrDefault, // pattern icin: tip tutarsa deger, tutmazsa 0 (firlatmaz; 'o is int i')
        NullableWrap,   // pop T/null; PrimitiveRef Nullable<T>; T -> {hasValue=1,value=T}, null -> default
        NullableHasValue, // pop Nullable<T>; push bool
        NullableValue,  // pop Nullable<T>; push T (caller HasValue ile guard etmeli)
        NullableBinary, // pop rhs/lhs; Value operatoru; lifted aritmetik veya karsilastirma
        DelegateNew,    // method group -> delegate nesnesi (Code=hedef, PrimitiveRef=delegate tipi; instance ise alici pop; Slot=-2 sanal->Resolver doldurur)
        DelegateCombine,// pop sag+sol delegate; immutable invocation list birlesimi
        DelegateRemove, // pop sag+sol delegate; sag listenin son eslesmesini cikar
        CallIndirect,   // pop arglar + delegate; imza PrimitiveRef'ten (delegate tipi); null->NullRef
        Default,        // PrimitiveRef tipinin sifir degerini push et (default(T); klonlamada somutlanir)
        Throw,    // pop exception nesnesi; en yakin try handler'ina (yoksa cokme) firlatir
        TryBegin, // try acar: Label = handler basi (handler ExIs/ExBind ile baslar; PrimitiveRef bilgi amacli catch tipi)
        TryEnd,   // try'i normal yoldan kapatir (govde sonunda handler'i Br ile atla, sonra TryEnd)
        ExIs,     // handler icinde: bekleyen exception PrimitiveRef tipine uyuyor mu -> 1/0 push
        ExBind,   // handler icinde: bekleyen exception'i baglar (runtime kind -> singleton; trace alanlari VARSA nesneye kopyalanir) ve push eder
        Rethrow,  // handler icinde: bekleyen exception'i trace'ini KORUYARAK yeniden firlatir (C# `throw;`)
        TryUnwind, // erken cikis (return/break/continue) icin: Slot=1 en icteki olmak uzere o bolgeye KADAR runtime try zincirini geri sarar (derleme yigini DEGISMEZ - dallanma yolu)
        Yield,    // SADECE IteratorLowering oncesi var olabilir (pop deger -> frame.current, resume noktasi); VM/CTranspiler'a ASLA ulasmaz
    }
    public class Op
    {
        public OpType Type;
        public int Slot; // index/newarray op'larinda rank (0 = eski IR, rank 1)
        public int Line; // kaynak satiri (1-tabanli, 0 = yok); DIGITOYENGINE_LINE/stack trace icin
        public PrimitiveField Field;
        public object Value;
        public Code Code;
        public Primitive PrimitiveRef; // New icin: olusturulacak model tipi
        public List<Primitive> TypeArguments = new List<Primitive>(); // New/Call generic ise somutlastirma argumanlari
        public Label Label; // Label op'unda bu op'un kimligi, Br/Brtrue/Brfalse'de hedef

        public override string ToString() => $"Op(Type={Type}, Slot={Slot}, Field={(Field != null ? Field.Owner.Name + "$" + Field.Name : "null")}, Value={Value}, Code={(Code != null ? Code.EncodeName() : "null")}, PrimitiveRef={(PrimitiveRef != null ? PrimitiveRef.Name : "null")})";
    }
    public class Argument
    {
        public string Name;
        public Primitive Type;
        public object Value;
        public bool IsRef;
        public bool IsOut;
    }

    public class Code
    {
        public bool Unresolved;
        public Primitive Owner;
        public string Name;
        public string Assembly; // tanimlayan assembly adi (CilFrontend; generic klonlar sablonunkini tasir)
        public Code Template; // generic METHOD somutlamasi ise kaynagi (GenericInstantiator); modul yazici "gomulu generic" karari
        public bool IsExternal;
        public List<Op> Operations = new List<Op>();
        public bool Inline;
        public List<Argument> Arguments = new List<Argument>();
        public bool IsStatic;
        public bool IsVirtual;  // vtable'da yeni slot acar
        public bool IsOverride; // parent'taki ayni isimli virtual slotu ezer
        public Primitive ExplicitInterface; // null degilse yalniz bu interface'in itable implementasyonu
        public Primitive ReturnType = Primitive.Void; // Call'da sonuc push edilecek mi karari icin
        public List<Primitive> GenericParameters = new List<Primitive>();
        public List<Primitive> Locals = new List<Primitive>(); // GetLocal/SetLocal slotlarinin tipleri, VM icin sadece boyut lazim
        public List<string> LocalNames = new List<string>(); // sadece .acode/asm okunurlugu icin, binary Encode/Decode'a girmez (index'ler zaten Slot'ta)
        public string NativeBody;
        public string NativeHeader; // Optional native header for the code
        public string ExternalSymbol; // null degilse: extern C sembol adi (P/Invoke EntryPoint) - mangling yerine bu kullanilir
        public string SourceFile;   // kaynak dosya adi (MethodInfo.file; debugger file:line eslesmesi)
        public string DisplayName;  // C# sozdizimli uye gosterimi: "Deep(int[], int)" / ".ctor(int)" / "Finalize()"
        public string UntranslatableReason; // null degilse: govde cevrilemedi, CTranspiler tipe uygun sifir stub uretir
        public bool BodyCloned; // gecici: monomorphization iki-fazli klonda govde doldurma bir kez yapilsin (serialize edilmez)

        public string EncodeName() => (Owner != null ? Owner.Name + "$" : "") + Name;

        // overload mangling'inin TEK tanimi (front-end + Intrinsics ayni sozlesmeyi kullanir)
        public static string Mangle(string methodName, IEnumerable<Primitive> paramTypes)
        {
            var sb = new System.Text.StringBuilder(methodName);
            foreach (var t in paramTypes) sb.Append('_').Append(t.Name.Replace('.', '_'));
            return sb.ToString();
        }
        public void DecodeName(string encodedName)
        {
            string[] ownerAndName = encodedName.Split('$');
            if (ownerAndName.Length == 2)
            {
                Owner = new Primitive { Name = ownerAndName[0], Unresolved = true };
            }
            Name = ownerAndName.Length == 2 ? ownerAndName[1] : ownerAndName[0];
        }
        public string Encode()
        {
            var w = new TextWriter();
            w.Str(EncodeName());
            w.Line(IsStatic);
            w.Line(IsExternal);
            w.Line(Inline);
            w.Line(IsVirtual);
            w.Line(IsOverride);
            w.Str(ExplicitInterface != null ? ExplicitInterface.Name : null);
            w.Str(NativeHeader);
            w.Str(NativeBody);
            w.Str(ExternalSymbol);
            w.Str(ReturnType != null ? ReturnType.Name : null);

            w.Line(Arguments.Count);
            foreach (var arg in Arguments)
            {
                w.Str(arg.Name);
                w.Str(arg.Type != null ? arg.Type.Name : null);
                w.Line(arg.IsRef);
                w.Line(arg.IsOut);
            }

            w.Line(GenericParameters.Count);
            foreach (var gp in GenericParameters)
                w.Str(gp.Name);

            w.Line(Locals.Count);
            foreach (var local in Locals)
                w.Str(local != null ? local.Name : null);

            w.Line(Operations.Count);
            foreach (var op in Operations)
            {
                w.Line(op.Type);
                w.Line(op.Slot);
                w.Line(op.Line);
                w.Str(op.Field != null ? op.Field.Owner.Name + "$" + op.Field.Name : null);
                if (op.Value != null)
                {
                    if (op.Value is string str) { w.Line("str"); w.Str(str); }
                    else if (op.Value is int i) { w.Line("int"); w.Str(i.ToString()); }
                    else if (op.Value is float f) { w.Line("float"); w.Str(f.ToString()); }
                    else if (op.Value is double d) { w.Line("double"); w.Str(d.ToString()); }
                    else if (op.Value is bool b) { w.Line("bool"); w.Str(b.ToString()); }
                    else if (op.Value is long l) { w.Line("long"); w.Str(l.ToString()); }
                    else if (op.Value is char c) { w.Line("char"); w.Str(c.ToString()); }
                    else if (op.Value is short s) { w.Line("short"); w.Str(s.ToString()); }
                    else if (op.Value is byte b2) { w.Line("byte"); w.Str(b2.ToString()); }
                    else
                        throw new System.Exception("Unsupported value type: " + op.Value.GetType().Name);
                }
                else w.Line("null");

                w.Str(op.Code != null ? op.Code.EncodeName() : null);
                w.Str(op.PrimitiveRef != null ? op.PrimitiveRef.Name : null);
                w.Line(op.TypeArguments.Count);
                foreach (var ta in op.TypeArguments)
                    w.Str(ta.Name);
            }
            return w.ToString();
        }

        public static Code Decode(string encoded)
        {
            var r = new TextReader(encoded);
            string encodedName = r.Str();
            var codeName = new Code();
            codeName.DecodeName(encodedName);
            var code = new Code
            {
                Owner = codeName.Owner,
                Name = codeName.Name,
                IsStatic = r.Bool(),
                IsExternal = r.Bool(),
                Inline = r.Bool(),
                IsVirtual = r.Bool(),
                IsOverride = r.Bool()
            };
            var explicitInterfaceName = r.Str();
            code.ExplicitInterface = explicitInterfaceName != null
                ? new Primitive { Name = explicitInterfaceName, Unresolved = true }
                : null;
            code.NativeHeader = r.Str();
            code.NativeBody = r.Str();
            code.ExternalSymbol = r.Str();
            var returnTypeName = r.Str();
            code.ReturnType = returnTypeName != null ? new Primitive { Name = returnTypeName, Unresolved = true } : null;

            var argCount = r.Int();
            for (int i = 0; i < argCount; i++)
            {
                var argName = r.Str();
                var argTypeName = r.Str();
                var isRef = r.Bool();
                var isOut = r.Bool();
                code.Arguments.Add(new Argument { Name = argName, Type = argTypeName != null ? new Primitive { Name = argTypeName, Unresolved = true } : null, IsRef = isRef, IsOut = isOut });
            }

            var genericParamCount = r.Int();
            for (int i = 0; i < genericParamCount; i++)
                code.GenericParameters.Add(new Primitive { Name = r.Str(), IsGenericParameter = true });

            var localCount = r.Int();
            for (int i = 0; i < localCount; i++)
            {
                var localName = r.Str();
                code.Locals.Add(localName != null ? new Primitive { Name = localName, Unresolved = true } : null);
            }

            int operationCount = r.Int();
            for (int i = 0; i < operationCount; i++)
            {
                var op = new Op();
                op.Type = (OpType)System.Enum.Parse(typeof(OpType), r.Line());
                op.Slot = r.Int();
                op.Line = r.Int();
                string fieldStr = r.Str();
                if (fieldStr != null)
                {
                    string[] fieldParts = fieldStr.Split('$');
                    op.Field = new PrimitiveField
                    {
                        Owner = new Primitive { Name = fieldParts[0], Unresolved = true },
                        Name = fieldParts[1]
                    };
                }
                string valueTag = r.Line();
                if (valueTag != "null")
                {
                    string value = r.Str();
                    switch (valueTag)
                    {
                        case "str": op.Value = value; break;
                        case "int": op.Value = int.Parse(value); break;
                        case "float": op.Value = float.Parse(value); break;
                        case "double": op.Value = double.Parse(value); break;
                        case "bool": op.Value = bool.Parse(value); break;
                        case "long": op.Value = long.Parse(value); break;
                        case "char": op.Value = char.Parse(value); break;
                        case "short": op.Value = short.Parse(value); break;
                        case "byte": op.Value = byte.Parse(value); break;
                        default: throw new System.Exception("Unsupported value type: " + valueTag);
                    }
                }

                string encodedCodeName = r.Str();
                if (encodedCodeName != null)
                {
                    var codeObj = new Code();
                    codeObj.DecodeName(encodedCodeName);
                    codeObj.Unresolved = true;
                    op.Code = codeObj;
                }

                string primitiveRefName = r.Str();
                if (primitiveRefName != null)
                    op.PrimitiveRef = new Primitive { Name = primitiveRefName, Unresolved = true };

                var typeArgCount = r.Int();
                for (int t = 0; t < typeArgCount; t++)
                    op.TypeArguments.Add(new Primitive { Name = r.Str(), Unresolved = true });

                code.Operations.Add(op);
            }
            return code;
        }

        // Elle yazilabilir tek-satirlik Op assembly'si (bkz. OpAsm) - Encode/Decode'un tam formatindan
        // AYRI ve ek bir temsil, sadece Operations listesini kapsar (Arguments/Locals ayrica kurulmali).
        public string ToAssembly()
        {
            var labelNames = new System.Collections.Generic.Dictionary<Label, string>();
            int n = 0;
            string NameOf(Label l)
            {
                if (!labelNames.TryGetValue(l, out var nm)) { nm = l.Name ?? ("L" + n++); labelNames[l] = nm; }
                return nm;
            }
            return string.Join("\n", Operations.Select(op => OpAsm.Encode(op, NameOf)));
        }

        public static List<Op> ParseAssembly(string text)
        {
            var labels = new Dictionary<string, Label>();
            Label LabelFor(string name)
            {
                if (!labels.TryGetValue(name, out var l)) { l = new Label { Name = name }; labels[name] = l; }
                return l;
            }
            var ops = new List<Op>();
            foreach (var rawLine in text.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                ops.Add(OpAsm.Parse(line, LabelFor));
            }
            return ops;
        }

        public override string ToString()
        {
            return $"Code(Owner={(Owner != null ? Owner.Name : "null")}, Name={Name}, Operations=[{string.Join(", ", Operations)}])";
        }
    }

}