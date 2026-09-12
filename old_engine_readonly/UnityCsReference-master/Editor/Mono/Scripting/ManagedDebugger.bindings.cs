// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEditor.Compilation;
using UnityEngine.Scripting;
using Unity.Scripting.LifecycleManagement;

namespace UnityEditor.Scripting
{
    [InitializeOnLoad]
    [NativeHeader("Editor/Src/Scripting/ManagedDebugger.h")]
    public sealed partial class ManagedDebugger
    {
        [AutoStaticsCleanupOnCodeReload]
        public static event Action<bool> debuggerAttached;

        public static bool isAttached
        {
            get { return IsAttached(); }
        }

        public static bool isEnabled
        {
            get { return IsEnabled(); }
        }

        [OnCodeLoaded]
        static void Initialize()
        {
            SubscribeToCodeOptimizationChanged();
        }

        public static void Disconnect()
        {
            DisconnectNative();
        }

        [FreeFunction("ManagedDebugger::Disconnect")]
        private static extern void DisconnectNative();

        [FreeFunction("ManagedDebugger::IsAttached")]
        private static extern bool IsAttached();

        [FreeFunction("ManagedDebugger::IsEnabled")]
        private static extern bool IsEnabled();

        [RequiredByNativeCode]
        private static void OnDebuggerAttached(bool attached)
        {
            if (debuggerAttached != null)
            {
                debuggerAttached(attached);
            }
        }

        private static void OnCodeOptimizationChanged(CodeOptimization codeOptimization)
        {
            if (CodeOptimization.Release == codeOptimization)
            {
                Disconnect();
            }
        }

        private static void SubscribeToCodeOptimizationChanged()
        {
            CompilationPipeline.codeOptimizationChanged += OnCodeOptimizationChanged;
        }
    }
}
