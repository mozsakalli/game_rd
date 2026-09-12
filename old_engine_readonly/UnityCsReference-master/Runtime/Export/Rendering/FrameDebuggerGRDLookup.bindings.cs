// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;

namespace UnityEngine.Rendering
{
    [NativeHeader("Runtime/Profiler/PerformanceTools/FrameDebuggerGRDLookup.h")]
    [StaticAccessor("FrameDebuggerGRDLookup", StaticAccessorType.DoubleColon)]
    internal static class FrameDebuggerGRDLookup
    {
        extern internal static void Add(EntityId id, byte reason);
        extern internal static void Remove(EntityId id);
        extern internal static void Clear();
    }
}
