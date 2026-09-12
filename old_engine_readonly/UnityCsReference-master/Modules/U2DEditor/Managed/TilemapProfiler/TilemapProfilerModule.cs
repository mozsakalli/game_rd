// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Profiling;
using Unity.Profiling.Editor;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.U2D.Profiling
{
    [Serializable]
    [ProfilerModuleMetadata("2D Tilemap", IconPath = "U2DEditor/TilemapProfiler/Icon/Tilemap@16.png")]
    class TilemapProfilerModule :ProfilerModule
    {
        // Immutable profiler counter descriptors built once from constant marker names; no user-type references, safe to persist across a code reload.
        [NoAutoStaticsCleanup]
        static readonly ProfilerCounterDescriptor[] k_Counters = new ProfilerCounterDescriptor[]
        {
            new ProfilerCounterDescriptor(TilemapProfilerMarkers.k_TilemapCounterName, ProfilerCategory.U2D),
            new ProfilerCounterDescriptor(TilemapProfilerMarkers.k_TilemapChunkCounterName, ProfilerCategory.U2D),
            new ProfilerCounterDescriptor(TilemapProfilerMarkers.k_TilemapChunkMeshesName, ProfilerCategory.U2D),
        };

        public TilemapProfilerModule()
            : base(k_Counters, ProfilerModuleChartType.Line) { }

        public override ProfilerModuleViewController CreateDetailsViewController()
        {
            return new TilemapProfilerController(ProfilerWindow);
        }
    }
}
