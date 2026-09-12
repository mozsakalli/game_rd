// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.Search
{
    enum SearchAsyncResolutionState
    {
        Unresolved = 0,
        Resolving,
        Failed,
        Resolved
    }

    readonly record struct SearchAsyncResult<T>(T Value, SearchAsyncResolutionState State)
    {
        public T Value { get; } = Value;
        public SearchAsyncResolutionState State { get; } = State;

        [NoAutoStaticsCleanup] // Immutable sentinel holding only default/null values; safe to persist across reload.
        public static SearchAsyncResult<T> Unresolved { get; } = new(default, SearchAsyncResolutionState.Unresolved);
    }
}
