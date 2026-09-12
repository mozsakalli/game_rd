// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Playables;

namespace UnityEngine.Audio
{
    ///<summary>A <see cref="IPlayableOutput" /> implementation that will be used to play audio.</summary>
    ///<remarks>NOTE: You can use <see cref="PlayableOutputExtensions" /> methods with AudioPlayableOutput objects.</remarks>
    [NativeHeader("Modules/Audio/Public/ScriptBindings/AudioPlayableOutput.bindings.h")]
    [NativeHeader("Modules/Audio/Public/Director/AudioPlayableOutput.h")]
    [NativeHeader("Modules/Audio/Public/AudioSource.h")]
    [StaticAccessor("AudioPlayableOutputBindings", StaticAccessorType.DoubleColon)]
    [RequiredByNativeCode]
    public struct AudioPlayableOutput : IPlayableOutput
    {
        private PlayableOutputHandle m_Handle;

        ///<summary>Creates an <see cref="AudioPlayableOutput" /> in the <see cref="PlayableGraph" />.</summary>
        ///<remarks>The <see cref="AudioSource" /> plays the source <see cref="Playable" /> of the <see cref="AudioPlayableOutput" />. This source Playable can be set with SetSourcePlayable.</remarks>
        ///<param name="graph">The <see cref="PlayableGraph" /> that will contain the <see cref="AudioPlayableOutput" />.</param>
        ///<param name="name">The name of the output.</param>
        ///<param name="target">The <see cref="AudioSource" /> that will play the <see cref="AudioPlayableOutput" /> source <see cref="Playable" />.</param>
        ///<returns>A new <see cref="AudioPlayableOutput" /> attached to the <see cref="PlayableGraph" />.</returns>
        public static AudioPlayableOutput Create(PlayableGraph graph, string name, AudioSource target)
        {
            PlayableOutputHandle handle;
            if (!AudioPlayableGraphExtensions.InternalCreateAudioOutput(ref graph, name, out handle))
                return AudioPlayableOutput.Null;

            AudioPlayableOutput output = new AudioPlayableOutput(handle);
            output.SetTarget(target);

            return output;
        }

        internal AudioPlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid())
            {
                if (!handle.IsPlayableOutputOfType<AudioPlayableOutput>())
                    throw new InvalidCastException("Can't set handle: the playable is not an AudioPlayableOutput.");
            }

            m_Handle = handle;
        }

        ///<summary>Returns an invalid AudioPlayableOutput.</summary>
        public static AudioPlayableOutput Null
        {
            get { return new AudioPlayableOutput(PlayableOutputHandle.Null); }
        }

        ///<exclude />
        public PlayableOutputHandle GetHandle()
        {
            return m_Handle;
        }

        ///<exclude />
        public static implicit operator PlayableOutput(AudioPlayableOutput output)
        {
            return new PlayableOutput(output.GetHandle());
        }

        ///<exclude />
        public static explicit operator AudioPlayableOutput(PlayableOutput output)
        {
            return new AudioPlayableOutput(output.GetHandle());
        }


        ///<exclude />
        public AudioSource GetTarget()
        {
            return InternalGetTarget(ref m_Handle);
        }

        ///<exclude />
        public void SetTarget(AudioSource value)
        {
            InternalSetTarget(ref m_Handle, value);
        }

        ///<summary>Gets the state of output playback when seeking.</summary>
        ///<returns>Returns true if the output plays when seeking. Returns false otherwise.</returns>
        ///<seealso cref="AudioPlayableOutput.SetEvaluateOnSeek" />
        public bool GetEvaluateOnSeek()
        {
            return InternalGetEvaluateOnSeek(ref m_Handle);
        }

        ///<summary>Controls whether the output should play when seeking.</summary>
        ///<param name="value">Set to true to play the output when seeking. Set to false to disable audio scrubbing on this output. Default is true.</param>
        ///<seealso cref="AudioPlayableOutput.GetEvaluateOnSeek" />
        public void SetEvaluateOnSeek(bool value)
        {
            InternalSetEvaluateOnSeek(ref m_Handle, value);
        }

        [NativeMethod(ThrowsException = true)]
        extern private static AudioSource InternalGetTarget(ref PlayableOutputHandle output);

        [NativeMethod(ThrowsException = true)]
        extern private static void InternalSetTarget(ref PlayableOutputHandle output, AudioSource target);

        [NativeMethod(ThrowsException = true)]
        extern private static bool InternalGetEvaluateOnSeek(ref PlayableOutputHandle output);

        [NativeMethod(ThrowsException = true)]
        extern private static void InternalSetEvaluateOnSeek(ref PlayableOutputHandle output, bool value);

    }
}
