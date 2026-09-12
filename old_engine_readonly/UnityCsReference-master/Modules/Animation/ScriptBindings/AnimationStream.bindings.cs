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
    internal enum AnimatorBindingsVersion
    {
        // Invalid.
        kInvalidNotNative = 0,       // Created in C# (with new)
        kInvalidUnresolved = 1,      // Created in C++, but still unresolved

        // Valid.
        kValidMinVersion = 2         // Minimum valid version
    }

    ///<summary>The stream of animation data passed from one <see cref="T:UnityEngine.Playables.Playable" /> to another.</summary>
    ///<remarks>The AnimationStream structure is passed through the animation <see cref="T:UnityEngine.Playables.Playable" /> structures, like <see cref="AnimationClipPlayable" /> and <see cref="AnimationMixerPlayable" />. They can be modified when used with an <see cref="IAnimationJobPlayable" />, like the <see cref="AnimationScriptPlayable" />.
    ///
    ///The Playables implementing <see cref="IAnimationJobPlayable" /> take a custom C# job, which must implement <see cref="IAnimationJob" />, and the AnimationStream is then passed to its callbacks during the animation processing pass.</remarks>
    ///<seealso cref="IAnimationJob" />
    ///<seealso cref="AnimationScriptPlayable" />
    ///<seealso cref="TransformStreamHandle" />
    ///<seealso cref="PropertyStreamHandle" />
    ///<seealso cref="TransformSceneHandle" />
    ///<seealso cref="PropertySceneHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationStream.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationStream.h")]
    [RequiredByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    public struct AnimationStream
    {
        private UInt32 m_AnimatorBindingsVersion;

        private System.IntPtr constant;
        private System.IntPtr input;
        private System.IntPtr output;
        private System.IntPtr workspace;
        private System.IntPtr inputStreamAccessor;
        private System.IntPtr animationHandleBinder;

        internal const int InvalidIndex = ~0;

        internal UInt32 animatorBindingsVersion
        {
            get { return m_AnimatorBindingsVersion; }
        }

        ///<summary>Returns <c>true</c> if the stream is valid; <c>false</c> otherwise. (RO)</summary>
        public bool isValid
        {
            get
            {
                return m_AnimatorBindingsVersion >= (UInt32)AnimatorBindingsVersion.kValidMinVersion &&
                    constant != System.IntPtr.Zero &&
                    input != System.IntPtr.Zero &&
                    output != System.IntPtr.Zero &&
                    workspace != System.IntPtr.Zero &&
                    animationHandleBinder != System.IntPtr.Zero;
            }
        }

        internal void CheckIsValid()
        {
            if (!isValid)
                throw new InvalidOperationException("The AnimationStream is invalid.");
        }

        ///<summary>Gets the delta time for the evaluated frame. (RO)</summary>
        public float deltaTime
        {
            get { CheckIsValid(); return GetDeltaTime(); }
        }

        ///<summary>Gets or sets the avatar velocity for the evaluated frame.</summary>
        public Vector3 velocity
        {
            get { CheckIsValid(); return GetVelocity(); }
            set { CheckIsValid(); SetVelocity(value); }
        }

        ///<summary>Gets or sets the avatar angular velocity for the evaluated frame.</summary>
        public Vector3 angularVelocity
        {
            get { CheckIsValid(); return GetAngularVelocity(); }
            set { CheckIsValid(); SetAngularVelocity(value); }
        }

        ///<summary>Gets the root motion position for the evaluated frame. (RO)</summary>
        ///<seealso cref="Animator.applyRootMotion" />
        ///<seealso cref="IAnimationJob.ProcessRootMotion" />
        public Vector3 rootMotionPosition
        {
            get { CheckIsValid(); return GetRootMotionPosition(); }
        }

        ///<summary>Gets the root motion rotation for the evaluated frame. (RO)</summary>
        ///<seealso cref="Animator.applyRootMotion" />
        ///<seealso cref="IAnimationJob.ProcessRootMotion" />
        public Quaternion rootMotionRotation
        {
            get { CheckIsValid(); return GetRootMotionRotation(); }
        }

        ///<summary>Returns <c>true</c> if the stream is from a humanoid avatar; <c>false</c> otherwise. (RO)</summary>
        ///<seealso cref="M:UnityEngine.Animations.AnimationStream.AsHuman" />
        public bool isHumanStream
        {
            get { CheckIsValid(); return GetIsHumanStream(); }
        }

        ///<summary>Gets the same stream, but as an <see cref="AnimationHumanStream" />.</summary>
        ///<remarks>This function throws an <c>InvalidOperationException</c> is the avatar is not a humanoid.</remarks>
        ///<returns>Returns the same stream, but as an <see cref="AnimationHumanStream" />.</returns>
        ///<seealso cref="isHumanStream" />
        public AnimationHumanStream AsHuman()
        {
            CheckIsValid();
            if (!GetIsHumanStream())
                throw new InvalidOperationException("Cannot create an AnimationHumanStream for a generic rig.");

            return GetHumanStream();
        }

        ///<summary>Gets the number of input streams. (RO)</summary>
        ///<remarks>The number of input streams are equal to the number of inputs in the Playable.</remarks>
        ///<seealso cref="GetInputStream" />
        public int inputStreamCount
        {
            get { CheckIsValid(); return GetInputStreamCount(); }
        }

        ///<summary>Gets the <see cref="AnimationStream" /> of the playable input at <c>index</c>.</summary>
        ///<param name="index">The input index.</param>
        ///<returns>Returns the <see cref="AnimationStream" /> of the playable input at <c>index</c>. Returns an invalid stream if the input is not an animation Playable.</returns>
        ///<seealso cref="inputStreamCount" />
        public AnimationStream GetInputStream(int index)
        {
            CheckIsValid();
            return InternalGetInputStream(index);
        }

        ///<summary>Gets the weight of the <see cref="T:UnityEngine.Playables.Playable" /> connected at a specific input index.</summary>
        ///<param name="index">The input index.</param>
        ///<returns>Returns the weight of the <see cref="T:UnityEngine.Playables.Playable" /> input as a float.</returns>
        ///<seealso cref="inputStreamCount" />
        public float GetInputWeight(int index)
        {
            CheckIsValid();
            return InternalGetInputWeight(index);
        }

        ///<summary>Deep copies motion from a source animation stream to the current animation stream.</summary>
        ///<remarks>The copied motion includes velocity, angular velocity, and other hidden velocity properties such as avatar foot velocity.</remarks>
        ///<param name="animationStream">The source animation stream with the motion to deep copy.</param>
        public void CopyAnimationStreamMotion(AnimationStream animationStream)
        {
            CheckIsValid();
            animationStream.CheckIsValid();
            CopyAnimationStreamMotionInternal(animationStream);
        }

        private void ReadSceneTransforms() { CheckIsValid(); InternalReadSceneTransforms(); }
        private void WriteSceneTransforms() { CheckIsValid(); InternalWriteSceneTransforms(); }

        [NativeMethod(Name = "AnimationStreamBindings::CopyAnimationStreamMotion", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void CopyAnimationStreamMotionInternal(AnimationStream animationStream);

        [NativeMethod(IsThreadSafe = true)]
        private extern float GetDeltaTime();

        [NativeMethod(IsThreadSafe = true)]
        private extern bool GetIsHumanStream();

        [NativeMethod(Name = "AnimationStreamBindings::GetVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetVelocity();

        [NativeMethod(Name = "AnimationStreamBindings::SetVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void SetVelocity(Vector3 velocity);

        [NativeMethod(Name = "AnimationStreamBindings::GetAngularVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetAngularVelocity();

        [NativeMethod(Name = "AnimationStreamBindings::SetAngularVelocity", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void SetAngularVelocity(Vector3 velocity);

        [NativeMethod(Name = "AnimationStreamBindings::GetRootMotionPosition", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetRootMotionPosition();

        [NativeMethod(Name = "AnimationStreamBindings::GetRootMotionRotation", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion GetRootMotionRotation();

        [NativeMethod(IsThreadSafe = true)]
        private extern int GetInputStreamCount();

        [NativeMethod(Name = "GetInputStream", IsThreadSafe = true)]
        private extern AnimationStream InternalGetInputStream(int index);

        [NativeMethod(Name = "GetInputWeight", IsThreadSafe = true)]
        private extern float InternalGetInputWeight(int index);

        [NativeMethod(IsThreadSafe = true)]
        private extern AnimationHumanStream GetHumanStream();

        [NativeMethod(Name = "ReadSceneTransforms", IsThreadSafe = true)]
        private extern void InternalReadSceneTransforms();

        [NativeMethod(Name = "WriteSceneTransforms", IsThreadSafe = true)]
        private extern void InternalWriteSceneTransforms();
    }
}
