// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Bindings;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace UnityEngine
{
    ///<summary>Allow recording the main output of the game or specific groups in the AudioMixer.</summary>
    [NativeHeader("Modules/Audio/Public/ScriptBindings/AudioRenderer.bindings.h")]
    public class AudioRenderer
    {
        ///<summary>Enters audio recording mode. After this Unity will output silence until <see cref="AudioRenderer.Stop" /> is called.</summary>
        ///<returns>True if the engine was switched into output recording mode. False if it is already recording.</returns>
        public static bool Start()
        {
            return Internal_AudioRenderer_Start();
        }

        ///<summary>Exits audio recording mode. After this audio output will be audible again.</summary>
        ///<returns>True if the engine was recording when this function was called.</returns>
        public static bool Stop()
        {
            return Internal_AudioRenderer_Stop();
        }

        ///<summary>Returns the number of samples available since the last time <see cref="AudioRenderer.Render" /> was called. This is dependent on the frame capture rate.</summary>
        ///<returns>Number of samples available since last recorded frame.</returns>
        public static int GetSampleCountForCaptureFrame()
        {
            return Internal_AudioRenderer_GetSampleCountForCaptureFrame();
        }

        // We should consider making this delegate-based in order to provide information like channel count and format. Also the term "sink" is quite audio-domain specific.
        unsafe internal static bool AddMixerGroupSink(AudioMixerGroup mixerGroup, NativeArray<float> buffer, bool excludeFromMix)
        {
            return Internal_AudioRenderer_AddMixerGroupSink(mixerGroup, buffer.GetUnsafePtr(), buffer.Length, excludeFromMix);
        }

        ///<summary>Performs the recording of the main output as well as any optional mixer groups that have been registered via <see cref="AudioRenderer.AddMixerGroupSink" />.</summary>
        ///<param name="buffer">The buffer to write the sample data to.</param>
        ///<returns>True if the recording succeeded.</returns>
        unsafe public static bool Render(NativeArray<float> buffer)
        {
            return Internal_AudioRenderer_Render(buffer.GetUnsafePtr(), buffer.Length);
        }

        internal static extern bool Internal_AudioRenderer_Start();
        internal static extern bool Internal_AudioRenderer_Stop();
        internal static extern int  Internal_AudioRenderer_GetSampleCountForCaptureFrame();
        unsafe internal static extern bool Internal_AudioRenderer_AddMixerGroupSink(AudioMixerGroup mixerGroup, void* ptr, int length, bool excludeFromMix);
        unsafe internal static extern bool Internal_AudioRenderer_Render(void* ptr, int length);
    }
}
