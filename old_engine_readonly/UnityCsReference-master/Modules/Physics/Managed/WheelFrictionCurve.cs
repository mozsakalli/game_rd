// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    //TODO: We should move this type into the VehicleModule assembly when possible.
    ///<summary>WheelFrictionCurve is used by the <see cref="T:UnityEngine.WheelCollider" /> to describe friction properties of the wheel tire.</summary>
    ///<remarks>The curve takes a measure of tire slip as an input and gives a force as output. The curve is approximated by
    ///a two-piece spline. The first section goes from (0,0) to (extremumSlip,extremumValue), at which
    ///point the curve's tangent is zero. The second section goes from (extremumSlip,extremumValue)
    ///to (asymptoteSlip,asymptoteValue), where curve's tangent is again zero:
    ///
    ///<img src="WheelFrictionCurve.png" />
    ///
    ///In the previous image a graph displays the wheel slip curve with force on the y-axis and slip on the x-axis. Force increases as slip increases up to the extremum point, after which force declines as slip increases up to the asymptote point. Beyond the asymptote the curve becomes a flat line as additional slip gives no further change in force.
    ///
    ///Wheel collider computes friction separately from the rest of physics engine, using a slip based
    ///friction model. It separates the overall friction force into a "forwards" component (in the
    ///direction of rolling, and responsible for acceleration and braking) and "sideways" component
    ///(orthogonal to rolling, responsible for keeping the car oriented). Tire friction is described
    ///separately in these directions using <see cref="P:UnityEngine.WheelCollider.forwardFriction" /> and <see cref="P:UnityEngine.WheelCollider.sidewaysFriction" />.
    ///In both directions it is first determined how much the tire is slipping (what is the speed difference between
    ///the rubber and the road). Then this slip value is used to find out tire force exerted on the contact.
    ///
    ///The property of real tires is that for low slip they can exert high forces as the rubber compensates
    ///for the slip by stretching. Later when the slip gets really high, the forces are reduced as the tire
    ///starts to slide or spin. Thus tire friction curves have a shape like in the image above.
    ///
    ///Because the friction for the tires is computed separately, the <see cref="PhysicsMaterial" /> of the ground
    ///does not affect the wheels. Simulation of different road materials is done by changing
    ///the <see cref="P:UnityEngine.WheelCollider.forwardFriction" /> and <see cref="P:UnityEngine.WheelCollider.sidewaysFriction" /> of the wheel,
    ///based on what material the wheel is hitting.</remarks>
    ///<seealso cref="T:UnityEngine.WheelCollider" />
    ///<seealso cref="P:UnityEngine.WheelCollider.forwardFriction" />
    ///<seealso cref="P:UnityEngine.WheelCollider.sidewaysFriction" />
    public struct WheelFrictionCurve
    {
        private float m_ExtremumSlip;
        private float m_ExtremumValue;
        private float m_AsymptoteSlip;
        private float m_AsymptoteValue;
        private float m_Stiffness;

        ///<summary>Extremum point slip (default 0.2f/0.4f).</summary>
        public float extremumSlip { get { return m_ExtremumSlip; } set { m_ExtremumSlip = value; } }
        ///<summary>Force at the extremum slip (default 1).</summary>
        public float extremumValue { get { return m_ExtremumValue; } set { m_ExtremumValue = value; } }
        ///<summary>Asymptote point slip (default 0.5f/0.8f).</summary>
        public float asymptoteSlip { get { return m_AsymptoteSlip; } set { m_AsymptoteSlip = value; } }
        ///<summary>Force at the asymptote slip (default 0.75f/0.5f).</summary>
        public float asymptoteValue { get { return m_AsymptoteValue; } set { m_AsymptoteValue = value; } }
        ///<summary>Multiplier for the <see cref="extremumValue" /> and <see cref="asymptoteValue" /> values (default 1).</summary>
        ///<remarks>Changes the stiffness of the friction. Setting this to zero
        ///will completely disable all friction from the wheel.
        ///
        ///Usually you modify <c>stiffness</c> to simulate various ground materials (e.g. lower the stiffness
        ///when driving on grass).</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using System.Collections;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public WheelCollider wheel;
        ///
        ///    void Start()
        ///    {
        ///        wheel = GetComponent<WheelCollider>();
        ///    }
        ///
        ///    // When attached to the WheelCollider, modifies tire friction based on
        ///    // static friction of the ground material.
        ///    void FixedUpdate()
        ///    {
        ///        WheelHit hit;
        ///        if (wheel.GetGroundHit(out hit))
        ///        {
        ///            WheelFrictionCurve fFriction = wheel.forwardFriction;
        ///            fFriction.stiffness = hit.collider.material.staticFriction;
        ///            wheel.forwardFriction = fFriction;
        ///            WheelFrictionCurve sFriction = wheel.sidewaysFriction;
        ///            sFriction.stiffness = hit.collider.material.staticFriction;
        ///            wheel.sidewaysFriction = sFriction;
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="M:UnityEngine.WheelCollider.GetGroundHit(UnityEngine.WheelHit@)" />
        public float stiffness { get { return m_Stiffness; } set { m_Stiffness = value; } }
    }
}
