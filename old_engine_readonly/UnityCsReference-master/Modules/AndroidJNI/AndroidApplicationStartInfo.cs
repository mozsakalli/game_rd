// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;

namespace UnityEngine.Android
{
    ///<summary>Options for the launch mode of an Android activity.</summary>
    ///<remarks>Use this enum with the <see cref="IApplicationStartInfo.launchMode" /> property. For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo"&gt;ApplicationStartInfo&lt;/a&gt;.</remarks>
    public enum LaunchMode
    {
        ///<summary>Mirrors <c>android:public static final int LAUNCH_MODE_STANDARD</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#LAUNCH_MODE_STANDARD"&gt;LAUNCH_MODE_STANDARD&lt;/a&gt;.</remarks>
        Standard = 0,

        ///<summary>Mirrors <c>android:public static final int LAUNCH_MODE_SINGLE_TOP</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#LAUNCH_MODE_SINGLE_TOP"&gt;LAUNCH_MODE_SINGLE_TOP&lt;/a&gt;.</remarks>
        SingleTop = 1,

        ///<summary>Mirrors <c>android:public static final int LAUNCH_MODE_SINGLE_INSTANCE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#LAUNCH_MODE_SINGLE_INSTANCE"&gt;LAUNCH_MODE_SINGLE_INSTANCE&lt;/a&gt;.</remarks>
        SingleInstance = 2,

        ///<summary>Mirrors <c>android:public static final int LAUNCH_MODE_SINGLE_TASK</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#LAUNCH_MODE_SINGLE_TASK"&gt;LAUNCH_MODE_SINGLE_TASK&lt;/a&gt;.</remarks>
        SingleTask = 3,

        ///<summary>Mirrors <c>android:public static final int LAUNCH_MODE_SINGLE_INSTANCE_PER_TASK</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#LAUNCH_MODE_SINGLE_INSTANCE_PER_TASK"&gt;LAUNCH_MODE_SINGLE_INSTANCE_PER_TASK&lt;/a&gt;.</remarks>
        SingleInstancePerTask = 4
    }

    ///<summary>Reason codes for an Android app process start.</summary>
    ///<remarks>Use this enum with the <see cref="IApplicationStartInfo.reason" /> property.</remarks>
    public enum StartReason
    {
        ///<summary>Mirrors <c>android:public static final int START_REASON_ALARM</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_ALARM"&gt;START_REASON_ALARM&lt;/a&gt;.</remarks>
        Alarm = 0,

        ///<summary>Mirrors <c>android:public static final int START_REASON_BACKUP</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_BACKUP"&gt;START_REASON_BACKUP&lt;/a&gt;.</remarks>
        Backup = 1,

        ///<summary>Mirrors <c>android:public static final int START_REASON_BOOT_COMPLETE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_BOOT_COMPLETE"&gt;START_REASON_BOOT_COMPLETE&lt;/a&gt;.</remarks>
        BootComplete = 2,

        ///<summary>Mirrors <c>android:public static final int START_REASON_BROADCAST</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_BROADCAST"&gt;START_REASON_BROADCAST&lt;/a&gt;.</remarks>
        Broadcast = 3,

        ///<summary>Mirrors <c>android:public static final int START_REASON_CONTENT_PROVIDER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_CONTENT_PROVIDER"&gt;START_REASON_CONTENT_PROVIDER&lt;/a&gt;.</remarks>
        ContentProvider = 4,

        ///<summary>Mirrors <c>android:public static final int START_REASON_JOB</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_JOB"&gt;START_REASON_JOB&lt;/a&gt;.</remarks>
        Job = 5,

        ///<summary>Mirrors <c>android:public static final int START_REASON_LAUNCHER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_LAUNCHER"&gt;START_REASON_LAUNCHER&lt;/a&gt;.</remarks>
        Launcher = 6,

        ///<summary>Mirrors <c>android:public static final int START_REASON_LAUNCHER_RECENTS</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_LAUNCHER_RECENTS"&gt;START_REASON_LAUNCHER_RECENTS&lt;/a&gt;.</remarks>
        LauncherRecents = 7,

        ///<summary>Mirrors <c>android:public static final int START_REASON_OTHER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_OTHER"&gt;START_REASON_OTHER&lt;/a&gt;.</remarks>
        Other = 8,

        ///<summary>Mirrors <c>android:public static final int START_REASON_PUSH</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_PUSH"&gt;START_REASON_PUSH&lt;/a&gt;.</remarks>
        Push = 9,

        ///<summary>Mirrors <c>android:public static final int START_REASON_SERVICE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_SERVICE"&gt;START_REASON_SERVICE&lt;/a&gt;.</remarks>
        Service = 10,

        ///<summary>Mirrors <c>android:public static final int START_REASON_START_ACTIVITY</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_REASON_START_ACTIVITY"&gt;START_REASON_START_ACTIVITY&lt;/a&gt;.</remarks>
        StartActivity = 11
    }

    ///<summary>Options for the start type of an Android app.</summary>
    ///<remarks>Use this enum with the <see cref="IApplicationStartInfo.startType" /> property.</remarks>
    public enum StartType
    {
        ///<summary>Mirrors <c>android:public static final int START_TYPE_UNSET</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TYPE_UNSET"&gt;START_TYPE_UNSET&lt;/a&gt;.</remarks>
        Unset = 0,

        ///<summary>Mirrors <c>android:public static final int START_TYPE_COLD</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TYPE_COLD"&gt;START_TYPE_COLD&lt;/a&gt;.</remarks>
        Cold = 1,

        ///<summary>Mirrors <c>android:public static final int START_TYPE_WARM</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TYPE_WARM"&gt;START_TYPE_WARM&lt;/a&gt;.</remarks>
        Warm = 2,

        ///<summary>Mirrors <c>android:public static final int START_TYPE_HOT</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TYPE_HOT"&gt;START_TYPE_HOT&lt;/a&gt;.</remarks>
        Hot = 3
    }

    ///<summary>Options for the startup state of an Android app process.</summary>
    ///<remarks>Use this enum with the <see cref="IApplicationStartInfo.startupState" /> property.</remarks>
    public enum StartupState
    {
        ///<summary>Mirrors <c>android:public static final int STARTUP_STATE_STARTED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#STARTUP_STATE_STARTED"&gt;STARTUP_STATE_STARTED&lt;/a&gt;.</remarks>
        Started = 0,

        ///<summary>Mirrors <c>android:public static final int STARTUP_STATE_ERROR</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#STARTUP_STATE_ERROR"&gt;STARTUP_STATE_ERROR&lt;/a&gt;.</remarks>
        Error = 1,

        ///<summary>Mirrors <c>android:public static final int STARTUP_STATE_FIRST_FRAME_DRAWN</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#STARTUP_STATE_FIRST_FRAME_DRAWN"&gt;STARTUP_STATE_FIRST_FRAME_DRAWN&lt;/a&gt;.</remarks>
        FirstFrameDrawn = 2
    }

    ///<summary>Options for the component type that started the app.</summary>
    ///<remarks>Use this enum with the <see cref="IApplicationStartInfo.startComponent" /> property. Available on API level 36+.</remarks>
    public enum StartComponent
    {
        ///<summary>Mirrors <c>android:public static final int START_COMPONENT_ACTIVITY</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_COMPONENT_ACTIVITY"&gt;START_COMPONENT_ACTIVITY&lt;/a&gt;.</remarks>
        Activity = 1,

        ///<summary>Mirrors <c>android:public static final int START_COMPONENT_BROADCAST</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_COMPONENT_BROADCAST"&gt;START_COMPONENT_BROADCAST&lt;/a&gt;.</remarks>
        Broadcast = 2,

        ///<summary>Mirrors <c>android:public static final int START_COMPONENT_CONTENT_PROVIDER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_COMPONENT_CONTENT_PROVIDER"&gt;START_COMPONENT_CONTENT_PROVIDER&lt;/a&gt;.</remarks>
        ContentProvider = 3,

        ///<summary>Mirrors <c>android:public static final int START_COMPONENT_SERVICE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_COMPONENT_SERVICE"&gt;START_COMPONENT_SERVICE&lt;/a&gt;.</remarks>
        Service = 4,

        ///<summary>Mirrors <c>android:public static final int START_COMPONENT_OTHER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_COMPONENT_OTHER"&gt;START_COMPONENT_OTHER&lt;/a&gt;.</remarks>
        Other = 5
    }

    ///<summary>Timestamp types for <c>ApplicationStartInfo</c> startup data.</summary>
    ///<remarks>Use these values as keys in the <see cref="IApplicationStartInfo.startupTimestamps" /> dictionary.</remarks>
    public enum StartTimestamp
    {
        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_LAUNCH</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_LAUNCH"&gt;START_TIMESTAMP_LAUNCH&lt;/a&gt;.</remarks>
        Launch = 0,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_FORK</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_FORK"&gt;START_TIMESTAMP_FORK&lt;/a&gt;.</remarks>
        Fork = 1,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_APPLICATION_ONCREATE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_APPLICATION_ONCREATE"&gt;START_TIMESTAMP_APPLICATION_ONCREATE&lt;/a&gt;.</remarks>
        ApplicationOnCreate = 2,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_BIND_APPLICATION</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_BIND_APPLICATION"&gt;START_TIMESTAMP_BIND_APPLICATION&lt;/a&gt;.</remarks>
        BindApplication = 3,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_FIRST_FRAME</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_FIRST_FRAME"&gt;START_TIMESTAMP_FIRST_FRAME&lt;/a&gt;.</remarks>
        FirstFrame = 4,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_FULLY_DRAWN</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_FULLY_DRAWN"&gt;START_TIMESTAMP_FULLY_DRAWN&lt;/a&gt;.</remarks>
        FullyDrawn = 5,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_INITIAL_RENDERTHREAD_FRAME</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_INITIAL_RENDERTHREAD_FRAME"&gt;START_TIMESTAMP_INITIAL_RENDERTHREAD_FRAME&lt;/a&gt;.</remarks>
        InitialRenderthreadFrame = 6,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_SURFACEFLINGER_COMPOSITION_COMPLETE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_SURFACEFLINGER_COMPOSITION_COMPLETE"&gt;START_TIMESTAMP_SURFACEFLINGER_COMPOSITION_COMPLETE&lt;/a&gt;.</remarks>
        SurfaceflingerCompositionComplete = 7,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_RESERVED_RANGE_SYSTEM</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_RESERVED_RANGE_SYSTEM"&gt;START_TIMESTAMP_RESERVED_RANGE_SYSTEM&lt;/a&gt;.</remarks>
        ReservedRangeSystem = 20,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_RESERVED_RANGE_DEVELOPER_START</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_RESERVED_RANGE_DEVELOPER_START"&gt;START_TIMESTAMP_RESERVED_RANGE_DEVELOPER_START&lt;/a&gt;.</remarks>
        ReservedRangeDeveloperStart = 21,

        ///<summary>Mirrors <c>android:public static final int START_TIMESTAMP_RESERVED_RANGE_DEVELOPER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#START_TIMESTAMP_RESERVED_RANGE_DEVELOPER"&gt;START_TIMESTAMP_RESERVED_RANGE_DEVELOPER&lt;/a&gt;.</remarks>
        ReservedRangeDeveloper = 30
    }

    ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo</c>.</summary>
    ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo"&gt;ApplicationStartInfo&lt;/a&gt;. Available on Android API 35+.
    ///
    ///Use <see cref="ApplicationStartInfoProvider.GetHistoricalProcessStartReasons" /> to obtain instances of this interface. Each instance describes one historical process start, including why the process was started, how it was started (cold, warm, or hot), and timing milestones recorded during startup.</remarks>
    public interface IApplicationStartInfo
    {
        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getPid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getPid()"&gt;getPid()&lt;/a&gt;.</remarks>
        int pid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getDefiningUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getDefiningUid()"&gt;getDefiningUid()&lt;/a&gt;.</remarks>
        int definingUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getPackageUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getPackageUid()"&gt;getPackageUid()&lt;/a&gt;.</remarks>
        int packageUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getRealUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getRealUid()"&gt;getRealUid()&lt;/a&gt;.</remarks>
        int realUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getProcessName()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getProcessName()"&gt;getProcessName()&lt;/a&gt;.</remarks>
        string processName { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getReason()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getReason()"&gt;getReason()&lt;/a&gt;.</remarks>
        ///<seealso cref="StartReason" />
        StartReason reason { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getStartType()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getStartType()"&gt;getStartType()&lt;/a&gt;.</remarks>
        ///<seealso cref="StartType" />
        StartType startType { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getStartupState()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getStartupState()"&gt;getStartupState()&lt;/a&gt;.</remarks>
        ///<seealso cref="StartupState" />
        StartupState startupState { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getLaunchMode()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getLaunchMode()"&gt;getLaunchMode()&lt;/a&gt;.</remarks>
        ///<seealso cref="LaunchMode" />
        LaunchMode launchMode { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getIntent()</c> method, returning the URI string representation of the launch intent via <c>Intent.toUri(0)</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getIntent()"&gt;getIntent()&lt;/a&gt;.
        ///Returns null if there was no intent associated with this start.
        ///
        ///This property returns the intent as a URI string rather than an <c>AndroidJavaObject</c> to avoid keeping a native JNI handle alive beyond the lifetime of the <see cref="ApplicationStartInfoProvider.GetHistoricalProcessStartReasons" /> call. To reconstruct the original Android Intent from this URI, use &lt;a href="https://developer.android.com/reference/android/content/Intent#parseUri(java.lang.String,%20int)"&gt;Intent.parseUri&lt;/a&gt;.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class ApplicationStartInfoExample : MonoBehaviour
        ///{
        ///    void PrintLaunchAction(IApplicationStartInfo info)
        ///    {
        ///        if (info.intentUri == null)
        ///            return;
        ///#if UNITY_ANDROID
        ///        using var intentClass = new AndroidJavaClass("android.content.Intent");
        ///        using var intent = intentClass.CallStatic<AndroidJavaObject>("parseUri", info.intentUri, 0);
        ///        string action = intent.Call<string>("getAction");
        ///        Debug.Log($"Launch action: {action}");
        ///#endif
        ///    }
        ///}
        ///]]></code>
        ///</example>
        string intentUri { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getStartupTimestamps()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getStartupTimestamps()"&gt;getStartupTimestamps()&lt;/a&gt;</remarks>
        ///<seealso cref="StartTimestamp" />
        IReadOnlyDictionary<StartTimestamp, long> startupTimestamps { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo wasForceStopped()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#wasForceStopped()"&gt;wasForceStopped()&lt;/a&gt;.</remarks>
        bool wasForceStopped { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationStartInfo getStartComponent()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo#getStartComponent()"&gt;getStartComponent()&lt;/a&gt;. Available on API 36+. Returns 0 on earlier versions.</remarks>
        ///<seealso cref="StartComponent" />
        StartComponent startComponent { get; }
    }


    ///<summary>Provides access to Android's <c>ApplicationStartInfo</c> API.</summary>
    ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationStartInfo"&gt;ApplicationStartInfo&lt;/a&gt;. Available on Android API 35+.
    ///
    ///Use <see cref="ApplicationStartInfoProvider.GetHistoricalProcessStartReasons" /> to retrieve <see cref="IApplicationStartInfo" /> records that describe why and how the application was started in recent launches.</remarks>
    public static class ApplicationStartInfoProvider
    {
        ///<summary>Returns a list of <c>ApplicationStartInfo</c> records containing the reasons for the most recent app starts.</summary>
        ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ActivityManager#getHistoricalProcessStartReasons(int)"&gt;getHistoricalProcessStartReasons&lt;/a&gt;.
        ///Available on Android API 35+. Returns an empty array on earlier versions.</remarks>
        ///<param name="maxNum">The maximum number of results to be returned. Set this to 0 to ignore this parameter and return all matching records.</param>
        ///<returns>An array of <see cref="IApplicationStartInfo" /> records matching the criteria, sorted in the order from most recent to least recent. Never null.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class AppStartDiagnostics : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        IApplicationStartInfo[] records = ApplicationStartInfoProvider.GetHistoricalProcessStartReasons(1);
        ///        if (records.Length == 0)
        ///        {
        ///            Debug.Log("No start info available (requires Android API 35+).");
        ///            return;
        ///        }
        ///
        ///        IApplicationStartInfo info = records[0];
        ///
        ///        // Log core start properties.
        ///        Debug.Log($"Process: {info.processName} (pid {info.pid})");
        ///        Debug.Log($"Reason: {info.reason}, type: {info.startType}, state: {info.startupState}");
        ///        Debug.Log($"Force-stopped before launch: {info.wasForceStopped}");
        ///
        ///        // Log the launch mode when the start was triggered by an activity launch.
        ///        if (info.reason == StartReason.Launcher || info.reason == StartReason.StartActivity)
        ///            Debug.Log($"Launch mode: {info.launchMode}");
        ///
        ///        // Calculate time to first frame; timestamps are clock-monotonic values in nanoseconds.
        ///        if (info.startupTimestamps.TryGetValue(StartTimestamp.Launch, out long launchNs) &&
        ///            info.startupTimestamps.TryGetValue(StartTimestamp.FirstFrame, out long firstFrameNs))
        ///        {
        ///            Debug.Log($"Time to first frame: {(firstFrameNs - launchNs) / 1_000_000} ms");
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static IApplicationStartInfo[] GetHistoricalProcessStartReasons(int maxNum = 0)
        {
            IApplicationStartInfo[] result = null;
            if (result == null)
                result = Array.Empty<IApplicationStartInfo>();

            return result;
        }
    }
}
