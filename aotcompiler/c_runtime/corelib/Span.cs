// corelib: Span<T>/ReadOnlySpan<T> — AOT INTRINSIC. Temsil: {ham adres, uzunluk}. GC nesne tasimaz ve
// yalniz managed frame yokken kosar; span'lar kisa omurlu stack degerleridir -> dizi/dizgi icine ham
// isaretci guvenlidir (fixed/pinning ile ayni gerekce).
// MiniCs'te pointer/ref-donus olmadigi icin su uyeler CilFrontend (SpanIntrinsics) tarafindan op dizisine
// acilir: get_Item (eleman ADRESI), .ctor(void*,int), .ctor(T[]), .ctor(T[],int,int), op_Implicit(T[]),
// op_Implicit(Span->ReadOnlySpan), Slice, CopyTo, ToArray, MemoryExtensions.AsSpan(T[]...), string.AsSpan(...).
// Burada yalniz veri + duz C# uyeler durur.
namespace System
{
    struct Span<T>
    {
        long _ptr;
        int _len;
        public Span(long ptr, int length) { _ptr = ptr; _len = length; }
        public int Length { get { return _len; } }
        public bool IsEmpty { get { return _len == 0; } }
        public static Span<T> Empty { get { return new Span<T>(0, 0); } }
        internal long Ptr { get { return _ptr; } }
    }

    struct ReadOnlySpan<T>
    {
        long _ptr;
        int _len;
        public ReadOnlySpan(long ptr, int length) { _ptr = ptr; _len = length; }
        public int Length { get { return _len; } }
        public bool IsEmpty { get { return _len == 0; } }
        public static ReadOnlySpan<T> Empty { get { return new ReadOnlySpan<T>(0, 0); } }
        internal long Ptr { get { return _ptr; } }
    }

    // Frontend'in urettigi op dizilerinin yardimcilari (byte-tabanli ham bellek).
    static class SpanOps
    {
        extern public static void Copy(long dst, long src, int bytes);   // memmove (CopyTo)
        extern public static void Fill(long dst, int bytes, byte value); // memset (Clear)
    }

    static class MemoryExtensions { } // AsSpan uyeleri frontend intrinsic'i (imza eslesmesi icin tip var olmali)
    static class Buffer { }           // MemoryCopy(void*,void*,long,long) frontend intrinsic'i -> SpanOps.Copy
}
