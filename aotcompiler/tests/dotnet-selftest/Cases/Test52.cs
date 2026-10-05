using System.Threading.Tasks;
namespace Demo52
{
    // Async zinciri (AsyncJobs/SceneLoader deseni): elle tamamlanan TaskCompletionSource "job"lari,
    // ic ice await, ayni Task'i bekleyen birden fazla continuation, continuation icinden yeni job.
    // .NET'te ayni sira/sonuc: continuation'lar TrySetResult'ta senkron kosar (TaskCreationOptions yok,
    // tamamlanma ana thread'de -> .NET de inline kosar).
    class App52
    {
        static System.Collections.Generic.List<TaskCompletionSource<int>> pending = new System.Collections.Generic.List<TaskCompletionSource<int>>();
        static int log;

        static Task<int> Job(int v)
        {
            var tcs = new TaskCompletionSource<int>();
            pending.Add(tcs);
            log = log * 31 + v;
            return tcs.Task;
        }

        static void Pump()
        {
            var batch = pending; pending = new System.Collections.Generic.List<TaskCompletionSource<int>>();
            for (int i = 0; i < batch.Count; i++) batch[i].TrySetResult(i + 1);
        }

        static async Task<int> Leaf(int k)
        {
            int a = await Job(k);
            int b = await Job(k + 1);
            return a * 10 + b;
        }

        static async Task<int> Node(int k)
        {
            int s = 0;
            for (int i = 0; i < 3; i++) s += await Leaf(k + i);
            var shared = Leaf(100);
            int x = await shared;
            int y = await shared; // tamamlanmis task'i tekrar await
            return s + x + y;
        }

        static async Task<bool> Root()
        {
            var t1 = Node(1); var t2 = Node(5);   // iki paralel zincir, ayni pump'ta ilerler
            int r = await t1 + await t2;
            log = log * 31 + r;
            return r > 0;
        }

        public static int Run()
        {
            log = 7;
            var root = Root();
            int frames = 0;
            while (!root.IsCompleted && frames < 1000) { Pump(); frames++; }
            return root.IsCompleted && root.Result ? log + frames : -1;
        }
    }
}