// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine.Networking
{
    ///<summary>Helpers for downloading asset bundles using <see cref="UnityWebRequest" />.</summary>
    ///<example>
    ///  <code><![CDATA[using UnityEngine;
    ///using UnityEngine.Networking;
    ///using System.Threading.Tasks;
    ///
    /// // This example demonstrates how to load an AssetBundle asynchronously.
    ///public class AssetBundleLoader : MonoBehaviour
    ///{
    ///    // Load an AssetBundle from a URL asynchronously
    ///    public async Task<AssetBundle> LoadAssetBundleAsync(string url)
    ///    {
    ///        using (UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(url))
    ///        {
    ///            var operation = request.SendWebRequest();
    ///
    ///            while (!operation.isDone)
    ///                await Task.Yield();
    ///
    ///            if (request.result != UnityWebRequest.Result.Success)
    ///            {
    ///                Debug.LogError($"Failed to load AssetBundle: {request.error}");
    ///                return null;
    ///            }
    ///
    ///            return DownloadHandlerAssetBundle.GetContent(request);
    ///        }
    ///    }
    ///
    ///    // Example usage
    ///    public async void LoadBundle()
    ///    {
    ///        // Replace this with your own URL.
    ///        // You can use python -m http.server to serve files locally.
    ///        // Remember to first build the AssetBundle
    ///        string bundleUrl = "http://example.com/mybundle.assetbundle";
    ///        AssetBundle bundle = await LoadAssetBundleAsync(bundleUrl);
    ///
    ///        if (bundle != null)
    ///        {
    ///            // Load assets from bundle
    ///            GameObject prefab = bundle.LoadAsset<GameObject>("MyPrefab");
    ///            Instantiate(prefab);
    ///
    ///            // Remember to unload when done
    ///            bundle.Unload(false);
    ///        }
    ///    }
    ///}]]></code>
    ///</example>
    ///<seealso cref="AssetBundle" />
    ///<seealso cref="DownloadHandlerAssetBundle" />
    ///<seealso cref="GetAssetBundle" />
    public static class UnityWebRequestAssetBundle
    {
        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(string uri)
        {
            return GetAssetBundle(uri, 0);
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(Uri uri)
        {
            return GetAssetBundle(uri, 0);
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(string uri, uint crc)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(Uri uri, uint crc)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri.AbsoluteUri, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="version">An integer version number, which will be compared to the cached version of the asset bundle to download. Increment this number to force Unity to redownload a cached asset bundle.
        ///
        ///Analogous to the <c>version</c> parameter for <c>WWW.LoadFromCacheOrDownload</c>.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(string uri, uint version, uint crc)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri, version, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="version">An integer version number, which will be compared to the cached version of the asset bundle to download. Increment this number to force Unity to redownload a cached asset bundle.
        ///
        ///Analogous to the <c>version</c> parameter for <c>WWW.LoadFromCacheOrDownload</c>.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(Uri uri, uint version, uint crc)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri.AbsoluteUri, version, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="hash">A version hash. If this hash does not match the hash for the cached version of this asset bundle, the asset bundle will be redownloaded.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(string uri, Hash128 hash, uint crc = 0)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri, hash, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="hash">A version hash. If this hash does not match the hash for the cached version of this asset bundle, the asset bundle will be redownloaded.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(Uri uri, Hash128 hash, uint crc = 0)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri.AbsoluteUri, hash, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="cachedAssetBundle">A structure used to download a given version of AssetBundle to a customized cache path.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(string uri, CachedAssetBundle cachedAssetBundle, uint crc = 0)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri, cachedAssetBundle, crc),
                null
            );

            return request;
        }

        ///<summary>Creates a UnityWebRequest optimized for downloading a Unity Asset Bundle via HTTP GET.</summary>
        ///<remarks>This method creates a UnityWebRequest, sets the method to <c>GET</c> and sets the target URL to the string <c>uri</c> argument. Sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAssetBundle" /> to the <see cref="UnityWebRequest" />. This <see cref="DownloadHandler" /> has a special <see cref="DownloadHandlerAssetBundle.assetBundle" /> property, which can be used to extract the asset bundle once enough data has been downloaded and decoded to permit access to the resources inside the bundle.
        ///
        ///In addition, the <see cref="DownloadHandlerAssetBundle" /> streams data into a ringbuffer and decompresses the data on a worker thread, saving many memory allocations compared to downloading the data all at once.
        ///
        ///If supplied with an integer <c>version</c> or Hash128 <c>hash</c> argument, the <see cref="DownloadHandlerAssetBundle" /> will employ the Asset Bundle caching system. If an Asset Bundle has been cached and does not need to be redownloaded, then the <see cref="UnityWebRequest" /> will complete once the Asset Bundle has finished loading from the cache.
        ///
        ///Cached AssetBundles are uniquely identified solely by the filename and version. All domain and path information in <c>url</c> is ignored by Caching. Since cached AssetBundles are identified by filename instead of the full URL, you can change the directory from where the asset bundle is downloaded at any time. This is useful for pushing out new versions of the game and ensuring that files are not cached incorrectly by the browser or by a CDN.
        ///
        ///Usually using the filename of the AssetBundle to generate the cache path is fine. But if there are different AssetBundles with the same last file name, cache conflicts happens. With CachedAssetBundle struct, you can use <see cref="CachedAssetBundle.name" /> to customized the cache path to avoid the cache conflicts. You can also utilize this to organize the cache data structure.
        ///
        ///**Note**, that while you can use this API to load Asset Bundle from local storage (using file:// URI or jar:file// on Android), this is not recommended, use <see cref="AssetBundle.LoadFromFileAsync" /> instead.</remarks>
        ///<param name="uri">The URI of the asset bundle to download.</param>
        ///<param name="crc">If nonzero, this number will be compared to the checksum of the downloaded asset bundle data. If the CRCs do not match, an error will be logged and the asset bundle will not be loaded. If set to zero, CRC checking will be skipped.</param>
        ///<param name="cachedAssetBundle">A structure used to download a given version of AssetBundle to a customized cache path.</param>
        ///<returns>A UnityWebRequest configured to downloading a Unity Asset Bundle.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetText());
        ///    }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        using (UnityWebRequest uwr = UnityWebRequestAssetBundle.GetAssetBundle("https://www.my-server.com/mybundle"))
        ///        {
        ///            yield return uwr.SendWebRequest();
        ///
        ///            if (uwr.result != UnityWebRequest.Result.Success)
        ///            {
        ///                Debug.Log(uwr.error);
        ///            }
        ///            else
        ///            {
        ///                // Get downloaded asset bundle
        ///                AssetBundle bundle = DownloadHandlerAssetBundle.GetContent(uwr);
        ///
        ///                // Unload the AssetBundle 
        ///                bundle.Unload(true);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAssetBundle(Uri uri, CachedAssetBundle cachedAssetBundle, uint crc = 0)
        {
            UnityWebRequest request = new UnityWebRequest(
                uri,
                UnityWebRequest.kHttpVerbGET,
                new DownloadHandlerAssetBundle(uri.AbsoluteUri, cachedAssetBundle, crc),
                null
            );

            return request;
        }

    }
}
