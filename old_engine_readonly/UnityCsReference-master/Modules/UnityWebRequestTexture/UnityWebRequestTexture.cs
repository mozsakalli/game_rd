// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine.Networking
{
    ///<summary>Helpers for downloading image files into Textures using <see cref="UnityWebRequest" />.</summary>
    public static class UnityWebRequestTexture
    {
        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Use <c>UnityWebRequestTexture.GetTexture</c> to retrieve a Texture file from a remote server. This function is very similar to <c>UnityWebRequest.Get</c>, but is optimized for downloading and storing textures efficiently. It creates a <see cref="UnityWebRequest" /> and sets the target URL to the one specified as a string or uri object in the <c>uri</c> argument. No other flags or custom headers are set.
        ///
        ///This method attaches a <see cref="DownloadHandlerTexture" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerTexture" /> is a specialized <see cref="DownloadHandler" /> optimized for storing images that are to be used as textures in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating a texture manually in script. In addition, texture conversion is performed on a worker thread.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.
        ///
        ///The texture is created as if it stores color data. Only JPG and PNG formats are supported.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
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
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png"))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="P:UnityEditor.TextureImporter.sRGBTexture" />
        public static UnityWebRequest GetTexture(string uri)
        {
            return UnityWebRequestTexture.GetTexture(uri, false);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Use <c>UnityWebRequestTexture.GetTexture</c> to retrieve a Texture file from a remote server. This function is very similar to <c>UnityWebRequest.Get</c>, but is optimized for downloading and storing textures efficiently. It creates a <see cref="UnityWebRequest" /> and sets the target URL to the one specified as a string or uri object in the <c>uri</c> argument. No other flags or custom headers are set.
        ///
        ///This method attaches a <see cref="DownloadHandlerTexture" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerTexture" /> is a specialized <see cref="DownloadHandler" /> optimized for storing images that are to be used as textures in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating a texture manually in script. In addition, texture conversion is performed on a worker thread.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.
        ///
        ///The texture is created as if it stores color data. Only JPG and PNG formats are supported.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
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
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png"))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="P:UnityEditor.TextureImporter.sRGBTexture" />
        public static UnityWebRequest GetTexture(Uri uri)
        {
            return UnityWebRequestTexture.GetTexture(uri, false);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Use <c>UnityWebRequestTexture.GetTexture</c> to retrieve a Texture file from a remote server. This function is very similar to <c>UnityWebRequest.Get</c>, but is optimized for downloading and storing textures efficiently. It creates a <see cref="UnityWebRequest" /> and sets the target URL to the one specified as a string or uri object in the <c>uri</c> argument. No other flags or custom headers are set.
        ///
        ///This method attaches a <see cref="DownloadHandlerTexture" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerTexture" /> is a specialized <see cref="DownloadHandler" /> optimized for storing images that are to be used as textures in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating a texture manually in script. In addition, texture conversion is performed on a worker thread.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.
        ///
        ///The texture is created as if it stores color data. Only JPG and PNG formats are supported.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<param name="nonReadable">If true, the texture's raw data will not be accessible to script. This can conserve memory. Default: <c>false</c>.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
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
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png"))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="P:UnityEditor.TextureImporter.sRGBTexture" />
        public static UnityWebRequest GetTexture(string uri, bool nonReadable)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(!nonReadable), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Use <c>UnityWebRequestTexture.GetTexture</c> to retrieve a Texture file from a remote server. This function is very similar to <c>UnityWebRequest.Get</c>, but is optimized for downloading and storing textures efficiently. It creates a <see cref="UnityWebRequest" /> and sets the target URL to the one specified as a string or uri object in the <c>uri</c> argument. No other flags or custom headers are set.
        ///
        ///This method attaches a <see cref="DownloadHandlerTexture" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerTexture" /> is a specialized <see cref="DownloadHandler" /> optimized for storing images that are to be used as textures in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating a texture manually in script. In addition, texture conversion is performed on a worker thread.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.
        ///
        ///The texture is created as if it stores color data. Only JPG and PNG formats are supported.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<param name="nonReadable">If true, the texture's raw data will not be accessible to script. This can conserve memory. Default: <c>false</c>.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
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
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png"))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="P:UnityEditor.TextureImporter.sRGBTexture" />
        public static UnityWebRequest GetTexture(Uri uri, bool nonReadable)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(!nonReadable), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Same as an overload with only <c>uri</c> parameter, except that it allows more control over the properties of texture that will be created. For example, using this overload you can disable creation of mipmaps or use linear color space.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<param name="parameters">Parameters specifying various properties of texture that will be created.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///
        ///public class NewMonoBehaviourScript : MonoBehaviour
        ///{
        ///   void Start()
        ///   {
        ///       StartCoroutine(GetText());
        ///   }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        // Use linear color space and reduce memory usage by disabling mipmaps and ability to read pixels
        ///        var parameters = DownloadedTextureParams.Default;
        ///        parameters.readable = false;
        ///        parameters.mipmapChain = false;
        ///        parameters.linearColorSpace = true;
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png", parameters))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    } 
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetTexture(string uri, DownloadedTextureParams parameters)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(parameters), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> intended to download an image via HTTP GET and create a <see cref="Texture" /> based on the retrieved data.</summary>
        ///<remarks>Same as an overload with only <c>uri</c> parameter, except that it allows more control over the properties of texture that will be created. For example, using this overload you can disable creation of mipmaps or use linear color space.</remarks>
        ///<param name="uri">The URI of the image to download.</param>
        ///<param name="parameters">Parameters specifying various properties of texture that will be created.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an image and convert it to a <see cref="Texture" />.</returns>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///
        ///public class NewMonoBehaviourScript : MonoBehaviour
        ///{
        ///   void Start()
        ///   {
        ///       StartCoroutine(GetText());
        ///   }
        ///
        ///    IEnumerator GetText()
        ///    {
        ///        // Use linear color space and reduce memory usage by disabling mipmaps and ability to read pixels
        ///        var parameters = DownloadedTextureParams.Default;
        ///        parameters.readable = false;
        ///        parameters.mipmapChain = false;
        ///        parameters.linearColorSpace = true;
        ///        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture("https://www.my-server.com/myimage.png", parameters))
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
        ///                var texture = DownloadHandlerTexture.GetContent(uwr);
        ///            }
        ///        }
        ///    } 
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetTexture(Uri uri, DownloadedTextureParams parameters)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerTexture(parameters), null);
        }

    }
}
