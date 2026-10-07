// corelib: System.Nullable<T> — yerlesim SOZLESMESI: {hasValue, value} (CTranspiler NullableValueType
// `(struct X){1, v}` ile dogrudan kurar; alan sirasi DEGISMEZ). .NET yuzeyi: ctor, HasValue/Value,
// GetValueOrDefault, T <-> T? donusum operatorleri (Roslyn lifted operator/?. lowering bunlari cagirir).
namespace System
{
    public struct Nullable<T> where T : struct
    {
        public bool hasValue;
        public T value;
        public Nullable(T value) { hasValue = true; this.value = value; }
        public bool HasValue { get { return hasValue; } }
        public T Value
        {
            get
            {
                if (!hasValue) { throw new InvalidOperationException("Nullable object must have a value."); }
                return value;
            }
        }
        public T GetValueOrDefault() { return value; }
        public T GetValueOrDefault(T defaultValue) { return hasValue ? value : defaultValue; }
        public static implicit operator Nullable<T>(T value) { return new Nullable<T>(value); }
        public static explicit operator T(Nullable<T> value) { return value.Value; }
    }
}
