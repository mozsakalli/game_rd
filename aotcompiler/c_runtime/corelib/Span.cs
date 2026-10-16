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
        public void Clear() { Fill(default(T)); }
        public void Fill(T value) { for (int i = 0; i < _len; i++) this[i] = value; } // saf C#: indexer intrinsic'i uzerinden
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

    // .NET ile ayni yuzey: dizi + ofset + sayi (saf C#, intrinsic degil). Common.dll Codec.GetBuffer bunu doner.
    public struct ArraySegment<T>
    {
        readonly T[] _array; readonly int _offset, _count;
        public ArraySegment(T[] array)
        {
            if (array == null) throw new ArgumentNullException("array");
            _array = array; _offset = 0; _count = array.Length;
        }
        public ArraySegment(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException("array");
            if (offset < 0 || count < 0 || offset + count > array.Length) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            _array = array; _offset = offset; _count = count;
        }
        public static ArraySegment<T> Empty { get { return new ArraySegment<T>(new T[0]); } }
        public T[] Array { get { return _array; } }
        public int Offset { get { return _offset; } }
        public int Count { get { return _count; } }
        public T this[int index]
        {
            get { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException("index"); return _array[_offset + index]; }
            set { if ((uint)index >= (uint)_count) throw new ArgumentOutOfRangeException("index"); _array[_offset + index] = value; }
        }
        public T[] ToArray()
        {
            var r = new T[_count];
            for (int i = 0; i < _count; i++) r[i] = _array[_offset + i];
            return r;
        }
        public ArraySegment<T> Slice(int index) { return Slice(index, _count - index); }
        public ArraySegment<T> Slice(int index, int count)
        {
            if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index)) throw new ArgumentOutOfRangeException("index");
            return new ArraySegment<T>(_array, _offset + index, count);
        }
        public static implicit operator ArraySegment<T>(T[] array) { return array == null ? default(ArraySegment<T>) : new ArraySegment<T>(array); }
    }

    // MemoryCopy(void*,void*,long,long) frontend intrinsic'i -> SpanOps.Copy
    public static class Buffer
    {
        public static extern unsafe void MemoryCopy(void* source, void* destination, long destinationSizeInBytes, long sourceBytesToCopy);
        // Yalniz primitive dizileri (.NET de bunu ister); bayt ofsetleri. Ayni dizi icinde ortusme guvenli (Array.Copy semantigi).
        public static void BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
        {
            if (src == null) throw new ArgumentNullException("src");
            if (dst == null) throw new ArgumentNullException("dst");
            if (srcOffset < 0 || dstOffset < 0 || count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            if (src is byte[] sb && dst is byte[] db)
            {
                if (srcOffset + count > sb.Length || dstOffset + count > db.Length) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
                Array.Copy(sb, srcOffset, db, dstOffset, count);
                return;
            }
            throw new NotSupportedException("Buffer.BlockCopy: only byte[] arrays are supported in corelib.");
        }
    }
}
