// corelib: List<T> + Dictionary<K,V> (C# kaynakli - monomorphize edilir, C'de ciplak dizi erisimi).
// PERFORMANS SOZLESMESI: steady-state SIFIR tahsis (alloc yalniz buyumede, kapasite 2x);
// Dictionary = pow2 bucket + zincir, paralel diziler (struct-dizisi GC siniri nedeniyle
// entry struct'i YOK: hashes/nexts/keys/values ayri - GC ref dizilerini normal tarar).
// Anahtar hash/eq: kisitsiz T'de object uyeleri (transpiler deger tiplerinde boxing'siz inline'lar).
// BILINCLI KISMI YUZEY: IEnumerable/foreach yok,
// EqualityComparer yok (GetHashCode/Equals dogrudan).
namespace System.Collections.Generic
{
    class KeyNotFoundException : Exception
    {
        public KeyNotFoundException() : base("The given key was not present in the dictionary.") { }
        public KeyNotFoundException(string message) : base(message) { }
    }

    // .NET hiyerarsisi: IDictionary<K,V> : ICollection<KeyValuePair<K,V>> : IEnumerable<KeyValuePair<K,V>>.
    // Count/foreach ICollection<KVP> uzerinden dispatch edilir (Roslyn boyle uretir).
    interface IDictionary<K, V> : ICollection<KeyValuePair<K, V>>
    {
        bool TryGetValue(K key, out V value);
        bool ContainsKey(K key);
        bool Remove(K key);
        DictionaryKeyCollection<K, V> Keys { get; }
        DictionaryValueCollection<K, V> Values { get; }
        V this[K key] { get; set; }
    }

    interface IComparer<T>
    {
        int Compare(T x, T y);
    }

    interface IEnumerator<T> : System.Collections.IEnumerator, System.IDisposable
    {
        new T Current { get; }
    }

    interface IEnumerable<T>
    {
        IEnumerator<T> GetEnumerator();
    }

    interface ICollection<T> : IEnumerable<T>
    {
        int Count { get; }
        void Add(T item);
    }

    interface IList<T> : ICollection<T>
    {
        T this[int index] { get; set; }
    }

    struct ListEnumerator<T>
    {
        List<T> list;
        int index;
        public bool MoveNext()
        {
            int next = index + 1;
            if (next >= list.size) { return false; }
            index = next;
            return true;
        }
        public T Current { get { return list.items[index]; } }
        public void Dispose() { } // foreach finally: List enumerator'unde serbest birakilacak kaynak yok
    }

    class ListInterfaceEnumerator<T> : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
    {
        ListEnumerator<T> enumerator;
        public ListInterfaceEnumerator(ListEnumerator<T> enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public T Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    class List<T> : IList<T>
    {
        T[] items;
        int size;
        public List() { items = new T[4]; size = 0; }
        public List(int capacity) { items = new T[capacity < 4 ? 4 : capacity]; size = 0; }
        public int Count { get { return size; } }
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)size) { throw new IndexOutOfRangeException(); }
                return items[index];
            }
            set
            {
                if ((uint)index >= (uint)size) { throw new IndexOutOfRangeException(); }
                items[index] = value;
            }
        }
        public void Add(T item)
        {
            if (size == items.Length) { Grow(); }
            items[size] = item;
            size++;
        }
        public void AddRange(List<T> collection)
        {
            int count = collection.size;
            for (int i = 0; i < count; i++) { Add(collection.items[i]); }
        }
        public ListEnumerator<T> GetEnumerator() { return new ListEnumerator<T> { list = this, index = -1 }; }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return new ListInterfaceEnumerator<T>(GetEnumerator()); }
        void Grow()
        {
            T[] buyuk = new T[items.Length * 2];
            for (int i = 0; i < size; i++) { buyuk[i] = items[i]; }
            items = buyuk;
        }
        public void Clear()
        {
            for (int i = 0; i < size; i++) { items[i] = default(T); } // ref'leri birak (GC)
            size = 0;
        }
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)size) { throw new IndexOutOfRangeException(); }
            for (int i = index; i < size - 1; i++) { items[i] = items[i + 1]; }
            size--;
            items[size] = default(T);
        }
        public bool Remove(T item)
        {
            for (int index = 0; index < size; index++)
            {
                if (!items[index].Equals(item)) continue;
                RemoveAt(index);
                return true;
            }
            return false;
        }
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)size) { throw new IndexOutOfRangeException(); }
            if (size == items.Length) { Grow(); }
            for (int move = size; move > index; move--) { items[move] = items[move - 1]; }
            items[index] = item;
            size++;
        }
        public void Reverse()
        {
            int left = 0;
            int right = size - 1;
            while (left < right)
            {
                T item = items[left];
                items[left] = items[right];
                items[right] = item;
                left++;
                right--;
            }
        }
        public bool Contains(T item)
        {
            for (int i = 0; i < size; i++)
            {
                if (items[i].Equals(item)) { return true; }
            }
            return false;
        }
        public void ForEach(Action<T> action)
        {
            for (int i = 0; i < size; i++) { action(items[i]); }
        }
        public T Find(Predicate<T> match)
        {
            for (int i = 0; i < size; i++)
            {
                if (match(items[i])) { return items[i]; }
            }
            return default(T);
        }
        public T[] ToArray()
        {
            T[] result = new T[size];
            for (int i = 0; i < size; i++) { result[i] = items[i]; }
            return result;
        }
        public void Sort(Comparison<T> comparison)
        {
            for (int i = 1; i < size; i++)
            {
                T item = items[i];
                int index = i;
                while (index > 0 && comparison(items[index - 1], item) > 0)
                {
                    items[index] = items[index - 1];
                    index--;
                }
                items[index] = item;
            }
        }
    }

    struct HashSetEnumerator<T>
    {
        HashSet<T> set;
        int index;
        public bool MoveNext()
        {
            while (++index < set.size)
                if (set.hashes[index] >= 0) return true;
            return false;
        }
        public T Current { get { return set.items[index]; } }
    }

    class HashSetInterfaceEnumerator<T> : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
    {
        HashSetEnumerator<T> enumerator;
        public HashSetInterfaceEnumerator(HashSetEnumerator<T> enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public T Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    class HashSet<T> : IEnumerable<T>
    {
        int[] buckets; // 1-tabanli slot, 0 = bos
        int[] hashes;  // -1 = silinmis slot
        int[] nexts;
        T[] items;
        int size;
        int count;

        public HashSet()
        {
            buckets = new int[8];
            hashes = new int[8];
            nexts = new int[8];
            items = new T[8];
            size = 0;
            count = 0;
        }

        public int Count { get { return count; } }
        public HashSetEnumerator<T> GetEnumerator() { return new HashSetEnumerator<T> { set = this, index = -1 }; }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return new HashSetInterfaceEnumerator<T>(GetEnumerator()); }

        int Find(T item)
        {
            int hash = item.GetHashCode() & 0x7FFFFFFF;
            int index = buckets[hash & (buckets.Length - 1)] - 1;
            while (index >= 0)
            {
                if (hashes[index] == hash && items[index].Equals(item)) return index;
                index = nexts[index] - 1;
            }
            return -1;
        }

        public bool Contains(T item) { return Find(item) >= 0; }

        public bool Add(T item)
        {
            if (Find(item) >= 0) return false;
            if (size == items.Length) { Grow(); }
            int hash = item.GetHashCode() & 0x7FFFFFFF;
            int bucket = hash & (buckets.Length - 1);
            hashes[size] = hash;
            items[size] = item;
            nexts[size] = buckets[bucket];
            buckets[bucket] = size + 1;
            size++;
            count++;
            return true;
        }

        public bool Remove(T item)
        {
            int hash = item.GetHashCode() & 0x7FFFFFFF;
            int bucket = hash & (buckets.Length - 1);
            int index = buckets[bucket] - 1;
            int previous = -1;
            while (index >= 0)
            {
                if (hashes[index] == hash && items[index].Equals(item))
                {
                    if (previous < 0) buckets[bucket] = nexts[index];
                    else nexts[previous] = nexts[index];
                    hashes[index] = -1;
                    items[index] = default(T);
                    count--;
                    return true;
                }
                previous = index;
                index = nexts[index] - 1;
            }
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < buckets.Length; i++) { buckets[i] = 0; }
            for (int i = 0; i < size; i++) { hashes[i] = -1; items[i] = default(T); }
            size = 0;
            count = 0;
        }

        void Grow()
        {
            int capacity = items.Length * 2;
            int[] newBuckets = new int[capacity];
            int[] newHashes = new int[capacity];
            int[] newNexts = new int[capacity];
            T[] newItems = new T[capacity];
            int newSize = 0;
            for (int i = 0; i < size; i++)
            {
                if (hashes[i] < 0) continue;
                int bucket = hashes[i] & (capacity - 1);
                newHashes[newSize] = hashes[i];
                newItems[newSize] = items[i];
                newNexts[newSize] = newBuckets[bucket];
                newBuckets[bucket] = newSize + 1;
                newSize++;
            }
            buckets = newBuckets;
            hashes = newHashes;
            nexts = newNexts;
            items = newItems;
            size = newSize;
        }
    }

    struct KeyValuePair<K, V>
    {
        K key;
        V value;
        public KeyValuePair(K key, V value) { this.key = key; this.value = value; }
        public K Key { get { return key; } }
        public V Value { get { return value; } }
    }

    struct DictionaryEnumerator<K, V>
    {
        Dictionary<K, V> dictionary;
        int index;
        public bool MoveNext() { index++; return index < dictionary.size; }
        public KeyValuePair<K, V> Current { get { return new KeyValuePair<K, V>(dictionary.keys[index], dictionary.values[index]); } }
    }

    struct DictionaryKeyEnumerator<K, V>
    {
        Dictionary<K, V> dictionary;
        int index;
        public bool MoveNext() { index++; return index < dictionary.size; }
        public K Current { get { return dictionary.keys[index]; } }
    }

    struct DictionaryValueEnumerator<K, V>
    {
        Dictionary<K, V> dictionary;
        int index;
        public bool MoveNext() { index++; return index < dictionary.size; }
        public V Current { get { return dictionary.values[index]; } }
    }

    class DictionaryKeyInterfaceEnumerator<K, V> : IEnumerator<K>, System.Collections.IEnumerator, System.IDisposable
    {
        DictionaryKeyEnumerator<K, V> enumerator;
        public DictionaryKeyInterfaceEnumerator(DictionaryKeyEnumerator<K, V> enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public K Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    class DictionaryValueInterfaceEnumerator<K, V> : IEnumerator<V>, System.Collections.IEnumerator, System.IDisposable
    {
        DictionaryValueEnumerator<K, V> enumerator;
        public DictionaryValueInterfaceEnumerator(DictionaryValueEnumerator<K, V> enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public V Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    class DictionaryInterfaceEnumerator<K, V> : IEnumerator<KeyValuePair<K, V>>, System.Collections.IEnumerator, System.IDisposable
    {
        DictionaryEnumerator<K, V> enumerator;
        public DictionaryInterfaceEnumerator(DictionaryEnumerator<K, V> enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public KeyValuePair<K, V> Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { throw new NotSupportedException(); } } // KVP struct kutulama (Dilim D) yok; jenerik yol kullanilir
        public void Reset() { }
        public void Dispose() { }
    }

    class DictionaryKeyCollection<K, V> : System.Collections.ICollection, ICollection<K>, IEnumerable<K>
    {
        Dictionary<K, V> dictionary;
        public int Count { get { return dictionary.Count; } }
        public void Add(K item) { throw new NotSupportedException(); } // C#: KeyCollection salt-okunur
        public DictionaryKeyEnumerator<K, V> GetEnumerator() { return new DictionaryKeyEnumerator<K, V> { dictionary = dictionary, index = -1 }; }
        IEnumerator<K> IEnumerable<K>.GetEnumerator() { return new DictionaryKeyInterfaceEnumerator<K, V>(GetEnumerator()); }
    }

    class DictionaryValueCollection<K, V> : System.Collections.ICollection, ICollection<V>, IEnumerable<V>
    {
        Dictionary<K, V> dictionary;
        public int Count { get { return dictionary.Count; } }
        public void Add(V item) { throw new NotSupportedException(); } // C#: ValueCollection salt-okunur
        public DictionaryValueEnumerator<K, V> GetEnumerator() { return new DictionaryValueEnumerator<K, V> { dictionary = dictionary, index = -1 }; }
        IEnumerator<V> IEnumerable<V>.GetEnumerator() { return new DictionaryValueInterfaceEnumerator<K, V>(GetEnumerator()); }
    }

    class Dictionary<K, V> : IDictionary<K, V>
    {
        int[] buckets; // 1-tabanli slot indeksi; 0 = bos
        int[] hashes;  // cache'li hash: buyumede rehash Equals'siz
        int[] nexts;   // zincir: 1-tabanli, 0 = son
        K[] keys;
        V[] values;
        int size;
        public Dictionary()
        {
            buckets = new int[8];
            hashes = new int[8];
            nexts = new int[8];
            keys = new K[8];
            values = new V[8];
            size = 0;
        }
        public int Count { get { return size; } }
        public DictionaryKeyCollection<K, V> Keys { get { return new DictionaryKeyCollection<K, V> { dictionary = this }; } }
        public DictionaryValueCollection<K, V> Values { get { return new DictionaryValueCollection<K, V> { dictionary = this }; } }
        public DictionaryEnumerator<K, V> GetEnumerator() { return new DictionaryEnumerator<K, V> { dictionary = this, index = -1 }; }
        // ICollection<KeyValuePair<K,V>> / IEnumerable<KeyValuePair<K,V>> (IDictionary tabani)
        IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator() { return new DictionaryInterfaceEnumerator<K, V>(GetEnumerator()); }
        void ICollection<KeyValuePair<K, V>>.Add(KeyValuePair<K, V> item) { this[item.Key] = item.Value; }
        int Find(K key)
        {
            int h = key.GetHashCode() & 0x7FFFFFFF;
            int i = buckets[h & (buckets.Length - 1)] - 1;
            while (i >= 0)
            {
                if (hashes[i] == h && keys[i].Equals(key)) { return i; }
                i = nexts[i] - 1;
            }
            return -1;
        }
        public bool ContainsKey(K key) { return Find(key) >= 0; }
        public bool Remove(K key)
        {
            int hash = key.GetHashCode() & 0x7FFFFFFF;
            int bucket = hash & (buckets.Length - 1);
            int previous = -1;
            int index = buckets[bucket] - 1;
            while (index >= 0)
            {
                if (hashes[index] == hash && keys[index].Equals(key)) { break; }
                previous = index;
                index = nexts[index] - 1;
            }
            if (index < 0) { return false; }

            if (previous < 0) { buckets[bucket] = nexts[index]; }
            else { nexts[previous] = nexts[index]; }

            int last = size - 1;
            if (index != last)
            {
                hashes[index] = hashes[last];
                keys[index] = keys[last];
                values[index] = values[last];
                nexts[index] = nexts[last];

                int movedBucket = hashes[index] & (buckets.Length - 1);
                int movedPrevious = -1;
                int movedIndex = buckets[movedBucket] - 1;
                while (movedIndex != last)
                {
                    movedPrevious = movedIndex;
                    movedIndex = nexts[movedIndex] - 1;
                }
                if (movedPrevious < 0) { buckets[movedBucket] = index + 1; }
                else { nexts[movedPrevious] = index + 1; }
            }

            hashes[last] = 0;
            nexts[last] = 0;
            keys[last] = default(K);
            values[last] = default(V);
            size--;
            return true;
        }
        public void Add(K key, V value)
        {
            if (ContainsKey(key)) { throw new Exception("An item with the same key has already been added."); }
            this[key] = value;
        }
        public bool TryGetValue(K key, out V value)
        {
            int i = Find(key);
            if (i >= 0) { value = values[i]; return true; }
            value = default(V);
            return false;
        }
        public V this[K key]
        {
            get
            {
                int i = Find(key);
                if (i < 0) { throw new KeyNotFoundException(); }
                return values[i];
            }
            set
            {
                int i = Find(key);
                if (i >= 0) { values[i] = value; return; } // upsert
                if (size == keys.Length) { Grow(); }
                int h = key.GetHashCode() & 0x7FFFFFFF;
                int b = h & (buckets.Length - 1);
                hashes[size] = h;
                keys[size] = key;
                values[size] = value;
                nexts[size] = buckets[b];
                buckets[b] = size + 1;
                size++;
            }
        }
        public void Clear()
        {
            int cap = keys.Length;
            buckets = new int[cap];
            hashes = new int[cap];
            nexts = new int[cap];
            keys = new K[cap];
            values = new V[cap];
            size = 0;
        }
        void Grow()
        {
            int cap = keys.Length * 2;
            int[] nb = new int[cap];
            int[] nh = new int[cap];
            int[] nn = new int[cap];
            K[] nk = new K[cap];
            V[] nv = new V[cap];
            for (int i = 0; i < size; i++)
            {
                nh[i] = hashes[i];
                nk[i] = keys[i];
                nv[i] = values[i];
            }
            for (int i = 0; i < size; i++) // hash cache'li rehash: Equals/GetHashCode cagrisi yok
            {
                int b = nh[i] & (cap - 1);
                nn[i] = nb[b];
                nb[b] = i + 1;
            }
            buckets = nb;
            hashes = nh;
            nexts = nn;
            keys = nk;
            values = nv;
        }
    }
}
