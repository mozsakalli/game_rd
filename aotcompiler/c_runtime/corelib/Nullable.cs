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
        // .NET: kutusu T kutusudur (CTranspiler.BoxValue); bu override'lar constrained cagri (T? uzerinden) icin.
        public override bool Equals(object other) { return hasValue ? other != null && value.Equals(other) : other == null; }
        public override int GetHashCode() { return hasValue ? value.GetHashCode() : 0; }
        public override string ToString() { return hasValue ? value.ToString() : ""; }
    }

    // .NET statik yuzeyi (System.Nullable). GetUnderlyingType: T? -> T, degilse null. Saf C#: Type extern'leri
    // (IsGenericType/GetGenericTypeDefinition/GetGenericArguments) uzerinden; C tarafina dokunmaz.
    public static class Nullable
    {
        public static Type GetUnderlyingType(Type nullableType)
        {
            if (nullableType == null) throw new ArgumentNullException("nullableType");
            if (!nullableType.IsGenericType || nullableType.IsGenericTypeDefinition) return null;
            return nullableType.GetGenericTypeDefinition() == typeof(Nullable<>) ? nullableType.GetGenericArguments()[0] : null;
        }
    }
}
