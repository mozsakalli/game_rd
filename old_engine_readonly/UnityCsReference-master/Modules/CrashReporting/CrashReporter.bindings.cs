// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.CompilerServices;
using UnityEngine.Bindings;

[assembly: InternalsVisibleTo("Unity.Services.CloudDiagnostics")]
[assembly: InternalsVisibleTo("Unity.Services.CloudDiagnostics.Tests")]
namespace UnityEngine.CrashReportHandler
{
    ///<summary>Engine API for CrashReporting Service.</summary>
    [NativeHeader("Modules/CrashReporting/Public/CrashReporter.h")]
    [StaticAccessor("CrashReporting::CrashReporter::Get()", StaticAccessorType.Dot)]
    public partial class CrashReportHandler
    {
        private CrashReportHandler()
        {
        }

        ///<summary>This Boolean field will cause CrashReportHandler to capture exceptions when set to true. By default enable capture exceptions is true.</summary>
        [NativeProperty("EnableCloudDiagnosticsReporting")]
        public static extern bool enableCaptureExceptions { get; set; }

        ///<summary>The Diagnostics service will keep a buffer of up to the last X log messages (Debug.Log, etc) to send along with crash reports.  The default is 10 log messages, the max is 50. Set this to 0 to disable capture of logs with your crash reports.</summary>
        [NativeMethod(ThrowsException = true)]
        public static extern UInt32 logBufferSize { get; set; }

        [NativeMethod(ThrowsException = true)]
        internal static extern string installationIdentifier { get; set; }

        ///<summary>Get a custom crash report metadata field that has been set.</summary>
        ///<returns>Value that was previously set for the key, or null if no value was found.</returns>
        [NativeMethod(ThrowsException = true)]
        public static extern string GetUserMetadata(string key);

        ///<summary>Set a custom metadata key-value pair to be included with crash reports.</summary>
        ///<remarks>Set a value to null to clear a key.
        ///
        ///Keys are limited to 255 characters and values to 1024 characters, and there is a limit of 64 key-value pairs. SetUserMetadata throws a System.ArgumentException if you attempt to exceed these limits.</remarks>
        [NativeMethod(ThrowsException = true)]
        public static extern void SetUserMetadata(string key, string value);
    }
}
