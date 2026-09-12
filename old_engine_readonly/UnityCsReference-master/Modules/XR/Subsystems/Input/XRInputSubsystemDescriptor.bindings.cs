// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UsedByNativeCodeAttribute = UnityEngine.Scripting.UsedByNativeCodeAttribute;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using UnityEngine.Experimental;

namespace UnityEngine.XR
{
    ///<summary>Information about an Input subsystem.</summary>
    [NativeHeader("Modules/XR/XRPrefix.h")]
    [NativeHeader("Modules/XR/Subsystems/Input/XRInputSubsystemDescriptor.h")]
    [UsedByNativeCode]
    [NativeConditional("ENABLE_XR")]
    public class XRInputSubsystemDescriptor : IntegratedSubsystemDescriptor<XRInputSubsystem>
    {
        ///<summary>When true, will suppress legacy support for Daydream, Oculus, OpenVR, and Windows MR built directly into the Unity runtime from generating input. This is useful when adding an XRInputSubsystem that supports these devices.</summary>
        [NativeConditional("ENABLE_XR")]
        public extern bool disablesLegacyInput { get; }

        internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(XRInputSubsystemDescriptor descriptor) => descriptor.m_Ptr;
        }
    }
}
