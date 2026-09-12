// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Experimental;

namespace UnityEngine.XR.Provider
{
    ///<summary>Provides timing and other statistics from XR subsystems.</summary>
    ///<remarks>The XRStats class allows XR SDK providers to expose timing and other statistics related to their XR subsystems. Such statistics can be used by XR application developers for profiling and making dynamic performance adjustments. For example, an application could dynamically adjust properties like <see cref="XRSettings.eyeTextureResolutionScale" /> or <see cref="XRSettings.renderViewportScale" /> at run time to improve performance based on statistics provided by an XR subsystem.
    ///
    ///<c>Note:</c> XR SDK providers can use this class to provide their own, device-specific class for reporting statistics. XR application developers should not need to use the XRStats class directly.</remarks>
    public static class XRStats
    {
        ///<summary>Retrieve a statistic for an XR subsystem.</summary>
        ///<remarks>The TryGetStat method queries an XR subsystem for the specified statistic and, if available, sets the output <c>value</c> parameter to the current statistic value. TryGetStat returns true to indicate that the output parameter contains a valid statistic value. If the specified tag is not defined for the subsystem or the subsystem itself is not ready, TryGetStat returns false.</remarks>
        ///<param name="xrSubsystem">The subsystem with which the stat is registered.</param>
        ///<param name="tag">The tag used to query for a statistic.</param>
        ///<param name="value">Receives the current value of the requested statistic. Contains a valid value when this method returns true.</param>
        ///<returns>True, if the requested statistic is available, false otherwise.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine.XR.Provider;
        ///using System.Collections.Generic;
        ///using UnityEngine.XR;
        ///using UnityEngine;
        ///using XRStats = UnityEngine.XR.Provider.XRStats;
        ///
        ///public static class OpenVRStats
        ///{
        ///    public static float GPUFrameTime()
        ///    {
        ///        float tmp;
        ///        XRStats.TryGetStat(GetFirstDisplaySubsystem(), "OpenVR.Display.GPUFrameTime", out tmp);
        ///        return tmp;
        ///    }
        ///
        ///    public static float MotionToPhoton()
        ///    {
        ///        float tmp;
        ///        XRStats.TryGetStat(GetFirstDisplaySubsystem(), "MotionToPhoton", out tmp);
        ///        return tmp;
        ///    }
        ///
        ///    // etc...
        ///    private static IntegratedSubsystem GetFirstDisplaySubsystem()
        ///    {
        ///        List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        ///        SubsystemManager.GetInstances(displays);
        ///        if (displays.Count == 0)
        ///        {
        ///            Debug.Log("No display subsystem found.");
        ///            return null;
        ///        }
        ///        return displays[0];
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static bool TryGetStat(IntegratedSubsystem xrSubsystem, string tag, out float value)
        {
            return TryGetStat_Internal(xrSubsystem.m_Ptr, tag, out value);
        }

        [NativeHeader("Modules/XR/Stats/XRStats.h")]
        [NativeConditional("ENABLE_XR")]
        [StaticAccessor("XRStats::Get()", StaticAccessorType.Dot)]
        [NativeMethod("TryGetStatByName_Internal")]
        private static extern bool TryGetStat_Internal(IntPtr ptr, string tag, out float value);
    }
}
