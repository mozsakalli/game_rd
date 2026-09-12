// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.Bindings;

namespace UnityEditor.Build.Analysis
{
    /// <summary>
    /// Cross-module entry point for opening the
    /// Build Analysis window on a specific build.
    /// </summary>
    [VisibleToOtherModules("UnityEditor.BuildProfileModule")]
    static class BuildAnalysisWindowLauncher
    {
        [VisibleToOtherModules("UnityEditor.BuildProfileModule")]
        internal static void OpenWithBuild(GUID buildSessionGuid)
            => BuildAnalysisWindow.ShowWindow(buildSessionGuid);
    }
}
