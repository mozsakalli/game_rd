// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using System.ComponentModel;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.SubsystemsImplementation;

namespace UnityEngine.XR
{
    // This partial class contains the pure C# logic and helper APIs for the XRDisplaySubsystem.
    // The native bindings (extern methods) are defined in the corresponding XRDisplaySubsystem.bindings.cs file.
    // New pure C# helper methods should be added to this file.

    public partial class XRDisplaySubsystem
    {
        [NoAutoStaticsCleanup] // infrastructure cache; no user refs
        private static readonly List<XRDisplaySubsystem> s_DisplaySubsystems = new List<XRDisplaySubsystem>();
        [NoAutoStaticsCleanup] // infrastructure cache; no user refs
        private static readonly XRDisplaySubsystemDefault s_Default = XRDisplaySubsystemDefault.instance;

        ///<summary>The active display subsystem, if any. If no display subsystem is active, this property value is <c>null</c>.</summary>
        ///<remarks>A helper property that references the first display subsystem instance in the list returned by <see cref="SubsystemManager.GetSubsystems" /> or <c>null</c> if the list is empty.
        ///
        ///Your code should use this property instead of <see cref="activeSubsystemOrStub" />.</remarks>
        public static XRDisplaySubsystem activeSubsystem
        {
            get
            {
                SubsystemManager.GetSubsystems(s_DisplaySubsystems);
                return s_DisplaySubsystems.Count > 0 ? s_DisplaySubsystems[0] : null;
            }
        }

        ///<summary>The active display subsystem, if any. If no display subsystem is active, this property value references a default, stub subsystem whose members return safe values.</summary>
        ///<remarks>This helper property references a stub display subsystem instead of <c>null</c> when no display subsystem is active. The property is intended for use when updating obsolete APIs in the <c>XRDevice</c> and <c>XRStats</c> classes to the replacement APIs in <c>XRDisplaySubsystem</c>.
        ///                    You should not use this property in your own code. Instead, use the <see cref="activeSubsystem" /> property and check whether it is <c>null</c>. Explicitly checking for <c>null</c> allows you to correctly handle cases where an XR display is unavailable.
        ///                    In addition, the returned stub display subsystem only provides safe default values for those members needed to replace the deprecated APIs. Therefore, accessing other display subsystem members could produce unexpected results.</remarks>
        public static XRDisplaySubsystem activeSubsystemOrStub
        {
            get
            {
                var subsystem = activeSubsystem;
                return subsystem ?? s_Default;
            }
        }
    }
}
