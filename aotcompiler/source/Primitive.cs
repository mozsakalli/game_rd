namespace DigitoyEngine.Language
{
    using System.Collections.Generic;

    public enum PrimitiveType
    {
        Byte,
        Bool,
        Short,
        Char,
        Int,
        Float,
        Double,
        Long,
        SByte,
        UShort,
        UInt,
        ULong,
        Array,
        FixedArray,
        Pointer,
        Void,
        Model
    }

    public class PrimitiveField
    {
        public Primitive Owner;
        public string Name;
        public Primitive Type;
        public bool IsStatic;
        public bool IsReadonly; // yalniz kendi sinifinin ctor/cctor'unda yazilabilir
        public int Index; // slot içindeki konum, runtime alan erişimi bunu kullanır
        public ushort CilAttributes; // System.Reflection.FieldAttributes (reflection: IsPublic/IsNotSerialized...)
        public List<PrimitiveAttribute> Attributes = new List<PrimitiveAttribute>();
    }

    // Custom attribute kaydi (CIL'den): tip + ctor + sabit argumanlar (string/int/bool/float/double/long/enum-int/Primitive=typeof)
    // + named argumanlar (alan ya da property). Transpiler lazy kurucu fonksiyon uretir (docs/registry-removal.md Faz 4b).
    public class PrimitiveAttribute
    {
        public Primitive Type;
        public Code Ctor;
        public List<object> FixedArgs = new List<object>();
        public List<(string Name, object Value, bool IsField)> NamedArgs = new List<(string, object, bool)>();
    }

    // Property desugar edildikten sonra metadata olarak kalır; C transpiler get_/set_ kodlarından
    // AOT reflection tablosunu üretir. Indexer'lar bu listede yer almaz.
    public class PrimitiveProperty
    {
        public string Name;
        public Primitive Type;
        public bool IsStatic;
        public bool HasGet;
        public bool HasSet;
        public Code Getter; // get_X / set_X Code'u (CilFrontend baglar; reflection erisimcisi isimle aranmaz)
        public Code Setter;
        public ushort CilAttributes;
        public List<PrimitiveAttribute> Attributes = new List<PrimitiveAttribute>();
    }

    public class Primitive
    {
        public static Primitive Bool = new Primitive { Name = "Bool", Type = PrimitiveType.Bool };
        public static Primitive Int = new Primitive { Name = "Int", Type = PrimitiveType.Int };
        public static Primitive Byte = new Primitive { Name = "Byte", Type = PrimitiveType.Byte };
        public static Primitive Short = new Primitive { Name = "Short", Type = PrimitiveType.Short };
        public static Primitive Char = new Primitive { Name = "Char", Type = PrimitiveType.Char };
        public static Primitive Float = new Primitive { Name = "Float", Type = PrimitiveType.Float };
        public static Primitive Double = new Primitive { Name = "Double", Type = PrimitiveType.Double };
        public static Primitive Long = new Primitive { Name = "Long", Type = PrimitiveType.Long };
        public static Primitive SByte = new Primitive { Name = "SByte", Type = PrimitiveType.SByte };
        public static Primitive UShort = new Primitive { Name = "UShort", Type = PrimitiveType.UShort };
        public static Primitive UInt = new Primitive { Name = "UInt", Type = PrimitiveType.UInt };
        public static Primitive ULong = new Primitive { Name = "ULong", Type = PrimitiveType.ULong };

        // C# binary numeric promotion (ECMA 12.4.7): iki taraf da ayni kurala uyar (front-end tip verir,
        // CTranspiler C ifadesinde operandlari bu tipe CAST eder -> C'nin kendi terfisi devre disi kalir)
        public static Primitive PromoteNumeric(Primitive a, Primitive b)
        {
            if (a.Type == PrimitiveType.Double || b.Type == PrimitiveType.Double) return Double;
            if (a.Type == PrimitiveType.Float || b.Type == PrimitiveType.Float) return Float;
            if (a.Type == PrimitiveType.ULong || b.Type == PrimitiveType.ULong) return ULong; // isaretli karisim ApplyBin'de engellenir
            if (a.Type == PrimitiveType.Long || b.Type == PrimitiveType.Long) return Long;
            bool au = a.Type == PrimitiveType.UInt, bu = b.Type == PrimitiveType.UInt;
            if (au && bu) return UInt;
            // IL yiginda int32/uint32 ayrimi YOK (Roslyn int<->uint icin conv emit etmez); C# kaynaginda long gereken `uint+int`
            // zaten acik conv.i8/conv.u8 ile gelir. Karisim -> 32-bit islem; isaret yalniz .un opcode'larinda (frontend Conv ile verir).
            if (au) return IsSignedSmall(b) ? Int : UInt;
            if (bu) return IsSignedSmall(a) ? Int : UInt;
            return Int;
        }

        static bool IsSignedSmall(Primitive t) =>
            t.Type == PrimitiveType.Int || t.Type == PrimitiveType.Short || t.Type == PrimitiveType.SByte;

        // C# sozdizimli tip gosterimi (trace/reflection): keyword'ler, dizi ekleri, dotted adlar
        public static string CsDisplay(Primitive t)
        {
            switch (t.Type)
            {
                case PrimitiveType.Int: return "int";
                case PrimitiveType.UInt: return "uint";
                case PrimitiveType.Long: return "long";
                case PrimitiveType.ULong: return "ulong";
                case PrimitiveType.Short: return "short";
                case PrimitiveType.UShort: return "ushort";
                case PrimitiveType.Byte: return "byte";
                case PrimitiveType.SByte: return "sbyte";
                case PrimitiveType.Char: return "char";
                case PrimitiveType.Bool: return "bool";
                case PrimitiveType.Float: return "float";
                case PrimitiveType.Double: return "double";
                case PrimitiveType.Void: return "void";
                case PrimitiveType.Array: return CsDisplay(t.ElementType) + "[" + new string(',', t.ArrayRank - 1) + "]";
                case PrimitiveType.FixedArray: return CsDisplay(t.ElementType) + "[" + t.FixedSize + "]";
                case PrimitiveType.Pointer: return CsDisplay(t.ElementType) + "*";
                default:
                    if (t == Object) return "object";
                    if (t == String) return "string";
                    return t.Display;
            }
        }
        // T[] KANONIK tip: (eleman, rank) basina TEK Primitive (.NET MethodTable / IL2CPP array class gibi birinci sinif).
        // Tum dizi tipi uretimi buradan gecer (frontend, Resolver, GenericInstantiator); emisyon InternedArrays'i gezer.
        static readonly Dictionary<(Primitive, int), Primitive> arrayIntern = new Dictionary<(Primitive, int), Primitive>();
        static readonly List<Primitive> arrayList = new List<Primitive>();
        public static IReadOnlyList<Primitive> InternedArrays => arrayList;
        public static Primitive ArrayOf(Primitive elementType, int rank = 1)
        {
            if (rank < 1) throw new System.ArgumentOutOfRangeException(nameof(rank));
            if (elementType == null) throw new System.ArgumentNullException(nameof(elementType));
            if (arrayIntern.TryGetValue((elementType, rank), out var a)) return a;
            a = new Primitive
            {
                Name = rank == 1 ? $"[{elementType.Name}" : $"[{rank}:{elementType.Name}",
                DisplayName = CsDisplay(elementType) + "[" + new string(',', rank - 1) + "]",
                Type = PrimitiveType.Array,
                ElementType = elementType,
                ArrayRank = rank,
            };
            arrayIntern[(elementType, rank)] = a;
            arrayList.Add(a);
            return a;
        }
        // FixedArray/Pointer de dizi gibi KANONIK: (eleman[, boyut]) basina tek Primitive -> tip esitligi her yerde referansla.
        static readonly Dictionary<(Primitive, int), Primitive> fixedIntern = new Dictionary<(Primitive, int), Primitive>();
        static readonly Dictionary<Primitive, Primitive> pointerIntern = new Dictionary<Primitive, Primitive>();
        public static Primitive FixedArrayOf(Primitive elementType, int size)
        {
            if (fixedIntern.TryGetValue((elementType, size), out var f)) return f;
            return fixedIntern[(elementType, size)] = new Primitive { Name = $"]{elementType.Name}:{size}", Type = PrimitiveType.FixedArray, ElementType = elementType, FixedSize = size };
        }
        // ham C pointer'i (*Float -> float*): GC'ye HIC girmez (mark/trace yok), atomic sayilir.
        // Dis dunyayla (native API, buffer) konusmak icin; sahiplik/omur programciya ait. IL byref (ref/out) de bu tiptir.
        public static Primitive PointerOf(Primitive elementType)
        {
            if (pointerIntern.TryGetValue(elementType, out var p)) return p;
            return pointerIntern[elementType] = new Primitive { Name = $"*{elementType.Name}", Type = PrimitiveType.Pointer, ElementType = elementType };
        }
        public static Primitive Void = new Primitive { Name = "Void", Type = PrimitiveType.Void };
        // System.Object koku: base'siz her class'in ortuk parent'i (C# gibi). C karsiligi runtime'daki
        // VmObject/vmobject_type (header-only) - transpiler struct/Type/ctor URETMEZ, ona baglar.
        public static Primitive Object = new Primitive { Name = WellKnown.Object, Type = PrimitiveType.Model };
        // System.String: degismez UTF-16 (C: VmString/vmstring_type; VM: .NET string degeri).
        // Yuzeyi corelib/String.cs tanimlar; transpiler struct/Type/ctor URETMEZ.
        public static Primitive String = new Primitive { Name = WellKnown.String, Type = PrimitiveType.Model, Parent = Object };
        public static Primitive ValueType = new Primitive { Name = WellKnown.ValueType, Type = PrimitiveType.Model, Parent = Object };
        // CANON (docs/engine-notes.md CANON GENERICS): ref-tip generic argumanlarinin ortak temsilcisi (.NET __Canon).
        // Model class, Object'ten turer, C'de GCHeader*. Runtime tipi DEGILDIR: descriptor'u yok, typeof/new/cast'te
        // asla sembol olarak gorunmez (emisyon assert) — exact tip her zaman sozlukten (dict) gelir.
        public static Primitive Canon = new Primitive { Name = "__Canon", Type = PrimitiveType.Model, Parent = Object, IsCanon = true };
        // CANON LOWERING yigin degerleri (Lower.cs): GC disi ham isaretciler; yalniz Ld*/..Rt op'lari arasinda yasar.
        public static Primitive TypeHandle = new Primitive { Name = "__TypeHandle", Type = PrimitiveType.Pointer }; // const Type*
        public static Primitive DictHandle = new Primitive { Name = "__DictHandle", Type = PrimitiveType.Pointer }; // const void *const *
        public static Primitive FnPtrHandle = new Primitive { Name = "__FnPtrHandle", Type = PrimitiveType.Pointer }; // const void*
        public bool IsCanon;          // __Canon ya da marker
        public int CanonIndex = -1;   // marker: sahibin kacinci generic argumani icinde (-1 = __Canon'un kendisi)
        public int[] CanonPath;       // marker: arguman icindeki yol (TypeArguments indeksleri; -1 = ElementType)
        public bool CanonIsMethod;    // marker: metot generic argumani (true) / tip argumani (false)
        public bool IsCanonMarker => IsCanon && CanonIndex >= 0;
        // Paylasim: exact ref-tip orneklemesinin (List<string>) kod/yerlesim sahibi canon orneklemesi (List<__Canon>).
        // null = paylasimsiz (value-type argumanli ya da generic degil) ya da canon'un kendisi.
        public Primitive Canonical;
        public bool IsSharedExact => Canonical != null && Canonical != this;
        public bool IsCanonForm; // bu ornek bir canon ornegidir (argumanlarinda __Canon var; kod burada, descriptor yok)

        public static Primitive IntArray = ArrayOf(Int);
        public static Primitive ByteArray = ArrayOf(Byte);
        public static Primitive ShortArray = ArrayOf(Short);
        public static Primitive CharArray = ArrayOf(Char);
        public static Primitive FloatArray = ArrayOf(Float);
        public static Primitive DoubleArray = ArrayOf(Double);
        public static Primitive LongArray = ArrayOf(Long);
        public string Name;
        public string Assembly; // tanimlayan assembly adi (CilFrontend; generic klonlar sablonunkini tasir). Modul publish kapsam karari (docs/modules.md)
        public string DisplayName; // C# sozdizimli gosterim (generic instance: "Box<int>"); null = Name
        public string Display => DisplayName ?? Name;
        public bool IsStruct;
        public bool IsEnum; // alt tip int; uyeler EnumMembers'ta (C tarafinda duz int)
        public bool IsDelegate; // referans tipi: [GCHeader][fn][target] (VmDelegate); multicast YOK (bilincli sinir)
        public Primitive DelegateReturn;
        public List<Primitive> DelegateParams = new List<Primitive>();
        public Dictionary<string, int> EnumMembers; // yalniz IsEnum'da
        public Dictionary<string, (Primitive type, object value)> Constants; // const alanlar (depolama yok, derleme zamani)
        public bool IsInterface; // Model alt turu: alan yok, instantiate edilemez, yalniz method sozlesmesi
        public List<Primitive> Interfaces = new List<Primitive>(); // implement edilen interface'ler (yalniz kendi bildirdikleri; parent'inkiler zincirden)
        public Dictionary<Primitive, List<Code>> ITables = new Dictionary<Primitive, List<Code>>(); // Hierarchy.Build doldurur (iface -> slot sirali impl)
        public bool IsArray => Type == PrimitiveType.Array || Type == PrimitiveType.FixedArray;
        public bool IsGeneric => GenericParameters.Count > 0;
        public PrimitiveType Type;
        public int ArrayRank = 1; // PrimitiveType.Array icin: []=1, [,]=2; Name kimlige dahil edilir.
        public List<Primitive> GenericParameters = new List<Primitive>();
        public Primitive ElementType;
        public Primitive Parent;
        public List<PrimitiveField> Fields = new List<PrimitiveField>();
        public List<PrimitiveField> StaticFields = new List<PrimitiveField>(); // ayri depo (instance layout'a girmez)
        public List<PrimitiveProperty> Properties = new List<PrimitiveProperty>(); // source property metadata; get_/set_ methodlardan ayri
        public uint CilAttributes; // System.Reflection.TypeAttributes (IsSerializable/IsPublic...)
        public List<PrimitiveAttribute> Attributes = new List<PrimitiveAttribute>();
        public object[] StaticStorage; // VM runtime: GetStatic/SetStatic deposu (lazy, VM.EnsureStatics)
        public int FixedSize;
        public bool IsGenericParameter;
        public List<Code> VTable = new List<Code>(); // Hierarchy.Build doldurur (serialize edilmez, tureyen bilgi)
        public Primitive GenericTemplate; // != null ise bu node ACIK/henuz somutlanmamis "TypeArguments ile Template'e uygulama"dir (Context.Apply intern'ler)
        public Primitive InstantiatedFrom; // somutlastirilmis ornegin sablonu (reflection: GetGenericTypeDefinition); uygulama dugumunde null
        public List<Primitive> TypeArguments = new List<Primitive>();
        // Generic tanim: uygulama dugumu ya da somut ornek icin sablon, aksi halde kendisi. "(x.GenericTemplate ?? x)" kalibinin tek dogru hali.
        public Primitive Definition => GenericTemplate ?? InstantiatedFrom ?? this;
        public bool IsGenericInstance => InstantiatedFrom != null;

        public void AddField(PrimitiveField field)
        {
            field.Owner = this;
            var list = field.IsStatic ? StaticFields : Fields; // Index kendi listesi icinde
            field.Index = list.Count;
            list.Add(field);
        }
    }

}
