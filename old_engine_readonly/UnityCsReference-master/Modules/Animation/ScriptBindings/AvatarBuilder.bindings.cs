// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Collections;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Details of the Transform name mapped to the skeleton bone of a model and its default position and rotation in the T-pose.</summary>
    ///<remarks>The skeleton models used in Unity have multiple bones.  The <see cref="SkeletonBone" /> struct has properties that are used to describe the position, rotation and scale of each bone.  The bones are not shown.  A <see cref="M:UnityEngine.MonoBehaviour.OnDrawGizmosSelected" /> tool can be created to view the skeleton. An array of <see cref="SkeletonBone" /> positions can be used to make a line model using <see cref="Gizmos.DrawLine" />.
    ///
    ///An array of <see cref="SkeletonBone" />s are used in <see cref="HumanDescription.skeleton" />.</remarks>
    [NativeHeader("Modules/Animation/HumanDescription.h")]
    [RequiredByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    [NativeType(CodegenOptions.Custom, "MonoSkeletonBone")]
    public struct SkeletonBone
    {
        ///<summary>The name of the Transform mapped to the bone.</summary>
        [NativeName("m_Name")]
        public string     name;
        [NativeName("m_ParentName")]
        internal string   parentName;

        ///<summary>The T-pose position of the bone in local space.</summary>
        [NativeName("m_Position")]
        public Vector3    position;

        ///<summary>The T-pose rotation of the bone in local space.</summary>
        [NativeName("m_Rotation")]
        public Quaternion rotation;

        ///<summary>The T-pose scaling of the bone in local space.</summary>
        [NativeName("m_Scale")]
        public Vector3    scale;

        ///<exclude />
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("transformModified is no longer used and has been deprecated.", true)]
        public int transformModified { get { return 0; } set {} }
    }

    ///<summary>This class stores the rotation limits that define the muscle for a single human bone.</summary>
    [NativeHeader("Modules/Animation/ScriptBindings/AvatarBuilder.bindings.h")]
    [NativeHeader("Modules/Animation/HumanDescription.h")]
    [StructLayout(LayoutKind.Sequential)]
    [NativeType(CodegenOptions.Custom, "MonoHumanLimit")]
    public struct HumanLimit
    {
        Vector3 m_Min;
        Vector3 m_Max;
        Vector3 m_Center;
        float   m_AxisLength;
        int     m_UseDefaultValues;

        ///<summary>Should this limit use the default values?</summary>
        ///<remarks>You should set useDefaultValues to false if you want to use your own limit values, otherwise the defaults will override your settings.</remarks>
        public bool     useDefaultValues { get { return m_UseDefaultValues != 0; } set { m_UseDefaultValues = value ? 1 : 0; } }
        ///<summary>The maximum negative rotation away from the initial value that this muscle can apply.</summary>
        ///<remarks>The <see cref="center" /> property specifies the rotation of the bone when the muscle is at "rest". The <c>min</c> value specfies the maximum negative rotation in degrees away from the rest value that the muscle can apply. The <see cref="max" /> value specifies a similar limit but in the positive direction of rotation.
        ///
        ///The allowed range for the minimum is -180..0 degrees.</remarks>
        ///<seealso cref="HumanLimit.useDefaultValues" />
        public Vector3  min { get { return m_Min; } set { m_Min = value; } }
        ///<summary>The maximum rotation away from the initial value that this muscle can apply.</summary>
        ///<remarks>The <see cref="center" /> property specifies the rotation of the bone when the muscle is at "rest". The <c>max</c> value specfies the maximum rotation in degrees away from the rest value that the muscle can apply. The <see cref="min" /> value specifies a similar limit but in the negative direction of rotation.
        ///
        ///The allowed range for the maximum is 0..180 degrees.</remarks>
        ///<seealso cref="HumanLimit.useDefaultValues" />
        public Vector3  max { get { return m_Max; } set { m_Max = value; } }
        ///<summary>The default orientation of a bone when no muscle action is applied.</summary>
        ///<remarks>The vector value represents the bone's rotation in degrees around the X, Y and Z axes relative to the initial position of the bone in the skeleton. Any muscle rotation subsequently applied to the bone will be relative to this value.
        ///
        ///If <see cref="useDefaultValues" /> is enabled, the value of <c>center</c> will be [0, 0, 0].</remarks>
        ///<seealso cref="HumanLimit.useDefaultValues" />
        public Vector3  center { get { return m_Center; } set { m_Center = value; } }
        ///<summary>Length of the bone to which the limit is applied.</summary>
        ///<seealso cref="HumanLimit.useDefaultValues" />
        public float    axisLength { get { return m_AxisLength; } set { m_AxisLength = value; } }
    }

    ///<summary>The mapping between a bone in the model and the conceptual bone in the Mecanim human anatomy.</summary>
    ///<remarks>
    ///  <para>The names of the Mecanim human bone and the bone in the model are stored along with the limiting muscle values that constrain the bone's rotation during animation.</para>
    ///  <para />
    ///</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///using System.Collections.Generic;
    ///
    ///public class ExampleClass : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        Dictionary<string, string> boneName = new System.Collections.Generic.Dictionary<string, string>();
    ///        boneName["Chest"] = "Bip001 Spine2";
    ///        boneName["Head"] = "Bip001 Head";
    ///        boneName["Hips"] = "Bip001 Pelvis";
    ///        boneName["LeftFoot"] = "Bip001 L Foot";
    ///        boneName["LeftHand"] = "Bip001 L Hand";
    ///        boneName["LeftLowerArm"] = "Bip001 L Forearm";
    ///        boneName["LeftLowerLeg"] = "Bip001 L Calf";
    ///        boneName["LeftShoulder"] = "Bip001 L Clavicle";
    ///        boneName["LeftUpperArm"] = "Bip001 L UpperArm";
    ///        boneName["LeftUpperLeg"] = "Bip001 L Thigh";
    ///        boneName["RightFoot"] = "Bip001 R Foot";
    ///        boneName["RightHand"] = "Bip001 R Hand";
    ///        boneName["RightLowerArm"] = "Bip001 R Forearm";
    ///        boneName["RightLowerLeg"] = "Bip001 R Calf";
    ///        boneName["RightShoulder"] = "Bip001 R Clavicle";
    ///        boneName["RightUpperArm"] = "Bip001 R UpperArm";
    ///        boneName["RightUpperLeg"] = "Bip001 R Thigh";
    ///        boneName["Spine"] = "Bip001 Spine1";
    ///        string[] humanName = HumanTrait.BoneName;
    ///        HumanBone[] humanBones = new HumanBone[boneName.Count];
    ///        int j = 0;
    ///        int i = 0;
    ///        while (i < humanName.Length)
    ///        {
    ///            if (boneName.ContainsKey(humanName[i]))
    ///            {
    ///                HumanBone humanBone = new HumanBone();
    ///                humanBone.humanName = humanName[i];
    ///                humanBone.boneName = boneName[humanName[i]];
    ///                humanBone.limit.useDefaultValues = true;
    ///                humanBones[j++] = humanBone;
    ///            }
    ///            i++;
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="HumanDescription" />
    ///<seealso cref="AvatarBuilder" />
    [NativeHeader("Modules/Animation/HumanDescription.h")]
    [RequiredByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    [NativeType(CodegenOptions.Custom, "MonoHumanBone")]
    public struct HumanBone
    {
        string              m_BoneName;
        string              m_HumanName;

        ///<summary>The rotation limits that define the muscle for this bone.</summary>
        ///<remarks>The muscle contains the default orientation of the bone alone with the allowed limits of rotation away from the default around all three axes.</remarks>
        [NativeName("m_Limit")]
        public HumanLimit   limit;

        ///<summary>The name of the bone to which the Mecanim human bone is mapped.</summary>
        ///<remarks>The name of the <see cref="GameObject" /> representing the human bone.</remarks>
        ///<seealso cref="humanName" />
        public string   boneName { get { return m_BoneName; } set { m_BoneName = value; } }
        ///<summary>The name of the Mecanim human bone to which the bone from the model is mapped.</summary>
        ///<remarks>To get a list of all available human bones, use <see cref="HumanTrait.BoneName" />.</remarks>
        public string   humanName { get { return m_HumanName; } set { m_HumanName = value; } }
    }

    ///<summary>Class that holds humanoid avatar parameters to pass to the <see cref="AvatarBuilder.BuildHumanAvatar" /> function.</summary>
    [NativeHeader("Modules/Animation/ScriptBindings/AvatarBuilder.bindings.h")]
    [NativeHeader("Modules/Animation/HumanDescription.h")]
    [StructLayout(LayoutKind.Sequential)]
    public struct HumanDescription
    {
        ///<summary>Mapping between Mecanim bone names and bone names in the rig.</summary>
        ///<remarks>Each item in the array is a HumanBone object that contains a Mecanim avatar bone name, a bone name in the model (to which the Mecanim bone is mapped) and a "muscle" that specifies the bone's limits of motion. The bones can be listed in any order but there are a certain number of bones that Mecanim requires you to define; use <see cref="HumanTrait.RequiredBone" /> to get a list of all required bones.</remarks>
        ///<seealso cref="HumanBone" />
        ///<seealso cref="HumanTrait.BoneName" />
        ///<seealso cref="HumanTrait.RequiredBone" />
        [NativeName("m_Human")]
        public HumanBone[]      human;
        ///<summary>List of bone Transforms to include in the model.</summary>
        ///<remarks>This list defines which transforms to include in the final avatar skeleton. All parents from the human transform must be included in the list.</remarks>
        ///<seealso cref="SkeletonBone" />
        [NativeName("m_Skeleton")]
        public SkeletonBone[]   skeleton;

        internal float  m_ArmTwist;
        internal float  m_ForeArmTwist;
        internal float  m_UpperLegTwist;
        internal float  m_LegTwist;
        internal float  m_ArmStretch;
        internal float  m_LegStretch;
        internal float  m_FeetSpacing;
        internal float  m_GlobalScale;

        internal string  m_RootMotionBoneName;

        internal bool   m_HasTranslationDoF;

        internal bool   m_HasExtraRoot;
        internal bool   m_SkeletonHasParents;

        ///<summary>Defines how the upper arm's roll/twisting is distributed between the shoulder and elbow joints.</summary>
        ///<remarks>When the upper arm needs to twist or roll based on IK, <c>upperArmTwist</c> (a weighted range of 0..1) determines how much rotation is applied to the shoulder and elbow. When <c>upperArmTwist</c> is set to 0, the twist applies entirely to the shoulder. When set to 1, the twist applies entirely to the elbow. The default value of 0.5 evenly distributes the twist between both the shoulder and the elbow.</remarks>
        ///<seealso cref="HumanDescription.lowerArmTwist" />
        ///<seealso cref="HumanDescription.lowerLegTwist" />
        ///<seealso cref="HumanDescription.upperLegTwist" />
        public float    upperArmTwist { get { return m_ArmTwist; } set { m_ArmTwist = value; }   }
        ///<summary>Defines how the lower arm's roll/twisting is distributed between the elbow and wrist joints.</summary>
        ///<remarks>When the lower arm needs to twist or roll based on IK, <c>lowerArmTwist</c> (a weighted range of 0..1) determines how much rotation is applied to the wrist and elbow. When <c>lowerArmTwist</c> is set to 0, the twist applies entirely to the elbow. When set to 1, the twist applies entirely to the wrist. The default value of 0.5 evenly distributes the twist between both the elbow and the wrist.</remarks>
        ///<seealso cref="HumanDescription.upperArmTwist" />
        ///<seealso cref="HumanDescription.lowerLegTwist" />
        ///<seealso cref="HumanDescription.upperLegTwist" />
        public float    lowerArmTwist { get { return m_ForeArmTwist; } set { m_ForeArmTwist = value; }   }
        ///<summary>Defines how the upper leg's roll/twisting is distributed between the thigh and knee joints.</summary>
        ///<remarks>When the upper leg needs to twist or roll based on IK, <c>upperLegTwist</c> (a weighted range of 0..1) determines how much rotation is applied to the thigh and knee. When <c>upperLegTwist</c> is set to 0, the twist applies entirely to the thigh. When set to 1, the twist applies entirely to the knee. The default value of 0.5 evenly distributes the twist between both the thigh and the knee.</remarks>
        ///<seealso cref="HumanDescription.lowerArmTwist" />
        ///<seealso cref="HumanDescription.lowerLegTwist" />
        ///<seealso cref="HumanDescription.upperArmTwist" />
        public float    upperLegTwist { get { return m_UpperLegTwist; } set { m_UpperLegTwist = value; }     }
        ///<summary>Defines how the lower leg's roll/twisting is distributed between the knee and ankle.</summary>
        ///<remarks>When the lower leg needs to twist or roll based on IK, <c>lowerLegTwist</c> (a weighted range of 0..1) determines how much rotation is applied to the knee and ankle. When <c>lowerLegTwist</c> is set to 0, the twist applies entirely to the knee. When set to 1, the twist applies entirely to the ankle. The default value of 0.5 evenly distributes the twist between both the knee and the ankle.</remarks>
        ///<seealso cref="HumanDescription.upperArmTwist" />
        ///<seealso cref="HumanDescription.lowerArmTwist" />
        ///<seealso cref="HumanDescription.upperLegTwist" />
        public float    lowerLegTwist { get { return m_LegTwist; } set { m_LegTwist = value; }   }
        ///<summary>Amount by which the arm's length is allowed to stretch when using IK.</summary>
        ///<remarks>Inverse Kinematics (IK) can often be handled more smoothly if a small amount of "slack" is allowed in the positions of bones relative to each other. This property controls how much slack is available in the arm joints.
        ///
        ///The value is given in world distance units in the range 0..1. For example, with the default setting of 0.05, the arm will begin to stretch when the IK goal is at 95% of the target and will stretch by 5%. The stretch is carried out by translating both the elbow and wrist transforms.
        ///
        ///The ideal value will depend on the rig and the animation but in general, a larger value will make for a smoother IK computation at the expense of more unrealistic stretching of the arm.</remarks>
        ///<seealso cref="HumanDescription.legStretch" />
        public float    armStretch { get { return m_ArmStretch; } set { m_ArmStretch = value; }  }
        ///<summary>Amount by which the leg's length is allowed to stretch when using IK.</summary>
        ///<remarks>Inverse Kinematics (IK) can often be handled more smoothly if a small amount of "slack" is allowed in the positions of bones relative to each other. This property controls how much slack is available in the leg joints.
        ///
        ///The value is given in world distance units in the range 0..1. For example, with the default setting of 0.05, the leg will begin to stretch when the IK goal is at 95% of the target and will stretch by 5%. The stretch is carried out by translating both the knee and ankle transforms.
        ///
        ///The ideal value will depend on the rig and the animation but in general, a larger value will make for a smoother IK computation at the expense of more unrealistic stretching of the leg.</remarks>
        ///<seealso cref="HumanDescription.armStretch" />
        public float    legStretch { get { return m_LegStretch; } set { m_LegStretch = value; }  }
        ///<summary>Modification to the minimum distance between the feet of a humanoid model.</summary>
        ///<remarks>When a humanoid model has unusually large feet (a cartoon-like character, say) the meshes for the feet can sometimes interpenetrate during IK movement. The default value for <c>feetSpacing</c> is zero, but using a larger value will increase the minimum distance that is maintained between the feet and avoid interpenetration.</remarks>
        public float    feetSpacing { get { return m_FeetSpacing; } set { m_FeetSpacing = value; }   }
        ///<summary>True for any human that has a translation Degree of Freedom (DoF). It is set to false by default.</summary>
        ///<remarks>Translation DoF are on Spine, Chest, Neck, Shoulder and Upper Leg bones.</remarks>
        public bool     hasTranslationDoF { get { return m_HasTranslationDoF; } set { m_HasTranslationDoF = value; }}
        ///<summary>The name of the <see cref="Transform" /> to use as the root.</summary>
        ///<remarks>Set to designate which Transform to use as the source of Root Motion. This property is valid for Humanoid and Generic avatars.</remarks>
        public string rootMotionBoneName { get { return m_RootMotionBoneName; } set { m_RootMotionBoneName = value; } }
    }

    ///<summary>Class to build avatars from user scripts.</summary>
    ///<remarks>This class allows you to create custom avatars for your animated characters entirely via script, in a similar way to what goes on behind the Scenes in the Unity Editor when you create an avatar from the model import inspector.</remarks>
    [NativeHeader("Modules/Animation/ScriptBindings/AvatarBuilder.bindings.h")]
    public class AvatarBuilder
    {
        ///<summary>Create a humanoid avatar.</summary>
        ///<remarks>The avatar is created using the supplied HumanDescription object which specifies the muscle space range limits and retargeting parameters like arm/leg twist and arm/leg stretch.</remarks>
        ///<param name="go">Root object of your transform hierachy. It must be the top most gameobject when you create the avatar.</param>
        ///<param name="humanDescription">Humanoid description of the avatar.</param>
        ///<returns>Returns the Avatar, you must always always check the avatar is valid before using it with <see cref="Avatar.isValid" />.</returns>
        ///<example nocheck="true">
        ///  <code><![CDATA[{code Tests/EditModeAndPlayModeTests/Animation/Assets/DocumentationExamples/HumanAvatarBuilderExample.cs}]]></code>
        ///</example>
        ///<seealso cref="HumanDescription" />
        public static Avatar BuildHumanAvatar(GameObject go, HumanDescription humanDescription)
        {
            if (go == null)
                throw new NullReferenceException();

            return BuildHumanAvatarInternal(go, humanDescription);
        }

        [FreeFunction("AvatarBuilderBindings::BuildHumanAvatar")]
        extern private static Avatar BuildHumanAvatarInternal(GameObject go, HumanDescription humanDescription);

        ///<summary>Create a new generic avatar.</summary>
        ///<remarks>All transforms under the root game object will be part of this generic avatar.</remarks>
        ///<param name="go">Root object of your transform hierarchy.</param>
        ///<param name="rootMotionTransformName">Transform name of the root motion transform. If empty no root motion is defined and you must take care of avatar movement yourself.</param>
        ///<example nocheck="true">
        ///  <code><![CDATA[{code Tests/EditModeAndPlayModeTests/Animation/Assets/DocumentationExamples/GenericAvatarBuilderExample.cs}]]></code>
        ///</example>
        [FreeFunction("AvatarBuilderBindings::BuildGenericAvatar")]
        extern public static Avatar BuildGenericAvatar([NotNull] GameObject go, [NotNull] string rootMotionTransformName);
    }
}
