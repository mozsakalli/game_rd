// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.Bindings;
using UnityEngine.Experimental.Rendering;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System;

namespace UnityEngine
{
    ///<summary>This class provides utility and extension methods to convert image data to and from PNG, JPEG and EXR formats, and to encode image data to TGA format.</summary>
    [NativeHeader("Modules/ImageConversion/ScriptBindings/ImageConversion.bindings.h")]
    public static class ImageConversion
    {
        ///<summary>Enables legacy PNG runtime import behavior.</summary>
        ///<remarks>In previous versions of Unity, texture data from all PNG
        ///textures containing a gAMA block was returned in gamma 2.0 space. If you want to retain
        ///this old behavior, for example when working with older projects that dynamically load
        ///textures using <see cref="ImageConversion.LoadImage" /> or <c>Texture2D.LoadImage</c>, set this flag to <c>true</c>.</remarks>
        public static bool EnableLegacyPngGammaRuntimeLoadBehavior
        {
            get
            {
                return GetEnableLegacyPngGammaRuntimeLoadBehavior();
            }
            set
            {
                SetEnableLegacyPngGammaRuntimeLoadBehavior(value);
            }
        }

        [NativeMethod(Name = "ImageConversionBindings::GetEnableLegacyPngGammaRuntimeLoadBehavior", IsFreeFunction = true, ThrowsException = false)]
        extern private static bool GetEnableLegacyPngGammaRuntimeLoadBehavior();

        [NativeMethod(Name = "ImageConversionBindings::SetEnableLegacyPngGammaRuntimeLoadBehavior", IsFreeFunction = true, ThrowsException = false)]
        extern private static void SetEnableLegacyPngGammaRuntimeLoadBehavior(bool enable);

        ///<summary>Encodes the specified texture in TGA format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the TGA file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///
        ///This function does not work on any compressed format.
        ///<see cref="Texture.isReadable" /> must be <c>true</c>.
        ///The encoded TGA data will be uncompressed 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded TGA data will be in 8-bit grayscale.</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">The texture to encode.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as TGA file.
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.IO;
        ///
        ///public class TGAScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenTGA();
        ///    }
        ///
        ///    IEnumerator SaveScreenTGA()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the texture in TGA format
        ///        byte[] bytes = ImageConversion.EncodeToTGA(tex);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.tga", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Texture2D.ReadPixels" />
        ///<seealso cref="WaitForEndOfFrame" />
        ///<seealso cref="LoadImage" />
        ///<seealso cref="EncodeArrayToTGA" />
        ///<seealso cref="EncodeNativeArrayToTGA" />
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeToEXR" />
        [NativeMethod(Name = "ImageConversionBindings::EncodeToTGA", IsFreeFunction = true, ThrowsException = true)]
        extern public static byte[] EncodeToTGA(this Texture2D tex);

        ///<summary>Encodes this texture into PNG format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the PNG file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///This function does not work on any compressed format.
        ///<see cref="Texture.isReadable" /> must be <c>true</c>.
        ///
        ///The encoded PNG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded PNG data will be in grayscale.
        ///PNG data will not contain gamma correction or color profile information.</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">The texture to convert.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as PNG file.
        ///using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///using System.IO;
        ///
        ///public class PNGUploader : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return UploadPNG();
        ///    }
        ///
        ///    IEnumerator UploadPNG()
        ///    {
        ///        // We should only read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture the size of the screen, RGB24 format
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode texture into PNG
        ///        byte[] bytes = ImageConversion.EncodeToPNG(tex);
        ///        Object.Destroy(tex);
        ///
        ///        // For testing purposes, also write to a file in the project folder
        ///        // File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", bytes);
        ///
        ///
        ///        // Create a Web Form
        ///        WWWForm form = new WWWForm();
        ///        form.AddField("frameCount", Time.frameCount.ToString());
        ///        form.AddBinaryData("fileUpload", bytes);
        ///
        ///        // Upload to a cgi script
        ///        var w = UnityWebRequest.Post("http://localhost/cgi-bin/env.cgi?post", form);
        ///        yield return w.SendWebRequest();
        ///
        ///        if (w.result != UnityWebRequest.Result.Success)
        ///        {
        ///            Debug.Log(w.error);
        ///        }
        ///        else
        ///        {
        ///            Debug.Log("Finished Uploading Screenshot");
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Texture2D.ReadPixels" />
        ///<seealso cref="WaitForEndOfFrame" />
        ///<seealso cref="LoadImage" />
        ///<seealso cref="EncodeArrayToPNG" />
        ///<seealso cref="EncodeNativeArrayToPNG" />
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeToTGA" />
        ///<seealso cref="EncodeToEXR" />
        [NativeMethod(Name = "ImageConversionBindings::EncodeToPNG", IsFreeFunction = true, ThrowsException = true)]
        extern public static byte[] EncodeToPNG(this Texture2D tex);

        ///<summary>Encodes this texture into JPG format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the JPG file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///This function does not work on any compressed format.
        ///<see cref="Texture.isReadable" /> must be <c>true</c>.
        ///The encoded JPG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">Text texture to convert.</param>
        ///<param name="quality">JPG quality to encode with. The range is 1 through 100. 1 is the lowest quality. The default is 75.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as JPG file.
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.IO;
        ///
        ///public class JPGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenJPG();
        ///    }
        ///
        ///    IEnumerator SaveScreenJPG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the texture in JPG format
        ///        byte[] bytes = ImageConversion.EncodeToJPG(tex);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.jpg", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeToTGA" />
        ///<seealso cref="EncodeToEXR" />
        [NativeMethod(Name = "ImageConversionBindings::EncodeToJPG", IsFreeFunction = true, ThrowsException = true)]
        extern public static byte[] EncodeToJPG(this Texture2D tex, int quality);
        ///<summary>Encodes this texture into JPG format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the JPG file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///This function does not work on any compressed format.
        ///<see cref="Texture.isReadable" /> must be <c>true</c>.
        ///The encoded JPG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">Text texture to convert.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as JPG file.
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.IO;
        ///
        ///public class JPGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenJPG();
        ///    }
        ///
        ///    IEnumerator SaveScreenJPG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the texture in JPG format
        ///        byte[] bytes = ImageConversion.EncodeToJPG(tex);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.jpg", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeToTGA" />
        ///<seealso cref="EncodeToEXR" />
        public static byte[] EncodeToJPG(this Texture2D tex)
        {
            return tex.EncodeToJPG(75);
        }

        ///<summary>Encodes this texture into the EXR format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the EXR file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    Although it is best to use this function for HDR texture formats (either 16-bit or 32-bit floats), it can be used with other formats (and the data is converted on the fly).
        ///                    The default output format is uncompressed 16-bit float EXR and can be controlled using the passed in flags.
        ///                    For the texture pass in, <see cref="Texture.isReadable" /> must be <c>true</c>.
        ///                    The encoded EXR data will only contain an alpha channel when the passed-in format has one. For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded data will be in grayscale mode.
        ///
        ///</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">The texture to convert.</param>
        ///<param name="flags">Flags used to control compression and the output format. The default is <see cref="Texture2D.EXRFlags.None" /></param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves HDR RenderTexture as an EXR file.
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.IO;
        ///
        ///public class SaveRenderTextureToEXR : MonoBehaviour
        ///{
        ///    RenderTexture m_InputTexture;
        ///
        ///    void SaveRenderTexture()
        ///    {
        ///        if (m_InputTexture != null)
        ///        {
        ///            int width = m_InputTexture.width;
        ///            int height = m_InputTexture.height;
        ///
        ///            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        ///
        ///            // Read screen contents into the texture
        ///            Graphics.SetRenderTarget(m_InputTexture);
        ///            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///            tex.Apply();
        ///
        ///            // Encode texture into the EXR
        ///            byte[] bytes = ImageConversion.EncodeToEXR(tex, Texture2D.EXRFlags.CompressZIP);
        ///            File.WriteAllBytes(Application.dataPath + "/../SavedRenderTexture.exr", bytes);
        ///
        ///            Object.DestroyImmediate(tex);
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="Texture2D.EXRFlags" />
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeArrayToEXR" />
        ///<seealso cref="EncodeNativeArrayToEXR" />
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeToTGA" />
        [NativeMethod(Name = "ImageConversionBindings::EncodeToEXR", IsFreeFunction = true, ThrowsException = true)]
        extern public static byte[] EncodeToEXR(this Texture2D tex, Texture2D.EXRFlags flags);
        public static byte[] EncodeToEXR(this Texture2D tex)
        {
            return EncodeToEXR(tex, Texture2D.EXRFlags.None);
        }

        [NativeMethod(Name = "ImageConversionBindings::EncodeToR2D", IsFreeFunction = true, ThrowsException = true)]
        extern internal static byte[] EncodeToR2DInternal(this Texture2D tex);

        [NativeMethod(Name = "ImageConversionBindings::LoadImage", IsFreeFunction = true)]
        extern public static bool LoadImage([NotNull] this Texture2D tex, ReadOnlySpan<byte> data, bool markNonReadable);
        public static bool LoadImage(this Texture2D tex, ReadOnlySpan<byte> data) => tex.LoadImage(data, false);
        ///<summary>Loads PNG, JPG or EXR image byte array into a texture.</summary>
        ///<remarks>
        ///  <para>The LoadImage function replaces texture contents with new image data. This function can also change texture
        ///size and format. JPG files are loaded into <see cref="TextureFormat.RGB24" /> format,
        ///PNG files are loaded into <see cref="TextureFormat.ARGB32" /> format, and EXR files are loaded into <see cref="TextureFormat.RGBAFloat" />.
        ///If texture format before calling LoadImage is <see cref="TextureFormat.DXT1" /> or <see cref="TextureFormat.DXT5" />,
        ///then the loaded image will be DXT5-compressed for JPG and PNG images. EXR images and/or any other compression format will result in
        ///an uncompressed image. Unity returns false if your platform cannot use the compressed format on the GPU. Use <see cref="SystemInfo.IsFormatSupported" /> to check if your platform supports a format. 
        ///
        ///Loading an EXR image is only supported on PC, Mac and Linux. Unity can load both tiled and untiled EXR images, but doesn't support the following features:
        ///
        ///- Interpreting channel names and layers. Unity interprets the channels as ABGR if there are four channels, as BGR with full opacity if there are three channels, and as Y (grayscale) if there's one channel. For example, Unity reads an EXR texture with a single channel named "heightmap" as a grayscale image, stores channels named "X", "Y" and "Z" in the blue, green and red channels respectively, interprets "Y-RY-BY" as RGB data instead of as a luminance/chroma image, and doesn't treat layers with channel names like "leftView.R" differently.
        ///- Embedded mipmaps. Unity generates mipmap levels from the full-resolution image instead.
        ///- Multipart images.
        ///- Deep images.
        ///- Chromaticity coordinates.
        ///
        ///Texture will be uploaded to the GPU automatically; there's no need to call <see cref="Texture2D.Apply" />.
        ///
        ///This function loads the texture data without gamma correction. If the texture data uses the sRGB
        ///color space, you must use an sRGB <see cref="Texture2D" /> object for correct rendering results. Likewise,
        ///if the texture data uses the linear color space, then you must use a linear <see cref="Texture2D" /> object.
        ///(A <see cref="Texture2D" /> object is sRGB if its <c>linear</c> constructor parameter was <c>false</c>, which is the
        ///default, and  linear if the parameter was set to <c>true</c>.)
        ///
        ///**Note:** In previous versions of Unity, texture data from all PNG textures containing a gAMA block
        ///was returned in gamma 2.0 space. If you want to retain this old behavior, for example when working
        ///with older projects that dynamically load textures using <see cref="LoadImage" />, set
        ///<see cref="ImageConversion.EnableLegacyPngGammaRuntimeLoadBehavior" /> to <c>true</c>.</para>
        ///  <para />
        ///</remarks>
        ///<param name="tex">The texture to load the image into.</param>
        ///<param name="data">The byte array containing the image data to load.</param>
        ///<param name="markNonReadable">Set to false by default, pass true to optionally mark the texture as non-readable.</param>
        ///<returns>Returns true if the data can be loaded, false otherwise.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    // Load a .jpg or .png file by adding .bytes extensions to the file
        ///    // and dragging it on the imageAsset variable.
        ///    public TextAsset imageAsset;
        ///    public void Start()
        ///    {
        ///        // Create a texture. Texture size does not matter, since
        ///        // LoadImage will replace with the size of the incoming image.
        ///        Texture2D tex = new Texture2D(2, 2);
        ///        ImageConversion.LoadImage(tex, imageAsset.bytes);
        ///        GetComponent<Renderer>().material.mainTexture = tex;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class ExampleScript : MonoBehaviour
        ///{
        ///    public void Start()
        ///    {
        ///        // Create a texture. Texture size does not matter, since
        ///        // LoadImage will replace with the size of the incoming image.
        ///        Texture2D tex = new Texture2D(2, 2);
        ///        // A small 64x64 Unity logo encoded into a PNG.
        ///        byte[] pngBytes = new byte[]
        ///        {
        ///            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        ///            0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x40, 0x08, 0x00, 0x00, 0x00, 0x00, 0x8F, 0x02, 0x2E,
        ///            0x02, 0x00, 0x00, 0x01, 0x57, 0x49, 0x44, 0x41, 0x54, 0x78, 0x01, 0xA5, 0x57, 0xD1, 0xAD, 0xC4,
        ///            0x30, 0x08, 0x83, 0x81, 0x32, 0x4A, 0x66, 0xC9, 0x36, 0x99, 0x85, 0x45, 0xBC, 0x4E, 0x74, 0xBD,
        ///            0x8F, 0x9E, 0x5B, 0xD4, 0xE8, 0xF1, 0x6A, 0x7F, 0xDD, 0x29, 0xB2, 0x55, 0x0C, 0x24, 0x60, 0xEB,
        ///            0x0D, 0x30, 0xE7, 0xF9, 0xF3, 0x85, 0x40, 0x74, 0x3F, 0xF0, 0x52, 0x00, 0xC3, 0x0F, 0xBC, 0x14,
        ///            0xC0, 0xF4, 0x0B, 0xF0, 0x3F, 0x01, 0x44, 0xF3, 0x3B, 0x3A, 0x05, 0x8A, 0x41, 0x67, 0x14, 0x05,
        ///            0x18, 0x74, 0x06, 0x4A, 0x02, 0xBE, 0x47, 0x54, 0x04, 0x86, 0xEF, 0xD1, 0x0A, 0x02, 0xF0, 0x84,
        ///            0xD9, 0x9D, 0x28, 0x08, 0xDC, 0x9C, 0x1F, 0x48, 0x21, 0xE1, 0x4F, 0x01, 0xDC, 0xC9, 0x07, 0xC2,
        ///            0x2F, 0x98, 0x49, 0x60, 0xE7, 0x60, 0xC7, 0xCE, 0xD3, 0x9D, 0x00, 0x22, 0x02, 0x07, 0xFA, 0x41,
        ///            0x8E, 0x27, 0x4F, 0x31, 0x37, 0x02, 0xF9, 0xC3, 0xF1, 0x7C, 0xD2, 0x16, 0x2E, 0xE7, 0xB6, 0xE5,
        ///            0xB7, 0x9D, 0xA7, 0xBF, 0x50, 0x06, 0x05, 0x4A, 0x7C, 0xD0, 0x3B, 0x4A, 0x2D, 0x2B, 0xF3, 0x97,
        ///            0x93, 0x35, 0x77, 0x02, 0xB8, 0x3A, 0x9C, 0x30, 0x2F, 0x81, 0x83, 0xD5, 0x6C, 0x55, 0xFE, 0xBA,
        ///            0x7D, 0x19, 0x5B, 0xDA, 0xAA, 0xFC, 0xCE, 0x0F, 0xE0, 0xBF, 0x53, 0xA0, 0xC0, 0x07, 0x8D, 0xFF,
        ///            0x82, 0x89, 0xB4, 0x1A, 0x7F, 0xE5, 0xA3, 0x5F, 0x46, 0xAC, 0xC6, 0x0F, 0xBA, 0x96, 0x1C, 0xB1,
        ///            0x12, 0x7F, 0xE5, 0x33, 0x26, 0xD2, 0x4A, 0xFC, 0x41, 0x07, 0xB3, 0x09, 0x56, 0xE1, 0xE3, 0xA1,
        ///            0xB8, 0xCE, 0x3C, 0x5A, 0x81, 0xBF, 0xDA, 0x43, 0x73, 0x75, 0xA6, 0x71, 0xDB, 0x7F, 0x0F, 0x29,
        ///            0x24, 0x82, 0x95, 0x08, 0xAF, 0x21, 0xC9, 0x9E, 0xBD, 0x50, 0xE6, 0x47, 0x12, 0x38, 0xEF, 0x03,
        ///            0x78, 0x11, 0x2B, 0x61, 0xB4, 0xA5, 0x0B, 0xE8, 0x21, 0xE8, 0x26, 0xEA, 0x69, 0xAC, 0x17, 0x12,
        ///            0x0F, 0x73, 0x21, 0x29, 0xA5, 0x2C, 0x37, 0x93, 0xDE, 0xCE, 0xFA, 0x85, 0xA2, 0x5F, 0x69, 0xFA,
        ///            0xA5, 0xAA, 0x5F, 0xEB, 0xFA, 0xC3, 0xA2, 0x3F, 0x6D, 0xFA, 0xE3, 0xAA, 0x3F, 0xEF, 0xFA, 0x80,
        ///            0xA1, 0x8F, 0x38, 0x04, 0xE2, 0x8B, 0xD7, 0x43, 0x96, 0x3E, 0xE6, 0xE9, 0x83, 0x26, 0xE1, 0xC2,
        ///            0xA8, 0x2B, 0x0C, 0xDB, 0xC2, 0xB8, 0x2F, 0x2C, 0x1C, 0xC2, 0xCA, 0x23, 0x2D, 0x5D, 0xFA, 0xDA,
        ///            0xA7, 0x2F, 0x9E, 0xFA, 0xEA, 0xAB, 0x2F, 0xDF, 0xF2, 0xFA, 0xFF, 0x01, 0x1A, 0x18, 0x53, 0x83,
        ///            0xC1, 0x4E, 0x14, 0x1B, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
        ///        };
        ///        // Load data into the texture.
        ///        ImageConversion.LoadImage(tex, pngBytes);
        ///
        ///        // Assign texture to renderer's material.
        ///        GetComponent<Renderer>().material.mainTexture = tex;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="Texture2D.LoadRawTextureData" />
        public static bool LoadImage(this Texture2D tex, byte[] data, bool markNonReadable) => tex.LoadImage(new ReadOnlySpan<byte>(data), markNonReadable);
        public static bool LoadImage(this Texture2D tex, byte[] data) => tex.LoadImage(new ReadOnlySpan<byte>(data), false);

        [FreeFunctionAttribute("ImageConversionBindings::EncodeArrayToTGA", true)]
        extern internal static byte[] EncodeArrayToTGA_Internal(Span<byte> span, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);
        ///<summary>Encodes this array into TGA format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the TGA file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded TGA data will be uncompressed 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///                    For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded TGA data will be in 8-bit grayscale.
        ///
        ///                    This method is thread safe.</para>
        ///  <para />
        ///</remarks>
        ///<param name="array">The byte array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as TGA file.
        ///using System.Collections;
        ///using System.IO;
        ///using UnityEngine;
        ///
        ///public class TGAScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenTGA();
        ///    }
        ///
        ///    IEnumerator SaveScreenTGA()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in TGA format
        ///        byte[] bytes = ImageConversion.EncodeArrayToTGA(tex.GetRawTextureData(), tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.tga", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToTGA" />
        ///<seealso cref="EncodeNativeArrayToTGA" />
        ///<seealso cref="EncodeArrayToPNG" />
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeArrayToEXR" />
        public static byte[] EncodeArrayToTGA(Array array, GraphicsFormat format, uint width, uint height, uint rowBytes = 0)
        {
            var elemSize = UnsafeUtility.SizeOf(array.GetType().GetElementType());
            int dataLen = array.Length;
            return EncodeArrayToTGA_Internal(UnsafeUtility.GetByteSpanFromArray(array, dataLen, elemSize), format, width, height, rowBytes);
        }

        [FreeFunctionAttribute("ImageConversionBindings::EncodeArrayToPNG", true)]
        extern internal static byte[] EncodeArrayToPNG_Internal(Span<byte> span, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);
        ///<summary>Encodes this array into PNG format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the PNG file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded PNG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///                    For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded PNG data will be in grayscale.
        ///                    PNG data will not contain gamma correction or color profile information.
        ///
        ///                    This method is thread safe.</para>
        ///  <para />
        ///</remarks>
        ///<param name="array">The byte array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as PNG file.
        ///using System.Collections;
        ///using System.IO;
        ///using UnityEngine;
        ///
        ///public class PNGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenPNG();
        ///    }
        ///
        ///    IEnumerator SaveScreenPNG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in PNG format
        ///        byte[] bytes = ImageConversion.EncodeArrayToPNG(tex.GetRawTextureData(), tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeNativeArrayToPNG" />
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeArrayToTGA" />
        ///<seealso cref="EncodeArrayToEXR" />
        public static byte[] EncodeArrayToPNG(Array array, GraphicsFormat format, uint width, uint height, uint rowBytes = 0)
        {
            var elemSize = UnsafeUtility.SizeOf(array.GetType().GetElementType());
            int dataLen = array.Length;
            return EncodeArrayToPNG_Internal(UnsafeUtility.GetByteSpanFromArray(array, dataLen, elemSize), format, width, height, rowBytes);
        }

        [FreeFunctionAttribute("ImageConversionBindings::EncodeArrayToJPG", true)]
        extern internal static byte[] EncodeArrayToJPG_Internal(Span<byte> span, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75);
        ///<summary>Encodes this array into JPG format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the JPG file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded JPG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///
        ///                    This method is thread safe.</para>
        ///  <para />
        ///</remarks>
        ///<param name="array">The byte array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<param name="quality">JPG quality to encode with. The range is 1 through 100. 1 is the lowest quality. The default is 75.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as JPG file.
        ///using System.Collections;
        ///using System.IO;
        ///using UnityEngine;
        ///
        ///public class JPGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenJPG();
        ///    }
        ///
        ///    IEnumerator SaveScreenJPG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in JPG format
        ///        byte[] bytes = ImageConversion.EncodeArrayToJPG(tex.GetRawTextureData(), tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.jpg", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeArrayToPNG" />
        ///<seealso cref="EncodeArrayToTGA" />
        ///<seealso cref="EncodeArrayToEXR" />
        public static byte[] EncodeArrayToJPG(Array array, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75)
        {
            var elemSize = UnsafeUtility.SizeOf(array.GetType().GetElementType());
            int dataLen = array.Length;
            return EncodeArrayToJPG_Internal(UnsafeUtility.GetByteSpanFromArray(array, dataLen, elemSize), format, width, height, rowBytes, quality);
        }

        [FreeFunctionAttribute("ImageConversionBindings::EncodeArrayToEXR", true)]
        extern internal static byte[] EncodeArrayToEXR_Internal(Span<byte> span, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, Texture2D.EXRFlags flags = Texture2D.EXRFlags.None);
        ///<summary>Encodes this array into the EXR format.</summary>
        ///<remarks>
        ///  <para>This function returns a byte array which is the EXR file data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    Although it is best to use this function for HDR texture formats (either 16-bit or 32-bit floats), it can be used with other formats (and the data is converted on the fly).
        ///                    The default output format is uncompressed 16-bit float EXR and can be controlled using the passed in flags.
        ///                    The encoded EXR data will only contain an alpha channel when the passed-in format has one. For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded data will be in grayscale mode.
        ///
        ///                    This method is thread safe.</para>
        ///  <para />
        ///</remarks>
        ///<param name="array">The byte array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<param name="flags">Flags used to control compression and the output format. The default is <see cref="Texture2D.EXRFlags.None" /></param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as EXR file.
        ///using System.Collections;
        ///using System.IO;
        ///using UnityEngine;
        ///
        ///public class EXRScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenEXR();
        ///    }
        ///
        ///    IEnumerator SaveScreenEXR()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGBAFloat format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in EXR format
        ///        byte[] bytes = ImageConversion.EncodeArrayToEXR(tex.GetRawTextureData(), tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.exr", bytes);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToEXR" />
        ///<seealso cref="EncodeNativeArrayToEXR" />
        ///<seealso cref="EncodeArrayToPNG" />
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeArrayToTGA" />
        public static byte[] EncodeArrayToEXR(Array array, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, Texture2D.EXRFlags flags = Texture2D.EXRFlags.None)
        {
            var elemSize = UnsafeUtility.SizeOf(array.GetType().GetElementType());
            int dataLen = array.Length;
            return EncodeArrayToEXR_Internal(UnsafeUtility.GetByteSpanFromArray(array, dataLen, elemSize), format, width, height, rowBytes, flags);
        }

        [FreeFunctionAttribute("ImageConversionBindings::EncodeArrayToR2D", true)]
        extern internal static byte[] EncodeArrayToR2DInternal(Span<byte> span, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);

        ///<summary>Encodes this native array into TGA format.</summary>
        ///<remarks>
        ///  <para>This function returns a NativeArray&lt;byte&gt; which is the TGA data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded TGA data will be uncompressed 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///                    For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded TGA data will be in 8-bit grayscale.
        ///
        ///                    The native array that this function returns is allocated with the persistent allocator, so this function should only be called from the main thread.</para>
        ///  <para />
        ///</remarks>
        ///<param name="input">The native array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as TGA file.
        ///using System.Collections;
        ///using System.IO;
        ///using Unity.Collections;
        ///using UnityEngine;
        ///
        ///public class TGAScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenTGA();
        ///    }
        ///
        ///    IEnumerator SaveScreenTGA()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in TGA format
        ///        NativeArray<byte> imageBytes = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Temp);
        ///        var bytes = ImageConversion.EncodeNativeArrayToTGA(imageBytes, tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.tga", bytes.ToArray());
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToTGA" />
        ///<seealso cref="EncodeArrayToTGA" />
        ///<seealso cref="EncodeNativeArrayToPNG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToEXR" />
        public static NativeArray<byte> EncodeNativeArrayToTGA<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0) where T : struct
        {
            unsafe
            {
                var size   = input.Length * UnsafeUtility.SizeOf<T>();
                var result = UnsafeEncodeNativeArrayToTGA(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(input), ref size, format, width, height, rowBytes);
                var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(result, size, Allocator.Persistent);
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
                return output;
            }
        }

        ///<summary>Encodes this native array into PNG format.</summary>
        ///<remarks>
        ///  <para>This function returns a NativeArray&lt;byte&gt; which is the PNG data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded PNG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///                    For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded PNG data will be in grayscale.
        ///
        ///                    PNG data will not contain gamma correction or color profile information.
        ///
        ///                    The native array that this function returns is allocated with the persistent allocator, so this function should only be called from the main thread.</para>
        ///  <para />
        ///</remarks>
        ///<param name="input">The native array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as PNG file.
        ///using System.Collections;
        ///using System.IO;
        ///using Unity.Collections;
        ///using UnityEngine;
        ///
        ///public class PNGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenPNG();
        ///    }
        ///
        ///    IEnumerator SaveScreenPNG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in PNG format
        ///        NativeArray<byte> imageBytes = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Temp);
        ///        var bytes = ImageConversion.EncodeNativeArrayToPNG(imageBytes, tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.png", bytes.ToArray());
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToPNG" />
        ///<seealso cref="EncodeArrayToPNG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToTGA" />
        ///<seealso cref="EncodeNativeArrayToEXR" />
        public static NativeArray<byte> EncodeNativeArrayToPNG<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0) where T : struct
        {
            unsafe
            {
                var size   = input.Length * UnsafeUtility.SizeOf<T>();
                var result = UnsafeEncodeNativeArrayToPNG(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(input), ref size, format, width, height, rowBytes);
                var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(result, size, Allocator.Persistent);
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
                return output;
            }
        }

        ///<summary>Encodes this native array into JPG format.</summary>
        ///<remarks>
        ///  <para>This function returns a NativeArray&lt;byte&gt; which is the JPG data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///
        ///                    This function does not work on any compressed format.
        ///                    The encoded JPG data will be either 8bit grayscale, RGB or RGBA (depending on the passed in format).
        ///
        ///                    The native array that this function returns is allocated with the persistent allocator, so this function should only be called from the main thread.</para>
        ///  <para />
        ///</remarks>
        ///<param name="input">The native array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<param name="quality">JPG quality to encode with. The range is 1 through 100. 1 is the lowest quality. The default is 75.</param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as JPG file.
        ///using System.Collections;
        ///using System.IO;
        ///using Unity.Collections;
        ///using UnityEngine;
        ///
        ///public class JPGScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenJPG();
        ///    }
        ///
        ///    IEnumerator SaveScreenJPG()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGB24 format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in JPG format
        ///        NativeArray<byte> imageBytes = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Temp);
        ///        var bytes = ImageConversion.EncodeNativeArrayToJPG(imageBytes, tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.jpg", bytes.ToArray());
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToJPG" />
        ///<seealso cref="EncodeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToPNG" />
        ///<seealso cref="EncodeNativeArrayToTGA" />
        ///<seealso cref="EncodeNativeArrayToEXR" />
        public static NativeArray<byte> EncodeNativeArrayToJPG<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75) where T : struct
        {
            unsafe
            {
                var size   = input.Length * UnsafeUtility.SizeOf<T>();
                var result = UnsafeEncodeNativeArrayToJPG(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(input), ref size, format, width, height, rowBytes, quality);
                var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(result, size, Allocator.Persistent);
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
                return output;
            }
        }

        ///<summary>Encodes this native array into the EXR format.</summary>
        ///<remarks>
        ///  <para>This function returns a NativeArray&lt;byte&gt; which is the EXR data. You can store the encoded data as a file or send it over the network without further processing.
        ///
        ///                    This function does not work on any compressed format.
        ///                    Although it is best to use this function for HDR texture formats (either 16-bit or 32-bit floats), it can be used with other formats (and the data is converted on the fly).
        ///                    The default output format is uncompressed 16-bit float EXR and can be controlled using the passed in flags.
        ///                    The encoded EXR data will only contain an alpha channel when the passed-in format has one. For single-channel red textures ( <c>R8</c>, <c>R16</c>, <c>RFloat</c> and <c>RHalf</c> ), the encoded data will be in grayscale mode.
        ///
        ///                    The native array that this function returns is allocated with the persistent allocator, so this function should only be called from the main thread.</para>
        ///  <para />
        ///</remarks>
        ///<param name="input">The native array to convert.</param>
        ///<param name="format">The pixel format of the image data.</param>
        ///<param name="width">The width of the image data in pixels.</param>
        ///<param name="height">The height of the image data in pixels.</param>
        ///<param name="rowBytes">The length of a single row in bytes. The default is 0, which means Unity calculates the length automatically.</param>
        ///<param name="flags">Flags used to control compression and the output format. The default is <see cref="Texture2D.EXRFlags.None" /></param>
        ///<example>
        ///  <code><![CDATA[
        /// // Saves screenshot as EXR file.
        ///using System.Collections;
        ///using System.IO;
        ///using Unity.Collections;
        ///using UnityEngine;
        ///
        ///public class EXRScreenSaver : MonoBehaviour
        ///{
        ///    // Take a shot immediately
        ///    IEnumerator Start()
        ///    {
        ///        yield return SaveScreenEXR();
        ///    }
        ///
        ///    IEnumerator SaveScreenEXR()
        ///    {
        ///        // Read the screen buffer after rendering is complete
        ///        yield return new WaitForEndOfFrame();
        ///
        ///        // Create a texture in RGBAFloat format the size of the screen
        ///        int width = Screen.width;
        ///        int height = Screen.height;
        ///        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBAFloat, false);
        ///
        ///        // Read the screen contents into the texture
        ///        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        ///        tex.Apply();
        ///
        ///        // Encode the bytes in EXR format
        ///        NativeArray<byte> imageBytes = new NativeArray<byte>(tex.GetRawTextureData(), Allocator.Temp);
        ///        var bytes = ImageConversion.EncodeNativeArrayToEXR(imageBytes, tex.graphicsFormat, (uint)width, (uint)height);
        ///        Object.Destroy(tex);
        ///
        ///        // Write the returned byte array to a file in the project folder
        ///        File.WriteAllBytes(Application.dataPath + "/../SavedScreen.exr", bytes.ToArray());
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="EncodeToEXR" />
        ///<seealso cref="EncodeArrayToEXR" />
        ///<seealso cref="EncodeNativeArrayToPNG" />
        ///<seealso cref="EncodeNativeArrayToJPG" />
        ///<seealso cref="EncodeNativeArrayToTGA" />
        public static NativeArray<byte> EncodeNativeArrayToEXR<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, Texture2D.EXRFlags flags = Texture2D.EXRFlags.None) where T : struct
        {
            unsafe
            {
                var size   = input.Length * UnsafeUtility.SizeOf<T>();
                var result = UnsafeEncodeNativeArrayToEXR(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(input), ref size, format, width, height, rowBytes, flags);
                var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(result, size, Allocator.Persistent);
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
                return output;
            }
        }

        internal static NativeArray<byte> EncodeNativeArrayToR2DInternal<T>(NativeArray<T> input, GraphicsFormat format, uint width, uint height, uint rowBytes = 0) where T : struct
        {
            unsafe
            {
                var size   = input.Length * UnsafeUtility.SizeOf<T>();
                var result = UnsafeEncodeNativeArrayToR2D(NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks<T>(input), ref size, format, width, height, rowBytes);
                var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(result, size, Allocator.Persistent);
                var safety = AtomicSafetyHandle.Create();
                NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
                AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
                return output;
            }
        }

        [FreeFunctionAttribute("ImageConversionBindings::UnsafeEncodeNativeArrayToTGA", true)]
        unsafe extern static void* UnsafeEncodeNativeArrayToTGA(void* array, ref int sizeInBytes, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);

        [FreeFunctionAttribute("ImageConversionBindings::UnsafeEncodeNativeArrayToPNG", true)]
        unsafe extern static void* UnsafeEncodeNativeArrayToPNG(void* array, ref int sizeInBytes, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);

        [FreeFunctionAttribute("ImageConversionBindings::UnsafeEncodeNativeArrayToJPG", true)]
        unsafe extern static void* UnsafeEncodeNativeArrayToJPG(void* array, ref int sizeInBytes, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, int quality = 75);

        [FreeFunctionAttribute("ImageConversionBindings::UnsafeEncodeNativeArrayToEXR", true)]
        unsafe extern static void* UnsafeEncodeNativeArrayToEXR(void* array, ref int sizeInBytes, GraphicsFormat format, uint width, uint height, uint rowBytes = 0, Texture2D.EXRFlags flags = Texture2D.EXRFlags.None);

        [FreeFunctionAttribute("ImageConversionBindings::UnsafeEncodeNativeArrayToR2D", true)]
        unsafe extern static void* UnsafeEncodeNativeArrayToR2D(void* array, ref int sizeInBytes, GraphicsFormat format, uint width, uint height, uint rowBytes = 0);

        [NativeMethod(Name = "ImageConversionBindings::LoadImageAtPathInternal", IsFreeFunction = true, IsThreadSafe = true, ThrowsException = true)]
        unsafe extern static void* LoadImageAtPathInternal(string path, ref int width, ref int height, ref int rowBytes, ref GraphicsFormat format);
        unsafe internal static NativeArray<byte> LoadImageDataAtPath(string path, ref int width, ref int height, ref int rowBytes, ref GraphicsFormat format)
        {
            var buffer = LoadImageAtPathInternal(path, ref width, ref height, ref rowBytes, ref format);
            var size = height * rowBytes;
            var output = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(buffer, size, Allocator.Persistent);
            var safety = AtomicSafetyHandle.Create();
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref output, safety);
            AtomicSafetyHandle.SetAllowReadOrWriteAccess(safety, true);
            return output;
        }

    }
}
