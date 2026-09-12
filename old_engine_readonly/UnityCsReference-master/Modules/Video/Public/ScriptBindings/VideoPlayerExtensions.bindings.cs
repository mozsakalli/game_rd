// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using UnityEngine.Bindings;
using UnityEngine.Video;
using UnityEngine.Experimental.Audio;

namespace UnityEngine.Experimental.Video
{
    ///<summary>Extension methods for the <see cref="UnityEngine.Video.VideoPlayer" /> class.</summary>
    [NativeHeader("Modules/Video/Public/ScriptBindings/VideoPlayerExtensions.bindings.h")]
    [NativeHeader("Modules/Video/Public/VideoPlayer.h")]
    [NativeHeader("VideoScriptingClasses.h")]
    [StaticAccessor("VideoPlayerExtensionsBindings", StaticAccessorType.DoubleColon)]
    public static class VideoPlayerExtensions
    {
        ///<summary>Return the <see cref="Experimental.Audio.AudioSampleProvider" /> for the specified track, used to receive audio samples during playback.</summary>
        ///<remarks>The sample provider can only be obtained when <see cref="UnityEngine.Video.VideoPlayer.audioOutputMode" /> is set to <see cref="UnityEngine.Video.VideoAudioOutputMode.APIOnly" />.</remarks>
        ///<param name="vp">The "this" pointer for the extension method.</param>
        ///<param name="trackIndex">The audio track index for which the sample provider is queried.</param>
        ///<returns>The sample provider for the specified track.</returns>
        public static AudioSampleProvider GetAudioSampleProvider(this VideoPlayer vp, ushort trackIndex)
        {
            var count = vp.controlledAudioTrackCount;
            if (trackIndex >= count)
                throw new ArgumentOutOfRangeException(
                    "trackIndex", trackIndex,
                    "VideoPlayer is currently configured with " + count + " tracks.");

            var mode = vp.audioOutputMode;
            if (mode != VideoAudioOutputMode.APIOnly)
                throw new InvalidOperationException(
                    "VideoPlayer.GetAudioSampleProvider requires audioOutputMode to be APIOnly. " +
                    "Current: " + mode);

            var provider = AudioSampleProvider.Lookup(
                vp.InternalGetAudioSampleProviderId(trackIndex), vp, trackIndex);

            if (provider == null)
                throw new InvalidOperationException(
                    "VideoPlayer.GetAudioSampleProvider got null provider.");

            if (provider.owner != vp)
                throw new InvalidOperationException(
                    "Internal error: VideoPlayer.GetAudioSampleProvider got provider used by another object.");

            if (provider.trackIndex != trackIndex)
                throw new InvalidOperationException(
                    "Internal error: VideoPlayer.GetAudioSampleProvider got provider for track " +
                    provider.trackIndex + " instead of " + trackIndex);

            return provider;
        }

        extern internal static uint InternalGetAudioSampleProviderId([NotNull] this VideoPlayer vp, ushort trackIndex);
    }
}

