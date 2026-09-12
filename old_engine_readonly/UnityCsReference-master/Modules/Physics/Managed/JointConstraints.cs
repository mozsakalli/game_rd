// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.ComponentModel;

namespace UnityEngine
{
    ///<summary>The limits defined by the <see cref="CharacterJoint" />.</summary>
    public partial struct SoftJointLimit
    {
        private float m_Limit;
        private float m_Bounciness;
        private float m_ContactDistance;

        ///<summary>The limit position/angle of the joint (in degrees).</summary>
        public float limit { get { return m_Limit; } set { m_Limit = value; } }
        ///<summary>When the joint hits the limit, it can be made to bounce off it.</summary>
        ///<remarks>Bounciness determines how much to bounce off an limit.
        ///range { 0, 1 }.</remarks>
        public float bounciness { get { return m_Bounciness; } set { m_Bounciness = value; } }
        ///<summary>Determines how far ahead in space the solver can "see" the joint limit.</summary>
        ///<remarks>Distance inside the limit value at which the limit will be considered to be active by the solver.
        ///
        ///For translational joints the unit is meters.
        ///
        ///For rotational joints the unit is degrees.
        ///
        ///Setting this low can cause jittering, but might run faster.
        ///
        ///Setting this high can reduce jittering, but might run slower. Jointed objects will still fall asleep correctly.
        ///
        ///**0 = use defaults**
        ///
        ///
        ///Pipeline:
        ///<img src="Physics_ContactDistancePipeline.png" />.</remarks>
        public float contactDistance { get { return m_ContactDistance; } set { m_ContactDistance = value; } }
    }

    ///<summary>The configuration of the spring attached to the joint's limits: linear and angular. Used by <see cref="CharacterJoint" /> and <see cref="ConfigurableJoint" />.</summary>
    public struct SoftJointLimitSpring
    {
        private float m_Spring;
        private float m_Damper;

        ///<summary>The stiffness of the spring limit. When stiffness is zero the limit is hard, otherwise soft.</summary>
        ///<remarks>{ 0, infinity }.</remarks>
        public float spring { get { return m_Spring; } set { m_Spring = value; } }
        ///<summary>The damping of the spring limit. In effect when the stiffness of the sprint limit is not zero.</summary>
        ///<remarks>{ 0, infinity }.</remarks>
        public float damper { get { return m_Damper; } set { m_Damper = value; } }
    }

    ///<summary>How the joint's movement will behave along its local X axis.</summary>
    public partial struct JointDrive
    {
        private float m_PositionSpring;
        private float m_PositionDamper;
        private float m_MaximumForce;
        private int m_UseAcceleration;

        ///<summary>Strength of a rubber-band pull toward the defined direction. Only used if <c>mode</c> includes Position.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Create a JointDrive, configure it, and assign it to the zDrive of a ConfigurableJoint.
        ///    void Start()
        ///    {
        ///        ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();
        ///        joint.targetPosition = new Vector3(0, 0, -10);
        ///
        ///        JointDrive drive = new JointDrive
        ///        {
        ///            positionSpring = 50,         // Add a spring force to pull toward the target position
        ///            positionDamper = 10,         // Dampen oscillations
        ///            maximumForce = Mathf.Infinity // Allow unlimited force
        ///        };
        ///
        ///        joint.zDrive = drive; // Assign the configured drive to the zDrive of the joint
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public float positionSpring { get { return m_PositionSpring; } set { m_PositionSpring = value; } }
        ///<summary>Resistance strength against the Position Spring. Only used if <c>mode</c> includes Position.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Create a JointDrive, configure it, and assign it to the zDrive of a ConfigurableJoint.
        ///    void Start()
        ///    {
        ///        ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();
        ///        joint.targetPosition = new Vector3(0, 0, -10);
        ///
        ///        JointDrive drive = new JointDrive
        ///        {
        ///            positionSpring = 50,         // Add a spring force to pull toward the target position
        ///            positionDamper = 10,         // Dampen oscillations
        ///            maximumForce = Mathf.Infinity // Allow unlimited force
        ///        };
        ///
        ///        joint.zDrive = drive; // Assign the configured drive to the zDrive of the joint
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public float positionDamper { get { return m_PositionDamper; } set { m_PositionDamper = value; } }
        ///<summary>Amount of force applied to push the object toward the defined direction.</summary>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    // Create a JointDrive, configure it, and assign it to the zDrive of a ConfigurableJoint.
        ///    void Start()
        ///    {
        ///        ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();
        ///        joint.targetPosition = new Vector3(0, 0, -10);
        ///
        ///        JointDrive drive = new JointDrive
        ///        {
        ///            positionSpring = 50,         // Add a spring force to pull toward the target position
        ///            positionDamper = 10,         // Dampen oscillations
        ///            maximumForce = Mathf.Infinity // Allow unlimited force
        ///        };
        ///
        ///        joint.zDrive = drive; // Assign the configured drive to the zDrive of the joint
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public float maximumForce { get { return m_MaximumForce; } set { m_MaximumForce = value; } }
        ///<summary>Defines whether the drive is an acceleration drive or a force drive.</summary>
        public bool useAcceleration { get { return m_UseAcceleration == 1; } set { m_UseAcceleration = value ? 1 : 0; } }
    }

    ///<summary>The JointMotor is used to motorize a joint.</summary>
    ///<remarks>For example the <see cref="HingeJoint" /> can be told to rotate at a given speed and force.
    ///The joint will then attempt to reach the velocity with the given maximum force.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///public class Example : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        HingeJoint hinge = GetComponent<HingeJoint>();
    ///
    ///        // Make the hinge motor rotate with 90 degrees per second and a strong force.
    ///        JointMotor motor = hinge.motor;
    ///        motor.force = 100;
    ///        motor.targetVelocity = 90;
    ///        motor.freeSpin = false;
    ///        hinge.motor = motor;
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="HingeJoint" />
    public struct JointMotor
    {
        private float m_TargetVelocity;
        private float m_Force;
        private int m_FreeSpin;

        ///<summary>The motor will apply a force up to <c>force</c> to achieve <c>targetVelocity</c>.</summary>
        public float targetVelocity { get { return m_TargetVelocity; } set { m_TargetVelocity = value; } }
        ///<summary>The motor will apply a force.</summary>
        public float force { get { return m_Force; } set { m_Force = value; } }
        ///<summary>If <c>freeSpin</c> is enabled the motor will only accelerate but never slow down.</summary>
        public bool freeSpin { get { return m_FreeSpin == 1; } set { m_FreeSpin = value ? 1 : 0; } }
    }

    ///<summary>JointSpring is used add a spring force to <see cref="HingeJoint" /> and <see cref="PhysicsMaterial" />.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///public class Example : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        HingeJoint hinge = GetComponent<HingeJoint>();
    ///
    ///        // Make the spring reach shoot for a 70 degree angle.
    ///        // This could be used to fire off a catapult.
    ///
    ///        JointSpring spring = hinge.spring;
    ///        spring.spring = 10;
    ///        spring.damper = 3;
    ///        spring.targetPosition = 70;
    ///        hinge.spring = spring;
    ///    }
    ///}
    ///]]></code>
    ///</example>
    public struct JointSpring
    {
        ///<summary>The spring forces used to reach the target position.</summary>
        public float spring;
        ///<summary>The damper force uses to dampen the spring.</summary>
        public float damper;
        ///<summary>The target position the joint attempts to reach.</summary>
        ///<remarks>In the case of a <see cref="HingeJoint" /> the target position is the target angle in degrees.</remarks>
        public float targetPosition;

        // We have to keep those as public variables because of a bug in the C# raycast sample.
    }

    ///<summary>JointLimits is used by the <see cref="HingeJoint" /> to limit the joints angle.</summary>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    ///public class Example : MonoBehaviour
    ///{
    ///    void Start()
    ///    {
    ///        // Set the hinge limits for a door.
    ///        HingeJoint hinge = GetComponent<HingeJoint>();
    ///
    ///        JointLimits limits = hinge.limits;
    ///        limits.min = 0;
    ///        limits.max = 90;
    ///        limits.bounciness = 0;
    ///        limits.bounceMinVelocity = 0;
    ///        hinge.limits = limits;
    ///    }
    ///}
    ///]]></code>
    ///</example>
    ///<seealso cref="HingeJoint" />
    public struct JointLimits
    {
        private float m_Min;
        private float m_Max;
        private float m_Bounciness;
        private float m_BounceMinVelocity;
        private float m_ContactDistance;

        ///<summary>The lower angular limit (in degrees) of the joint.</summary>
        ///<remarks>When the joint angle or position is below it, the joint will exert forces to constrain it.</remarks>
        public float min { get { return m_Min; } set { m_Min = value; } }
        ///<summary>The upper angular limit (in degrees) of the joint.</summary>
        ///<remarks>When the joint angle or position is above it, the joint will exert forces to constrain it.</remarks>
        public float max { get { return m_Max; } set { m_Max = value; } }
        ///<summary>Determines the size of the bounce when the joint hits it's limit. Also known as restitution.</summary>
        ///<remarks>
        ///
        ///The **size** of the new velocity after the bounce will be determined as:
        ///
        ///<c>newVelocity = currentVelocity * bounciness</c>.
        ///
        ///When opening an old project using the deprecated <c>maxBounce</c> and <c>minBounce</c> the new <c>bounciness</c> will be chosen as the maximum <c>maxBounce</c> and <c>minBounce</c>.</remarks>
        ///<seealso cref="bounceMinVelocity" />
        public float bounciness { get { return m_Bounciness; } set { m_Bounciness = value; } }
        ///<summary>The minimum impact velocity which will cause the joint to bounce.</summary>
        ///<remarks>
        ///
        ///Setting this very low, like zero, will cause the joint to never stop bouncing. This can lead to jittering and performance problems.
        ///
        ///Setting this very high will cause to joint to never bounce.</remarks>
        ///<seealso cref="bounciness" />
        public float bounceMinVelocity { get { return m_BounceMinVelocity; } set { m_BounceMinVelocity = value; } }
        ///<summary>Distance inside the limit value at which the limit will be considered to be active by the solver.</summary>
        ///<remarks>Setting this low can cause jittering, but runs fast. Setting high can deduce jittering, but runs the solver more often. **(0 = use defaults)**
        ///
        ///For translational joints the unit is meters.
        ///
        ///For rotational joints the unit is degrees.
        ///
        ///Pipeline:
        ///<img src="Physics_ContactDistancePipeline.png" />.</remarks>
        public float contactDistance { get { return m_ContactDistance; } set { m_ContactDistance = value; } }

        // NB - member fields can't be in other partial structs, so we cannot move this out; work out a plan to remove them then
        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("minBounce and maxBounce are replaced by a single JointLimits.bounciness for both limit ends.", true)]
        public float minBounce;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Obsolete("minBounce and maxBounce are replaced by a single JointLimits.bounciness for both limit ends.", true)]
        public float maxBounce;
    }
}
