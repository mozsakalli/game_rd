// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Scripting.LifecycleManagement;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine.Analytics
{
    ///<exclude />
    [RequiredByNativeCode]
    public enum AnalyticsSessionState
    {
        ///<exclude />
        kSessionStopped = 0,
        ///<exclude />
        kSessionStarted = 1,
        ///<exclude />
        kSessionPaused = 2,
        ///<exclude />
        kSessionResumed = 3
    }

    ///<exclude />
    [RequiredByNativeCode]
    [NativeHeader("UnityAnalyticsScriptingClasses.h")]
    [NativeHeader("Modules/UnityAnalytics/Public/UnityAnalytics.h")]
    public static partial class AnalyticsSessionInfo
    {
        ///<exclude />
        public delegate void SessionStateChanged(AnalyticsSessionState sessionState, long sessionId, long sessionElapsedTime, bool sessionChanged);
        ///<exclude />
        [AutoStaticsCleanupOnCodeReload] // holds user-registered session-state change handlers
        public static event SessionStateChanged sessionStateChanged;

        [RequiredByNativeCode]
        internal static void CallSessionStateChanged(AnalyticsSessionState sessionState, long sessionId, long sessionElapsedTime, bool sessionChanged)
        {
            var handler = sessionStateChanged;
            if (handler != null)
                handler(sessionState, sessionId, sessionElapsedTime, sessionChanged);
        }

        ///<exclude />
        public extern static AnalyticsSessionState sessionState
        {
            [NativeMethod("GetPlayerSessionState")]
            get;
        }

        ///<exclude />
        public extern static long sessionId
        {
            [NativeMethod("GetPlayerSessionId")]
            get;
        }

        ///<exclude />
        public extern static long sessionCount
        {
            [NativeMethod("GetPlayerSessionCount")]
            get;
        }


        ///<exclude />
        public extern static long sessionElapsedTime
        {
            [NativeMethod("GetPlayerSessionElapsedTime")]
            get;
        }

        ///<exclude />
        public extern static bool sessionFirstRun
        {
            [NativeMethod("GetPlayerSessionFirstRun", false, true)]
            get;
        }

        ///<exclude />
        public extern static string userId
        {
            [NativeMethod("GetUserId")]
            get;
        }

        ///<exclude />
        public static string customUserId
        {
            get
            {
                if (!Analytics.IsInitialized())
                    return null;
                return customUserIdInternal;
            }
            set
            {
                if (Analytics.IsInitialized())
                    customUserIdInternal = value;
            }
        }

        ///<exclude />
        public static string customDeviceId
        {
            get
            {
                if (!Analytics.IsInitialized())
                    return null;
                return customDeviceIdInternal;
            }
            set
            {
                if (Analytics.IsInitialized())
                    customDeviceIdInternal = value;
            }
        }

        ///<exclude />
        public delegate void IdentityTokenChanged(string token);
        ///<exclude />
        [AutoStaticsCleanupOnCodeReload] // holds user-registered identity-token change handlers
        public static event IdentityTokenChanged identityTokenChanged;

        [RequiredByNativeCode]
        internal static void CallIdentityTokenChanged(string token)
        {
            var handler = identityTokenChanged;
            if (handler != null)
                handler(token);
        }

        ///<exclude />
        public static string identityToken
        {
            get
            {
                if (!Analytics.IsInitialized())
                    return null;
                return identityTokenInternal;
            }
        }

        [StaticAccessor("GetUnityAnalytics()", StaticAccessorType.Dot)]
        private extern static string identityTokenInternal
        {
            [NativeMethod("GetIdentityToken")]
            get;
        }

        [StaticAccessor("GetUnityAnalytics()", StaticAccessorType.Dot)]
        private extern static string customUserIdInternal
        {
            [NativeMethod("GetCustomUserId")]
            get;
            [NativeMethod("SetCustomUserId")]
            set;
        }

        [StaticAccessor("GetUnityAnalytics()", StaticAccessorType.Dot)]
        private extern static string customDeviceIdInternal
        {
            [NativeMethod("GetCustomDeviceId")]
            get;
            [NativeMethod("SetCustomDeviceId")]
            set;
        }
    }
}
