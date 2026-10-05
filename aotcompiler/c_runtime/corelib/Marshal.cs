namespace System
{
    struct IntPtr { }
}

namespace System.Runtime.InteropServices
{
    static class Marshal
    {
        extern public static IntPtr AllocHGlobal(int cb);
        extern public static IntPtr AllocHGlobal(IntPtr cb);
        extern public static IntPtr ReAllocHGlobal(IntPtr pv, IntPtr cb);
        extern public static void FreeHGlobal(IntPtr hglobal);
        extern public static void Copy(IntPtr source, byte[] destination, int startIndex, int length);
        extern public static string PtrToStringAnsi(IntPtr ptr);
    }
}