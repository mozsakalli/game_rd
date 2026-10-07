// corelib: Exception hiyerarsisi (C# semantigi). Trace KOMPAKT tasinir - string YOK
// (Message/StackTrace getter'lari string corlib'i gelince bu verinin ustune eklenecek).
// ALAN SOZLESMESI: trace (ExceptionTrace: mi[24]/line[24]) + traceCount adlarini transpiler ExBind
// emisyonu ve corelib.c'deki DigitoyEngineException layout kopyasi kullanir; kapasite 24.
// fixed tamponlar ayri struct'ta: C# (Roslyn) fixed buffer'i yalniz struct icinde kabul eder; bayt yerlesimi ayni.
// Print: alloc'suz stderr raporu (extern: corelib.c + Intrinsics).
// Message/StackTrace: C# yuzeyi; throw ani alloc'suz kalir, string GETTER cagrisinda uretilir.
namespace System
{
    public unsafe struct ExceptionTrace
    {
        public fixed long mi[24];
        public fixed int line[24];
    }

    public class Exception
    {
        public string message;
        public ExceptionTrace trace;
        public int traceCount;
        public Exception innerException;
        public Exception() { }
        public Exception(string message) { this.message = message; }
        public Exception(string message, Exception innerException) { this.message = message; this.innerException = innerException; }
        public extern virtual string Message { get; }
        public extern virtual string StackTrace { get; }
        public Exception InnerException { get { return innerException; } }
        public extern void Print();
    }
    // runtime hata kind'lari (RT_EX_*) -> siniflar; digitoyengine_init'te SINGLETON olarak yaratilir
    // (throw/catch sifir alloc; ctor default C# mesajini atar). Eslesme: WellKnown.ExceptionKinds.
    public class NullReferenceException : Exception { public NullReferenceException() : base("Object reference not set to an instance of an object.") { } }
    public class IndexOutOfRangeException : Exception { public IndexOutOfRangeException() : base("Index was outside the bounds of the array.") { } }
    public class DivideByZeroException : Exception { public DivideByZeroException() : base("Attempted to divide by zero.") { } }
    public class InvalidCastException : Exception { public InvalidCastException() : base("Specified cast is not valid.") { } }
    public class InvalidOperationException : Exception { public InvalidOperationException(string message) : base(message) { } }
    public class NotImplementedException : Exception { public NotImplementedException() : base("The method or operation is not implemented.") { } }
    public class NotSupportedException : Exception { public NotSupportedException() : base("Specified method is not supported.") { } }
    public class IOException : Exception { public IOException() : base("I/O error occurred.") { } }
}
