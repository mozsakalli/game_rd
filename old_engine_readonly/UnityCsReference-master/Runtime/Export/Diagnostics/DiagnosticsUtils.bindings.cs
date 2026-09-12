// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngineInternal;
using UnityEngine.Bindings;


namespace UnityEngine.Diagnostics
{
    public enum ForcedCrashCategory
    {
        AccessViolation = 0,
        FatalError = 1,
        Abort = 2,
        PureVirtualFunction = 3,
        MonoAbort = 4
    }

    [NativeHeader("Runtime/Export/Diagnostics/DiagnosticsUtils.bindings.h")]
    [NativeHeader("Runtime/Misc/GarbageCollectSharedAssets.h")]
    public static class Utils
    {
        [FreeFunction("DiagnosticsUtils_Bindings::ForceCrash", IsThreadSafe = true, ThrowsException = true)]
        extern public static void ForceCrash(ForcedCrashCategory crashCategory);

        [FreeFunction("DiagnosticsUtils_Bindings::NativeAssert", IsThreadSafe = true)]
        extern public static void NativeAssert(string message);

        [FreeFunction("DiagnosticsUtils_Bindings::NativeError", IsThreadSafe = true)]
        extern public static void NativeError(string message);

        [FreeFunction("DiagnosticsUtils_Bindings::NativeWarning", IsThreadSafe = true)]
        extern public static void NativeWarning(string message);

        [FreeFunction("ValidateHeap")]
        extern public static void ValidateHeap();

        // Pauses (pause=true) or resumes (pause=false) CoreCLR's GC stress mode, which forces
        // frequent collections to surface GC-correctness bugs. CoreCLR-only; on other runtimes
        // this is a no-op.
        // This requires that GC stress has been initialized and enabled at startup: the
        // "System.GC.Stress" knob (or the DOTNET_GCStress env var fallback) must be set to
        // a non-zero level. Otherwise this call has no effect.
        [FreeFunction("DiagnosticsUtils_Bindings::SetGCStressPaused")]
        [NativeHeader("Modules/Scripting/Include/Scripting/ScriptingBackend/ScriptingApi.h")]
        extern internal static void SetGCStressPaused(bool pause);
    }
}
