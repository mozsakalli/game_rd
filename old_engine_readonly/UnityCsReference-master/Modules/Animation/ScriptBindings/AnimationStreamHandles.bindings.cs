// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    // This enum is mapped to UnityEngine::Animation::BindType
    internal enum BindType
    {
        Unbound                        = 0,  // UnityEngine::Animation::kUnbound
        Float                          = 5,  // UnityEngine::Animation::kBindFloat
        Bool                           = 6,  // UnityEngine::Animation::kBindFloatToBool
        GameObjectActive               = 7,  // UnityEngine::Animation::kBindGameObjectActive
        ObjectReference                = 9,  // UnityEngine::Animation::kBindScriptObjectReference;
        Int                            = 10, // UnityEngine::Animation::kBindFloatToInt
        DiscreetInt                    = 11, // UnityEngine::Animation::kBindDiscreteInt
    }

    ///<summary>Position, rotation and scale of an object in the <see cref="AnimationStream" />.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Playables;
    ///using UnityEngine.Animations;
    ///
    ///public struct TransformStreamHandleJob : IAnimationJob
    ///{
    ///    public TransformStreamHandle handle;
    ///    public Vector3 position;
    ///    public Vector3 rotation;
    ///    public Vector3 scale;
    ///
    ///    public void ProcessRootMotion(AnimationStream stream)
    ///    {
    ///        // Set the new local position.
    ///        handle.SetLocalPosition(stream, position);
    ///
    ///        // Set the new local rotation (converted from euler).
    ///        handle.SetLocalRotation(stream, Quaternion.Euler(rotation));
    ///
    ///        // Set the new local scale.
    ///        handle.SetLocalScale(stream, scale);
    ///    }
    ///
    ///    public void ProcessAnimation(AnimationStream stream)
    ///    {
    ///    }
    ///}
    ///
    ///[RequireComponent(typeof(Animator))]
    ///public class TransformStreamHandleExample : MonoBehaviour
    ///{
    ///    public Vector3 position;
    ///    public Vector3 rotation;
    ///    public Vector3 scale = Vector3.one;
    ///
    ///    PlayableGraph m_Graph;
    ///    AnimationScriptPlayable m_AnimationScriptPlayable;
    ///
    ///    void Start()
    ///    {
    ///        var animator = GetComponent<Animator>();
    ///
    ///        m_Graph = PlayableGraph.Create("TransformStreamHandleExample");
    ///        var output = AnimationPlayableOutput.Create(m_Graph, "output", animator);
    ///
    ///        var animationJob = new TransformStreamHandleJob();
    ///        animationJob.handle = animator.BindStreamTransform(gameObject.transform);
    ///        m_AnimationScriptPlayable = AnimationScriptPlayable.Create(m_Graph, animationJob);
    ///
    ///        output.SetSourcePlayable(m_AnimationScriptPlayable);
    ///        m_Graph.Play();
    ///    }
    ///
    ///    void Update()
    ///    {
    ///        var animationJob = m_AnimationScriptPlayable.GetJobData<TransformStreamHandleJob>();
    ///        animationJob.position = position;
    ///        animationJob.rotation = rotation;
    ///        animationJob.scale = scale;
    ///        m_AnimationScriptPlayable.SetJobData(animationJob);
    ///    }
    ///
    ///    void OnDisable()
    ///    {
    ///        m_Graph.Destroy();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="AnimatorJobExtensions.BindStreamTransform" />
    ///<seealso cref="PropertyStreamHandle" />
    ///<seealso cref="PropertySceneHandle" />
    ///<seealso cref="TransformSceneHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationStreamHandles.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationStreamHandles.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct TransformStreamHandle
    {
        private UInt32 m_AnimatorBindingsVersion;
        private int handleIndex;
        private int skeletonIndex;

        ///<summary>Returns whether this is a valid handle.</summary>
        ///<remarks>A TransformStreamHandle may be invalid if, for example, you didn't use the correct function to create it. .</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Whether this is a valid handle.</returns>
        ///<seealso cref="AnimatorJobExtensions.BindStreamTransform" />
        public bool IsValid(AnimationStream stream)
        {
            return IsValidInternal(ref stream);
        }

        private bool IsValidInternal(ref AnimationStream stream)
        {
            return stream.isValid && createdByNative && hasHandleIndex;
        }

        private bool createdByNative
        {
            get { return animatorBindingsVersion != (UInt32)AnimatorBindingsVersion.kInvalidNotNative; }
        }

        private bool IsSameVersionAsStream(ref AnimationStream stream)
        {
            return animatorBindingsVersion == stream.animatorBindingsVersion;
        }

        private bool hasHandleIndex
        {
            get { return handleIndex != AnimationStream.InvalidIndex; }
        }

        private bool hasSkeletonIndex
        {
            get { return skeletonIndex != AnimationStream.InvalidIndex; }
        }

        // internal for EditorTests
        internal UInt32 animatorBindingsVersion
        {
            private set { m_AnimatorBindingsVersion = value; }
            get { return m_AnimatorBindingsVersion; }
        }

        ///<summary>Bind this handle with an animated values from the <see cref="AnimationStream" />.</summary>
        ///<remarks>Handles are lazily resolved as they're accessed, but in order to prevent unwanted CPU spikes, this method allows to resolve handles in a deterministic way.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<seealso cref="IsResolved" />
        ///<seealso cref="AnimatorJobExtensions.ResolveAllStreamHandles" />
        public void Resolve(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
        }

        ///<summary>Returns whether this handle is resolved.</summary>
        ///<remarks>A TransformStreamHandle is resolved if it is valid, if it has the same bindings version than the one in the stream, and if it is bound to the transform in the stream. A TransformStreamHandle can become unresolved if the animator bindings have changed or if the transform had been destroyed.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns <c>true</c> if the handle is resolved, <c>false</c> otherwise.</returns>
        ///<seealso cref="Resolve" />
        ///<seealso cref="IsValid" />
        public bool IsResolved(AnimationStream stream)
        {
            return IsResolvedInternal(ref stream);
        }

        private bool IsResolvedInternal(ref AnimationStream stream)
        {
            return IsValidInternal(ref stream) &&
                IsSameVersionAsStream(ref stream) &&
                hasSkeletonIndex;
        }

        private void CheckIsValidAndResolve(ref AnimationStream stream)
        {
            // Verify stream.
            stream.CheckIsValid();

            if (IsResolvedInternal(ref stream))
                return;

            // Handle create directly by user are never valid
            if (!createdByNative || !hasHandleIndex)
                throw new InvalidOperationException("The TransformStreamHandle is invalid. Please use proper function to create the handle.");

            if (!IsSameVersionAsStream(ref stream) || (hasHandleIndex && !hasSkeletonIndex))
            {
                ResolveInternal(ref stream);
            }

            if (hasHandleIndex && !hasSkeletonIndex)
                throw new InvalidOperationException("The TransformStreamHandle cannot be resolved.");
        }

        ///<summary>Gets the position of the transform in world space.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The position of the transform in world space.</returns>
        public Vector3 GetPosition(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetPositionInternal(ref stream); }
        ///<summary>Sets the position of the transform in world space.</summary>
        ///<param name="position">The position of the transform in world space.</param>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        public void SetPosition(AnimationStream stream, Vector3 position) { CheckIsValidAndResolve(ref stream); SetPositionInternal(ref stream, position); }

        ///<summary>Gets the rotation of the transform in world space.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The rotation of the transform in world space.</returns>
        public Quaternion GetRotation(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetRotationInternal(ref stream); }
        ///<summary>Sets the rotation of the transform in world space.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="rotation">The rotation of the transform in world space.</param>
        public void SetRotation(AnimationStream stream, Quaternion rotation) { CheckIsValidAndResolve(ref stream); SetRotationInternal(ref stream, rotation); }

        ///<summary>Gets the position of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The position of the transform relative to the parent.</returns>
        public Vector3 GetLocalPosition(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetLocalPositionInternal(ref stream); }
        ///<summary>Sets the position of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="position">The position of the transform relative to the parent.</param>
        public void SetLocalPosition(AnimationStream stream, Vector3 position) { CheckIsValidAndResolve(ref stream); SetLocalPositionInternal(ref stream, position); }

        ///<summary>Gets the rotation of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The rotation of the transform relative to the parent.</returns>
        public Quaternion GetLocalRotation(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetLocalRotationInternal(ref stream); }
        ///<summary>Sets the rotation of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="rotation">The rotation of the transform relative to the parent.</param>
        public void SetLocalRotation(AnimationStream stream, Quaternion rotation) { CheckIsValidAndResolve(ref stream); SetLocalRotationInternal(ref stream, rotation); }

        ///<summary>Gets the scale of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The scale of the transform relative to the parent.</returns>
        public Vector3 GetLocalScale(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetLocalScaleInternal(ref stream); }
        ///<summary>Sets the scale of the transform relative to the parent.</summary>
        ///<param name="scale">The scale of the transform relative to the parent.</param>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        public void SetLocalScale(AnimationStream stream, Vector3 scale) { CheckIsValidAndResolve(ref stream); SetLocalScaleInternal(ref stream, scale); }

        ///<summary>Gets the local to parent matrix of the transform.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns the local to parent matrix.</returns>
        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            return GetLocalToParentMatrixInternal(ref stream);
        }

        ///<summary>Gets the position read mask of the transform.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns true if the position can be read.</returns>
        public bool GetPositionReadMask(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetPositionReadMaskInternal(ref stream); }
        ///<summary>Gets the rotation read mask of the transform.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns true if the rotation can be read.</returns>
        public bool GetRotationReadMask(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetRotationReadMaskInternal(ref stream); }
        ///<summary>Gets the scale read mask of the transform.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns true if the scale can be read.</returns>
        public bool GetScaleReadMask(AnimationStream stream) { CheckIsValidAndResolve(ref stream); return GetScaleReadMaskInternal(ref stream); }

        ///<summary>Gets the position, rotation and scale of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="position">The position of the transform relative to the parent.</param>
        ///<param name="rotation">The rotation of the transform relative to the parent.</param>
        ///<param name="scale">The scale of the transform relative to the parent.</param>
        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            CheckIsValidAndResolve(ref stream);
            GetLocalTRSInternal(ref stream, out position, out rotation, out scale);
        }

        ///<summary>Sets the position, rotation and scale of the transform relative to the parent.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="position">The position of the transform relative to the parent.</param>
        ///<param name="rotation">The rotation of the transform relative to the parent.</param>
        ///<param name="scale">The scale of the transform relative to the parent.</param>
        ///<param name="useMask">Set to true to write the specified parameters if the matching stream parameters have not already been modified.</param>
        public void SetLocalTRS(AnimationStream stream, Vector3 position, Quaternion rotation, Vector3 scale, bool useMask)
        {
            CheckIsValidAndResolve(ref stream);
            SetLocalTRSInternal(ref stream, position, rotation, scale, useMask);
        }

        ///<summary>Gets the position and scaled rotation of the transform in world space.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="position">The position of the transform in world space.</param>
        ///<param name="rotation">The rotation of the transform in world space.</param>
        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            CheckIsValidAndResolve(ref stream);
            GetGlobalTRInternal(ref stream, out position, out rotation);
        }

        ///<summary>Gets the local to world matrix of the transform.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns the local to world matrix.</returns>
        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            return GetLocalToWorldMatrixInternal(ref stream);
        }

        ///<summary>Sets the position and rotation of the transform in world space.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="position">The position of the transform in world space.</param>
        ///<param name="rotation">The rotation of the transform in world space.</param>
        ///<param name="useMask">Set to true to write the specified parameters if the matching stream parameters have not already been modified.</param>
        public void SetGlobalTR(AnimationStream stream, Vector3 position, Quaternion rotation, bool useMask)
        {
            CheckIsValidAndResolve(ref stream);
            SetGlobalTRInternal(ref stream, position, rotation, useMask);
        }

        [NativeMethod(Name = "Resolve", IsThreadSafe = true)]
        private extern void ResolveInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetPositionInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Vector3 GetPositionInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetPositionInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetPositionInternal(ref AnimationStream stream, Vector3 position);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetRotationInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Quaternion GetRotationInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetRotationInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetRotationInternal(ref AnimationStream stream, Quaternion rotation);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalPositionInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Vector3 GetLocalPositionInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetLocalPositionInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetLocalPositionInternal(ref AnimationStream stream, Vector3 position);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalRotationInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Quaternion GetLocalRotationInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetLocalRotationInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetLocalRotationInternal(ref AnimationStream stream, Quaternion rotation);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalScaleInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Vector3 GetLocalScaleInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetLocalScaleInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetLocalScaleInternal(ref AnimationStream stream, Vector3 scale);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalToParentMatrixInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Matrix4x4 GetLocalToParentMatrixInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetPositionReadMaskInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern bool GetPositionReadMaskInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetRotationReadMaskInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern bool GetRotationReadMaskInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetScaleReadMaskInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern bool GetScaleReadMaskInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalTRSInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void GetLocalTRSInternal(ref AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetLocalTRSInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetLocalTRSInternal(ref AnimationStream stream, Vector3 position, Quaternion rotation, Vector3 scale, bool useMask);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetGlobalTRInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void GetGlobalTRInternal(ref AnimationStream stream, out Vector3 position, out Quaternion rotation);

        [NativeMethod(Name = "TransformStreamHandleBindings::GetLocalToWorldMatrixInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Matrix4x4 GetLocalToWorldMatrixInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformStreamHandleBindings::SetGlobalTRInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern void SetGlobalTRInternal(ref AnimationStream stream, Vector3 position, Quaternion rotation, bool useMask);
    }

    ///<summary>Handle for a <see cref="Component" /> property on an object in the <see cref="AnimationStream" />.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Playables;
    ///using UnityEngine.Animations;
    ///
    ///public struct PropertyStreamHandleJob : IAnimationJob
    ///{
    ///    public PropertyStreamHandle handleR;
    ///    public PropertyStreamHandle handleG;
    ///    public PropertyStreamHandle handleB;
    ///    public Color color;
    ///
    ///    public void ProcessRootMotion(AnimationStream stream)
    ///    {
    ///    }
    ///
    ///    public void ProcessAnimation(AnimationStream stream)
    ///    {
    ///        // Set the new light color.
    ///        handleR.SetFloat(stream, color.r);
    ///        handleG.SetFloat(stream, color.g);
    ///        handleB.SetFloat(stream, color.b);
    ///    }
    ///}
    ///
    ///[RequireComponent(typeof(Animator))]
    ///[RequireComponent(typeof(Light))]
    ///public class PropertyStreamHandleExample : MonoBehaviour
    ///{
    ///    public Color color = Color.white;
    ///
    ///    PlayableGraph m_Graph;
    ///    AnimationScriptPlayable m_AnimationScriptPlayable;
    ///
    ///    void Start()
    ///    {
    ///        var animator = GetComponent<Animator>();
    ///
    ///        m_Graph = PlayableGraph.Create("PropertyStreamHandleExample");
    ///        var output = AnimationPlayableOutput.Create(m_Graph, "output", animator);
    ///
    ///        var animationJob = new PropertyStreamHandleJob();
    ///        animationJob.handleR = animator.BindStreamProperty(gameObject.transform, typeof(Light), "m_Color.r");
    ///        animationJob.handleG = animator.BindStreamProperty(gameObject.transform, typeof(Light), "m_Color.g");
    ///        animationJob.handleB = animator.BindStreamProperty(gameObject.transform, typeof(Light), "m_Color.b");
    ///        m_AnimationScriptPlayable = AnimationScriptPlayable.Create(m_Graph, animationJob);
    ///
    ///        output.SetSourcePlayable(m_AnimationScriptPlayable);
    ///        m_Graph.Play();
    ///    }
    ///
    ///    void Update()
    ///    {
    ///        var animationJob = m_AnimationScriptPlayable.GetJobData<PropertyStreamHandleJob>();
    ///        animationJob.color = color;
    ///        m_AnimationScriptPlayable.SetJobData(animationJob);
    ///    }
    ///
    ///    void OnDisable()
    ///    {
    ///        m_Graph.Destroy();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="AnimatorJobExtensions.BindStreamProperty" />
    ///<seealso cref="TransformStreamHandle" />
    ///<seealso cref="PropertySceneHandle" />
    ///<seealso cref="TransformSceneHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/Director/AnimationStreamHandles.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyStreamHandle
    {
        private UInt32 m_AnimatorBindingsVersion;
        private int handleIndex;
        private int valueArrayIndex;
        private int bindType;

        ///<summary>Returns whether or not the handle is valid.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Whether or not the handle is valid.</returns>
        public bool IsValid(AnimationStream stream)
        {
            return IsValidInternal(ref stream);
        }

        private bool IsValidInternal(ref AnimationStream stream)
        {
            return stream.isValid && createdByNative && hasHandleIndex && hasBindType;
        }

        private bool createdByNative
        {
            get { return animatorBindingsVersion != (UInt32)AnimatorBindingsVersion.kInvalidNotNative; }
        }

        private bool IsSameVersionAsStream(ref AnimationStream stream)
        {
            return animatorBindingsVersion == stream.animatorBindingsVersion;
        }

        private bool hasHandleIndex
        {
            get { return handleIndex != AnimationStream.InvalidIndex; }
        }

        private bool hasValueArrayIndex
        {
            get { return valueArrayIndex != AnimationStream.InvalidIndex; }
        }

        private bool hasBindType
        {
            get { return bindType != (int)BindType.Unbound; }
        }

        // internal for EditorTests
        internal UInt32 animatorBindingsVersion
        {
            private set { m_AnimatorBindingsVersion = value; }
            get { return m_AnimatorBindingsVersion; }
        }

        ///<summary>Resolves the handle.</summary>
        ///<remarks>Handles are lazily resolved as they're accessed, but in order to prevent unwanted CPU spikes, this method allows to resolve handles in a deterministic way.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<seealso cref="IsResolved" />
        ///<seealso cref="AnimatorJobExtensions.ResolveAllStreamHandles" />
        public void Resolve(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
        }

        ///<summary>Returns whether or not the handle is resolved.</summary>
        ///<remarks>A PropertyStreamHandle is resolved if it is valid, if it has the same bindings version than the one in the stream, and if it is bound to the property in the stream. A PropertyStreamHandle can become unresolved if the animator bindings have changed or if the property doesn't exist anymore (e.g. the component has been removed).</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns <c>true</c> if the handle is resolved, <c>false</c> otherwise.</returns>
        ///<seealso cref="Resolve" />
        ///<seealso cref="IsValid" />
        public bool IsResolved(AnimationStream stream)
        {
            return IsResolvedInternal(ref stream);
        }

        private bool IsResolvedInternal(ref AnimationStream stream)
        {
            return IsValidInternal(ref stream) &&
                IsSameVersionAsStream(ref stream) &&
                hasValueArrayIndex;
        }

        private void CheckIsValidAndResolve(ref AnimationStream stream)
        {
            // Verify stream.
            stream.CheckIsValid();

            if (IsResolvedInternal(ref stream))
                return;

            // Handle create directly by user are never valid
            if (!createdByNative || !hasHandleIndex || !hasBindType)
                throw new InvalidOperationException("The PropertyStreamHandle is invalid. Please use proper function to create the handle.");

            if (!IsSameVersionAsStream(ref stream) || (hasHandleIndex && !hasValueArrayIndex))
            {
                ResolveInternal(ref stream);
            }

            if (hasHandleIndex && !hasValueArrayIndex)
                throw new InvalidOperationException("The PropertyStreamHandle cannot be resolved.");
        }

        ///<summary>Gets the float property value from a stream.</summary>
        ///<remarks>If the property is not a float, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The float property value.</returns>
        public float GetFloat(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.Float)
                throw new InvalidOperationException("GetValue type doesn't match PropertyStreamHandle bound type.");
            return GetFloatInternal(ref stream);
        }

        ///<summary>Sets the float property value into a stream.</summary>
        ///<remarks>If the property is not a float, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="value">The new float property value.</param>
        public void SetFloat(AnimationStream stream, float value)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.Float)
                throw new InvalidOperationException("SetValue type doesn't match PropertyStreamHandle bound type.");
            SetFloatInternal(ref stream, value);
        }

        ///<summary>Gets the integer property value from a stream.</summary>
        ///<remarks>If the property is not an integer, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The integer property value.</returns>
        public int GetInt(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType == (int)BindType.ObjectReference)
            {
                throw new InvalidOperationException("Please Use GetEntityId directly to get the value of an ObjectReference PropertyStreamHandle.");
            }
            if (bindType != (int)BindType.Int && bindType != (int)BindType.DiscreetInt)
                throw new InvalidOperationException("GetValue type doesn't match PropertyStreamHandle bound type.");
            return GetIntInternal(ref stream);
        }

        ///<summary>Sets the integer property value into a stream.</summary>
        ///<remarks>If the property is not an integer, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="value">The new integer property value.</param>
        public void SetInt(AnimationStream stream, int value)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType == (int)BindType.ObjectReference)
            {
                throw new InvalidOperationException("Use SetEntityId directly to set the value of an ObjectReference PropertyStreamHandle.");
            }

            if (bindType != (int)BindType.Int && bindType != (int)BindType.DiscreetInt)
                throw new InvalidOperationException("SetValue type doesn't match PropertyStreamHandle bound type.");
            SetIntInternal(ref stream, value);
        }

        ///<summary>Gets the EntityId property value from a stream.</summary>
        ///<remarks>If the property is not an EntityId, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The EntityId property value.</returns>
        public EntityId GetEntityId(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.ObjectReference)
                throw new InvalidOperationException("GetValue type doesn't match PropertyStreamHandle bound type.");
            return GetEntityIdInternal(ref stream);
        }

        ///<summary>Sets the EntityId property value into a stream.</summary>
        ///<remarks>If the property is not an EntityId, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="value">The new EntityId property value.</param>
        public void SetEntityId(AnimationStream stream, EntityId value)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.ObjectReference)
                throw new InvalidOperationException("SetValue type doesn't match PropertyStreamHandle bound type.");
            SetEntityIdInternal(ref stream, value);
        }

        ///<summary>Gets the boolean property value from a stream.</summary>
        ///<remarks>If the property is not a boolean, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>The boolean property value.</returns>
        public bool GetBool(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.Bool && bindType != (int)BindType.GameObjectActive)
                throw new InvalidOperationException("GetValue type doesn't match PropertyStreamHandle bound type.");
            return GetBoolInternal(ref stream);
        }

        ///<summary>Sets the boolean property value into a stream.</summary>
        ///<remarks>If the property is not a boolean, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<param name="value">The new boolean property value.</param>
        public void SetBool(AnimationStream stream, bool value)
        {
            CheckIsValidAndResolve(ref stream);
            if (bindType != (int)BindType.Bool && bindType != (int)BindType.GameObjectActive)
                throw new InvalidOperationException("SetValue type doesn't match PropertyStreamHandle bound type.");
            SetBoolInternal(ref stream, value);
        }

        ///<summary>Gets the read mask of the property.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> that holds the animated values.</param>
        ///<returns>Returns true if the property can be read.</returns>
        public bool GetReadMask(AnimationStream stream)
        {
            CheckIsValidAndResolve(ref stream);
            return GetReadMaskInternal(ref stream);
        }

        [NativeMethod(Name = "Resolve", IsThreadSafe = true)]
        private extern void ResolveInternal(ref AnimationStream stream);

        [NativeMethod(Name = "GetFloat", IsThreadSafe = true)]
        private extern float GetFloatInternal(ref AnimationStream stream);

        [NativeMethod(Name = "SetFloat", IsThreadSafe = true)]
        private extern void SetFloatInternal(ref AnimationStream stream, float value);

        [NativeMethod(Name = "GetInt", IsThreadSafe = true)]
        private extern int GetIntInternal(ref AnimationStream stream);

        [NativeMethod(Name = "SetInt", IsThreadSafe = true)]
        private extern void SetIntInternal(ref AnimationStream stream, int value);

        [NativeMethod(Name = "GetEntityId", IsThreadSafe = true)]
        private extern EntityId GetEntityIdInternal(ref AnimationStream stream);

        [NativeMethod(Name = "SetEntityId", IsThreadSafe = true)]
        private extern void SetEntityIdInternal(ref AnimationStream stream, EntityId value);

        [NativeMethod(Name = "GetBool", IsThreadSafe = true)]
        private extern bool GetBoolInternal(ref AnimationStream stream);

        [NativeMethod(Name = "SetBool", IsThreadSafe = true)]
        private extern void SetBoolInternal(ref AnimationStream stream, bool value);

        [NativeMethod(Name = "GetReadMask", IsThreadSafe = true)]
        private extern bool GetReadMaskInternal(ref AnimationStream stream);
    }

    ///<summary>Handle to read position, rotation and scale of an object in the Scene.</summary>
    ///<remarks>
    ///  <para>TransformSceneHandle are read-only.
    ///
    ///A TransformSceneHandle is a safe handle on a <see cref="T:UnityEngine.Jobs.TransformAccess" />. The <see cref="Animator" /> used to create this handle manages the validity of this handle.</para>
    ///  <para />
    ///</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Playables;
    ///using UnityEngine.Animations;
    ///
    ///public struct TransformSceneHandleJob : IAnimationJob
    ///{
    ///    public TransformSceneHandle handle;
    ///
    ///    public void ProcessRootMotion(AnimationStream stream)
    ///    {
    ///        // Log the local position.
    ///        var position = handle.GetLocalPosition(stream);
    ///        Debug.LogFormat("Position: {0}", position);
    ///
    ///        // Log the local rotation (converted from euler).
    ///        var rotation = handle.GetLocalRotation(stream);
    ///        Debug.LogFormat("Rotation: {0}", rotation.eulerAngles);
    ///
    ///        // Log the local scale.
    ///        var scale = handle.GetLocalScale(stream);
    ///        Debug.LogFormat("Scale: {0}", scale);
    ///    }
    ///
    ///    public void ProcessAnimation(AnimationStream stream)
    ///    {
    ///    }
    ///}
    ///
    ///[RequireComponent(typeof(Animator))]
    ///public class TransformSceneHandleExample : MonoBehaviour
    ///{
    ///    public Transform sceneTransform;
    ///
    ///    PlayableGraph m_Graph;
    ///    AnimationScriptPlayable m_AnimationScriptPlayable;
    ///
    ///    void Start()
    ///    {
    ///        if (sceneTransform == null)
    ///            return;
    ///
    ///        var animator = GetComponent<Animator>();
    ///
    ///        m_Graph = PlayableGraph.Create("TransformSceneHandleExample");
    ///        var output = AnimationPlayableOutput.Create(m_Graph, "output", animator);
    ///
    ///        var animationJob = new TransformSceneHandleJob();
    ///        animationJob.handle = animator.BindSceneTransform(sceneTransform);
    ///        m_AnimationScriptPlayable = AnimationScriptPlayable.Create(m_Graph, animationJob);
    ///
    ///        output.SetSourcePlayable(m_AnimationScriptPlayable);
    ///        m_Graph.Play();
    ///    }
    ///
    ///    void OnDisable()
    ///    {
    ///        if (sceneTransform == null)
    ///            return;
    ///
    ///        m_Graph.Destroy();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="AnimatorJobExtensions.BindSceneTransform" />
    ///<seealso cref="PropertySceneHandle" />
    ///<seealso cref="PropertyStreamHandle" />
    ///<seealso cref="TransformStreamHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationStreamHandles.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationSceneHandles.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct TransformSceneHandle
    {
        private UInt32 valid;
        private int transformSceneHandleDefinitionIndex;

        ///<summary>Returns whether this is a valid handle.</summary>
        ///<remarks>A TransformSceneHandle may be invalid if, for example, the transform binded to this handle is deleted or if you didn't use the correct function to create it.</remarks>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>Whether this is a valid handle.</returns>
        ///<seealso cref="AnimatorJobExtensions.BindSceneTransform" />
        public bool IsValid(AnimationStream stream)
        {
            // [case 1032369] Cannot call native code before validating that handle was created in native and has a valid handle index
            return stream.isValid &&
                createdByNative &&
                hasTransformSceneHandleDefinitionIndex &&
                HasValidTransform(ref stream);
        }

        private bool createdByNative
        {
            get { return valid != 0; }
        }

        private bool hasTransformSceneHandleDefinitionIndex
        {
            get { return transformSceneHandleDefinitionIndex != AnimationStream.InvalidIndex; }
        }

        private void CheckIsValid(ref AnimationStream stream)
        {
            // Verify stream.
            stream.CheckIsValid();

            // Handle create directly by user are never valid
            if (!createdByNative || !hasTransformSceneHandleDefinitionIndex)
                throw new InvalidOperationException("The TransformSceneHandle is invalid. Please use proper function to create the handle.");

            // [case 1032369] Cannot call native code before validating that handle was created in native and has a valid handle index
            if (!HasValidTransform(ref stream))
                throw new NullReferenceException("The transform is invalid.");
        }

        ///<summary>Gets the position of the transform in world space.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>The position of the transform in world space.</returns>
        public Vector3 GetPosition(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetPositionInternal(ref stream);
        }

        ///<summary>Sets the position of the transform in world space.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="position">The position of the transform in world space.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetPosition(AnimationStream stream, Vector3 position) {}

        ///<summary>Gets the position of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>The position of the transform relative to the parent.</returns>
        public Vector3 GetLocalPosition(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetLocalPositionInternal(ref stream);
        }

        ///<summary>Sets the position of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="position">The position of the transform relative to the parent.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetLocalPosition(AnimationStream stream, Vector3 position) {}

        ///<summary>Gets the rotation of the transform in world space.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>The rotation of the transform in world space.</returns>
        public Quaternion GetRotation(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetRotationInternal(ref stream);
        }

        ///<summary>Sets the rotation of the transform in world space.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="rotation">The rotation of the transform in world space.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetRotation(AnimationStream stream, Quaternion rotation) {}

        ///<summary>Gets the rotation of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>The rotation of the transform relative to the parent.</returns>
        public Quaternion GetLocalRotation(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetLocalRotationInternal(ref stream);
        }

        ///<summary>Sets the rotation of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="rotation">The rotation of the transform relative to the parent.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetLocalRotation(AnimationStream stream, Quaternion rotation) {}

        ///<summary>Gets the scale of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>The scale of the transform relative to the parent.</returns>
        public Vector3 GetLocalScale(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetLocalScaleInternal(ref stream);
        }

        ///<summary>Gets the position, rotation and scale of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="position">The position of the transform relative to the parent.</param>
        ///<param name="rotation">The rotation of the transform relative to the parent.</param>
        ///<param name="scale">The scale of the transform relative to the parent.</param>
        public void GetLocalTRS(AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            CheckIsValid(ref stream);
            GetLocalTRSInternal(ref stream, out position, out rotation, out scale);
        }

        ///<summary>Gets the local to parent matrix of the transform.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>Returns the local to parent matrix.</returns>
        public Matrix4x4 GetLocalToParentMatrix(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetLocalToParentMatrixInternal(ref stream);
        }

        ///<summary>Gets the position and scaled rotation of the transform in world space.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="position">The position of the transform in world space.</param>
        ///<param name="rotation">The rotation of the transform in world space.</param>
        public void GetGlobalTR(AnimationStream stream, out Vector3 position, out Quaternion rotation)
        {
            CheckIsValid(ref stream);
            GetGlobalTRInternal(ref stream, out position, out rotation);
        }

        ///<summary>Gets the local to world matrix of the transform.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<returns>Returns the local to world matrix.</returns>
        public Matrix4x4 GetLocalToWorldMatrix(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetLocalToWorldMatrixInternal(ref stream);
        }

        ///<summary>Sets the scale of the transform relative to the parent.</summary>
        ///<param name="stream">The AnimationStream that manages this handle.</param>
        ///<param name="scale">The scale of the transform relative to the parent.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetLocalScale(AnimationStream stream, Vector3 scale) {}

        [NativeMethod(IsThreadSafe = true)]
        private extern bool HasValidTransform(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetPositionInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetPositionInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalPositionInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetLocalPositionInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetRotationInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion GetRotationInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalRotationInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Quaternion GetLocalRotationInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalScaleInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern Vector3 GetLocalScaleInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalTRSInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void GetLocalTRSInternal(ref AnimationStream stream, out Vector3 position, out Quaternion rotation, out Vector3 scale);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalToParentMatrixInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Matrix4x4 GetLocalToParentMatrixInternal(ref AnimationStream stream);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetGlobalTRInternal", IsFreeFunction = true, IsThreadSafe = true, HasExplicitThis = true)]
        private extern void GetGlobalTRInternal(ref AnimationStream stream, out Vector3 position, out Quaternion rotation);

        [NativeMethod(Name = "TransformSceneHandleBindings::GetLocalToWorldMatrixInternal", IsFreeFunction = true, HasExplicitThis = true, IsThreadSafe = true)]
        private extern Matrix4x4 GetLocalToWorldMatrixInternal(ref AnimationStream stream);
    }

    ///<summary>Handle to read a <see cref="Component" /> property on an object in the Scene.</summary>
    ///<remarks>
    ///  <para>PropertySceneHandle are read-only.</para>
    ///  <para />
    ///</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using UnityEngine.Playables;
    ///using UnityEngine.Animations;
    ///
    ///public struct PropertySceneHandleJob : IAnimationJob
    ///{
    ///    public PropertySceneHandle handleR;
    ///    public PropertySceneHandle handleG;
    ///    public PropertySceneHandle handleB;
    ///
    ///    public void ProcessRootMotion(AnimationStream stream)
    ///    {
    ///    }
    ///
    ///    public void ProcessAnimation(AnimationStream stream)
    ///    {
    ///        // Log the light color.
    ///        var r = handleR.GetFloat(stream);
    ///        var g = handleG.GetFloat(stream);
    ///        var b = handleB.GetFloat(stream);
    ///        Debug.LogFormat("Light color: (R: {0}, G: {1}, B: {2})", r, g, b);
    ///    }
    ///}
    ///
    ///[RequireComponent(typeof(Animator))]
    ///[RequireComponent(typeof(Light))]
    ///public class PropertySceneHandleExample : MonoBehaviour
    ///{
    ///    public Light sceneLight;
    ///
    ///    PlayableGraph m_Graph;
    ///    AnimationScriptPlayable m_AnimationScriptPlayable;
    ///
    ///    void Start()
    ///    {
    ///        if (sceneLight == null)
    ///            return;
    ///
    ///        var animator = GetComponent<Animator>();
    ///
    ///        m_Graph = PlayableGraph.Create("PropertySceneHandleExample");
    ///        var output = AnimationPlayableOutput.Create(m_Graph, "output", animator);
    ///
    ///        var animationJob = new PropertySceneHandleJob();
    ///        animationJob.handleR = animator.BindSceneProperty(sceneLight.transform, typeof(Light), "m_Color.r");
    ///        animationJob.handleG = animator.BindSceneProperty(sceneLight.transform, typeof(Light), "m_Color.g");
    ///        animationJob.handleB = animator.BindSceneProperty(sceneLight.transform, typeof(Light), "m_Color.b");
    ///        m_AnimationScriptPlayable = AnimationScriptPlayable.Create(m_Graph, animationJob);
    ///
    ///        output.SetSourcePlayable(m_AnimationScriptPlayable);
    ///        m_Graph.Play();
    ///    }
    ///
    ///    void OnDisable()
    ///    {
    ///        if (sceneLight == null)
    ///            return;
    ///
    ///        m_Graph.Destroy();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="AnimatorJobExtensions.BindSceneProperty" />
    ///<seealso cref="TransformSceneHandle" />
    ///<seealso cref="PropertyStreamHandle" />
    ///<seealso cref="TransformStreamHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/Director/AnimationSceneHandles.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertySceneHandle
    {
        private UInt32 valid;
        private int handleIndex;

        ///<summary>Returns whether or not the handle is valid.</summary>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>Whether or not the handle is valid.</returns>
        public bool IsValid(AnimationStream stream)
        {
            return IsValidInternal(ref stream);
        }

        private bool IsValidInternal(ref AnimationStream stream)
        {
            // [case 1032369] Cannot call native code before validating that handle was created in native and has a valid handle index
            return stream.isValid &&
                createdByNative &&
                hasHandleIndex &&
                HasValidTransform(ref stream);
        }

        private bool createdByNative
        {
            get { return valid != 0; }
        }

        private bool hasHandleIndex
        {
            get { return handleIndex != AnimationStream.InvalidIndex; }
        }

        ///<summary>Resolves the handle.</summary>
        ///<remarks>Handles are lazily resolved as they're accessed, but in order to prevent unwanted CPU spikes, this method allows to resolve handles in a deterministic way.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<seealso cref="IsResolved" />
        ///<seealso cref="AnimatorJobExtensions.ResolveAllStreamHandles" />
        public void Resolve(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            ResolveInternal(ref stream);
        }

        ///<summary>Returns whether or not the handle is resolved.</summary>
        ///<remarks>A PropertySceneHandle is resolved if it is valid, and if it is bound to the property. A PropertySceneHandle can become unresolved if the property doesn't exist anymore (e.g. the component has been removed).</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>Returns <c>true</c> if the handle is resolved, <c>false</c> otherwise.</returns>
        ///<seealso cref="Resolve" />
        ///<seealso cref="IsValid" />
        public bool IsResolved(AnimationStream stream)
        {
            return IsValidInternal(ref stream) && IsBound(ref stream);
        }

        private void CheckIsValid(ref AnimationStream stream)
        {
            // Verify stream.
            stream.CheckIsValid();

            // Handle create directly by user are never valid
            if (!createdByNative || !hasHandleIndex)
                throw new InvalidOperationException("The PropertySceneHandle is invalid. Please use proper function to create the handle.");

            // [case 1032369] Cannot call native code before validating that handle was created in native and has a valid handle index
            if (!HasValidTransform(ref stream))
                throw new NullReferenceException("The transform is invalid.");
        }

        ///<summary>Gets the float property value from an object in the Scene.</summary>
        ///<remarks>If the property is not a float, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>The float property value.</returns>
        public float GetFloat(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetFloatInternal(ref stream);
        }

        ///<summary>Sets the float property value to an object in the Scene.</summary>
        ///<remarks>If the property is not a float, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<param name="value">The new float property value.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetFloat(AnimationStream stream, float value) {}

        ///<summary>Gets the integer property value from an object in the Scene.</summary>
        ///<remarks>If the property is not an integer, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>The integer property value.</returns>
        public int GetInt(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetIntInternal(ref stream);
        }

        ///<summary>Gets the EntityId property value from an object in the Scene.</summary>
        ///<remarks>If the property is not an EntityId, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>The EntityId property value.</returns>
        public EntityId GetEntityId(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetEntityIdInternal(ref stream);
        }

        ///<summary>Sets the integer property value to an object in the Scene.</summary>
        ///<remarks>If the property is not an integer, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<param name="value">The new integer property value.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetInt(AnimationStream stream, int value) {}

        ///<summary>Gets the boolean property value from an object in the Scene.</summary>
        ///<remarks>If the property is not a boolean, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<returns>The boolean property value.</returns>
        public bool GetBool(AnimationStream stream)
        {
            CheckIsValid(ref stream);
            return GetBoolInternal(ref stream);
        }

        ///<summary>Sets the boolean property value to an object in the Scene.</summary>
        ///<remarks>If the property is not a boolean, the method will throw an <c>InvalidOperationException</c>.</remarks>
        ///<param name="stream">The <see cref="AnimationStream" /> managing this handle.</param>
        ///<param name="value">The new boolean property value.</param>
        [Obsolete("SceneHandle is now read-only; it was problematic with the engine multithreading and determinism", true)]
        public void SetBool(AnimationStream stream, bool value) {}

        [NativeMethod(IsThreadSafe = true)]
        private extern bool HasValidTransform(ref AnimationStream stream);

        [NativeMethod(IsThreadSafe = true)]
        private extern bool IsBound(ref AnimationStream stream);

        [NativeMethod(Name = "Resolve", IsThreadSafe = true)]
        private extern void ResolveInternal(ref AnimationStream stream);

        [NativeMethod(Name = "GetFloat", IsThreadSafe = true)]
        private extern float GetFloatInternal(ref AnimationStream stream);

        [NativeMethod(Name = "GetInt", IsThreadSafe = true)]
        private extern int GetIntInternal(ref AnimationStream stream);

        [NativeMethod(Name = "GetEntityId", IsThreadSafe = true)]
        private extern EntityId GetEntityIdInternal(ref AnimationStream stream);

        [NativeMethod(Name = "GetBool", IsThreadSafe = true)]
        private extern bool GetBoolInternal(ref AnimationStream stream);
    }

    ///<summary>Static class providing utility functions for animation scene handles.</summary>
    ///<seealso cref="AnimationStream" />
    ///<seealso cref="PropertySceneHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationStreamHandles.bindings.h")]
    unsafe public static class AnimationSceneHandleUtility
    {
        ///<summary>Reads integer properties from the PropertySceneHandle array (handles) and stores the integers in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertySceneHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores integer values.</param>
        public static void ReadInts(AnimationStream stream, NativeArray<PropertySceneHandle> handles, NativeArray<int> buffer)
        {
            int count = ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadSceneIntsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        ///<summary>Reads float properties from the PropertySceneHandle array (handles) and stores the floats in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertySceneHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores float values.</param>
        public static void ReadFloats(AnimationStream stream, NativeArray<PropertySceneHandle> handles, NativeArray<float> buffer)
        {
            int count = ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadSceneFloatsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        ///<summary>Reads EntityId properties from the PropertySceneHandle array (handles) and stores the EntityIds in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertySceneHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores EntityId values.</param>
        public static void ReadEntityIds(AnimationStream stream, NativeArray<PropertySceneHandle> handles, NativeArray<EntityId> buffer)
        {
            int count = ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadSceneEntityIdsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        internal static int ValidateAndGetArrayCount<T0, T1>(ref AnimationStream stream, NativeArray<T0> handles, NativeArray<T1> buffer)
            where T0 : struct
            where T1 : struct
        {
            stream.CheckIsValid();

            if (!handles.IsCreated)
                throw new NullReferenceException("Handle array is invalid.");
            if (!buffer.IsCreated)
                throw new NullReferenceException("Data buffer is invalid.");
            if (buffer.Length < handles.Length)
                throw new InvalidOperationException("Data buffer array is smaller than handles array.");

            return handles.Length;
        }

        // PropertySceneHandle
        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadSceneIntsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadSceneIntsInternal(ref AnimationStream stream, void* propertySceneHandles, void* intBuffer, int count);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadSceneFloatsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadSceneFloatsInternal(ref AnimationStream stream, void* propertySceneHandles, void* floatBuffer, int count);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadSceneEntityIdsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadSceneEntityIdsInternal(ref AnimationStream stream, void* propertySceneHandles, void* instanceIDBuffer, int count);
    }

    ///<summary>Static class providing utility functions for animation stream handles.</summary>
    ///<seealso cref="AnimationStream" />
    ///<seealso cref="PropertyStreamHandle" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationStreamHandles.bindings.h")]
    unsafe public static class AnimationStreamHandleUtility
    {
        ///<summary>Write integers from buffer to property stream handles.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to write to.</param>
        ///<param name="buffer">The buffer of integer properties.</param>
        ///<param name="useMask">Set to true to write new values if the matching stream handles have not already been modified.</param>
        public static void WriteInts(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<int> buffer, bool useMask)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            WriteStreamIntsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count, useMask);
        }

        ///<summary>Writes float properties from the buffer to the PropertyStreamHandle array (handles).</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to write to.</param>
        ///<param name="buffer">The buffer of float properties.</param>
        ///<param name="useMask">Set to true to write new values if the matching stream handles have not already been modified.</param>
        public static void WriteFloats(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<float> buffer, bool useMask)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            WriteStreamFloatsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count, useMask);
        }

        ///<summary>Write EntityIds from buffer to property stream handles.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to write to.</param>
        ///<param name="buffer">The buffer of EntityId properties.</param>
        ///<param name="useMask">Set to true to write new values if the matching stream handles have not already been modified.</param>
        public static void WriteEntityIds(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<EntityId> buffer, bool useMask)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            WriteStreamEntityIdsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count, useMask);
        }

        ///<summary>Reads integer properties from the PropertyStreamHandle array (handles) and stores the integers in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores integer values.</param>
        public static void ReadInts(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<int> buffer)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadStreamIntsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        ///<summary>Reads float properties from the PropertyStreamHandle array (handles) and stores the floats in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores float values.</param>
        public static void ReadFloats(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<float> buffer)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadStreamFloatsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        ///<summary>Reads EntityId properties from the PropertyStreamHandle array (handles) and stores the EntityIds in the provided buffer. The buffer must have enough allocated space to store all values.</summary>
        ///<param name="stream">The animation stream.</param>
        ///<param name="handles">The PropertyStreamHandle array to read from.</param>
        ///<param name="buffer">The buffer that stores EntityId values.</param>
        public static void ReadEntityIds(AnimationStream stream, NativeArray<PropertyStreamHandle> handles, NativeArray<EntityId> buffer)
        {
            stream.CheckIsValid();
            int count = AnimationSceneHandleUtility.ValidateAndGetArrayCount(ref stream, handles, buffer);
            if (count == 0)
                return;

            ReadStreamEntityIdsInternal(ref stream, handles.GetUnsafePtr(), buffer.GetUnsafePtr(), count);
        }

        // PropertyStreamHandle
        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadStreamIntsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadStreamIntsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* intBuffer, int count);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadStreamFloatsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadStreamFloatsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* floatBuffer, int count);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::ReadStreamEntityIdsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void ReadStreamEntityIdsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* instanceIDBuffer, int count);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::WriteStreamIntsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void WriteStreamIntsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* intBuffer, int count, bool useMask);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::WriteStreamFloatsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void WriteStreamFloatsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* floatBuffer, int count, bool useMask);

        [NativeMethod(Name = "AnimationHandleUtilityBindings::WriteStreamEntityIdsInternal", IsFreeFunction = true, HasExplicitThis = false, IsThreadSafe = true)]
        static private extern void WriteStreamEntityIdsInternal(ref AnimationStream stream, void* propertyStreamHandles, void* instanceIDBuffer, int count, bool useMask);
    }
}

