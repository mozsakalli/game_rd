// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEngine.Bindings;

namespace UnityEngine.Android
{
    // Directly matches values returned by PlayCore API
    // https://developer.android.com/reference/com/google/android/play/core/assetpacks/model/AssetPackStatus
    ///<summary>Values that indicate the status of an Android asset pack.</summary>
    ///<remarks>Unity always returns the status value and the error together in AndroidAssetPackInfo or AndroidAssetPackState objects. Unity returns these objects via callback methods after you call either AndroidAssetPacks.DownloadAssetPackAsync or AndroidAssetPacks.GetAssetPackStateAsync.
    ///When the status value is AndroidAssetPackStatus.Failed or AndroidAssetPackStatus.Unknown, the error value indicates the cause of the failure. For any other status value, the error value should always be AndroidAssetPackError.NoError.
    ///This enum directly wraps the &lt;a href="https://developer.android.com/reference/com/google/android/play/core/assetpacks/model/AssetPackStatus"&gt;AssetPackStatus&lt;/a&gt; values in the PlayCore API.</remarks>
    ///<seealso cref="AndroidAssetPackInfo" />
    ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
    ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
    ///<seealso cref="AndroidAssetPackState" />
    public enum AndroidAssetPackStatus
    {
        ///<summary>Indicates that the Android asset pack is not available for the application.</summary>
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Unknown = 0,
        ///<summary>Indicates that the Android asset pack status should soon change.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Pending = 1,
        ///<summary>Indicates that the device is downloading the Android asset pack.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Downloading = 2,
        ///<summary>Indicates that the device has downloaded the Android asset pack and is unpacking the asset pack to its final location.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Transferring = 3,
        ///<summary>Indicates that the device has downloaded the Android asset pack and the asset pack is available to the application.</summary>
        ///<remarks>You can call AndroidAssetPacks.GetAssetPackPath to get the full path to this asset pack.</remarks>
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Completed = 4,
        ///<summary>Indicates that the device failed to download the Android asset pack.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Failed = 5,
        ///<summary>Indicates that the Android asset pack download is canceled.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        Canceled = 6,
        ///<summary>Indicates that the device has paused the Android asset pack download until it connects to the WiFi network.</summary>
        ///<remarks>You can call AndroidAssetPacks.RequestToUseMobileDataAsync to ask for the permission to continue download using the mobile data. If such permission is given by the user, the download should resume automatically.</remarks>
        ///<seealso cref="AndroidAssetPacks.RequestToUseMobileDataAsync" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        WaitingForWifi = 7,
        ///<summary>Indicates that the Android asset pack is not installed.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        NotInstalled = 8,
        ///<summary>Indicates that the Android asset pack requires user consent to be downloaded.</summary>
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        RequiresUserConfirmation = 9,
    }

    // Directly matches values returned by PlayCore API
    // https://developer.android.com/reference/com/google/android/play/core/assetpacks/model/AssetPackErrorCode
    ///<summary>Values that indicate the type of Android asset pack error when the status is either AndroidAssetPackStatus.Failed or AndroidAssetPackStatus.Unknown.</summary>
    ///<remarks>Unity always returns the error value and the status together in AndroidAssetPackInfo or AndroidAssetPackState objects. Unity returns these objects via callback methods after you call either AndroidAssetPacks.DownloadAssetPackAsync or AndroidAssetPacks.GetAssetPackStateAsync. When the status value is AndroidAssetPackStatus.Failed or AndroidAssetPackStatus.Unknown, the error value indicates the cause of the failure. For any other status value, the error value should always be AndroidAssetPackError.NoError.
    ///This enum directly wraps the &lt;a href="https://developer.android.com/reference/com/google/android/play/core/assetpacks/model/AssetPackErrorCode"&gt;AssetPackErrorCode&lt;/a&gt; values in the PlayCore API.</remarks>
    ///<seealso cref="AndroidAssetPackInfo" />
    ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
    ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
    ///<seealso cref="AndroidAssetPackState" />
    public enum AndroidAssetPackError
    {
        ///<summary>Indicates that there is no error.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        NoError = 0,
        ///<summary>Indicates that this application is unavailable in the Google's Play Store.</summary>
        ///<remarks>The possible causes of this are:
        ///
        ///* The application is not published to the Google Play Store.
        ///* The version code of the application does not exist in the Google Play Store. For example, if only an older version of the application is in the Google Play Store.
        ///* The user doesn't own the application. For example, if they did not install it from the Google Play Store.
        ///* The user doesn't have access to the application in the Google Play Store. For example, if the application is on a track the user does not have access to.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        AppUnavailable = -1,
        ///<summary>Indicates that the requested Android asset pack is not available in the Google Play Store.</summary>
        ///<remarks>One possible cause of this is that the Android App Bundle file you uploaded to the store does not include the specified Android asset pack.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        PackUnavailable = -2,
        ///<summary>Indicates that the request was invalid.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        InvalidRequest = -3,
        ///<summary>Indicates that the requested download is not found.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        DownloadNotFound = -4,
        ///<summary>Indicates that the Asset Delivery API is not available.</summary>
        ///<remarks>One possible cause of this is that the PlayCore version you added to the project is too old.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        ApiNotAvailable = -5,
        ///<summary>Indicates that the Android asset pack is not accessible because there was an error related to the network connection.</summary>
        ///<remarks>One possible cause of this is that the PlayCore plugin is unable to obtain the Android asset pack details from the Google Play Store.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        NetworkError = -6,
        ///<summary>Indicates that the application does not have permission to download asset packs under the current device circumstances.</summary>
        ///<remarks>One possible cause of this is that the application is running in the background.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        AccessDenied = -7,
        ///<summary>Indicates that there is not enough storage space on the device to download the Android asset pack.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        InsufficientStorage = -10,
        ///<summary>Indicates that the device does not have the Play Store application installed or has an unofficial version.</summary>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        PlayStoreNotFound = -11,
        ///<summary>Indicates that the app requested to use mobile data while there were no Android asset packs waiting for WiFi.</summary>
        ///<seealso cref="AndroidAssetPacks.RequestToUseMobileDataAsync" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        NetworkUnrestricted = -12,
        ///<summary>Indicates that the end user does not own the application on the device.</summary>
        ///<remarks>The device considers the end user to own the application only if they acquired the application from the Google Play Store.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        AppNotOwned = -13,
        ///<summary>Indicates that unknown error occured while downloading an asset pack.</summary>
        ///<remarks>Some possible causes of this are:
        ///
        ///* You are trying to use PlayCore API to access install-time delivered Android asset pack.
        ///* You are testing a locally built application (not installed from the Google Play Store) and the Android asset pack with the specified name does not exist.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        InternalError = -100,
    }

    ///<summary>Represents the download progress of a single Android asset pack.</summary>
    ///<remarks>The download progress is received as a callback after AndroidAssetPacks.DownloadAssetPackAsync is called.</remarks>
    ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
    public class AndroidAssetPackInfo
    {
        internal AndroidAssetPackInfo(string name, AndroidAssetPackStatus status, ulong size, ulong bytesDownloaded, float transferProgress, AndroidAssetPackError error)
        {
            this.name = name;
            this.status = status;
            this.size = size;
            this.bytesDownloaded = bytesDownloaded;
            this.transferProgress = transferProgress;
            this.error = error;
        }

        ///<summary>The name of the Android asset pack that the device is downloading.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public string                 name { get; }
        ///<summary>The status of the Android asset pack that the device is downloading.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackStatus" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public AndroidAssetPackStatus status { get; }
        ///<summary>The total size of the Android asset pack in bytes.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public ulong                  size { get; }
        ///<summary>The downloaded size of the Android asset pack in bytes.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public ulong                  bytesDownloaded { get; }
        ///<summary>The transfering progress of the downloaded Android asset pack.</summary>
        ///<remarks>A floating point value. The range is 0 through 1.
        ///Read-only.
        ///Asset packs get compressed to reduce the download size. After they are downloaded, they have to be uncompressed.</remarks>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public float                  transferProgress { get; }
        ///<summary>Indicates an error which the device encountered when downloading the Android asset pack.</summary>
        ///<remarks>The default value is AndroidAssetPackError.NoError which indicates no errors.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackError" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public AndroidAssetPackError  error { get; }

        internal bool downloadInProgress => DownloadInProgress(status);
        internal static bool DownloadInProgress(AndroidAssetPackStatus status)
        {
            return status != AndroidAssetPackStatus.Canceled
                && status != AndroidAssetPackStatus.Completed
                && status != AndroidAssetPackStatus.Failed
                && status != AndroidAssetPackStatus.Unknown;
        }
    }

    ///<summary>Represents the state of a single Android asset pack.</summary>
    ///<remarks>The state is received as a callback after AndroidAssetPacks.GetAssetPackStateAsync is called.</remarks>
    ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
    public class AndroidAssetPackState
    {
        internal AndroidAssetPackState(string name, AndroidAssetPackStatus status, AndroidAssetPackError error)
        {
            this.name = name;
            this.status = status;
            this.error = error;
        }

        ///<summary>The name of the Android asset pack the status query is for.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        public string                 name { get; }
        ///<summary>The status of the Android asset pack.</summary>
        ///<remarks>Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackStatus" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        public AndroidAssetPackStatus status { get; }
        ///<summary>Indicates an error code that describes what happened when querying the Android asset pack state.</summary>
        ///<remarks>The default value is AndroidAssetPackError.NoError which indicates no errors.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackError" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        public AndroidAssetPackError  error { get; }
    }

    ///<summary>Represents the choice of an end user that indicates if your application can use mobile data to download Android asset packs.</summary>
    ///<remarks>The value is received from a callback after AndroidAssetPacks.RequestToUseMobileDataAsync is called.</remarks>
    ///<seealso cref="AndroidAssetPacks.RequestToUseMobileDataAsync" />
    public class AndroidAssetPackUseMobileDataRequestResult
    {
        internal AndroidAssetPackUseMobileDataRequestResult(bool allowed)
        {
            this.allowed = allowed;
        }

        ///<summary>Indicates if mobile data can be used to download Android asset packs.</summary>
        ///<seealso cref="AndroidAssetPacks.RequestToUseMobileDataAsync" />
        public bool allowed { get; }
    }

    ///<summary>Represents the user's response indicating whether the user gave consent to download asset packs that require explicit permission.</summary>
    ///<remarks>The value is received from a callback after <see cref="AndroidAssetPacks.ShowConfirmationDialogAsync" /> is called.</remarks>
    public class AndroidAssetPackConfirmationDialogResult
    {
        internal AndroidAssetPackConfirmationDialogResult(bool consentGiven)
        {
            this.consentGiven = consentGiven;
        }

        ///<summary>Indicates whether the user gave consent to download asset packs that require user confirmation or WiFi connection.</summary>
        ///<seealso cref="AndroidAssetPacks.ShowConfirmationDialogAsync" />
        ///<seealso cref="AndroidAssetPackStatus.RequiresUserConfirmation" />
        ///<seealso cref="AndroidAssetPackStatus.WaitingForWifi" />
        public bool consentGiven { get; }
    }

    ///<summary>Represents an asynchronous Android asset pack download operation. <see cref="AndroidAssetPacks.DownloadAssetPackAsync" /> returns an instance of this class.</summary>
    ///<remarks>You can yield until the operation completes, or manually check whether it's done using isDone or keepWaiting properties. You can also track the progress of the operation using the progress property.</remarks>
    ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
    public class DownloadAssetPackAsyncOperation : CustomYieldInstruction
    {
        Dictionary<string, AndroidAssetPackInfo> m_AssetPackInfos;

        ///<summary>Checks if the operation is still running.</summary>
        ///<remarks>Returns <c>true</c> if the operation is still running. Otherwise, returns <c>false</c>.
        ///Read-only.</remarks>
        ///<seealso cref="DownloadAssetPackAsyncOperation.isDone" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.progress" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.downloadedAssetPacks" />
        public override bool keepWaiting
        {
            get
            {
                lock (m_AssetPackInfos)
                {
                    foreach (var info in m_AssetPackInfos.Values)
                    {
                        // Continue waiting if we did not get even a single callback for some of the expected asset packs
                        // Google's PlayCore API does not call any callbacks for non-existing asset packs, but we should detect that in java and still report AndroidAssetPackStatus.Unknown
                        if (info == null)
                            return true;

                        if (info.downloadInProgress)
                        {
                            return true;
                        }
                    }

                    // Stop waiting when all asset packs were downloaded, canceled downloading or failed the download
                    return false;
                }
            }
        }

        ///<summary>Checks if the operation is finished.</summary>
        ///<remarks>Returns <c>true</c> if the operation is finished. Otherwise, returns <c>false</c>.
        ///Read-only.</remarks>
        ///<seealso cref="DownloadAssetPackAsyncOperation.keepWaiting" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.progress" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.downloadedAssetPacks" />
        public bool isDone => !keepWaiting;

        ///<summary>Gets the progress of the operation.</summary>
        ///<remarks>The range is 0 to 1.
        ///Read-only.</remarks>
        ///<seealso cref="DownloadAssetPackAsyncOperation.isDone" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.progress" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.downloadedAssetPacks" />
        public float progress
        {
            get
            {
                lock (m_AssetPackInfos)
                {
                    var downloadProgress = 0f;
                    var transferProgress = 0f;
                    foreach (var info in m_AssetPackInfos.Values)
                    {
                        if (info == null)
                            continue;
                        if (!info.downloadInProgress)
                        {
                            // We are counting the whole operation progress, so a failed subtask is "done" subtask in this case
                            downloadProgress += 1f;
                            transferProgress += 1f;
                        }
                        else
                        {
                            double result = (double)info.bytesDownloaded / (double)info.size;
                            downloadProgress += (float)result;
                            transferProgress += info.transferProgress;
                        }
                    }
                    // Use 0.8 weight for download and 0.2 weight for transfer (unpacking)
                    return Mathf.Clamp((downloadProgress * 0.8f + transferProgress * 0.2f) / m_AssetPackInfos.Count, 0f, 1f);
                }
            }
        }

        ///<summary>Gets the names of Android asset packs downloaded by this operation.</summary>
        ///<remarks>This property returns the names of asset packs that the device downloaded and made available to the app based on the download request that this async operation represents. To get the path to the directory that contains the assets, call <see cref="AndroidAssetPacks.GetAssetPackPath" />.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.isDone" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.progress" />
        public string[] downloadedAssetPacks
        {
            get
            {
                lock (m_AssetPackInfos)
                {
                    List<string> packNames = new List<string>();
                    foreach (var info in m_AssetPackInfos.Values)
                    {
                        if (info == null)
                            continue;
                        if (info.status == AndroidAssetPackStatus.Completed)
                        {
                            packNames.Add(info.name);
                        }
                    }
                    return packNames.ToArray();
                }
            }
        }

        ///<summary>Gets the names of Android asset packs that failed to download.</summary>
        ///<remarks>The Android asset pack names this property returns correspond to asset pack downloads that either failed due to some error or that the end user canceled. For more information, call <see cref="AndroidAssetPacks.DownloadAssetPackAsync" /> and pass a callback method to it. Unity raises the callback with additional information, such as the error code, during the download.
        ///Note: This list also includes the names of Android asset packs for which Google's PlayCore API did not raise any callbacks. This happens if you access this property right away after calling DownloadAssetPackAsync, before the PlayCore API raises any of the callbacks.
        ///Read-only.</remarks>
        ///<seealso cref="DownloadAssetPackAsyncOperation.isDone" />
        ///<seealso cref="DownloadAssetPackAsyncOperation.progress" />
        public string[] downloadFailedAssetPacks
        {
            get
            {
                lock (m_AssetPackInfos)
                {
                    List<string> packNames = new List<string>();
                    foreach (var keyPair in m_AssetPackInfos)
                    {
                        var info = keyPair.Value;
                        if (info == null)
                        {
                            packNames.Add(keyPair.Key);
                        }
                        else if (info.status == AndroidAssetPackStatus.Canceled
                                 || info.status == AndroidAssetPackStatus.Failed
                                 || info.status == AndroidAssetPackStatus.Unknown)
                        {
                            packNames.Add(info.name);
                        }
                    }
                    return packNames.ToArray();
                }
            }
        }

        internal DownloadAssetPackAsyncOperation(string[] assetPackNames)
        {
            m_AssetPackInfos = new Dictionary<string, AndroidAssetPackInfo>(assetPackNames.Length);
            foreach (var name in assetPackNames)
            {
                m_AssetPackInfos.Add(name, null);
            }
        }

        internal void OnUpdate(AndroidAssetPackInfo info)
        {
            lock (m_AssetPackInfos)
            {
                m_AssetPackInfos[info.name] = info;
            }
        }
    }

    ///<summary>Represents an asynchronous Android asset pack state request operation. <see cref="AndroidAssetPacks.GetAssetPackStateAsync" /> returns an instance of this class.</summary>
    ///<remarks>You can yield until the operation completes, or manually check whether it's done using isDone or keepWaiting properties.</remarks>
    ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
    public class GetAssetPackStateAsyncOperation : CustomYieldInstruction
    {
        ulong m_Size;
        AndroidAssetPackState[] m_States;
        readonly object m_OperationLock;

        ///<summary>Checks if the operation is still running.</summary>
        ///<remarks>Returns <c>true</c> if the operation is still running. Otherwise, returns <c>false</c>.
        ///Once the property returns false, <see cref="GetAssetPackStateAsyncOperation.size" /> and <see cref="GetAssetPackStateAsyncOperation.states" /> properties can be called to get results.
        ///Read-only.</remarks>
        ///<seealso cref="GetAssetPackStateAsyncOperation.isDone" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.size" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.states" />
        public override bool keepWaiting
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_States == null;
                }
            }
        }

        ///<summary>Checks if the operation is finished.</summary>
        ///<remarks>Returns <c>true</c> if the operation is finished. Otherwise, returns <c>false</c>.
        ///Once the property returns true, <see cref="GetAssetPackStateAsyncOperation.size" /> and <see cref="GetAssetPackStateAsyncOperation.states" /> properties can be called to get results.
        ///Read-only.</remarks>
        ///<seealso cref="GetAssetPackStateAsyncOperation.keepWaiting" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.size" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.states" />
        public bool isDone => !keepWaiting;

        ///<summary>Gets the total size in bytes of all Android asset packs that had their status checked by this operation.</summary>
        ///<remarks>Until the operation is complete and <see cref="GetAssetPackStateAsyncOperation.isDone" /> returns true, this returns 0.
        ///This value includes the size of both already downloaded and not yet installed Android asset packs.
        ///Read-only.</remarks>
        ///<seealso cref="GetAssetPackStateAsyncOperation.isDone" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.states" />
        public ulong size
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_Size;
                }
            }
        }

        ///<summary>Gets the states of all Android asset packs that had their status checked by this operation.</summary>
        ///<remarks>Until the operation is complete and <see cref="GetAssetPackStateAsyncOperation.isDone" /> returns true, this returns null.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackState" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.isDone" />
        ///<seealso cref="GetAssetPackStateAsyncOperation.size" />
        public AndroidAssetPackState[] states
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_States;
                }
            }
        }

        internal GetAssetPackStateAsyncOperation()
        {
            m_OperationLock = new object();
        }

        internal void OnResult(ulong size, AndroidAssetPackState[] states)
        {
            lock (m_OperationLock)
            {
                m_Size = size;
                m_States = states;
            }
        }
    }

    ///<summary>Represents an asynchronous operation that requests to use mobile data to download Android asset packs.</summary>
    ///<remarks>
    ///  <see cref="AndroidAssetPacks.RequestToUseMobileDataAsync" /> returns an instance of this class.
    ///You can yield until the operation completes, or manually check whether it's done using isDone or keepWaiting properties.</remarks>
    ///<seealso cref="AndroidAssetPacks.RequestToUseMobileDataAsync" />
    public class RequestToUseMobileDataAsyncOperation : CustomYieldInstruction
    {
        AndroidAssetPackUseMobileDataRequestResult m_RequestResult;
        readonly object m_OperationLock;

        ///<summary>Checks if the operation is still running.</summary>
        ///<remarks>Returns <c>true</c> if the operation is still running. Otherwise, returns <c>false</c>.
        ///After this property returns <c>false</c>, you can check <see cref="RequestToUseMobileDataAsyncOperation.result" /> to see if the end user allowed the application to use mobile data.
        ///Read-only.</remarks>
        ///<seealso cref="RequestToUseMobileDataAsyncOperation.isDone" />
        ///<seealso cref="RequestToUseMobileDataAsyncOperation.result" />
        public override bool keepWaiting
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_RequestResult == null;
                }
            }
        }

        ///<summary>Checks if the operation is finished.</summary>
        ///<remarks>Returns <c>true</c> if the operation is finished. Otherwise, returns <c>false</c>.
        ///After this property returns <c>true</c>, you can check <see cref="RequestToUseMobileDataAsyncOperation.result" /> to see if the end user allowed the application to use mobile data.
        ///Read-only.</remarks>
        ///<seealso cref="RequestToUseMobileDataAsyncOperation.keepWaiting" />
        ///<seealso cref="RequestToUseMobileDataAsyncOperation.result" />
        public bool isDone => !keepWaiting;

        ///<summary>Indicates whether the end user allowed the application to use mobile data to download Android asset packs.</summary>
        ///<remarks>Until the operation is complete and <see cref="RequestToUseMobileDataAsyncOperation.isDone" /> returns true, this returns null.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackUseMobileDataRequestResult" />
        ///<seealso cref="RequestToUseMobileDataAsyncOperation.isDone" />
        public AndroidAssetPackUseMobileDataRequestResult result
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_RequestResult;
                }
            }
        }

        internal RequestToUseMobileDataAsyncOperation()
        {
            m_OperationLock = new object();
        }

        internal void OnResult(AndroidAssetPackUseMobileDataRequestResult result)
        {
            lock (m_OperationLock)
            {
                m_RequestResult = result;
            }
        }
    }

    ///<summary>Represents an asynchronous operation that requests the user for consent to download Android asset packs.</summary>
    ///<remarks>
    ///  <see cref="AndroidAssetPacks.ShowConfirmationDialogAsync" /> returns an instance of this class.
    ///You can yield until the operation completes, or manually check whether it's completed using <c>isDone</c> or <c>keepWaiting</c> properties.</remarks>
    ///<seealso cref="AndroidAssetPacks.ShowConfirmationDialogAsync" />
    public class ConfirmationDialogAsyncOperation : CustomYieldInstruction
    {
        AndroidAssetPackConfirmationDialogResult m_ConfirmationDialogResult;
        readonly object m_OperationLock;

        ///<summary>Indicates whether the operation is still in progress.</summary>
        ///<remarks>Returns <c>true</c> if the operation is still running. Otherwise, returns <c>false</c>.
        ///After this property returns <c>false</c>, you can check <see cref="ConfirmationDialogAsyncOperation.result" /> to verify if the user consented to download asset packs.
        ///Read-only.</remarks>
        ///<seealso cref="ConfirmationDialogAsyncOperation.isDone" />
        ///<seealso cref="ConfirmationDialogAsyncOperation.result" />
        public override bool keepWaiting
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_ConfirmationDialogResult == null;
                }
            }
        }

        ///<summary>Indicates whether the operation has completed.</summary>
        ///<remarks>Returns <c>true</c> if the operation is finished. Otherwise, returns <c>false</c>.
        ///After this property returns <c>true</c>, you can check <see cref="ConfirmationDialogAsyncOperation.result" /> to verify if the user consented to download asset packs.
        ///Read-only.</remarks>
        ///<seealso cref="ConfirmationDialogAsyncOperation.keepWaiting" />
        ///<seealso cref="ConfirmationDialogAsyncOperation.result" />
        public bool isDone => !keepWaiting;

        ///<summary>Indicates whether the end user gave their consent to download Android asset packs.</summary>
        ///<remarks>This property returns null if the operation is not yet completed and <c>ConfirmationDialogAsyncOperation.isDone</c> is false.
        ///Read-only.</remarks>
        ///<seealso cref="AndroidAssetPackConfirmationDialogResult" />
        ///<seealso cref="ConfirmationDialogAsyncOperation.isDone" />
        public AndroidAssetPackConfirmationDialogResult result
        {
            get
            {
                lock (m_OperationLock)
                {
                    return m_ConfirmationDialogResult;
                }
            }
        }

        internal ConfirmationDialogAsyncOperation()
        {
            m_OperationLock = new object();
        }

        internal void OnResult(AndroidAssetPackConfirmationDialogResult result)
        {
            lock (m_OperationLock)
            {
                m_ConfirmationDialogResult = result;
            }
        }
    }

    ///<summary>Provides methods for handling Android asset packs.</summary>
    ///<remarks>Methods in this class are either direct wrappers of java APIs in Google's PlayCore plugin, or depend on values that the PlayCore API returns. Therefore to use it, the gradle project must include the "com.google.android.play:core" dependency. If your project contains custom asset packs or you enable **Split Application Binary** in Player Settings, Unity automatically adds this dependency to the unityLibrary submodule's build.gradle file. If the PlayCore plugin is missing, calling any wrapper throws an InvalidOperationException exception.
    ///Note that PlayCore APIs only work with fast-follow and on-demand delivery type asset packs, therefore methods in this class have the same limitation.</remarks>
    ///<example>
    ///  <code><![CDATA[using System.Collections;
    ///using System.Collections.Generic;
    ///using UnityEngine;
    ///using UnityEngine.Android;
    ///
    /// // Demonstrates a complete asset pack workflow.
    ///public class AndroidAssetPacksExample : MonoBehaviour
    ///{
    ///    bool isShowingDialog = false;
    ///
    ///    IEnumerator Start()
    ///    {
    ///        // Step 1: Ensure all core Unity asset packs are available.
    ///        if (!AndroidAssetPacks.coreUnityAssetPacksDownloaded)
    ///        {
    ///            var corePackNames = AndroidAssetPacks.GetCoreUnityAssetPackNames();
    ///            if (corePackNames.Length > 0)
    ///            {
    ///                Debug.Log("Downloading core Unity asset packs...");
    ///                var coreDownload = AndroidAssetPacks.DownloadAssetPackAsync(corePackNames);
    ///                yield return coreDownload;
    ///            }
    ///        }
    ///
    ///        // Step 2: Query the state of custom asset packs.
    ///        var customPacks = new string[] { "Textures", "Audio" };
    ///        var stateOperation = AndroidAssetPacks.GetAssetPackStateAsync(customPacks);
    ///        yield return stateOperation;
    ///
    ///        if (stateOperation.states == null)
    ///        {
    ///            Debug.LogError("Failed to retrieve asset pack states.");
    ///            yield break;
    ///        }
    ///
    ///        // Step 3: Download any packs that are not yet installed.
    ///        var packsToDownload = new List<string>();
    ///        foreach (var state in stateOperation.states)
    ///        {
    ///            if (state.status == AndroidAssetPackStatus.NotInstalled)
    ///            {
    ///                packsToDownload.Add(state.name);
    ///            }
    ///        }
    ///
    ///        if (packsToDownload.Count == 0)
    ///        {
    ///            Debug.Log("All custom asset packs are already installed.");
    ///            yield break;
    ///        }
    ///
    ///        // Step 4: Download with progress monitoring.
    ///        AndroidAssetPacks.DownloadAssetPackAsync(
    ///            packsToDownload.ToArray(),
    ///            info =>
    ///            {
    ///                if (!isShowingDialog &&
    ///                    (info.status == AndroidAssetPackStatus.WaitingForWifi ||
    ///                     info.status == AndroidAssetPackStatus.RequiresUserConfirmation))
    ///                {
    ///                    isShowingDialog = true;
    ///                    AndroidAssetPacks.ShowConfirmationDialogAsync(result =>
    ///                    {
    ///                        isShowingDialog = false;
    ///                        Debug.Log(result.consentGiven
    ///                            ? "User gave consent. Downloads will resume."
    ///                            : "User denied consent.");
    ///                    });
    ///                }
    ///                else if (info.status == AndroidAssetPackStatus.Completed)
    ///                {
    ///                    var path = AndroidAssetPacks.GetAssetPackPath(info.name);
    ///                    Debug.Log($"{info.name} is ready at: {path}");
    ///                }
    ///                else if (info.status == AndroidAssetPackStatus.Failed)
    ///                {
    ///                    Debug.LogError($"{info.name} failed: {info.error}");
    ///                }
    ///            }
    ///        );
    ///    }
    ///}]]></code>
    ///</example>
    [NativeHeader("Modules/AndroidJNI/Public/AndroidAssetPacksBindingsHelpers.h")]
    [StaticAccessor("AndroidAssetPacksBindingsHelpers", StaticAccessorType.DoubleColon)]
    public static class AndroidAssetPacks
    {
        ///<summary>Checks if all core Unity asset packs are downloaded.</summary>
        ///<remarks>Read-only. Returns <c>true</c> if all core Unity asset packs are downloaded. Otherwise, returns <c>false</c> if any core Unity asset packs aren't downloaded or if the &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; is missing.
        ///
        ///Core Unity asset packs are asset packs that Unity creates automatically when it builds the Android app bundle. Unity only creates core asset packs if you enable **Split Application Binary** in Android Player settings or if you use &lt;a href="../Manual/android-distribution-google-play.html#texture-compression-targeting"&gt;Texture Compression Targeting&lt;/a&gt;.
        ///To safely access assets in the streaming assets path, resources directory, or to safely load any scenes other than the first, only access them after this property returns <c>true</c>.
        ///
        ///When the property returns <c>false</c>, check the download status of the core Unity asset packs. To do this, call <see cref="AndroidAssetPacks.GetCoreUnityAssetPackNames" /> to get the array of core Unity asset pack names, then call <see cref="AndroidAssetPacks.GetAssetPackStateAsync" /> and pass in the names array. If the state indicates that the device hasn't yet downloaded an asset pack, or the device is not downloading an asset pack, call <see cref="AndroidAssetPacks.DownloadAssetPackAsync" /> passing in the asset pack's name. If there are asset packs that have the <c>WaitingForWifi</c> status, ask the user to allow the application to download them using mobile data. To do this, call <see cref="AndroidAssetPacks.ShowConfirmationDialogAsync" />.
        ///
        ///If this property returns <c>false</c> and <c>GetCoreUnityAssetPackNames</c> returns an empty array, then the application doesn't have the PlayCore plugin. Unity automatically adds it as a dependency in unityLibrary's <c>build.gradle</c> file when it builds the Android application with asset packs.</remarks>
        public static bool coreUnityAssetPacksDownloaded { get { return CoreUnityAssetPacksDownloaded(); } }

        internal static string textureCompressionsPackName { get { return GetTextureCompressionsPackName(); } }
        internal static string dataPackName { get { return GetDataPackName(); } }
        internal static string streamingAssetsPackName { get { return GetStreamingAssetsPackName(); } }

        [NativeConditional("PLATFORM_ANDROID")]
        private static extern bool CoreUnityAssetPacksDownloaded();

        ///<summary>Gets the names of the core Unity asset packs built for this application that use the fast-follow or on-demand delivery type.</summary>
        ///<remarks>Core Unity asset packs are asset packs that Unity creates automatically when it builds the Android app bundle. Unity creates core asset packs only when you enable **Split Application Binary** in Android Player settings.
        ///
        ///This method uses PlayCore APIs, therefore it can only return names of asset packs that use the fast-follow or on-demand delivery types. If this method returns an empty array, to differentiate between the two potential causes, check the <see cref="AndroidAssetPacks.coreUnityAssetPacksDownloaded" /> property.
        ///
        ///You can pass the asset pack names that this method returns to other methods such as <see cref="AndroidAssetPacks.GetAssetPackStateAsync" /> or <see cref="AndroidAssetPacks.DownloadAssetPackAsync" /> to get status information or to start the download. However, calling <see cref="AndroidAssetPacks.RemoveAssetPack" /> with the names returned by this method has no effect.</remarks>
        ///<returns>An array of asset pack names for core Unity asset packs with the fast-follow or on-demand delivery type. An empty array if Unity didn't create any core asset packs with these delivery types, or if the &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; is missing.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using System.Collections.Generic;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class GetCoreUnityAssetPackNamesExample : MonoBehaviour
        ///{
        ///    IEnumerator Start()
        ///    {
        ///        if (AndroidAssetPacks.coreUnityAssetPacksDownloaded)
        ///        {
        ///            Debug.Log("All core Unity asset packs are already available.");
        ///            yield break;
        ///        }
        ///
        ///        var corePackNames = AndroidAssetPacks.GetCoreUnityAssetPackNames();
        ///
        ///        if (corePackNames.Length == 0)
        ///        {
        ///            Debug.LogError("No core asset packs found. The PlayCore plugin might be missing.");
        ///            yield break;
        ///        }
        ///
        ///        // Check the state of each core asset pack.
        ///        var stateOperation = AndroidAssetPacks.GetAssetPackStateAsync(corePackNames);
        ///        yield return stateOperation;
        ///
        ///        if (stateOperation.states == null)
        ///        {
        ///            Debug.LogError("Failed to retrieve core asset pack states.");
        ///            yield break;
        ///        }
        ///
        ///        // Filter to only packs that are not yet installed.
        ///        var packsToDownload = new List<string>();
        ///        foreach (var state in stateOperation.states)
        ///        {
        ///            if (state.status == AndroidAssetPackStatus.NotInstalled)
        ///            {
        ///                packsToDownload.Add(state.name);
        ///            }
        ///            else
        ///            {
        ///                Debug.Log($"Core pack {state.name}: {state.status}");
        ///            }
        ///        }
        ///
        ///        if (packsToDownload.Count > 0)
        ///        {
        ///            Debug.Log($"Downloading {packsToDownload.Count} core asset pack(s)...");
        ///            yield return AndroidAssetPacks.DownloadAssetPackAsync(packsToDownload.ToArray());
        ///        }
        ///
        ///        Debug.Log($"Core Unity asset packs downloaded: {AndroidAssetPacks.coreUnityAssetPacksDownloaded}");
        ///    }
        ///}]]></code>
        ///</example>
        public static string[] GetCoreUnityAssetPackNames() { return Array.Empty<string>(); }
        ///<summary>Queries the state of Android asset packs.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.</remarks>
        ///<param name="assetPackNames">The array of names of the Android asset packs to query the state of.</param>
        ///<param name="callback">The callback method to get the result. Unity raises this callback once when the query is complete and the callback receives the state of queried Android asset packs. The callback method must have two parameters:
        ///
        ///* A ulong type parameter which indicates the total size of the queried asset packs.
        ///* An array of AndroidAssetPackState which contains the state of each queried asset pack.</param>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class GetAssetPackStateAsyncCallbackExample : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        var assetPackNames = new string[] { "MyAssetPack" };
        ///        AndroidAssetPacks.GetAssetPackStateAsync(assetPackNames, OnStateQueryComplete);
        ///    }
        ///
        ///    void OnStateQueryComplete(ulong totalSize, AndroidAssetPackState[] states)
        ///    {
        ///        Debug.Log($"Total size: {totalSize} bytes");
        ///
        ///        if (states == null)
        ///        {
        ///            Debug.LogError("Failed to retrieve asset pack states.");
        ///            return;
        ///        }
        ///
        ///        foreach (var state in states)
        ///        {
        ///            Debug.Log($"{state.name}: status={state.status}, error={state.error}");
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPackState" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        public static void GetAssetPackStateAsync(string[] assetPackNames, Action<ulong, AndroidAssetPackState[]> callback) {}
        ///<summary>Queries the state of Android asset packs.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.</remarks>
        ///<param name="assetPackNames">The array of names of the Android asset packs to query the state of.</param>
        ///<returns>An object that represents the query operation. If you yield this object inside a coroutine, the coroutine pauses until the operation is complete.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using System.Collections.Generic;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class GetAssetPackStateAsyncExample : MonoBehaviour
        ///{
        ///    IEnumerator Start()
        ///    {
        ///        var assetPackNames = new string[] { "MyAssetPack", "AnotherAssetPack" };
        ///        var stateOperation = AndroidAssetPacks.GetAssetPackStateAsync(assetPackNames);
        ///
        ///        // Yield until the query completes.
        ///        yield return stateOperation;
        ///
        ///        Debug.Log($"Total size of queried packs: {stateOperation.size} bytes");
        ///
        ///        if (stateOperation.states == null)
        ///        {
        ///            Debug.LogError("Failed to retrieve asset pack states.");
        ///            yield break;
        ///        }
        ///
        ///        var packsToDownload = new List<string>();
        ///        foreach (var state in stateOperation.states)
        ///        {
        ///            if (state.status == AndroidAssetPackStatus.NotInstalled)
        ///            {
        ///                packsToDownload.Add(state.name);
        ///            }
        ///            else if (state.status == AndroidAssetPackStatus.Completed)
        ///            {
        ///                Debug.Log($"{state.name} is already downloaded.");
        ///            }
        ///            else if (state.error != AndroidAssetPackError.NoError)
        ///            {
        ///                Debug.LogError($"{state.name} has error: {state.error}");
        ///            }
        ///        }
        ///
        ///        if (packsToDownload.Count > 0)
        ///        {
        ///            Debug.Log($"Downloading {packsToDownload.Count} asset pack(s): {string.Join(", ", packsToDownload)}");
        ///            yield return AndroidAssetPacks.DownloadAssetPackAsync(packsToDownload.ToArray());
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="GetAssetPackStateAsyncOperation" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        public static GetAssetPackStateAsyncOperation GetAssetPackStateAsync(string[] assetPackNames) { return null; }
        ///<summary>Downloads Android asset packs.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.</remarks>
        ///<param name="assetPackNames">The array of names of Android asset packs to download.</param>
        ///<param name="callback">The callback method to inform about download progress. It gets called multiple times for each asset pack during its download. The callback method must have a parameter of AndroidAssetPackInfo type.
        ///The default value is null.</param>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class DownloadAssetPackCallbackExample : MonoBehaviour
        ///{
        ///    bool isShowingDialog = false;
        ///
        ///    void Start()
        ///    {
        ///        AndroidAssetPacks.DownloadAssetPackAsync(
        ///            new string[] { "MyAssetPack" },
        ///            OnDownloadProgress
        ///        );
        ///    }
        ///
        ///    void OnDownloadProgress(AndroidAssetPackInfo info)
        ///    {
        ///        switch (info.status)
        ///        {
        ///            case AndroidAssetPackStatus.Pending:
        ///                Debug.Log($"{info.name}: Pending");
        ///                break;
        ///            case AndroidAssetPackStatus.Downloading:
        ///                float percentComplete = info.size > 0
        ///                    ? (float)info.bytesDownloaded / info.size * 100f
        ///                    : 0f;
        ///                Debug.Log($"{info.name}: Downloading {percentComplete:F1}%");
        ///                break;
        ///            case AndroidAssetPackStatus.Transferring:
        ///                Debug.Log($"{info.name}: Transferring {(info.transferProgress * 100f):F1}%");
        ///                break;
        ///            case AndroidAssetPackStatus.Completed:
        ///                var path = AndroidAssetPacks.GetAssetPackPath(info.name);
        ///                Debug.Log($"{info.name}: Completed. Path: {path}");
        ///                break;
        ///            case AndroidAssetPackStatus.Failed:
        ///                Debug.LogError($"{info.name}: Failed with error {info.error}");
        ///                break;
        ///            case AndroidAssetPackStatus.WaitingForWifi:
        ///            case AndroidAssetPackStatus.RequiresUserConfirmation:
        ///                if (!isShowingDialog)
        ///                {
        ///                    isShowingDialog = true;
        ///                    Debug.Log(info.status == AndroidAssetPackStatus.WaitingForWifi
        ///                        ? $"{info.name}: Download paused until connected to Wi-Fi."
        ///                        : $"{info.name}: Download requires user confirmation.");
        ///                    AndroidAssetPacks.ShowConfirmationDialogAsync(result =>
        ///                    {
        ///                        isShowingDialog = false;
        ///                        Debug.Log(result.consentGiven
        ///                            ? "User gave consent. Downloads will resume."
        ///                            : "User denied consent.");
        ///                    });
        ///                }
        ///                break;
        ///            case AndroidAssetPackStatus.Canceled:
        ///                Debug.Log($"{info.name}: Download canceled.");
        ///                break;
        ///            case AndroidAssetPackStatus.NotInstalled:
        ///                Debug.Log($"{info.name}: Android asset pack is not installed");
        ///                break;
        ///            default:
        ///                Debug.Log($"{info.name}: Status {info.status}");
        ///                break;
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPackInfo" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        ///<seealso cref="AndroidAssetPacks.RemoveAssetPack" />
        ///<seealso cref="AndroidAssetPacks.ShowConfirmationDialogAsync" />
        public static void DownloadAssetPackAsync(string[] assetPackNames, Action<AndroidAssetPackInfo> callback) {}
        ///<summary>Downloads Android asset packs to the device.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.</remarks>
        ///<param name="assetPackNames">The array of names of Android asset packs to download.</param>
        ///<returns>An object that represents the download operation. If you yield this object inside a coroutine, the coroutine pauses until the operation is complete.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class DownloadAssetPackAsyncExample : MonoBehaviour
        ///{
        ///    IEnumerator Start()
        ///    {
        ///        var assetPackNames = new string[] { "MyAssetPack" };
        ///
        ///        var downloadOperation = AndroidAssetPacks.DownloadAssetPackAsync(assetPackNames);
        ///
        ///        // Yield until the download completes.
        ///        yield return downloadOperation;
        ///
        ///        if (downloadOperation.downloadedAssetPacks.Length == 0)
        ///        {
        ///            Debug.LogWarning("No asset packs were downloaded.");
        ///        }
        ///
        ///        foreach (var packName in downloadOperation.downloadedAssetPacks)
        ///        {
        ///            var path = AndroidAssetPacks.GetAssetPackPath(packName);
        ///            Debug.Log($"Asset pack downloaded to: {path}");
        ///        }
        ///
        ///        foreach (var failedPack in downloadOperation.downloadFailedAssetPacks)
        ///        {
        ///            Debug.LogError($"Failed to download asset pack: {failedPack}");
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="DownloadAssetPackAsyncOperation" />
        ///<seealso cref="AndroidAssetPackInfo" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackPath" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        ///<seealso cref="AndroidAssetPacks.RemoveAssetPack" />
        ///<seealso cref="AndroidAssetPacks.ShowConfirmationDialogAsync" />
        public static DownloadAssetPackAsyncOperation DownloadAssetPackAsync(string[] assetPackNames) { return null; }
        ///<summary>Requests to use mobile data to download Android asset packs.</summary>
        ///<remarks>If the device is not connected to WiFi, it pauses large Android asset pack downloads until a WiFi connection is available. If this is the case, the asset pack has the AndroidAssetPackStatus.WaitingForWifi status. In this situation, you can call RequestToUseMobileDataAsync to give the end user the option to download your application's asset packs using mobile data.
        ///This method directly wraps Google's PlayCore plugin API. If the PlayCore plugin is missing, calling this method throws an InvalidOperationException exception.
        ///
        ///
        ///**Note:** <c>RequestToUseMobileDataAsync</c> is deprecated. Use <see cref="AndroidAssetPacks.ShowConfirmationDialogAsync" /> instead.</remarks>
        ///<param name="callback">The callback method to get the result. The callback method must have an AndroidAssetPackUseMobileDataRequestResult parameter. This contains the value that indicates the end user's choice. The application raises this callback a single time after the end user submits their decision.</param>
        ///<seealso cref="AndroidAssetPackUseMobileDataRequestResult" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        [Obsolete("RequestToUseMobileDataAsync is deprecated. Use ShowConfirmationDialogAsync instead.", false)]
        public static void RequestToUseMobileDataAsync(Action<AndroidAssetPackUseMobileDataRequestResult> callback) {}
        ///<summary>Requests to use mobile data to download Android asset packs.</summary>
        ///<remarks>If the device is not connected to WiFi, it pauses large Android asset pack downloads until a WiFi connection is available. If this is the case, the asset pack has the AndroidAssetPackStatus.WaitingForWifi status. In this situation, you can call RequestToUseMobileDataAsync to give the end user the option to download your application's asset packs using mobile data.
        ///This method directly wraps Google's PlayCore plugin API. If the PlayCore plugin is missing, calling this method throws an InvalidOperationException exception.
        ///
        ///
        ///**Note:** <c>RequestToUseMobileDataAsync</c> is deprecated. Use <see cref="AndroidAssetPacks.ShowConfirmationDialogAsync" /> instead.</remarks>
        ///<returns>Returns an object that represents the request operation. If you yield this object inside a coroutine, the coroutine pauses until the operation is complete.</returns>
        ///<seealso cref="RequestToUseMobileDataAsyncOperation" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        [Obsolete("RequestToUseMobileDataAsync is deprecated. Use ShowConfirmationDialogAsync instead.", false)]
        public static RequestToUseMobileDataAsyncOperation RequestToUseMobileDataAsync() { return null; }
        ///<summary>Displays a dialog that asks the user for consent to download asset packs that require user confirmation or WiFi connection.</summary>
        ///<remarks>If the device isn't connected to WiFi, large Android asset pack downloads pause until a WiFi connection is available. In this case, the asset pack has the <see cref="AndroidAssetPackStatus.WaitingForWifi" /> status. Asset packs can also have <see cref="AndroidAssetPackStatus.RequiresUserConfirmation" /> status if the current app version was not installed through the Google Play Store. In both situations, the asset packs require user consent to download. To get this consent, call <c>ShowConfirmationDialogAsync</c> to give the user the option to download your application's asset packs using mobile data or to update the app if a valid version isn't installed.
        ///This method directly wraps Google's PlayCore plugin API. If the PlayCore plugin is missing, calling this method throws an InvalidOperationException exception.</remarks>
        ///<param name="callback">The callback method to get the user's response. Must have an AndroidAssetPackConfirmationDialogResult parameter containing the user's response. The application invokes this callback once after the user submits their decision.</param>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class ShowConfirmationDialogAsyncCallbackExample : MonoBehaviour
        ///{
        ///    // Call this when an asset pack download is waiting for user confirmation
        ///    // (for example, when the status is WaitingForWifi or RequiresUserConfirmation).    
        ///    public void ShowDialog()
        ///    {
        ///        AndroidAssetPacks.ShowConfirmationDialogAsync(OnDialogResult);
        ///    }
        ///
        ///    void OnDialogResult(AndroidAssetPackConfirmationDialogResult result)
        ///    {
        ///        if (result.consentGiven)
        ///        {
        ///            Debug.Log("User gave consent. Downloads will resume.");
        ///        }
        ///        else
        ///        {
        ///            Debug.Log("User denied consent. Downloads will remain paused.");
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPackConfirmationDialogResult" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        ///<seealso cref="AndroidAssetPackStatus.RequiresUserConfirmation" />
        ///<seealso cref="AndroidAssetPackStatus.WaitingForWifi" />
        public static void ShowConfirmationDialogAsync(Action<AndroidAssetPackConfirmationDialogResult> callback) {}
        ///<summary>Displays a dialog that asks the user for consent to download asset packs that require user confirmation or WiFi connection.</summary>
        ///<remarks>If the device isn't connected to WiFi, large Android asset pack downloads pause until a WiFi connection is available. In this case, the asset pack has the <see cref="AndroidAssetPackStatus.WaitingForWifi" /> status. Asset packs can also have <see cref="AndroidAssetPackStatus.RequiresUserConfirmation" /> status if the current app version was not installed through the Google Play Store. In both situations, the asset packs require user consent to download. To get this consent, call <c>ShowConfirmationDialogAsync</c> to give the user the option to download your application's asset packs using mobile data or to update the app if a valid version isn't installed.
        ///This method directly wraps Google's PlayCore plugin API. If the PlayCore plugin is missing, calling this method throws an InvalidOperationException exception.</remarks>
        ///<returns>Returns an object that represents the request operation. If you yield this object inside a coroutine, the coroutine pauses until the operation is complete.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class ShowConfirmationDialogAsyncExample : MonoBehaviour
        ///{
        ///    bool isShowingDialog = false;
        ///
        ///    void Start()
        ///    {
        ///        var assetPackNames = new string[] { "MyAssetPack" };
        ///
        ///        // Start the download with a callback to monitor status changes.
        ///        AndroidAssetPacks.DownloadAssetPackAsync(
        ///            assetPackNames,
        ///            info =>
        ///            {
        ///                Debug.Log($"{info.name}: {info.status}");
        ///
        ///                if (!isShowingDialog &&
        ///                    (info.status == AndroidAssetPackStatus.WaitingForWifi ||
        ///                     info.status == AndroidAssetPackStatus.RequiresUserConfirmation))
        ///                {
        ///                    // Show a dialog asking the user for consent.
        ///                    isShowingDialog = true;
        ///                    StartCoroutine(HandleConfirmationDialog());
        ///                }
        ///            }
        ///        );
        ///    }
        ///
        ///    IEnumerator HandleConfirmationDialog()
        ///    {
        ///        var dialogOperation = AndroidAssetPacks.ShowConfirmationDialogAsync();
        ///
        ///        // Yield until the user responds.
        ///        yield return dialogOperation;
        ///
        ///        if (dialogOperation.result.consentGiven)
        ///        {
        ///            Debug.Log("User gave consent. Downloads will resume.");
        ///        }
        ///        else
        ///        {
        ///            Debug.Log("User denied consent. Downloads remain paused.");
        ///        }
        ///
        ///        isShowingDialog = false;
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="ConfirmationDialogAsyncOperation" />
        ///<seealso cref="AndroidAssetPacks.CancelAssetPackDownload" />
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        ///<seealso cref="AndroidAssetPacks.GetAssetPackStateAsync" />
        ///<seealso cref="AndroidAssetPackStatus.RequiresUserConfirmation" />
        ///<seealso cref="AndroidAssetPackStatus.WaitingForWifi" />
        public static ConfirmationDialogAsyncOperation ShowConfirmationDialogAsync() { return null; }
        ///<summary>Gets the full path to the location where the device stores the assets for the Android asset pack.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.
        ///
        ///You can use the returned path with file I/O methods to access assets inside.</remarks>
        ///<param name="assetPackName">The name of the Android asset pack to get path.</param>
        ///<returns>The full path to the location where the device stores the assets for the Android asset pack. An empty string if the asset pack you specify is not on the device, or if it doesn't use the fast-follow or on-demand delivery type.</returns>
        ///<example>
        ///  <code><![CDATA[using System.IO;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class GetAssetPackPathExample : MonoBehaviour
        ///{
        ///    void LoadAssetFromPack()
        ///    {
        ///        var path = AndroidAssetPacks.GetAssetPackPath("MyAssetPack");
        ///
        ///        if (string.IsNullOrEmpty(path))
        ///        {
        ///            Debug.LogError("Asset pack is not available. Ensure it has been downloaded and completed.");
        ///            return;
        ///        }
        ///
        ///        // Access a file inside the asset pack.
        ///        var filePath = Path.Combine(path, "mydata.json");
        ///
        ///        if (!File.Exists(filePath))
        ///        {
        ///            Debug.LogError($"File not found at {filePath}");
        ///            return;
        ///        }
        ///
        ///        var contents = File.ReadAllText(filePath);
        ///        Debug.Log($"Loaded data from asset pack: {contents}");
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public static string GetAssetPackPath(string assetPackName) { return ""; }
        ///<summary>Cancels Android asset pack downloads.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.</remarks>
        ///<param name="assetPackNames">The array of names of the Android asset packs to cancel the download for.</param>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class CancelAssetPackDownloadExample : MonoBehaviour
        ///{
        ///    string[] assetPackNames = new string[] { "LargeAssetPack" };
        ///    DownloadAssetPackAsyncOperation downloadOperation;
        ///
        ///    IEnumerator Start()
        ///    {
        ///        downloadOperation = AndroidAssetPacks.DownloadAssetPackAsync(assetPackNames);
        ///        yield return downloadOperation;
        ///    }
        ///
        ///    // Call this method from a UI button to cancel the download.
        ///    public void CancelDownload()
        ///    {
        ///        if (downloadOperation != null && !downloadOperation.isDone)
        ///        {
        ///            AndroidAssetPacks.CancelAssetPackDownload(assetPackNames);
        ///            Debug.Log("Asset pack download canceled.");
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public static void CancelAssetPackDownload(string[] assetPackNames) {}
        ///<summary>Removes the specified Android asset pack from the device.</summary>
        ///<remarks>This method directly wraps Google's &lt;a href="https://developer.android.com/guide/playcore"&gt;PlayCore plug-in&lt;/a&gt; API. If the PlayCore plug-in is missing, calling this method throws an <c>InvalidOperationException</c>.
        ///
        ///If you call this method with any core Unity asset pack names that <see cref="AndroidAssetPacks.GetCoreUnityAssetPackNames" /> returns as a parameter, the method has no effect.</remarks>
        ///<param name="assetPackName">The name of the Android asset pack to remove.</param>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Android;
        ///
        ///public class RemoveAssetPackExample : MonoBehaviour
        ///{
        ///    // Call this method to remove an on-demand asset pack that is no longer needed.
        ///    public void RemoveCustomAssetPack(string assetPackName)
        ///    {
        ///        var path = AndroidAssetPacks.GetAssetPackPath(assetPackName);
        ///        if (string.IsNullOrEmpty(path))
        ///        {
        ///            Debug.Log($"{assetPackName}: Asset pack is not installed.");
        ///            return;
        ///        }
        ///
        ///        AndroidAssetPacks.RemoveAssetPack(assetPackName);
        ///        Debug.Log($"Removed asset pack: {assetPackName}");
        ///    }
        ///}]]></code>
        ///</example>
        ///<seealso cref="AndroidAssetPacks.DownloadAssetPackAsync" />
        public static void RemoveAssetPack(string assetPackName) {}

        // These values must match constants in AndroidAssetPacks.h
        // We can't directly access them since all code in PlatformDependent gets stripped when building source code delivery
        private static string GetTextureCompressionsPackName() { return "UnityTextureCompressionsAssetPack"; }
        private static string GetDataPackName() { return "UnityDataAssetPack"; }
        private static string GetStreamingAssetsPackName() { return "UnityStreamingAssetsPack"; }
    }
}
