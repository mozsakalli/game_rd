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
            if (au) return IsSignedSmall(b) ? Long : UInt; // uint+int -> long (C#); uint+byte -> uint
            if (bu) return IsSignedSmall(a) ? Long : UInt;
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
        public static Primitive ArrayOf(Primitive elementType, int rank = 1)
        {
            if (rank < 1) throw new System.ArgumentOutOfRangeException(nameof(rank));
            return new Primitive { Name = rank == 1 ? $"[{elementType.Name}" : $"[{rank}:{elementType.Name}", Type = PrimitiveType.Array, ElementType = elementType, ArrayRank = rank };
        }
        public static Primitive FixedArrayOf(Primitive elementType, int size) => new Primitive { Name = $"]{elementType.Name}:{size}", Type = PrimitiveType.FixedArray, ElementType = elementType, FixedSize = size };
        // ham C pointer'i (*Float -> float*): GC'ye HIC girmez (mark/trace yok), atomic sayilir.
        // Dis dunyayla (native API, buffer) konusmak icin; sahiplik/omur programciya ait.
        public static Primitive PointerOf(Primitive elementType) => new Primitive { Name = $"*{elementType.Name}", Type = PrimitiveType.Pointer, ElementType = elementType };
        public static Primitive Void = new Primitive { Name = "Void", Type = PrimitiveType.Void };
        // System.Object koku: base'siz her class'in ortuk parent'i (C# gibi). C karsiligi runtime'daki
        // VmObject/vmobject_type (header-only) - transpiler struct/Type/ctor URETMEZ, ona baglar.
        public static Primitive Object = new Primitive { Name = WellKnown.Object, Type = PrimitiveType.Model };
        // System.String: degismez UTF-16 (C: VmString/vmstring_type; VM: .NET string degeri).
        // Yuzeyi corelib/String.cs tanimlar; transpiler struct/Type/ctor URETMEZ.
        public static Primitive String = new Primitive { Name = WellKnown.String, Type = PrimitiveType.Model, Parent = Object };
        public static Primitive ValueType = new Primitive { Name = WellKnown.ValueType, Type = PrimitiveType.Model, Parent = Object };

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
        public object[] StaticStorage; // VM runtime: GetStatic/SetStatic deposu (lazy, VM.EnsureStatics)
        public int FixedSize;
        public bool Unresolved;
        public bool IsGenericParameter;
        public List<Code> VTable = new List<Code>(); // Hierarchy.Build doldurur (serialize edilmez, tureyen bilgi)
        public Primitive GenericTemplate; // != null ise bu node "TypeArguments ile Template'e uygulama" ifadesidir (orn. Box<T>)
        public List<Primitive> TypeArguments = new List<Primitive>();

        // Apply node adi MANGLED tutulur ("Box<Int>"): ayni uygulamalar isimle esitlenebilir,
        // somutlastirma sonrasi gercek primitive ile ayni ada cozulur.
        public static Primitive Apply(Primitive template, params Primitive[] typeArgs) =>
            new Primitive
            {
                Name = GenericInstantiator.MangleName(template.Name, new List<Primitive>(typeArgs)),
                Type = template.Type,
                IsStruct = template.IsStruct,
                IsInterface = template.IsInterface,
                IsDelegate = template.IsDelegate,
                GenericTemplate = template,
                TypeArguments = new List<Primitive>(typeArgs)
            };

        public void AddField(PrimitiveField field)
        {
            field.Owner = this;
            var list = field.IsStatic ? StaticFields : Fields; // Index kendi listesi icinde
            field.Index = list.Count;
            list.Add(field);
        }
        public string Encode()
        {
            if (GenericTemplate != null)
                throw new System.Exception($"Generic 'Apply' node ({Name}) encode edilemez, sadece template ya da somutlastirilmis primitive serialize edilebilir");
            var w = new TextWriter();
            w.Str(Name);
            w.Line(IsStruct);
            w.Line(Type);
            w.Str(ElementType != null ? ElementType.Name : null);
            w.Str(Parent != null ? Parent.Name : null);
            w.Line(FixedSize);
            w.Line(IsGenericParameter);
            w.Line(Fields.Count + StaticFields.Count);
            foreach (var field in System.Linq.Enumerable.Concat(Fields, StaticFields))
            {
                if (field.Type != null && field.Type.GenericTemplate != null)
                    throw new System.Exception($"Alan '{field.Name}' generic Apply node tipinde ({field.Type.Name}), encode edilemez");
                w.Str(field.Name);
                w.Str(field.Type != null ? field.Type.Name : null);
                w.Line(field.IsStatic);
            }
            w.Line(GenericParameters.Count);
            foreach (var genericParameter in GenericParameters)
                w.Str(genericParameter.Name);
            w.Line(IsInterface);
            w.Line(Interfaces.Count);
            foreach (var iface in Interfaces)
                w.Str(iface.Name);
            return w.ToString();
        }

        public static Primitive Decode(string encoded)
        {
            var r = new TextReader(encoded);
            var primitive = new Primitive();
            primitive.Name = r.Str();
            if (primitive.Name.StartsWith("[") && primitive.Name.IndexOf(':') > 1 && int.TryParse(primitive.Name.Substring(1, primitive.Name.IndexOf(':') - 1), out var rank)) primitive.ArrayRank = rank;
            primitive.IsStruct = r.Bool();
            primitive.Type = (PrimitiveType)System.Enum.Parse(typeof(PrimitiveType), r.Line());
            var elementTypeName = r.Str();
            primitive.ElementType = elementTypeName != null ? new Primitive { Name = elementTypeName, Unresolved = true } : null;
            var parentName = r.Str();
            primitive.Parent = parentName != null ? new Primitive { Name = parentName, Unresolved = true } : null;
            primitive.FixedSize = r.Int();
            primitive.IsGenericParameter = r.Bool();
            var fieldsCount = r.Int();
            for (int i = 0; i < fieldsCount; i++)
            {
                var fieldName = r.Str();
                var fieldTypeName = r.Str();
                var isStatic = r.Bool();
                primitive.AddField(new PrimitiveField { Name = fieldName, Type = fieldTypeName != null ? new Primitive { Name = fieldTypeName, Unresolved = true } : null, IsStatic = isStatic });
            }
            var genericParametersCount = r.Int();
            for (int i = 0; i < genericParametersCount; i++)
                primitive.GenericParameters.Add(new Primitive { Name = r.Str(), IsGenericParameter = true });
            primitive.IsInterface = r.Bool();
            var interfaceCount = r.Int();
            for (int i = 0; i < interfaceCount; i++)
                primitive.Interfaces.Add(new Primitive { Name = r.Str(), Unresolved = true });
            return primitive;
        }
    }

}