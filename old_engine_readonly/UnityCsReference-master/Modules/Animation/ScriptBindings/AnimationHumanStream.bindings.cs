// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;

using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>The humanoid stream of animation data passed from one <see cref="T:UnityEngine.Playables.Playable" /> to another.</summary>
    ///<remarks>The AnimationHumanStream structure is passed through the animation <see cref="T:UnityEngine.Playables.Playable" /> structures, like <see cref="AnimationClipPlayable" /> and <see cref="AnimationMixerPlayable" />. They can be modified when used with an <see cref="IAnimationJobPlayable" />, like the <see cref="AnimationScriptPlayable" />.
    ///
    ///The Playables implementing <see cref="IAnimationJobPlayable" /> take a custom C# job, which must implement <see cref="IAnimationJob" />, and the AnimationHumanStream is then passed to its callbacks during the animation processing pass.</remarks>
    ///<seealso cref="AnimationStream" />
    ///<seealso cref="AnimationStream.isHumanStream" />
    ///<seealso cref="AnimationStream.AsHuman()" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationHumanStream.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationHumanStream.h")]
    [RequiredByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationHumanStream
    {
        private System.IntPtr stream;

        ///<summary>Returns <c>true</c> if the stream is valid; <c>false</c> otherwise. (RO)</summary>
        public bool isValid
        {
            get { return stream != System.IntPtr.Zero; }
        }

        private void ThrowIfInvalid()
        {
            if (!isValid)
                throw new InvalidOperationException("The AnimationHumanStream is invalid.");
        }

        ///<summary>The scale of the Avatar. (RO)</summary>
        public float humanScale { get { ThrowIfInvalid(); return GetHumanScale(); } }

        ///<summary>The left foot height from the floor. (RO)</summary>
        ///<remarks>The foot height is the distance between the ankle and the floor computed when the avatar was configured.</remarks>
        public float leftFootHeight { get { ThrowIfInvalid(); return GetFootHeight(true); } }

        ///<summary>The right foot height from the floor. (RO)</summary>
        ///<remarks>The foot height is the distance between the ankle and the floor computed when the avatar was configured.</remarks>
        public float rightFootHeight { get { ThrowIfInvalid(); return GetFootHeight(false); } }

        ///<summary>The position of the body center of mass relative to the root.</summary>
        public Vector3 bodyLocalPosition
        {
            get { ThrowIfInvalid(); return InternalGetBodyLocalPosition(); }
            set { ThrowIfInvalid(); InternalSetBodyLocalPosition(value); }
        }

        ///<summary>The rotation of the body center of mass relative to the root.</summary>
        public Quaternion bodyLocalRotation
        {
            get { ThrowIfInvalid(); return InternalGetBodyLocalRotation(); }
            set { ThrowIfInvalid(); InternalSetBodyLocalRotation(value); }
        }

        ///<summary>The position of the body center of mass in world space.</summary>
        public Vector3 bodyPosition
        {
            get { ThrowIfInvalid(); return InternalGetBodyPosition(); }
            set { ThrowIfInvalid(); InternalSetBodyPosition(value); }
        }

        ///<summary>The rotation of the body center of mass in world space.</summary>
        public Quaternion bodyRotation
        {
            get { ThrowIfInvalid(); return InternalGetBodyRotation(); }
            set { ThrowIfInvalid(); InternalSetBodyRotation(value); }
        }

        ///<summary>Returns the muscle value.</summary>
        ///<param name="muscle">The Muscle that is queried.</param>
        ///<returns>The muscle value.</returns>
        ///<seealso cref="MuscleHandle" />
        public float GetMuscle(MuscleHandle muscle) { ThrowIfInvalid(); return InternalGetMuscle(muscle); }
        ///<summary>Sets the muscle value.</summary>
        ///<param name="muscle">The Muscle that is queried.</param>
        ///<param name="value">The muscle value.</param>
        ///<seealso cref="MuscleHandle" />
        public void  SetMuscle(MuscleHandle muscle, float value) { ThrowIfInvalid(); InternalSetMuscle(muscle, value); }

        ///<summary>The left foot velocity from the last evaluated frame. (RO)</summary>
        public Vector3 leftFootVelocity  { get { ThrowIfInvalid(); return GetLeftFootVelocity(); } }
        ///<summary>The right foot velocity from the last evaluated frame. (RO)</summary>
        public Vector3 rightFootVelocity { get { ThrowIfInvalid(); return GetRightFootVelocity(); } }

        // IK goals
        ///<summary>Reset the current pose to the stance pose (T Pose).</summary>
        public void ResetToStancePose() { ThrowIfInvalid(); InternalResetToStancePose(); }

        ///<summary>Returns the position of this IK goal in world space computed from the stream current pose.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The position of this IK goal.</returns>
        public Vector3 GetGoalPositionFromPose(AvatarIKGoal index) { ThrowIfInvalid(); return InternalGetGoalPositionFromPose(index); }
        ///<summary>Returns the rotation of this IK goal in world space computed from the stream current pose.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The rotation of this IK goal.</returns>
        public Quaternion GetGoalRotationFromPose(AvatarIKGoal index) { ThrowIfInvalid(); return InternalGetGoalRotationFromPose(index); }

        ///<summary>Returns the position of this IK goal relative to the root.</summary>
        ///<remarks>Some playable like <see cref="AnimationClipPlayable" /> can generate IK goals. This function can be used to retrieve goal generate from a previous playable in the graph.</remarks>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The position of this IK goal.</returns>
        public Vector3 GetGoalLocalPosition(AvatarIKGoal index)                 { ThrowIfInvalid(); return InternalGetGoalLocalPosition(index); }
        ///<summary>Sets the position of this IK goal relative to the root.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="pos">The position of this IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalLocalPosition(AvatarIKGoal index,  Vector3 pos)      { ThrowIfInvalid(); InternalSetGoalLocalPosition(index, pos); }
        ///<summary>Returns the rotation of this IK goal relative to the root.</summary>
        ///<remarks>Some playable like <see cref="AnimationClipPlayable" /> can generate IK goals. This function can be used to retrieve goal generate from a previous playable in the graph.</remarks>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The rotation of this IK goal.</returns>
        public Quaternion GetGoalLocalRotation(AvatarIKGoal index)              { ThrowIfInvalid(); return InternalGetGoalLocalRotation(index); }
        ///<summary>Sets the rotation of this IK goal relative to the root.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="rot">The rotation of this IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalLocalRotation(AvatarIKGoal index, Quaternion rot)    { ThrowIfInvalid(); InternalSetGoalLocalRotation(index, rot); }
        ///<summary>Returns the position of this IK goal in world space.</summary>
        ///<remarks>Some playable like <see cref="AnimationClipPlayable" /> can generate IK goals. This function can be used to retrieve goal generate from a previous playable in the graph.</remarks>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The position of this IK goal.</returns>
        public Vector3 GetGoalPosition(AvatarIKGoal index)                      { ThrowIfInvalid(); return InternalGetGoalPosition(index); }
        ///<summary>Sets the position of this IK goal in world space.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="pos">The position of this IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalPosition(AvatarIKGoal index,  Vector3 pos)           { ThrowIfInvalid(); InternalSetGoalPosition(index, pos); }
        ///<summary>Returns the rotation of this IK goal in world space.</summary>
        ///<remarks>Some playable like <see cref="AnimationClipPlayable" /> can generate IK goals. This function can be used to retrieve goal generate from a previous playable in the graph.</remarks>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The rotation of this IK goal.</returns>
        public Quaternion GetGoalRotation(AvatarIKGoal index)                   { ThrowIfInvalid(); return InternalGetGoalRotation(index); }
        ///<summary>Sets the rotation of this IK goal in world space.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="rot">The rotation of this IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalRotation(AvatarIKGoal index, Quaternion rot)         { ThrowIfInvalid(); InternalSetGoalRotation(index, rot); }
        ///<summary>Sets the position weight of the IK goal.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="value">The position weight of the IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalWeightPosition(AvatarIKGoal index, float value)      { ThrowIfInvalid(); InternalSetGoalWeightPosition(index, value); }
        ///<summary>Sets the rotation weight of the IK goal.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<param name="value">The rotation weight of the IK goal.</param>
        ///<seealso cref="SolveIK" />
        public void SetGoalWeightRotation(AvatarIKGoal index, float value)      { ThrowIfInvalid(); InternalSetGoalWeightRotation(index, value); }
        ///<summary>Returns the position weight of the IK goal.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The position weight of the IK goal.</returns>
        public float GetGoalWeightPosition(AvatarIKGoal index)                  { ThrowIfInvalid(); return InternalGetGoalWeightPosition(index); }
        ///<summary>Returns the rotation weight of the IK goal.</summary>
        ///<param name="index">The AvatarIKGoal that is queried.</param>
        ///<returns>The rotation weight of the IK goal.</returns>
        public float GetGoalWeightRotation(AvatarIKGoal index)                  { ThrowIfInvalid(); return InternalGetGoalWeightRotation(index); }

        // IK Hints
        ///<summary>Returns the position of this IK Hint in world space.</summary>
        ///<param name="index">The AvatarIKHint that is queried.</param>
        ///<returns>The position of this IK Hint.</returns>
        public Vector3 GetHintPosition(AvatarIKHint index)                      { ThrowIfInvalid(); return InternalGetHintPosition(index); }
        ///<summary>Sets the position of this IK hint in world space.</summary>
        ///<param name="index">The AvatarIKHint that is queried.</param>
        ///<param name="pos">The position of this IK hint.</param>
        ///<seealso cref="SolveIK" />
        public void SetHintPosition(AvatarIKHint index,  Vector3 pos)           { ThrowIfInvalid(); InternalSetHintPosition(index, pos); }
        ///<summary>Sets the position weight of the IK Hint.</summary>
        ///<param name="index">The AvatarIKHint that is queried.</param>
        ///<param name="value">The position weight of the IK Hint.</param>
        ///<seealso cref="SolveIK" />
        public void SetHintWeightPosition(AvatarIKHint index, float value)      { ThrowIfInvalid(); InternalSetHintWeightPosition(index, value); }
        ///<summary>Returns the position weight of the IK Hint.</summary>
        ///<param name="index">The AvatarIKHint that is queried.</param>
        ///<returns>The position weight of the IK Hint.</returns>
        public float GetHintWeightPosition(AvatarIKHint index)                  { ThrowIfInvalid(); return InternalGetHintWeightPosition(index); }

        // Lookat
        ///<summary>Sets the look at position in world space.</summary>
        ///<param name="lookAtPosition">The look at position.</param>
        ///<seealso cref="SolveIK" />
        ///<seealso cref="SetLookAtBodyWeight" />
        ///<seealso cref="SetLookAtHeadWeight" />
        ///<seealso cref="SetLookAtEyesWeight" />
        ///<seealso cref="SetLookAtClampWeight" />
        public void SetLookAtPosition(Vector3 lookAtPosition)                   { ThrowIfInvalid(); InternalSetLookAtPosition(lookAtPosition); }
        ///<summary>Sets the LookAt clamp weight.</summary>
        ///<remarks>0.0 means the character is unrestrained in motion. 1.0 means the character is clamped (look at becomes impossible). 0.5 means the character is able to move on half of the possible range (180 degrees).</remarks>
        ///<param name="weight">The LookAt clamp weight.</param>
        ///<seealso cref="SolveIK" />
        ///<seealso cref="SetLookAtPosition" />
        public void SetLookAtClampWeight(float weight)                          { ThrowIfInvalid(); InternalSetLookAtClampWeight(weight); }
        ///<summary>Sets the LookAt body weight.</summary>
        ///<remarks>Determines how much the body is involved in the LookAt. Rotates the body transform so the forward vector points at LookAt position.</remarks>
        ///<param name="weight">The LookAt body weight.</param>
        ///<seealso cref="SolveIK" />
        ///<seealso cref="SetLookAtPosition" />
        public void SetLookAtBodyWeight(float weight)                           { ThrowIfInvalid(); InternalSetLookAtBodyWeight(weight); }
        ///<summary>Sets the LookAt head weight.</summary>
        ///<remarks>Determines how much the head is involved in the LookAt. Rotates the head transform so the forward vector points at LookAt position.</remarks>
        ///<param name="weight">The LookAt head weight.</param>
        ///<seealso cref="SolveIK" />
        ///<seealso cref="SetLookAtPosition" />
        public void SetLookAtHeadWeight(float weight)                           { ThrowIfInvalid(); InternalSetLookAtHeadWeight(weight); }
        ///<summary>Sets the LookAt eyes weight.</summary>
        ///<remarks>Determines how much the eyes are involved in the LookAt. Rotates the eyes transforms so the forward vector points at LookAt position.</remarks>
        ///<param name="weight">The LookAt eyes weight.</param>
        ///<seealso cref="SolveIK" />
        ///<seealso cref="SetLookAtPosition" />
        public void SetLookAtEyesWeight(float weight)                           { ThrowIfInvalid(); InternalSetLookAtEyesWeight(weight); }
        ///<summary>Execute the IK solver.</summary>
        ///<remarks>The humanoid IK solver is executed using the IK goal position, rotation, and weight currently set in the <see cref="AnimationHumanStream" />.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Playables;
        ///using UnityEngine.Animations;
        ///
        ///public struct IKJob : IAnimationJob
        ///{
        ///    public TransformSceneHandle effector;
        ///    public PropertySceneHandle positionWeight;
        ///    public PropertySceneHandle rotationWeight;
        ///
        ///    public void ProcessRootMotion(AnimationStream stream) {}
        ///
        ///    public void ProcessAnimation(AnimationStream stream)
        ///    {
        ///        AnimationHumanStream humanStream = stream.AsHuman();
        ///        if (effector.IsValid(stream) && positionWeight.IsValid(stream) && rotationWeight.IsValid(stream))
        ///        {
        ///            humanStream.SetGoalPosition(AvatarIKGoal.LeftFoot, effector.GetPosition(stream));
        ///            humanStream.SetGoalRotation(AvatarIKGoal.LeftFoot, effector.GetRotation(stream));
        ///            humanStream.SetGoalWeightPosition(AvatarIKGoal.LeftFoot, positionWeight.GetFloat(stream));
        ///            humanStream.SetGoalWeightRotation(AvatarIKGoal.LeftFoot, rotationWeight.GetFloat(stream));
        ///        }
        ///
        ///        humanStream.SolveIK();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public void SolveIK()                                                   { ThrowIfInvalid(); InternalSolveIK(); }

        [NativeMethod(IsThreadSafe = true)]
        private extern float GetHumanScale();

        [NativeMethod(IsThreadSafe = true)]
        private extern float GetFootHeight(bool left);

        [NativeMethod(Name = "ResetToStancePose", IsThreadSafe = true)]
        private extern void InternalResetToStancePose();

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalPositionFromPose", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetGoalPositionFromPose(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalRotationFromPose", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion InternalGetGoalRotationFromPose(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetBodyLocalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetBodyLocalPosition();
        [NativeMethod(Name = "AnimationHumanStreamBindings::SetBodyLocalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetBodyLocalPosition(Vector3 value);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetBodyLocalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion InternalGetBodyLocalRotation();

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetBodyLocalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetBodyLocalRotation(Quaternion value);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetBodyPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetBodyPosition();

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetBodyPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetBodyPosition(Vector3 value);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetBodyRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion InternalGetBodyRotation();

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetBodyRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetBodyRotation(Quaternion value);

        [NativeMethod(Name = "GetMuscle", IsThreadSafe = true)]
        private extern float InternalGetMuscle(MuscleHandle muscle);

        [NativeMethod(Name = "SetMuscle", IsThreadSafe = true)]
        private extern void InternalSetMuscle(MuscleHandle muscle, float value);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetLeftFootVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetLeftFootVelocity();

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetRightFootVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetRightFootVelocity();

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalLocalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetGoalLocalPosition(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetGoalLocalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetGoalLocalPosition(AvatarIKGoal index, Vector3 pos);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalLocalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion InternalGetGoalLocalRotation(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetGoalLocalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetGoalLocalRotation(AvatarIKGoal index, Quaternion rot);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetGoalPosition(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetGoalPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetGoalPosition(AvatarIKGoal index, Vector3 pos);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetGoalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion InternalGetGoalRotation(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetGoalRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetGoalRotation(AvatarIKGoal index, Quaternion rot);

        [NativeMethod(Name = "SetGoalWeightPosition", IsThreadSafe = true)]
        private extern void InternalSetGoalWeightPosition(AvatarIKGoal index, float value);

        [NativeMethod(Name = "SetGoalWeightRotation", IsThreadSafe = true)]
        private extern void InternalSetGoalWeightRotation(AvatarIKGoal index, float value);

        [NativeMethod(Name = "GetGoalWeightPosition", IsThreadSafe = true)]
        private extern float InternalGetGoalWeightPosition(AvatarIKGoal index);

        [NativeMethod(Name = "GetGoalWeightRotation", IsThreadSafe = true)]
        private extern float InternalGetGoalWeightRotation(AvatarIKGoal index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::GetHintPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 InternalGetHintPosition(AvatarIKHint index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetHintPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetHintPosition(AvatarIKHint index, Vector3 pos);

        [NativeMethod(Name = "SetHintWeightPosition", IsThreadSafe = true)]
        private extern void InternalSetHintWeightPosition(AvatarIKHint index, float value);

        [NativeMethod(Name = "GetHintWeightPosition", IsThreadSafe = true)]
        private extern float InternalGetHintWeightPosition(AvatarIKHint index);

        [NativeMethod(Name = "AnimationHumanStreamBindings::SetLookAtPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void InternalSetLookAtPosition(Vector3 lookAtPosition);

        [NativeMethod(Name = "SetLookAtClampWeight", IsThreadSafe = true)]
        private extern void InternalSetLookAtClampWeight(float weight);

        [NativeMethod(Name = "SetLookAtBodyWeight", IsThreadSafe = true)]
        private extern void InternalSetLookAtBodyWeight(float weight);

        [NativeMethod(Name = "SetLookAtHeadWeight", IsThreadSafe = true)]
        private extern void InternalSetLookAtHeadWeight(float weight);

        [NativeMethod(Name = "SetLookAtEyesWeight", IsThreadSafe = true)]
        private extern void InternalSetLookAtEyesWeight(float weight);

        [NativeMethod(Name = "SolveIK", IsThreadSafe = true)]
        private extern void InternalSolveIK();
    }
}
