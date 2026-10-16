// Main-thread task core. A native worker must never call these methods: it posts a POD
// DigitoyEngineAsyncCompletion, then the host main-thread drain resolves the source here.
using System.Collections.Generic;

namespace System.Threading.Tasks
{
    public class Task
    {
        public bool completed;
        public Exception fault; // SetException ile dolar; Result/GetResult yeniden firlatir
        public List<Action> continuations = new List<Action>();
        public bool canceled; // fault = TaskCanceledException; IsFaulted false (.NET ayrimi)

        public bool IsCompleted { get { return completed; } }
        public bool IsFaulted { get { return fault != null && !canceled; } }
        public bool IsCanceled { get { return canceled; } }
        public bool IsCompletedSuccessfully { get { return completed && fault == null; } }
        internal Exception Fault { get { return fault; } }

        static Task completedTask;
        public static Task CompletedTask
        {
            get
            {
                if (completedTask == null) { completedTask = new Task(); completedTask.completed = true; }
                return completedTask;
            }
        }
        public static Task<T> FromResult<T>(T value) { var t = new Task<T>(); t.TrySetResult(value); return t; }
        public static Task FromException(Exception e) { var t = new Task(); t.TrySetException(e); return t; }
        public static Task FromCanceled(System.Threading.CancellationToken token) { var t = new Task(); t.TrySetCanceled(token); return t; }

        // Hepsi bitince tamamlanir. Biri hata verdiyse ilk hata ile fault; yalniz iptal varsa iptal.
        public static Task WhenAll(Task[] tasks) { return new WhenAllState(tasks).Source.Task; }
        public static Task WhenAll(IEnumerable<Task> tasks) { return WhenAll(ToArray(tasks)); }
        public static Task<T[]> WhenAll<T>(Task<T>[] tasks) { return new WhenAllState<T>(tasks).Source.Task; }
        public static Task<T[]> WhenAll<T>(IEnumerable<Task<T>> tasks) { return WhenAll(ToArray(tasks)); }
        // Ilk biten task'i (hata/iptal dahil) sonuc olarak verir.
        public static Task<Task> WhenAny(Task[] tasks) { return new WhenAnyState(tasks).Source.Task; }
        public static Task<Task> WhenAny(IEnumerable<Task> tasks) { return WhenAny(ToArray(tasks)); }
        public static Task<Task<T>> WhenAny<T>(Task<T>[] tasks) { return new WhenAnyState<T>(tasks).Source.Task; }
        public static Task<Task<T>> WhenAny<T>(IEnumerable<Task<T>> tasks) { return WhenAny(ToArray(tasks)); }
        static TTask[] ToArray<TTask>(IEnumerable<TTask> tasks)
        {
            if (tasks == null) throw new ArgumentNullException("tasks");
            if (tasks is TTask[] arr) return arr;
            var list = new List<TTask>();
            foreach (var t in tasks) list.Add(t);
            return list.ToArray();
        }

        public System.Runtime.CompilerServices.TaskAwaiter GetAwaiter()
        {
            return new System.Runtime.CompilerServices.TaskAwaiter(this);
        }

        internal void ThrowIfFaulted()
        {
            if (fault != null) throw fault;
        }

        public void ContinueWith(Action continuation)
        {
            if (continuation == null) return;
            if (completed) { continuation(); return; }
            continuations.Add(continuation);
        }

        internal bool TrySetException(Exception cause)
        {
            if (completed) return false;
            fault = cause;
            return Complete();
        }

        internal bool TrySetCanceled(System.Threading.CancellationToken token)
        {
            if (completed) return false;
            canceled = true;
            fault = new TaskCanceledException(token);
            return Complete();
        }

        protected bool Complete()
        {
            if (completed) return false;
            completed = true;
            // FIFO continuation order. Clear before callbacks so a re-entrant registration
            // observes the completed state and does not retain a stale list node.
            for (int i = 0; i < continuations.Count; i++) { continuations[i](); }
            continuations.Clear();
            return true;
        }
    }

    // Yalniz non-generic ContinueWith(Action) var: Action<Task<T>> overload'u her T icin List<Action<Task<T>>> ornekliyordu
    // (~50 KB/T kod), kullanan yoktu. Devam icin `await` kullanilir.
    public class Task<T> : Task
    {
        public T result;

        public T Result
        {
            get
            {
                if (!IsCompleted) throw new InvalidOperationException("Task has not completed.");
                ThrowIfFaulted();
                return result;
            }
        }

        public System.Runtime.CompilerServices.TaskAwaiter<T> GetAwaiter()
        {
            return new System.Runtime.CompilerServices.TaskAwaiter<T>(this);
        }

        internal bool TrySetResult(T value)
        {
            if (IsCompleted) return false;
            result = value;
            return Complete();
        }
    }

    public class TaskCompletionSource<T>
    {
        public Task<T> task = new Task<T>();
        public Task<T> Task { get { return task; } }
        public bool TrySetResult(T value) { return task.TrySetResult(value); }
        public bool TrySetException(Exception cause) { return task.TrySetException(cause); }
        public bool TrySetCanceled() { return task.TrySetCanceled(System.Threading.CancellationToken.None); }
        public bool TrySetCanceled(System.Threading.CancellationToken token) { return task.TrySetCanceled(token); }
    }

    // WhenAll/WhenAny yardimcilari: lambda yerine acik siniflar (generic icinde closure frontend'de yok).
    internal sealed class WhenAllState
    {
        public readonly TaskCompletionSource<int> Source;
        readonly Task[] tasks;
        int remaining;
        public WhenAllState(Task[] tasks)
        {
            Source = new TaskCompletionSource<int>();
            this.tasks = tasks;
            remaining = tasks.Length;
            if (remaining == 0) { Source.TrySetResult(0); return; }
            for (int i = 0; i < tasks.Length; i++) tasks[i].ContinueWith(OneDone);
        }
        void OneDone()
        {
            if (--remaining != 0) return;
            Exception firstFault = null; bool anyCanceled = false;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsFaulted && firstFault == null) firstFault = tasks[i].fault;
                if (tasks[i].IsCanceled) anyCanceled = true;
            }
            if (firstFault != null) Source.TrySetException(firstFault);
            else if (anyCanceled) Source.TrySetCanceled();
            else Source.TrySetResult(0);
        }
    }

    internal sealed class WhenAnyState
    {
        public readonly TaskCompletionSource<Task> Source;
        public WhenAnyState(Task[] tasks)
        {
            Source = new TaskCompletionSource<Task>();
            for (int i = 0; i < tasks.Length; i++) new WhenAnyOne(this, tasks[i]).Arm();
        }
    }
    internal sealed class WhenAnyOne
    {
        readonly WhenAnyState owner; readonly Task task;
        public WhenAnyOne(WhenAnyState owner, Task task) { this.owner = owner; this.task = task; }
        public void Arm() { task.ContinueWith(Done); }
        void Done() { owner.Source.TrySetResult(task); }
    }

    // Generic karsiliklar (Task<T>[] -> T[] / Task<T>); lambda yerine acik siniflar (ayni gerekce).
    internal sealed class WhenAllState<T>
    {
        public readonly TaskCompletionSource<T[]> Source;
        readonly Task<T>[] tasks;
        int remaining;
        public WhenAllState(Task<T>[] tasks)
        {
            Source = new TaskCompletionSource<T[]>();
            this.tasks = tasks;
            remaining = tasks.Length;
            if (remaining == 0) { Source.TrySetResult(new T[0]); return; }
            for (int i = 0; i < tasks.Length; i++) tasks[i].ContinueWith(OneDone);
        }
        void OneDone()
        {
            if (--remaining != 0) return;
            Exception firstFault = null; bool anyCanceled = false;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsFaulted && firstFault == null) firstFault = tasks[i].fault;
                if (tasks[i].IsCanceled) anyCanceled = true;
            }
            if (firstFault != null) { Source.TrySetException(firstFault); return; }
            if (anyCanceled) { Source.TrySetCanceled(); return; }
            var results = new T[tasks.Length];
            for (int i = 0; i < tasks.Length; i++) results[i] = tasks[i].result;
            Source.TrySetResult(results);
        }
    }
    internal sealed class WhenAnyState<T>
    {
        public readonly TaskCompletionSource<Task<T>> Source;
        public WhenAnyState(Task<T>[] tasks)
        {
            Source = new TaskCompletionSource<Task<T>>();
            for (int i = 0; i < tasks.Length; i++) new WhenAnyOne<T>(this, tasks[i]).Arm();
        }
    }
    internal sealed class WhenAnyOne<T>
    {
        readonly WhenAnyState<T> owner; readonly Task<T> task;
        public WhenAnyOne(WhenAnyState<T> owner, Task<T> task) { this.owner = owner; this.task = task; }
        public void Arm() { task.ContinueWith(Done); }
        void Done() { owner.Source.TrySetResult(task); }
    }
}