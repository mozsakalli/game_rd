// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UsedByNativeCodeAttribute = UnityEngine.Scripting.UsedByNativeCodeAttribute;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine.XR
{
    ///<summary>Engine reserved blit modes. Blit mode capabilities should be queried from <see cref="XRDisplaySubsystemDescriptor.GetAvailableMirrorBlitModeCount" /> and <see cref="XRDisplaySubsystemDescriptor.GetMirrorBlitModeByIndex" />.</summary>
    public struct XRMirrorViewBlitMode
    {
        // *MUST* be in sync with the kUnityXRMirrorBlitDefault
        ///<summary>Mirror view pass should blit platform default image to the mirror target.</summary>
        public const int Default = 0;
        // *MUST* be in sync with the kUnityXRMirrorBlitLeftEye
        ///<summary>Mirror view pass should blit left eye image to the mirror target.</summary>
        public const int LeftEye = -1;
        // *MUST* be in sync with the kUnityXRMirrorBlitRightEye
        ///<summary>Mirror view pass should blit right eye image to the mirror target.</summary>
        public const int RightEye = -2;
        // *MUST* be in sync with the kUnityXRMirrorBlitSideBySide
        ///<summary>Mirror view pass should blit left eye image and right eye image in a side-by-side fashion to the mirror target.</summary>
        public const int SideBySide = -3;
        // *MUST* be in sync with the kUnityXRMirrorBlitSideBySideOcclusionMesh
        ///<summary>Mirror view pass should blit similar to side-by-side mode, but also showing not rendered pixels saved by the occlusion mesh.</summary>
        public const int SideBySideOcclusionMesh = -4;
        // *MUST* be in sync with the kUnityXRMirrorBlitDistort
        ///<summary>Mirror view pass should blit after distortion pass image to the mirror target.</summary>
        public const int Distort = -5;
        // *MUST* be in sync with the kUnityXRMirrorBlitNone
        ///<summary>Mirror view pass should not be performed.</summary>
        public const int None = -6;
        // *MUST* be in sync with the kUnityXRMirrorBlitMotionVectors
        ///<summary>Mirror view pass should blit left eye image and right eye image in a side-by-side fashion to the mirror target, displaying motion vectors.</summary>
        public const int MotionVectors = -7;
    }

    ///<summary>Struct that describes the mirror view blit mode.</summary>
    [NativeHeader("Modules/XR/XRPrefix.h")]
    [NativeHeader("Modules/XR/Subsystems/Display/XRDisplaySubsystemDescriptor.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct XRMirrorViewBlitModeDesc
    {
        ///<summary>Mirror view blit mode Id. For details, see <see cref="XRMirrorViewBlitMode" />. In case of provider's custom blit mode, the value wouldn't be the reserved XRMirrorViewBlitMode.</summary>
        public int blitMode;
        ///<summary>String that describes the mirror view blit mode.</summary>
        public String blitModeDesc;
    }

    ///<summary>Class providing information about <see cref="XRDisplaySubsystem" /> registration.</summary>
    [NativeHeader("Modules/XR/Subsystems/Display/XRDisplaySubsystemDescriptor.h")]
    [UsedByNativeCode]
    public class XRDisplaySubsystemDescriptor : IntegratedSubsystemDescriptor<XRDisplaySubsystem>
    {
        ///<summary>Indicates whether legacy VR settings must be disabled for the subsystem. Set to true if the Editor must disable the legacy VR settings  disabled; otherwise false.</summary>
        [NativeConditional("ENABLE_XR")]
        public extern bool disablesLegacyVr { get; }

        ///<summary>Indicates whether MSAA must be resolved in the back buffer. Set to true if MSAA needs to be resolved in the back buffer; otherwise false.</summary>
        [NativeConditional("ENABLE_XR")]
        public extern bool enableBackBufferMSAA { get; }

        ///<summary>Get current display subsystem's total number of supported mirror blit modes.</summary>
        ///<returns>Number of supported mirror blit modes.</returns>
        [NativeConditional("ENABLE_XR")]
        [NativeMethod("TryGetAvailableMirrorModeCount")]
        extern public int GetAvailableMirrorBlitModeCount();

        ///<summary>Get a supported mirror view blit mode from the current display subsystem descriptor.</summary>
        ///<param name="mode">
        ///  <see cref="XRMirrorViewBlitMode" /> to populate.</param>
        ///<param name="index">Index of the mirror blit mode to get.</param>
        [NativeConditional("ENABLE_XR")]
        [NativeMethod("TryGetMirrorModeByIndex")]
        extern public void GetMirrorBlitModeByIndex(int index, out XRMirrorViewBlitModeDesc mode);

        internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(XRDisplaySubsystemDescriptor descriptor) => descriptor.m_Ptr;
        }
    }
}
