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
        // ---- CANON LOWERING (Lower.cs, docs/engine-notes.md): T'ye bagli bilgi YIGINDAN gelir; emitter/interp "canon" bilmez ----
        // Sozlesme: gizli operandlar tuketici op'un HEMEN ONCESINDE (bitisik) push edilir, yiginin tepesindedir:
        //   [..., degerler, T]  (Rt op'lari)   [recv?, delegateT, fn] (DelegateNewRt)   [..., args, ifaceT?, dict?] (Call/CallVirtual)
        LdType,      // PrimitiveRef: statik descriptor adresi (const Type*) push
        LdDictSlot,  // Slot: mevcut sozlukten slot; Value=(DictSlotKind): TypeHandle -> Type*, TypeDict/MethodDict -> dict, FnPtr -> fn
        LdDictSelf,  // mevcut metodun kendi sozlugu (dict) push — ayni sozlugu paylasan cagri
        LdDictConst, // Code=exact hedef: onun derleme-zamani sozluk sembolu push (exact cagiran -> sozluklu callee)
        LdFnPtr,     // Code: fonksiyon isaretcisi (instantiating stub / dogrudan sembol) push
        NewRt,            // pop T; T descriptor'uyla tahsis (PrimitiveRef: yerlesim/C tipi)
        NewArrayRt,       // pop T (T[] descriptor'u), pop boyutlar; tahsis
        BoxRt,            // pop T, pop deger (struct); T kutusu
        UnboxRt,          // pop T, pop obj; exact unbox
        UnboxOrDefaultRt, // pop T, pop obj; tip tutarsa deger, tutmazsa 0
        IsTypeRt,         // pop T, pop obj; runtime is (class/iface/dizi ayrimi runtime'da)
        AsTypeRt,         // pop T, pop obj; runtime as
        CastRt,           // pop T, pop obj; runtime cast (uymazsa InvalidCast) / deger tipi -> unbox
        TypeOfRt,         // pop T; System.Type wrapper
        GetStaticRt,      // pop T; T'nin statik blogundan Field oku
        SetStaticRt,      // pop T, pop deger; T'nin statik bloguna yaz
        AddrStaticRt,     // pop T; statik alanin adresi
        ExIsRt,           // pop T; bekleyen exception T'ye uyuyor mu
        ExBindRt,         // pop T; bekleyen exception'i T olarak bagla
        DelegateNewRt,    // pop fn, pop delegateT, (pop recv); delegate nesnesi
    }
    public class Op
    {
        public OpType Type;
        public int Slot; // index/newarray op'larinda rank (0 = eski IR, rank 1); LdDictSlot: slot indeksi
        public int Line; // kaynak satiri (1-tabanli, 0 = yok); DIGITOYENGINE_LINE/stack trace icin
        public PrimitiveField Field;
        public object Value;
        public Code Code;
        public Primitive PrimitiveRef; // New icin: olusturulacak model tipi
        public List<Primitive> TypeArguments = new List<Primitive>(); // New/Call generic ise somutlastirma argumanlari (canon govdede MARKER'li acik bicim; Resolver temizlemez)
        public Primitive OpenRef; // canon govde: PrimitiveRef'in marker'li ACIK bicimi (Resolver somutlamadan once saklar); Lower.cs sozluk slot anahtari
        public Label Label; // Label op'unda bu op'un kimligi, Br/Brtrue/Brfalse'de hedef

        // Lower.cs'in urettigi gizli operand push'u mu (Trim/vmint: tuketiciyi bulmak icin atlanir)
        public bool IsHiddenOperand => Type == OpType.LdType || Type == OpType.LdDictSlot || Type == OpType.LdDictSelf || Type == OpType.LdDictConst || Type == OpType.LdFnPtr;

        public override string ToString() => $"Op(Type={Type}, Slot={Slot}, Field={(Field != null ? Field.Owner.Name + "$" + Field.Name : "null")}, Value={Value}, Code={(Code != null ? Code.EncodeName() : "null")}, PrimitiveRef={(PrimitiveRef != null ? PrimitiveRef.Name : "null")})";
    }
    public class Argument
    {
        public string Name;
        public Primitive Type;
        public object Value;
        public bool IsRef;
        public bool IsOut;
        public bool IsOutParam; // CIL `out` parametresi (tip Pointer kalir; IsOut MiniCs sozlesmesidir ve pointer EKLER) - yalniz corelib sembol adi icin
    }

    public class Code
    {
        public Primitive Owner;
        public string SimpleName; // metadata adi: "ctor", "cctor", "finalize", "op_add", "get_X", "ShouldEncode" (acik iface impl'de de yalin ad)
        public bool ByrefNamedMangle; // corelib: C sembolunde byref parametre `out_/ref_` + eleman adi (corelib.c sozlesmesi)
        public string Assembly; // tanimlayan assembly adi (CilFrontend; generic klonlar sablonunkini tasir)
        public Code Template; // generic METHOD somutlamasi ise kaynagi (GenericInstantiator); modul yazici "gomulu generic" karari
        public bool IsExternal;
        public List<Op> Operations = new List<Op>();
        public bool Inline;
        public List<Argument> Arguments = new List<Argument>();
        public bool IsStatic;
        public bool IsVirtual;  // vtable'da yeni slot acar (newslot)
        public bool IsOverride; // parent'ta AD + substitute edilmis PARAMETRE imzasi eslesen slotu ezer (Hierarchy/Signature)
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
        public bool BodyCloned; // monomorphization: ornek govdesi IL'den bir kez cevrildi (serialize edilmez)
        public bool ExactContext; // ModuleWriter exact klonu: IL cevirisi marker'siz, tam exact baglamla (canon host'un ic meselesi)
        // IL kaynagi (CilFrontend.IlRef): sablon ve tum ornekleri AYNI IL govdesini tasir; ornek govdesi klonlanmaz,
        // SOMUT generic baglamla (Owner.TypeArguments + MethodTypeArguments) IL'den yeniden cevrilir (docs/engine-notes.md GENERICS-S1).
        public object IlSource;
        public List<Primitive> MethodTypeArguments; // generic METHOD orneklemesi: sablonun GenericParameters sirasiyla somut argumanlar
        // CANON: exact ref-tip orneklemesinin kabugu govdesizdir; kodu bu canon Code'dadir (List<string>.Add -> List<__Canon>.Add).
        // null = paylasimsiz. Emisyon/Trim/vtable girisleri daima Canonical uzerinden gider.
        public Code Canonical;
        public bool IsSharedExact => Canonical != null && Canonical != this;
        public Code Shared => Canonical ?? this;
        // canon kod: gizli `__dict` parametresi alir mi (K6): static metot, struct uyesi, canon generic metot. Class instance metodu this'ten bulur.
        // Metot argumaninda __Canon OZYINELEMELI aranir (struct icinde ref: Items<ValueTuple<int,__Canon>>) — tip tarafi (IsCanonForm) ile ayni kapi.
        public bool HasMethodDict => Canon.ContainsCanon(MethodTypeArguments); // sozluk sahibi metot (yoksa Owner)
        public bool IsCanonCode => (Owner != null && Owner.IsCanonForm) || HasMethodDict;
        public bool NeedsDictParam => IsCanonCode && !IsExternal && NativeBody == null && (IsStatic || (Owner != null && Owner.IsStruct) || HasMethodDict);
        public ushort CilAttributes; // System.Reflection.MethodAttributes
        public List<PrimitiveAttribute> Attributes = new List<PrimitiveAttribute>(); // custom attribute'lar (reflection tablosu)

        public string EncodeName() => (Owner != null ? Owner.Name + "$" : "") + Name;

        // EMISYON ADI (C sembolu / trace). KIMLIK DEGIL: hicbir arama bununla yapilmaz (Context.CodeKey yapisal).
        // Turetilmis: basit ad + parametre tipleri (+ generic metot argumanlari / arity). Klon argumanlari somut oldugundan
        // Serializer<Encoded>.Encode(Encoded,Codec) -> "Encode_M_Encoded_M_Codec" cikar; owner oneki benzersizligi tamamlar.
        public string Name
        {
            get
            {
                // Klon: sablonun adi (sablon icinde benzersiz -> Ov<int>.Foo(T)/Foo(int) "Foo_T"/"Foo_Int" ayri kalir) + metot argumanlari
                if (Template != null)
                {
                    var baseName = Template.Name;
                    if (MethodTypeArguments != null && MethodTypeArguments.Count > 0 && Template.MethodTypeArguments == null)
                        baseName += "<" + string.Join(",", MethodTypeArguments.Select(a => a.Name)) + ">";
                    return baseName;
                }
                var sb = new System.Text.StringBuilder();
                if (ExplicitInterface != null) sb.Append("__iface_").Append(ExplicitInterface.Name.Replace('.', '_')).Append("__");
                sb.Append(SimpleName);
                for (int i = IsStatic ? 0 : 1; i < Arguments.Count; i++)
                {
                    var t = Arguments[i].Type;
                    sb.Append('_');
                    if (t == null) { sb.Append('?'); continue; }
                    // corelib.c sembol sozlesmesi: byref parametre `out_Int`/`ref_Int` (eleman adi), ham pointer degil
                    if (ByrefNamedMangle && t.Type == PrimitiveType.Pointer) sb.Append(Arguments[i].IsOutParam ? "out_" : "ref_").Append(t.ElementType.Name.Replace('.', '_'));
                    else sb.Append(t.Name.Replace('.', '_'));
                }
                return sb.ToString();
            }
        }
        public int ParamCount => Arguments.Count - (IsStatic ? 0 : 1); // this haric
        public override string ToString()
        {
            return $"Code(Owner={(Owner != null ? Owner.Name : "null")}, Name={Name}, Operations=[{string.Join(", ", Operations)}])";
        }
    }

}
