using System;
using System.Collections.Generic;
using ParityKit;

// 95: exception semantigi — try/catch/finally sirasi, filter'lar (2-pass unwinding), rethrow, finally'de exception,
// using/Dispose, static ctor hatasi (TypeInitializationException), standart exception Message metinleri.
namespace Demo95
{
    class MyEx : Exception { public int Code; public MyEx(string m, int c) : base(m) { Code = c; } public MyEx(string m, Exception inner) : base(m, inner) { } }
    class DerivedEx : MyEx { public DerivedEx() : base("derived", 7) { } }

    class Res : IDisposable { public static List<string> Log = new List<string>(); string n; bool throwOnDispose; public Res(string n, bool t = false) { this.n = n; throwOnDispose = t; Log.Add("open:" + n); } public void Dispose() { Log.Add("dispose:" + n); if (throwOnDispose) throw new InvalidOperationException("dispose-" + n); } }

    // BULGU: AOT cctor'lari program basinda EAGER kosar ve cctor exception'i sureci abort eder (exit 134, TypeInitializationException yok).
    // Testi kilitlememek icin cctor yalniz Arm.On iken firlatir (.NET lazy: step icinde Arm sonrasi ilk erisim -> firlatir; AOT eager: Arm=false -> firlatmaz -> parite FAIL).
    static class Arm { public static bool On; }
    static class BadStatic { public static int V = Init(); static int Init() { if (Arm.On) throw new InvalidOperationException("cctor boom"); return -1; } }
    static class BadStatic2 { static BadStatic2() { if (Arm.On) throw new ArgumentException("explicit cctor"); } public static int V = 5; public static void Touch() { } }
    class BadInstance { static BadInstance() { if (Arm.On) throw new MyEx("inst cctor", 3); } public int X = 1; }
    static class GoodAfterBad { public static int Count; static GoodAfterBad() { Count++; } public static void Touch() { } }

    partial class App95
    {
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static int I(int v) { return v; }
        static string P(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string PM(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return e.GetType().Name + ":" + e.Message; } }
        static string V(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }
        static string VM(Action a) { try { a(); return "ok"; } catch (Exception e) { return e.GetType().Name + ":" + e.Message; } }
        static List<string> log = new List<string>();
        static void L(string s) { log.Add(s); }
        static string Flush() { var s = string.Join(",", log); log.Clear(); return s; }

        public static int Run() { return Par.Loop(95, 26, Step); }

        static string Step(int k)
        {
            log.Clear(); Res.Log.Clear();
            switch (k)
            {
                case 1: return NestedOrder();
                case 2: return Rethrow();
                case 3: return Filters();
                case 4: return FilterOrder();
                case 5: return FinallyThrows();
                case 6: return ReturnInTryFinally();
                case 7: return LoopControlFinally();
                case 8: return UsingSemantics();
                case 9: return StaticCtorFailure();
                case 10: return StandardMessages();
                case 11: return StandardMessages2();
                case 12: return UnwindThroughGeneric();
                case 13: return UnwindThroughDelegate();
                case 14: return UnwindThroughIterator();
                case 15: return CatchOrder();
                case 16: return ExceptionProps();
                case 17: return InnerAndAggregate();
                case 18: return NullRefForms();
                case 19: return CastExceptions();
                case 20: return ArithmeticExceptionTypes();
                case 21: return TryCatchInCatch();
                case 22: return GotoFinally();
                case 23: return ExceptionInFilterSwallowed();
                case 24: return DeepNesting();
                case 25: return ThrowExpressions();
                case 26: return CustomExceptionBehaviour();
            }
            return "?";
        }

        static string NestedOrder()
        {
            try
            {
                L("t1");
                try { L("t2"); throw new MyEx("x", 1); }
                catch (ArgumentException) { L("wrong"); }
                finally { L("f2"); }
                L("unreached");
            }
            catch (MyEx e) { L("c1:" + e.Code); }
            finally { L("f1"); }
            try { try { L("a"); } finally { L("fa"); } L("after"); } finally { L("fo"); }
            try { try { throw new Exception("e"); } finally { L("inner-finally"); } } catch (Exception) { L("outer-catch"); } finally { L("outer-finally"); }
            try { try { throw new Exception("e"); } catch (Exception) { L("ic"); throw; } finally { L("if"); } } catch (Exception) { L("oc"); } finally { L("of"); }
            try { try { throw new Exception("e"); } catch (Exception) { L("ic2"); throw new MyEx("new", 2); } finally { L("if2"); } } catch (MyEx e) { L("oc2:" + e.Message); }
            return Flush();
        }

        static string Rethrow()
        {
            string t1, t2, t3, t4 = "unset";
            try { try { throw new DerivedEx(); } catch (MyEx) { throw; } } catch (Exception e) { t1 = e.GetType().Name + ":" + e.Message; }
            try { try { throw new DerivedEx(); } catch (MyEx e) { throw e; } } catch (Exception e) { t2 = e.GetType().Name; }
            try { try { throw new DerivedEx(); } catch (MyEx e) { throw new MyEx("wrap", e); } } catch (MyEx e) { t3 = e.Message + "/" + e.InnerException.GetType().Name + "/" + ((MyEx)e.InnerException).Code; }
            try { try { throw new DerivedEx(); } catch (MyEx e) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e).Throw(); } } catch (Exception e) { t4 = e.GetType().Name; }
            Exception saved = null; try { throw new MyEx("saved", 9); } catch (MyEx e) { saved = e; }
            string t5 = PM(() => { throw saved; }); string t6 = VM(() => System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(saved));
            string t7 = PM(() => { try { throw new Exception("a"); } catch { throw; } });
            string t8 = PM(() => { try { throw new Exception("a"); } catch (Exception) when (false) { throw; } });
            string t9 = PM(() => { try { throw new Exception("a"); } catch (ArgumentException) { throw; } });
            object o = new MyEx("as-object", 1); string t10 = PM(() => { throw (Exception)o; });
            string t11 = PM(() => { Exception n = null; throw n; }); // null throw -> NullReferenceException
            string t12 = PM(() => { throw null; });
            return Par.J(Q(() => t1), Q(() => t2), Q(() => t3), Q(() => t4), Q(() => t5), Q(() => t6), Q(() => t7), Q(() => t8), Q(() => t9), Q(() => t10), Q(() => t11), Q(() => t12), Q(() => saved.Message), Q(() => ReferenceEquals(saved, saved)));
        }

        static string Filters()
        {
            string r1 = PM(() => { try { throw new MyEx("a", 5); } catch (MyEx e) when (e.Code > 3) { return "big:" + e.Code; } return "no"; });
            string r2 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (e.Code > 3) { return "big"; } catch (MyEx e) when (e.Code > 0) { return "small:" + e.Code; } });
            string r3 = PM(() => { try { throw new MyEx("a", 0); } catch (MyEx e) when (e.Code > 3) { return "big"; } catch (MyEx e) when (e.Code > 0) { return "small"; } });
            string r4 = PM(() => { try { throw new MyEx("a", 0); } catch (MyEx e) when (e.Code > 3) { return "big"; } catch (Exception) { return "any"; } });
            string r5 = PM(() => { try { throw new DerivedEx(); } catch (MyEx e) when (e is DerivedEx) { return "derived-filter"; } catch (MyEx) { return "base"; } });
            string r6 = PM(() => { try { throw new Exception("m"); } catch (Exception e) when (e.Message == "m") { return "msg"; } return "no"; });
            string r7 = PM(() => { try { throw new Exception("m"); } catch (Exception e) when (e.Message == "x") { return "msg"; } return "no"; });
            int side = 0; string r8 = PM(() => { try { throw new Exception("m"); } catch (Exception) when (++side > 0) { return "side:" + side; } return "no"; });
            string r9 = PM(() => { try { throw new Exception("m"); } catch (Exception) when (Log("f1") && false) { return "1"; } catch (Exception) when (Log("f2") && false) { return "2"; } catch (Exception) when (Log("f3")) { return "3:" + Flush(); } return "no"; });
            string r10 = PM(() => { try { try { throw new Exception("m"); } catch (Exception) when (Log("inner-filter") && false) { return "inner"; } } catch (Exception) when (Log("outer-filter")) { return "outer:" + Flush(); } return "no"; });
            string r11 = PM(() => { try { throw new MyEx("c", 2); } catch (MyEx e) when (e.Code == 2) { throw new ArgumentException("from-catch"); } });
            string r12 = PM(() => { try { throw new MyEx("c", 2); } catch (MyEx e) when (e.Code == 2) { throw; } });
            string r13 = PM(() => { try { int z = I(0); return 1 / z; } catch (DivideByZeroException) when (I(1) == 1) { return -1; } return 0; });
            string r14 = PM(() => { try { throw new Exception(); } catch when (true) { return "catch-when-no-type"; } return "no"; });
            string r15 = PM(() => { try { throw new Exception(); } catch when (false) { return "no"; } catch { return "fallback"; } });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => side), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15));
        }

        static bool Log(string s) { L(s); return true; }

        static string FilterOrder()
        {
            // 2-pass unwinding: filter'lar, ic finally bloklarindan ONCE kosar
            log.Clear();
            try { try { try { L("throw"); throw new Exception("x"); } finally { L("finally-inner"); } } finally { L("finally-mid"); } }
            catch (Exception) when (Log("filter-outer")) { L("catch-outer"); }
            string o1 = Flush();
            try { try { L("throw"); throw new Exception("x"); } catch (Exception) when (Log("filter-inner") && false) { L("no"); } finally { L("finally-inner"); } }
            catch (Exception) when (Log("filter-outer")) { L("catch-outer"); }
            string o2 = Flush();
            try { try { L("throw"); throw new Exception("x"); } finally { L("finally-inner"); } }
            catch (Exception) when (Log("filter-1") && false) { L("c1"); }
            catch (Exception) when (Log("filter-2")) { L("c2"); }
            string o3 = Flush();
            try { using (new Res("r")) { L("body"); throw new Exception("x"); } } catch (Exception) when (Log("filter")) { L("catch"); }
            string o4 = Flush() + "|" + string.Join(",", Res.Log); Res.Log.Clear();
            int state = 0;
            try { try { state = 1; throw new Exception("x"); } finally { state = 2; } } catch (Exception) when (CheckState(ref state)) { L("state-in-catch:" + state); }
            string o5 = Flush();
            try { try { throw new Exception("x"); } catch (Exception) { L("inner-catch"); throw; } finally { L("inner-finally"); } } catch (Exception) when (Log("outer-filter")) { L("outer-catch"); }
            string o6 = Flush();
            try { Thrower(3); } catch (Exception) when (Log("filter-depth")) { L("caught"); }
            string o7 = Flush();
            return Par.J(Q(() => o1), Q(() => o2), Q(() => o3), Q(() => o4), Q(() => o5), Q(() => o6), Q(() => o7));
        }

        static bool CheckState(ref int s) { L("filter-sees-state:" + s); return true; }
        static void Thrower(int d) { try { if (d == 0) throw new Exception("deep"); Thrower(d - 1); } finally { L("fin" + d); } }

        static string FinallyThrows()
        {
            string r1 = PM(() => { try { throw new ArgumentException("orig"); } finally { throw new InvalidOperationException("from-finally"); } });
            string r2 = PM(() => { try { throw new ArgumentException("orig"); } catch (ArgumentException) { throw new MyEx("from-catch", 1); } finally { throw new InvalidOperationException("from-finally2"); } });
            string r3 = PM(() => { try { return "ret"; } finally { throw new InvalidOperationException("finally-after-return"); } });
            string r4 = PM(() => { try { try { throw new ArgumentException("orig"); } finally { L("f-inner"); } } catch (ArgumentException e) { return "caught:" + e.Message + "/" + Flush(); } return "no"; });
            string r5 = PM(() => { try { try { throw new ArgumentException("orig"); } finally { throw new MyEx("replace", 1); } } catch (ArgumentException) { return "orig-caught"; } catch (MyEx) { return "replace-caught"; } return "no"; });
            string r6 = PM(() => { try { try { throw new ArgumentException("orig"); } finally { try { throw new MyEx("in-finally", 1); } catch (MyEx) { L("handled-in-finally"); } } } catch (ArgumentException) { return "orig-survives:" + Flush(); } return "no"; });
            string r7 = PM(() => { try { throw new ArgumentException("orig"); } catch (ArgumentException) { try { throw new MyEx("in-catch", 1); } finally { L("f-in-catch"); } } });
            string r8 = PM(() => { int x = 0; try { x = 1; } finally { x = 2; } return x; });
            string r9 = PM(() => { try { throw new Exception("a"); } catch (Exception e) when (e.Message == "a") { throw new Exception("b"); } finally { L("fin"); } });
            string r10 = PM(() => { try { throw new Exception("e1"); } catch { try { throw new Exception("e2"); } catch { throw new Exception("e3"); } finally { L("f2"); } } finally { L("f1"); } });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => Flush()), Q(() => r8), Q(() => r9), Q(() => Flush()), Q(() => r10), Q(() => Flush()));
        }

        static string ReturnInTryFinally()
        {
            return Par.J(Q(() => RetTry()), Q(() => RetFinallyMod()), Q(() => RetRef()), Q(() => RetCatch()), Q(() => RetNested()), Q(() => RetLoop()), Q(() => RetStructMod()), Q(() => RetObjMod()), Q(() => Flush()));
        }
        static int RetTry() { int x = 1; try { return x; } finally { x = 2; L("rt:" + x); } }
        static int RetFinallyMod() { int x = 1; try { x = 5; return x; } finally { x = 99; } }
        static string RetRef() { var arr = new[] { 1 }; try { return "v" + arr[0]; } finally { arr[0] = 7; L("arr:" + arr[0]); } }
        static int RetCatch() { try { throw new Exception(); } catch { return 10; } finally { L("rc"); } }
        static int RetNested() { try { try { return 1; } finally { L("in"); } } finally { L("out"); } }
        static int RetLoop() { int s = 0; for (int i = 0; i < 3; i++) { try { if (i == 1) return s; s += i; } finally { s += 100; L("loop" + i + ":" + s); } } return -1; }
        static string RetStructMod() { var p = new Demo94.Pt { X = 1 }; try { return p.ToString(); } finally { p.X = 9; } }
        static string RetObjMod() { var sb = new System.Text.StringBuilder("a"); try { return sb.ToString(); } finally { sb.Append("b"); L("sb:" + sb); } }

        static string LoopControlFinally()
        {
            for (int i = 0; i < 3; i++) { try { if (i == 1) continue; if (i == 2) break; L("body" + i); } finally { L("fin" + i); } }
            foreach (var x in new[] { 1, 2, 3 }) { try { if (x == 2) break; L("fe" + x); } finally { L("ff" + x); } }
            int n = 0; while (true) { try { n++; if (n > 2) break; } finally { L("w" + n); } }
            try { for (int i = 0; i < 5; i++) { try { if (i == 2) throw new Exception("at2"); L("i" + i); } finally { L("fi" + i); } } } catch (Exception e) { L("caught:" + e.Message); }
            int depth = 0; try { Recurse(ref depth, 3); } catch (Exception) { L("rec-caught"); }
            try { try { throw new Exception(); } finally { for (int i = 0; i < 2; i++) { try { L("ffor" + i); } finally { L("ffin" + i); } } } } catch { L("c"); }
            string sw = PM(() => { switch (I(1)) { case 1: try { return "case1"; } finally { L("swfin"); } default: return "def"; } });
            return Par.J(Q(() => Flush()), Q(() => sw), Q(() => Flush()));
        }
        static void Recurse(ref int d, int max) { try { d++; if (d >= max) throw new Exception(); Recurse(ref d, max); } finally { L("rf" + d); } }

        static string UsingSemantics()
        {
            using (var r = new Res("a")) { L("body-a"); }
            using (new Res("b")) { }
            using (Res n = null) { L("null-body"); }
            using (var r1 = new Res("c1")) using (var r2 = new Res("c2")) { L("both"); }
            using (Res x = new Res("d1"), y = new Res("d2")) { }
            string e1 = PM(() => { using (new Res("e")) { throw new Exception("body-ex"); } });
            string e2 = VM(() => { using (new Res("f", true)) { L("f-body"); } });
            string e3 = PM(() => { using (new Res("g", true)) { throw new Exception("body-ex-g"); } }); // Dispose exception orijinali EZER
            string e4 = PM(() => { using (new Res("h")) { return "returned"; } });
            string e5 = PM(() => { try { using (new Res("i", true)) { } } catch (InvalidOperationException ex) { return "caught:" + ex.Message; } return "no"; });
            { using var u1 = new Res("u1"); using var u2 = new Res("u2"); L("decl-body"); }
            string e6 = PM(() => { using var bad = new Res("u3", true); L("u3-body"); return "ret-u3"; });
            IDisposable boxedStruct = new DispStruct(); boxedStruct.Dispose(); boxedStruct.Dispose();
            var ds = new DispStruct(); using (ds) { ds.Touch(); } // kopya: struct using
            string e7 = PM(() => { using (var d2 = new DispStruct()) { d2.Touch(); return DispStruct.Count; } });
            string e8 = PM(() => { using ((IDisposable)null) { } return "null-iface"; });
            string e9 = VM(() => { using (var r = new Res("j")) { r.Dispose(); L("after-manual"); } }); // cift Dispose
            var enumer = ((IEnumerable<int>)new List<int> { 1 }).GetEnumerator(); using (enumer) { } using (enumer) { }
            return Par.J(Q(() => string.Join(",", Res.Log)), Q(() => e1), Q(() => e2), Q(() => e3), Q(() => e4), Q(() => e5), Q(() => e6), Q(() => DispStruct.Count), Q(() => e7), Q(() => e8), Q(() => e9), Q(() => Flush()));
        }
        struct DispStruct : IDisposable { public static int Count; public int local; public void Touch() { local++; } public void Dispose() { Count++; local += 100; } }

        static string StaticCtorFailure()
        {
            Arm.On = true;
            string a1 = PM(() => BadStatic.V), a2 = PM(() => BadStatic.V), a3 = PM(() => { BadStatic2.Touch(); return "touched"; }), a4 = PM(() => BadStatic2.V), a5 = PM(() => new BadInstance().X), a6 = PM(() => new BadInstance()), a7 = PM(() => typeof(BadStatic).Name), a8 = PM(() => Activator.CreateInstance(typeof(BadInstance))), a9 = PM(() => Activator.CreateInstance<BadInstance>()), a10 = VM(() => System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(BadStatic2).TypeHandle));
            string inner1 = "", inner2 = "", inner3 = "";
            try { var v = BadStatic.V; } catch (TypeInitializationException e) { inner1 = e.InnerException?.GetType().Name + ":" + e.InnerException?.Message + "|" + e.TypeName + "|" + e.Message; }
            try { BadStatic2.Touch(); } catch (TypeInitializationException e) { inner2 = e.InnerException?.GetType().Name + ":" + e.InnerException?.Message; }
            try { new BadInstance(); } catch (TypeInitializationException e) { inner3 = e.InnerException?.GetType().Name + "|" + ((MyEx)e.InnerException).Code; }
            GoodAfterBad.Touch(); GoodAfterBad.Touch();
            string g1 = PM(() => Generic<int>.V), g2 = PM(() => Generic<int>.V), g3 = PM(() => Generic<string>.V), g4 = PM(() => Generic<long>.V);
            return Par.J(Q(() => a1), Q(() => a2), Q(() => a3), Q(() => a4), Q(() => a5), Q(() => a6), Q(() => a7), Q(() => a8), Q(() => a9), Q(() => a10), Q(() => inner1), Q(() => inner2), Q(() => inner3), Q(() => GoodAfterBad.Count), Q(() => g1), Q(() => g2), Q(() => g3), Q(() => g4), PM(() => BadStatic.V) == a1, Q(() => typeof(BadStatic).TypeInitializer == null), Q(() => typeof(BadStatic2).TypeInitializer != null), Q(() => typeof(GoodAfterBad).TypeInitializer != null), Q(() => typeof(App95).TypeInitializer != null));
        }
        static class Generic<T> { public static int V = Init(); static int Init() { if (Demo95.Arm.On && typeof(T) == typeof(int)) throw new InvalidOperationException("int-cctor"); return typeof(T).Name.Length; } }

        static string StandardMessages()
        {
            int[] arr = new int[1]; string s = null; object o = "str"; var d = new Dictionary<int, int>(); var l = new List<int>(); int z = I(0);
            return Par.J(PM(() => arr[5]), PM(() => s.Length), PM(() => (int)o), PM(() => d[5]), PM(() => l[0]), PM(() => 1 / z), PM(() => checked(int.MaxValue + I(1))), PM(() => int.Parse("abc")), PM(() => int.Parse("")), PM(() => int.Parse((string)null)), PM(() => int.Parse("99999999999")), PM(() => "abc".Substring(5)), PM(() => "abc".Substring(1, 5)), PM(() => new int[I(-1)]), PM(() => Enum.Parse(typeof(Demo94.Col), "Nope")), PM(() => Convert.ToInt32("x")), PM(() => Convert.ToInt32(3000000000.0)), PM(() => Convert.ToByte(300)), PM(() => new List<int>().First2()), PM(() => new Queue<int>().Dequeue()), PM(() => new Stack<int>().Pop()), VM(() => new Dictionary<int, int> { { 1, 1 } }.Add(1, 2)), VM(() => new object[1].SetValue("x", 0)), VM(() => ((object[])new string[1])[0] = 5), PM(() => ((System.Collections.IList)new int[1]).Add(1)), VM(() => ((IList<int>)new int[1]).Add(1)), PM(() => new List<int> { 1 }.AsReadOnly().Add2()), PM(() => Activator.CreateInstance(typeof(string))), PM(() => Activator.CreateInstance(typeof(Abstract95))), PM(() => Activator.CreateInstance(typeof(IDisposable))), PM(() => Activator.CreateInstance(typeof(int[]))), PM(() => Activator.CreateInstance(typeof(NoDefault))), PM(() => typeof(NoDefault).GetConstructor(Type.EmptyTypes).Invoke(null)), PM(() => new ArgumentNullException("p")), PM(() => { throw new ArgumentNullException("p"); }), PM(() => { throw new ArgumentNullException("p", "custom"); }), PM(() => { throw new ArgumentNullException(); }), PM(() => { throw new ArgumentNullException(null, "only-message"); }), PM(() => { throw new ArgumentException("msg", "pname"); }), PM(() => { throw new ArgumentException("msg"); }), PM(() => { throw new ArgumentException(); }), PM(() => { throw new ArgumentOutOfRangeException("p"); }), PM(() => { throw new ArgumentOutOfRangeException("p", "custom"); }), PM(() => { throw new ArgumentOutOfRangeException("p", 42, "custom"); }), PM(() => { throw new ArgumentOutOfRangeException("p", null, "custom"); }), PM(() => { throw new ArgumentOutOfRangeException(); }), VM(() => ArgumentNullException.ThrowIfNull((object)null, "x")), VM(() => ArgumentOutOfRangeException.ThrowIfNegative(I(-1), "n")), VM(() => ArgumentOutOfRangeException.ThrowIfGreaterThan(I(5), 3, "g")), VM(() => ArgumentOutOfRangeException.ThrowIfZero(I(0))), VM(() => ArgumentException.ThrowIfNullOrEmpty("", "e")), VM(() => ArgumentException.ThrowIfNullOrWhiteSpace(" ", "w")), VM(() => ObjectDisposedException.ThrowIf(true, new object())), PM(() => { throw new ObjectDisposedException("objname"); }), PM(() => { throw new ObjectDisposedException("objname", "msg"); }), PM(() => { throw new ObjectDisposedException(null); }));
        }
        abstract class Abstract95 { }
        class NoDefault { public NoDefault(int x) { } }

        static string StandardMessages2()
        {
            return Par.J(PM(() => { throw new Exception(); }), PM(() => { throw new Exception(null); }), PM(() => { throw new Exception(""); }), PM(() => { throw new InvalidOperationException(); }), PM(() => { throw new InvalidCastException(); }), PM(() => { throw new NullReferenceException(); }), PM(() => { throw new IndexOutOfRangeException(); }), PM(() => { throw new DivideByZeroException(); }), PM(() => { throw new OverflowException(); }), PM(() => { throw new FormatException(); }), PM(() => { throw new NotSupportedException(); }), PM(() => { throw new NotImplementedException(); }), PM(() => { throw new KeyNotFoundException(); }), PM(() => { throw new ArithmeticException(); }), PM(() => { throw new OutOfMemoryException(); }), PM(() => { throw new StackOverflowException(); }), PM(() => { throw new TimeoutException(); }), PM(() => { throw new OperationCanceledException(); }), PM(() => { throw new System.Threading.Tasks.TaskCanceledException(); }), PM(() => { throw new ObjectDisposedException("x"); }), PM(() => { throw new ArrayTypeMismatchException(); }), PM(() => { throw new RankException(); }), PM(() => { throw new TypeInitializationException("T", null); }), PM(() => { throw new TypeInitializationException(null, null); }), PM(() => { throw new MissingMethodException(); }), PM(() => { throw new MissingMethodException("C", "M"); }), PM(() => { throw new MemberAccessException(); }), PM(() => { throw new FieldAccessException(); }), PM(() => { throw new MethodAccessException(); }), PM(() => { throw new TypeLoadException(); }), PM(() => { throw new EntryPointNotFoundException(); }), PM(() => { throw new DllNotFoundException(); }), PM(() => { throw new BadImageFormatException(); }), PM(() => { throw new ApplicationException(); }), PM(() => { throw new SystemException(); }), PM(() => { throw new InvalidProgramException(); }), PM(() => { throw new AccessViolationException(); }), PM(() => { throw new InsufficientMemoryException(); }), PM(() => { throw new PlatformNotSupportedException(); }), PM(() => { throw new UnauthorizedAccessException(); }), PM(() => { throw new System.IO.IOException(); }), PM(() => { throw new System.IO.FileNotFoundException(); }), PM(() => { throw new System.IO.FileNotFoundException("m", "file.txt"); }), PM(() => { throw new System.IO.EndOfStreamException(); }), PM(() => { throw new System.IO.DirectoryNotFoundException(); }), PM(() => { throw new System.IO.PathTooLongException(); }), PM(() => { throw new AggregateException(); }), PM(() => { throw new AggregateException(new Exception("a"), new Exception("b")); }), PM(() => { throw new AggregateException("msg", new Exception("a")); }), PM(() => { throw new System.Collections.Generic.KeyNotFoundException("k"); }), PM(() => { throw new InvalidTimeZoneException(); }), PM(() => { throw new DuplicateWaitObjectException(); }), PM(() => { throw new System.Threading.SynchronizationLockException(); }), PM(() => { throw new System.Threading.ThreadStateException(); }), PM(() => { throw new System.Threading.SemaphoreFullException(); }), PM(() => { throw new System.Threading.LockRecursionException(); }), PM(() => { throw new System.Reflection.TargetInvocationException(new Exception("inner")); }), PM(() => { throw new System.Reflection.TargetInvocationException(null); }), PM(() => { throw new System.Reflection.AmbiguousMatchException(); }), PM(() => { throw new System.Reflection.TargetParameterCountException(); }), PM(() => { throw new System.Runtime.Serialization.SerializationException(); }), PM(() => { throw new System.Runtime.InteropServices.COMException(); }), PM(() => { throw new System.Runtime.InteropServices.ExternalException(); }), PM(() => { throw new System.Runtime.InteropServices.MarshalDirectiveException(); }), PM(() => { throw new System.Security.SecurityException(); }), PM(() => { throw new System.Security.VerificationException(); }), PM(() => { throw new DataMisalignedException(); }), PM(() => { throw new ContextMarshalException(); }), PM(() => { throw new System.Text.DecoderFallbackException(); }), PM(() => { throw new System.Text.EncoderFallbackException(); }), PM(() => { throw new System.Globalization.CultureNotFoundException(); }), PM(() => { throw new System.Globalization.CultureNotFoundException("p", "xx-YY", "m"); }), PM(() => { throw new UriFormatException(); }), PM(() => { throw new System.Net.WebException(); }), PM(() => { throw new System.Net.Sockets.SocketException(); }), PM(() => { throw new System.Net.Http.HttpRequestException(); }), PM(() => { throw new System.Text.RegularExpressions.RegexParseException2(); }), PM(() => { throw new System.Text.Json.JsonException(); }));
        }
    }
}

namespace System.Text.RegularExpressions { class RegexParseException2 : Exception { } }
