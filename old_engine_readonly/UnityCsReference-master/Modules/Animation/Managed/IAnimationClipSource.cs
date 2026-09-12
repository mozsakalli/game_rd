// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Playables;
using System.Collections.Generic;

namespace UnityEngine
{
    ///<summary>Use this interface to have a class provide its own list of Animation Clips to the Animation Window.  The class must inherit from <see cref="MonoBehaviour" />.</summary>
    ///<remarks>This interface requires an <see cref="Animator" /> or <see cref="Animation" /> component to preview Animation Clips in the Scene.  .</remarks>
    ///<seealso cref="M:UnityEditor.AnimationUtility.GetAnimationClips" />
    public interface IAnimationClipSource
    {
        ///<summary>Returns a list of Animation Clips.</summary>
        void GetAnimationClips(List<AnimationClip> results);
    }
}
