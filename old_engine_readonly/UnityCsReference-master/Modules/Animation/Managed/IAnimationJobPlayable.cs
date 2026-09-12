// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>The interface defining an animation playable that uses <see cref="IAnimationJob" />.</summary>
    ///<seealso cref="AnimationScriptPlayable" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    public interface IAnimationJobPlayable : IPlayable
    {
        ///<summary>Gets the job data contained in the playable.</summary>
        ///<remarks>The generic type must be the same as the one used for the creation of the playable.</remarks>
        ///<returns>Returns the <see cref="IAnimationJob" /> data contained in the playable.</returns>
        T GetJobData<T>() where T : struct, IAnimationJob;
        ///<summary>Sets a new job data in the playable.</summary>
        ///<remarks>The generic type must be the same as the one used for the creation of the playable.</remarks>
        ///<param name="jobData">The new <see cref="IAnimationJob" /> data to set in the playable.</param>
        void SetJobData<T>(T jobData) where T : struct, IAnimationJob;
    }
}

