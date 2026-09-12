// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>The interface defining an animation job to use with an <see cref="IAnimationJobPlayable" />.</summary>
    ///<seealso cref="AnimationScriptPlayable.Create" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [JobProducerType(typeof(ProcessAnimationJobStruct<>))]
    public interface IAnimationJob
    {
        ///<summary>Defines what to do when processing the animation.</summary>
        ///<remarks>This method is called after <see cref="IAnimationJob.ProcessRootMotion" />. Depending on <see cref="Animator.cullingMode" />, it is possible this method won't be called.</remarks>
        ///<param name="stream">The animation stream to work on.</param>
        void ProcessAnimation(AnimationStream stream);
        ///<summary>Defines what to do when processing the root motion.</summary>
        ///<remarks>This method is called before <see cref="IAnimationJob.ProcessAnimation" />.</remarks>
        ///<param name="stream">The animation stream to work on.</param>
        void ProcessRootMotion(AnimationStream stream);
    }
}

