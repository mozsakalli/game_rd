namespace System.Buffers
{
    // Basit paylasimli dizi havuzu: 2'nin kuvveti kovalar (16..2^20), kova basina kucuk yigin. Ana-thread, kilit yok.
    // Return edilmeyen dizi sizmaz (GC toplar); clearArray yalniz istenirse. .NET semantigi: Rent(n) >= n uzunluk doner.
    public abstract class ArrayPool<T>
    {
        static ArrayPool<T> shared;
        public static ArrayPool<T> Shared { get { if (shared == null) shared = new SharedArrayPool<T>(); return shared; } }
        public static ArrayPool<T> Create() { return new SharedArrayPool<T>(); }
        public abstract T[] Rent(int minimumLength);
        public abstract void Return(T[] array, bool clearArray = false);
    }

    sealed class SharedArrayPool<T> : ArrayPool<T>
    {
        const int MinShift = 4, Buckets = 17, PerBucket = 8; // 16 .. 2^20
        readonly T[][][] stacks = new T[Buckets][][];
        readonly int[] counts = new int[Buckets];

        static int BucketOf(int len)
        {
            int b = 0, s = 1 << MinShift;
            while (s < len && b < Buckets - 1) { s <<= 1; b++; }
            return s >= len ? b : -1;
        }

        public override T[] Rent(int minimumLength)
        {
            if (minimumLength < 0) throw new ArgumentOutOfRangeException("minimumLength");
            if (minimumLength == 0) return Array.Empty<T>();
            int b = BucketOf(minimumLength);
            if (b < 0) return new T[minimumLength];
            if (counts[b] > 0) { var a = stacks[b][--counts[b]]; stacks[b][counts[b]] = null; return a; }
            return new T[1 << (MinShift + b)];
        }

        public override void Return(T[] array, bool clearArray = false)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (array.Length == 0) return;
            int b = BucketOf(array.Length);
            if (b < 0 || array.Length != 1 << (MinShift + b)) return; // havuzdan gelmemis boyut: birak
            if (clearArray) Array.Clear(array, 0, array.Length);
            if (stacks[b] == null) stacks[b] = new T[PerBucket][];
            if (counts[b] < PerBucket) stacks[b][counts[b]++] = array;
        }
    }
}
