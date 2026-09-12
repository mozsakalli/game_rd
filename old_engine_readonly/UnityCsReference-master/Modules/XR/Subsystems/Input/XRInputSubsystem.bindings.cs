// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UnityEngine.XR
{
    ///<summary>This enum provides context to where the 0,0,0 point of tracking for <see cref="InputDevice" />s is.</summary>
    ///<remarks>Each <see cref="XRInputSubsystem" /> has a single origin for all reported <see cref="InputDevice" />s. The origin can be relative to either real-world objects, such as a physical tracking device, or virtual objects, such as the center of a user-drawn bounding region.</remarks>
    public enum TrackingOriginModeFlags
    {
        ///<summary>TrackingOriginModeFlags.Unknown enumerates when the <see cref="XRInputSubsystem" /> was not able to set its tracking origin or has no tracking.</summary>
        Unknown = 0,
        ///<summary>
        ///  <see cref="XRInputSubsystem" /> tracks all <see cref="InputDevice" />s in reference to the first known location of a specific <see cref="InputDevice" /> when set to TrackingOriginModeFlags.Device.</summary>
        ///<remarks>For mobile AR applications, the <see cref="InputDevice" /> is the mobile device the application is running on. For head-mounted device applications, the head-mounted device acts as the root.</remarks>
        Device = 1,
        ///<summary>
        ///  <see cref="XRInputSubsystem" /> tracks all <see cref="InputDevice" />s in reference to a point on the floor when set to TrackingOriginModeFlags.Floor.</summary>
        Floor = 2,
        ///<summary>
        ///  <see cref="XRInputSubsystem" /> tracks all <see cref="InputDevice" />s in reference to an <see cref="InputDevice" /> with the <see cref="InputDeviceCharacteristics.TrackingReference" /> flag set when set to TrackingOriginModeFlags.TrackingReference.</summary>
        TrackingReference = 4,
        ///<summary>
        ///  <see cref="XRInputSubsystem" /> tracks all <see cref="InputDevice" />s in relation to a world anchor. This world anchor can change at any time, and is chosen by the runtime.</summary>
        ///<remarks>Use <see cref="XRInputSubsystem.trackingOriginUpdated" /> to be notified when the anchor has been updated.</remarks>
        Unbounded = 8
    }

    ///<summary>XRInputSubsystem
    ///Instance is used to enable and disable the inputs coming from a specific plugin.</summary>
    ///<remarks>Starting up an XRInputSubsystem
    ///Instance will cause that plugin to start feeding input device data to the following <see cref="XR.InputTracking" /> systems: <see cref="XR.InputTracking.GetLocalPosition" /> and <see cref="XR.InputTracking.GetLocalRotation" />. Calling the Stop or Shutdown functions will disable polling any input device data for that plugin.</remarks>
    [NativeHeader("Modules/XR/Subsystems/Input/XRInputSubsystem.h")]
    [UsedByNativeCode]
    [NativeConditional("ENABLE_XR")]
    public class XRInputSubsystem : IntegratedSubsystem<XRInputSubsystemDescriptor>
    {
        internal extern UInt32 GetIndex();

        ///<summary>Centers the tracking features on all <see cref="InputDevice" />s to the current position and orientation of the head-mounted device.</summary>
        ///<remarks>This can fail depending on the <see cref="TrackingOriginModeFlags" /> of the subsystem does not support recentering.</remarks>
        ///<returns>True if the method recenters the XRInputSubsystem. Returns false otherwise.</returns>
        public extern bool TryRecenter();

        ///<summary>Gets a list of all connected <see cref="InputDevice" />s reported by this <see cref="XRInputSubsystem" />.</summary>
        ///<param name="devices">The list of devices reported by this subsystem.</param>
        ///<returns>True, if the <see cref="XRInputSubsystem" /> retrieves any devices.  Returns false otherwise.</returns>
        public bool TryGetInputDevices(List<InputDevice> devices)
        {
            if (devices == null)
                throw new ArgumentNullException("devices");

            devices.Clear();

            if (m_DeviceIdsCache == null)
                m_DeviceIdsCache = new List<UInt64>();

            m_DeviceIdsCache.Clear();

            TryGetDeviceIds_AsList(m_DeviceIdsCache);
            for (int i = 0; i < m_DeviceIdsCache.Count; i++)
            {
                devices.Add(new InputDevice(m_DeviceIdsCache[i]));
            }
            return true;
        }

        ///<summary>Attempts to set the <see cref="TrackingOriginModeFlags" /> of the subsystem.</summary>
        ///<remarks>See <see cref="XRInputSubsystem.GetSupportedTrackingOriginModes" /> in order to see what modes this individual XRInputSubsystem supports, and <see cref="XRInputSubsystem.GetTrackingOriginMode" /> to see the current mode.</remarks>
        ///<param name="origin">The new <see cref="TrackingOriginModeFlags" /> that you'd like to change to.</param>
        ///<returns>True if the method changes the origin. Returns false otherwise.</returns>
        public extern bool TrySetTrackingOriginMode(TrackingOriginModeFlags origin);
        ///<summary>Gets the Tracking Origin Mode.</summary>
        ///<remarks>See <see cref="TrackingOriginModeFlags" /> for more details on different modes.</remarks>
        ///<returns>The Tracking Origin Mode that this subsystem is in.</returns>
        public extern TrackingOriginModeFlags GetTrackingOriginMode();
        ///<summary>Gets all <see cref="TrackingOriginModeFlags" /> that this subsystem supports.</summary>
        ///<returns>A single series of flags that contains all supported <see cref="TrackingOriginModeFlags" />.</returns>
        public extern TrackingOriginModeFlags GetSupportedTrackingOriginModes();

        ///<summary>Gets the list of 3D position values that represents the SDK-set boundary.</summary>
        ///<param name="boundaryPoints">The list of boundary points.</param>
        ///<returns>True if this <see cref="XRInputSubsystem" /> supports boundary points and they are available.  Returns false otherwise.</returns>
        public bool TryGetBoundaryPoints(List<Vector3> boundaryPoints)
        {
            if (boundaryPoints == null)
                throw new ArgumentNullException("boundaryPoints");

            return TryGetBoundaryPoints_AsList(boundaryPoints);
        }

        private extern bool TryGetBoundaryPoints_AsList(List<Vector3> boundaryPoints);

        ///<summary>An event that takes the delegate instance that the <see cref="XRInputSubsystem" /> calls when it changes the origin it reports devices at.</summary>
        ///<remarks>This can be due to a change in the <see cref="TrackingOriginModeFlags" /> or from the <see cref="XRInputSubsystem" /> moving the location of the origin without changing the type. See <c>XRInputSubsystem.GetTrackingOrigin</c> in order to see the new <see cref="TrackingOriginModeFlags" />.</remarks>
        public event Action<XRInputSubsystem> trackingOriginUpdated;

        ///<summary>An event that takes the delegate instance that the <see cref="XRInputSubsystem" /> calls when it changes its tracking boundary.</summary>
        ///<remarks>See <see cref="XRInputSubsystem.TryGetBoundaryPoints"/> to get the new boundary.</remarks>
        public event Action<XRInputSubsystem> boundaryChanged;

        [RequiredByNativeCode(GenerateProxy = true)]
        private static void InvokeTrackingOriginUpdatedEvent(IntPtr internalPtr)
        {
            IntegratedSubsystem subsystem = SubsystemManager.GetIntegratedSubsystemByPtr(internalPtr);
            XRInputSubsystem inputSubsystem = subsystem as XRInputSubsystem;
            if ((inputSubsystem != null) && (inputSubsystem.trackingOriginUpdated != null))
                inputSubsystem.trackingOriginUpdated(inputSubsystem);
        }

        [RequiredByNativeCode(GenerateProxy = true)]
        private static void InvokeBoundaryChangedEvent(IntPtr internalPtr)
        {
            IntegratedSubsystem subsystem = SubsystemManager.GetIntegratedSubsystemByPtr(internalPtr);
            XRInputSubsystem inputSubsystem = subsystem as XRInputSubsystem;
            if ((inputSubsystem != null) && (inputSubsystem.boundaryChanged != null))
                inputSubsystem.boundaryChanged(inputSubsystem);
        }

        internal extern void TryGetDeviceIds_AsList(List<UInt64> deviceIds);

        private List<UInt64> m_DeviceIdsCache;

        new internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(XRInputSubsystem xrInputSubsystem) => xrInputSubsystem.m_Ptr;
        }
    }
}
