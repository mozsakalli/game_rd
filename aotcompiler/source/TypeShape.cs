namespace DigitoyEngine.Language
{
    // Tip sekli: C ABI / GC / kutulama kararlarinin TEK kapisi (docs/engine-notes.md GENERICS-S2).
    // Emisyon ve frontend "struct mu, ref mi, skaler mi" sorusunu burada sorar; ad hoc IsStruct/IsNumericVal
    // kombinasyonlari yazilmaz. Yeni bir kategori eklendiginde (orn. Nullable ozel yolu) yalniz burasi degisir.
    public enum Shape
    {
        Void,
        Scalar,       // sayisal/bool/char + ENUM (Primitive.Type = Int, IsEnum): C skaleri, kutulama helper'la
        Struct,       // Model+IsStruct: C deger tipi (GCHeader yok, kopyalanir), kutusu [GCHeader][struct]
        Class,        // Model class (Object/String dahil): GC pointer
        Interface,    // Model iface: GC pointer, dispatch itable
        Delegate,     // VmDelegate*: GC pointer
        Array,        // VmArray*: GC pointer
        FixedArray,   // fixed T[N]: struct icine gomulu skaler dizi
        Pointer,      // ham T*: GC disi
        GenericParam, // SOMUTLASMAMIS T: emisyonda gorulmesi HATA (S1 sonrasi hic olusmaz)
    }

    public static class TypeShape
    {
        public static Shape Of(Primitive t)
        {
            if (t == null || t.Type == PrimitiveType.Void) return Shape.Void;
            if (t.IsCanon) return Shape.Class; // __Canon ve marker'lar: GC pointer (ref-tip temsilcisi)
            if (t.IsGenericParameter) return Shape.GenericParam;
            switch (t.Type)
            {
                case PrimitiveType.Array: return Shape.Array;
                case PrimitiveType.FixedArray: return Shape.FixedArray;
                case PrimitiveType.Pointer: return Shape.Pointer;
                case PrimitiveType.Model:
                    if (t.IsStruct) return Shape.Struct;
                    if (t.IsInterface) return Shape.Interface;
                    if (t.IsDelegate) return Shape.Delegate;
                    return Shape.Class;
                default: return Shape.Scalar; // Int/UInt/Long/.../Bool/Char/Float/Double (+enum)
            }
        }

        // C'de DEGERLE tasinan: skaler/enum/struct. Kutulama gerektirir, null olamaz, default = sifir blok.
        public static bool IsValueLike(Primitive t) { var s = Of(t); return s == Shape.Scalar || s == Shape.Struct; }
        // GC'nin gordugu referans: class/iface/delegate/dizi (trace, write barrier, kok).
        public static bool IsGcRef(Primitive t) { var s = Of(t); return s == Shape.Class || s == Shape.Interface || s == Shape.Delegate || s == Shape.Array; }
        // Model struct (C deger tipi). Model class/iface/delegate = GC pointer'li model.
        public static bool IsStructModel(Primitive t) => Of(t) == Shape.Struct;
        public static bool IsClassModel(Primitive t) { var s = Of(t); return s == Shape.Class || s == Shape.Interface || s == Shape.Delegate; }
        // Skaler (enum dahil): C aritmetik tipi, box helper'i var.
        public static bool IsScalar(Primitive t) => Of(t) == Shape.Scalar;
    }
}
