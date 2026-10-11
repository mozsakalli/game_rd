using System;
using System.Threading;
using System.Threading.Tasks;

namespace Demo60
{
    // CancellationToken / CTS (corelib alt kumesi) + Task iptal durumu + WhenAll/WhenAny. Ana-thread modeli:
    // Cancel callback'leri senkron ve LIFO (.NET davranisi); iptal edilmis token'a Register hemen kosar; Dispose edilen kayit kosmaz.
    // await edilen iptal Task'i TaskCanceledException (OperationCanceledException turevi) firlatir; IsCanceled true, IsFaulted false.
    class App60
    {
        static long log;
        static void L(int d) { log = (log * 10 + d) % 1000000007L; } // tasma yok (C'de signed tasma UB)

        static Task<int> Job(CancellationToken ct, TaskCompletionSource<int> tcs)
        {
            ct.Register(() => { L(9); tcs.TrySetCanceled(ct); });
            return tcs.Task;
        }

        static async Task<int> Worker(CancellationToken ct, TaskCompletionSource<int> tcs)
        {
            try
            {
                int v = await Job(ct, tcs);
                return v;
            }
            catch (OperationCanceledException e)
            {
                L(e is TaskCanceledException ? 5 : 6);
                L(e.CancellationToken == ct ? 7 : 8);
                return -1;
            }
        }

        public static int Run()
        {
            log = 1;
            var cts = new CancellationTokenSource();
            var ct = cts.Token;
            L(ct.CanBeCanceled ? 1 : 0);
            L(CancellationToken.None.CanBeCanceled ? 1 : 0);
            L(ct.IsCancellationRequested ? 1 : 0);

            // kayit sirasi + Dispose edilen kayit kosmaz
            var r1 = ct.Register(() => L(2));
            var r2 = ct.Register(() => L(3));
            r2.Dispose();
            ct.Register(() => L(4));

            var tcs = new TaskCompletionSource<int>();
            var w = Worker(ct, tcs);
            L(w.IsCompleted ? 1 : 0);
            cts.Cancel();                       // LIFO: 9 (Job kaydi -> Worker catch 5,7), 4, 2
            L(w.IsCompleted ? 1 : 0);
            L(w.Result == -1 ? 1 : 0);
            L(tcs.Task.IsCanceled ? 1 : 0);
            L(tcs.Task.IsFaulted ? 1 : 0);
            L(tcs.Task.IsCompletedSuccessfully ? 1 : 0);
            cts.Cancel();                       // ikinci Cancel no-op
            ct.Register(() => L(6));            // iptal edilmise kayit: hemen kosar
            L(tcs.TrySetResult(5) ? 1 : 0);     // bitmis task'a sonuc: false

            // ThrowIfCancellationRequested
            int thrown = 0;
            try { ct.ThrowIfCancellationRequested(); } catch (OperationCanceledException) { thrown = 1; }
            try { CancellationToken.None.ThrowIfCancellationRequested(); } catch (OperationCanceledException) { thrown = 2; }
            L(thrown);

            // WhenAll: hepsi basarili; biri iptal -> IsCanceled. (Acik dizi: .NET 9'un params ReadOnlySpan overload'u InlineArray uretir.)
            var a = new TaskCompletionSource<int>(); var b = new TaskCompletionSource<int>();
            var all = Task.WhenAll(new Task[] { a.Task, b.Task });
            b.TrySetResult(20); L(all.IsCompleted ? 1 : 0);
            a.TrySetResult(10); L(all.IsCompleted ? 1 : 0);
            L(all.IsCompletedSuccessfully && a.Task.Result + b.Task.Result == 30 ? 1 : 0);
            var c = new TaskCompletionSource<int>(); var d = new TaskCompletionSource<int>();
            var all2 = Task.WhenAll(new Task[] { c.Task, d.Task });
            c.TrySetCanceled(); L(all2.IsCompleted ? 1 : 0);
            d.TrySetResult(1); L(all2.IsCanceled ? 1 : 0);
            L(Task.WhenAll(new Task[0]).IsCompletedSuccessfully ? 1 : 0);

            // WhenAny: ilk biten
            var e = new TaskCompletionSource<int>(); var f = new TaskCompletionSource<int>();
            var any = Task.WhenAny(new Task[] { e.Task, f.Task });
            f.TrySetResult(3);
            L(any.IsCompleted && any.Result == f.Task ? 1 : 0);
            e.TrySetResult(4);
            L(any.Result == f.Task ? 1 : 0);

            L(Task.FromResult(7).Result == 7 && Task.CompletedTask.IsCompletedSuccessfully ? 1 : 0);
            return (int)log;
        }
    }
}
