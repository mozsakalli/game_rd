// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using System.Runtime.InteropServices;

using UnityEngine.Bindings;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>Handle for a muscle in the <see cref="AnimationHumanStream" />.</summary>
    ///<remarks>MuscleHandle can only be used on <see cref="AnimationHumanStream" />, otherwise an <c>InvalidOperationException</c> is thrown.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Animations;
    ///using UnityEngine.Playables;
    ///
    ///public struct MuscleHandleExampleJob : IAnimationJob
    ///{
    ///    public MuscleHandle muscleHandle;
    ///
    ///    public void ProcessRootMotion(AnimationStream stream) {}
    ///    public void ProcessAnimation(AnimationStream stream)
    ///    {
    ///        AnimationHumanStream humanStream = stream.AsHuman();
    ///
    ///        // Get a muscle value.
    ///        float muscleValue = humanStream.GetMuscle(muscleHandle);
    ///
    ///        // Set a muscle value.
    ///        humanStream.SetMuscle(muscleHandle, muscleValue);
    ///    }
    ///}
    ///
    ///[RequireComponent(typeof(Animator))]
    ///public class MuscleHandleExample : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        var graph = PlayableGraph.Create();
    ///        var output = AnimationPlayableOutput.Create(graph, "output", GetComponent<Animator>());
    ///
    ///        var job = new MuscleHandleExampleJob();
    ///        job.muscleHandle = new MuscleHandle(HumanPartDof.LeftArm, ArmDof.HandDownUp);
    ///
    ///        var scriptPlayable = AnimationScriptPlayable.Create(graph, job);
    ///        output.SetSourcePlayable(scriptPlayable);
    ///
    ///        graph.Evaluate(1.0f);
    ///
    ///        graph.Destroy();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/Animator.h")] // -> dof enum
    [NativeHeader("Modules/Animation/MuscleHandle.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct MuscleHandle
    {
        ///<summary>The muscle human part. (RO)</summary>
        public HumanPartDof humanPartDof
        {
            get;
            private set;
        }
        ///<summary>The muscle human sub-part. (RO)</summary>
        public int dof
        {
            get;
            private set;
        }

        ///<summary>The different constructors that creates the muscle handle.</summary>
        ///<param name="bodyDof">The muscle body sub-part.</param>
        public MuscleHandle(BodyDof bodyDof)
        {
            humanPartDof = HumanPartDof.Body;
            dof = (int)bodyDof;
        }

        ///<summary>The different constructors that creates the muscle handle.</summary>
        ///<param name="headDof">The muscle head sub-part.</param>
        public MuscleHandle(HeadDof headDof)
        {
            humanPartDof = HumanPartDof.Head;
            dof = (int)headDof;
        }

        ///<summary>The different constructors that creates the muscle handle.</summary>
        ///<param name="partDof">The muscle human part.</param>
        ///<param name="legDof">The muscle leg sub-part.</param>
        public MuscleHandle(HumanPartDof partDof, LegDof legDof)
        {
            if (partDof != HumanPartDof.LeftLeg && partDof != HumanPartDof.RightLeg)
                throw new InvalidOperationException("Invalid HumanPartDof for a leg, please use either HumanPartDof.LeftLeg or HumanPartDof.RightLeg.");

            humanPartDof = partDof;
            dof = (int)legDof;
        }

        ///<summary>The different constructors that creates the muscle handle.</summary>
        ///<param name="partDof">The muscle human part.</param>
        ///<param name="armDof">The muscle arm sub-part.</param>
        public MuscleHandle(HumanPartDof partDof, ArmDof armDof)
        {
            if (partDof != HumanPartDof.LeftArm && partDof != HumanPartDof.RightArm)
                throw new InvalidOperationException("Invalid HumanPartDof for an arm, please use either HumanPartDof.LeftArm or HumanPartDof.RightArm.");

            humanPartDof = partDof;
            dof = (int)armDof;
        }

        ///<summary>The different constructors that creates the muscle handle.</summary>
        ///<param name="partDof">The muscle human part.</param>
        ///<param name="fingerDof">The muscle finger sub-part.</param>
        public MuscleHandle(HumanPartDof partDof, FingerDof fingerDof)
        {
            if (partDof < HumanPartDof.LeftThumb || partDof > HumanPartDof.RightLittle)
                throw new InvalidOperationException("Invalid HumanPartDof for a finger.");

            humanPartDof = partDof;
            dof = (int)fingerDof;
        }

        ///<summary>The name of the muscle. (RO)</summary>
        public string name
        {
            get { return GetName(); }
        }

        ///<summary>The total number of DoF parts in a humanoid. (RO)</summary>
        public static int muscleHandleCount
        {
            get { return GetMuscleHandleCount(); }
        }

        ///<summary>Fills the array with all the possible muscle handles on a humanoid.</summary>
        ///<param name="muscleHandles">An array of MuscleHandle.</param>
        public extern static void GetMuscleHandles([NotNull][Out] MuscleHandle[] muscleHandles);

        private extern string GetName();

        private extern static int GetMuscleHandleCount();
    }
}

