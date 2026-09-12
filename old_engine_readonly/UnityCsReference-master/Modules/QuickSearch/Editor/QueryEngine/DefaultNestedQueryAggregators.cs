// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.Search
{
    static class MaxAggregator<T>
    {
        public static IEnumerable<T> Aggregate(IEnumerable<T> enumerable)
        {
#pragma warning disable UAC2002 // Avoid Linq
            var empty = !enumerable.Any();
#pragma warning restore UAC2002
            #pragma warning disable UAC2001 // Avoid Linq
            return empty ? System.Array.Empty<T>() : [enumerable.Max()];
#pragma warning restore UAC2001
        }
    }

    static class MinAggregator<T>
    {
        public static IEnumerable<T> Aggregate(IEnumerable<T> enumerable)
        {
#pragma warning disable UAC2002 // Avoid Linq
            var empty = !enumerable.Any();
#pragma warning restore UAC2002
            #pragma warning disable UAC2001 // Avoid Linq
            return empty ? System.Array.Empty<T>() : [enumerable.Min()];
#pragma warning restore UAC2001
        }
    }

    static class FirstAggregator<T>
    {
        public static IEnumerable<T> Aggregate(IEnumerable<T> enumerable)
        {
#pragma warning disable UAC2002 // Avoid Linq
            var empty = !enumerable.Any();
#pragma warning restore UAC2002
            #pragma warning disable UAC2010 // Avoid Linq
            return empty ? System.Array.Empty<T>() : [enumerable.First()];
#pragma warning restore UAC2010
        }
    }

    static class LastAggregator<T>
    {
        public static IEnumerable<T> Aggregate(IEnumerable<T> enumerable)
        {
#pragma warning disable UAC2002 // Avoid Linq
            var empty = !enumerable.Any();
#pragma warning restore UAC2002
            #pragma warning disable UAC2009 // Avoid Linq
            return empty ? System.Array.Empty<T>() : [enumerable.Last()];
#pragma warning restore UAC2009
        }
    }
}
