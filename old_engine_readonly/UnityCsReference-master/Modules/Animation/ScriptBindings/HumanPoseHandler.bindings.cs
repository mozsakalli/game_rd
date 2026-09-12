// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine
{
    ///<summary>Retargetable humanoid pose.</summary>
    ///<remarks>Represents a humanoid pose that is completely abstracted from any skeleton rig.</remarks>
    ///<seealso cref="HumanPoseHandler" />
    public struct HumanPose
    {
        [NoAutoStaticsCleanup]
        static int k_NumIkGoals = Enum.GetValues(typeof(AvatarIKGoal)).Length;
        //These must stay in sync with the definition in HumanGetGoalOrientationOffset in human.cpp
        [NoAutoStaticsCleanup]
        internal static Quaternion[] s_IKGoalOffsets = { new Quaternion(0.5f, -0.5f, 0.5f, 0.5f), new Quaternion(0.5f, -0.5f, 0.5f, 0.5f), new Quaternion(0.707107f, 0, 0.707107f, 0), new Quaternion(0, 0.707107f, 0, 0.707107f) };

        ///<summary>The human body position for that pose.</summary>
        ///<remarks>Center of mass of the humanoid. The center of mass is approximated using a human average body parts mass distribution.</remarks>
        public Vector3 bodyPosition;
        ///<summary>The human body orientation for that pose.</summary>
        ///<remarks>Average body orientation. The average body orientation up vector is computed out of the hips and shoulders middle points. The front vector is then the cross product of the up vector and average left/right hips/shoulders vectors.</remarks>
        public Quaternion bodyRotation;
        ///<summary>The array of muscle values for that pose.</summary>
        ///<remarks>A muscle value moves a bone for one axis in the range [min,max] define in Humanoid Rig.</remarks>
        ///<seealso cref="HumanTrait" />
        public float[] muscles;
        internal Vector3 [] m_IkGoalPositions;
        internal Quaternion[] m_IkGoalRotations;
        internal Quaternion[] m_OffsetIkGoalRotations;


        ///<summary>The positions of the four IK goals: left foot, right foot, left hand and right hand in character space.</summary>
        ///<remarks>Use in conjunction with <see cref="HumanPose.bodyRotation" />, <see cref="HumanPose.bodyPosition" /> and <see cref="Animator.humanScale" /> to calculate the global position of Avatar IK effectors. See code example for details.
        ///
        ///**Note**: These values can be used to create Avatar IK goal position keyframes in Humanoid AnimationClips.</remarks>
        ///<example nocheck="true">
        ///  <code><![CDATA[{code Tests/EditModeAndPlayModeTests/Animation/Assets/DocumentationExamples/HumanPoseHandlerExamples.cs}]]></code>
        ///</example>
        public ReadOnlySpan<Vector3> ikGoalPositions => new ReadOnlySpan<Vector3>(m_IkGoalPositions);
        ///<summary>The rotations of the four IK goals: left foot, right foot, left hand and right hand, as used internally in the Humanoid system.</summary>
        ///<remarks>These values can be used to create Avatar IK goal rotation keyframes in Humanoid AnimationClips.
        ///
        ///
        ///**Note**: <see cref="HumanPose.internalIkGoalRotations" /> cannot be used directly to calculate global rotations, as they are offset to follow internal Humanoid logic. To calculate global IK goal rotations, see <see cref="HumanPose.ikGoalRotations" />.</remarks>
        public ReadOnlySpan<Quaternion> internalIkGoalRotations => new ReadOnlySpan<Quaternion>(m_IkGoalRotations);
        ///<summary>The rotations of the four IK goals: left foot, right foot, left hand and right hand in character space.</summary>
        ///<remarks>Use in conjunction with <see cref="HumanPose.bodyRotation" /> to calculate the global rotation of Avatar IK effectors. See code example for details.</remarks>
        ///<example nocheck="true">
        ///  <code><![CDATA[{code Tests/EditModeAndPlayModeTests/Animation/Assets/DocumentationExamples/HumanPoseHandlerExamples.cs}]]></code>
        ///</example>
        public ReadOnlySpan<Quaternion> ikGoalRotations => new ReadOnlySpan<Quaternion>(m_OffsetIkGoalRotations);


        internal void Init()
        {
            if (muscles != null)
            {
                if (muscles.Length != HumanTrait.MuscleCount)
                {
                    throw new InvalidOperationException("Bad array size for HumanPose.muscles. Size must equal HumanTrait.MuscleCount");
                }
            }

            if (muscles == null)
            {
                muscles = new float[HumanTrait.MuscleCount];

                if (bodyRotation.x == 0 && bodyRotation.y == 0 && bodyRotation.z == 0 && bodyRotation.w == 0)
                {
                    bodyRotation.w = 1;
                }
            }
            
            if (m_IkGoalPositions != null && m_IkGoalPositions.Length != k_NumIkGoals)
            {
                throw new InvalidOperationException("Bad array size for HumanPose.ikGoalPositions. Size must equal AvatakIKGoal size");
            }

            if (m_IkGoalPositions == null)
            {
                m_IkGoalPositions = new Vector3[k_NumIkGoals];
            }

            if (m_IkGoalRotations != null && m_IkGoalRotations.Length != k_NumIkGoals)
            {
                throw new InvalidOperationException("Bad array size for HumanPose.ikGoalPositions. Size must equal AvatakIKGoal size");
            }

            if (m_IkGoalRotations == null)
            {
                m_IkGoalRotations = new Quaternion[k_NumIkGoals];
            }

            if (m_OffsetIkGoalRotations != null && m_OffsetIkGoalRotations.Length != k_NumIkGoals)
            {
                throw new InvalidOperationException("Bad array size for HumanPose.ikGoalPositions. Size must equal AvatakIKGoal size");
            }

            if (m_OffsetIkGoalRotations == null)
            {
                m_OffsetIkGoalRotations = new Quaternion[k_NumIkGoals];
            }
        }
    }

    ///<summary>Use this class to create, read, and write the <see cref="HumanPose" /> for a humanoid avatar skeleton hierarchy or an avatar pose.</summary>
    [NativeHeader("Modules/Animation/HumanPoseHandler.h")]
    [NativeHeader("Modules/Animation/ScriptBindings/Animation.bindings.h")]
    public class HumanPoseHandler : IDisposable
    {
        internal IntPtr m_Ptr;

        [FreeFunction("AnimationBindings::CreateHumanPoseHandler")]
        extern private static IntPtr Internal_CreateFromRoot(Avatar avatar, Transform root);

        [FreeFunction("AnimationBindings::CreateHumanPoseHandler", IsThreadSafe = true)]
        extern private static IntPtr Internal_CreateFromJointPaths(Avatar avatar, string[] jointPaths);

        [FreeFunction("AnimationBindings::DestroyHumanPoseHandler")]
        extern private static void Internal_Destroy(IntPtr ptr);

        extern private void GetHumanPose(out Vector3 bodyPosition, out Quaternion bodyRotation, [Out] float[] muscles, [Out] Vector3[] ikGoalPositions, [Out] Quaternion[] ikGoalRotations);
        extern private void SetHumanPose(ref Vector3 bodyPosition, ref Quaternion bodyRotation, float[] muscles);

        [NativeMethod(IsThreadSafe = true)]
        extern private void GetInternalHumanPose(out Vector3 bodyPosition, out Quaternion bodyRotation, [Out] float[] muscles, [Out] Vector3[] ikGoalPositions, [Out] Quaternion[] ikGoalRotation);

        [NativeMethod(IsThreadSafe = true)]
        extern private void SetInternalHumanPose(ref Vector3 bodyPosition, ref Quaternion bodyRotation, float[] muscles);

        [NativeMethod(IsThreadSafe = true)]
        extern private unsafe void GetInternalAvatarPose(void* avatarPose, int avatarPoseLength);

        [NativeMethod(IsThreadSafe = true)]
        extern private unsafe void SetInternalAvatarPose(void* avatarPose, int avatarPoseLength);

        ///<exclude />
        public void Dispose()
        {
            if (m_Ptr != IntPtr.Zero)
            {
                Internal_Destroy(m_Ptr);
                m_Ptr = IntPtr.Zero;
            }

            GC.SuppressFinalize(this);
        }

        ///<summary>Creates a human pose handler from an <c>avatar</c> and a <c>root</c> transform and either a list of joint paths.</summary>
        ///<remarks>
        ///  <para>Specify an <c>avatar</c> and a <c>root</c> transform to create a human pose from the skeleton hierarchy (avatar).
        ///Specify an <c>avatar</c> and a list of joint paths to create a human pose handler that is not bound to a skeleton transform hierarchy.
        ///You can use a human pose handler created from joint paths to convert a human pose to or from an array of local joint transforms (jointPaths).</para>
        ///  <para>The above example creates a human pose handler from an avatar and a list of avatar joints. The example reads the pose from the avatar root transform and writes the pose to the human pose handler.</para>
        ///</remarks>
        ///<param name="avatar">The avatar from which <see cref="HumanPose" /> will be read or written. The avatar must be a humanoid.</param>
        ///<param name="root">The top most parent of the skeleton hierarchy defined in the humanoid <c>avatar</c>. This must match the <c>avatar</c> definition.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.Collections.Generic;
        ///using Unity.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public Avatar avatar;
        ///    public Transform avatarRoot;
        ///
        ///    void ParseAvatarTransformRecursive(Transform t, string parentPath, List<string> jointPaths, List<Transform> transforms)
        ///    {
        ///        string jointPath = parentPath.Length == 0 ? t.gameObject.name : parentPath + "/" + t.gameObject.name;
        ///        jointPaths.Add(jointPath);
        ///        transforms.Add(t);
        ///
        ///        foreach (Transform child in t)
        ///        {
        ///            ParseAvatarTransformRecursive(t, jointPath, jointPaths, transforms);
        ///        }
        ///    }
        ///
        ///    void ParseAvatarRootTransform(Transform rootTransform, List<string> jointPaths, List<Transform> transforms)
        ///    {
        ///        jointPaths.Add(""); // root tranform path is the empty string
        ///        transforms.Add(rootTransform);
        ///
        ///        foreach (Transform t in rootTransform)
        ///        {
        ///            ParseAvatarTransformRecursive(t, "", jointPaths, transforms);
        ///        }
        ///    }
        ///
        ///    void Start()
        ///    {
        ///        List<string> jointPaths = new List<string>();
        ///        List<Transform> avatarTransforms = new List<Transform>();
        ///        ParseAvatarRootTransform(avatarRoot, jointPaths, avatarTransforms);
        ///
        ///        HumanPoseHandler humanPoseHandler = new HumanPoseHandler(avatar, jointPaths.ToArray());
        ///        NativeArray<float> avatarPose = new NativeArray<float>(jointPaths.Count * 7, Allocator.Persistent);
        ///
        ///        for (int i = 0; i < jointPaths.Count; ++i)
        ///        {
        ///            Vector3 position = avatarTransforms[i].localPosition;
        ///            Quaternion rotation = avatarTransforms[i].localRotation;
        ///            avatarPose[7 * i] = position.x;
        ///            avatarPose[7 * i + 1] = position.y;
        ///            avatarPose[7 * i + 2] = position.z;
        ///            avatarPose[7 * i + 3] = rotation.x;
        ///            avatarPose[7 * i + 4] = rotation.y;
        ///            avatarPose[7 * i + 5] = rotation.z;
        ///            avatarPose[7 * i + 6] = rotation.w;
        ///        }
        ///
        ///        humanPoseHandler.SetInternalAvatarPose(avatarPose);
        ///
        ///        avatarPose.Dispose();
        ///        humanPoseHandler.Dispose();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public HumanPoseHandler(Avatar avatar, Transform root)
        {
            m_Ptr = IntPtr.Zero;

            if (root == null)
                throw new ArgumentNullException("HumanPoseHandler root Transform is null");

            if (avatar == null)
                throw new ArgumentNullException("HumanPoseHandler avatar is null");

            if (!avatar.isValid)
                throw new ArgumentException("HumanPoseHandler avatar is invalid");

            if (!avatar.isHuman)
                throw new ArgumentException("HumanPoseHandler avatar is not human");

            m_Ptr = Internal_CreateFromRoot(avatar, root);
        }

        ///<summary>Creates a human pose handler from an <c>avatar</c> and a <c>root</c> transform and either a list of joint paths.</summary>
        ///<remarks>
        ///  <para>Specify an <c>avatar</c> and a <c>root</c> transform to create a human pose from the skeleton hierarchy (avatar).
        ///Specify an <c>avatar</c> and a list of joint paths to create a human pose handler that is not bound to a skeleton transform hierarchy.
        ///You can use a human pose handler created from joint paths to convert a human pose to or from an array of local joint transforms (jointPaths).</para>
        ///  <para>The above example creates a human pose handler from an avatar and a list of avatar joints. The example reads the pose from the avatar root transform and writes the pose to the human pose handler.</para>
        ///</remarks>
        ///<param name="avatar">The avatar from which <see cref="HumanPose" /> will be read or written. The avatar must be a humanoid.</param>
        ///<param name="jointPaths">A list that defines the <c>avatar</c> joint paths. Each joint path starts from the node after the root transform and continues down the avatar skeleton hierarchy. The root transform joint path is an empty string.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///using System.Collections.Generic;
        ///using Unity.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public Avatar avatar;
        ///    public Transform avatarRoot;
        ///
        ///    void ParseAvatarTransformRecursive(Transform t, string parentPath, List<string> jointPaths, List<Transform> transforms)
        ///    {
        ///        string jointPath = parentPath.Length == 0 ? t.gameObject.name : parentPath + "/" + t.gameObject.name;
        ///        jointPaths.Add(jointPath);
        ///        transforms.Add(t);
        ///
        ///        foreach (Transform child in t)
        ///        {
        ///            ParseAvatarTransformRecursive(t, jointPath, jointPaths, transforms);
        ///        }
        ///    }
        ///
        ///    void ParseAvatarRootTransform(Transform rootTransform, List<string> jointPaths, List<Transform> transforms)
        ///    {
        ///        jointPaths.Add(""); // root tranform path is the empty string
        ///        transforms.Add(rootTransform);
        ///
        ///        foreach (Transform t in rootTransform)
        ///        {
        ///            ParseAvatarTransformRecursive(t, "", jointPaths, transforms);
        ///        }
        ///    }
        ///
        ///    void Start()
        ///    {
        ///        List<string> jointPaths = new List<string>();
        ///        List<Transform> avatarTransforms = new List<Transform>();
        ///        ParseAvatarRootTransform(avatarRoot, jointPaths, avatarTransforms);
        ///
        ///        HumanPoseHandler humanPoseHandler = new HumanPoseHandler(avatar, jointPaths.ToArray());
        ///        NativeArray<float> avatarPose = new NativeArray<float>(jointPaths.Count * 7, Allocator.Persistent);
        ///
        ///        for (int i = 0; i < jointPaths.Count; ++i)
        ///        {
        ///            Vector3 position = avatarTransforms[i].localPosition;
        ///            Quaternion rotation = avatarTransforms[i].localRotation;
        ///            avatarPose[7 * i] = position.x;
        ///            avatarPose[7 * i + 1] = position.y;
        ///            avatarPose[7 * i + 2] = position.z;
        ///            avatarPose[7 * i + 3] = rotation.x;
        ///            avatarPose[7 * i + 4] = rotation.y;
        ///            avatarPose[7 * i + 5] = rotation.z;
        ///            avatarPose[7 * i + 6] = rotation.w;
        ///        }
        ///
        ///        humanPoseHandler.SetInternalAvatarPose(avatarPose);
        ///
        ///        avatarPose.Dispose();
        ///        humanPoseHandler.Dispose();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public HumanPoseHandler(Avatar avatar, string[] jointPaths)
        {
            m_Ptr = IntPtr.Zero;

            if (jointPaths == null)
                throw new ArgumentNullException("HumanPoseHandler jointPaths array is null");

            if (avatar == null)
                throw new ArgumentNullException("HumanPoseHandler avatar is null");

            if (!avatar.isValid)
                throw new ArgumentException("HumanPoseHandler avatar is invalid");

            if (!avatar.isHuman)
                throw new ArgumentException("HumanPoseHandler avatar is not human");

            m_Ptr = Internal_CreateFromJointPaths(avatar, jointPaths);
        }

        static void CalculateIKOffsets(in Quaternion[] sourceRotations, ref Quaternion[] destRotations)
        {
            for (int i = 0; i < 4; i++)
                destRotations[i] = sourceRotations[i] * HumanPose.s_IKGoalOffsets[i];
        }

        ///<summary>Computes a human pose from the avatar skeleton, stores the pose in the human pose handler, and returns the human pose.</summary>
        ///<remarks>If the human pose handler was constructed with <c>jointPaths</c>, it is not bound to a skeleton transform hierarchy. In this case, <c>GetHumanPose</c> returns the internally stored human pose as the output.</remarks>
        ///<param name="humanPose">The output human pose. In the human pose, the <c>bodyPosition</c> and <c>bodyRotation</c> are the position and rotation of the approximate center of mass of the humanoid in world space. <c>bodyPosition</c> is normalized: the position is divided by <c>avatar</c> human scale.</param>
        public void GetHumanPose(ref HumanPose humanPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            humanPose.Init();
            GetHumanPose(out humanPose.bodyPosition, out humanPose.bodyRotation, humanPose.muscles, humanPose.m_IkGoalPositions, humanPose.m_IkGoalRotations);
            CalculateIKOffsets(humanPose.m_IkGoalRotations, ref humanPose.m_OffsetIkGoalRotations); 
        }

        ///<summary>Stores the specified human pose inside the human pose handler.</summary>
        ///<remarks>If the <c>HumanPoseHander</c> was constructed from an <c>avatar</c> and a <c>root</c>, the human pose is applied to the transform hierarchy representing the humanoid in the scene. If the <c>HumanPoseHander</c> was constructed from an <c>avatar</c> and <c>jointPaths</c>, the human pose is not bound to a transform hierarchy.</remarks>
        ///<param name="humanPose">The human pose to set. In the human pose, the <c>bodyPosition</c> and <c>bodyRotation</c> are the position and rotation of the approximate center of mass of the humanoid. This is relative to the humanoid root transform and it is normalized: the local position is divided by <c>avatar</c> human scale.</param>
        public void SetHumanPose(ref HumanPose humanPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            humanPose.Init();
            SetHumanPose(ref humanPose.bodyPosition, ref humanPose.bodyRotation, humanPose.muscles);
        }

        ///<summary>Gets the internal human pose stored in the human pose handler.</summary>
        ///<param name="humanPose">The output human pose. In the human pose, the <c>bodyPosition</c> and <c>bodyRotation</c> are the position and rotation of the approximate center of mass of the humanoid in world space. <c>bodyPosition</c> is normalized: the position is divided by <c>avatar</c> human scale.</param>
        public void GetInternalHumanPose(ref HumanPose humanPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            humanPose.Init();
            GetInternalHumanPose(out humanPose.bodyPosition, out humanPose.bodyRotation, humanPose.muscles, humanPose.m_IkGoalPositions, humanPose.m_IkGoalRotations);
            CalculateIKOffsets(humanPose.m_IkGoalRotations, ref humanPose.m_OffsetIkGoalRotations);
        }

        ///<summary>Stores the specified human pose as the internal human pose inside the human pose handler.</summary>
        ///<remarks>If the human pose handler is not bound to a transform hierarchy representing a humanoid in the scene, the humanoids's root transform is considered to be the identity transform.</remarks>
        ///<param name="humanPose">The human pose to set. In the human pose, the <c>bodyPosition</c> and <c>bodyRotation</c> are the position and rotation of the approximate center of mass of the humanoid. This is relative to the humanoid root transform and it is normalized: the local position is divided by <c>avatar</c> human scale.</param>
        public void SetInternalHumanPose(ref HumanPose humanPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            humanPose.Init();
            SetInternalHumanPose(ref humanPose.bodyPosition, ref humanPose.bodyRotation, humanPose.muscles);
        }

        ///<summary>Gets the internal human pose stored in the human pose handler and converts it to an avatar pose.</summary>
        ///<remarks>If the human pose handler was constructed with a skeleton root transform, this method does nothing.</remarks>
        ///<param name="avatarPose">The output avatar pose. The avatar pose is expressed as an array of floats. The floats represent the translation and rotation of the joints as local transforms.
        ///Each joint local transform is represented by 3 floats for the translation and 4 floats for the rotation (expressed as a quaternion). The joint transform is stored in the array in the same order as the joint paths in the <c>jointPaths</c> parameter used to construct the human pose handler. For example, if the human pose handler was constructed with 20 joint paths, the <c>avatarPose</c> parameter should be an array of 140 floats.</param>
        public unsafe void GetInternalAvatarPose(NativeArray<float> avatarPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            GetInternalAvatarPose(avatarPose.GetUnsafePtr(), avatarPose.Length);
        }

        ///<summary>Converts an avatar pose to a human pose and stores it as the internal human pose inside the human pose handler.</summary>
        ///<remarks>If the human pose handler was constructed with a skeleton root transform, this method does nothing.</remarks>
        ///<param name="avatarPose">The input avatar pose. The avatar pose is expressed as an array of floats. The floats represent the translation and rotation of the joints as local transforms.
        ///Each joint local transform is represented by 3 floats for the translation and 4 floats for the rotation (expressed as a quaternion). The joint transform is stored in the array in the same order as the joint paths in the <c>jointPaths</c> parameter used to construct the human pose handler. For example, if the human pose handler was constructed with 20 joint paths, the <c>avatarPose</c> parameter should be an array of 140 floats.</param>
        public unsafe void SetInternalAvatarPose(NativeArray<float> avatarPose)
        {
            if (m_Ptr == IntPtr.Zero)
                throw new NullReferenceException("HumanPoseHandler is not initialized properly");

            SetInternalAvatarPose(avatarPose.GetUnsafeReadOnlyPtr(), avatarPose.Length);
        }
        
        internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(HumanPoseHandler humanPoseHandler) => humanPoseHandler.m_Ptr;
        }
    }
}
