// Roslyn async/await lowering hedefi (Debug: state machine = class). Thread YOK:
// continuation'lar TrySetResult cagiran thread'de (main-thread drain modeli) inline kosar.
// CIL frontend'in remap'leri: Start<TSM>(ref) -> dogrudan sm.MoveNext();
// AwaitUnsafeOnCompleted<TA,TSM>(ref,ref) -> awaiter.Schedule(sm). (CilFrontend.cs)
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    public interface IAsyncStateMachine
    {
        public void MoveNext();
        public void SetStateMachine(IAsyncStateMachine stateMachine);
    }

    // Roslyn await deseni: awaiter INotifyCompletion (OnCompleted) / ICriticalNotifyCompletion (UnsafeOnCompleted) ister.
    // Frontend AwaitUnsafeOnCompleted'i Schedule(sm)'e remap eder; bu uyeler dogrudan cagri icin de calisir.
    public interface INotifyCompletion
    {
        void OnCompleted(Action continuation);
    }
    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        void UnsafeOnCompleted(Action continuation);
    }

    public struct TaskAwaiter : ICriticalNotifyCompletion
    {
        public Task task;
        public TaskAwaiter(Task target) { task = target; }
        public bool IsCompleted { get { return task.IsCompleted; } }
        public void GetResult() { task.ThrowIfFaulted(); }
        public void OnCompleted(Action continuation) { task.ContinueWith(continuation); }
        public void UnsafeOnCompleted(Action continuation) { task.ContinueWith(continuation); }
        public void Schedule(IAsyncStateMachine sm)
        {
            task.ContinueWith(new StateMachineStep(sm).Run);
        }
    }

    // Continuation hedefi: sm.MoveNext() (lambda yerine acik sinif — generic tipin icinde Roslyn closure'u
    // generic-nested tip uretir, frontend o sekli desteklemez; bu sinif tek ve generic'siz).
    public sealed class StateMachineStep
    {
        IAsyncStateMachine sm;
        public StateMachineStep(IAsyncStateMachine sm) { this.sm = sm; }
        public void Run() { sm.MoveNext(); }
    }

    public struct TaskAwaiter<T> : ICriticalNotifyCompletion
    {
        public Task<T> task;
        public TaskAwaiter(Task<T> target) { task = target; }
        public bool IsCompleted { get { return task.IsCompleted; } }
        public T GetResult() { return task.Result; }
        public void OnCompleted(Action continuation) { task.ContinueWith(continuation); }
        public void UnsafeOnCompleted(Action continuation) { task.ContinueWith(continuation); }
        public void Schedule(IAsyncStateMachine sm)
        {
            task.ContinueWith(new StateMachineStep(sm).Run);
        }
    }

    public struct AsyncTaskMethodBuilder
    {
        public TaskCompletionSource<int> source; // deger tasimayan Task: int'lik kaynak yeterli
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
        // Roslyn well-known uyeler. Frontend Start -> sm.MoveNext(), Await*OnCompleted -> awaiter.Schedule(sm) remap'i yapar;
        // govdeler dogrudan cagri icin esdeger davranir (state machine class'a terfi: ref kopya degil, ayni nesne).
        public void Start<TSM>(ref TSM stateMachine) where TSM : IAsyncStateMachine { stateMachine.MoveNext(); }
        public void AwaitOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA : INotifyCompletion where TSM : IAsyncStateMachine
        { awaiter.OnCompleted(new StateMachineStep(stateMachine).Run); }
        public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine
        { awaiter.UnsafeOnCompleted(new StateMachineStep(stateMachine).Run); }    }

    public struct AsyncTaskMethodBuilder<T>
    {
        public TaskCompletionSource<T> source;
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
        // Roslyn well-known uyeler. Frontend Start -> sm.MoveNext(), Await*OnCompleted -> awaiter.Schedule(sm) remap'i yapar;
        // govdeler dogrudan cagri icin esdeger davranir (state machine class'a terfi: ref kopya degil, ayni nesne).
        public void Start<TSM>(ref TSM stateMachine) where TSM : IAsyncStateMachine { stateMachine.MoveNext(); }
        public void AwaitOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA : INotifyCompletion where TSM : IAsyncStateMachine
        { awaiter.OnCompleted(new StateMachineStep(stateMachine).Run); }
        public void AwaitUnsafeOnCompleted<TA, TSM>(ref TA awaiter, ref TSM stateMachine) where TA : ICriticalNotifyCompletion where TSM : IAsyncStateMachine
        { awaiter.UnsafeOnCompleted(new StateMachineStep(stateMachine).Run); }    }
}
