// corelib: CancellationToken / CancellationTokenSource (.NET alt kumesi). ANA THREAD modeli: kilit yok,
// Cancel icindeki callback'ler senkron ve .NET gibi kayit sirasinin tersine (LIFO) kosar. CancelAfter YOK
// (saat motorun isi: DigitoyEngine.Wait); CreateLinkedTokenSource var (saatsiz, Register ile). Iptal edilmis token'a Register -> callback hemen kosar.
using System.Collections.Generic;

namespace System.Threading
{
    public sealed class CancellationTokenSource : IDisposable
    {
        internal bool canceled;
        internal bool disposed;
        internal List<CancellationCallback> callbacks;
        internal int nextId = 1;

        public bool IsCancellationRequested { get { return canceled; } }
        public CancellationToken Token { get { return new CancellationToken(this); } }

        public void Cancel()
        {
            if (canceled) return;
            canceled = true;
            var list = callbacks;
            callbacks = null;
            if (list == null) return;
            // .NET: callback'ler kayit sirasinin TERSINE (LIFO) kosar. Callback icinden yeni Register gelirse
            // (iptal edilmis -> hemen kosar); listeyi koparip bosaltmak yeterli.
            for (int i = list.Count - 1; i >= 0; i--)
                list[i].Action();
        }

        public void Dispose()
        {
            disposed = true; callbacks = null;
            if (linkedRegistrations != null)
            {
                for (int i = 0; i < linkedRegistrations.Count; i++) linkedRegistrations[i].Dispose();
                linkedRegistrations = null;
            }
        }

        // Bagli kaynak: kaynaklardan biri iptal olunca bu da iptal olur (saat yok; Register ile). Dispose bagi koparir.
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token1, CancellationToken token2)
        {
            var linked = new CancellationTokenSource();
            linked.Link(token1);
            linked.Link(token2);
            return linked;
        }
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token) { return CreateLinkedTokenSource(token, CancellationToken.None); }
        public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
        {
            if (tokens == null) throw new ArgumentNullException("tokens");
            var linked = new CancellationTokenSource();
            for (int i = 0; i < tokens.Length; i++) linked.Link(tokens[i]);
            return linked;
        }
        void Link(CancellationToken token)
        {
            if (!token.CanBeCanceled) return;
            var reg = token.Register(Cancel);
            if (linkedRegistrations == null) linkedRegistrations = new List<CancellationTokenRegistration>();
            linkedRegistrations.Add(reg);
        }
        List<CancellationTokenRegistration> linkedRegistrations;

        internal int Add(Action action)
        {
            if (callbacks == null) callbacks = new List<CancellationCallback>();
            int id = nextId++;
            callbacks.Add(new CancellationCallback(id, action));
            return id;
        }

        internal void Remove(int id)
        {
            if (callbacks == null) return;
            for (int i = 0; i < callbacks.Count; i++)
                if (callbacks[i].Id == id) { callbacks.RemoveAt(i); return; }
        }
    }

    internal struct CancellationCallback
    {
        public int Id;
        public Action Action;
        public CancellationCallback(int id, Action action) { Id = id; Action = action; }
    }

    public struct CancellationToken
    {
        internal CancellationTokenSource source; // null = None (asla iptal olmaz)

        internal CancellationToken(CancellationTokenSource source) { this.source = source; }

        public static CancellationToken None { get { return new CancellationToken(null); } }
        public bool CanBeCanceled { get { return source != null; } }
        public bool IsCancellationRequested { get { return source != null && source.canceled; } }

        public void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested) throw new OperationCanceledException(this);
        }

        public CancellationTokenRegistration Register(Action callback)
        {
            if (callback == null || source == null) return new CancellationTokenRegistration(null, 0);
            if (source.canceled) { callback(); return new CancellationTokenRegistration(null, 0); }
            return new CancellationTokenRegistration(source, source.Add(callback));
        }
        public CancellationTokenRegistration Register(Action<object> callback, object state)
        {
            if (callback == null) return new CancellationTokenRegistration(null, 0);
            return Register(new StateCallback(callback, state).Run);
        }
        // Lambda yerine acik sinif (generic/closure sekli frontend'de kisitli).
        sealed class StateCallback
        {
            readonly Action<object> callback; readonly object state;
            public StateCallback(Action<object> callback, object state) { this.callback = callback; this.state = state; }
            public void Run() { callback(state); }
        }

        public bool Equals(CancellationToken other) { return source == other.source; }
        public override bool Equals(object obj) { return obj is CancellationToken t && source == t.source; }
        public override int GetHashCode() { return source == null ? 0 : source.GetHashCode(); }
        public static bool operator ==(CancellationToken a, CancellationToken b) { return a.source == b.source; }
        public static bool operator !=(CancellationToken a, CancellationToken b) { return a.source != b.source; }
    }

    public struct CancellationTokenRegistration : IDisposable
    {
        CancellationTokenSource source;
        int id;
        internal CancellationTokenRegistration(CancellationTokenSource source, int id) { this.source = source; this.id = id; }
        public CancellationToken Token { get { return source == null ? CancellationToken.None : new CancellationToken(source); } }
        public void Dispose()
        {
            if (source != null) source.Remove(id);
            source = null;
        }
        // .NET: kayit kosmadan kaldirilirsa true. Ana thread'de Dispose ile ayni; kosmus ya da yok ise false.
        public bool Unregister()
        {
            if (source == null) return false;
            bool had = false;
            if (source.callbacks != null)
                for (int i = 0; i < source.callbacks.Count; i++)
                    if (source.callbacks[i].Id == id) { had = true; break; }
            Dispose();
            return had;
        }
    }
}

namespace System
{
    public class OperationCanceledException : Exception
    {
        System.Threading.CancellationToken token;
        public OperationCanceledException() : base("The operation was canceled.") { }
        public OperationCanceledException(string message) : base(message) { }
        public OperationCanceledException(System.Threading.CancellationToken token) : base("The operation was canceled.") { this.token = token; }
        public OperationCanceledException(string message, System.Threading.CancellationToken token) : base(message) { this.token = token; }
        public System.Threading.CancellationToken CancellationToken { get { return token; } }
    }
}

namespace System.Threading.Tasks
{
    public class TaskCanceledException : OperationCanceledException
    {
        public TaskCanceledException() : base("A task was canceled.") { }
        public TaskCanceledException(string message) : base(message) { }
        public TaskCanceledException(CancellationToken token) : base("A task was canceled.", token) { }
    }
}
