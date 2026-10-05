// Roslyn async/await lowering hedefi (Debug: state machine = class). Thread YOK:
// continuation'lar TrySetResult cagiran thread'de (main-thread drain modeli) inline kosar.
// CIL frontend'in remap'leri: Start<TSM>(ref) -> dogrudan sm.MoveNext();
// AwaitUnsafeOnCompleted<TA,TSM>(ref,ref) -> awaiter.Schedule(sm). (CilFrontend.cs)
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    interface IAsyncStateMachine
    {
        void MoveNext();
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }

    struct TaskAwaiter
    {
        Task task;
        public TaskAwaiter(Task target) { task = target; }
        public bool IsCompleted { get { return task.IsCompleted; } }
        public void GetResult() { task.ThrowIfFaulted(); }
        public void Schedule(IAsyncStateMachine sm)
        {
            task.ContinueWith(() => sm.MoveNext());
        }
    }

    struct TaskAwaiter<T>
    {
        Task<T> task;
        public TaskAwaiter(Task<T> target) { task = target; }
        public bool IsCompleted { get { return task.IsCompleted; } }
        public T GetResult() { return task.Result; }
        public void Schedule(IAsyncStateMachine sm)
        {
            task.ContinueWith(() => sm.MoveNext());
        }
    }

    struct AsyncTaskMethodBuilder
    {
        TaskCompletionSource<int> source; // deger tasimayan Task: int'lik kaynak yeterli
        public static AsyncTaskMethodBuilder Create()
        {
            var b = new AsyncTaskMethodBuilder();
            b.source = new TaskCompletionSource<int>();
            return b;
        }
        public Task Task { get { return source.Task; } }
        public void SetResult() { source.TrySetResult(0); }
        public void SetException(Exception cause) { source.TrySetException(cause); }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }

    struct AsyncTaskMethodBuilder<T>
    {
        TaskCompletionSource<T> source;
        public static AsyncTaskMethodBuilder<T> Create()
        {
            var b = new AsyncTaskMethodBuilder<T>();
            b.source = new TaskCompletionSource<T>();
            return b;
        }
        public Task<T> Task { get { return source.Task; } }
        public void SetResult(T result) { source.TrySetResult(result); }
        public void SetException(Exception cause) { source.TrySetException(cause); }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }
}
