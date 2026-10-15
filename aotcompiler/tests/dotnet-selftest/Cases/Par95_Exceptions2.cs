using System;
using System.Collections.Generic;
using ParityKit;

// 95 (devam)
namespace Demo95
{
    partial class App95
    {
        static string UnwindThroughGeneric()
        {
            string r1 = PM(() => Gen<int>.Throw(5)), r2 = PM(() => Gen<string>.Throw("s")), r3 = PM(() => Gen<Demo94.Pt>.Throw(new Demo94.Pt { X = 1 })), r4 = PM(() => GenMethod<int>(1)), r5 = PM(() => GenMethod<string>(null)), r6 = PM(() => Gen<int>.ThrowInFinally(1)), r7 = PM(() => { try { Gen<long>.Throw(2L); } catch (MyEx e) { return "caught:" + e.Message; } return "no"; }), r8 = PM(() => Gen<int>.Nested<string>(1, "x")), r9 = PM(() => new GenClass<int>().Virt()), r10 = PM(() => ((IThrower)new GenClass<string>()).Virt()), r11 = PM(() => new GenClass<Demo94.Pt>().Virt()), r12 = PM(() => Gen<int>.Lambda(3)), r13 = PM(() => Gen<int>.Local(3)), r14 = PM(() => Gen<int>.ViaInterface(new GenClass<int>()));
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => Flush()), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => Flush()));
        }
        interface IThrower { string Virt(); }
        static class Gen<T>
        {
            public static string Throw(T v) { throw new MyEx("gen:" + v, 1); }
            public static string ThrowInFinally(T v) { try { L("g-try"); throw new ArgumentException("a"); } finally { L("g-finally:" + v); } }
            public static string Nested<U>(T t, U u) { try { throw new MyEx("nested:" + t + u, 2); } finally { L("nested-finally"); } }
            public static string Lambda(T v) { Func<T, string> f = x => { throw new MyEx("lambda:" + x, 3); }; return f(v); }
            public static string Local(T v) { string Loc(T x) { throw new MyEx("local:" + x, 4); } return Loc(v); }
            public static string ViaInterface(IThrower t) { try { return t.Virt(); } finally { L("iface-finally"); } }
        }
        class GenClass<T> : IThrower { public virtual string Virt() { throw new MyEx("virt:" + typeof(T).Name, 5); } }
        static string GenMethod<T>(T v) { try { throw new MyEx("gm:" + (v == null ? "null" : v.ToString()), 1); } finally { L("gm-finally"); } }

        static string UnwindThroughDelegate()
        {
            Func<int> f = () => { throw new MyEx("lam", 1); };
            Action a = () => { throw new ArgumentException("act"); };
            Func<int, int> chain = x => { try { return f(); } finally { L("chain-finally:" + x); } };
            string r1 = PM(() => f()), r2 = VM(() => a()), r3 = PM(() => chain(2)), r4 = PM(() => f.Invoke()), r5 = PM(() => ((Delegate)f).DynamicInvoke()), r6 = PM(() => f.Method.Invoke(f.Target, null)), r7 = PM(() => Invoker(f)), r8 = PM(() => { Func<int> multi = () => 1; multi += f; multi += () => 2; return multi(); }), r9 = PM(() => { Action m = () => L("m1"); m += () => { throw new MyEx("m2", 2); }; m += () => L("m3"); m(); return "no"; }), r10 = Flush();
            string r11 = PM(() => { Func<int> captured = null; int local = 5; captured = () => { local++; throw new MyEx("cap:" + local, local); }; try { captured(); } catch (MyEx e) { return e.Code + "/" + local; } return "no"; });
            string r12 = PM(() => { var en = Throwing().GetEnumerator(); return en.MoveNext(); });
            string r13 = PM(() => { Comparison<int> cmp = (x, y) => { throw new MyEx("cmp", 1); }; var l = new List<int> { 2, 1 }; l.Sort(cmp); return "sorted"; });
            string r14 = PM(() => { var d = new Dictionary<string, int>(new ThrowingComparer()); d["a"] = 1; return "added"; });
            string r15 = PM(() => { var l = new List<int> { 1, 2 }; return l.Find(x => { throw new MyEx("find", 1); }); });
            string r16 = PM(() => { var l = new List<int> { 1, 2 }; l.RemoveAll(x => { throw new MyEx("removeall", 1); }); return l.Count; });
            string r17 = PM(() => { var l = new List<int> { 1, 2 }; try { l.RemoveAll(x => { if (x == 2) throw new MyEx("ra", 1); return true; }); } catch (MyEx) { } return string.Join(",", l); }); // kismi silme sonrasi liste durumu
            string r18 = PM(() => { var l = new List<int> { 3, 1, 2, 5, 4 }; try { l.Sort((x, y) => { if (x == 5 || y == 5) throw new MyEx("srt", 1); return x.CompareTo(y); }); } catch (InvalidOperationException e) { return "IOE:" + e.InnerException?.GetType().Name + ":" + string.Join(",", l); } return "no"; });
            string r19 = PM(() => { var arr = new[] { 3, 1, 2 }; try { Array.Sort(arr, (x, y) => { throw new MyEx("asrt", 1); }); } catch (InvalidOperationException e) { return "IOE:" + e.InnerException?.GetType().Name; } return "no"; });
            string r20 = PM(() => { Func<int> fp = Thrower2; return fp(); });
            string r21 = PM(() => { Func<string, int> parse = int.Parse; return parse("x"); });
            string r22 = PM(() => { Func<object, string> ts = Convert.ToString; return ts(new Demo94.Node(1)); });
            string r23 = PM(() => { Action<int> boxed = x => { throw new MyEx("boxed" + x, x); }; try { boxed(7); } catch (MyEx e) when (e.Code == 7) { return "filtered"; } return "no"; });
            string r24 = PM(() => { EventHandler h = (s, e) => { throw new MyEx("evt", 1); }; h(null, EventArgs.Empty); return "no"; });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => r17), Q(() => r18), Q(() => r19), Q(() => r20), Q(() => r21), Q(() => r22), Q(() => r23), Q(() => r24), Q(() => Flush()));
        }
        static int Invoker(Func<int> f) { try { return f(); } finally { L("invoker-finally"); } }
        static int Thrower2() { throw new MyEx("mg", 1); }
        static IEnumerable<int> Throwing() { throw new MyEx("iter", 1); }
        class ThrowingComparer : IEqualityComparer<string> { public bool Equals(string a, string b) { return a == b; } public int GetHashCode(string s) { throw new MyEx("hash", 1); } }

        static string UnwindThroughIterator()
        {
            string r1 = PM(() => { foreach (var x in Iter(2)) L("got" + x); return "done"; });
            string r2 = Flush();
            string r3 = PM(() => { var en = Iter(1).GetEnumerator(); L("created"); en.MoveNext(); L("mn1:" + en.Current); en.MoveNext(); L("mn2"); return "no"; });
            string r4 = Flush();
            string r5 = PM(() => { foreach (var x in IterFinally()) { L("f" + x); if (x == 2) break; } return "broke"; });
            string r6 = Flush();
            string r7 = PM(() => { foreach (var x in IterFinally()) { L("f" + x); if (x == 2) throw new MyEx("body", 1); } return "no"; });
            string r8 = Flush();
            string r9 = PM(() => { var en = IterFinally().GetEnumerator(); en.Dispose(); return "disposed-unstarted"; });
            string r10 = Flush();
            string r11 = PM(() => { var en = IterFinally().GetEnumerator(); en.MoveNext(); en.Dispose(); bool mn = en.MoveNext(); return "after-dispose-mn:" + mn; });
            string r12 = Flush();
            string r13 = PM(() => { foreach (var x in IterCatch()) L("c" + x); return "done"; });
            string r14 = Flush();
            string r15 = PM(() => { var en = IterFinallyThrows().GetEnumerator(); en.MoveNext(); en.Dispose(); return "no"; });
            string r16 = Flush();
            string r17 = PM(() => { foreach (var x in IterFinallyThrows()) { if (x == 1) break; } return "no"; });
            string r18 = Flush();
            string r19 = PM(() => { foreach (var x in IterFinallyThrows()) { if (x == 1) throw new ArgumentException("body"); } return "no"; }); // body ex vs finally ex
            string r20 = Flush();
            string r21 = PM(() => { int s = 0; foreach (var x in IterNested()) s += x; return s; });
            string r22 = Flush();
            string r23 = PM(() => { var en = Iter(1).GetEnumerator(); en.MoveNext(); try { en.MoveNext(); } catch (MyEx) { } bool mn = en.MoveNext(); return "after-throw-mn:" + mn + "/" + en.Current; });
            string r24 = PM(() => { var en = IterUsing().GetEnumerator(); en.MoveNext(); en.MoveNext(); en.Dispose(); return "u"; });
            string r25 = Flush() + "|" + string.Join(",", Res.Log); Res.Log.Clear();
            string r26 = PM(() => { foreach (var x in IterUsing()) { throw new MyEx("in-using-body", 1); } return "no"; });
            string r27 = string.Join(",", Res.Log); Res.Log.Clear();
            string r28 = PM(() => { foreach (var x in IterLazy()) { L("L" + x); } return "lazy"; });
            string r29 = Flush();
            string r30 = PM(() => { var e1 = IterLazy().GetEnumerator(); var e2 = IterLazy().GetEnumerator(); e1.MoveNext(); e2.MoveNext(); e2.MoveNext(); return e1.Current + "/" + e2.Current; });
            string r31 = Flush();
            string r32 = PM(() => { var it = IterLazy(); var a = it.GetEnumerator(); var b = it.GetEnumerator(); return ReferenceEquals(a, b) + "/" + ReferenceEquals(a, it); }); // ilk GetEnumerator ayni thread'de this doner
            string r33 = Flush();
            string r34 = PM(() => { var it = IterLazy(); var a = it.GetEnumerator(); a.MoveNext(); var b = it.GetEnumerator(); b.MoveNext(); return a.Current + "/" + b.Current + "/" + ReferenceEquals(a, b); });
            string r35 = Flush();
            string r36 = PM(() => { var en = IterLazy().GetEnumerator(); return en.Current; }); // baslamamis Current
            string r37 = PM(() => { var en = IterLazy().GetEnumerator(); while (en.MoveNext()) { } return en.Current; }); // bitmis Current
            string r38 = PM(() => { var en = IterLazy().GetEnumerator(); ((System.Collections.IEnumerator)en).Reset(); return "reset"; });
            string r39 = PM(() => { var en = (IEnumerator<int>)IterLazy(); return en.MoveNext() + "/" + en.Current; }); // IEnumerable == IEnumerator ayni nesne
            string r40 = Flush();
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => r17), Q(() => r18), Q(() => r19), Q(() => r20), Q(() => r21), Q(() => r22), Q(() => r23), Q(() => r24), Q(() => r25), Q(() => r26), Q(() => r27), Q(() => r28), Q(() => r29), Q(() => r30), Q(() => r31), Q(() => r32), Q(() => r33), Q(() => r34), Q(() => r35), Q(() => r36), Q(() => r37), Q(() => r38), Q(() => r39), Q(() => r40));
        }
        static IEnumerable<int> Iter(int throwAt) { for (int i = 0; i < 5; i++) { if (i == throwAt) throw new MyEx("iter-at-" + i, i); L("yield" + i); yield return i; } }
        static IEnumerable<int> IterFinally() { try { L("start"); yield return 1; L("mid"); yield return 2; L("late"); yield return 3; } finally { L("iter-finally"); } }
        static IEnumerable<int> IterCatch() { int i = 0; while (i < 3) { try { if (i == 1) throw new MyEx("inside", 1); } catch (MyEx) { L("iter-caught"); } yield return i++; } }
        static IEnumerable<int> IterFinallyThrows() { try { yield return 1; yield return 2; } finally { L("ift-finally"); throw new MyEx("from-iter-finally", 9); } }
        static IEnumerable<int> IterNested() { try { foreach (var x in IterFinally()) { try { yield return x * 10; } finally { L("inner-fin" + x); } } } finally { L("outer-fin"); } }
        static IEnumerable<int> IterUsing() { using (new Res("iter")) { yield return 1; yield return 2; } }
        static IEnumerable<int> IterLazy() { L("body-start"); yield return 1; L("after1"); yield return 2; L("after2"); }

        static string CatchOrder()
        {
            string r1 = PM(() => { try { throw new DerivedEx(); } catch (DerivedEx) { return "derived"; } catch (MyEx) { return "my"; } catch (Exception) { return "ex"; } });
            string r2 = PM(() => { try { throw new MyEx("m", 1); } catch (DerivedEx) { return "derived"; } catch (MyEx) { return "my"; } catch (Exception) { return "ex"; } });
            string r3 = PM(() => { try { throw new ArgumentNullException("p"); } catch (ArgumentException e) { return "argex:" + e.ParamName; } });
            string r4 = PM(() => { try { throw new ArgumentNullException("p"); } catch (ArgumentOutOfRangeException) { return "aoor"; } catch (ArgumentException) { return "argex"; } });
            string r5 = PM(() => { try { throw new System.Threading.Tasks.TaskCanceledException(); } catch (OperationCanceledException) { return "oce"; } });
            string r6 = PM(() => { try { throw new KeyNotFoundException(); } catch (ArgumentException) { return "arg"; } catch (SystemException) { return "sys"; } });
            string r7 = PM(() => { try { throw new MyEx("m", 1); } catch (SystemException) { return "sys"; } catch (Exception) { return "ex"; } });
            string r8 = PM(() => { try { throw new DivideByZeroException(); } catch (ArithmeticException) { return "arith"; } });
            string r9 = PM(() => { try { throw new OverflowException(); } catch (ArithmeticException) { return "arith"; } });
            string r10 = PM(() => { try { throw new System.IO.FileNotFoundException(); } catch (System.IO.IOException) { return "io"; } });
            string r11 = PM(() => { try { throw new InvalidCastException(); } catch (SystemException) { return "sys"; } });
            string r12 = PM(() => { try { throw new NullReferenceException(); } catch (SystemException) { return "sys"; } });
            string r13 = PM(() => { try { throw new IndexOutOfRangeException(); } catch (SystemException) { return "sys"; } });
            string r14 = PM(() => { try { throw new ObjectDisposedException("x"); } catch (InvalidOperationException) { return "ioe"; } });
            string r15 = PM(() => { try { throw new System.Threading.Tasks.TaskCanceledException(); } catch (InvalidOperationException) { return "ioe"; } catch (OperationCanceledException) { return "oce"; } });
            string r16 = PM(() => { try { throw new TypeInitializationException("T", new Exception()); } catch (SystemException) { return "sys"; } });
            string r17 = PM(() => { try { throw new AggregateException(); } catch (SystemException) { return "sys"; } catch (Exception) { return "ex"; } });
            string r18 = PM(() => { try { throw new FormatException(); } catch (SystemException) { return "sys"; } });
            string r19 = PM(() => { try { throw new NotSupportedException(); } catch (SystemException) { return "sys"; } });
            string r20 = PM(() => { try { throw new NotImplementedException(); } catch (SystemException) { return "sys"; } });
            string r21 = PM(() => { try { throw new ArrayTypeMismatchException(); } catch (SystemException) { return "sys"; } });
            string r22 = PM(() => { try { throw new OutOfMemoryException(); } catch (SystemException) { return "sys"; } });
            string r23 = PM(() => { try { throw new System.Collections.Generic.KeyNotFoundException(); } catch (SystemException) { return "sys"; } });
            string r24 = PM(() => { try { throw new MissingMethodException(); } catch (MemberAccessException) { return "member"; } });
            string r25 = PM(() => { try { throw new System.Reflection.TargetInvocationException(new Exception()); } catch (ApplicationException) { return "app"; } });
            string r26 = PM(() => { try { throw new UriFormatException(); } catch (FormatException) { return "fmt"; } });
            string r27 = PM(() => { try { throw new System.Text.DecoderFallbackException(); } catch (ArgumentException) { return "arg"; } });
            string r28 = PM(() => { try { throw new System.Globalization.CultureNotFoundException(); } catch (ArgumentException) { return "arg"; } });
            string r29 = PM(() => { try { throw new System.Threading.LockRecursionException(); } catch (SystemException) { return "sys"; } catch (Exception) { return "ex"; } });
            string r30 = PM(() => { try { throw new System.Runtime.InteropServices.COMException(); } catch (System.Runtime.InteropServices.ExternalException) { return "ext"; } });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => r17), Q(() => r18), Q(() => r19), Q(() => r20), Q(() => r21), Q(() => r22), Q(() => r23), Q(() => r24), Q(() => r25), Q(() => r26), Q(() => r27), Q(() => r28), Q(() => r29), Q(() => r30));
        }

        static string ExceptionProps()
        {
            var e = new MyEx("hello", 3); var inner = new ArgumentException("in"); var wrapped = new MyEx("wrap", inner);
            Exception thrown = null; try { throw new InvalidOperationException("thrown"); } catch (Exception x) { thrown = x; }
            Exception rethrown = null; try { try { throw new InvalidOperationException("re"); } catch (Exception x) { throw; } } catch (Exception x) { rethrown = x; }
            return Par.J(Q(() => e.Message), Q(() => e.Code), Q(() => e.InnerException == null), Q(() => e.StackTrace == null), Q(() => e.Source == null), Q(() => e.HResult), Q(() => new Exception().HResult), Q(() => new ArgumentException().HResult), Q(() => new ArgumentNullException().HResult), Q(() => new ArgumentOutOfRangeException().HResult), Q(() => new InvalidOperationException().HResult), Q(() => new InvalidCastException().HResult), Q(() => new NullReferenceException().HResult), Q(() => new IndexOutOfRangeException().HResult), Q(() => new DivideByZeroException().HResult), Q(() => new OverflowException().HResult), Q(() => new FormatException().HResult), Q(() => new NotSupportedException().HResult), Q(() => new NotImplementedException().HResult), Q(() => new KeyNotFoundException().HResult), Q(() => new ObjectDisposedException("x").HResult), Q(() => new OperationCanceledException().HResult), Q(() => new System.Threading.Tasks.TaskCanceledException().HResult), Q(() => new TypeInitializationException("T", null).HResult), Q(() => new ArrayTypeMismatchException().HResult), Q(() => new MissingMethodException().HResult), Q(() => new AggregateException().HResult), Q(() => new OutOfMemoryException().HResult), Q(() => new StackOverflowException().HResult), Q(() => new System.IO.IOException().HResult), Q(() => new System.IO.FileNotFoundException().HResult), Q(() => new TimeoutException().HResult), Q(() => new ApplicationException().HResult), Q(() => new SystemException().HResult), Q(() => new ArithmeticException().HResult), Q(() => new RankException().HResult), Q(() => new TypeLoadException().HResult), Q(() => new EntryPointNotFoundException().HResult), Q(() => new PlatformNotSupportedException().HResult), Q(() => new UnauthorizedAccessException().HResult), Q(() => new BadImageFormatException().HResult), Q(() => new MemberAccessException().HResult), Q(() => new FieldAccessException().HResult), Q(() => new MethodAccessException().HResult), Q(() => new DllNotFoundException().HResult), Q(() => new InvalidProgramException().HResult), Q(() => new AccessViolationException().HResult), Q(() => new InsufficientMemoryException().HResult), Q(() => new DataMisalignedException().HResult), Q(() => new DuplicateWaitObjectException().HResult), Q(() => new InvalidTimeZoneException().HResult), Q(() => new UriFormatException().HResult), Q(() => new System.Reflection.TargetInvocationException(null).HResult), Q(() => new System.Reflection.AmbiguousMatchException().HResult), Q(() => new System.Threading.SynchronizationLockException().HResult), Q(() => new System.Threading.ThreadStateException().HResult), Q(() => new System.Threading.SemaphoreFullException().HResult), Q(() => new System.Threading.LockRecursionException().HResult), Q(() => new System.Runtime.InteropServices.COMException().HResult), Q(() => new System.Runtime.InteropServices.ExternalException().HResult), Q(() => new System.Text.DecoderFallbackException().HResult), Q(() => new System.Globalization.CultureNotFoundException().HResult), Q(() => wrapped.InnerException.Message), Q(() => wrapped.GetBaseException().Message), Q(() => wrapped.GetBaseException().GetType().Name), Q(() => e.GetBaseException() == e), Q(() => new MyEx("a", new MyEx("b", new MyEx("c", 1))).GetBaseException().Message), Q(() => thrown.StackTrace != null), Q(() => thrown.Source), Q(() => thrown.TargetSite != null), Q(() => rethrown.Message), Q(() => e.Data.Count), Q(() => e.Data.IsReadOnly), V(() => e.Data["k"] = 1), Q(() => e.Data["k"]), Q(() => e.Data.Count), Q(() => e.Data["missing"] == null), V(() => e.Data[null] = 1), Q(() => e.ToString().StartsWith("Demo95.MyEx: hello")), Q(() => new Exception("m").ToString()), Q(() => new Exception("m", new Exception("i")).ToString().Replace("\r\n", "\n")), Q(() => new Exception().ToString()), Q(() => wrapped.ToString().Replace("\r\n", "\n")), Q(() => e.GetType().FullName), Q(() => e.GetType().BaseType.Name), Q(() => typeof(Exception).IsAssignableFrom(typeof(MyEx))), Q(() => e is ISerializable2), Q(() => new ArgumentException("m", "p").ParamName), Q(() => new ArgumentException("m").ParamName == null), Q(() => new ArgumentNullException("p").ParamName), Q(() => new ArgumentOutOfRangeException("p", 5, "m").ActualValue), Q(() => new ArgumentOutOfRangeException("p", 5, "m").ParamName), Q(() => new ArgumentOutOfRangeException("p").ActualValue == null), Q(() => new ObjectDisposedException("obj").ObjectName), Q(() => new ObjectDisposedException(null).ObjectName), Q(() => new System.IO.FileNotFoundException("m", "f.txt").FileName), Q(() => new System.IO.FileNotFoundException().FileName == null), Q(() => new KeyNotFoundException("k").Message), Q(() => new OperationCanceledException().CancellationToken.IsCancellationRequested), Q(() => new OperationCanceledException("m", new System.Threading.CancellationToken(true)).CancellationToken.IsCancellationRequested), Q(() => new AggregateException(new Exception("a"), new Exception("b")).InnerExceptions.Count), Q(() => new AggregateException(new Exception("a"), new Exception("b")).InnerException.Message), Q(() => new AggregateException().InnerException == null), Q(() => new AggregateException(new AggregateException(new Exception("deep"))).Flatten().InnerExceptions[0].Message), Q(() => new AggregateException(new AggregateException(new Exception("deep"))).GetBaseException().Message), Q(() => new AggregateException("top", new Exception("a")).GetBaseException().Message), Q(() => new TypeInitializationException("TName", new Exception("i")).TypeName), Q(() => new TypeInitializationException(null, null).TypeName), Q(() => new System.Reflection.TargetInvocationException(new Exception("ti")).InnerException.Message), Q(() => new MissingMethodException("C", "M").Message), Q(() => new TimeoutException("t").Message), Q(() => new System.Threading.Tasks.TaskCanceledException().Task == null), Q(() => new System.Threading.Tasks.TaskCanceledException("m").Message), Q(() => new OperationCanceledException(new System.Threading.CancellationToken(true)).Message));
        }
        interface ISerializable2 { }

        static string InnerAndAggregate()
        {
            var a = new AggregateException(new Exception("a"), new MyEx("b", 2), new AggregateException(new Exception("c")));
            var flat = a.Flatten();
            string handled = PM(() => { a.Handle(x => x is MyEx); return "all-handled"; });
            string handled2 = PM(() => { new AggregateException(new MyEx("x", 1), new MyEx("y", 2)).Handle(x => x is MyEx); return "all-handled"; });
            string handled3 = PM(() => { a.Handle(null); return "no"; });
            string msgs = ""; foreach (var ie in flat.InnerExceptions) msgs += ie.GetType().Name + ":" + ie.Message + ";";
            var wrapped = new Exception("l1", new Exception("l2", new Exception("l3")));
            int depth = 0; for (var x = wrapped; x != null; x = x.InnerException) depth++;
            string r1 = PM(() => { try { throw new MyEx("outer", new ArgumentException("inner")); } catch (MyEx e) when (e.InnerException is ArgumentException ae) { return "filtered-inner:" + ae.Message; } });
            string r2 = PM(() => { try { try { throw new ArgumentException("first"); } catch (ArgumentException e) { throw new MyEx("second", e); } } catch (MyEx e) { return e.Message + "<-" + e.InnerException.Message + "/" + e.GetBaseException().Message; } });
            string r3 = PM(() => { try { try { throw new ArgumentException("first"); } finally { try { throw new MyEx("fin", 1); } catch { } } } catch (ArgumentException e) { return "survived:" + e.Message + "/" + (e.InnerException == null); } });
            string r4 = PM(() => { var ex = new Exception("m"); var ex2 = new Exception("m", null); return (ex.InnerException == null) + "/" + (ex2.InnerException == null); });
            string r5 = PM(() => new AggregateException(new Exception("x")).Message);
            string r6 = PM(() => new AggregateException("custom", new Exception("x"), new Exception("y")).Message);
            string r7 = PM(() => new AggregateException(new List<Exception> { new Exception("l") }).InnerExceptions.Count);
            string r8 = PM(() => new AggregateException((Exception[])null));
            string r9 = PM(() => new AggregateException(new Exception[] { null }));
            string r10 = PM(() => new AggregateException("m", (Exception)null));
            string r11 = PM(() => { var ag = new AggregateException(new Exception("a"), new Exception("b")); var l = new List<string>(); foreach (var x in ag.InnerExceptions) l.Add(x.Message); return string.Join(",", l) + "|" + ag.InnerExceptions.GetType().Name; });
            string r12 = PM(() => { try { throw new AggregateException(new MyEx("thrown", 1)); } catch (AggregateException e) { return e.InnerException.GetType().Name + "/" + e.InnerExceptions.Count; } });
            string r13 = PM(() => { var e = new AggregateException(new Exception("a")); return e.ToString().Replace("\r\n", "\n"); });
            string r14 = PM(() => new AggregateException().Flatten().InnerExceptions.Count);
            string r15 = PM(() => new AggregateException(new AggregateException(new AggregateException(new Exception("deep3")))).Flatten().InnerExceptions[0].Message);
            string r16 = PM(() => new AggregateException(new AggregateException(new Exception("x"), new AggregateException(new Exception("y"))), new Exception("z")).Flatten().InnerExceptions.Count);
            return Par.J(Q(() => a.InnerExceptions.Count), Q(() => flat.InnerExceptions.Count), Q(() => msgs), Q(() => handled), Q(() => handled2), Q(() => handled3), Q(() => depth), Q(() => wrapped.GetBaseException().Message), Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => a.Message), Q(() => a.InnerException.Message), Q(() => a.GetBaseException().Message), Q(() => flat.GetBaseException().Message));
        }

        static string NullRefForms()
        {
            string s = null; object o = null; int[] arr = null; Demo94.Node n = null; Func<int> f = null; Action a = null; IDisposable d = null; List<int> l = null; int? ni = null; Demo94.Pt? np = null; Exception ex = null; Delegate dg = null; string[] sarr = null; System.Collections.IEnumerable ie = null; dynamic dyn = null; Type t = null;
            return Par.J(PM(() => s.Length), PM(() => o.ToString()), PM(() => o.GetHashCode()), PM(() => o.GetType()), PM(() => o.Equals(null)), PM(() => arr.Length), PM(() => arr[0]), PM(() => n.V), PM(() => n.ToString()), PM(() => f()), PM(() => { a(); return 1; }), PM(() => { d.Dispose(); return 1; }), PM(() => l.Count), PM(() => l[0]), PM(() => ni.Value), PM(() => np.Value.X), PM(() => ex.Message), PM(() => dg.Method), PM(() => sarr[0].Length), PM(() => ie.GetEnumerator()), PM(() => t.Name), PM(() => { foreach (var x in arr) { } return 1; }), PM(() => { foreach (var x in l) { } return 1; }), PM(() => { foreach (var x in s) { } return 1; }), PM(() => { lock (o) { } return 1; }), PM(() => { using (d) { } return "using-null-ok"; }), PM(() => s?.Length), PM(() => s?.Length ?? -1), PM(() => n?.V), PM(() => arr?[0]), PM(() => f?.Invoke()), PM(() => (s ?? "dflt").Length), PM(() => o is string), PM(() => o as string), PM(() => (string)o), PM(() => (int)o), PM(() => (int?)o), PM(() => (Demo94.Pt)o), PM(() => (Demo94.Pt?)o), PM(() => (Demo94.Col)o), PM(() => ni.GetValueOrDefault()), PM(() => ni.HasValue), PM(() => ni.ToString()), PM(() => ni.GetHashCode()), PM(() => ni.Equals(null)), PM(() => ni == null), PM(() => ((object)ni) == null), PM(() => ni.GetType()), PM(() => np.GetValueOrDefault().X), PM(() => s == null), PM(() => s + "x"), PM(() => string.Concat(s, s)), PM(() => s.ToString()), PM(() => s.Equals(null)), PM(() => string.Equals(s, null)), PM(() => s.GetHashCode()), PM(() => s.GetType()), PM(() => Equals(s, o)), PM(() => ReferenceEquals(s, o)), PM(() => s is null), PM(() => s is not null), PM(() => o is var _), PM(() => o switch { null => "null-arm", _ => "other" }), PM(() => o switch { string str => "str", _ => "other" }), PM(() => s switch { "" => "empty", null => "null", _ => "x" }), PM(() => n.ToString2()), PM(() => arr.Length2()), PM(() => $"{s}{o}{n}"), PM(() => string.Format("{0}{1}", s, o)), PM(() => (object)s == (object)o), PM(() => o?.ToString() ?? "coalesced"), PM(() => ((Demo94.Pt?)null).HasValue), PM(() => ((int?)null) + 1), PM(() => ((int?)null) == 1), PM(() => ((int?)null) != 1), PM(() => ((int?)null) < 1), PM(() => ((int?)null) > 1), PM(() => ((int?)null) <= 1), PM(() => ((int?)null) >= 1), PM(() => ((int?)null) == ((int?)null)), PM(() => ((int?)null) < ((int?)null)), PM(() => ((bool?)null) & true), PM(() => ((bool?)null) & false), PM(() => ((bool?)null) | true), PM(() => ((bool?)null) | false), PM(() => ((bool?)null) ^ true), PM(() => !((bool?)null)), PM(() => ((bool?)null) == false), PM(() => ((int?)null).GetValueOrDefault(5)), PM(() => (object)((int?)null) is int), PM(() => (object)((int?)null) is int?), PM(() => (object)((int?)null) is ValueType), PM(() => (object)((int?)null) is object), PM(() => (object)((int?)5) is int?), PM(() => ((object)((int?)5)).GetType().Name), PM(() => typeof(int?).Name), PM(() => Nullable.GetUnderlyingType(typeof(int?)).Name), PM(() => Nullable.GetUnderlyingType(typeof(int)) == null), PM(() => Nullable.Compare<int>(null, 1)), PM(() => Nullable.Compare<int>(null, null)), PM(() => Nullable.Compare<int>(1, null)), PM(() => Nullable.Equals<int>(null, null)), PM(() => Nullable.Equals<int>(null, 0)), PM(() => default(int?).HasValue), PM(() => new int?().HasValue), PM(() => new int?(5).Value), PM(() => ((int?)5).Equals(5)), PM(() => ((int?)5).Equals((object)5)), PM(() => ((int?)5).Equals(5L)), PM(() => ((int?)5).GetHashCode() == 5.GetHashCode()), PM(() => ((int?)5).ToString()), PM(() => ((double?)0.1).ToString()), PM(() => ((int?)5) + ((int?)null)), PM(() => (((int?)5) + ((int?)null)).HasValue), PM(() => ((int?)5) ?? 7), PM(() => ((int?)null) ?? 7), PM(() => ((int?)null) ?? ((int?)null)), PM(() => (((int?)null) ?? ((int?)null)).HasValue), PM(() => ((int?)null) ?? ((int?)3) ?? 9));
        }

        static string CastExceptions()
        {
            object i = I(5); object l = 5L; object s = "str"; object d = 5.0; object b = (byte)5; object e = Demo94.Col.G; object n = null; object arr = new int[1]; object pt = new Demo94.Pt(); object np = (int?)5; object nn = (int?)null; object ch = 'a'; object bo = true;
            return Par.J(PM(() => (long)i), PM(() => (int)l), PM(() => (short)i), PM(() => (uint)i), PM(() => (double)i), PM(() => (int)d), PM(() => (float)d), PM(() => (int)b), PM(() => (byte)i), PM(() => (int)e), PM(() => (Demo94.Col)i), PM(() => (Demo94.ColByte)b), PM(() => (Demo94.ColByte)i), PM(() => (Demo94.Col)e), PM(() => (Demo94.ColByte)e), PM(() => (int)s), PM(() => (string)i), PM(() => (string)n), PM(() => (int)n), PM(() => (int?)n), PM(() => (int?)i), PM(() => (long?)i), PM(() => (int)np), PM(() => (int?)np), PM(() => (int)nn), PM(() => (int?)nn), PM(() => (long?)np), PM(() => (int[])arr), PM(() => (uint[])arr), PM(() => (long[])arr), PM(() => (object[])arr), PM(() => (Array)arr), PM(() => (System.Collections.IList)arr), PM(() => (IList<int>)arr), PM(() => (IList<uint>)arr), PM(() => (IList<long>)arr), PM(() => (Demo94.Pt)pt), PM(() => (Demo94.Pt?)pt), PM(() => (ValueType)pt), PM(() => (IComparable)i), PM(() => (IComparable<int>)i), PM(() => (IComparable<long>)i), PM(() => (IFormattable)i), PM(() => (IConvertible)s), PM(() => (IDisposable)s), PM(() => (int)ch), PM(() => (char)i), PM(() => (ushort)ch), PM(() => (int)bo), PM(() => (bool)i), PM(() => (Enum)e), PM(() => (Enum)i), PM(() => (ValueType)i), PM(() => (ValueType)s), PM(() => (IEnumerable<char>)s), PM(() => (IEnumerable<object>)s), PM(() => (System.Collections.IEnumerable)s), PM(() => (ICloneable)s), PM(() => (ICloneable)arr), PM(() => (ICloneable)i), PM(() => (Exception)s), PM(() => (Delegate)s), PM(() => (Type)s), PM(() => i as string), PM(() => s as string), PM(() => i as int?), PM(() => l as int?), PM(() => e as Demo94.Col?), PM(() => i as Demo94.Col?), PM(() => e as int?), PM(() => arr as uint[]), PM(() => arr as object[]), PM(() => arr as IList<uint>), PM(() => i is long), PM(() => i is int), PM(() => i is IComparable), PM(() => i is IComparable<int>), PM(() => i is IComparable<long>), PM(() => i is ValueType), PM(() => i is Enum), PM(() => e is int), PM(() => e is Enum), PM(() => e is IConvertible), PM(() => e is IFormattable), PM(() => b is int), PM(() => b is byte), PM(() => np is int), PM(() => np is int?), PM(() => nn is int?), PM(() => nn is object), PM(() => arr is uint[]), PM(() => arr is IList<uint>), PM(() => arr is IReadOnlyList<int>), PM(() => arr is System.Collections.IList), PM(() => arr is ICloneable), PM(() => pt is ValueType), PM(() => pt is IComparable), PM(() => s is IComparable), PM(() => s is IComparable<string>), PM(() => s is IEnumerable<char>), PM(() => s is ICloneable), PM(() => s is IConvertible), PM(() => n is object), PM(() => n is string), PM(() => n is int?), PM(() => ch is int), PM(() => ch is IComparable<char>), PM(() => bo is IConvertible), PM(() => d is IComparable<double>), PM(() => d is IFormattable), PM(() => i is IEquatable<int>), PM(() => s is IEquatable<string>), PM(() => i is ISpanFormattable), PM(() => i is IUtf8SpanFormattable), PM(() => d is IParsable<double>), PM(() => i is System.Numerics.INumber<int>), PM(() => i is System.Numerics.IBinaryInteger<int>), PM(() => ch is System.Numerics.INumber<char>));
        }

        static string ArithmeticExceptionTypes()
        {
            int z = I(0); long lz = 0; uint uz = 0; decimal dz = 0; double big = 1e100 + I(0), nan = double.NaN + I(0), e30 = 1e30 + I(0), inf = double.PositiveInfinity + I(0), b3 = 3000000000.0 + I(0); decimal dmax = decimal.MaxValue - dz;
            return Par.J(PM(() => I(1) / z), PM(() => I(1) % z), PM(() => 1L / lz), PM(() => 1UL / uz), PM(() => 1m / dz), Par.K(() => PM(() => I(int.MinValue) / I(-1))), Par.K(() => PM(() => I(int.MinValue) % I(-1))), Par.K(() => PM(() => long.MinValue / (long)I(-1))), Par.K(() => PM(() => long.MinValue % (long)I(-1))), PM(() => checked(I(int.MaxValue) + 1)), PM(() => checked(I(int.MinValue) - 1)), PM(() => checked(I(int.MaxValue) * 2)), PM(() => checked(-I(int.MinValue))), PM(() => checked((int)(3000000000L + I(0)))), PM(() => checked((int)nan)), PM(() => checked((byte)I(256))), PM(() => checked((uint)I(-1))), PM(() => checked((ulong)(-1.0 + I(0)))), PM(() => Math.Abs(I(int.MinValue))), PM(() => Math.Abs(long.MinValue)), PM(() => Math.Abs((short)short.MinValue)), PM(() => Math.Abs((sbyte)sbyte.MinValue)), PM(() => dmax + 1m), PM(() => dmax * 2), PM(() => (int)dmax), PM(() => (decimal)nan), PM(() => (decimal)inf), PM(() => (decimal)e30), PM(() => decimal.Parse("1e30")), PM(() => decimal.Round(1.5m, 29)), PM(() => decimal.Round(1.5m, -1)), PM(() => Math.Round(1.5, 16)), PM(() => Math.Round(1.5, -1)), PM(() => Math.Round(1.5m, 29)), PM(() => Math.Round(1.5, (MidpointRounding)99)), PM(() => Math.Round(1.5m, 2, (MidpointRounding)99)), PM(() => Math.Sign(double.NaN)), PM(() => Math.Sign(float.NaN)), PM(() => Math.Clamp(1, 3, 2)), PM(() => Math.Clamp(1.0, 3.0, 2.0)), PM(() => Math.Clamp(1.0, double.NaN, 2.0)), PM(() => Math.Clamp(1.0, 0.0, double.NaN)), PM(() => Math.DivRem(1, z)), Par.K(() => PM(() => Math.DivRem(I(int.MinValue), I(-1)))), PM(() => Math.BigMul(1, 1, out _)), PM(() => Math.Log(2, 1)), PM(() => Math.ScaleB(1, int.MaxValue)), PM(() => Math.ILogB(0.0)), PM(() => Math.ILogB(double.NaN)), PM(() => Math.ILogB(double.PositiveInfinity)), PM(() => Math.IEEERemainder(1, 0)), PM(() => Math.Pow(0, -1)), PM(() => Math.Sqrt(-1)), PM(() => Math.Log(-1)), PM(() => Math.Acos(2)), PM(() => (int)big), PM(() => (long)big), PM(() => (int)(float)nan), PM(() => unchecked((byte)(1e10 + I(0)))), PM(() => int.Parse("2147483648")), PM(() => int.Parse("-2147483649")), PM(() => int.Parse("abc")), PM(() => long.Parse("9223372036854775808")), PM(() => byte.Parse("256")), PM(() => byte.Parse("-1")), PM(() => sbyte.Parse("128")), PM(() => uint.Parse("-1")), PM(() => ulong.Parse("18446744073709551616")), PM(() => decimal.Parse("79228162514264337593543950336")), PM(() => double.Parse("1e400")), PM(() => float.Parse("1e40")), PM(() => int.Parse("1e400")), PM(() => Convert.ToInt32(b3)), PM(() => Convert.ToInt32("3000000000")), PM(() => Convert.ToByte(-1)), PM(() => Convert.ToUInt32(-1)), PM(() => Convert.ToInt32(double.NaN)), PM(() => Convert.ToInt32(double.PositiveInfinity)), PM(() => Convert.ToInt32(long.MaxValue)), PM(() => Convert.ToInt32(ulong.MaxValue)), PM(() => Convert.ToInt32(decimal.MaxValue)), PM(() => Convert.ToDecimal(double.MaxValue)), PM(() => Convert.ToDecimal(double.NaN)), PM(() => Convert.ToSingle(double.MaxValue)), PM(() => Convert.ToChar(70000)), PM(() => Convert.ToChar(-1)), PM(() => Convert.ToChar(1.5)), PM(() => Convert.ToBoolean(1.5)), PM(() => Convert.ToBoolean("1")), PM(() => Convert.ToBoolean('t')), PM(() => Convert.ToInt32('a')), PM(() => Convert.ToInt32(true)), PM(() => Convert.ToDateTime(5)), PM(() => Convert.ToInt32(DateTime.Now)), PM(() => Convert.ToDouble("1,5")), PM(() => Convert.ToDouble("1.5")), Par.H(() => PM(() => new int[I(int.MaxValue)])), PM(() => new int[I(-1)]), PM(() => new int[I(1), I(-1)]), PM(() => Array.CreateInstance(typeof(int), -1)), PM(() => new string('a', -1)), Par.H(() => PM(() => new string('a', int.MaxValue))), PM(() => "a".PadLeft(-1)), Par.H(() => PM(() => "a".PadLeft(int.MaxValue))), PM(() => new List<int>(-1)), Par.H(() => PM(() => new List<int>(int.MaxValue))), PM(() => new Dictionary<int, int>(-1)), PM(() => new System.Text.StringBuilder(-1)), Par.H(() => PM(() => new System.Text.StringBuilder(int.MaxValue))), Par.H(() => PM(() => new System.Text.StringBuilder().Append('a', int.MaxValue))), PM(() => new System.Text.StringBuilder().Append('a', -1)), PM(() => System.GC.GetTotalMemory(false) > 0), VM(() => System.GC.KeepAlive(null)), PM(() => System.GC.GetGeneration(new object()) >= 0), PM(() => System.GC.GetGeneration((object)null)), PM(() => System.GC.CollectionCount(-1)), PM(() => System.GC.CollectionCount(0) >= 0), PM(() => System.GC.MaxGeneration >= 0), VM(() => System.GC.AddMemoryPressure(0)), VM(() => System.GC.AddMemoryPressure(-1)), VM(() => System.GC.SuppressFinalize(null)), VM(() => System.GC.ReRegisterForFinalize(null)), VM(() => System.GC.WaitForPendingFinalizers()), PM(() => { System.GC.Collect(); return "collected"; }), VM(() => System.GC.Collect(-1)), VM(() => System.GC.Collect(int.MaxValue)), VM(() => System.GC.Collect(0, (GCCollectionMode)99)));
        }
    }

    static class Ext95
    {
        public static int First2(this List<int> l) { if (l.Count == 0) throw new InvalidOperationException("Sequence contains no elements"); return l[0]; }
        public static int Add2(this System.Collections.ObjectModel.ReadOnlyCollection<int> r) { ((IList<int>)r).Add(1); return r.Count; }
        public static string ToString2(this Demo94.Node n) { return n.ToString(); }
        public static int Length2(this int[] a) { return a.Length; }
    }
}
