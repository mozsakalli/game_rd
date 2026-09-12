// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Collections;

namespace Unity.U2D.Physics
{
    /// <summary>
    /// A filter applied to each element of a physics result array during enumeration.
    /// </summary>
    /// <remarks>
    /// Implement this on a value type so the enumeration inlines, allocates nothing, and runs inside Burst.
    /// Any arguments the filter needs are carried as fields on the implementing struct.
    /// </remarks>
    public interface IPhysicsResultFilter<T> where T : unmanaged
    {
        /// <summary>
        /// Decide whether a single result element is kept.
        /// </summary>
        /// <remarks>
        /// The element is passed by reference to avoid copying large result structs.
        /// </remarks>
        /// <param name="item">The result element being tested.</param>
        /// <returns>True to keep the element in the enumeration, false to skip it.</returns>
        bool Keep(in T item);
    }

    /// <summary>
    /// A lazy, allocation-free view over a physics result array that yields only the elements a filter keeps.
    /// </summary>
    /// <remarks>
    /// This is a ref struct so it cannot escape to the heap, which keeps the enumeration Burst-compatible.
    /// It does not own the source array and never disposes it, so disposal stays with whoever created the results.
    /// </remarks>
    public readonly ref struct PhysicsResultEnumerable<T, TFilter>
        where T : unmanaged
        where TFilter : IPhysicsResultFilter<T>
    {
        /// <summary>
        /// Create a filtered view over a physics result array.
        /// </summary>
        /// <param name="source">The result array to iterate.</param>
        /// <param name="filter">The filter deciding which elements are kept.</param>
        public PhysicsResultEnumerable(NativeArray<T> source, TFilter filter)
        {
            m_Source = source;
            m_Filter = filter;
        }

        /// <summary>
        /// Get an enumerator that walks the source array and yields only the kept elements.
        /// </summary>
        /// <returns>A value-type enumerator over the filtered elements.</returns>
        public Enumerator GetEnumerator() => new Enumerator(m_Source, m_Filter);

        /// <summary>
        /// Walks a physics result array and yields only the elements the filter keeps.
        /// </summary>
        /// <remarks>
        /// This is a value-type enumerator so a foreach over the filtered view allocates nothing.
        /// </remarks>
        public ref struct Enumerator
        {
            /// <summary>
            /// Create an enumerator over a result array and its filter.
            /// </summary>
            /// <param name="source">The result array to iterate.</param>
            /// <param name="filter">The filter deciding which elements are kept.</param>
            public Enumerator(NativeArray<T> source, TFilter filter)
            {
                m_Source = source;
                m_Filter = filter;
                m_Index = -1;
            }

            /// <summary>
            /// Get the element at the current position of the enumerator.
            /// </summary>
            public T Current => m_Source[m_Index];

            /// <summary>
            /// Advance to the next element the filter keeps.
            /// </summary>
            /// <returns>True if a kept element was found, false once the source is exhausted.</returns>
            public bool MoveNext()
            {
                while (++m_Index < m_Source.Length)
                {
                    if (m_Filter.Keep(m_Source[m_Index]))
                        return true;
                }

                return false;
            }

            #region Internal

            NativeArray<T> m_Source;
            TFilter m_Filter;
            int m_Index;

            #endregion
        }

        #region Internal

        readonly NativeArray<T> m_Source;
        readonly TFilter m_Filter;

        #endregion
    }

    /// <summary>
    /// Fluent filtering extensions for the physics result arrays returned by queries, events, and other physics operations.
    /// </summary>
    public static class PhysicsResultExtensions
    {
        /// <summary>
        /// Produce a lazy, allocation-free view over a physics result array that yields only the elements the filter keeps.
        /// </summary>
        /// <remarks>
        /// The filter is a value type so the whole enumeration inlines and runs inside Burst.
        /// </remarks>
        /// <param name="source">The result array to filter.</param>
        /// <param name="filter">The filter deciding which elements are kept.</param>
        /// <returns>A filtered view that can be iterated with foreach.</returns>
        public static PhysicsResultEnumerable<T, TFilter> Filter<T, TFilter>(this NativeArray<T> source, TFilter filter)
            where T : unmanaged
            where TFilter : IPhysicsResultFilter<T>
            => new PhysicsResultEnumerable<T, TFilter>(source, filter);
    }
}
