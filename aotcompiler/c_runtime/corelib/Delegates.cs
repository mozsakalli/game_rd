// corelib: standart delegate aileleri (C# yuzeyi birebir). Generic sablonlar - kullanildikca
// monomorphize edilir (Action`1 gibi arity-suffix'li kayit; Action ve Action<T> ayri tipler).
// Multicast (+/-) ve lambda YOK - bilincli sinir (dilim 1: method group + cagri).
namespace System
{
    delegate void Action();
    delegate void Action<T>(T obj);
    delegate void Action<T1, T2>(T1 arg1, T2 arg2);
    delegate void Action<T1, T2, T3>(T1 arg1, T2 arg2, T3 arg3);
    delegate void Action<T1, T2, T3, T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    delegate TResult Func<TResult>();
    delegate TResult Func<T, TResult>(T arg);
    delegate TResult Func<T1, T2, TResult>(T1 arg1, T2 arg2);
    delegate TResult Func<T1, T2, T3, TResult>(T1 arg1, T2 arg2, T3 arg3);
    delegate TResult Func<T1, T2, T3, T4, TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    delegate int Comparison<T>(T x, T y);
    delegate bool Predicate<T>(T obj);
    delegate TOutput Converter<TInput, TOutput>(TInput input);
}
