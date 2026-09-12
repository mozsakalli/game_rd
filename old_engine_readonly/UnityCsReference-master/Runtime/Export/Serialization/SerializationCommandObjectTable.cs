// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using System.Threading;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine
{
    /// <summary>
    /// Intern table for managed objects (delegates, Types, MemberInfos, ...) that
    /// native serialization commands need to reference. Commands store a small
    /// integer index into this table instead of holding a GCHandle directly,
    /// which keeps command storage compact and centralizes the GC-root lifecycle.
    ///
    /// The table grows monotonically and is cleared together with the
    /// SerializationCache (typically on a domain reload). Calls to <see cref="Intern"/>
    /// dedupe by default equality so two equivalent delegates / two references to
    /// the same Type collapse onto the same index.
    /// </summary>
    /// <remarks>
    /// Threading: writes are serialized by <c>s_GrowLock</c>. Reads via
    /// <see cref="Get"/> are lock-free — the backing array reference is published
    /// with <see cref="Volatile.Write"/> and observed with <see cref="Volatile.Read"/>,
    /// so a reader sees either the pre-resize array (still valid for the index
    /// requested, since the old array remains alive while any reader holds it)
    /// or the post-resize array (also valid).
    /// </remarks>
    internal static partial class SerializationCommandObjectTable
    {
        private const int InitialCapacity = 64;

        // The four fields below are released as a unit by Clear(), which runs under s_GrowLock on
        // [OnCodeUnloading] (see below). Field-level auto-cleanup is unsuitable here: it cannot take the
        // grow lock (racing concurrent Get/Intern) and resetting the backing array to null would NRE the
        // next Intern/Get. So each field opts out and defers to the lock-safe Clear() hook.
        [NoAutoStaticsCleanup] // interned user delegates/Types released by [OnCodeUnloading] Clear(); array allocation persists
        private static object[] s_Objects = new object[InitialCapacity];
        [NoAutoStaticsCleanup] // reset to 0 by [OnCodeUnloading] Clear()
        private static int s_Count;
        [NoAutoStaticsCleanup] // interned user object keys released by [OnCodeUnloading] Clear()
        private static readonly Dictionary<object, int> s_Dedup = new Dictionary<object, int>(InitialCapacity);
        [NoAutoStaticsCleanup] // lock object, holds no references, safe to persist
        private static readonly object s_GrowLock = new object();

        /// <summary>
        /// Returns the existing index for <paramref name="obj"/> if it's already
        /// in the table, otherwise appends it and returns the new index.
        /// </summary>
        internal static int Intern(object obj)
        {
            lock (s_GrowLock)
            {
                if (s_Dedup.TryGetValue(obj, out int existing))
                    return existing;

                if (s_Count == s_Objects.Length)
                {
                    var grown = new object[s_Objects.Length * 2];
                    System.Array.Copy(s_Objects, grown, s_Count);
                    // Atomic publish so concurrent readers see either the
                    // pre-resize or fully-populated post-resize array.
                    Volatile.Write(ref s_Objects, grown);
                }

                int idx = s_Count;
                s_Objects[idx] = obj;
                s_Dedup[obj] = idx;
                s_Count++;
                return idx;
            }
        }

        /// <summary>
        /// Lock-free index lookup. Caller must pass an index previously returned
        /// by <see cref="Intern"/> on a still-valid table generation.
        /// </summary>
        internal static object Get(int index)
        {
            return Volatile.Read(ref s_Objects)[index];
        }

        /// <summary>
        /// Empties the table and the dedup map. Called from the same site that
        /// invalidates the SerializationCache — typically on domain reload.
        /// Also runs on [OnCodeUnloading] so interned old-ALC delegates/Types are
        /// released before the reloadable assemblies unload.
        /// </summary>
        [OnCodeUnloading]
        internal static void Clear()
        {
            lock (s_GrowLock)
            {
                if (s_Count > 0)
                    System.Array.Clear(s_Objects, 0, s_Count);
                s_Count = 0;
                s_Dedup.Clear();
            }
        }
    }
}
