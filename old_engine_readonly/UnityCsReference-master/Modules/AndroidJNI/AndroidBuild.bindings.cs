// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Bindings;

namespace UnityEngine.Android
{
    ///<summary>Provides access to Android build and platform information.</summary>
    public static partial class AndroidBuild
    {
        ///<summary>Provides access to Android platform version information, such as the device's current API level, and the application's minimum and target API levels.</summary>
        public static partial class Version
        {
            [NoAutoStaticsCleanup] // Android API level is a device constant; survives code reload
            private static int? m_ApiLevel;
            [NoAutoStaticsCleanup] // same — minimum API level is fixed at device/build time
            private static int? m_MinApiLevel;
            [NoAutoStaticsCleanup] // same — target API level is fixed at build time
            private static int? m_TargetApiLevel;

            ///<summary>The device's current Android API level.</summary>
            ///<remarks>For more information, refer to the Android documentation on &lt;a href="https://developer.android.com/reference/android/os/Build.VERSION#SDK_INT"&gt;Build.VERSION.SDK_INT&lt;/a&gt;.</remarks>
            ///<example>
            ///  <code><![CDATA[using UnityEngine;
            ///using UnityEngine.Android;
            ///
            ///public class AndroidVersionCheckExample : MonoBehaviour
            ///{
            ///    void Start()
            ///    {
            ///#if UNITY_ANDROID && !UNITY_EDITOR
            ///        if (AndroidBuild.Version.apiLevel >= 33)
            ///            Debug.Log("Running on Android 13 (API level 33) or higher.");
            ///        else
            ///            Debug.Log($"Running on an older Android version (API level {AndroidBuild.Version.apiLevel}).");
            ///#else
            ///        Debug.Log("Not running on an Android device.");
            ///#endif
            ///    }
            ///}]]></code>
            ///</example>
            public static int apiLevel => m_ApiLevel ??= GetApiLevel();
            ///<summary>The minimum Android API level that the application can run on.</summary>
            ///<remarks>For more information, refer to the Android documentation on &lt;a href="https://developer.android.com/ndk/guides/sdk-versions#minsdkversion"&gt;minSdkVersion&lt;/a&gt;.</remarks>
            public static int minApiLevel => m_MinApiLevel ??= GetMinApiLevel();
            ///<summary>The target Android API level that the application is designed to run on.</summary>
            ///<remarks>For more information, refer to the Android documentation on &lt;a href="https://developer.android.com/ndk/guides/sdk-versions#targetsdkversion"&gt;targetSdkVersion&lt;/a&gt;.</remarks>
            public static int targetApiLevel => m_TargetApiLevel ??= GetTargetApiLevel();

            private static int GetApiLevel() => 0;
            private static int GetMinApiLevel() => 0;
            private static int GetTargetApiLevel() => 0;
        }
    }
}
