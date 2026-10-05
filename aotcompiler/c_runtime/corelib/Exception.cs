// corelib: Exception hiyerarsisi (C# semantigi). Trace KOMPAKT tasinir - string YOK
// (Message/StackTrace getter'lari string corlib'i gelince bu verinin ustune eklenecek).
// ALAN SOZLESMESI: traceMi/traceLine/traceCount adlarini transpiler ExBind emisyonu,
// VM BindPending ve corelib.c'deki DigitoyEngineException layout kopyasi kullanir; kapasite 24.
// Print: alloc'suz stderr raporu (extern: corelib.c + Intrinsics).
// Message/StackTrace: C# yuzeyi; throw ani alloc'suz kalir, string GETTER cagrisinda uretilir.
namespace System
{
    class Exception
    {
        string message;
        fixed long traceMi[24];
        fixed int traceLine[24];
        int traceCount;
        Exception innerException;
        Exception() { }
        Exception(string message) { this.message = message; }
        Exception(string message, Exception innerException) { this.message = message; this.innerException = innerException; }
        extern virtual string Message { get; }
        extern virtual string StackTrace { get; }
        Exception InnerException { get { return innerException; } }
        extern void Print();
    }
    // runtime hata kind'lari (RT_EX_*) -> siniflar; digitoyengine_init'te SINGLETON olarak yaratilir
    // (throw/catch sifir alloc; ctor default C# mesajini atar). Eslesme: WellKnown.ExceptionKinds.
    class NullReferenceException : Exception { NullReferenceException() : base("Object reference not set to an instance of an object.") { } }
    class IndexOutOfRangeException : Exception { IndexOutOfRangeException() : base("Index was outside the bounds of the array.") { } }
    class DivideByZeroException : Exception { DivideByZeroException() : base("Attempted to divide by zero.") { } }
    class InvalidCastException : Exception { InvalidCastException() : base("Specified cast is not valid.") { } }
    class InvalidOperationException : Exception { InvalidOperationException(string message) : base(message) { } }
    class NotImplementedException : Exception { NotImplementedException() : base("The method or operation is not implemented.") { } }
    class NotSupportedException : Exception { public NotSupportedException() : base("Specified method is not supported.") { } }
    class IOException : Exception { IOException() : base("I/O error occurred.") { } }
}
