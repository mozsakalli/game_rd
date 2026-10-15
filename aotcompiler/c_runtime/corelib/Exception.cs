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
        // .NET yuzeyi: override edilebilir (aksi halde AOT "override edilecek virtual yok" ile DERLEMEYI durdurur)
        public string source; public string helpLink; public System.Collections.IDictionary data;
        public virtual string Source { get { return source; } set { source = value; } }
        public virtual string HelpLink { get { return helpLink; } set { helpLink = value; } }
        public virtual System.Collections.IDictionary Data { get { if (data == null) data = new System.Collections.Generic.Dictionary<object, object>(); return data; } } // .NET: bos sozluk (null degil)
        public virtual Exception GetBaseException() { Exception e = this; while (e.innerException != null) e = e.innerException; return e; }
        public extern void Print();
    }
    // runtime hata kind'lari (RT_EX_*) -> siniflar; digitoyengine_init'te SINGLETON olarak yaratilir
    // (throw/catch sifir alloc; ctor default C# mesajini atar). Eslesme: WellKnown.ExceptionKinds.
    public class NullReferenceException : Exception { public NullReferenceException() : base("Object reference not set to an instance of an object.") { } }
    public class IndexOutOfRangeException : Exception { public IndexOutOfRangeException() : base("Index was outside the bounds of the array.") { } }
    public class DivideByZeroException : ArithmeticException { public DivideByZeroException() : base("Attempted to divide by zero.") { } }
    public class ArithmeticException : Exception { public ArithmeticException() : base("Overflow or underflow in the arithmetic operation.") { } public ArithmeticException(string message) : base(message) { } }
    public class OverflowException : ArithmeticException { public OverflowException() : base("Arithmetic operation resulted in an overflow.") { } public OverflowException(string message) : base(message) { } }
    public class InvalidCastException : Exception { public InvalidCastException() : base("Specified cast is not valid.") { } }
    public class InvalidOperationException : Exception { public InvalidOperationException() : base("Operation is not valid due to the current state of the object.") { } public InvalidOperationException(string message) : base(message) { } public InvalidOperationException(string message, Exception inner) : base(message, inner) { } }
    public class NotImplementedException : Exception { public NotImplementedException() : base("The method or operation is not implemented.") { } public NotImplementedException(string message) : base(message) { } }
    public class NotSupportedException : Exception { public NotSupportedException() : base("Specified method is not supported.") { } public NotSupportedException(string message) : base(message) { } }
    public class IOException : Exception { public IOException() : base("I/O error occurred.") { } public IOException(string message) : base(message) { } }
    public class FormatException : Exception { public FormatException() : base("One of the identified items was in an invalid format.") { } public FormatException(string message) : base(message) { } }
    public class ArgumentException : Exception
    {
        public string paramName;
        public ArgumentException() : base("Value does not fall within the expected range.") { }
        public ArgumentException(string message) : base(message) { }
        public ArgumentException(string message, string paramName) : base(paramName == null ? message : message + " (Parameter '" + paramName + "')") { this.paramName = paramName; }
        public ArgumentException(string message, Exception inner) : base(message, inner) { }
        public virtual string ParamName { get { return paramName; } }
    }
    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() : base("Value cannot be null.") { }
        public ArgumentNullException(string paramName) : base("Value cannot be null.", paramName) { }
        public ArgumentNullException(string paramName, string message) : base(message, paramName) { }
    }
    public class ArgumentOutOfRangeException : ArgumentException
    {
        public object actualValue;
        public ArgumentOutOfRangeException() : base("Specified argument was out of the range of valid values.") { }
        public ArgumentOutOfRangeException(string paramName) : base("Specified argument was out of the range of valid values.", paramName) { }
        public ArgumentOutOfRangeException(string paramName, string message) : base(message, paramName) { }
        public ArgumentOutOfRangeException(string paramName, object actualValue, string message) : base(message, paramName) { this.actualValue = actualValue; }
        public virtual object ActualValue { get { return actualValue; } }
    }
}
