// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;

namespace UnityEngine.Networking
{
    ///<summary>A <see cref="DownloadHandler" /> subclass specialized for downloading <see cref="AssetBundle" />s.</summary>
    ///<remarks>This subclass streams downloaded data into Unity's AssetBundle decompression and decoding system on worker threads, providing efficient downloading and processing for <see cref="AssetBundle" /> objects.
    ///
    ///The advantage to this download handler is that it can stream data to Unity's AssetBundle system. After all the data has been received, the AssetBundle is available as an <see cref="AssetBundle" /> object. Only one copy of the <see cref="AssetBundle" /> object is created. This reduces runtime memory allocation and the memory impact of loading the AssetBundle. It also allows AssetBundles to be partially used while not fully downloaded, so you can stream assets.
    ///
    ///All downloading and decompression occurs on worker threads, except on the Web platform.
    ///
    ///Downloaded AssetBundle data is processed by a <c>DownloadHandlerAssetBundle</c> object, which has a special <c>assetBundle</c> property to retrieve the <see cref="AssetBundle" /> object.
    ///
    ///Due to the way the AssetBundle system works, all AssetBundles must have an address associated with them. Generally, this is the nominal URL at which they're located (meaning the URL before any redirects). In almost all cases, you should pass in the same URL as you passed to the UnityWebRequest. When using the High Level API (HLAPI), this is done for you.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Networking;
    ///using System.Collections;
    ///
    ///public class MyBehaviour : MonoBehaviour {
    ///    void Start() {
    ///        StartCoroutine(GetAssetBundle());
    ///    }
    ///
    ///    IEnumerator GetAssetBundle() {
    ///        UnityWebRequest www = new UnityWebRequest("https://www.my-server.com");
    ///        DownloadHandlerAssetBundle handler = new DownloadHandlerAssetBundle(www.url, 0);
    ///        www.downloadHandler = handler;
    ///        yield return www.SendWebRequest();
    ///
    ///        if (www.result != UnityWebRequest.Result.Success) {
    ///            Debug.Log(www.error);
    ///        }
    ///        else {
    ///            // Extracts AssetBundle
    ///            AssetBundle bundle = handler.assetBundle;
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="UnityWebRequestAssetBundle" />
    [StructLayout(LayoutKind.Sequential)]
    [NativeHeader("Modules/UnityWebRequestAssetBundle/Public/DownloadHandlerAssetBundle.h")]
    public sealed class DownloadHandlerAssetBundle : DownloadHandler
    {
        private extern static IntPtr Create([UnityMarshalAs(NativeType.ScriptingObjectPtr)] DownloadHandlerAssetBundle obj, string url, uint crc);
        private extern static IntPtr CreateCached([UnityMarshalAs(NativeType.ScriptingObjectPtr)] DownloadHandlerAssetBundle obj, string url, string name, Hash128 hash, uint crc);

        private void InternalCreateAssetBundle(string url, uint crc)
        {
            m_Ptr = Create(this, url, crc);
        }

        private void InternalCreateAssetBundleCached(string url, string name, Hash128 hash, uint crc)
        {
            m_Ptr = CreateCached(this, url, name, hash, crc);
        }

        ///<summary>Standard constructor for non-cached asset bundles.</summary>
        ///<remarks>This constructor will bypass the caching system and simply download the <see cref="AssetBundle" /> from <c>url</c>.
        ///
        ///If the <c>crc</c> argument is non-zero, then the <c>crc</c> argument will be compared to the checksum of the downloaded data. If the CRCs do not match, an error will be logged, the asset bundle will not be loaded, and <see cref="assetBundle" /> will return <c>null</c>.
        ///
        ///If you do not wish to use CRC integrity checking, pass zero as the <c>crc</c> argument.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the asset bundle is located.</param>
        ///<param name="crc">A checksum to compare to the downloaded data for integrity checking, or zero to skip integrity checking.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    IEnumerator Start()
        ///    {
        ///        string url = "https://website.com/assetbundle";
        ///        using (var uwr = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
        ///        {
        ///            uwr.downloadHandler = new DownloadHandlerAssetBundle(url, 0);
        ///            yield return uwr.SendWebRequest();
        ///            AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///            //Unload the AssetBundle
        ///            bundle.Unload(true);
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public DownloadHandlerAssetBundle(string url, uint crc)
        {
            InternalCreateAssetBundle(url, crc);
        }

        ///<summary>Simple versioned constructor. Caches downloaded asset bundles.</summary>
        ///<remarks>When this constructor is used, the <see cref="DownloadHandlerAssetBundle" /> will first check to see if there is a cached <see cref="AssetBundle" /> from <c>url</c>.
        ///
        ///If there is no cached asset bundle, or if the cached asset bundle's <c>version</c> matches the <c>version</c> argument, then the system will skip downloading the asset bundle and instead load it from the cache.
        ///
        ///If there is a cached asset bundle, but the cached bundle's <c>version</c> does not match the <c>version</c> argument, then the system will re-download the asset bundle from <c>url</c>.
        ///
        ///If the <c>crc</c> argument is non-zero, then the <c>crc</c> argument will be compared to the checksum of the downloaded data. If the CRCs do not match, an error will be logged, the asset bundle will not be loaded, and <see cref="assetBundle" /> will return <c>null</c>.
        ///
        ///If you do not wish to use CRC integrity checking, pass zero as the <c>crc</c> argument.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the asset bundle is located.</param>
        ///<param name="crc">A checksum to compare to the downloaded data for integrity checking, or zero to skip integrity checking.</param>
        ///<param name="version">Current version number of the asset bundle at <c>url</c>. Increment to redownload.</param>
        public DownloadHandlerAssetBundle(string url, uint version, uint crc)
        {
            InternalCreateAssetBundleCached(url, "", new Hash128(0, 0, 0, version), crc);
        }

        ///<summary>Versioned constructor. Caches downloaded asset bundles.</summary>
        ///<remarks>When this constructor is used, the <see cref="DownloadHandlerAssetBundle" /> will first check to see if there is a cached <see cref="AssetBundle" /> from <c>url</c>.
        ///
        ///If there is no cached asset bundle, or if the cached asset bundle's <c>hash</c> matches the <c>hash</c> argument, then the system will skip downloading the asset bundle and instead load it from the cache.
        ///
        ///If there is a cached asset bundle, but the cached bundle's <c>hash</c> does not match the <c>hash</c> argument, then the system will re-download the asset bundle from <c>url</c>.
        ///
        ///If the <c>crc</c> argument is non-zero, then the <c>crc</c> argument will be compared to the checksum of the downloaded data. If the CRCs do not match, an error will be logged, the asset bundle will not be loaded, and <see cref="assetBundle" /> will return <c>null</c>.
        ///
        ///If you do not wish to use CRC integrity checking, pass zero as the <c>crc</c> argument.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the asset bundle is located.</param>
        ///<param name="crc">A checksum to compare to the downloaded data for integrity checking, or zero to skip integrity checking.</param>
        ///<param name="hash">A hash object defining the version of the asset bundle.</param>
        public DownloadHandlerAssetBundle(string url, Hash128 hash, uint crc)
        {
            InternalCreateAssetBundleCached(url, "", hash, crc);
        }

        ///<summary>Versioned constructor. Caches downloaded asset bundles to a customized cache path.</summary>
        ///<remarks>Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the asset bundle is located.</param>
        ///<param name="hash">A hash object defining the version of the asset bundle.</param>
        ///<param name="crc">A checksum to compare to the downloaded data for integrity checking, or zero to skip integrity checking.</param>
        ///<param name="name">AssetBundle name which is used as the customized cache path.</param>
        public DownloadHandlerAssetBundle(string url, string name, Hash128 hash, uint crc)
        {
            InternalCreateAssetBundleCached(url, name, hash, crc);
        }

        ///<summary>Versioned constructor. Caches downloaded asset bundles to a customized cache path.</summary>
        ///<remarks>Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the asset bundle is located.</param>
        ///<param name="crc">A checksum to compare to the downloaded data for integrity checking, or zero to skip integrity checking.</param>
        ///<param name="cachedBundle">A structure used to download a given version of AssetBundle to a customized cache path.</param>
        public DownloadHandlerAssetBundle(string url, CachedAssetBundle cachedBundle, uint crc)
        {
            InternalCreateAssetBundleCached(url, cachedBundle.name, cachedBundle.hash, crc);
        }

        ///<summary>Not implemented. Throws &lt;a href="https://msdn.microsoft.com/en-us/library/system.notsupportedexception"&gt;NotSupportedException&lt;/a&gt;.</summary>
        ///<returns>Not implemented.</returns>
        protected override byte[] GetData()
        {
            throw new System.NotSupportedException("Raw data access is not supported for asset bundles");
        }

        ///<summary>Not implemented. Throws &lt;a href="https://msdn.microsoft.com/en-us/library/system.notsupportedexception"&gt;NotSupportedException&lt;/a&gt;.</summary>
        ///<returns>Not implemented.</returns>
        protected override string GetText()
        {
            throw new System.NotSupportedException("String access is not supported for asset bundles");
        }

        ///<summary>Returns the downloaded <see cref="AssetBundle" />, or <c>null</c>. (RO)</summary>
        ///<remarks>This property returns the asset bundle which has been downloaded (or is downloading, in the case of streamed asset bundles).
        ///
        ///If there is an error decoding the asset bundle’s assets, the system will log the error and this property will return <c>null</c>.</remarks>
        public extern AssetBundle assetBundle { get; }

        ///<summary>If true, the AssetBundle will be loaded as part of the <see cref="UnityWebRequest" /> process. If false, the <see cref="AssetBundle" /> will be loaded on demand when accessing the <see cref="DownloadHandlerAssetBundle.assetBundle" /> property.</summary>
        ///<remarks>Default: false.</remarks>
        public extern bool autoLoadAssetBundle { get; [NativeMethod(ThrowsException = true)] set; }

        ///<summary>Returns true if the data downloading portion of the operation is complete.</summary>
        public extern bool isDownloadComplete { get; }

        ///<summary>Returns the downloaded <see cref="AssetBundle" />, or <c>null</c>.</summary>
        ///<remarks>A static function provided for convenience; equivalent to ((DownloadHandlerAssetBundle)www.downloadHandler).assetBundle.</remarks>
        ///<param name="www">A finished UnityWebRequest object with <see cref="DownloadHandlerAssetBundle" /> attached.</param>
        ///<returns>The same as <see cref="DownloadHandlerAssetBundle.assetBundle" /></returns>
        public static AssetBundle GetContent(UnityWebRequest www)
        {
            return GetCheckedDownloader<DownloadHandlerAssetBundle>(www).assetBundle;
        }
        new internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(DownloadHandlerAssetBundle handler) => handler.m_Ptr;
        }
    }
}
