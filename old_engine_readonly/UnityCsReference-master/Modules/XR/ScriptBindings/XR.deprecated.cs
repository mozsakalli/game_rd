// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine.XR
{
    public static partial class XRSettings
    {
        ///<summary>Loads the requested device at the beginning of the next frame.</summary>
        ///<remarks>This API is deprecated and should no longer be used.</remarks>
        ///<param name="deviceName">Name of the device from <see cref="XRSettings.supportedDevices" />.</param>
        [Obsolete("XRSettings.LoadDeviceByName is deprecated and should no longer be used. Instead, query subsystem descriptors via the SubsystemManager to create and start the subsystems you need.", true)]
        public static void LoadDeviceByName(string deviceName)
        {
            LoadDeviceByName(new string[] { deviceName });
        }

        ///<summary>Loads the requested device at the beginning of the next frame.</summary>
        ///<remarks>This API is deprecated and should no longer be used.</remarks>
        ///<param name="prioritizedDeviceNameList">Prioritized list of device names from <see cref="XRSettings.supportedDevices" />.</param>
        [Obsolete("XRSettings.LoadDeviceByName is deprecated and should no longer be used. Instead, query subsystem descriptors via the SubsystemManager to create and start the subsystems you need..", true)]
        public static void LoadDeviceByName(string[] prioritizedDeviceNameList)
        {
            throw new NotSupportedException("XRSettings.LoadDeviceByName is deprecated and no longer supported.");
        }
    }

    ///<summary>Represents the size of physical space available for XR.</summary>
    [Obsolete("TrackingSpaceType is obsolete, and should no longer be used. Please use TrackingOriginModeFlags.", true)]
    public enum TrackingSpaceType
    {
        ///<summary>Represents a small space where movement may be constrained or positional tracking is unavailable.</summary>
        Stationary,
        ///<summary>Represents a space large enough for free movement.</summary>
        RoomScale
    }

    ///<summary>Contains all functionality related to a XR device.</summary>
    [NativeConditional("ENABLE_VR")]
    [Obsolete("UnityEngine.VRModule is deprecated and will be removed in a future version. Please use the APIs in the UnityEngine.XRModule instead")]
    public static class XRDevice
    {
        ///<summary>Refresh rate of the display in Hertz.</summary>
        ///<remarks>This property may return zero if the current XR SDK does not report refresh rate information.</remarks>
        [Obsolete("XRDevice.refreshRate is deprecated. " +
            "Use XRDisplaySubsystem.activeSubsystemOrStub.displayRefreshRate instead. For a more robust alternative, use XRDisplaySubsystem.TryGetDisplayRefreshRate and check the return value.")]
        public static float refreshRate
        {
            get => XRDisplaySubsystem.activeSubsystemOrStub.displayRefreshRate;
        }

        ///<summary>Recreates the XR platform's eye texture swap chain with the appropriate anti-aliasing sample count.  The reallocation of the eye texture will only occur if the MSAA quality setting's sample count is different from the sample count of the current eye texture.  Reallocations of the eye textures will happen at the beginning of the next frame.  This is an expensive operation and should only be used when necessary.</summary>
        ///<returns>Nothing.</returns>
        [NativeName("UpdateEyeTextureMSAASetting")]
        [StaticAccessor("GetIVRDeviceScripting()", StaticAccessorType.ArrowWithDefaultReturnIfNull)]
        [Obsolete("XRDevice.UpdateEyeTextureMSAASetting is deprecated. Update your code to use XRDisplaySubsystem.SetMSAALevel() instead, passing the desired MSAA level.")]
        extern public static void UpdateEyeTextureMSAASetting();

        ///<summary>Zooms the XR projection.</summary>
        ///<remarks>Set this to zoom the XR projection matrix by scaling the viewing frustum. The value is clamped so that it will never fall below 1.0f.
        ///For asymmetric XR projections, setting the FoV doesn't make sense, so use this property to scale the frustum half angles uniformly by a single value.
        ///
        ///For example: A symmetric frustum starting with an FoV of 90 degrees, an fovZoomFactor of 2 will scale the viewing frustum so that it has an FoV of 45 degrees.</remarks>
        [Obsolete("XRDevice.fovZoomFactor is deprecated. " + 
            "Use XRDisplaySubsystem.activeSubsystemOrStub.fovZoomFactor instead.")]
        public static float fovZoomFactor
        {
            get => XRDisplaySubsystem.activeSubsystemOrStub.fovZoomFactor;
            set
            {
                var display = XRDisplaySubsystem.activeSubsystem;
                if (display != null)
                    display.fovZoomFactor = value;
            }
        }

        ///<summary>This method returns an IntPtr representing the native pointer to the XR device if one is available, otherwise the value will be IntPtr.Zero.</summary>
        ///<remarks>This native pointer can be used along with other plugins or extensions to access additional details or functionality related to the XR device.</remarks>
        ///<returns>The native pointer to the XR device.</returns>
        [Obsolete("XRDevice.GetNativePtr is deprecated, and should no longer be used. This API was only supported for legacy VR.", true)]
        public static IntPtr GetNativePtr()
        {
            throw new NotSupportedException("XRDevice.GetNativePtr is deprecated and no longer supported.");
        }

        ///<summary>Returns the device's current TrackingSpaceType. This value determines how the camera is positioned relative to its starting position. For more, see the section "Understanding the camera" in <see href="xref:VROverview" />.</summary>
        ///<returns>The device's current TrackingSpaceType.</returns>
        [Obsolete("XRDevice.GetTrackingSpaceType is deprecated, and should no longer be used. Please use XRInputSubsystem.GetTrackingOriginMode.", true)]
        public static TrackingSpaceType GetTrackingSpaceType()
        {
            throw new NotSupportedException("XRDevice.GetTrackingSpaceType is deprecated. Please use XRInputSubsystem.GetTrackingOriginMode.");
        }

        ///<summary>Sets the device's current TrackingSpaceType. Returns true on success. Returns false if the given TrackingSpaceType is not supported or the device fails to switch.</summary>
        ///<returns>True on success. False if the given TrackingSpaceType is not supported or the device fails to switch.</returns>
        [Obsolete("XRDevice.SetTrackingSpaceType is deprecated, and should no longer be used. Please use XRInputSubsystem.TrySetTrackingOriginMode.", true)]
        public static bool SetTrackingSpaceType(TrackingSpaceType trackingSpaceType)
        {
            throw new NotSupportedException("XRDevice.SetTrackingSpaceType is deprecated. Please use XRInputSubsystem.TrySetTrackingOriginMode.");
        }

        [Obsolete("XRDevice.DisableAutoXRCameraTracking is deprecated, and should no longer be used. This API was only supported for legacy VR.", true)]
        public static void DisableAutoXRCameraTracking(Camera camera, bool disabled)
        {
            throw new NotSupportedException("XRDevice.DisableAutoXRCameraTracking is deprecated and no longer supported.");
        }

        ///<summary>Subscribe a delegate to this event to get notified when an XRDevice is successfully loaded.</summary>
        ///<remarks>In order to change the currently loaded device or reload the current device, use <see cref="XRSettings.LoadDeviceByName" />.</remarks>
        [Obsolete("XRDevice.deviceLoaded is deprecated, and should no longer be used. This API was only supported for legacy VR.", true)]
        public static event Action<string> deviceLoaded
        {
            add { throw new NotSupportedException("XRDevice.deviceLoaded is deprecated and no longer supported."); }
            remove { throw new NotSupportedException("XRDevice.deviceLoaded is deprecated and no longer supported."); }
        }

    }

    ///<summary>Timing and other statistics from the XR subsystem.</summary>
    ///<remarks>Some XR SDKs provide access to additional timing and other statistics. These can be used by games and applications for profiling and dynamic performance adjustments. For example, modifying <see cref="XRSettings.eyeTextureResolutionScale" /> or <see cref="XRSettings.renderViewportScale" /> during runtime can improve performance. This class exposes a set of information that can be optionally reported by SDKs. Make sure to use the return values of any methods to know whether the data is being reported by the SDK or not.</remarks>
    [NativeConditional("ENABLE_VR")]
    [Obsolete("UnityEngine.VRModule is deprecated and will be removed in a future version. Please use the APIs in the UnityEngine.XRModule instead")]
    public static class XRStats
    {
        ///<summary>Retrieves the time spent by the GPU last frame, in seconds, as reported by the XR SDK.</summary>
        ///<remarks>On SDKs that support it, this method allows access to more accurate timing information from the SDK itself. This information can take into account GPU time spent in SDK-specific layers.
        ///
        ///Statistics are not always available and can vary based on hardware, SDK, and even frame to frame. As such it is important to check the return value of this method before using the statistic value from the out parameter.</remarks>
        ///<param name="gpuTimeLastFrame">Outputs the time spent by the GPU last frame.</param>
        ///<returns>True if the GPU time spent last frame is available, false otherwise.</returns>
        [Obsolete(
            "XRStats.TryGetGPUTimeLastFrame is deprecated. " +
            "Use XRDisplaySubsystem.activeSubsystemOrStub.TryGetAppGPUTimeLastFrame instead.")]
        public static bool TryGetGPUTimeLastFrame(out float gpuTimeLastFrame)
        {
            return XRDisplaySubsystem.activeSubsystemOrStub.TryGetAppGPUTimeLastFrame(out gpuTimeLastFrame);
        }


        ///<summary>Retrieves the number of dropped frames reported by the XR SDK.</summary>
        ///<remarks>The number of dropped frames can be useful to games or applications that wish to dynamically scale content or settings in order to maximize frame rate. It is important for XR applications to run at a consistent, high frame rate. If an application is drawing too much or making too many calculations, it may be unable to maintain a high frame rate and "drop" frames. When the SDK reports that frames are being dropped, the game or application can adjust settings, disable objects, or perform other actions to reduce overhead.
        ///
        ///Statistics are not always available and can vary based on hardware, SDK, and even frame to frame. As such it is important to check the return value of this method before using the statistic value from the out parameter.</remarks>
        ///<param name="droppedFrameCount">Outputs the number of frames dropped since the last update.</param>
        ///<returns>True if the dropped frame count is available, false otherwise.</returns>
        [Obsolete(
            "XRStats.TryGetDroppedFrameCount is deprecated. " +
            "Use XRDisplaySubsystem.activeSubsystemOrStub.TryGetDroppedFrameCount instead.")]
        public static bool TryGetDroppedFrameCount(out int droppedFrameCount)
        {
            return XRDisplaySubsystem.activeSubsystemOrStub.TryGetDroppedFrameCount(out droppedFrameCount);
        }

        ///<summary>Retrieves the number of times the current frame has been drawn to the device as reported by the XR SDK.</summary>
        ///<remarks>If performance degrades, some SDKs may choose to draw the current frame multiple times with or without some kind of adaptation to compensate. The frame present count can tell if the SDK has presented the same frame to the viewer multiple times.
        ///
        ///Statistics are not always available and can vary based on hardware, SDK, and even frame to frame. As such it is important to check the return value of this method before using the statistic value from the out parameter.</remarks>
        ///<param name="framePresentCount">Outputs the number of times the current frame has been presented.</param>
        ///<returns>True if the frame present count is available, false otherwise.</returns>
        [Obsolete(
            "XRStats.TryGetFramePresentCount is deprecated. " +
            "Use XRDisplaySubsystem.activeSubsystemOrStub.TryGetFramePresentCount instead.")]
        public static bool TryGetFramePresentCount(out int framePresentCount)
        {
            return XRDisplaySubsystem.activeSubsystemOrStub.TryGetFramePresentCount(out framePresentCount);
        }
    }

}
