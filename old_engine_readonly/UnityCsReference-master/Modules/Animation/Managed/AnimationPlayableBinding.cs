// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Bindings;
using UnityEngine.Playables;

namespace UnityEngine.Animations
{
    ///<summary>A <see cref="PlayableBinding" /> that contains information representing an <see cref="AnimationPlayableOutput" />.</summary>
    public static class AnimationPlayableBinding
    {
        ///<summary>Creates a <see cref="PlayableBinding" /> that contains information representing an <see cref="AnimationPlayableOutput" />.</summary>
        ///<param name="name">The name of the AnimationPlayableOutput.</param>
        ///<param name="key">A reference to a <see cref="UnityEngine.Object" /> that acts as a key for this binding.</param>
        ///<returns>Returns a <see cref="PlayableBinding" /> that contains information that is used to create an <see cref="AnimationPlayableOutput" />.</returns>
        public static PlayableBinding Create(string name, UnityEngine.Object key)
        {
            return PlayableBinding.CreateInternal(name, key, typeof(Animator), CreateAnimationOutput);
        }

        private static PlayableOutput CreateAnimationOutput(PlayableGraph graph, string name)
        {
            return (PlayableOutput)AnimationPlayableOutput.Create(graph, name, null);
        }
    }
}
