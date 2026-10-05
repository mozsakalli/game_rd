namespace System
{
    struct IntPtr { }
}

namespace System.Runtime.InteropServices
{
    static class Marshal
    {
        extern public static IntPtr AllocHGlobal(int cb);
        extern public static void FreeHGlobal(IntPtr hglobal);
    }
}