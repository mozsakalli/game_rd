// corelib: System.Collections.Concurrent — TEK THREAD modeli (motor tek is parcacikli: Task/async tek pump). Kilit/CAS YOK;
// yuzey .NET ile ayni, semantik tek-is-parcacikli Dictionary<K,V> sarmalayicisi. Kullanici DLL'leri (Digitoy.Common) IL'de bu
// tipi adiyla referans eder; CilFrontend adla cozer -> frontend'de remap/intrinsic yok (kimlik = tip, ad degil).
// IMZALAR .NET ILE BIREBIR (donus tipleri dahil): kullanici DLL'i gercek .NET'e karsi derlenir; GetEnumerator burada ARAYUZ doner
// (Dictionary'deki struct Enumerator degil) - aksi halde IL arayuz beklerken struct kutulanip InvalidCast olur.
// Bilincli kismi yuzey: ConcurrentBag/Queue/Stack yok (gerekirse buraya eklenir).
namespace System.Collections.Concurrent
{
    using System.Collections.Generic;

    public class ConcurrentDictionary<K, V> : IDictionary<K, V>, IReadOnlyDictionary<K, V>, System.Collections.IEnumerable
    {
        readonly Dictionary<K, V> map;

        public ConcurrentDictionary() { map = new Dictionary<K, V>(); }
        public ConcurrentDictionary(IEqualityComparer<K> comparer) { map = new Dictionary<K, V>(comparer); }
        public ConcurrentDictionary(int concurrencyLevel, int capacity) { map = new Dictionary<K, V>(capacity); } // concurrencyLevel anlamsiz (tek thread)
        public ConcurrentDictionary(int concurrencyLevel, int capacity, IEqualityComparer<K> comparer) { map = new Dictionary<K, V>(capacity, comparer); }

        public int Count { get { return map.Count; } }
        public bool IsEmpty { get { return map.Count == 0; } }
        public ICollection<K> Keys { get { return map.Keys; } }
        public ICollection<V> Values { get { return map.Values; } }
        Dictionary<K, V>.KeyCollection IDictionary<K, V>.Keys { get { return map.Keys; } }
        Dictionary<K, V>.ValueCollection IDictionary<K, V>.Values { get { return map.Values; } }
        Dictionary<K, V>.KeyCollection IReadOnlyDictionary<K, V>.Keys { get { return map.Keys; } }
        Dictionary<K, V>.ValueCollection IReadOnlyDictionary<K, V>.Values { get { return map.Values; } }
        public V this[K key] { get { return map[key]; } set { map[key] = value; } }

        public bool ContainsKey(K key) { return map.ContainsKey(key); }
        public bool TryGetValue(K key, out V value) { return map.TryGetValue(key, out value); }
        public bool TryAdd(K key, V value)
        {
            if (map.ContainsKey(key)) return false;
            map[key] = value;
            return true;
        }
        public bool TryRemove(K key, out V value)
        {
            if (!map.TryGetValue(key, out value)) return false;
            map.Remove(key);
            return true;
        }
        public bool TryUpdate(K key, V newValue, V comparisonValue)
        {
            V cur;
            if (!map.TryGetValue(key, out cur)) return false;
            if (!EqualityComparer<V>.Default.Equals(cur, comparisonValue)) return false;
            map[key] = newValue;
            return true;
        }
        public V GetOrAdd(K key, V value)
        {
            V cur;
            if (map.TryGetValue(key, out cur)) return cur;
            map[key] = value;
            return value;
        }
        public V GetOrAdd(K key, Func<K, V> valueFactory)
        {
            V cur;
            if (map.TryGetValue(key, out cur)) return cur;
            V v = valueFactory(key);
            map[key] = v;
            return v;
        }
        public V AddOrUpdate(K key, V addValue, Func<K, V, V> updateValueFactory)
        {
            V cur;
            V v = map.TryGetValue(key, out cur) ? updateValueFactory(key, cur) : addValue;
            map[key] = v;
            return v;
        }
        public V AddOrUpdate(K key, Func<K, V> addValueFactory, Func<K, V, V> updateValueFactory)
        {
            V cur;
            V v = map.TryGetValue(key, out cur) ? updateValueFactory(key, cur) : addValueFactory(key);
            map[key] = v;
            return v;
        }
        public void Clear() { map.Clear(); }
        public KeyValuePair<K, V>[] ToArray()
        {
            var result = new KeyValuePair<K, V>[map.Count];
            int i = 0;
            foreach (var kv in map) result[i++] = kv;
            return result;
        }

        public IEnumerator<KeyValuePair<K, V>> GetEnumerator() { return new DictionaryInterfaceEnumerator<K, V>(map.GetEnumerator()); }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return new DictionaryInterfaceEnumerator<K, V>(map.GetEnumerator()); }
        // IDictionary<K,V> : ICollection<KeyValuePair<K,V>>
        void ICollection<KeyValuePair<K, V>>.Add(KeyValuePair<K, V> item) { map[item.Key] = item.Value; }
        bool IDictionary<K, V>.Remove(K key) { return map.Remove(key); }
    }
}
