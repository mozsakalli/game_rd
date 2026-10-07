// corelib: System.Console (minimal C# yuzeyi).
// extern = govde c_runtime/corelib.c'de (vm_write_utf8/printf) + source/Intrinsics.cs'te (VM,
// System.Console.Out'a yazar - test SetOut ile yakalayabilir).
namespace System
{
    public class Console
    {
        static IO.TextWriter error = new IO.TextWriter();
        public static IO.TextWriter Error { get { return error; } }
        public extern static void Write(string value);
        public extern static void Write(int value);
        public extern static void WriteLine(string value);
        public extern static void WriteLine(int value);
        public extern static void WriteLine();
        // boxed deger dahil her nesne (dotnet: null -> bos satir, degilse ToString)
        public static void WriteLine(object value)
        {
            if (value == null) { WriteLine(""); return; }
            WriteLine(value.ToString());
        }
    }
    // GC yuzeyi: Collect deterministik tam toplama yapar (finalizer'lar kosulur).
    public class GC
    {
        public extern static void Collect();
        public extern static void Collect(int generation); // 0 = yalniz genc nesil (gc_minor); diger = tam
    }
}
