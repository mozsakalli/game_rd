// corelib: List<T> + Dictionary<K,V> (C# kaynakli - monomorphize edilir, C'de ciplak dizi erisimi).
// PERFORMANS SOZLESMESI: steady-state SIFIR tahsis (alloc yalniz buyumede, kapasite 2x);
// Dictionary = pow2 bucket + zincir, paralel diziler (struct-dizisi GC siniri nedeniyle
// entry struct'i YOK: hashes/nexts/keys/values ayri - GC ref dizilerini normal tarar).
// Anahtar hash/eq: kisitsiz T'de object uyeleri (transpiler deger tiplerinde boxing'siz inline'lar);
// comparer verilmisse (IEqualityComparer<T>, orn. StringComparer.OrdinalIgnoreCase) onun uzerinden.
// BILINCLI KISMI YUZEY: IEnumerable/foreach yok.
namespace System.Collections.Generic
{
    public interface IEqualityComparer<T>
    {
        public bool Equals(T x, T y);
        public int GetHashCode(T obj);
    }

    // .NET: EqualityComparer<T>.Default. Roslyn anonim tip / record Equals-GetHashCode'u ve Dictionary varsayilan comparer
    // deseni bunu kullanir. Varsayilan: object.Equals/GetHashCode (kisitsiz T -> deger tiplerinde transpiler inline'lar).
    public abstract class EqualityComparer<T> : IEqualityComparer<T>
    {
        static EqualityComparer<T> defaultComparer; // ornekleme basina ayri static
        public static EqualityComparer<T> Default
        {
            get
            {
                if (defaultComparer == null) defaultComparer = new DefaultEqualityComparer<T>();
                return defaultComparer;
            }
        }
        public abstract bool Equals(T x, T y);
        public abstract int GetHashCode(T obj);
    }

    public sealed class DefaultEqualityComparer<T> : EqualityComparer<T>
    {
        public override bool Equals(T x, T y)
        {
            if (x == null) return y == null;
            if (y == null) return false;
            return x.Equals(y);
        }
        public override int GetHashCode(T obj) { return obj == null ? 0 : obj.GetHashCode(); }
    }

    public class KeyNotFoundException : Exception
    {
        public KeyNotFoundException() : base("The given key was not present in the dictionary.") { }
        public KeyNotFoundException(string message) : base(message) { }
    }

    // .NET hiyerarsisi: IDictionary<K,V> : ICollection<KeyValuePair<K,V>> : IEnumerable<KeyValuePair<K,V>>.
    // Count/foreach ICollection<KVP> uzerinden dispatch edilir (Roslyn boyle uretir).
    public interface IDictionary<K, V> : ICollection<KeyValuePair<K, V>>
    {
        public bool TryGetValue(K key, out V value);
        public bool ContainsKey(K key);
        public bool Remove(K key);
        public Dictionary<K, V>.KeyCollection Keys { get; }
        public Dictionary<K, V>.ValueCollection Values { get; }
        public V this[K key] { get; set; }
    }

    public interface IComparer<T>
    {
        public int Compare(T x, T y);
    }

    public interface IEnumerator<T> : System.Collections.IEnumerator, System.IDisposable
    {
        new T Current { get; }
    }

    public interface IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator();
    }

    public interface ICollection<T> : IEnumerable<T>
    {
        public int Count { get; }
        public void Add(T item);
    }

    public interface IList<T> : ICollection<T>
    {
        public T this[int index] { get; set; }
    }

    // Salt-okunur gorunumler (.NET: IReadOnlyCollection<T> : IEnumerable<T>). List/Dictionary bunlari
    // ayni public uyelerle saglar; CollectionExtensions.GetValueOrDefault bu arayuz uzerinden gelir.
    public interface IReadOnlyCollection<T> : IEnumerable<T>
    {
        public int Count { get; }
    }

    public interface IReadOnlyList<T> : IReadOnlyCollection<T>
    {
        public T this[int index] { get; }
    }

    public interface IReadOnlyDictionary<K, V> : IReadOnlyCollection<KeyValuePair<K, V>>
    {
        public bool TryGetValue(K key, out V value);
        public bool ContainsKey(K key);
        public Dictionary<K, V>.KeyCollection Keys { get; }
        public Dictionary<K, V>.ValueCollection Values { get; }
        public V this[K key] { get; }
    }

    public static class CollectionExtensions
    {
        public static V GetValueOrDefault<K, V>(this IReadOnlyDictionary<K, V> dictionary, K key)
        {
            V value;
            return dictionary.TryGetValue(key, out value) ? value : default(V);
        }
        public static V GetValueOrDefault<K, V>(this IReadOnlyDictionary<K, V> dictionary, K key, V defaultValue)
        {
            V value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }
    }

    public class ListInterfaceEnumerator<T> : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
    {
        public List<T>.Enumerator enumerator;
        public ListInterfaceEnumerator(List<T>.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public T Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    public class List<T> : IList<T>, IReadOnlyList<T>, System.Collections.IEnumerable, System.Collections.IList
    {
        public struct Enumerator
        {
            public List<T> list;
            public int index;
            public int version;
            public bool MoveNext()
            {
                if (version != list.version) { throw new InvalidOperationException("Collection was modified; enumeration operation may not execute."); }
                int next = index + 1;
                if (next >= list.size) { return false; }
                index = next;
                return true;
            }
            public T Current { get { return list.items[index]; } }
            public void Dispose() { } // foreach finally: List enumerator'unde serbest birakilacak kaynak yok
        }
        public T[] items;
        public int size;
        public int version; // .NET: enumeration sirasinda Add/Insert/RemoveAt/Clear -> InvalidOperationException
        public List() { items = new T[4]; size = 0; }
        public List(int capacity) { items = new T[capacity < 4 ? 4 : capacity]; size = 0; }
        public List(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException("collection");
            if (collection is T[] arr) { items = new T[arr.Length < 4 ? 4 : arr.Length]; for (int i = 0; i < arr.Length; i++) items[i] = arr[i]; size = arr.Length; return; }
            items = new T[4]; size = 0;
            foreach (var x in collection) Add(x);
        }
        public int Count { get { return size; } }
        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)size) { throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection."); }
                return items[index];
            }
            set
            {
                if ((uint)index >= (uint)size) { throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection."); }
                items[index] = value;
            }
        }
        public void Add(T item)
        {
            version++;
            if (size == items.Length) { Grow(); }
            items[size] = item;
            size++;
        }
        public void AddRange(List<T> collection)
        {
            if (collection == null) { throw new ArgumentNullException("collection"); }
            int count = collection.size;
            for (int i = 0; i < count; i++) { Add(collection.items[i]); }
        }
        public List<T>.Enumerator GetEnumerator() { return new List<T>.Enumerator { list = this, index = -1, version = version }; }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return new ListInterfaceEnumerator<T>(GetEnumerator()); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new ListInterfaceEnumerator<T>(GetEnumerator()); } // koleksiyon baslaticisi (new List<T>{...}) bunu ister
        // ---- System.Collections.IList (non-generic; reflection tabanli serializer boxing ile erisir) ----
        object System.Collections.IList.this[int index] { get { return this[index]; } set { if (value == null && default(T) != null) throw new ArgumentNullException("value"); if (!(value is T) && value != null) throw new ArgumentException("The value is not of the collection element type and cannot be used in this generic collection.", "value"); this[index] = (T)value; } }
        bool System.Collections.IList.IsFixedSize { get { return false; } }
        bool System.Collections.IList.IsReadOnly { get { return false; } }
        int System.Collections.IList.Add(object value) { if (value == null && default(T) != null) throw new ArgumentNullException("value"); if (!(value is T) && value != null) throw new ArgumentException("The value is not of the collection element type and cannot be used in this generic collection.", "value"); Add((T)value); return size - 1; }
        void System.Collections.IList.Clear() { Clear(); }
        bool System.Collections.IList.Contains(object value) { return value is T && Contains((T)value); }
        int System.Collections.IList.IndexOf(object value) { return value is T ? IndexOf((T)value) : -1; }
        void System.Collections.IList.Insert(int index, object value) { if (value == null && default(T) != null) throw new ArgumentNullException("value"); if (!(value is T) && value != null) throw new ArgumentException("The value is not of the collection element type and cannot be used in this generic collection.", "value"); Insert(index, (T)value); }
        void System.Collections.IList.Remove(object value) { if (value is T) Remove((T)value); }
        void System.Collections.IList.RemoveAt(int index) { RemoveAt(index); }
        int System.Collections.ICollection.Count { get { return size; } }
        bool System.Collections.ICollection.IsSynchronized { get { return false; } }
        object System.Collections.ICollection.SyncRoot { get { return this; } }
        void System.Collections.ICollection.CopyTo(Array array, int index) { for (int i = 0; i < size; i++) array.SetValue(items[i], index + i); }
        public void Grow()
        {
            T[] buyuk = new T[items.Length * 2];
            for (int i = 0; i < size; i++) { buyuk[i] = items[i]; }
            items = buyuk;
        }
        public void Clear()
        {
            version++;
            for (int i = 0; i < size; i++) { items[i] = default(T); } // ref'leri birak (GC)
            size = 0;
        }
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)size) { throw new ArgumentOutOfRangeException("index", "Index was out of range. Must be non-negative and less than the size of the collection."); }
            version++;
            for (int i = index; i < size - 1; i++) { items[i] = items[i + 1]; }
            size--;
            items[size] = default(T);
        }
        public bool Remove(T item)
        {
            for (int index = 0; index < size; index++)
            {
                if (items[index] == null ? item != null : !items[index].Equals(item)) continue;
                RemoveAt(index);
                return true;
            }
            return false;
        }
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)size) { throw new ArgumentOutOfRangeException("index", "Index must be within the bounds of the List."); }
            version++;
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
        public void RemoveRange(int index, int count)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index", "Non-negative number required.");
            if (count < 0) throw new ArgumentOutOfRangeException("count", "Non-negative number required.");
            if (size - index < count) throw new ArgumentException("Offset and length were out of bounds for the array or count is greater than the number of elements from index to the end of the source collection.");
            if (count == 0) return;
            version++;
            int tail = size - index - count;
            for (int i = 0; i < tail; i++) { items[index + i] = items[index + count + i]; }
            for (int i = size - count; i < size; i++) { items[i] = default(T); }
            size -= count;
        }
        public bool Contains(T item) { return IndexOf(item) >= 0; }
        public int IndexOf(T item)
        {
            for (int i = 0; i < size; i++)
            {
                if (items[i] == null ? item == null : items[i].Equals(item)) { return i; }
            }
            return -1;
        }
        public int LastIndexOf(T item)
        {
            for (int i = size - 1; i >= 0; i--)
            {
                if (items[i] == null ? item == null : items[i].Equals(item)) { return i; }
            }
            return -1;
        }
        public void ForEach(Action<T> action)
        {
            for (int i = 0; i < size; i++) { action(items[i]); }
        }
        public T Find(Predicate<T> match)
        {
            if (match == null) { throw new ArgumentNullException("match"); }
            for (int i = 0; i < size; i++)
            {
                if (match(items[i])) { return items[i]; }
            }
            return default(T);
        }
        public int FindIndex(Predicate<T> match)
        {
            if (match == null) { throw new ArgumentNullException("match"); }
            for (int i = 0; i < size; i++) { if (match(items[i])) { return i; } }
            return -1;
        }
        public bool Exists(Predicate<T> match) { return FindIndex(match) >= 0; }
        public T[] ToArray()
        {
            T[] result = new T[size];
            for (int i = 0; i < size; i++) { result[i] = items[i]; }
            return result;
        }
        public void Sort(IComparer<T> comparer)
        {
            for (int i = 1; i < size; i++)
            {
                T item = items[i];
                int index = i;
                while (index > 0 && comparer.Compare(items[index - 1], item) > 0)
                {
                    items[index] = items[index - 1];
                    index--;
                }
                items[index] = item;
            }
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

    public class HashSetInterfaceEnumerator<T> : IEnumerator<T>, System.Collections.IEnumerator, System.IDisposable
    {
        public HashSet<T>.Enumerator enumerator;
        public HashSetInterfaceEnumerator(HashSet<T>.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public T Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    public class HashSet<T> : IEnumerable<T>, System.Collections.IEnumerable
    {
        public struct Enumerator
        {
            public HashSet<T> set;
            public int index;
            public int version;
            public bool MoveNext()
            {
                if (version != set.version) { throw new InvalidOperationException("Collection was modified; enumeration operation may not execute."); }
                while (++index < set.size)
                    if (set.hashes[index] >= 0) return true;
                return false;
            }
            public T Current { get { return set.items[index]; } }
            public void Dispose() { }
        }
        public int version;
        public int[] buckets; // 1-tabanli slot, 0 = bos
        public int[] hashes;  // -1 = silinmis slot
        public int[] nexts;
        public T[] items;
        public int size;
        public int count;
        public IEqualityComparer<T> comparer; // null = T.GetHashCode/Equals dogrudan (sifir ek maliyet)

        public HashSet()
        {
            buckets = new int[8];
            hashes = new int[8];
            nexts = new int[8];
            items = new T[8];
            size = 0;
            count = 0;
        }
        public HashSet(IEqualityComparer<T> comparer) : this() { this.comparer = comparer; }
        public HashSet(int capacity, IEqualityComparer<T> comparer) : this() { this.comparer = comparer; }

        int HashOf(T item) { if (item == null) { return 0; } return (comparer == null ? item.GetHashCode() : comparer.GetHashCode(item)) & 0x7FFFFFFF; } // .NET HashSet null elemana izin verir
        bool Eq(T stored, T item) { return comparer == null ? (stored == null ? item == null : stored.Equals(item)) : comparer.Equals(stored, item); }

        public int Count { get { return count; } }
        public HashSet<T>.Enumerator GetEnumerator() { return new HashSet<T>.Enumerator { set = this, index = -1, version = version }; }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return new HashSetInterfaceEnumerator<T>(GetEnumerator()); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new HashSetInterfaceEnumerator<T>(GetEnumerator()); }

        public int Find(T item)
        {
            int hash = HashOf(item);
            int index = buckets[hash & (buckets.Length - 1)] - 1;
            while (index >= 0)
            {
                if (hashes[index] == hash && Eq(items[index], item)) return index;
                index = nexts[index] - 1;
            }
            return -1;
        }

        public bool Contains(T item) { return Find(item) >= 0; }

        public bool Add(T item)
        {
            if (Find(item) >= 0) return false;
            version++; // .NET: basarili Add enumerator'u gecersiz kilar (Remove/Clear kilmaz, .NET Core 3.0+)
            if (size == items.Length) { Grow(); }
            int hash = HashOf(item);
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
            int hash = HashOf(item);
            int bucket = hash & (buckets.Length - 1);
            int index = buckets[bucket] - 1;
            int previous = -1;
            while (index >= 0)
            {
                if (hashes[index] == hash && Eq(items[index], item))
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

        public void Grow()
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

    public struct KeyValuePair<K, V>
    {
        public K key;
        public V value;
        public KeyValuePair(K key, V value) { this.key = key; this.value = value; }
        public K Key { get { return key; } }
        public V Value { get { return value; } }
        public void Deconstruct(out K key, out V value) { key = this.key; value = this.value; } // foreach (var (k, v) in dict)
    }

    public class DictionaryKeyInterfaceEnumerator<K, V> : IEnumerator<K>, System.Collections.IEnumerator, System.IDisposable
    {
        public Dictionary<K, V>.KeyCollection.Enumerator enumerator;
        public DictionaryKeyInterfaceEnumerator(Dictionary<K, V>.KeyCollection.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public K Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    public class DictionaryValueInterfaceEnumerator<K, V> : IEnumerator<V>, System.Collections.IEnumerator, System.IDisposable
    {
        public Dictionary<K, V>.ValueCollection.Enumerator enumerator;
        public DictionaryValueInterfaceEnumerator(Dictionary<K, V>.ValueCollection.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public V Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { return Current; } }
        public void Reset() { }
        public void Dispose() { }
    }

    public class DictionaryInterfaceEnumerator<K, V> : IEnumerator<KeyValuePair<K, V>>, System.Collections.IEnumerator, System.IDisposable
    {
        public Dictionary<K, V>.Enumerator enumerator;
        public DictionaryInterfaceEnumerator(Dictionary<K, V>.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public KeyValuePair<K, V> Current { get { return enumerator.Current; } }
        object System.Collections.IEnumerator.Current { get { throw new NotSupportedException(); } } // KVP struct kutulama (Dilim D) yok; jenerik yol kullanilir
        public void Reset() { }
        public void Dispose() { }
    }

    public class DictionaryEntryEnumerator<K, V> : System.Collections.IDictionaryEnumerator, System.Collections.IEnumerator
    {
        public Dictionary<K, V>.Enumerator enumerator;
        public DictionaryEntryEnumerator(Dictionary<K, V>.Enumerator enumerator) { this.enumerator = enumerator; }
        public bool MoveNext() { return enumerator.MoveNext(); }
        public object Current { get { var kv = enumerator.Current; return new System.Collections.DictionaryEntry(kv.Key, kv.Value); } }
        public System.Collections.DictionaryEntry Entry { get { var kv = enumerator.Current; return new System.Collections.DictionaryEntry(kv.Key, kv.Value); } }
        public object Key { get { return enumerator.Current.Key; } }
        public object Value { get { return enumerator.Current.Value; } }
        public void Reset() { }
    }

    public class Dictionary<K, V> : IDictionary<K, V>, IReadOnlyDictionary<K, V>, System.Collections.IEnumerable, System.Collections.IDictionary
    {
        // ---- System.Collections.IDictionary (non-generic; .NET: Dictionary<K,V> bunu uygular — Exception.Data, eski API'ler) ----
        object System.Collections.IDictionary.this[object key]
        {
            get { if (key == null) { throw new ArgumentNullException("key"); } if (key is K) { V v; if (TryGetValue((K)key, out v)) return v; } return null; }
            set
            {
                if (key == null) { throw new ArgumentNullException("key"); }
                if (!(key is K)) { throw new ArgumentException("The value is not of the collection key type.", "key"); }
                if (value == null && default(V) != null) { throw new ArgumentNullException("value"); } if (!(value is V) && value != null) { throw new ArgumentException("The value is not of the collection value type.", "value"); }
                this[(K)key] = (V)value;
            }
        }
        System.Collections.ICollection System.Collections.IDictionary.Keys { get { return Keys; } }
        System.Collections.ICollection System.Collections.IDictionary.Values { get { return Values; } }
        bool System.Collections.IDictionary.IsReadOnly { get { return false; } }
        bool System.Collections.IDictionary.IsFixedSize { get { return false; } }
        bool System.Collections.IDictionary.Contains(object key) { if (key == null) { throw new ArgumentNullException("key"); } return key is K && ContainsKey((K)key); }
        void System.Collections.IDictionary.Add(object key, object value)
        {
            if (key == null) { throw new ArgumentNullException("key"); }
            if (!(key is K)) { throw new ArgumentException("The value is not of the collection key type.", "key"); }
            if (value == null && default(V) != null) { throw new ArgumentNullException("value"); } if (!(value is V) && value != null) { throw new ArgumentException("The value is not of the collection value type.", "value"); }
            Add((K)key, (V)value);
        }
        void System.Collections.IDictionary.Clear() { Clear(); }
        void System.Collections.IDictionary.Remove(object key) { if (key == null) { throw new ArgumentNullException("key"); } if (key is K) Remove((K)key); }
        System.Collections.IDictionaryEnumerator System.Collections.IDictionary.GetEnumerator() { return new DictionaryEntryEnumerator<K, V>(GetEnumerator()); }
        int System.Collections.ICollection.Count { get { return size; } }
        bool System.Collections.ICollection.IsSynchronized { get { return false; } }
        object System.Collections.ICollection.SyncRoot { get { return this; } }
        void System.Collections.ICollection.CopyTo(Array array, int index) { for (int i = 0; i < size; i++) array.SetValue(new KeyValuePair<K, V>(keys[i], values[i]), index + i); }
        public struct Enumerator
        {
            public Dictionary<K, V> dictionary;
            public int index;
            public bool MoveNext() { index++; return index < dictionary.size; }
            public KeyValuePair<K, V> Current { get { return new KeyValuePair<K, V>(dictionary.keys[index], dictionary.values[index]); } }
            public void Dispose() { } // foreach lowering struct enumerator'da Dispose cagirir
        }
        public sealed class KeyCollection : System.Collections.ICollection, ICollection<K>, IEnumerable<K>
        {
            public struct Enumerator
            {
                public Dictionary<K, V> dictionary;
                public int index;
                public bool MoveNext() { index++; return index < dictionary.size; }
                public K Current { get { return dictionary.keys[index]; } }
                public void Dispose() { } // foreach lowering struct enumerator'da Dispose cagirir
            }
            public Dictionary<K, V> dictionary;
            public int Count { get { return dictionary.Count; } }
            public void Add(K item) { throw new NotSupportedException(); } // C#: KeyCollection salt-okunur
            public bool IsSynchronized { get { return false; } }
            public object SyncRoot { get { return this; } }
            public void CopyTo(Array array, int index) { throw new NotSupportedException(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new DictionaryKeyInterfaceEnumerator<K, V>(GetEnumerator()); }
            public Dictionary<K, V>.KeyCollection.Enumerator GetEnumerator() { return new Dictionary<K, V>.KeyCollection.Enumerator { dictionary = dictionary, index = -1 }; }
            IEnumerator<K> IEnumerable<K>.GetEnumerator() { return new DictionaryKeyInterfaceEnumerator<K, V>(GetEnumerator()); }
        }
        public sealed class ValueCollection : System.Collections.ICollection, ICollection<V>, IEnumerable<V>
        {
            public struct Enumerator
            {
                public Dictionary<K, V> dictionary;
                public int index;
                public bool MoveNext() { index++; return index < dictionary.size; }
                public V Current { get { return dictionary.values[index]; } }
                public void Dispose() { } // foreach lowering struct enumerator'da Dispose cagirir
            }
            public Dictionary<K, V> dictionary;
            public int Count { get { return dictionary.Count; } }
            public void Add(V item) { throw new NotSupportedException(); } // C#: ValueCollection salt-okunur
            public bool IsSynchronized { get { return false; } }
            public object SyncRoot { get { return this; } }
            public void CopyTo(Array array, int index) { throw new NotSupportedException(); }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new DictionaryValueInterfaceEnumerator<K, V>(GetEnumerator()); }
            public Dictionary<K, V>.ValueCollection.Enumerator GetEnumerator() { return new Dictionary<K, V>.ValueCollection.Enumerator { dictionary = dictionary, index = -1 }; }
            IEnumerator<V> IEnumerable<V>.GetEnumerator() { return new DictionaryValueInterfaceEnumerator<K, V>(GetEnumerator()); }
        }
        public int[] buckets; // 1-tabanli slot indeksi; 0 = bos
        public int[] hashes;  // cache'li hash: buyumede rehash Equals'siz
        public int[] nexts;   // zincir: 1-tabanli, 0 = son
        public K[] keys;
        public V[] values;
        public int size;
        public IEqualityComparer<K> comparer; // null = K.GetHashCode/Equals dogrudan (sifir ek maliyet)
        public Dictionary(int capacity) : this() { } // kapasite ipucu: buyume zaten dinamik
        public Dictionary(IEqualityComparer<K> comparer) : this() { this.comparer = comparer; }
        public Dictionary(int capacity, IEqualityComparer<K> comparer) : this() { this.comparer = comparer; }
        public Dictionary()
        {
            buckets = new int[8];
            hashes = new int[8];
            nexts = new int[8];
            keys = new K[8];
            values = new V[8];
            size = 0;
        }
        int HashOf(K key) { if (key == null) { throw new ArgumentNullException("key"); } return (comparer == null ? key.GetHashCode() : comparer.GetHashCode(key)) & 0x7FFFFFFF; }
        bool Eq(K stored, K key) { return comparer == null ? stored.Equals(key) : comparer.Equals(stored, key); }
        public int Count { get { return size; } }
        public Dictionary<K, V>.KeyCollection Keys { get { return new Dictionary<K, V>.KeyCollection { dictionary = this }; } }
        public Dictionary<K, V>.ValueCollection Values { get { return new Dictionary<K, V>.ValueCollection { dictionary = this }; } }
        public Dictionary<K, V>.Enumerator GetEnumerator() { return new Dictionary<K, V>.Enumerator { dictionary = this, index = -1 }; }
        // ICollection<KeyValuePair<K,V>> / IEnumerable<KeyValuePair<K,V>> (IDictionary tabani)
        IEnumerator<KeyValuePair<K, V>> IEnumerable<KeyValuePair<K, V>>.GetEnumerator() { return new DictionaryInterfaceEnumerator<K, V>(GetEnumerator()); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new DictionaryInterfaceEnumerator<K, V>(GetEnumerator()); }
        void ICollection<KeyValuePair<K, V>>.Add(KeyValuePair<K, V> item) { this[item.Key] = item.Value; }
        public int Find(K key)
        {
            int h = HashOf(key);
            int i = buckets[h & (buckets.Length - 1)] - 1;
            while (i >= 0)
            {
                if (hashes[i] == h && Eq(keys[i], key)) { return i; }
                i = nexts[i] - 1;
            }
            return -1;
        }
        public bool ContainsKey(K key) { return Find(key) >= 0; }
        public bool Remove(K key)
        {
            int hash = HashOf(key);
            int bucket = hash & (buckets.Length - 1);
            int previous = -1;
            int index = buckets[bucket] - 1;
            while (index >= 0)
            {
                if (hashes[index] == hash && Eq(keys[index], key)) { break; }
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
            if (ContainsKey(key)) { throw new ArgumentException("An item with the same key has already been added. Key: " + key); }
            this[key] = value;
        }
        public bool TryGetValue(K key, out V value)
        {
            int i = Find(key);
            if (i >= 0) { value = values[i]; return true; }
            value = default(V);
            return false;
        }
        public bool Remove(K key, out V value)
        {
            int i = Find(key);
            if (i < 0) { value = default(V); return false; }
            value = values[i];
            return Remove(key);
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
                int h = HashOf(key);
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
        public void Grow()
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
