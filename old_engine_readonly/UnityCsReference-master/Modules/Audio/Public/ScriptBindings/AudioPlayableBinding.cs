// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Bindings;
using UnityEngine.Playables;

namespace UnityEngine.Audio
{
    ///<summary>A <see cref="PlayableBinding" /> that contains information representing an <see cref="AudioPlayableOutput" />.</summary>
    public static class AudioPlayableBinding
    {
        ///<summary>Creates a <see cref="PlayableBinding" /> that contains information representing an <see cref="AudioPlayableOutput" />.</summary>
        ///<param name="key">A reference to a <see cref="UnityEngine.Object" /> that acts as a key for this binding.</param>
        ///<param name="name">The name of the AudioPlayableOutput.</param>
        ///<returns>Returns a <see cref="PlayableBinding" /> that contains information that is used to create an <see cref="AudioPlayableOutput" />.</returns>
        public static PlayableBinding Create(string name, UnityEngine.Object key)
        {
            return PlayableBinding.CreateInternal(name, key, typeof(AudioSource), CreateAudioOutput);
        }

        private static PlayableOutput CreateAudioOutput(PlayableGraph graph, string name)
        {
            return (PlayableOutput)AudioPlayableOutput.Create(graph, name, null);
        }
    }
}
