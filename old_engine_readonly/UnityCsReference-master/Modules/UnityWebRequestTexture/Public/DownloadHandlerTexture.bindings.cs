// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngineInternal;
using Unity.Collections;

namespace UnityEngine.Networking
{
    ///<summary>Flags to be set in <see cref="DownloadedTextureParams" /> to indicate properties of the texture that will be created.</summary>
    ///<remarks>These flags should be combined using bitwise-or operator.</remarks>
    [Flags]
    public enum DownloadedTextureFlags : uint
    {
        ///<summary>Special value indicating that none of the other options are being used.</summary>
        None = 0,
        ///<summary>Indicates that created texture must be readable. Allows reading values of texture pixels, but increases memory usage.</summary>
        Readable = 1 << 0,
        ///<summary>Indicates that created texture must have mipmaps (multiple versions of different quality of the same texture). More efficient, but uses more memory.</summary>
        MipmapChain = 1 << 1,
        ///<summary>Indicates that a texture using linear color space must be created.</summary>
        LinearColorSpace = 1 << 2,
    }

    ///<summary>Parameters for the texture to be created.</summary>
    ///<remarks>Refer to <see cref="Texture2D" /> for more details about these parameters.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct DownloadedTextureParams
    {
        ///<summary>A set of properties the created texture should have.</summary>
        public DownloadedTextureFlags flags;
        ///<summary>Number of mipmaps to generate for the created texture.</summary>
        ///<remarks>The value of -1 means the number of mipmaps is automatically determined. Only has meaning when <see cref="flags" /> have mipmap chain flag set.</remarks>
        public int mipmapCount;

        ///<summary>Returns parameters set to recommended values. It is recommended to use this for the initial values and modify only specific parameters that you need.</summary>
        public static DownloadedTextureParams Default => new DownloadedTextureParams()
        {
            flags = DownloadedTextureFlags.Readable | DownloadedTextureFlags.MipmapChain,
            mipmapCount = -1,
        };

        ///<summary>Check or change the flag for readability in <see cref="flags" /> variable.</summary>
        public bool readable
        {
            get => flags.HasFlag(DownloadedTextureFlags.Readable);
            set => SetFlags(DownloadedTextureFlags.Readable, value);
        }

        ///<summary>Check or change the flag for mipmaps in <see cref="flags" /> variable.</summary>
        public bool mipmapChain
        {
            get => flags.HasFlag(DownloadedTextureFlags.MipmapChain);
            set => SetFlags(DownloadedTextureFlags.MipmapChain, value);
        }

        ///<summary>Check or change the flag for linear color space in <see cref="flags" /> variable.</summary>
        public bool linearColorSpace
        {
            get => flags.HasFlag(DownloadedTextureFlags.LinearColorSpace);
            set => SetFlags(DownloadedTextureFlags.LinearColorSpace, value);
        }

        void SetFlags(DownloadedTextureFlags flgs, bool add)
        {
            if (add)
                flags |= flgs;
            else
                flags &= ~flgs;
        }
    }

    ///<summary>A <see cref="DownloadHandler" /> subclass specialized for downloading images for use as <see cref="Texture" /> objects.</summary>
    ///<remarks>
    ///  <c>DownloadHandlerTexture</c> stores received data in a pre-allocated Unity <see cref="Texture" /> object. It's optimized for downloading images from web servers, and performs image decompression and decoding on a worker thread.
    ///
    ///This download handler stores received data in a buffer and on download completion decodes the data into valid Unity <see cref="Texture" /> objects. If the texture is destroyed, it can be created again by the same <c>DownloadHandlerTexture</c> object.
    ///
    ///The handler performs buffering, decompression and texture creation in native code. Additionally, decompression and texture creation are performed on a worker thread instead of the main thread, which can improve frame time when loading large textures.
    ///
    ///<c>DownloadHandlerTexture</c> only allocates managed memory when finally creating the Texture itself, which eliminates the garbage collection overhead associated with performing the byte-to-texture conversion in script.
    ///
    ///For use cases where you wish to download an image via HTTP and use it as a Texture within Unity, usage of this class is strongly recommended.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.UI;
    ///using UnityEngine.Networking;
    ///using System.Collections;
    ///
    ///[RequireComponent(typeof(Image))]
    ///public class ImageDownloader : MonoBehaviour {
    ///    Image _img;
    ///
    ///    void Start () {
    ///        _img = GetComponent<UnityEngine.UI.Image>();
    ///        Download("https://www.mysite.com/myimage.png");
    ///    }
    ///
    ///    public void Download(string url) {
    ///        StartCoroutine(LoadFromWeb(url));
    ///    }
    ///
    ///    IEnumerator LoadFromWeb(string url)
    ///    {
    ///        UnityWebRequest wr = new UnityWebRequest(url);
    ///        DownloadHandlerTexture texDl = new DownloadHandlerTexture(true);
    ///        wr.downloadHandler = texDl;
    ///        yield return wr.SendWebRequest();
    ///        if (wr.result == UnityWebRequest.Result.Success) {
    ///            Texture2D t = texDl.texture;
    ///            Sprite s = Sprite.Create(t, new Rect(0, 0, t.width, t.height),
    ///                Vector2.zero, 1f);
    ///            _img.sprite = s;
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [StructLayout(LayoutKind.Sequential)]
    [NativeHeader("Modules/UnityWebRequestTexture/Public/DownloadHandlerTexture.h")]
    public sealed class DownloadHandlerTexture : DownloadHandler
    {
        private NativeArray<byte> m_NativeData;

        private static extern IntPtr Create([UnityMarshalAs(NativeType.ScriptingObjectPtr)] DownloadHandlerTexture obj, DownloadedTextureParams parameters);

        private void InternalCreateTexture(DownloadedTextureParams parameters)
        {
            m_Ptr = Create(this, parameters);
        }

        ///<summary>Default constructor.</summary>
        ///<remarks>Convenience constructor. Assumes the value of <c>readable</c> is <c>false</c>. The <see cref="Texture" /> returned by <c>texture</c> will not have its texture data accessible from script.</remarks>
        ///<example>
        ///  <code><![CDATA[using System.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    IEnumerator Start()
        ///    {
        ///        using (var uwr = new UnityWebRequest("https://website.com/image.jpg", UnityWebRequest.kHttpVerbGET))
        ///        {
        ///            uwr.downloadHandler = new DownloadHandlerTexture();
        ///            yield return uwr.SendWebRequest();
        ///            GetComponent<Renderer>().material.mainTexture = DownloadHandlerTexture.GetContent(uwr);
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public DownloadHandlerTexture()
            : this(true)
        {
        }

        ///<summary>Constructor, allows <see cref="P:UnityEditor.TextureImporter.isReadable" /> property to be set.</summary>
        ///<remarks>The value in <c>readable</c> will be used to set the <see cref="P:UnityEditor.TextureImporter.isReadable" /> property when importing the downloaded texture data.</remarks>
        ///<param name="readable">Value to set for <see cref="P:UnityEditor.TextureImporter.isReadable" />.</param>
        public DownloadHandlerTexture(bool readable)
        {
            var parameters = DownloadedTextureParams.Default;
            parameters.readable = readable;
            InternalCreateTexture(parameters);
        }

        ///<summary>Constructor that allows you to specify the full set of supported properties when creating a texture from a downloaded image.</summary>
        ///<remarks>The value of <c>parameters</c> allows control of more properties of the texture. Refer to <see cref="Texture2D" /> for more information about texture properties.</remarks>
        ///<param name="parameters">Parameters specifying various properties of texture that will be created.</param>
        public DownloadHandlerTexture(DownloadedTextureParams parameters)
        {
            InternalCreateTexture(parameters);
        }

        ///<exclude />
        protected override NativeArray<byte> GetNativeData()
        {
            return InternalGetNativeArray(this, ref m_NativeData);
        }

        ///<exclude />
        public override void Dispose()
        {
            DisposeNativeArray(ref m_NativeData);
            base.Dispose();
        }

        ///<summary>Returns the downloaded <see cref="Texture" />, or <c>null</c>. (RO)</summary>
        ///<remarks>This property returns a <see cref="Texture" /> object. If Unity was unable to decode the downloaded data, or has not yet finished decompressing/decoding the downloaded data, this property will return <c>null</c>.
        ///
        ///If all data has not yet been downloaded, accessing this property will throw an &lt;a href="http://msdn.microsoft.com/en-us/library/system.invalidoperationexception"&gt;InvalidOperationException&lt;/a&gt;.
        ///
        ///Note: This property will return a reference to the same <see cref="Texture" /> object on every call. Accessing this property causes no additional memory allocation.</remarks>
        public Texture2D texture
        {
            get { return InternalGetTextureNative(); }
        }

        [NativeMethod(ThrowsException = true)]
        private extern Texture2D InternalGetTextureNative();

        ///<summary>Returns the downloaded <see cref="Texture" />, or <c>null</c>.</summary>
        ///<remarks>A static function provided for convenience; equivalent to ((DownloadHandlerTexture)www.downloadHandler).texture.</remarks>
        ///<param name="www">A finished UnityWebRequest object with <see cref="DownloadHandlerTexture" /> attached.</param>
        ///<returns>The same as <see cref="DownloadHandlerTexture.texture" /></returns>
        public static Texture2D GetContent(UnityWebRequest www)
        {
            return GetCheckedDownloader<DownloadHandlerTexture>(www).texture;
        }
        new internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(DownloadHandlerTexture handler) => handler.m_Ptr;
        }

    }
}
