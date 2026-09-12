// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.XR
{
    // Matches UnityVRTrackedNodeType in IUnityVR.h
    ///<summary>Enumeration of XR nodes which can be updated by XR input or sent haptic data.</summary>
    ///<remarks>**Note:** The types GameController, TrackingReference, and HardwareTracker are considered non-singleton nodes, as there can be many of each available.  As a result, <see cref="InputTracking.GetLocalPosition" />, and <see cref="InputTracking.GetLocalRotation" /> will not work with those values.  Please use <see cref="InputTracking.GetNodeStates" /> instead.
    ///            **Note:** Only XR nodes with valid haptic devices as endpoints can be sent haptic data.</remarks>
    public enum XRNode
    {
        ///<summary>Node representing the left eye.</summary>
        LeftEye,
        ///<summary>Node representing the right eye.</summary>
        RightEye,
        ///<summary>Node representing a point between the left and right eyes.</summary>
        CenterEye,
        ///<summary>Node representing the user's head.</summary>
        Head,
        ///<summary>Node representing the left hand.</summary>
        ///<remarks>XR SDKs are responsible for defining which tracked devices represents hands and as such the hands may not always be matched to the user's actual hands, for example if the user passed controllers between their hands after the SDK made the node assignments.</remarks>
        LeftHand,
        ///<summary>Node representing the right hand.</summary>
        ///<remarks>XR SDKs are responsible for defining which tracked devices represents hands and as such the hands may not always be matched to the user's actual hands, for example if the user passed controllers between their hands after the SDK made the node assignments.</remarks>
        RightHand,
        ///<summary>Represents a tracked game Controller not associated with a specific hand.</summary>
        GameController,
        ///<summary>Represents a stationary physical device that can be used as a point of reference in the tracked area.</summary>
        TrackingReference,
        ///<summary>Represents a physical device that provides tracking data for objects to which it is attached.</summary>
        HardwareTracker
    }
}
