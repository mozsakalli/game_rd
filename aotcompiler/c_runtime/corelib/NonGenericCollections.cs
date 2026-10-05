// Corelib: legacy System.Collections API surface. These interfaces are intentionally
// separate from generic List/Dictionary; a boxing adapter for List<T> -> IList is TODO.
namespace System.Collections
{
    interface IEnumerator
    {
        bool MoveNext();
        object Current { get; }
        void Reset();
    }
    interface IDictionaryEnumerator { }
    interface ICollection { }

    interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    interface IList : IEnumerable
    {
        int Count { get; }
        object this[int index] { get; }
        IEnumerator GetEnumerator();
        void Add(object value);
    }

    interface IDictionary
    {
        bool Contains(object key);
        object this[object key] { get; set; }
        ICollection Keys { get; }
        ICollection Values { get; }
        bool IsReadOnly { get; }
        bool IsFixedSize { get; }
        int Count { get; }
        object SyncRoot { get; }
        bool IsSynchronized { get; }
        void Add(object key, object value);
        void Clear();
        void CopyTo(Array array, int index);
        IDictionaryEnumerator GetEnumerator();
        void Remove(object key);
    }
}

namespace System
{
    // Array runtime operations remain VmArray intrinsics; this class supplies the common
    // reference type used by legacy collection signatures such as ICollection.CopyTo.
    class Array
    {
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