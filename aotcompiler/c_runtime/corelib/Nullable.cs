namespace System
{
    struct Nullable<T>
    {
        bool hasValue;
        T value;
        public bool HasValue { get { return hasValue; } }
        public T Value
        {
            get
            {
                if (!hasValue) { throw new InvalidOperationException("Nullable object must have a value."); }
                return value;
            }
        }
    }
}
