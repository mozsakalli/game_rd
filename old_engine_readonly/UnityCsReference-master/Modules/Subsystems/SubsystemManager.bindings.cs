// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Gives access to subsystems which provide additional functionality through plugins.</summary>
    ///<remarks>Provides the ability to query for <see cref="SubsystemDescriptor" />s which enumerate features. Given an <see cref="SubsystemDescriptor" />, you can create an <see cref="Subsystem" /> to utilize the subsystem.</remarks>
    [NativeHeader("Modules/Subsystems/SubsystemManager.h")]
    public static partial class SubsystemManager
    {
        // ReSharper disable once UnusedMember.Local - called by native code
        [RequiredByNativeCode]
        static void ReloadSubsystemsStarted()
        {
            if (reloadSubsytemsStarted != null)
                reloadSubsytemsStarted();

            if (beforeReloadSubsystems != null)
                beforeReloadSubsystems();
        }

        // ReSharper disable once UnusedMember.Local - called by native code
        [RequiredByNativeCode]
        static void ReloadSubsystemsCompleted()
        {
            if (reloadSubsytemsCompleted != null)
                reloadSubsytemsCompleted();

            if (afterReloadSubsystems != null)
                afterReloadSubsystems();
        }

        // ReSharper disable once UnusedMember.Local - called by native code
        [RequiredByNativeCode]
        static void InitializeIntegratedSubsystem(IntPtr ptr, IntegratedSubsystem subsystem)
        {
            subsystem.m_Ptr = ptr;
            subsystem.SetHandle(subsystem);
            s_IntegratedSubsystems.Add(subsystem);
        }

        // ReSharper disable once UnusedMember.Local - called by native code
        [RequiredByNativeCode]
        static void ClearSubsystems()
        {
            if (s_IntegratedSubsystems == null)
                return;

            foreach (var subsystem in s_IntegratedSubsystems)
                subsystem.m_Ptr = IntPtr.Zero;

            s_IntegratedSubsystems.Clear();
            s_StandaloneSubsystems.Clear();
            s_DeprecatedSubsystems.Clear();
        }

        // We want to construct the map after new code has been loaded.
        [OnCodeLoaded]
        static extern void StaticConstructScriptingClassMap();
        internal static extern void ReportSingleSubsystemAnalytics(string id);
    }
}
