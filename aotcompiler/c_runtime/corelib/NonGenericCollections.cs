// Corelib: legacy System.Collections yuzeyi — .NET sekliyle BIREBIR (arayuzler artik tam modellenir:
// bir sinif IList implement ediyorsa Hierarchy tum uyeleri ister; eksik/uyumsuz imza = hata).
// List<T> -> IList kutulama adaptoru yok (bilincli sinir).
namespace System.Collections
{
    public interface IEnumerator
    {
        bool MoveNext();
        object Current { get; }
        void Reset();
    }

    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    public interface ICollection : IEnumerable
    {
        int Count { get; }
        bool IsSynchronized { get; }
        object SyncRoot { get; }
        void CopyTo(Array array, int index);
    }

    public interface IList : ICollection, IEnumerable
    {
        object this[int index] { get; set; }
        bool IsFixedSize { get; }
        bool IsReadOnly { get; }
        int Add(object value);
        void Clear();
        bool Contains(object value);
        int IndexOf(object value);
        void Insert(int index, object value);
        void Remove(object value);
        void RemoveAt(int index);
    }

    public interface IDictionaryEnumerator : IEnumerator
    {
        DictionaryEntry Entry { get; }
        object Key { get; }
        object Value { get; }
    }

    public struct DictionaryEntry
    {
        public object Key;
        public object Value;
        public DictionaryEntry(object key, object value) { Key = key; Value = value; }
    }

    public interface IDictionary : ICollection, IEnumerable
    {
        object this[object key] { get; set; }
        ICollection Keys { get; }
        ICollection Values { get; }
        bool IsReadOnly { get; }
        bool IsFixedSize { get; }
        bool Contains(object key);
        void Add(object key, object value);
        void Clear();
        new IDictionaryEnumerator GetEnumerator();
        void Remove(object key);
    }
}
namespace System
{
    // Array runtime operations remain VmArray intrinsics; this class supplies the common
    // reference type used by legacy collection signatures such as ICollection.CopyTo.
    public class Array
    {
        public extern int Length { get; } // ldlen: frontend intrinsic (VmArray.len)
        public static extern int IndexOf(string[] array, string value);
        public static extern void Copy(byte[] sourceArray, byte[] destinationArray, int length);
        public static extern void Copy(byte[] sourceArray, int sourceIndex, byte[] destinationArray, int destinationIndex, int length);

        public static T[] Empty<T>()
        {
            return new T[0];
        }

        public static void Copy<T>(T[] sourceArray, T[] destinationArray, int length)
        {
            Copy(sourceArray, 0, destinationArray, 0, length);
        }

        public static void Copy<T>(T[] sourceArray, int sourceIndex, T[] destinationArray, int destinationIndex, int length)
        {
            if (sourceIndex < 0 || destinationIndex < 0 || length < 0 ||
                sourceIndex + length > sourceArray.Length || destinationIndex + length > destinationArray.Length)
                throw new IndexOutOfRangeException();
            if (sourceArray == destinationArray && destinationIndex > sourceIndex && destinationIndex < sourceIndex + length)
            {
                for (int i = length - 1; i >= 0; i--) destinationArray[destinationIndex + i] = sourceArray[sourceIndex + i];
                return;
            }
            for (int i = 0; i < length; i++) destinationArray[destinationIndex + i] = sourceArray[sourceIndex + i];
        }

        public static void Resize<T>(ref T[] array, int newSize)
        {
            T[] resized = new T[newSize];
            if (array != null)
            {
                int count = array.Length < newSize ? array.Length : newSize;
                for (int i = 0; i < count; i++) resized[i] = array[i];
            }
            array = resized;
        }

        public static void Sort<T>(T[] array, Comparison<T> comparison)
        {
            if (array == null) throw new NullReferenceException();
            for (int i = 1; i < array.Length; i++)
            {
                T value = array[i];
                int cursor = i - 1;
                while (cursor >= 0 && comparison(array[cursor], value) > 0) { array[cursor + 1] = array[cursor]; cursor--; }
                array[cursor + 1] = value;
            }
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] array, Converter<TInput, TOutput> converter)
        {
            if (array == null) throw new NullReferenceException();
            TOutput[] r = new TOutput[array.Length];
            for (int i = 0; i < array.Length; i++) r[i] = converter(array[i]);
            return r;
        }

        public static void Clear<T>(T[] array)
        {
            if (array == null) throw new NullReferenceException();
            for (int i = 0; i < array.Length; i++) array[i] = default(T);
        }

        // Non-generic System.Array yuzeyi (Roslyn bu asiri yuklemeleri secer): eleman tipinden
        // bagimsiz, VmArray elemsize uzerinden memmove/memset (C: vmarray_copy/vmarray_clear).
        public static extern void Copy(Array sourceArray, Array destinationArray, int length);
        public static extern void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length);
        public static extern void Clear(Array array);
        public static extern void Clear(Array array, int index, int length);

        public static void Fill<T>(T[] array, T value)
        {
            if (array == null) throw new NullReferenceException();
            for (int i = 0; i < array.Length; i++) array[i] = value;
        }

        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null) throw new NullReferenceException();
            if (startIndex < 0 || count < 0 || startIndex + count > array.Length) throw new IndexOutOfRangeException();
            for (int i = 0; i < count; i++) array[startIndex + i] = value;
        }

        public static void Sort<T>(T[] array, int index, int length, Collections.Generic.IComparer<T> comparer)
        {
            if (array == null) throw new NullReferenceException();
            if (index < 0 || length < 0 || index + length > array.Length)
                throw new IndexOutOfRangeException();
            for (int i = index + 1; i < index + length; i++)
            {
                T value = array[i];
                int cursor = i - 1;
                while (cursor >= index && comparer.Compare(array[cursor], value) > 0)
                {
                    array[cursor + 1] = array[cursor];
                    cursor--;
                }
                array[cursor + 1] = value;
            }
        }
    }
}