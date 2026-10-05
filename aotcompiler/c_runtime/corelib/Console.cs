// corelib: System.Console (minimal C# yuzeyi).
// extern = govde c_runtime/corelib.c'de (vm_write_utf8/printf) + source/Intrinsics.cs'te (VM,
// System.Console.Out'a yazar - test SetOut ile yakalayabilir).
namespace System
{
    class Console
    {
        static IO.TextWriter error = new IO.TextWriter();
        static IO.TextWriter Error { get { return error; } }
        extern static void Write(string value);
        extern static void Write(int value);
        extern static void WriteLine(string value);
        extern static void WriteLine(int value);
        extern static void WriteLine();
        // boxed deger dahil her nesne (dotnet: null -> bos satir, degilse ToString)
        static void WriteLine(object value)
        {
            if (value == null) { WriteLine(""); return; }
            WriteLine(value.ToString());
        }
    }
    // GC yuzeyi: Collect deterministik tam toplama yapar (finalizer'lar kosulur).
    class GC
    {
        extern static void Collect();
    }
}
