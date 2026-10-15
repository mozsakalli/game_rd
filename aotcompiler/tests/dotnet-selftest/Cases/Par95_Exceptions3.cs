using System;
using System.Collections.Generic;
using ParityKit;

// 95 (devam 2)
namespace Demo95
{
    partial class App95
    {
        static string TryCatchInCatch()
        {
            string r1 = PM(() => { try { throw new Exception("e1"); } catch (Exception e1) { try { throw new Exception("e2"); } catch (Exception e2) { return e1.Message + "+" + e2.Message; } } });
            string r2 = PM(() => { try { throw new Exception("e1"); } catch (Exception e1) { try { throw new Exception("e2"); } catch (Exception e2) { throw; } } });
            string r3 = PM(() => { try { throw new Exception("e1"); } catch (Exception) { try { throw new Exception("e2"); } catch (Exception) { } throw; } }); // dis throw; e1'i yeniden firlatir
            string r4 = PM(() => { try { throw new Exception("e1"); } catch (Exception) { try { throw new Exception("e2"); } finally { L("f-in-catch"); } } });
            string r5 = PM(() => { try { throw new Exception("e1"); } catch (Exception e1) { try { throw new Exception("e2", e1); } catch (Exception e2) when (e2.InnerException == e1) { return "chained"; } } });
            string r6 = PM(() => { try { throw new Exception("e1"); } finally { try { throw new Exception("e2"); } catch { L("swallow-in-finally"); } } });
            string r7 = PM(() => { try { throw new Exception("e1"); } catch (Exception e) { try { return "ret-from-inner:" + e.Message; } finally { L("inner-fin"); } } finally { L("outer-fin"); } });
            string r8 = PM(() => { int depth = 0; try { throw new Exception("0"); } catch (Exception a) { depth++; try { throw new Exception("1", a); } catch (Exception b) { depth++; try { throw new Exception("2", b); } catch (Exception c) { depth++; return depth + ":" + c.GetBaseException().Message; } } } });
            string r9 = PM(() => { try { try { throw new Exception("e1"); } catch (Exception) { throw new Exception("e2"); } } catch (Exception e) { return e.Message + "/" + (e.InnerException == null); } });
            string r10 = PM(() => { try { try { throw new MyEx("e1", 1); } catch (MyEx) { throw; } } catch (MyEx e) { try { throw new MyEx("e2", 2); } catch (MyEx e2) { throw new MyEx("e3", e2); } } });
            string r11 = PM(() => { try { throw new Exception("e1"); } catch (Exception) { try { } finally { L("empty-try-fin"); } return "after"; } });
            string r12 = PM(() => { try { throw new Exception("e1"); } catch (Exception) { try { throw new Exception("e2"); } catch (ArgumentException) { return "no"; } } }); // ic catch eslesmez: e2 disari
            string r13 = PM(() => { try { try { throw new Exception("e1"); } catch (Exception) { try { throw new Exception("e2"); } catch (ArgumentException) { return "no"; } } } catch (Exception e) { return "outer:" + e.Message; } });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => Flush()));
        }

        static string GotoFinally()
        {
            int i = 0;
        again:
            try { i++; if (i < 3) goto again; L("try" + i); } finally { L("fin" + i); }
            try { goto outside; } finally { L("goto-out-fin"); }
        outside:
            L("outside");
            for (int k = 0; k < 3; k++) { try { if (k == 1) goto done; L("k" + k); } finally { L("kf" + k); } }
        done:
            string sw = PM(() => { switch (I(2)) { case 1: L("c1"); goto case 2; case 2: try { L("c2"); goto case 3; } finally { L("c2fin"); } case 3: return "c3"; default: return "d"; } });
            string r = PM(() => { try { try { goto end; } finally { L("in-fin"); } } finally { L("out-fin"); } end: return "jumped"; });
            string r2 = PM(() => { int x = 0; try { x = 1; goto skip; } finally { x = 2; } skip: return x; });
            string r3 = PM(() => { while (true) { try { break; } finally { L("brk-fin"); } } return "after-break"; });
            string r4 = PM(() => { int n = 0; do { try { n++; continue; } finally { L("cont" + n); } } while (n < 2); return n; });
            string r5 = PM(() => { foreach (var x in new[] { 1, 2, 3 }) { try { try { if (x == 2) break; } finally { L("i" + x); } } finally { L("o" + x); } } return "fe"; });
            string r6 = PM(() => { try { return Lbl(); } finally { L("call-fin"); } });
            return Par.J(Q(() => Flush()), Q(() => sw), Q(() => Flush()), Q(() => r), Q(() => Flush()), Q(() => r2), Q(() => r3), Q(() => Flush()), Q(() => r4), Q(() => Flush()), Q(() => r5), Q(() => Flush()), Q(() => r6), Q(() => Flush()));
        }
        static string Lbl() { int c = 0; start: c++; try { if (c < 2) goto start; } finally { L("lbl" + c); } return "lbl:" + c; }

        static string ExceptionInFilterSwallowed()
        {
            string r1 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (Throws()) { return "caught-by-throwing-filter"; } catch (MyEx) { return "fallback"; } });
            string r2 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (Throws()) { return "no"; } });
            string r3 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (e.Message.Length > 100 || NullDeref()) { return "no"; } catch (Exception e) { return "fb:" + e.GetType().Name; } });
            string r4 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (ThrowsAndLogs("f1")) { return "no1"; } catch (MyEx e) when (ThrowsAndLogs("f2")) { return "no2"; } catch (MyEx e) when (Log("f3")) { return "yes3:" + Flush(); } });
            string r5 = PM(() => { try { try { throw new MyEx("a", 1); } catch (MyEx) when (Throws()) { return "inner"; } } catch (MyEx e) { return "outer:" + e.Message; } });
            string r6 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx e) when (e.Code == I(1) && Throws()) { return "no"; } catch (Exception) { return "generic"; } });
            string r7 = PM(() => { try { try { throw new MyEx("a", 1); } finally { L("fin-before-filter?"); } } catch (MyEx) when (ThrowsAndLogs("filter")) { return "no"; } catch (MyEx) { return "fb:" + Flush(); } });
            string r8 = PM(() => { try { throw new MyEx("a", 1); } catch (MyEx) when (StackOverflowish(0)) { return "no"; } catch (MyEx) { return "deep-filter-ex-swallowed"; } });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => Flush()));
        }
        static bool Throws() { throw new InvalidOperationException("filter-ex"); }
        static bool NullDeref() { string s = null; return s.Length > 0; }
        static bool ThrowsAndLogs(string s) { L(s); throw new InvalidOperationException(s); }
        static bool StackOverflowish(int d) { if (d > 50) throw new InvalidOperationException("deep"); return StackOverflowish(d + 1); }

        static string DeepNesting()
        {
            string r1 = PM(() => Deep(0, 20)); string f1 = Flush();
            string r2 = PM(() => DeepCatch(0, 10)); string f2 = Flush();
            string r3 = PM(() => { try { Deep(0, 100); } catch (MyEx e) { return "caught:" + e.Code; } return "no"; }); string f3 = Flush().Length.ToString();
            string r4 = PM(() => DeepRethrow(0, 5)); string f4 = Flush();
            string r5 = PM(() => DeepFilter(0, 5)); string f5 = Flush();
            string r6 = PM(() => { int n = 0; try { for (int i = 0; i < 100; i++) { try { try { if (i % 7 == 6) throw new MyEx("i" + i, i); n++; } catch (MyEx e) when (e.Code % 2 == 0) { n += 1000; } } catch (MyEx) { n += 100000; } } } finally { n += 10000000; } return n; });
            string r7 = PM(() => { int count = 0; for (int i = 0; i < 1000; i++) { try { throw new MyEx("x", i); } catch (MyEx e) { count += e.Code; } } return count; });
            string r8 = PM(() => { int count = 0; for (int i = 0; i < 300; i++) { try { try { throw new MyEx("x", i); } finally { count++; } } catch (MyEx) { count += 2; } } return count; });
            string r9 = PM(() => { string s = ""; try { try { try { try { throw new MyEx("4", 4); } catch (ArgumentException) { s += "a"; } } catch (InvalidOperationException) { s += "b"; } } catch (MyEx e) when (e.Code != 4) { s += "c"; } } catch (MyEx e) { s += "d" + e.Code; } return s; });
            string r10 = PM(() => Recursive(0));
            return Par.J(Q(() => r1), Q(() => f1), Q(() => r2), Q(() => f2), Q(() => r3), Q(() => f3), Q(() => r4), Q(() => f4), Q(() => r5), Q(() => f5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => Flush()));
        }
        static string Deep(int d, int max) { try { if (d == max) throw new MyEx("bottom", d); return Deep(d + 1, max); } finally { if (d % 5 == 0) L("f" + d); } }
        static string DeepCatch(int d, int max) { try { if (d == max) throw new MyEx("bottom", d); return DeepCatch(d + 1, max); } catch (MyEx e) when (d == 3) { return "caught-at-3:" + e.Code; } finally { L("f" + d); } }
        static string DeepRethrow(int d, int max) { try { if (d == max) throw new MyEx("b", d); return DeepRethrow(d + 1, max); } catch (MyEx e) { L("c" + d); if (d == 0) return "top:" + e.Code; throw; } }
        static string DeepFilter(int d, int max) { try { if (d == max) throw new MyEx("b", d); return DeepFilter(d + 1, max); } catch (MyEx e) when (Log("flt" + d) && d == 0) { return "top"; } finally { L("fin" + d); } }
        static string Recursive(int d) { if (d > 200) throw new MyEx("deep", d); try { return Recursive(d + 1); } catch (MyEx e) when (d == 100) { return "unwound-100:" + e.Code; } }

        static string ThrowExpressions()
        {
            string s = null; object o = null; int? n = null;
            string r1 = PM(() => s ?? throw new ArgumentNullException("s"));
            string r2 = PM(() => (s ?? "x") ?? throw new ArgumentNullException("s"));
            string r3 = PM(() => I(1) > 0 ? "pos" : throw new InvalidOperationException("neg"));
            string r4 = PM(() => I(-1) > 0 ? "pos" : throw new InvalidOperationException("neg"));
            string r5 = PM(() => n ?? throw new MyEx("n", 1));
            string r6 = PM(() => { Func<int> f = () => throw new MyEx("lam", 2); return f(); });
            string r7 = PM(() => { Func<int, int> f = x => x > 0 ? x : throw new MyEx("lam2", x); return f(I(-5)); });
            string r8 = PM(() => ExprBodied());
            string r9 = PM(() => new WithThrowCtor(null));
            string r10 = PM(() => new WithThrowCtor("ok").V);
            string r11 = PM(() => new WithThrowProp().P);
            string r12 = PM(() => { var w = new WithThrowProp(); w.P = 5; return "set"; });
            string r13 = PM(() => new WithThrowProp()[0]);
            string r14 = PM(() => (o as string) ?? throw new MyEx("as", 3));
            string r15 = PM(() => (o is string str ? str : throw new MyEx("is", 4)));
            string r16 = PM(() => o switch { string x => x, int x => "int", _ => throw new MyEx("sw", 5) });
            string r17 = PM(() => I(5) switch { 1 => "one", 2 => "two", _ => throw new ArgumentOutOfRangeException("v", I(5), null) });
            string r18 = PM(() => I(5) switch { 1 => "one", 2 => "two" }); // SwitchExpressionException
            string r19 = PM(() => ((object)null) switch { string x => x }); // null -> SwitchExpressionException
            string r20 = PM(() => Demo94.Col.R switch { Demo94.Col.G => "g" });
            string r21 = PM(() => ((Demo94.Col)I(9)) switch { Demo94.Col.R => "r", Demo94.Col.G => "g", Demo94.Col.B => "b" });
            string r22 = PM(() => (I(1), I(2)) switch { (1, 1) => "11" });
            string r23 = PM(() => { try { return I(5) switch { 1 => "one" }; } catch (System.Runtime.CompilerServices.SwitchExpressionException e) { return "SEE:" + e.UnmatchedValue + ":" + e.Message; } });
            string r24 = PM(() => { try { return ((object)null) switch { string x => x }; } catch (System.Runtime.CompilerServices.SwitchExpressionException e) { return "SEE:" + (e.UnmatchedValue == null) + ":" + e.Message; } });
            string r25 = PM(() => { try { return (I(1), "a") switch { (2, _) => "x" }; } catch (System.Runtime.CompilerServices.SwitchExpressionException e) { return "SEE:" + (e.UnmatchedValue == null) + ":" + e.Message; } });
            string r26 = PM(() => { try { return new Demo94.Pt { X = 3 } switch { { X: 1 } => "1" }; } catch (System.Runtime.CompilerServices.SwitchExpressionException e) { return "SEE:" + e.UnmatchedValue + ":" + e.Message; } });
            string r27 = PM(() => { int x = I(1); return x switch { > 0 => "pos", < 0 => "neg" }; }); // 0 eksik ama x=1
            string r28 = PM(() => { int x = I(0); return x switch { > 0 => "pos", < 0 => "neg" }; });
            string r29 = PM(() => { double d = double.NaN; return d switch { > 0 => "pos", < 0 => "neg", 0 => "zero" }; });
            string r30 = PM(() => { double d = double.NaN; return d switch { > 0 => "pos", < 0 => "neg", 0 => "zero", double.NaN => "nan" }; });
            string r31 = PM(() => { double d = -0.0; return d switch { 0 => "zero", _ => "other" }; });
            string r32 = PM(() => { double d = double.NaN; return d switch { not (> 0 or < 0 or 0) => "nan-by-elimination", _ => "x" }; });
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => r17), Q(() => r18), Q(() => r19), Q(() => r20), Q(() => r21), Q(() => r22), Q(() => r23), Q(() => r24), Q(() => r25), Q(() => r26), Q(() => r27), Q(() => r28), Q(() => r29), Q(() => r30), Q(() => r31), Q(() => r32));
        }
        static string ExprBodied() => throw new MyEx("expr", 6);
        class WithThrowCtor { public string V; public WithThrowCtor(string v) { V = v ?? throw new ArgumentNullException(nameof(v)); } }
        class WithThrowProp { public int P { get => throw new MyEx("get", 7); set => throw new MyEx("set", 8); } public int this[int i] => throw new IndexOutOfRangeException(); }

        static string CustomExceptionBehaviour()
        {
            var e = new MyEx("m", 1);
            var ov = new OverrideEx();
            var noMsg = new NoMessageEx();
            string r1 = PM(() => ov.Message), r2 = PM(() => ov.ToString()), r3 = PM(() => ov.StackTrace), r4 = PM(() => ov.Source), r5 = PM(() => ov.HelpLink), r6 = PM(() => ov.Data.Count), r7 = PM(() => ov.GetBaseException().GetType().Name), r8 = PM(() => { throw ov; }), r9 = PM(() => noMsg.Message), r10 = PM(() => { throw noMsg; }), r11 = PM(() => new DataEx().Data["key"]), r12 = PM(() => new DataEx().Data.Count);
            string r13 = PM(() => { try { throw new OverrideEx(); } catch (Exception x) { return x.StackTrace != null; } });
            string r14 = PM(() => { try { throw new OverrideEx(); } catch (Exception x) { return x.Message; } });
            string r15 = PM(() => new MyEx("a", 1).Equals(new MyEx("a", 1)));
            string r16 = PM(() => { var x = new MyEx("a", 1); return x.Equals(x); });
            string r17 = PM(() => new MyEx("a", 1).GetHashCode() == new MyEx("a", 1).GetHashCode());
            string r18 = PM(() => e.GetType().IsSerializable);
            string r19 = PM(() => e.GetType().GetProperty("Message").GetValue(e));
            string r20 = PM(() => e.GetType().GetField("Code").GetValue(e));
            string r21 = PM(() => typeof(Exception).GetProperty("Message").GetValue(ov));
            string r22 = PM(() => typeof(Exception).GetProperty("Message").DeclaringType.Name);
            string r23 = PM(() => ov.GetType().GetProperty("Message").DeclaringType.Name);
            string r24 = PM(() => new ThrowingToString().ToString());
            string r25 = PM(() => { try { throw new ThrowingToString(); } catch (Exception x) { return x.Message; } });
            string r26 = PM(() => { try { throw new ThrowingMessage(); } catch (Exception x) { try { return x.Message; } catch (Exception y) { return "msg-threw:" + y.GetType().Name; } } });
            string r27 = PM(() => { try { throw new ThrowingMessage(); } catch (Exception x) { try { return x.ToString(); } catch (Exception y) { return "tostring-threw:" + y.GetType().Name; } } });
            string r28 = PM(() => string.Format("{0}", new ThrowingMessage()));
            string r29 = PM(() => { try { throw new ThrowingMessage(); } catch (Exception x) when (x.Message == "x") { return "no"; } catch (Exception) { return "filter-threw-swallowed"; } });
            string r30 = PM(() => new SelfInner().InnerException == null);
            string r31 = PM(() => new NullMessageEx().Message);
            string r32 = PM(() => new NullMessageEx().ToString());
            string r33 = PM(() => new EmptyMessageEx().Message.Length);
            string r34 = PM(() => new EmptyMessageEx().ToString());
            string r35 = PM(() => new Exception("a\nb").ToString().Replace("\r", "\\r").Replace("\n", "\\n"));
            string r36 = PM(() => new Exception("a", new Exception("b\nc")).ToString().Replace("\r", "\\r").Replace("\n", "\\n"));
            return Par.J(Q(() => r1), Q(() => r2), Q(() => r3), Q(() => r4), Q(() => r5), Q(() => r6), Q(() => r7), Q(() => r8), Q(() => r9), Q(() => r10), Q(() => r11), Q(() => r12), Q(() => r13), Q(() => r14), Q(() => r15), Q(() => r16), Q(() => r17), Q(() => r18), Q(() => r19), Q(() => r20), Q(() => r21), Q(() => r22), Q(() => r23), Q(() => r24), Q(() => r25), Q(() => r26), Q(() => r27), Q(() => r28), Q(() => r29), Q(() => r30), Q(() => r31), Q(() => r32), Q(() => r33), Q(() => r34), Q(() => r35), Q(() => r36));
        }
        // BULGU: corelib Exception'da Source/HelpLink/Data/GetBaseException virtual degil -> override AOT derlemesini TUMDEN durdurur ("override edilecek virtual yok"); bu yuzden burada yalniz Message/ToString/StackTrace.
        class OverrideEx : Exception { public override string Message => "overridden"; public override string ToString() { return "custom-tostring"; } public override string StackTrace => "fake-stack"; public override string Source { get => "fake-src"; set { } } public override string HelpLink { get => "help"; set { } } public override System.Collections.IDictionary Data => new Dictionary<string, string> { { "a", "b" } }; public override Exception GetBaseException() { return new ArgumentException("fake-base"); } }
        class NoMessageEx : Exception { }
        class DataEx : Exception { public DataEx() { Data["key"] = "val"; } }
        class ThrowingToString : Exception { public override string ToString() { throw new InvalidOperationException("ts"); } }
        class ThrowingMessage : Exception { public override string Message => throw new InvalidOperationException("msg"); }
        class SelfInner : Exception { public SelfInner() : base("self", null) { } }
        class NullMessageEx : Exception { public NullMessageEx() : base(null) { } }
        class EmptyMessageEx : Exception { public EmptyMessageEx() : base("") { } }
    }
}
