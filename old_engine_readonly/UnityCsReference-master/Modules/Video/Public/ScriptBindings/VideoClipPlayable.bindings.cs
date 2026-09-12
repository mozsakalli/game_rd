// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using System.ComponentModel;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Playables;
using UnityEngine.Video;

using UnityObject = UnityEngine.Object;

namespace UnityEngine.Experimental.Video
{
    ///<summary>An implementation of <see cref="IPlayable" /> that controls playback of a <see cref="VideoClip" />.</summary>
    ///<remarks>NOTE: You can use <see cref="PlayableExtensions" /> methods with VideoClipPlayable objects.</remarks>
    [NativeHeader("Modules/Video/Public/ScriptBindings/VideoClipPlayable.bindings.h")]
    [NativeHeader("Modules/Video/Public/Director/VideoClipPlayable.h")]
    [NativeHeader("Modules/Video/Public/VideoClip.h")]
    [NativeHeader("Runtime/Director/Core/HPlayable.h")]
    [StaticAccessor("VideoClipPlayableBindings", StaticAccessorType.DoubleColon)]
    [RequiredByNativeCode]
    public struct VideoClipPlayable : IPlayable, IEquatable<VideoClipPlayable>
    {
        PlayableHandle m_Handle;

        ///<summary>Creates a <see cref="VideoClipPlayable" /> in the <see cref="PlayableGraph" />.</summary>
        ///<param name="graph">The <see cref="PlayableGraph" /> object that will own the <see cref="VideoClipPlayable" />.</param>
        ///<param name="looping">Indicates if <see cref="VideoClip" /> loops when it reaches the end.</param>
        ///<param name="clip">
        ///  <see cref="VideoClip" /> used to produce textures in the <see cref="PlayableGraph" />.</param>
        ///<returns>A <see cref="VideoClipPlayable" /> linked to the <see cref="PlayableGraph" />.</returns>
        public static VideoClipPlayable Create(PlayableGraph graph, VideoClip clip, bool looping)
        {
            var handle = CreateHandle(graph, clip, looping);
            var playable = new VideoClipPlayable(handle);
            if (clip != null)
                playable.SetDuration(clip.length);
            return playable;
        }

        private static PlayableHandle CreateHandle(PlayableGraph graph, VideoClip clip, bool looping)
        {
            PlayableHandle handle = PlayableHandle.Null;
            if (!InternalCreateVideoClipPlayable(ref graph, clip, looping, ref handle))
                return PlayableHandle.Null;
            return handle;
        }

        internal VideoClipPlayable(PlayableHandle handle)
        {
            if (handle.IsValid())
            {
                if (!handle.IsPlayableOfType<VideoClipPlayable>())
                    throw new InvalidCastException("Can't set handle: the playable is not an VideoClipPlayable.");
            }

            m_Handle = handle;
        }

        ///<exclude />
        public PlayableHandle GetHandle()
        {
            return m_Handle;
        }

        ///<exclude />
        public static implicit operator Playable(VideoClipPlayable playable)
        {
            return new Playable(playable.GetHandle());
        }

        ///<exclude />
        public static explicit operator VideoClipPlayable(Playable playable)
        {
            return new VideoClipPlayable(playable.GetHandle());
        }

        ///<exclude />
        public bool Equals(VideoClipPlayable other)
        {
            return GetHandle() == other.GetHandle();
        }


        ///<exclude />
        public VideoClip GetClip()
        {
            return GetClipInternal(ref m_Handle);
        }

        ///<exclude />
        public void SetClip(VideoClip value)
        {
            SetClipInternal(ref m_Handle, value);
        }

        ///<exclude />
        public bool GetLooped()
        {
            return GetLoopedInternal(ref m_Handle);
        }

        ///<exclude />
        public void SetLooped(bool value)
        {
            SetLoopedInternal(ref m_Handle, value);
        }

        ///<exclude />
        public bool IsPlaying()
        {
            return GetIsPlayingInternal(ref m_Handle);
        }

        ///<exclude />
        public double GetStartDelay()
        {
            return GetStartDelayInternal(ref m_Handle);
        }

        internal void SetStartDelay(double value)
        {
            ValidateStartDelayInternal(value);
            SetStartDelayInternal(ref m_Handle, value);
        }

        ///<exclude />
        public double GetPauseDelay()
        {
            return GetPauseDelayInternal(ref m_Handle);
        }

        internal void GetPauseDelay(double value)
        {
            double currentDelay = GetPauseDelayInternal(ref m_Handle);
            //FIXME Chanage 0.5 from arbitrary value to something that is dependent on sample rate and dsp buffer size
            if (m_Handle.GetPlayState() == PlayState.Playing &&
                (value < 0.05 || (currentDelay != 0.0 && currentDelay < 0.05)))
                throw new ArgumentException("VideoClipPlayable.pauseDelay: Setting new delay when existing delay is too small or 0.0 ("
                    + currentDelay + "), Video system will not be able to change in time");

            SetPauseDelayInternal(ref m_Handle, value);
        }

        ///<exclude />
        public void Seek(double startTime, double startDelay)
        {
            Seek(startTime, startDelay, 0);
        }

        public void Seek(double startTime, double startDelay, [DefaultValue("0")] double duration)
        {
            ValidateStartDelayInternal(startDelay);
            SetStartDelayInternal(ref m_Handle, startDelay);
            if (duration > 0)
            {
                // playable duration is the local time (without speed modifier applied)
                //  that it stops, since it will not advance time until the delay is complete
                m_Handle.SetDuration(duration + startTime);
                // start delay is offset by the length of the clip that plays
                SetPauseDelayInternal(ref m_Handle, startDelay + duration);
            }
            else
            {
                m_Handle.SetDuration(double.MaxValue);
                SetPauseDelayInternal(ref m_Handle, 0);
            }

            m_Handle.SetTime(startTime);
            m_Handle.Play();
        }

        private void ValidateStartDelayInternal(double startDelay)
        {
            double currentDelay = GetStartDelayInternal(ref m_Handle);

            //FIXME Chanage 0.5 from arbitrary value to something that is dependent on sample rate and dsp buffer size
            const double validEndDelay = 0.05;
            const double validStartDelay = 0.00001; // for double/float errors

            if (IsPlaying() &&
                (startDelay < validEndDelay || (currentDelay >= validStartDelay && currentDelay < validEndDelay)))
            {
                Debug.LogWarning("VideoClipPlayable.StartDelay: Setting new delay when existing delay is too small or 0.0 ("
                    + currentDelay + "), Video system will not be able to change in time");
            }
        }

        [NativeMethod(ThrowsException = true)]
        extern private static VideoClip GetClipInternal(ref PlayableHandle hdl);

        [NativeMethod(ThrowsException = true)]
        extern private static void SetClipInternal(ref PlayableHandle hdl, VideoClip clip);

        [NativeMethod(ThrowsException = true)]
        extern private static bool GetLoopedInternal(ref PlayableHandle hdl);

        [NativeMethod(ThrowsException = true)]
        extern private static void SetLoopedInternal(ref PlayableHandle hdl, bool looped);

        [NativeMethod(ThrowsException = true)]
        extern private static bool GetIsPlayingInternal(ref PlayableHandle hdl);

        [NativeMethod(ThrowsException = true)]
        extern private static double GetStartDelayInternal(ref PlayableHandle hdl);

        [NativeMethod(ThrowsException = true)]
        extern private static void SetStartDelayInternal(ref PlayableHandle hdl, double delay);

        [NativeMethod(ThrowsException = true)]
        extern private static double GetPauseDelayInternal(ref PlayableHandle hdl);

        [NativeMethod(ThrowsException = true)]
        extern private static void SetPauseDelayInternal(ref PlayableHandle hdl, double delay);

        [NativeMethod(ThrowsException = true)]
        extern private static bool InternalCreateVideoClipPlayable(ref PlayableGraph graph, VideoClip clip, bool looping, ref PlayableHandle handle);

        [NativeMethod(ThrowsException = true)]
        extern private static bool ValidateType(ref PlayableHandle hdl);

    }
}
