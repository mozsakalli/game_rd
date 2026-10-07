// corelib: Span<T>/ReadOnlySpan<T> — AOT INTRINSIC. Temsil: {ham adres, uzunluk}. GC nesne tasimaz ve
// yalniz managed frame yokken kosar; span'lar kisa omurlu stack degerleridir -> dizi/dizgi icine ham
// isaretci guvenlidir (fixed/pinning ile ayni gerekce).
// Asagidaki `extern` uyeler CilFrontend (SpanIntrinsics) tarafindan op dizisine acilir (C govdesi YOK):
// get_Item (eleman ADRESI), ctor(void*,int), ctor(T[]), ctor(T[],int,int), op_Implicit(T[]),
// op_Implicit(Span->ReadOnlySpan), Slice, CopyTo, ToArray, MemoryExtensions.AsSpan, Buffer.MemoryCopy.
// Bildirimler Roslyn icin (motor kodu bunlara karsi derlenir); Enumerator saf C# (indexer intrinsic'i uzerinden).
namespace System
{
    public readonly ref struct Span<T>
    {
        readonly long _ptr;
        readonly int _len;
        public Span(long ptr, int length) { _ptr = ptr; _len = length; }
        public extern unsafe Span(void* pointer, int length);
        public extern Span(T[] array);
        public extern Span(T[] array, int start, int length);
        public int Length { get { return _len; } }
        public bool IsEmpty { get { return _len == 0; } }
        public static Span<T> Empty { get { return new Span<T>(0, 0); } }
        internal long Ptr { get { return _ptr; } }
        public extern ref T this[int index] { get; }
        public extern Span<T> Slice(int start);
        public extern Span<T> Slice(int start, int length);
        public extern void CopyTo(Span<T> destination);
        public extern T[] ToArray();
        public extern void Clear();
        public extern void Fill(T value);
        public static extern implicit operator Span<T>(T[] array);
        public static extern implicit operator ReadOnlySpan<T>(Span<T> span);
        public Enumerator GetEnumerator() { return new Enumerator(this); }

        public ref struct Enumerator
        {
            readonly Span<T> _span;
            int _index;
            public Enumerator(Span<T> span) { _span = span; _index = -1; }
            public bool MoveNext() { int i = _index + 1; if (i < _span.Length) { _index = i; return true; } return false; }
            public ref T Current { get { return ref _span[_index]; } }
        }
    }

    public readonly ref struct ReadOnlySpan<T>
    {
        readonly long _ptr;
        readonly int _len;
        public ReadOnlySpan(long ptr, int length) { _ptr = ptr; _len = length; }
        public extern unsafe ReadOnlySpan(void* pointer, int length);
        public extern ReadOnlySpan(T[] array);
        public extern ReadOnlySpan(T[] array, int start, int length);
        public int Length { get { return _len; } }
        public bool IsEmpty { get { return _len == 0; } }
        public static ReadOnlySpan<T> Empty { get { return new ReadOnlySpan<T>(0, 0); } }
        internal long Ptr { get { return _ptr; } }
        public extern ref readonly T this[int index] { get; }
        public extern ReadOnlySpan<T> Slice(int start);
        public extern ReadOnlySpan<T> Slice(int start, int length);
        public extern void CopyTo(Span<T> destination);
        public extern T[] ToArray();
        public static extern implicit operator ReadOnlySpan<T>(T[] array);
        public Enumerator GetEnumerator() { return new Enumerator(this); }

        public ref struct Enumerator
        {
            readonly ReadOnlySpan<T> _span;
            int _index;
            public Enumerator(ReadOnlySpan<T> span) { _span = span; _index = -1; }
            public bool MoveNext() { int i = _index + 1; if (i < _span.Length) { _index = i; return true; } return false; }
            public ref readonly T Current { get { return ref _span[_index]; } }
        }
    }

    // Frontend'in urettigi op dizilerinin yardimcilari (byte-tabanli ham bellek).
    public static class SpanOps
    {
        extern public static void Copy(long dst, long src, int bytes);   // memmove (CopyTo)
        extern public static void Fill(long dst, int bytes, byte value); // memset (Clear)
        extern public static void StoreInt16(long dst, short value);     // BitConverter.TryWriteBytes (LE, hizasiz)
        extern public static void StoreInt32(long dst, int value);
    }

    // AsSpan uyeleri frontend intrinsic'i: bildirim Roslyn icin.
    public static class MemoryExtensions
    {
        public static extern Span<T> AsSpan<T>(this T[] array);
        public static extern Span<T> AsSpan<T>(this T[] array, int start);
        public static extern Span<T> AsSpan<T>(this T[] array, int start, int length);
        public static extern ReadOnlySpan<char> AsSpan(this string text);
        public static extern ReadOnlySpan<char> AsSpan(this string text, int start);
        public static extern ReadOnlySpan<char> AsSpan(this string text, int start, int length);
        public static bool SequenceEqual<T>(this ReadOnlySpan<T> a, ReadOnlySpan<T> b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!Object.Equals(a[i], b[i])) return false;
            return true;
        }
        public static bool SequenceEqual<T>(this Span<T> a, ReadOnlySpan<T> b) { return SequenceEqual((ReadOnlySpan<T>)a, b); }
    }

    // MemoryCopy(void*,void*,long,long) frontend intrinsic'i -> SpanOps.Copy
    public static class Buffer
    {
        public static extern unsafe void MemoryCopy(void* source, void* destination, long destinationSizeInBytes, long sourceBytesToCopy);
    }
}
