using System;
using System.Threading.Tasks;
namespace Demo49
{
    class App49
    {
        static TaskCompletionSource<int> gate;
        static int sideLog;
        // await tamamlanmis task: hic askiya alinmadan senkron biter (fast path)
        static async Task<int> Immediate()
        {
            var tcs = new TaskCompletionSource<int>();
            tcs.TrySetResult(5);
            int v = await tcs.Task;
            return v * 2;
        }
        // await bekleyen task: await noktasinda askiya alinir, TrySetResult continuation'i kosar
        static async Task<int> Deferred()
        {
            sideLog = sideLog * 10 + 1;
            int x = await gate.Task;
            sideLog = sideLog * 10 + 2;
            return x + 1;
        }
        // iki await ust uste + await'ler arasi local korunumu (hoisted state)
        static async Task<int> TwoAwaits()
        {
            var a = new TaskCompletionSource<int>();
            a.TrySetResult(3);
            int first = await a.Task;
            var b = new TaskCompletionSource<int>();
            b.TrySetResult(4);
            int second = await b.Task;
            return first * 10 + second;
        }
        public static int Run()
        {
            int acc = 0;
            var t1 = Immediate();
            if (t1.IsCompleted && t1.Result == 10) { acc += 1; }
            gate = new TaskCompletionSource<int>();
            var t2 = Deferred();
            if (!t2.IsCompleted && sideLog == 1) { acc += 2; }
            gate.TrySetResult(41);
            if (t2.IsCompleted && t2.Result == 42 && sideLog == 12) { acc += 4; }
            var t3 = TwoAwaits();
            if (t3.IsCompleted && t3.Result == 34) { acc += 8; }
            return acc;
        }
    }
}
