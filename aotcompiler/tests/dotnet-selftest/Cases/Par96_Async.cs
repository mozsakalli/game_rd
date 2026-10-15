using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ParityKit;

// 96: async/await + iterator semantigi — exception await'i gecer, try/finally icinde await, faulted task, WhenAll coklu fault,
// Result vs await vs GetResult, TaskCanceled, custom awaiter, async lambda/local, status bayraklari, iterator finally/Dispose.
namespace Demo96
{
    class MyEx : Exception { public int Code; public MyEx(string m, int c) : base(m) { Code = c; } }

    class App96
    {
        static int I(int v) { return v; }
        static string Q(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return Par.Ex(e); } }
        static string QV(Action a) { try { a(); return "ok"; } catch (Exception e) { return Par.Ex(e); } }
        static string QM(Func<object> f) { try { var r = f(); return r == null ? "null" : r.ToString(); } catch (Exception e) { return e.GetType().Name + ":" + e.Message; } }
        static List<string> log = new List<string>();
        static void L(string s) { log.Add(s); }
        static string Flush() { var s = string.Join(",", log); log.Clear(); return s; }
        static List<TaskCompletionSource<int>> pending = new List<TaskCompletionSource<int>>();
        static Task<int> Job() { var t = new TaskCompletionSource<int>(); pending.Add(t); return t.Task; }
        static void Pump() { var b = pending; pending = new List<TaskCompletionSource<int>>(); for (int i = 0; i < b.Count; i++) b[i].TrySetResult(i + 1); }
        static void PumpAll(Task root) { int n = 0; while (!root.IsCompleted && n++ < 100) Pump(); }
        static string Done(Task t) { PumpAll(t); return t.Status + "/" + t.IsCompleted + "/" + t.IsCompletedSuccessfully + "/" + t.IsFaulted + "/" + t.IsCanceled; }
        static string Res<T>(Task<T> t) { PumpAll(t); try { return t.Result.ToString(); } catch (AggregateException e) { return "AGG:" + e.InnerException.GetType().Name + ":" + e.InnerException.Message + "/" + e.InnerExceptions.Count; } }
        static string Await<T>(Task<T> t) { PumpAll(t); try { return t.GetAwaiter().GetResult().ToString(); } catch (Exception e) { return e.GetType().Name + ":" + e.Message; } }

        public static int Run() { return Par.Loop(96, 20, Step); }

        static string Step(int k)
        {
            log.Clear(); pending.Clear();
            switch (k)
            {
                case 1: return SyncCompletedAwait();
                case 2: return DeferredAwait();
                case 3: return ExceptionBeforeFirstAwait();
                case 4: return ExceptionAfterAwait();
                case 5: return TryCatchAroundAwait();
                case 6: return TryFinallyWithAwait();
                case 7: return AwaitInFinally();
                case 8: return WhenAllFaults();
                case 9: return ResultVsAwait();
                case 10: return Cancellation();
                case 11: return CustomAwaiter();
                case 12: return AsyncLambdaLocal();
                case 13: return DeepChain();
                case 14: return TaskStatics();
                case 15: return ValueTaskBasics();
                case 16: return ContinuationOrder();
                case 17: return IteratorFinallyDispose();
                case 18: return IteratorLazyOrder();
                case 19: return IteratorExceptions();
                case 20: return AsyncLoopAccumulate();
            }
            return "?";
        }

        static async Task<int> Sync3() { L("s-start"); await Task.CompletedTask; L("s-after"); return 3; }
        static async Task<int> SyncFromResult() { var v = await Task.FromResult(7); return v * 2; }
        static string SyncCompletedAwait()
        {
            var t = Sync3(); string f1 = Flush();
            return Par.J(Q(() => t.IsCompleted), Q(() => t.Status), Q(() => t.Result), f1, Q(() => Await(SyncFromResult())), Q(() => Done(Task.CompletedTask)), Q(() => Done(Task.FromResult(1))), Q(() => Task.CompletedTask.IsCompletedSuccessfully), Q(() => ReferenceEquals(Task.CompletedTask, Task.CompletedTask)), Q(() => Task.FromResult(5).GetAwaiter().IsCompleted), Q(() => Task.CompletedTask.GetAwaiter().IsCompleted));
        }

        static async Task<int> Deferred() { L("d0"); var a = await Job(); L("d1:" + a); var b = await Job(); L("d2:" + b); return a + b; }
        static string DeferredAwait()
        {
            var t = Deferred(); string s0 = t.Status + "|" + Flush();
            Pump(); string s1 = t.Status + "|" + Flush();
            Pump(); string s2 = t.Status + "|" + t.Result + "|" + Flush();
            var t2 = Deferred(); PumpAll(t2);
            return Par.J(s0, s1, s2, Q(() => t2.Result), Q(() => Done(t2)), Flush());
        }

        static async Task<int> ThrowBefore() { L("tb"); throw new MyEx("before", 1); }
        static async Task<int> ThrowBeforeWithAwait() { L("tbw"); if (I(1) == 1) throw new MyEx("before2", 2); await Job(); return 0; }
        static string ExceptionBeforeFirstAwait()
        {
            string callSite = QM(() => { var t = ThrowBefore(); return "no-throw-at-call:" + t.IsFaulted + "/" + t.Status; });
            var t2 = ThrowBefore();
            string ex1 = Q(() => t2.Exception.GetType().Name + "/" + t2.Exception.InnerException.GetType().Name + "/" + t2.Exception.InnerExceptions.Count);
            string r = Res(t2), a = Await(t2);
            var t3 = ThrowBeforeWithAwait();
            return Par.J(callSite, Flush(), ex1, r, a, Q(() => t3.IsFaulted), Q(() => Await(t3)), Q(() => t3.Exception.InnerException.Message), Q(() => Done(t3)), Q(() => t2.IsCompletedSuccessfully), Q(() => t2.IsCanceled), Q(() => t2.Exception == t2.Exception), Flush());
        }

        static async Task<int> ThrowAfter() { L("ta0"); await Job(); L("ta1"); throw new MyEx("after", 3); }
        static async Task<int> ThrowAfterTwo() { await Job(); await Job(); throw new InvalidOperationException("after2"); }
        static string ExceptionAfterAwait()
        {
            var t = ThrowAfter(); string s0 = t.Status + "|" + t.IsFaulted; Pump(); string s1 = t.Status + "|" + t.IsFaulted + "|" + Flush();
            var t2 = ThrowAfterTwo(); Pump(); string s2 = t2.Status.ToString(); Pump(); string s3 = t2.Status.ToString();
            return Par.J(s0, s1, Res(t), Await(t), s2, s3, Res(t2), Await(t2), Q(() => t2.Exception.InnerException.GetType().Name), Q(() => Done(t2)));
        }

        static async Task<string> CatchAwaited(bool sync) { try { if (sync) { await ThrowBefore(); } else { await ThrowAfter(); } return "no"; } catch (MyEx e) { L("caught:" + e.Message); return "caught:" + e.Code; } }
        static async Task<string> CatchFiltered() { try { await ThrowAfter(); return "no"; } catch (MyEx e) when (e.Code == 3) { return "filtered"; } }
        static async Task<string> CatchWrongType() { try { await ThrowAfter(); return "no"; } catch (ArgumentException) { return "wrong"; } }
        static async Task<string> CatchRethrow() { try { await ThrowAfter(); return "no"; } catch (MyEx) { L("rethrow"); throw; } }
        static async Task<string> CatchThrowNew() { try { await ThrowAfter(); return "no"; } catch (MyEx e) { throw new InvalidOperationException("wrapped", e); } }
        static async Task<string> AwaitInCatch() { try { throw new MyEx("c", 1); } catch (MyEx) { L("in-catch-before"); var v = await Job(); L("in-catch-after:" + v); return "awaited-in-catch:" + v; } }
        static async Task<string> NestedTry() { try { try { await ThrowAfter(); } finally { L("inner-fin"); } return "no"; } catch (MyEx) { L("outer-catch"); return "nested"; } finally { L("outer-fin"); } }
        static string TryCatchAroundAwait()
        {
            return Par.J(Q(() => Await(CatchAwaited(true))), Flush(), Q(() => Await(CatchAwaited(false))), Flush(), Q(() => Await(CatchFiltered())), Q(() => Await(CatchWrongType())), Q(() => Await(CatchRethrow())), Flush(), Q(() => Await(CatchThrowNew())), Q(() => { var t = CatchThrowNew(); PumpAll(t); return t.Exception.InnerException.InnerException.GetType().Name; }), Q(() => Await(AwaitInCatch())), Flush(), Q(() => Await(NestedTry())), Flush());
        }

        static async Task<int> FinallyNormal() { try { L("t"); var v = await Job(); L("after:" + v); return v; } finally { L("fin"); } }
        static async Task<int> FinallyThrow() { try { L("t"); await Job(); throw new MyEx("in-try", 1); } finally { L("fin"); } }
        static async Task<int> FinallyBeforeAwaitThrow() { try { throw new MyEx("pre", 2); } finally { L("fin-pre"); } }
        static async Task<int> FinallyOrder() { try { try { await Job(); L("a"); throw new MyEx("x", 1); } finally { L("inner"); await Job(); L("inner-after-await"); } } catch (MyEx) { L("catch"); } finally { L("outer"); } return 9; }
        static async Task<int> FinallyReturnOverride() { int x = 1; try { await Job(); x = 5; return x; } finally { x = 99; L("x=" + x); } }
        static string TryFinallyWithAwait()
        {
            var t1 = FinallyNormal(); string s1 = Flush(); PumpAll(t1); string s2 = Flush();
            var t2 = FinallyThrow(); string s3 = Flush(); PumpAll(t2); string s4 = Flush();
            return Par.J(Q(() => t1.Result), s1, s2, s3, s4, Q(() => Await(t2)), Q(() => Await(FinallyBeforeAwaitThrow())), Flush(), Q(() => Await(FinallyOrder())), Flush(), Q(() => Await(FinallyReturnOverride())), Flush());
        }

        static async Task<int> AwaitFinally() { try { L("body"); return 1; } finally { L("fin-start"); var v = await Job(); L("fin-end:" + v); } }
        static async Task<int> AwaitFinallyThrows() { try { throw new MyEx("body", 1); } finally { L("fin-start"); await Job(); L("fin-end"); } }
        static async Task<int> AwaitFinallyThenThrowInFinally() { try { throw new MyEx("body", 1); } finally { await Job(); L("fin-throw"); throw new InvalidOperationException("from-finally"); } }
        static async Task<int> UsingAsyncDispose() { await using (var d = new AsyncDisp()) { L("body"); await Job(); L("body2"); } return 1; }
        static async Task<int> UsingAsyncDisposeThrow() { await using (var d = new AsyncDisp()) { await Job(); throw new MyEx("in-using", 1); } }
        class AsyncDisp : IAsyncDisposable { public async ValueTask DisposeAsync() { L("dispose-start"); await Job(); L("dispose-end"); } }
        static string AwaitInFinally()
        {
            var t1 = AwaitFinally(); string s1 = t1.Status + "|" + Flush(); PumpAll(t1); string s2 = t1.Result + "|" + Flush();
            var t2 = AwaitFinallyThrows(); string s3 = t2.Status + "|" + Flush(); PumpAll(t2); string s4 = Await(t2) + "|" + Flush();
            return Par.J(s1, s2, s3, s4, Q(() => Await(AwaitFinallyThenThrowInFinally())), Flush(), Q(() => Await(UsingAsyncDispose())), Flush(), Q(() => Await(UsingAsyncDisposeThrow())), Flush());
        }

        static string WhenAllFaults()
        {
            var a = ThrowAfter(); var b = ThrowAfterTwo(); var c = Deferred();
            var all = Task.WhenAll(a, b, c); PumpAll(all);
            string viaAwait = Await(all.ContinueWith2()); 
            string viaRes = ""; try { all.GetAwaiter().GetResult(); } catch (Exception e) { viaRes = e.GetType().Name + ":" + e.Message; }
            string agg = Q(() => all.Exception.InnerExceptions.Count + "/" + all.Exception.InnerExceptions[0].GetType().Name + "/" + all.Exception.InnerExceptions[1].GetType().Name);
            var all2 = Task.WhenAll(ThrowAfter(), ThrowAfter()); PumpAll(all2); string two = Q(() => all2.Exception.InnerExceptions.Count);
            var all3 = Task.WhenAll(Deferred(), Deferred()); PumpAll(all3); string ok = Q(() => string.Join(",", all3.Result));
            var all4 = Task.WhenAll(new Task<int>[0]); string empty = Q(() => all4.IsCompletedSuccessfully + "/" + all4.Result.Length);
            var any = Task.WhenAny(ThrowAfter(), Deferred()); PumpAll(any); string anyS = Q(() => any.Result.IsFaulted + "/" + any.Status);
            var any2 = Task.WhenAny(Deferred(), Task.FromResult(42)); string any2S = Q(() => any2.Result.Result + "/" + any2.IsCompleted);
            string anyEmpty = Q(() => Task.WhenAny(new Task[0]));
            var tcs = new TaskCompletionSource<int>(); tcs.SetException(new MyEx("tcs", 1)); string tcsE = Q(() => tcs.Task.Exception.InnerException.Message + "/" + tcs.TrySetException(new Exception()) + "/" + tcs.TrySetResult(1));
            string setTwice = Q(() => { var t = new TaskCompletionSource<int>(); t.SetResult(1); t.SetResult(2); return "no"; });
            var tcs2 = new TaskCompletionSource<int>(); tcs2.SetException(new[] { new MyEx("a", 1), new MyEx("b", 2) }); string multi = Q(() => tcs2.Task.Exception.InnerExceptions.Count + "/" + Await(tcs2.Task));
            string setNull = QV(() => new TaskCompletionSource<int>().SetException((Exception)null));
            string setEmpty = QV(() => new TaskCompletionSource<int>().SetException(new Exception[0]));
            return Par.J(Q(() => all.Status), viaRes, agg, two, ok, empty, anyS, any2S, anyEmpty, tcsE, setTwice, multi, setNull, setEmpty, Q(() => all.IsFaulted), Q(() => Task.WhenAll(Task.CompletedTask, Task.CompletedTask).IsCompletedSuccessfully), Q(() => Task.WhenAll((Task[])null)), Q(() => Task.WhenAll(new Task[] { null })));
        }

        static string ResultVsAwait()
        {
            var f = ThrowAfter(); PumpAll(f);
            string r1 = Q(() => f.Result), r2 = QM(() => f.Result), r3 = Await(f), r4 = QV(() => f.Wait()), r5 = QM(() => { f.Wait(); return 1; }), r6 = Q(() => f.Exception.GetType().Name), r7 = Q(() => f.Exception.InnerException.Message), r8 = Q(() => f.Exception.GetBaseException().Message), r9 = Q(() => f.Exception.Flatten().InnerExceptions.Count);
            var ok = Deferred(); PumpAll(ok); string r10 = Q(() => ok.Result), r11 = Q(() => ok.Exception == null), r12 = Q(() => { ok.Wait(); return "waited"; }), r13 = Q(() => ok.GetAwaiter().GetResult());
            var pend = Job(); string r14 = Q(() => pend.Wait(0)), r15 = Q(() => pend.Status), r16 = Q(() => pend.GetAwaiter().IsCompleted);
            var tcsC = new TaskCompletionSource<int>(); tcsC.SetCanceled(); string r17 = Q(() => tcsC.Task.Result), r18 = QM(() => tcsC.Task.Result), r19 = Await(tcsC.Task), r20 = Q(() => tcsC.Task.Exception == null), r21 = Q(() => tcsC.Task.Status), r22 = QM(() => { tcsC.Task.Wait(); return 1; });
            // DIKKAT: bekleyen task'ta .Result/.Wait() tek thread'de DEADLOCK -> burada asla.
            return Par.J(r1, r2, r3, r4, r5, r6, r7, r8, r9, r10, r11, r12, r13, r14, r15, r16, r17, r18, r19, r20, r21, r22);
        }

        static async Task<int> Cancellable(CancellationToken ct) { L("c0"); await Job(); ct.ThrowIfCancellationRequested(); L("c1"); await Job(); ct.ThrowIfCancellationRequested(); L("c2"); return 1; }
        static async Task<int> AwaitCanceledTask() { var tcs = new TaskCompletionSource<int>(); tcs.SetCanceled(); try { await tcs.Task; } catch (TaskCanceledException e) { return 100 + (e.Task == tcs.Task ? 1 : 0); } return -1; }
        static async Task<int> AwaitCanceledTaskOce() { var tcs = new TaskCompletionSource<int>(); tcs.SetCanceled(); try { await tcs.Task; } catch (OperationCanceledException) { return 200; } return -1; }
        static string Cancellation()
        {
            var cts = new CancellationTokenSource(); var t = Cancellable(cts.Token); Pump(); cts.Cancel(); PumpAll(t);
            string s1 = Flush(), s2 = Done(t), s3 = Await(t), s4 = Q(() => t.Exception.InnerException.GetType().Name), s5 = Res(t);
            var cts2 = new CancellationTokenSource(); cts2.Cancel(); var t2 = Cancellable(cts2.Token); PumpAll(t2); string s6 = Done(t2) + "|" + Flush();
            var cts3 = new CancellationTokenSource(); var t3 = Cancellable(cts3.Token); PumpAll(t3); string s7 = Done(t3) + "|" + t3.Result;
            string s8 = Q(() => Await(AwaitCanceledTask())), s9 = Q(() => Await(AwaitCanceledTaskOce()));
            var ct = new CancellationToken(true); string s10 = Q(() => ct.IsCancellationRequested + "/" + ct.CanBeCanceled), s11 = QM(() => { ct.ThrowIfCancellationRequested(); return "no"; }), s12 = Q(() => CancellationToken.None.CanBeCanceled + "/" + CancellationToken.None.IsCancellationRequested);
            var cts4 = new CancellationTokenSource(); int cbCount = 0; var reg = cts4.Token.Register(() => { cbCount++; L("cb1"); }); cts4.Token.Register(() => L("cb2")); cts4.Cancel(); cts4.Cancel(); string s13 = cbCount + "|" + Flush(); reg.Dispose(); var cts5 = new CancellationTokenSource(); var reg2 = cts5.Token.Register(() => L("never")); reg2.Dispose(); cts5.Cancel(); string s14 = Flush(); var cts6 = new CancellationTokenSource(); cts6.Cancel(); cts6.Token.Register(() => L("late-cb")); string s15 = Flush();
            string s16 = Q(() => { var c = new CancellationTokenSource(); c.Dispose(); return c.Token.IsCancellationRequested; }), s17 = Q(() => { var c = new CancellationTokenSource(); c.Dispose(); c.Cancel(); return "no"; }), s18 = Q(() => { var c = new CancellationTokenSource(); c.Dispose(); return c.IsCancellationRequested; });
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, new CancellationTokenSource().Token); string s19 = Q(() => linked.IsCancellationRequested);
            var tcsCt = new TaskCompletionSource<int>(); tcsCt.SetCanceled(cts.Token); string s20 = Q(() => tcsCt.Task.IsCanceled), s21 = Await(tcsCt.Task); string s22 = Q(() => Task.FromCanceled(ct).Status), s23 = Q(() => Task.FromCanceled<int>(ct).IsCanceled), s24 = Q(() => Task.FromCanceled(CancellationToken.None)), s25 = Await(Task.FromCanceled<int>(ct));
            string s26 = Q(() => Task.FromException<int>(new MyEx("fe", 1)).Status), s27 = Await(Task.FromException<int>(new MyEx("fe", 1))), s28 = Q(() => Task.FromException(new OperationCanceledException()).IsCanceled), s29 = Q(() => Task.FromException(new TaskCanceledException()).Status);
            return Par.J(s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14, s15, s16, s17, s18, s19, s20, s21, s22, s23, s24, s25, s26, s27, s28, s29);
        }

        struct MyAwaitable { public int V; public MyAwaiter GetAwaiter() { L("GetAwaiter"); return new MyAwaiter(V); } }
        class MyAwaiter : System.Runtime.CompilerServices.INotifyCompletion { int v; public static Action Pending; public MyAwaiter(int v) { this.v = v; } public bool IsCompleted { get { L("IsCompleted:" + (v > 0)); return v > 0; } } public void OnCompleted(Action c) { L("OnCompleted"); Pending = c; } public int GetResult() { L("GetResult"); if (v == -1) throw new MyEx("awaiter", 1); return v * 10; } }
        static async Task<int> UseAwaitable(int v) { L("before"); var r = await new MyAwaitable { V = v }; L("after:" + r); return r; }
        static async Task<int> UseYield() { L("y0"); await Task.Yield(); L("y1"); return 1; }
        static async Task<int> UseConfigureAwait() { var a = await Job().ConfigureAwait(false); var b = await Task.FromResult(2).ConfigureAwait(true); return a + b; }
        static string CustomAwaiter()
        {
            var t1 = UseAwaitable(2); string s1 = t1.Status + "|" + t1.Result + "|" + Flush();
            var t2 = UseAwaitable(0); string s2 = t2.Status + "|" + Flush(); MyAwaiter.Pending(); string s3 = t2.Status + "|" + Await(t2) + "|" + Flush();
            var t3 = UseAwaitable(-1); string s4 = t3.Status + "|" + Await(t3) + "|" + Flush();
            var t4 = UseYield(); string s5 = t4.Status + "|" + Flush(); PumpAll(t4); string s6 = t4.Status + "|" + Flush(); // Task.Yield: tek thread'de thread pool'a gider -> parite: tamamlanmayabilir
            return Par.J(s1, s2, s3, s4, s5, Q(() => Await(UseConfigureAwait())), Q(() => Task.CompletedTask.ConfigureAwait(false).GetAwaiter().IsCompleted), Q(() => Task.FromResult(1).ConfigureAwait(false).GetAwaiter().GetResult()), Q(() => new MyAwaitable { V = 3 }.GetAwaiter().GetResult()), Flush());
        }

        static string AsyncLambdaLocal()
        {
            Func<int, Task<int>> lam = async x => { var v = await Job(); return x + v; };
            Func<Task<int>> lamThrow = async () => { await Job(); throw new MyEx("lam", 1); };
            Func<Task> lamVoidTask = async () => { await Job(); L("lamVoid"); };
            async Task<int> Local(int x) { var v = await Job(); return x * v; }
            async Task<int> LocalCapture() { int c = I(5); await Job(); c++; await Job(); return c; }
            int outer = I(10); async Task<int> LocalCaptureOuter() { await Job(); outer++; return outer; }
            Func<int, int, Task<int>> multi = async (a, b) => { await Job(); return a * b; };
            Action<int> asyncVoid = async x => { await Job(); L("asyncvoid:" + x); };
            asyncVoid(7); string v0 = Flush(); Pump(); string v1 = Flush();
            var tl = lam(1); var tt = lamThrow(); var tv = lamVoidTask(); var tloc = Local(3); var tcap = LocalCapture(); var tco = LocalCaptureOuter(); var tm = multi(3, 4);
            PumpAll(Task.WhenAll(tl, tt, tv, tloc, tcap, tco, tm));
            return Par.J(Q(() => tl.Result), Await(tt), Q(() => tv.Status), Flush(), Q(() => tloc.Result), Q(() => tcap.Result), Q(() => tco.Result), outer, Q(() => tm.Result), v0, v1, Q(() => ((Func<Task<int>>)(async () => 5))().Result), Q(() => ((Func<Task>)(async () => { })).Invoke().Status), Q(() => ((Func<Task<int>>)(async () => { await Task.CompletedTask; return 6; }))().Result));
        }

        static async Task<int> Level3() { L("L3-start"); await Job(); L("L3-throw"); throw new MyEx("deep", 3); }
        static async Task<int> Level2() { try { L("L2-start"); return await Level3(); } finally { L("L2-fin"); } }
        static async Task<int> Level1() { L("L1-start"); var t = Level2(); L("L1-after-call:" + t.Status); try { return await t; } catch (MyEx e) { L("L1-catch:" + e.Message); throw; } }
        static async Task<int> Level0() { try { return await Level1(); } catch (MyEx e) when (e.Code == 3) { L("L0-filter-catch"); return -3; } }
        static async Task<int> Fan() { var a = Level0(); var b = Deferred(); var c = Sync3(); L("fan-started"); var ra = await a; var rb = await b; var rc = await c; return ra + rb + rc; }
        static string DeepChain()
        {
            var t = Level0(); string s0 = t.Status + "|" + Flush(); PumpAll(t); string s1 = t.Result + "|" + Flush();
            var f = Fan(); string s2 = f.Status + "|" + Flush(); PumpAll(f); string s3 = f.Result + "|" + Flush();
            var t2 = Level1(); PumpAll(t2); string s4 = Await(t2) + "|" + Flush();
            return Par.J(s0, s1, s2, s3, s4);
        }

        static string TaskStatics()
        {
            return Par.J(Q(() => Task.FromResult(1).Status), Q(() => Task.FromResult((string)null).Result == null), Q(() => Task.FromResult(1).Id > 0), Q(() => Task.FromResult(1).Id != Task.FromResult(1).Id), Q(() => Task.CompletedTask.Id == Task.CompletedTask.Id), Q(() => Task.FromResult(1).Exception == null), Q(() => Task.FromResult(1).AsyncState == null), Q(() => Task.FromResult(1).CreationOptions), Q(() => Task.FromResult(1).IsCompletedSuccessfully), Q(() => new TaskCompletionSource<int>().Task.Status), Q(() => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously).Task.CreationOptions), Q(() => new TaskCompletionSource<int>("state").Task.AsyncState), Q(() => Task.FromResult(1).GetType().Name), Q(() => Task.CompletedTask.GetType().Name), Q(() => new TaskCompletionSource<int>().Task.GetType().Name), Q(() => Deferred().GetType().Name), Q(() => Task.FromResult(1).GetAwaiter().GetType().Name), Q(() => ((Task)Task.FromResult(1)).GetAwaiter().GetType().Name), Q(() => Task.FromResult(1).ConfigureAwait(false).GetType().Name), Q(() => Task.CurrentId == null), Q(() => TaskScheduler.Current != null), Q(() => TaskScheduler.Default.MaximumConcurrencyLevel > 0), Q(() => SynchronizationContext.Current == null), Q(() => Task.FromResult(1).ToString() == "System.Threading.Tasks.Task`1[System.Int32]"), Q(() => Task.FromResult(1).Equals(Task.FromResult(1))), Q(() => Task.FromResult(1).GetHashCode() == Task.FromResult(1).GetHashCode()), Q(() => Task.FromResult(1).IsCanceled), Q(() => Task.FromResult(1).IsFaulted), Q(() => Task.FromResult(1).Wait(0)), Q(() => Task.FromResult(1).Wait(-1)), Q(() => Task.FromResult(1).Wait(-2)), Q(() => { Task.FromResult(1).Wait(); return "w"; }), Q(() => Task.FromResult(1).Wait(TimeSpan.FromMilliseconds(1))), Q(() => Task.WhenAll(Task.FromResult(1), Task.FromResult(2)).Result.Length), Q(() => string.Join(",", Task.WhenAll(new List<Task<int>> { Task.FromResult(3), Task.FromResult(4) }).Result)), Q(() => Task.WhenAny(Task.FromResult(1), Task.FromResult(2)).Result.Result), Q(() => Task.WhenAll(new Task[0]).IsCompletedSuccessfully), Q(() => Task.FromResult(1).Result.GetType().Name), Q(() => ((Task)Task.FromResult(1)).Status), Q(() => Task.FromResult(1).ContinueWith2().Result));
        }

        static async ValueTask<int> VT(int v) { if (v < 0) throw new MyEx("vt", v); if (v == 0) return 0; await Job(); return v; }
        static async ValueTask VTVoid() { L("vtv0"); await Job(); L("vtv1"); }
        static async Task<int> ConsumeVT() { var a = await VT(0); var b = await VT(5); await VTVoid(); return a + b; }
        static string ValueTaskBasics()
        {
            var v0 = VT(0); string s0 = v0.IsCompleted + "/" + v0.IsCompletedSuccessfully + "/" + v0.Result;
            var v1 = VT(5); string s1 = v1.IsCompleted + "/" + v1.IsCompletedSuccessfully; var t1 = v1.AsTask(); PumpAll(t1); string s2 = t1.Result + "/" + v1.IsCompleted;
            var vn = VT(-1); string s3 = vn.IsCompleted + "/" + vn.IsFaulted + "/" + Q(() => vn.Result) + "/" + QM(() => vn.GetAwaiter().GetResult());
            var vd = default(ValueTask<int>); string s4 = vd.IsCompleted + "/" + vd.Result + "/" + vd.IsCompletedSuccessfully;
            var vdv = default(ValueTask); string s5 = vdv.IsCompleted + "/" + vdv.IsCompletedSuccessfully;
            var c = ConsumeVT(); PumpAll(c); string s6 = c.Result + "|" + Flush();
            var fr = new ValueTask<int>(42); string s7 = fr.Result + "/" + fr.IsCompleted + "/" + fr.AsTask().Result;
            var fromTask = new ValueTask<int>(Task.FromResult(7)); string s8 = fromTask.Result + "/" + fromTask.IsCompleted;
            var fromPending = new ValueTask<int>(Job()); string s9 = fromPending.IsCompleted.ToString(); var pt = fromPending.AsTask(); PumpAll(pt); string s10 = pt.Result.ToString();
            string s11 = Q(() => ValueTask.CompletedTask.IsCompleted), s12 = Q(() => ValueTask.FromResult(3).Result), s13 = Q(() => ValueTask.FromException<int>(new MyEx("x", 1)).IsFaulted), s14 = QM(() => ValueTask.FromException<int>(new MyEx("x", 1)).Result), s15 = Q(() => ValueTask.FromCanceled<int>(new CancellationToken(true)).IsCanceled), s16 = Q(() => new ValueTask<int>(5).Equals(new ValueTask<int>(5))), s17 = Q(() => new ValueTask<int>(5) == new ValueTask<int>(5)), s18 = Q(() => new ValueTask<int>(5).GetHashCode() == new ValueTask<int>(5).GetHashCode()), s19 = Q(() => new ValueTask<int>(5).ToString()), s20 = Q(() => new ValueTask<int>(Task.FromResult(5)).ToString()), s21 = Q(() => default(ValueTask<int>).ToString()), s22 = Q(() => new ValueTask<int>((Task<int>)null)), s23 = Q(() => new ValueTask<int>(5).Preserve().Result), s24 = Q(() => new ValueTask<int>(5).ConfigureAwait(false).GetAwaiter().GetResult());
            return Par.J(s0, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14, s15, s16, s17, s18, s19, s20, s21, s22, s23, s24);
        }

        static async Task<int> Waiter(string name, Task<int> t) { L(name + "-wait"); var v = await t; L(name + "-got:" + v); return v; }
        static string ContinuationOrder()
        {
            var tcs = new TaskCompletionSource<int>(); var a = Waiter("a", tcs.Task); var b = Waiter("b", tcs.Task); var c = Waiter("c", tcs.Task); string s0 = Flush(); tcs.SetResult(1); string s1 = Flush() + "|" + a.IsCompleted + b.IsCompleted + c.IsCompleted;
            var tcs2 = new TaskCompletionSource<int>(); int order = 0; var log2 = new List<string>(); tcs2.Task.GetAwaiter().OnCompleted(() => log2.Add("oc1:" + order++)); tcs2.Task.GetAwaiter().UnsafeOnCompleted(() => log2.Add("uoc:" + order++)); tcs2.Task.GetAwaiter().OnCompleted(() => log2.Add("oc2:" + order++)); tcs2.SetResult(0); string s2 = string.Join(",", log2);
            var tcs3 = new TaskCompletionSource<int>(); tcs3.SetResult(5); var log3 = new List<string>(); tcs3.Task.GetAwaiter().OnCompleted(() => log3.Add("already")); string s3 = string.Join(",", log3); // tamamlanmis task: OnCompleted hemen mi kosar (inline) yoksa pool'a mi?
            var tcs4 = new TaskCompletionSource<int>(); var nested = Waiter("outer", Waiter("inner", tcs4.Task)); string s4 = Flush(); tcs4.SetResult(2); string s5 = Flush();
            var tcs5 = new TaskCompletionSource<int>(); var chain = Waiter("w1", tcs5.Task); var chain2 = Waiter("w2", chain); var chain3 = Waiter("w3", chain2); tcs5.SetResult(3); string s6 = Flush();
            var tcs6 = new TaskCompletionSource<int>(); int reent = 0; Task<int> self = null; self = Waiter("self", tcs6.Task); tcs6.Task.GetAwaiter().OnCompleted(() => { reent++; L("cb-sees-self:" + self.IsCompleted); }); tcs6.SetResult(4); string s7 = Flush() + "|" + reent;
            var tcs7 = new TaskCompletionSource<int>(); var cont = tcs7.Task.ContinueWith2(); tcs7.SetResult(9); string s8 = Q(() => cont.Result);
            return Par.J(s0, s1, s2, s3, s4, s5, s6, s7, s8);
        }

        static IEnumerable<int> IterFin() { try { L("i-start"); yield return 1; L("i-mid"); yield return 2; L("i-end"); } finally { L("i-fin"); } }
        static IEnumerable<int> IterUsing() { using (new Disp("u")) { yield return 1; yield return 2; } }
        class Disp : IDisposable { string n; public Disp(string n) { this.n = n; L("open:" + n); } public void Dispose() { L("close:" + n); } }
        static IEnumerable<int> IterNestedFin() { try { try { yield return 1; } finally { L("inner"); } yield return 2; } finally { L("outer"); } }
        static IEnumerable<int> IterBreak() { for (int i = 0; i < 10; i++) { try { if (i == 2) yield break; yield return i; } finally { L("f" + i); } } }
        static IEnumerable<int> IterFinThrows() { try { yield return 1; } finally { throw new MyEx("fin-throw", 1); } }
        static string IteratorFinallyDispose()
        {
            string s1 = Q(() => { foreach (var x in IterFin()) L("got" + x); return "full"; }) + "|" + Flush();
            string s2 = Q(() => { foreach (var x in IterFin()) { L("got" + x); break; } return "break"; }) + "|" + Flush();
            string s3 = Q(() => { var e = IterFin().GetEnumerator(); e.Dispose(); return "unstarted-dispose"; }) + "|" + Flush();
            string s4 = Q(() => { var e = IterFin().GetEnumerator(); e.MoveNext(); e.Dispose(); e.Dispose(); return e.MoveNext() + "/" + e.Current; }) + "|" + Flush();
            string s5 = Q(() => { var e = IterFin().GetEnumerator(); while (e.MoveNext()) { } e.Dispose(); return "exhausted-then-dispose"; }) + "|" + Flush();
            string s6 = Q(() => { foreach (var x in IterUsing()) { if (x == 1) break; } return "using-break"; }) + "|" + Flush();
            string s7 = Q(() => { foreach (var x in IterNestedFin()) { if (x == 1) break; } return "nested-break-at-1"; }) + "|" + Flush();
            string s8 = Q(() => { foreach (var x in IterNestedFin()) { if (x == 2) break; } return "nested-break-at-2"; }) + "|" + Flush();
            string s9 = Q(() => { int s = 0; foreach (var x in IterBreak()) s += x; return s; }) + "|" + Flush();
            string s10 = QM(() => { foreach (var x in IterFinThrows()) { break; } return "no"; }) + "|" + Flush();
            string s11 = QM(() => { var e = IterFinThrows().GetEnumerator(); e.MoveNext(); e.Dispose(); return "no"; }) + "|" + Flush();
            string s12 = QM(() => { var e = IterFinThrows().GetEnumerator(); e.MoveNext(); e.MoveNext(); return "no"; }) + "|" + Flush();
            string s13 = QM(() => { foreach (var x in IterFinThrows()) { throw new InvalidOperationException("body"); } return "no"; }) + "|" + Flush();
            string s14 = Q(() => { var e = IterFin().GetEnumerator(); e.MoveNext(); ((System.Collections.IEnumerator)e).Reset(); return "reset"; }) + "|" + Flush();
            string s15 = Q(() => { var e = (IEnumerator<int>)IterFin(); return e.MoveNext() + "/" + e.Current; }) + "|" + Flush();
            string s16 = Q(() => { var it = IterFin(); var a = it.GetEnumerator(); var b = it.GetEnumerator(); return ReferenceEquals(a, it) + "/" + ReferenceEquals(b, it) + "/" + ReferenceEquals(a, b); }) + "|" + Flush();
            return Par.J(s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13, s14, s15, s16);
        }

        static IEnumerable<int> Lazy(string tag) { L(tag + ":start"); for (int i = 0; i < 3; i++) { L(tag + ":before" + i); yield return i; L(tag + ":after" + i); } L(tag + ":end"); }
        static IEnumerable<T> Where2<T>(IEnumerable<T> src, Func<T, bool> p) { foreach (var x in src) { L("where-test:" + x); if (p(x)) yield return x; } }
        static IEnumerable<R> Select2<T, R>(IEnumerable<T> src, Func<T, R> f) { foreach (var x in src) { L("select:" + x); yield return f(x); } }
        static string IteratorLazyOrder()
        {
            var it = Lazy("a"); string s0 = Flush();
            var e = it.GetEnumerator(); string s1 = Flush();
            e.MoveNext(); string s2 = e.Current + "|" + Flush();
            e.MoveNext(); string s3 = e.Current + "|" + Flush();
            var cur = e.Current; var cur2 = e.Current; string s4 = (cur == cur2) + "|" + Flush(); // Current yan etkisiz
            while (e.MoveNext()) { } string s5 = Flush();
            bool again = e.MoveNext(); string s6 = again + "|" + Flush();
            var pipe = Select2(Where2(Lazy("p"), x => x % 2 == 0), x => x * 10); string s7 = Flush();
            var res = new List<int>(); foreach (var x in pipe) { L("consume:" + x); res.Add(x); } string s8 = string.Join(",", res) + "|" + Flush();
            var twice = Lazy("t"); int c1 = 0, c2 = 0; foreach (var x in twice) c1++; foreach (var x in twice) c2++; string s9 = c1 + "/" + c2 + "|" + Flush();
            var e1 = Lazy("m").GetEnumerator(); var e2 = Lazy("n").GetEnumerator(); e1.MoveNext(); e2.MoveNext(); e2.MoveNext(); string s10 = e1.Current + "/" + e2.Current + "|" + Flush();
            var shared = Lazy("s"); var ea = shared.GetEnumerator(); var eb = shared.GetEnumerator(); ea.MoveNext(); eb.MoveNext(); eb.MoveNext(); string s11 = ea.Current + "/" + eb.Current + "/" + ReferenceEquals(ea, eb) + "|" + Flush();
            var inf = Infinite().GetEnumerator(); int sum = 0; for (int i = 0; i < 5; i++) { inf.MoveNext(); sum += inf.Current; } string s12 = sum.ToString();
            string s13 = Q(() => { var en = Lazy("c").GetEnumerator(); return en.Current; }) + "|" + Flush();
            return Par.J(s0, s1, s2, s3, s4, s5, s6, s7, s8, s9, s10, s11, s12, s13);
        }
        static IEnumerable<int> Infinite() { int i = 0; while (true) yield return i++; }

        static IEnumerable<int> ThrowAt(int n) { for (int i = 0; ; i++) { if (i == n) throw new MyEx("at" + i, i); yield return i; } }
        static IEnumerable<int> ThrowImmediately() { throw new MyEx("imm", 0); }
        static IEnumerable<int> ThrowImmediatelyIter() { if (I(1) == 1) throw new MyEx("imm-iter", 0); yield return 1; }
        static IEnumerable<int> CatchInside() { for (int i = 0; i < 3; i++) { int v; try { if (i == 1) throw new MyEx("in", i); v = i; } catch (MyEx e) { L("caught" + e.Code); v = -1; } yield return v; } }
        static IEnumerable<int> Recover() { var e = ThrowAt(2).GetEnumerator(); while (true) { bool mn; bool failed = false; try { mn = e.MoveNext(); } catch (MyEx) { L("rec"); mn = false; failed = true; } if (failed) { yield return 99; yield break; } if (!mn) yield break; yield return e.Current; } }
        static string IteratorExceptions()
        {
            string s1 = QM(() => { int s = 0; foreach (var x in ThrowAt(2)) s += x; return s; });
            string s2 = QM(() => { var t = ThrowImmediately(); L("created"); return "created-ok"; }) + "|" + Flush();
            string s3 = QM(() => { var t = ThrowImmediatelyIter(); L("created"); var e = t.GetEnumerator(); L("enumerator"); e.MoveNext(); return "no"; }) + "|" + Flush();
            string s4 = Q(() => string.Join(",", CatchInside())) + "|" + Flush();
            string s5 = Q(() => string.Join(",", Recover())) + "|" + Flush();
            string s6 = QM(() => { var e = ThrowAt(1).GetEnumerator(); e.MoveNext(); try { e.MoveNext(); } catch (MyEx) { } return e.MoveNext() + "/" + e.Current; });
            string s7 = QM(() => { var e = ThrowAt(1).GetEnumerator(); e.MoveNext(); try { e.MoveNext(); } catch (MyEx) { } e.Dispose(); return "disposed-after-throw"; });
            string s8 = QM(() => { foreach (var x in ThrowAt(1)) { try { L("body" + x); } finally { L("bodyfin" + x); } } return "no"; }) + "|" + Flush();
            string s9 = QM(() => { try { foreach (var x in IterFin()) { if (x == 2) throw new InvalidOperationException("consumer"); } } catch (InvalidOperationException e) { return "consumer-caught:" + e.Message; } return "no"; }) + "|" + Flush();
            return Par.J(s1, s2, s3, s4, s5, s6, s7, s8, s9);
        }

        static async Task<int> LoopAwait(int n) { int sum = 0; for (int i = 0; i < n; i++) { try { sum += await Job(); } finally { sum += 100; } } return sum; }
        static async Task<string> LoopCatch() { var sb = new System.Text.StringBuilder(); for (int i = 0; i < 4; i++) { try { if (i % 2 == 1) await ThrowAfter(); else await Job(); sb.Append("ok" + i); } catch (MyEx) { sb.Append("ex" + i); } } return sb.ToString(); }
        static async Task<int> WhileAwait() { int i = 0; while (await Job() > 0 && i < 5) i++; return i; }
        static async Task<int> ForeachAwait() { int s = 0; foreach (var x in new[] { 1, 2, 3 }) { s += x * await Job(); } return s; }
        static async Task<int> HoistedLocals() { int a = I(1); long b = I(2); string c = "x"; var d = new List<int> { 1 }; double e = 0.5; await Job(); a++; b *= 3; c += "y"; d.Add(2); e *= 2; await Job(); return a + (int)b + c.Length + d.Count + (int)(e * 10); }
        static async Task<int> AwaitInExpression() { return (await Job()) + (await Job()) * (await Job()); }
        static async Task<int> AwaitInCondition() { if (await Job() == 1 && await Job() == 2) return 12; return 0; }
        static async Task<int> AwaitInSwitch() { switch (await Job()) { case 1: return 10 + await Job(); default: return -1; } }
        static async Task<string> AwaitInInterpolation() { return $"{await Job()}-{await Job()}"; }
        static async Task<int> AwaitInUsing() { using (new Disp("au")) { return await Job(); } }
        static async Task<int> AwaitInLock() { lock (log) { } return await Job(); }
        static async Task<int> AwaitNested() { return await Task.FromResult(await Job()); }
        static async Task<int> AwaitArray() { var arr = new int[3]; for (int i = 0; i < 3; i++) arr[i] = await Job(); return arr[0] * 100 + arr[1] * 10 + arr[2]; }
        static string AsyncLoopAccumulate()
        {
            var tasks = new Task[] { LoopAwait(3), LoopCatch(), WhileAwait(), ForeachAwait(), HoistedLocals(), AwaitInExpression(), AwaitInCondition(), AwaitInSwitch(), AwaitInInterpolation(), AwaitInUsing(), AwaitInLock(), AwaitNested(), AwaitArray() };
            PumpAll(Task.WhenAll(tasks));
            return Par.J(Q(() => ((Task<int>)tasks[0]).Result), Q(() => ((Task<string>)tasks[1]).Result), Q(() => ((Task<int>)tasks[2]).Result), Q(() => ((Task<int>)tasks[3]).Result), Q(() => ((Task<int>)tasks[4]).Result), Q(() => ((Task<int>)tasks[5]).Result), Q(() => ((Task<int>)tasks[6]).Result), Q(() => ((Task<int>)tasks[7]).Result), Q(() => ((Task<string>)tasks[8]).Result), Q(() => ((Task<int>)tasks[9]).Result), Q(() => ((Task<int>)tasks[10]).Result), Q(() => ((Task<int>)tasks[11]).Result), Q(() => ((Task<int>)tasks[12]).Result), Flush());
        }
    }

    static class TaskExt96
    {
        // ContinueWith ENGINE KODUNDA YASAK (thread pool); burada yalnizca 'tamamlanmis task -> ayni thread' continuation taklidi
        public static Task<string> ContinueWith2(this Task t) { var tcs = new TaskCompletionSource<string>(); t.GetAwaiter().OnCompleted(() => tcs.TrySetResult(t.Status.ToString())); return tcs.Task; }
        public static Task<int> ContinueWith2(this Task<int> t) { var tcs = new TaskCompletionSource<int>(); t.GetAwaiter().OnCompleted(() => { if (t.IsFaulted) tcs.TrySetException(t.Exception.InnerExceptions); else tcs.TrySetResult(t.Result * 2); }); return tcs.Task; }
    }
}
