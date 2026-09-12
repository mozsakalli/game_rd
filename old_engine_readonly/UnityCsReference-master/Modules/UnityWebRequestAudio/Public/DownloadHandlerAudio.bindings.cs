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
    ///<summary>A <see cref="DownloadHandler" /> subclass specialized for downloading audio data for use as <see cref="AudioClip" /> objects.</summary>
    ///<remarks>DownloadHandlerAudioClip stores received data in a pre-allocated Unity <see cref="AudioClip" /> object. It is optimized for downloading audio data from Web servers, and may perform audio data decompression and decoding on a worker thread.
    ///
    ///For use cases where you wish to download an audio clip via HTTP and use it as an <see cref="AudioClip" /> within Unity, usage of this class is strongly recommended.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using System.Collections;
    ///using UnityEngine;
    ///using UnityEngine.Networking;
    ///
    ///public class AudioDownloader : MonoBehaviour {
    ///
    ///    void Start () {
    ///        StartCoroutine(GetAudioClip());
    ///    }
    ///
    ///    IEnumerator GetAudioClip() {
    ///        using (var uwr = UnityWebRequestMultimedia.GetAudioClip("https://myserver.com/mysound.ogg", AudioType.OGGVORBIS)) {
    ///            yield return uwr.SendWebRequest();
    ///            if (uwr.result != UnityWebRequest.Result.Success) {
    ///                Debug.LogError(uwr.error);
    ///                yield break;
    ///            }
    ///
    ///            AudioClip clip = DownloadHandlerAudioClip.GetContent(uwr);
    ///            // use audio clip
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [StructLayout(LayoutKind.Sequential)]
    [NativeHeader("Modules/UnityWebRequestAudio/Public/DownloadHandlerAudioClip.h")]
    public sealed class DownloadHandlerAudioClip : DownloadHandler
    {
        private NativeArray<byte> m_NativeData;

        private extern static IntPtr Create([UnityMarshalAs(NativeType.ScriptingObjectPtr)] DownloadHandlerAudioClip obj, string url, AudioType audioType);

        private void InternalCreateAudioClip(string url, AudioType audioType)
        {
            m_Ptr = Create(this, url, audioType);
        }

        ///<summary>Constructor, specifies what kind of audio data is going to be downloaded.</summary>
        ///<remarks>The value in <c>audioType</c> will be used to parameterize the <see cref="AudioClip" /> when importing the downloaded audio data.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the audio clip is located.</param>
        ///<param name="audioType">Value to set for <see cref="AudioClip" /> type.</param>
        public DownloadHandlerAudioClip(string url, AudioType audioType)
        {
            InternalCreateAudioClip(url, audioType);
        }

        ///<summary>Constructor, specifies what kind of audio data is going to be downloaded.</summary>
        ///<remarks>The value in <c>audioType</c> will be used to parameterize the <see cref="AudioClip" /> when importing the downloaded audio data.</remarks>
        ///<param name="audioType">Value to set for <see cref="AudioClip" /> type.</param>
        ///<param name="uri">A System.Uri object identifying the audio clip resource.</param>
        public DownloadHandlerAudioClip(Uri uri, AudioType audioType)
        {
            InternalCreateAudioClip(uri.AbsoluteUri, audioType);
        }

        ///<summary>Constructor, specifies what kind of audio data is going to be downloaded.</summary>
        ///<remarks>The value in <c>audioType</c> will be used to parameterize the <see cref="AudioClip" /> when importing the downloaded audio data.</remarks>
        ///<param name="url">The nominal (pre-redirect) URL at which the audio clip is located.</param>
        ///<param name="audioType">Value to set for <see cref="AudioClip" /> type.</param>
        ///<param name="ambisonic">Set to true to mark the downloaded <see cref="AudioClip" /> as ambisonic.</param>
        public DownloadHandlerAudioClip(string url, AudioType audioType, bool ambisonic)
        {
            InternalCreateAudioClip(url, audioType);
            this.ambisonic = ambisonic;
        }

        ///<summary>Constructor, specifies what kind of audio data is going to be downloaded.</summary>
        ///<remarks>The value in <c>audioType</c> will be used to parameterize the <see cref="AudioClip" /> when importing the downloaded audio data.</remarks>
        ///<param name="audioType">Value to set for <see cref="AudioClip" /> type.</param>
        ///<param name="uri">A System.Uri object identifying the audio clip resource.</param>
        ///<param name="ambisonic">Set to true to mark the downloaded <see cref="AudioClip" /> as ambisonic.</param>
        public DownloadHandlerAudioClip(Uri uri, AudioType audioType, bool ambisonic)
        {
            InternalCreateAudioClip(uri.AbsoluteUri, audioType);
            this.ambisonic = ambisonic;
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

        ///<exclude />
        protected override string GetText()
        {
            throw new System.NotSupportedException("String access is not supported for audio clips");
        }

        ///<summary>Returns the downloaded <see cref="AudioClip" />, or <c>null</c>. (RO)</summary>
        ///<remarks>This property returns an <see cref="AudioClip" /> object. If Unity was unable to decode the downloaded data, or has not yet finished decompressing/decoding the downloaded data, this property will return <c>null</c>.
        ///
        ///If all data has not yet been downloaded, accessing this property will throw an &lt;a href="http://msdn.microsoft.com/en-us/library/system.invalidoperationexception"&gt;InvalidOperationException&lt;/a&gt;.
        ///
        ///Note: This property will return a reference to the same <see cref="AudioClip" /> object on every call. Accessing this property causes no additional memory allocation.
        ///
        ///Note: The <see cref="DownloadHandlerAudioClip.streamAudio">streamAudio</see>, <see cref="DownloadHandlerAudioClip.compressed">compressed</see>, and <see cref="DownloadHandlerAudioClip.ambisonic">ambisonic</see> properties are read when the clip is created. Changing them afterwards has no effect.</remarks>
        [NativeMethod(ThrowsException = true)]
        public extern AudioClip audioClip { get; }

        ///<summary>Create streaming <see cref="AudioClip" />.</summary>
        ///<remarks>Creates an AudioClip that can begin playback without needing the whole file to be downloaded. After starting the download, you must perform checks to determine that enough of your file has downloaded before attempting playback. To do this, poll <see cref="Networking.UnityWebRequest.downloadedBytes" /> to calculate an average download speed. Only begin playback after your checks confirm that the remainder of the file will finish downloading before the playback of your AudioClip finishes.
        ///
        ///Note: When streamAudio is true, it supersedes compression, and the download handler creates an AudioClip similar to an imported clip with the loadType <see cref="AudioClipLoadType.Streaming" />.
        ///
        ///Note: Changing this after the <see cref="AudioClip" /> has been created has no effect.</remarks>
        public extern bool streamAudio { get; set; }

        ///<summary>Create <see cref="AudioClip" /> that is compressed in memory.
        ///
        ///Note: When streamAudio is true, it supersedes compression, and the download handler creates an AudioClip similar to an imported clip with the loadType <see cref="AudioClipLoadType.Streaming" />.</summary>
        ///<remarks>See <see cref="AudioClipLoadType.CompressedInMemory" />.
        ///
        ///Note: Changing this after the <see cref="AudioClip" /> has been created has no effect.</remarks>
        public extern bool compressed { get; set; }

        ///<summary>Whether the downloaded <see cref="AudioClip" /> is marked as ambisonic.</summary>
        ///<remarks>When true, Unity treats the downloaded <see cref="AudioClip" /> as ambisonic audio. The project's ambisonic decoder plug-in then decodes the audio based on the orientation of the <see cref="AudioSource" /> and <see cref="AudioListener" />.
        ///
        ///You can set this property before accessing <see cref="DownloadHandlerAudioClip.audioClip">audioClip</see>, or pass <c>true</c> for the <c>ambisonic</c> parameter in the <see cref="DownloadHandlerAudioClip">constructor</see> or <see cref="UnityWebRequestMultimedia.GetAudioClip" />.
        ///
        ///Note: Changing this after the <see cref="AudioClip" /> has been created has no effect.
        ///
        ///For more information, refer to [Ambisonic Audio](xref:um-ambisonic-audio).</remarks>
        public extern bool ambisonic { get; set; }

        ///<summary>Returns the downloaded <see cref="AudioClip" />, or <c>null</c>.</summary>
        ///<remarks>A static function provided for convenience; equivalent to ((DownloadHandlerAudioClip)www.downloadHandler).audioClip.</remarks>
        ///<param name="www">A finished UnityWebRequest object with <see cref="DownloadHandlerAudioClip" /> attached.</param>
        ///<returns>The same as <see cref="DownloadHandlerAudioClip.audioClip" /></returns>
        public static AudioClip GetContent(UnityWebRequest www)
        {
            return GetCheckedDownloader<DownloadHandlerAudioClip>(www).audioClip;
        }
        new internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(DownloadHandlerAudioClip handler) => handler.m_Ptr;
        }

    }
}
