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

        public bool IsCompleted { get { return completed; } }
        public bool IsFaulted { get { return fault != null; } }
        internal Exception Fault { get { return fault; } }

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

    public class Task<T> : Task
    {
        public T result;
        public List<Action<Task<T>>> typedContinuations = new List<Action<Task<T>>>();

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
            if (!Complete()) return false;
            for (int i = 0; i < typedContinuations.Count; i++) { typedContinuations[i](this); }
            typedContinuations.Clear();
            return true;
        }

        public void ContinueWith(Action<Task<T>> continuation)
        {
            if (continuation == null) return;
            if (IsCompleted) { continuation(this); return; }
            typedContinuations.Add(continuation);
        }

    }

    public class TaskCompletionSource<T>
    {
        public Task<T> task = new Task<T>();
        public Task<T> Task { get { return task; } }
        public bool TrySetResult(T value) { return task.TrySetResult(value); }
        public bool TrySetException(Exception cause) { return task.TrySetException(cause); }
    }
}