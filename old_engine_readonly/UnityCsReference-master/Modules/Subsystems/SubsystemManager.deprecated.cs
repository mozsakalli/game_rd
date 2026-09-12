// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine
{
    public static partial class SubsystemManager
    {
        ///<summary>Returns active <see cref="Subsystem" />s of a specific instance type.
        ///
        ///*Note:* This method is deprecated, use <see cref="GetSubsystems" /> instead.</summary>
        ///<param name="subsystems">Active instances.</param>
        [Obsolete("Use GetSubsystems instead. (UnityUpgradable) -> GetSubsystems<T>(*)", false)]
        public static void GetInstances<T>(List<T> subsystems)
            where T : ISubsystem
        {
            GetSubsystems(subsystems);
        }

#pragma warning disable CS0618
        internal static void AddDeprecatedSubsystem(Subsystem subsystem) => s_DeprecatedSubsystems.Add(subsystem);
        internal static bool RemoveDeprecatedSubsystem(Subsystem subsystem) => s_DeprecatedSubsystems.Remove(subsystem);

        internal static Subsystem FindDeprecatedSubsystemByDescriptor(SubsystemDescriptor descriptor)
        {
            foreach (var subsystem in s_DeprecatedSubsystems)
            {
                if (subsystem.m_SubsystemDescriptor == descriptor)
                    return subsystem;
            }

            return null;
        }

#pragma warning restore CS0618

// event never invoked warning (invoked indirectly from native code)
#pragma warning disable CS0067
        ///<summary>Called from <see cref="SubsystemManager" /> before reloading all XR SDK Provider packaged subsystems.</summary>
        ///<remarks>When the Editor starts or when packages are installed or removed, the <see cref="SubsystemManager" /> searches the packages and loads all XR SDK packages that it finds. Handling this event allows the user to do work they may need prior to the subsystem manager cleaning up any current subsystem descriptors.
        ///
        ///
        ///
        ///*Note:* This is deprecated, use <see cref="beforeReloadSubsystems" /> instead.</remarks>
        ///<seealso cref="SubsystemManager" />
        ///<seealso cref="SubsystemDescriptor" />
        [Obsolete("Use beforeReloadSubsystems instead. (UnityUpgradable) -> beforeReloadSubsystems", false)]
        [AutoStaticsCleanupOnCodeReload]
        public static event Action reloadSubsytemsStarted;

        ///<summary>Called from <see cref="SubsystemManager" /> when it has completed reloading all XR SDK Provider packaged subsystems.</summary>
        ///<remarks>When the Editor starts or when packages are installed or removed, the <see cref="SubsystemManager" /> searches the packages and loads all XR SDK packages that it finds. Handling this event allows the user to do work they may need after the subsystem manager loads and initializes new subsystem descriptors.
        ///
        ///
        ///
        ///*Note:* This is deprecated, use <see cref="afterReloadSubsystems" /> instead.</remarks>
        ///<seealso cref="SubsystemManager" />
        ///<seealso cref="SubsystemDescriptor" />
        [Obsolete("Use afterReloadSubsystems instead. (UnityUpgradable) -> afterReloadSubsystems", false)]
        [AutoStaticsCleanupOnCodeReload]
        public static event Action reloadSubsytemsCompleted;
#pragma warning restore CS0067
    }
}
