// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine.Networking
{
    ///<summary>Helpers for downloading multimedia files using <see cref="UnityWebRequest" />.</summary>
    public static class UnityWebRequestMultimedia
    {
        ///<summary>Create a <see cref="UnityWebRequest" /> to download an audio clip via HTTP GET and create an <see cref="AudioClip" /> based on the retrieved data.</summary>
        ///<remarks>This method creates a <see cref="UnityWebRequest" /> and sets the target URL to the string <c>uri</c> argument. This method sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAudioClip" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerAudioClip" /> is a specialized <see cref="DownloadHandler" />. It is optimized for storing data which is to be used as an audio clip in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating an audio clip manually in script.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.</remarks>
        ///<param name="uri">The URI of the audio clip to download.</param>
        ///<param name="audioType">The type of audio encoding for the downloaded audio clip. See <see cref="AudioType" />.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an audio clip and convert it to an <see cref="AudioClip" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetAudioClip());
        ///    }
        ///
        ///    IEnumerator GetAudioClip()
        ///    {
        ///        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("https://www.my-server.com/audio.ogg", AudioType.OGGVORBIS))
        ///        {
        ///            yield return www.SendWebRequest();
        ///
        ///            if (www.result == UnityWebRequest.Result.ConnectionError)
        ///            {
        ///                Debug.Log(www.error);
        ///            }
        ///            else
        ///            {
        ///                AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAudioClip(string uri, AudioType audioType)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> to download an audio clip via HTTP GET and create an <see cref="AudioClip" /> based on the retrieved data.</summary>
        ///<remarks>This method creates a <see cref="UnityWebRequest" /> and sets the target URL to the string <c>uri</c> argument. This method sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAudioClip" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerAudioClip" /> is a specialized <see cref="DownloadHandler" />. It is optimized for storing data which is to be used as an audio clip in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating an audio clip manually in script.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.</remarks>
        ///<param name="uri">The URI of the audio clip to download.</param>
        ///<param name="audioType">The type of audio encoding for the downloaded audio clip. See <see cref="AudioType" />.</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an audio clip and convert it to an <see cref="AudioClip" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetAudioClip());
        ///    }
        ///
        ///    IEnumerator GetAudioClip()
        ///    {
        ///        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("https://www.my-server.com/audio.ogg", AudioType.OGGVORBIS))
        ///        {
        ///            yield return www.SendWebRequest();
        ///
        ///            if (www.result == UnityWebRequest.Result.ConnectionError)
        ///            {
        ///                Debug.Log(www.error);
        ///            }
        ///            else
        ///            {
        ///                AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAudioClip(Uri uri, AudioType audioType)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> to download an audio clip via HTTP GET and create an <see cref="AudioClip" /> based on the retrieved data.</summary>
        ///<remarks>This method creates a <see cref="UnityWebRequest" /> and sets the target URL to the string <c>uri</c> argument. This method sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAudioClip" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerAudioClip" /> is a specialized <see cref="DownloadHandler" />. It is optimized for storing data which is to be used as an audio clip in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating an audio clip manually in script.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.</remarks>
        ///<param name="uri">The URI of the audio clip to download.</param>
        ///<param name="audioType">The type of audio encoding for the downloaded audio clip. See <see cref="AudioType" />.</param>
        ///<param name="ambisonic">Set to true to mark the downloaded <see cref="AudioClip" /> as ambisonic. For more information, refer to [Ambisonic Audio](xref:um-ambisonic-audio).</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an audio clip and convert it to an <see cref="AudioClip" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetAudioClip());
        ///    }
        ///
        ///    IEnumerator GetAudioClip()
        ///    {
        ///        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("https://www.my-server.com/audio.ogg", AudioType.OGGVORBIS))
        ///        {
        ///            yield return www.SendWebRequest();
        ///
        ///            if (www.result == UnityWebRequest.Result.ConnectionError)
        ///            {
        ///                Debug.Log(www.error);
        ///            }
        ///            else
        ///            {
        ///                AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAudioClip(string uri, AudioType audioType, bool ambisonic)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType, ambisonic), null);
        }

        ///<summary>Create a <see cref="UnityWebRequest" /> to download an audio clip via HTTP GET and create an <see cref="AudioClip" /> based on the retrieved data.</summary>
        ///<remarks>This method creates a <see cref="UnityWebRequest" /> and sets the target URL to the string <c>uri</c> argument. This method sets no other flags or custom headers.
        ///
        ///This method attaches a <see cref="DownloadHandlerAudioClip" /> object to the <see cref="UnityWebRequest" />. <see cref="DownloadHandlerAudioClip" /> is a specialized <see cref="DownloadHandler" />. It is optimized for storing data which is to be used as an audio clip in the Unity Engine. Using this class significantly reduces memory reallocation compared to downloading raw bytes and creating an audio clip manually in script.
        ///
        ///This method attaches no <see cref="UploadHandler" /> to the <see cref="UnityWebRequest" />.</remarks>
        ///<param name="uri">The URI of the audio clip to download.</param>
        ///<param name="audioType">The type of audio encoding for the downloaded audio clip. See <see cref="AudioType" />.</param>
        ///<param name="ambisonic">Set to true to mark the downloaded <see cref="AudioClip" /> as ambisonic. For more information, refer to [Ambisonic Audio](xref:um-ambisonic-audio).</param>
        ///<returns>A <see cref="UnityWebRequest" /> properly configured to download an audio clip and convert it to an <see cref="AudioClip" />.</returns>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Networking;
        ///using System.Collections;
        ///
        ///public class MyBehaviour : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        StartCoroutine(GetAudioClip());
        ///    }
        ///
        ///    IEnumerator GetAudioClip()
        ///    {
        ///        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip("https://www.my-server.com/audio.ogg", AudioType.OGGVORBIS))
        ///        {
        ///            yield return www.SendWebRequest();
        ///
        ///            if (www.result == UnityWebRequest.Result.ConnectionError)
        ///            {
        ///                Debug.Log(www.error);
        ///            }
        ///            else
        ///            {
        ///                AudioClip myClip = DownloadHandlerAudioClip.GetContent(www);
        ///            }
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static UnityWebRequest GetAudioClip(Uri uri, AudioType audioType, bool ambisonic)
        {
            return new UnityWebRequest(uri, UnityWebRequest.kHttpVerbGET, new DownloadHandlerAudioClip(uri, audioType, ambisonic), null);
        }

    }
}
