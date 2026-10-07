namespace System
{
    // IntPtr = nint (RuntimeFeature.NumericIntPtr): donusumler conv.i; Zero okumasini frontend sabit 0'a cevirir.
    public struct IntPtr
    {
        public static readonly IntPtr Zero;
        public static int Size { get { return 8; } }
    }
}

namespace System.Runtime.InteropServices
{
    public static class Marshal
    {
        public extern static IntPtr AllocHGlobal(int cb);
        public extern static IntPtr AllocHGlobal(IntPtr cb);
        public extern static IntPtr ReAllocHGlobal(IntPtr pv, IntPtr cb);
        public extern static void FreeHGlobal(IntPtr hglobal);
        public extern static void Copy(IntPtr source, byte[] destination, int startIndex, int length);
        public extern static string PtrToStringAnsi(IntPtr ptr);
    }
}