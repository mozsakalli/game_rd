namespace System.Collections.Generic
{
    // Dairesel dizi kuyrugu (.NET Queue<T> yuzeyinin kullanilan alt kumesi). Ana-thread, kilit yok.
    public class Queue<T> : IEnumerable<T>, System.Collections.IEnumerable
    {
        public struct Enumerator
        {
            public Queue<T> q;
            public int index; // 0..size
            public int version;
            public bool MoveNext()
            {
                if (version != q.version) { throw new InvalidOperationException("Collection was modified; enumeration operation may not execute."); }
                if (index + 1 >= q.size) return false;
                index++;
                return true;
            }
            public T Current { get { return q.items[(q.head + index) % q.items.Length]; } }
            public void Dispose() { }
        }

        T[] items;
        int head, size, version;

        public Queue() { items = new T[4]; }
        public Queue(int capacity) { items = new T[capacity < 4 ? 4 : capacity]; }
        public Queue(IEnumerable<T> collection) : this() { if (collection == null) throw new ArgumentNullException("collection"); foreach (var x in collection) Enqueue(x); }

        public int Count { get { return size; } }

        public void Enqueue(T item)
        {
            if (size == items.Length)
            {
                var n = new T[items.Length * 2];
                for (int i = 0; i < size; i++) n[i] = items[(head + i) % items.Length];
                items = n; head = 0;
            }
            items[(head + size) % items.Length] = item;
            size++; version++;
        }

        public T Dequeue()
        {
            if (size == 0) throw new InvalidOperationException("Queue empty.");
            T v = items[head];
            items[head] = default(T);
            head = (head + 1) % items.Length;
            size--; version++;
            return v;
        }

        public bool TryDequeue(out T result)
        {
            if (size == 0) { result = default(T); return false; }
            result = Dequeue();
            return true;
        }

        public T Peek()
        {
            if (size == 0) throw new InvalidOperationException("Queue empty.");
            return items[head];
        }

        public bool TryPeek(out T result)
        {
            if (size == 0) { result = default(T); return false; }
            result = items[head];
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < size; i++) items[(head + i) % items.Length] = default(T);
            head = 0; size = 0; version++;
        }

        public bool Contains(T item)
        {
            var c = EqualityComparer<T>.Default;
            for (int i = 0; i < size; i++) if (c.Equals(items[(head + i) % items.Length], item)) return true;
            return false;
        }

        public T[] ToArray()
        {
            var a = new T[size];
            for (int i = 0; i < size; i++) a[i] = items[(head + i) % items.Length];
            return a;
        }

        public Enumerator GetEnumerator() { return new Enumerator { q = this, index = -1, version = version }; }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return new QueueInterfaceEnumerator<T>(GetEnumerator()); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new QueueInterfaceEnumerator<T>(GetEnumerator()); }
    }

    public class QueueInterfaceEnumerator<T> : IEnumerator<T>
    {
        Queue<T>.Enumerator e;
        public QueueInterfaceEnumerator(Queue<T>.Enumerator e) { this.e = e; }
        public bool MoveNext() { return e.MoveNext(); }
        public T Current { get { return e.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }
}
