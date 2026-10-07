namespace DigitoyEngine.Language
{
    // Derleyicinin/runtime'in tanimak ZORUNDA oldugu corlib adlari - TEK yer.
    // (Roslyn'in System.Object'i hardcode etmesi gibi mesru; ama dagitik degil, burada toplu.)
    public static class WellKnown
    {
        public const string Namespace = "System";
        public const string Object = Namespace + ".Object";
        public const string String = Namespace + ".String";
        public const string ValueType = Namespace + ".ValueType";
        public const string Exception = Namespace + ".Exception";
        public const string Nullable = Namespace + ".Nullable";

        // DIGITOYENGINE_EX_* kind indeksi -> corlib sinifi (vmrt.h ile sozlesmeli; 0 ve 5 kullanilmiyor)
        public static readonly string[] ExceptionKinds =
        {
            null,
            Namespace + ".NullReferenceException",
            Namespace + ".IndexOutOfRangeException",
            Namespace + ".DivideByZeroException",
            Namespace + ".InvalidCastException",
            null,
            Namespace + ".IOException"
        };

        // corelib'te bu FQ adla bildirilen class YENI tip yaratmaz, runtime tipine baglanir
        // (primitive struct'lar dahil: Int32.TryParse gibi static uyeler methodTable'a bu tiple girer)
        public static Primitive RuntimeRootFor(string fullName) =>
            fullName == Object ? Primitive.Object :
            fullName == String ? Primitive.String :
            fullName == ValueType ? Primitive.ValueType :
            fullName == "System.Boolean" ? Primitive.Bool :
            fullName == "System.Void" ? Primitive.Void :
            fullName == "System.Char" ? Primitive.Char :
            fullName == "System.Byte" ? Primitive.Byte :
            fullName == "System.SByte" ? Primitive.SByte :
            fullName == "System.Int16" ? Primitive.Short :
            fullName == "System.UInt16" ? Primitive.UShort :
            fullName == "System.Int32" ? Primitive.Int :
            fullName == "System.UInt32" ? Primitive.UInt :
            fullName == "System.Int64" ? Primitive.Long :
            fullName == "System.IntPtr" ? Primitive.Long :
            fullName == "System.UIntPtr" ? Primitive.ULong :
            fullName == "System.UInt64" ? Primitive.ULong :
            fullName == "System.Single" ? Primitive.Float :
            fullName == "System.Double" ? Primitive.Double :
            null;
    }
}
