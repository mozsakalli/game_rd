// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine.Android
{
    ///<summary>Indicates the relative importance level that the system assigns to the process. These levels are represented by constants. The constants are numbered in such a way that more important values are always smaller than the less important values.</summary>
    public enum ProcessImportance
    {
        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_FOREGROUND</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_FOREGROUND">IMPORTANCE_FOREGROUND</see>.</remarks>
        Foreground = 100,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_FOREGROUND_SERVICE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_FOREGROUND_SERVICE">IMPORTANCE_FOREGROUND_SERVICE</see>.</remarks>
        ForeGroundService = 125,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_VISIBLE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_VISIBLE">IMPORTANCE_VISIBLE</see>.</remarks>
        Visible = 200,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_PERCEPTIBLE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_PERCEPTIBLE">IMPORTANCE_PERCEPTIBLE</see>.</remarks>
        Perceptible = 230,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_TOP_SLEEPING</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_TOP_SLEEPING">IMPORTANCE_TOP_SLEEPING</see>.</remarks>
        TopSleeping = 325,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_CANT_SAVE_STATE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_CANT_SAVE_STATE">IMPORTANCE_CANT_SAVE_STATE</see>.</remarks>
        CantSaveState = 350,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_SERVICE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_SERVICE">IMPORTANCE_SERVICE</see>.</remarks>
        Service = 300,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_CACHED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_CACHED">IMPORTANCE_CACHED</see>.</remarks>
        Cached = 400,

        ///<summary>Mirrors <c>android:public static final int IMPORTANCE_GONE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ActivityManager.RunningAppProcessInfo#IMPORTANCE_GONE">IMPORTANCE_GONE</see>.</remarks>
        Gone = 1000
    }

    ///<summary>The reason code for termination of the process.</summary>
    public enum ExitReason
    {
        ///<summary>Mirrors <c>android:public static final int REASON_UNKNOWN</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_UNKNOWN">REASON_UNKNOWN</see>.</remarks>
        Unknown = 0,

        ///<summary>Mirrors <c>android:public static final int REASON_EXIT_SELF</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_EXIT_SELF">REASON_EXIT_SELF</see>.</remarks>
        ExitSelf = 1,

        ///<summary>Mirrors <c>android:public static final int REASON_SIGNALED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_SIGNALED">REASON_SIGNALED</see>.</remarks>
        Signaled = 2,

        ///<summary>Mirrors <c>android:public static final int REASON_LOW_MEMORY</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_LOW_MEMORY">REASON_LOW_MEMORY</see>.</remarks>
        LowMemory = 3,

        ///<summary>Mirrors <c>android:public static final int REASON_CRASH</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_CRASH">REASON_CRASH</see>.</remarks>
        Crash = 4,

        ///<summary>Mirrors <c>android:public static final int REASON_CRASH_NATIVE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_CRASH_NATIVE">REASON_CRASH_NATIVE</see>.</remarks>
        CrashNative = 5,

        ///<summary>Mirrors <c>android:public static final int REASON_ANR</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_ANR">REASON_ANR</see>.</remarks>
        ANR = 6,

        ///<summary>Mirrors <c>android:public static final int REASON_INITIALIZATION_FAILURE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_INITIALIZATION_FAILURE">REASON_INITIALIZATION_FAILURE</see>.</remarks>
        InititalizationFailure = 7,

        ///<summary>Mirrors <c>android:public static final int REASON_PERMISSION_CHANGE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_PERMISSION_CHANGE">REASON_PERMISSION_CHANGE</see>.</remarks>
        PermissionChange = 8,

        ///<summary>Mirrors <c>android:public static final int REASON_EXCESSIVE_RESOURCE_USAGE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_EXCESSIVE_RESOURCE_USAGE">REASON_EXCESSIVE_RESOURCE_USAGE</see>.</remarks>
        ExcessiveResourceUsage = 9,

        ///<summary>Mirrors <c>android:public static final int REASON_USER_REQUESTED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_USER_REQUESTED">REASON_USER_REQUESTED</see>.</remarks>
        UserRequested = 10,

        ///<summary>Mirrors <c>android:public static final int REASON_USER_STOPPED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_USER_STOPPED">REASON_USER_STOPPED</see>.</remarks>
        UserStopped = 11,

        ///<summary>Mirrors <c>android:public static final int REASON_DEPENDENCY_DIED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_DEPENDENCY_DIED">REASON_DEPENDENCY_DIED</see>.</remarks>
        DependencyDied = 12,

        ///<summary>Mirrors <c>android:public static final int REASON_OTHER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_OTHER">REASON_OTHER</see>.</remarks>
        Other = 13,

        ///<summary>Mirrors <c>android:public static final int REASON_FREEZER</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_FREEZER">REASON_FREEZER</see>.</remarks>
        Freezer = 14,

        ///<summary>Mirrors <c>android:public static final int REASON_PACKAGE_STATE_CHANGE</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_PACKAGE_STATE_CHANGE">REASON_PACKAGE_STATE_CHANGE</see>.</remarks>
        PackageStateChange = 15,

        ///<summary>Mirrors <c>android:public static final int REASON_PACKAGE_UPDATED</c>.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#REASON_PACKAGE_UPDATED">REASON_PACKAGE_UPDATED</see>.</remarks>
        PackageUpdated = 16
    }

    ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo</c>.</summary>
    ///<remarks>For more information, refer to Android's documentation on &lt;a href="https://developer.android.com/reference/android/app/ApplicationExitInfo"&gt;ApplicationExitInfo&lt;/a&gt;.</remarks>
    public interface IApplicationExitInfo
    {
        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getDescription()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getDescription()">getDescription()</see>.</remarks>
        string description { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo describeContents()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#describeContents()">describeContents()</see>.</remarks>
        int describeContents { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getDefiningUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getDefiningUid()">getDefiningUid()</see>.</remarks>
        int definingUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getImportance()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getImportance()">getImportance()</see>.</remarks>
        ///<seealso cref="ProcessImportance" />
        ProcessImportance importance { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getPackageUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getPackageUid()">getPackageUid()</see>.</remarks>
        int packageUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getPid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getPid()">getPid()</see>.</remarks>
        int pid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getProcessName()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getProcessName()">getProcessName()</see>.</remarks>
        string processName { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getProcessStateSummary()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getProcessStateSummary()">getProcessStateSummary()</see>.</remarks>
        sbyte[] processStateSummary { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getPss()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getPss()">getPss()</see>.</remarks>
        long pss { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getRealUid()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getRealUid()">getRealUid()</see>.</remarks>
        int realUid { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getReason()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getReason()">getReason()</see>.</remarks>
        ///<seealso cref="ExitReason" />
        ExitReason reason { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getRss()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getRss()">getRss()</see>.</remarks>
        long rss { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getStatus()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getStatus()">getStatus()</see>.</remarks>
        int status { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo getTimestamp()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getTimestamp()">getTimestamp()</see>.</remarks>
        long timestamp { get; }

        ///<summary>Mirrors <c>android:android.app.ApplicationExitInfo TraceInputStream()</c> method.</summary>
        ///<remarks>For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/android/app/ApplicationExitInfo#getTraceInputStream()">getTraceInputStream()</see>.</remarks>
        byte[] trace { get; }

        ///<summary>Returns the trace content in UTF8 string format.</summary>
        public string traceAsString { get; }
    }


    ///<summary>Access point to get the list of <see cref="IApplicationExitInfo">ApplicationExitInfo</see> records with the reasons for the most recent application terminations.</summary>
    public static class ApplicationExitInfoProvider
    {
        ///<summary>Gets records of application terminations including the reasons for the most recent terminations.</summary>
        ///<remarks>Mirrors <c>android:android.app.ApplicationExitInfo gethistoricalprocessexitreasons()</c> method. For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/kotlin/android/app/ActivityManager#gethistoricalprocessexitreasons">getHistoricalProcessExitReasons method</see>.</remarks>
        ///<param name="packageName">Optional. A null value matches all packages that belong to the caller's UID. To retrieve records for a package that belongs to another UID, you must hold the <c>android.Manifest.permission.DUMP</c> permission.</param>
        ///<param name="pid">A process ID that used to belong to this package but has since died. A value of 0 ignores this parameter and returns all matching records.</param>
        ///<param name="maxNum">The maximum number of records to return. A value of 0 ignores this parameter and returns all matching records.</param>
        ///<returns>An array of <see cref="IApplicationExitInfo">ApplicationExitInfo</see> records matching the criteria, sorted from most recent to least recent. This value is never null.</returns>
        public static IApplicationExitInfo[] GetHistoricalProcessExitInfo(string packageName = null, int pid = 0, int maxNum = 0)
        {
            IApplicationExitInfo[] result = null;
            if (result == null)
                result = Array.Empty<IApplicationExitInfo>();

            return result;
        }

        ///<summary>Sets custom state data for the process.</summary>
        ///<remarks>Mirrors <c>android:android.app.ApplicationExitInfo setprocessstatesummary()</c> method. For more information, refer to Android's documentation on <see href="https://developer.android.com/reference/kotlin/android/app/ActivityManager#setprocessstatesummary">setProcessStateSummary method</see>.</remarks>
        ///<param name="buffer">The state data. Do not include sensitive information or data (PII, SPII, or other sensitive user data) here. The maximum length is 128 bytes.</param>
        public static void SetProcessStateSummary(SByte[] buffer)
        {
        }
    }
}
