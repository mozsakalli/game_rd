// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;

namespace UnityEngine
{
    ///<summary>The mode in which particles are emitted.</summary>
    [Obsolete("ParticleSystemEmissionType no longer does anything. Time and Distance based emission are now both always active.", false)]
    public enum ParticleSystemEmissionType
    {
        ///<summary>Emit over time.</summary>
        Time = 0,
        ///<summary>Emit when emitter moves.</summary>
        Distance = 1
    }

    ///<summary>Script interface for the Built-in Particle System. Unity's powerful and versatile particle system implementation.</summary>
    ///<remarks>**General parameters**
    ///
    ///The Particle System's general parameters are kept inside a special Main module. These parameters are visible in the Inspector above all the other modules.
    ///
    ///In script, these parameters are accessible through <see cref="ParticleSystem.main" />.
    ///
    ///**Accessing module properties**
    ///
    ///Particle System properties are grouped by the module they belong to, such as <see cref="ParticleSystem.noise" /> and <see cref="ParticleSystem.emission" />. These properties are structs, but do not behave like normal C# structs. They are simply interfaces directly into the native code, so it is important to know how to use them, compared to a normal C# struct.
    ///
    ///The key difference is that it is not necessary to assign the struct back to the Particle System component. When you set any property on a module struct, Unity immediately assigns that value to the Particle System.
    ///
    ///Also, because each module is a struct, you must cache it in a local variable before you can assign any new values to the module. For example, instead of:
    ///
    ///<c>ParticleSystem.emission.enabled = true;    // Doesn't compile</c>
    ///
    ///write:
    ///
    ///<c>var emission = ParticleSystem.emission;    // Stores the module in a local variable</c><c>emission.enabled = true;    // Applies the new value directly to the Particle System</c>
    ///
    ///
    ///**Module effect multipliers**
    ///
    ///Every module has special multiplier properties that allow you to change the overall effect of a curve without having to edit the curve itself. These multiplier properties are all named after the curve they affect - for instance <see cref="ParticleSystem.EmissionModule.rateOverTimeMultiplier" /> controls the overall effect of <see cref="ParticleSystem.EmissionModule.rateOverTime" /> in a given system.
    ///
    ///**Constant value shorthand**
    ///
    ///Parameters support a shorthand notation for simple constant values. To set a constant value for a parameter, all you need to do is assign a number to it. It is not necessary to create a <see cref="MinMaxCurve" /> or <see cref="MinMaxGradient" /> object in the <see cref="ParticleSystemCurveMode.Constant" /> mode.
    ///
    ///For example, instead of:
    ///
    ///<c>var emission = ParticleSystem.emission;</c><c>emission.rate = new ParticleSystem.MinMaxCurve(5.0f);</c>
    ///
    ///write:
    ///
    ///<c>var emission = ParticleSystem.emission;</c><c>emission.rate = 5.0f;</c>
    ///
    ///**Performance note**: When setting properties on particle modules, the settings are passed immediately into native code. This gives the best performance. This means that setting properties on a module struct doesn't set something in script that requires setting back to the Particle System; it all happens automatically.</remarks>
    ///<seealso cref="ParticleSystem.Particle" />
    public partial class ParticleSystem
    {
        ///<summary>Script interface for a Min-Max Curve.</summary>
        ///<remarks>Min-Max Curve. describes functions which take a value between a minimum and maximum limit and return a value based on <see cref="ParticleSystem.MinMaxCurve.mode" />. Depending on the mode, this may return randomized values.
        ///For modes that require curves, the value returned is dependent on one or two curves designed in the ParticleSystem Inspector, that can be evaluated to a single value between -n and n, where n is a constant also set in the Inspector. See <see cref="ParticleSystemCurveMode" /> for more information.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // This example shows setting a constant rate value.
        ///public class ConstantRateExample : MonoBehaviour
        ///{
        ///    ParticleSystem myParticleSystem;
        ///    ParticleSystem.EmissionModule emissionModule;
        ///
        ///    void Start()
        ///    {
        ///        // Get the system and the emission module.
        ///        myParticleSystem = GetComponent<ParticleSystem>();
        ///        emissionModule = myParticleSystem.emission;
        ///
        ///        GetValue();
        ///        SetValue();
        ///    }
        ///
        ///    void GetValue()
        ///    {
        ///        print("The constant value is " + emissionModule.rateOverTime.constant);
        ///    }
        ///
        ///    void SetValue()
        ///    {
        ///        emissionModule.rateOverTime = 10.0f;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // This example shows using 2 constants to drive the rate.
        ///public class TwoConstantsRateExample : MonoBehaviour
        ///{
        ///    ParticleSystem myParticleSystem;
        ///    ParticleSystem.EmissionModule emissionModule;
        ///
        ///    void Start()
        ///    {
        ///        // Get the system and the emission module.
        ///        myParticleSystem = GetComponent<ParticleSystem>();
        ///        emissionModule = myParticleSystem.emission;
        ///
        ///        GetValue();
        ///        SetValue();
        ///    }
        ///
        ///    void GetValue()
        ///    {
        ///        print(string.Format("The constant values are: min {0} max {1}.", emissionModule.rateOverTime.constantMin, emissionModule.rateOverTime.constantMax));
        ///    }
        ///
        ///    void SetValue()
        ///    {
        ///        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(0.0f, 10.0f);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // This example shows using a curve to drive the rate.
        ///public class CurveRateExample : MonoBehaviour
        ///{
        ///    ParticleSystem myParticleSystem;
        ///    ParticleSystem.EmissionModule emissionModule;
        ///
        ///    // We can "scale" the curve with this value. It gets multiplied by the curve.
        ///    public float scalar = 1.0f;
        ///
        ///    AnimationCurve ourCurve;
        ///
        ///    void Start()
        ///    {
        ///        // Get the system and the emission module.
        ///        myParticleSystem = GetComponent<ParticleSystem>();
        ///        emissionModule = myParticleSystem.emission;
        ///
        ///        // A simple linear curve.
        ///        ourCurve = new AnimationCurve();
        ///        ourCurve.AddKey(0.0f, 0.0f);
        ///        ourCurve.AddKey(1.0f, 1.0f);
        ///
        ///        // Apply the curve.
        ///        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scalar, ourCurve);
        ///
        ///        // In 5 seconds we will modify the curve.
        ///        Invoke("ModifyCurve", 5.0f);
        ///    }
        ///
        ///    void ModifyCurve()
        ///    {
        ///        // Add a key to the current curve.
        ///        ourCurve.AddKey(0.5f, 0.0f);
        ///
        ///        // Apply the changed curve.
        ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(scalar, ourCurve);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // This example shows using 2 curves to drive the rate.
        ///public class TwoCurveRateExample : MonoBehaviour
        ///{
        ///    ParticleSystem myParticleSystem;
        ///    ParticleSystem.EmissionModule emissionModule;
        ///
        ///    AnimationCurve ourCurveMin;
        ///    AnimationCurve ourCurveMax;
        ///
        ///    // We can "scale" the curves with this value. It gets multiplied by the curves.
        ///    public float scalar = 1.0f;
        ///
        ///    void Start()
        ///    {
        ///        // Get the system and the emission module.
        ///        myParticleSystem = GetComponent<ParticleSystem>();
        ///        emissionModule = myParticleSystem.emission;
        ///
        ///        // A horizontal straight line at value 1.
        ///        ourCurveMin = new AnimationCurve();
        ///        ourCurveMin.AddKey(0.0f, 1.0f);
        ///        ourCurveMin.AddKey(1.0f, 1.0f);
        ///
        ///        // A horizontal straight line at value 0.5.
        ///        ourCurveMax = new AnimationCurve();
        ///        ourCurveMax.AddKey(0.0f, 0.5f);
        ///        ourCurveMax.AddKey(1.0f, 0.5f);
        ///
        ///        // Apply the curves.
        ///        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scalar, ourCurveMin, ourCurveMax);
        ///
        ///        // In 5 seconds we will modify the curve.
        ///        Invoke("ModifyCurve", 5.0f);
        ///    }
        ///
        ///    void ModifyCurve()
        ///    {
        ///        // Create a "pinch" point.
        ///        ourCurveMin.AddKey(0.5f, 0.7f);
        ///        ourCurveMax.AddKey(0.5f, 0.6f);
        ///
        ///        // Apply the changed curve.
        ///        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scalar, ourCurveMin, ourCurveMax);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // This example shows how to retrieve existing keys from a MinMaxCurve
        ///public class ReadCurveExample : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        // Get the system and the emission module.
        ///        var myParticleSystem = GetComponent<ParticleSystem>();
        ///        var emissionModule = myParticleSystem.emission;
        ///
        ///        // Get the curve (assuming the MinMaxCurve is in Curve mode)
        ///        AnimationCurve curve = emissionModule.rateOverTime.curve;
        ///
        ///        // Get the keys
        ///        Keyframe[] keys = curve.keys;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="ParticleSystem" />
        public partial struct MinMaxCurve
        {
            ///<exclude />
            [Obsolete("Please use MinMaxCurve.curveMultiplier instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MinMaxCurve.curveMultiplier", false)]
            public float curveScalar { get { return m_CurveMultiplier; } set { m_CurveMultiplier = value; } }
        }

        ///<summary>Script interface for MainModule.</summary>
        ///<remarks>This module provides access to the general settings that are displayed above all of the other module settings in the Particle System's Inspector window.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.main" />
        public partial struct MainModule
        {
            ///<summary>Cause some particles to spin in the opposite direction.</summary>
            ///<remarks>Set between 0 and 1. Higher values cause a higher proportion of particles to spin in the opposite direction.</remarks>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///using System.Collections;
            ///
            ///[RequireComponent(typeof(ParticleSystem))]
            ///public class ExampleClass : MonoBehaviour
            ///{
            ///    private ParticleSystem ps;
            ///    public float hSliderValue = 0.0F;
            ///
            ///    void Start()
            ///    {
            ///        ps = GetComponent<ParticleSystem>();
            ///
            ///        var rot = ps.rotationOverLifetime;
            ///        rot.enabled = true;
            ///        rot.zMultiplier = 90.0f * Mathf.Deg2Rad;
            ///
            ///        var psr = GetComponent<ParticleSystemRenderer>();
            ///        psr.material = new Material(Shader.Find("Sprites/Default"));    // this material renders a square billboard, so we can see the rotation
            ///    }
            ///
            ///    void Update()
            ///    {
            ///        var main = ps.main;
            ///        main.randomizeRotationDirection = hSliderValue;
            ///    }
            ///
            ///    void OnGUI()
            ///    {
            ///        hSliderValue = GUI.HorizontalSlider(new Rect(25, 25, 100, 30), hSliderValue, 0.0F, 1.0F);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            [Obsolete("Please use flipRotation instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/MainModule.flipRotation", false)]
            public float randomizeRotationDirection { get { return flipRotation; } set { flipRotation = value; } }
        }

        ///<summary>Script interface for EmissionModule.</summary>
        ///<remarks>The EmissionModule provides control over how many particles that the system has emitted.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.emission" />
        public partial struct EmissionModule
        {
            ///<summary>The emission type.</summary>
            [Obsolete("ParticleSystemEmissionType no longer does anything. Time and Distance based emission are now both always active.", false)]
            public ParticleSystemEmissionType type { get { return ParticleSystemEmissionType.Time; } set {} }
            ///<summary>The rate at which the system spawns new particles.</summary>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            /// // This example shows setting a constant rate value.
            ///public class ConstantRateExample : MonoBehaviour
            ///{
            ///    ParticleSystem myParticleSystem;
            ///    ParticleSystem.EmissionModule emissionModule;
            ///
            ///    void Start()
            ///    {
            ///        // Get the system and the emission module.
            ///        myParticleSystem = GetComponent<ParticleSystem>();
            ///        emissionModule = myParticleSystem.emission;
            ///
            ///        GetValue();
            ///        SetValue();
            ///    }
            ///
            ///    void GetValue()
            ///    {
            ///        print("The constant value is " + emissionModule.rate.constant);
            ///    }
            ///
            ///    void SetValue()
            ///    {
            ///        emissionModule.rate = 10.0f;
            ///    }
            ///}
            ///]]></code>
            ///</example>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            /// // This example shows using 2 constants to drive the rate.
            ///public class TwoConstantsRateExample : MonoBehaviour
            ///{
            ///    ParticleSystem myParticleSystem;
            ///    ParticleSystem.EmissionModule emissionModule;
            ///
            ///    void Start()
            ///    {
            ///        // Get the system and the emission module.
            ///        myParticleSystem = GetComponent<ParticleSystem>();
            ///        emissionModule = myParticleSystem.emission;
            ///
            ///        GetValue();
            ///        SetValue();
            ///    }
            ///
            ///    void GetValue()
            ///    {
            ///        print(string.Format("The constant values are: min {0} max {1}.", emissionModule.rate.constantMin, emissionModule.rate.constantMax));
            ///    }
            ///
            ///    void SetValue()
            ///    {
            ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(0.0f, 10.0f);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            /// // This example shows using a curve to drive the rate.
            ///public class CurveRateExample : MonoBehaviour
            ///{
            ///    ParticleSystem myParticleSystem;
            ///    ParticleSystem.EmissionModule emissionModule;
            ///
            ///    // We can "scale" the curve with this value. It gets multiplied by the curve.
            ///    public float scalar = 1.0f;
            ///
            ///    AnimationCurve ourCurve;
            ///
            ///    void Start()
            ///    {
            ///        // Get the system and the emission module.
            ///        myParticleSystem = GetComponent<ParticleSystem>();
            ///        emissionModule = myParticleSystem.emission;
            ///
            ///        // A simple linear curve.
            ///        ourCurve = new AnimationCurve();
            ///        ourCurve.AddKey(0.0f, 0.0f);
            ///        ourCurve.AddKey(1.0f, 1.0f);
            ///
            ///        // Apply the curve.
            ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(scalar, ourCurve);
            ///
            ///        // In 5 seconds we will modify the curve.
            ///        Invoke("ModifyCurve", 5.0f);
            ///    }
            ///
            ///    void ModifyCurve()
            ///    {
            ///        // Add a key to the current curve.
            ///        ourCurve.AddKey(0.5f, 0.0f);
            ///
            ///        // Apply the changed curve.
            ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(scalar, ourCurve);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            /// // This example shows using 2 curves to drive the rate.
            ///public class TwoCurveRateExample : MonoBehaviour
            ///{
            ///    ParticleSystem myParticleSystem;
            ///    ParticleSystem.EmissionModule emissionModule;
            ///
            ///    AnimationCurve ourCurveMin;
            ///    AnimationCurve ourCurveMax;
            ///
            ///    // We can "scale" the curves with this value. It gets multiplied by the curves.
            ///    public float scalar = 1.0f;
            ///
            ///    void Start()
            ///    {
            ///        // Get the system and the emission module.
            ///        myParticleSystem = GetComponent<ParticleSystem>();
            ///        emissionModule = myParticleSystem.emission;
            ///
            ///        // A horizontal straight line at value 1.
            ///        ourCurveMin = new AnimationCurve();
            ///        ourCurveMin.AddKey(0.0f, 1.0f);
            ///        ourCurveMin.AddKey(1.0f, 1.0f);
            ///
            ///        // A horizontal straight line at value 0.5.
            ///        ourCurveMax = new AnimationCurve();
            ///        ourCurveMax.AddKey(0.0f, 0.5f);
            ///        ourCurveMax.AddKey(1.0f, 0.5f);
            ///
            ///        // Apply the curves.
            ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(scalar, ourCurveMin, ourCurveMax);
            ///
            ///        // In 5 seconds we will modify the curve.
            ///        Invoke("ModifyCurve", 5.0f);
            ///    }
            ///
            ///    void ModifyCurve()
            ///    {
            ///        // Create a "pinch" point.
            ///        ourCurveMin.AddKey(0.5f, 0.7f);
            ///        ourCurveMax.AddKey(0.5f, 0.6f);
            ///
            ///        // Apply the changed curve.
            ///        emissionModule.rate = new ParticleSystem.MinMaxCurve(scalar, ourCurveMin, ourCurveMax);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            ///<seealso cref="ParticleSystem.MinMaxCurve" />
            [Obsolete("rate property is deprecated. Use rateOverTime or rateOverDistance instead.", false)]
            public MinMaxCurve rate { get { return rateOverTime; } set { rateOverTime = value; } }
            ///<summary>Change the rate multiplier.</summary>
            ///<remarks>Changing this property is more efficient than accessing the entire curve, if you only want to change the overall rate multiplier.</remarks>
            [Obsolete("rateMultiplier property is deprecated. Use rateOverTimeMultiplier or rateOverDistanceMultiplier instead.", false)]
            public float rateMultiplier { get { return rateOverTimeMultiplier; } set { rateOverTimeMultiplier = value; } }
        }

        ///<summary>Script interface for the ShapeModule.</summary>
        ///<remarks>Configures the initial positions and directions of particles.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.shape" />
        public partial struct ShapeModule
        {
            ///<summary>Scale of the box to emit particles from.</summary>
            ///<seealso cref="ParticleSystem.ShapeModule.shapeType" />
            [Obsolete("Please use scale instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/ShapeModule.scale", false)]
            public Vector3 box { get { return scale; } set { scale = value; } }
            ///<summary>Apply a scaling factor to the Mesh that emits the particles.</summary>
            ///<seealso cref="ParticleSystem.ShapeModule.shapeType" />
            [Obsolete("meshScale property is deprecated.Please use scale instead.", false)]
            public float meshScale { get { return scale.x; } set { scale = new Vector3(value, value, value); } }
            ///<summary>Randomizes the starting direction of particles.</summary>
            ///<seealso cref="ParticleSystem.ShapeModule.shapeType" />
            [Obsolete("randomDirection property is deprecated. Use randomDirectionAmount instead.", false)]
            public bool randomDirection { get { return (randomDirectionAmount >= 0.5f); } set { randomDirectionAmount = value ? 1.0f : 0.0f; } }
        }

        ///<summary>Script interface for CollisionModule.</summary>
        ///<remarks>CollisionModule allows particles to collide with a predefined list of planes, or with the 2D and 3D physics worlds.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.collision" />
        public partial struct CollisionModule
        {
            ///<summary>The maximum number of planes it is possible to set as colliders.</summary>
            [Obsolete("The maxPlaneCount restriction has been removed. Please use planeCount instead to find out how many planes there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/CollisionModule.planeCount", false)]
            public int maxPlaneCount { get { return planeCount; } }
        }

        ///<summary>Script interface for the TriggerModule.</summary>
        ///<remarks>This module is useful for killing particles when they touch a set of collision shapes, or for calling a script command to let you apply custom particle behaviors when the trigger is activated.
        ///
        ///The example code for <see cref="M:UnityEngine.MonoBehaviour.OnParticleTrigger" /> shows how the callback type action works.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.trigger" />
        public partial struct TriggerModule
        {
            ///<summary>The maximum number of collision shapes that can be attached to this Particle System trigger.</summary>
            [Obsolete("The maxColliderCount restriction has been removed. Please use colliderCount instead to find out how many colliders there are. (UnityUpgradable) -> UnityEngine.ParticleSystem/TriggerModule.colliderCount", false)]
            public int maxColliderCount { get { return colliderCount; } }
        }

        ///<summary>Script interface for the SubEmittersModule.</summary>
        ///<remarks>The sub-emitters module allows you to spawn particles in child emitters from the positions of particles in the parent system.
        ///
        ///This module triggers child particle emission on events such as the birth, death, and collision of particles in the parent system.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.subEmitters" />
        public partial struct SubEmittersModule
        {
            ///<summary>Sub-Particle System which spawns at the locations of the birth of the particles from the parent system.</summary>
            [Obsolete("birth0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem birth0 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }
            ///<summary>Sub-Particle System which spawns at the locations of the birth of the particles from the parent system.</summary>
            [Obsolete("birth1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem birth1 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }
            ///<summary>Sub-Particle System which spawns at the locations of the collision of the particles from the parent system.</summary>
            ///<remarks>This module's Particle System uses the first set of emission burst properties to spawn a sub-Particle System. If the system does not contain burst properties, then the system emits nothing; if the system contains more than one set of burst properties, then it ignores the additional properties.</remarks>
            [Obsolete("collision0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem collision0 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }
            ///<summary>Sub-Particle System which spawns at the locations of the collision of the particles from the parent system.</summary>
            ///<remarks>This module's Particle System uses the first set of emission burst properties to spawn a sub-Particle System. If the system does not contain burst properties, then the system emits nothing; if the system contains more than one set of burst properties, then it ignores the additional properties.</remarks>
            [Obsolete("collision1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem collision1 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }
            ///<summary>Sub-Particle System which spawns at the locations of the death of the particles from the parent system.</summary>
            ///<remarks>This module's Particle System uses the first set of emission burst properties to spawn a sub-Particle System. If the system does not contain burst properties, then the system emits nothing; if the system contains more than one set of burst properties, then it ignores the additional properties.</remarks>
            [Obsolete("death0 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem death0 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }
            ///<summary>Sub-Particle System to spawn on death of the parent system's particles.</summary>
            ///<remarks>The sub-Particle System will be emitted using the first emission burst parameters. If the system does not contain burst parameters, then nothing will be emitted; if the system contains more than one burst parameter, then the additional parameters will not be used.</remarks>
            [Obsolete("death1 property is deprecated. Use AddSubEmitter, RemoveSubEmitter, SetSubEmitterSystem and GetSubEmitterSystem instead.", false)]
            public ParticleSystem death1 { get { ThrowNotImplemented(); return null; } set { ThrowNotImplemented(); } }

            static void ThrowNotImplemented()
            {
                throw new NotImplementedException();
            }
        }

        ///<summary>Script interface for the TextureSheetAnimationModule.</summary>
        ///<remarks>This module allows you to add animations to your particle textures. To author an animation, you must use a flipbook Texture.
        ///
        ///<img src="ParticleFlipbook.png" />
        ///
        ///A flipbook texture sheet that contains eight sub-images of the numbers 1-8 across two rows of four columns. The first row contains the numbers 1-4 and the second row contains the numbers 5-8.
        ///
        ///Each numbered region represents a frame of the animation, which you must distribute evenly across the Texture.
        ///Select a variable below to see script examples. You may want to use this Texture on your Particle System with each example, to see how the module works.</remarks>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.textureSheetAnimation" />
        public partial struct TextureSheetAnimationModule
        {
            ///<summary>Flip the U coordinate on particles, causing them to appear mirrored horizontally.</summary>
            ///<remarks>Set between 0 and 1, where higher values mirror a higher proportion of particles, and a value of 1 mirrors all particles.</remarks>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            ///[RequireComponent(typeof(ParticleSystem))]
            ///public class ExampleClass : MonoBehaviour
            ///{
            ///    private ParticleSystem ps;
            ///    public float flipU = 0.0f;
            ///    public float flipV = 0.0f;
            ///
            ///    void Start()
            ///    {
            ///        ps = GetComponent<ParticleSystem>();
            ///
            ///        var main = ps.main;
            ///        main.startLifetimeMultiplier = 2.0f;
            ///
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.enabled = true;
            ///        tex.numTilesX = 4;
            ///        tex.numTilesY = 2;
            ///    }
            ///
            ///    void Update()
            ///    {
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.flipU = flipU;
            ///        tex.flipV = flipV;
            ///    }
            ///
            ///    void OnGUI()
            ///    {
            ///        GUI.Label(new Rect(25, 20, 100, 30), "Flip U Amount");
            ///        GUI.Label(new Rect(25, 60, 100, 30), "Flip V Amount");
            ///
            ///        flipU = GUI.HorizontalSlider(new Rect(125, 25, 100, 30), flipU, 0.0f, 1.0f);
            ///        flipV = GUI.HorizontalSlider(new Rect(125, 65, 100, 30), flipV, 0.0f, 1.0f);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            [Obsolete("flipU property is deprecated. Use ParticleSystemRenderer.flip.x instead.", false)]
            public float flipU { get { return m_ParticleSystem.GetComponent<ParticleSystemRenderer>().flip.x; } set { var psr = m_ParticleSystem.GetComponent<ParticleSystemRenderer>(); var flip = psr.flip; flip.x = value; psr.flip = flip; } }
            ///<summary>Flip the V coordinate on particles, causing them to appear mirrored vertically.</summary>
            ///<remarks>Set between 0 and 1, where higher values mirror a higher proportion of particles, and a value of 1 mirrors all particles.</remarks>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            ///[RequireComponent(typeof(ParticleSystem))]
            ///public class ExampleClass : MonoBehaviour
            ///{
            ///    private ParticleSystem ps;
            ///    public float flipU = 0.0f;
            ///    public float flipV = 0.0f;
            ///
            ///    void Start()
            ///    {
            ///        ps = GetComponent<ParticleSystem>();
            ///
            ///        var main = ps.main;
            ///        main.startLifetimeMultiplier = 2.0f;
            ///
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.enabled = true;
            ///        tex.numTilesX = 4;
            ///        tex.numTilesY = 2;
            ///    }
            ///
            ///    void Update()
            ///    {
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.flipU = flipU;
            ///        tex.flipV = flipV;
            ///    }
            ///
            ///    void OnGUI()
            ///    {
            ///        GUI.Label(new Rect(25, 20, 100, 30), "Flip U Amount");
            ///        GUI.Label(new Rect(25, 60, 100, 30), "Flip V Amount");
            ///
            ///        flipU = GUI.HorizontalSlider(new Rect(125, 25, 100, 30), flipU, 0.0f, 1.0f);
            ///        flipV = GUI.HorizontalSlider(new Rect(125, 65, 100, 30), flipV, 0.0f, 1.0f);
            ///    }
            ///}
            ///]]></code>
            ///</example>
            [Obsolete("flipV property is deprecated. Use ParticleSystemRenderer.flip.y instead.", false)]
            public float flipV { get { return m_ParticleSystem.GetComponent<ParticleSystemRenderer>().flip.y; } set { var psr = m_ParticleSystem.GetComponent<ParticleSystemRenderer>(); var flip = psr.flip; flip.y = value; psr.flip = flip; } }
            ///<summary>Use a random row of the Texture sheet for each particle emitted.</summary>
            ///<example>
            ///  <code><![CDATA[
            ///using UnityEngine;
            ///
            ///[RequireComponent(typeof(ParticleSystem))]
            ///public class ExampleClass : MonoBehaviour
            ///{
            ///    private ParticleSystem ps;
            ///    public bool useRandomRow = true;
            ///    public int row = 0;
            ///
            ///    void Start()
            ///    {
            ///        ps = GetComponent<ParticleSystem>();
            ///
            ///        var main = ps.main;
            ///        main.startLifetimeMultiplier = 2.0f;
            ///
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.enabled = true;
            ///        tex.numTilesX = 4;
            ///        tex.numTilesY = 2;
            ///        tex.animation = ParticleSystemAnimationType.SingleRow;
            ///    }
            ///
            ///    void Update()
            ///    {
            ///        var tex = ps.textureSheetAnimation;
            ///        tex.useRandomRow = useRandomRow;
            ///        tex.rowIndex = row;
            ///    }
            ///
            ///    void OnGUI()
            ///    {
            ///        useRandomRow = GUI.Toggle(new Rect(25, 20, 100, 30), useRandomRow, "Use Random Row");
            ///
            ///        if (useRandomRow == false)
            ///        {
            ///            GUI.Label(new Rect(25, 60, 100, 30), "Row Index");
            ///            row = (int)GUI.HorizontalSlider(new Rect(125, 65, 100, 30), (float)row, 0.0f, 1.0f);
            ///        }
            ///    }
            ///}
            ///]]></code>
            ///</example>
            [Obsolete("useRandomRow property is deprecated. Use rowMode instead.", false)]
            public bool useRandomRow { set { rowMode = value ? ParticleSystemAnimationRowMode.Random : ParticleSystemAnimationRowMode.Custom; } get { return (rowMode == ParticleSystemAnimationRowMode.Random); } }
        }

        ///<summary>Script interface for a Particle.</summary>
        ///<seealso cref="ParticleSystem" />
        ///<seealso cref="ParticleSystem.GetParticles" />
        ///<seealso cref="ParticleSystem.SetParticles" />
        public partial struct Particle
        {
            ///<summary>The lifetime of the particle.</summary>
            ///<remarks>This is the time in seconds for how long this particle remains alive.
            ///When the lifetime drops below zero the system destroys the particle.</remarks>
            [Obsolete("Please use Particle.remainingLifetime instead. (UnityUpgradable) -> UnityEngine.ParticleSystem/Particle.remainingLifetime", false)]
            public float lifetime { get { return remainingLifetime; } set { remainingLifetime = value; } }
            ///<summary>The random value of the particle.</summary>
            ///<remarks>This value is used to interpolate between the two curves when random between curves is used.</remarks>
            [Obsolete("randomValue property is deprecated. Use randomSeed instead to control random behavior of particles.", false)]
            public float randomValue { get { return BitConverter.ToSingle(BitConverter.GetBytes(m_RandomSeed), 0); } set { m_RandomSeed = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0); } }
            [Obsolete("size property is deprecated. Use startSize or GetCurrentSize() instead.", false)]
            public float size { get { return startSize; } set { startSize = value; } }
            [Obsolete("color property is deprecated. Use startColor or GetCurrentColor() instead.", false)]
            public Color32 color { get { return startColor; } set { startColor = value; } }
        }

        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("ParticleSystem.CollisionEvent has been deprecated. Use ParticleCollisionEvent instead (UnityUpgradable)", true)]
        public struct CollisionEvent
        {
            public Vector3 intersection { get { return default(Vector3); } }
            public Vector3 normal { get { return default(Vector3); } }
            public Vector3 velocity { get { return default(Vector3); } }
            public Component collider { get { return default(Component); } }
        }

        [Obsolete("safeCollisionEventSize has been deprecated. Use GetSafeCollisionEventSize() instead (UnityUpgradable) -> ParticlePhysicsExtensions.GetSafeCollisionEventSize(UnityEngine.ParticleSystem)", false)]
        public int safeCollisionEventSize { get { return ParticleSystemExtensionsImpl.GetSafeCollisionEventSize(this); } }

        
        [FreeFunction(Name = "ParticleSystemScriptBindings::SetTrailData", HasExplicitThis = true)]
        extern private void SetTrailsInternal(Trails trailData);

        ///<summary>Use this method with the results of an earlier call to <see cref="ParticleSystem.GetTrails" />, in order to restore the Particle System to the state stored in the Trails object.</summary>
        ///<remarks>To fully restore a Particle System to a previous state, use this method along with <see cref="ParticleSystem.SetParticles" /> and <see cref="ParticleSystem.SetPlaybackState" />.</remarks>
        ///<param name="trailData">The Trails to apply to the Particle System.</param>
        ///<seealso cref="GetTrails" />
        ///<seealso cref="SetParticles" />
        ///<seealso cref="SetPlaybackState" />
        [Obsolete("SetTrails is deprecated. Use SetParticlesAndTrails() instead. Avoid SetTrails when ParticleSystem.trails.dieWithParticles is false.", false)]
        public void SetTrails(Trails trailData)
        {
            if (trailData.positions == null || trailData.frontPositions == null || trailData.backPositions == null || trailData.positionCounts == null || trailData.textureOffsets == null)
                throw new NullReferenceException("ParticleSystem.TrailData has not been initialized");
            SetTrailsInternal(trailData);
        }

        ///<exclude />
        [Obsolete("Emit with specific parameters is deprecated. Pass a ParticleSystem.EmitParams parameter instead, which allows you to override some/all of the emission properties", false)]
        public void Emit(Vector3 position, Vector3 velocity, float size, float lifetime, Color32 color)
        {
            ParticleSystem.Particle particle = new ParticleSystem.Particle();
            particle.position = position;
            particle.velocity = velocity;
            particle.lifetime = lifetime;
            particle.startLifetime = lifetime;
            particle.startSize = size;
            particle.rotation3D = Vector3.zero;
            particle.angularVelocity3D = Vector3.zero;
            particle.startColor = color;
            particle.randomSeed = 5;
            EmitOld_Internal(ref particle);
        }

        ///<exclude />
        [Obsolete("Emit with a single particle structure is deprecated. Pass a ParticleSystem.EmitParams parameter instead, which allows you to override some/all of the emission properties", false)]
        public void Emit(ParticleSystem.Particle particle)
        {
            EmitOld_Internal(ref particle);
        }

        ///<summary>Start delay in seconds.</summary>
        ///<remarks>Use this to delay when playback starts on the system.</remarks>
        [Obsolete("startDelay property is deprecated. Use main.startDelay or main.startDelayMultiplier instead.", false)]
        public float startDelay { get { return main.startDelayMultiplier; } set { var m = main; m.startDelayMultiplier = value; } }

        ///<summary>Determines whether the Particle System is looping.</summary>
        ///<remarks>If you disable looping on a playing Particle System, it will stop after the end of the current loop.</remarks>
        [Obsolete("loop property is deprecated. Use main.loop instead.", false)]
        public bool loop { get { return main.loop; } set { var m = main; m.loop = value; } }

        ///<summary>If set to true, the Particle System will automatically start playing on startup.</summary>
        ///<remarks>Note that this setting is shared between all Particle Systems in the current particle effect.</remarks>
        [Obsolete("playOnAwake property is deprecated. Use main.playOnAwake instead.", false)]
        public bool playOnAwake { get { return main.playOnAwake; } set { var m = main; m.playOnAwake = value; } }

        ///<summary>The duration of the Particle System in seconds (Read Only).</summary>
        [Obsolete("duration property is deprecated. Use main.duration instead.", false)]
        public float duration { get { return main.duration; } }

        ///<summary>The playback speed of the Particle System. 1 is normal playback speed.</summary>
        ///<remarks>A negative playback speed is not supported.</remarks>
        [Obsolete("playbackSpeed property is deprecated. Use main.simulationSpeed instead.", false)]
        public float playbackSpeed { get { return main.simulationSpeed; } set { var m = main; m.simulationSpeed = value; } }

        ///<summary>When set to false, the Particle System will not emit particles.</summary>
        [Obsolete("enableEmission property is deprecated. Use emission.enabled instead.", false)]
        public bool enableEmission { get { return emission.enabled; } set { var em = emission; em.enabled = value; } }

        ///<summary>The rate of particle emission.</summary>
        [Obsolete("emissionRate property is deprecated. Use emission.rateOverTime, emission.rateOverDistance, emission.rateOverTimeMultiplier or emission.rateOverDistanceMultiplier instead.", false)]
        public float emissionRate { get { return emission.rateOverTimeMultiplier; } set { var em = emission; em.rateOverTime = value; } }

        ///<summary>The initial speed of particles when emitted. When using curves, this value acts as a scale on the curve.</summary>
        [Obsolete("startSpeed property is deprecated. Use main.startSpeed or main.startSpeedMultiplier instead.", false)]
        public float startSpeed { get { return main.startSpeedMultiplier; } set { var m = main; m.startSpeedMultiplier = value; } }

        ///<summary>The initial size of particles when emitted. When using curves, this value acts as a scale on the curve.</summary>
        [Obsolete("startSize property is deprecated. Use main.startSize or main.startSizeMultiplier instead.", false)]
        public float startSize { get { return main.startSizeMultiplier; } set { var m = main; m.startSizeMultiplier = value; } }

        ///<summary>The initial color of particles when emitted.</summary>
        [Obsolete("startColor property is deprecated. Use main.startColor instead.", false)]
        public Color startColor { get { return main.startColor.color; } set { var m = main; m.startColor = value; } }

        ///<summary>The initial rotation of particles when emitted. When using curves, this value acts as a scale on the curve.</summary>
        ///<remarks>Note that the value should be given in radians.</remarks>
        [Obsolete("startRotation property is deprecated. Use main.startRotation or main.startRotationMultiplier instead.", false)]
        public float startRotation { get { return main.startRotationMultiplier; } set { var m = main; m.startRotationMultiplier = value; } }

        ///<summary>The initial 3D rotation of particles when emitted. When using curves, this value acts as a scale on the curves.</summary>
        ///<remarks>Note that the values are Euler angles and should be given in radians.</remarks>
        [Obsolete("startRotation3D property is deprecated. Use main.startRotationX, main.startRotationY and main.startRotationZ instead. (Or main.startRotationXMultiplier, main.startRotationYMultiplier and main.startRotationZMultiplier).", false)]
        public Vector3 startRotation3D { get { return new Vector3(main.startRotationXMultiplier, main.startRotationYMultiplier, main.startRotationZMultiplier); } set { var m = main; m.startRotationXMultiplier = value.x; m.startRotationYMultiplier = value.y; m.startRotationZMultiplier = value.z; } }

        ///<summary>The total lifetime in seconds that particles will have when emitted. When using curves, this value acts as a scale on the curve. This value is set in the particle when it is created by the Particle System.</summary>
        ///<remarks>**Note:** Documentation for <see cref="startLifetime" /> is <see cref="ParticleSystem.MainModule.startLifetime" />.</remarks>
        [Obsolete("startLifetime property is deprecated. Use main.startLifetime or main.startLifetimeMultiplier instead.", false)]
        public float startLifetime { get { return main.startLifetimeMultiplier; } set { var m = main; m.startLifetimeMultiplier = value; } }

        ///<summary>Scale being applied to the gravity defined by <see cref="P:UnityEngine.Physics.gravity" />.</summary>
        ///<seealso cref="P:UnityEngine.Physics.gravity" />
        ///<seealso cref="ParticleSystem.MainModule.gravityModifier" />
        [Obsolete("gravityModifier property is deprecated. Use main.gravityModifier or main.gravityModifierMultiplier instead.", false)]
        public float gravityModifier { get { return main.gravityModifierMultiplier; } set { var m = main; m.gravityModifierMultiplier = value; } }

        ///<summary>The maximum number of particles to emit.</summary>
        [Obsolete("maxParticles property is deprecated. Use main.maxParticles instead.", false)]
        public int maxParticles { get { return main.maxParticles; } set { var m = main; m.maxParticles = value; } }

        ///<summary>This selects the space in which to simulate particles. It can be either world or local space.</summary>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///
        ///public class Example : MonoBehaviour
        ///{
        ///    ParticleSystem part;
        ///    bool useLocal = true;
        ///
        ///    void Start()
        ///    {
        ///        part = GetComponent<ParticleSystem> ();
        ///        useLocal = (part.simulationSpace == ParticleSystemSimulationSpace.Local);
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        part.simulationSpace = (useLocal ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World);
        ///    }
        ///
        ///    void OnGUI()
        ///    {
        ///        useLocal = GUI.Toggle(new Rect(10, 60, 200, 30), useLocal, " Use Local Simulation Space");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [Obsolete("simulationSpace property is deprecated. Use main.simulationSpace instead.", false)]
        public ParticleSystemSimulationSpace simulationSpace { get { return main.simulationSpace; } set { var m = main; m.simulationSpace = value; } }

        ///<summary>The scaling mode applied to particle sizes and positions.</summary>
        [Obsolete("scalingMode property is deprecated. Use main.scalingMode instead.", false)]
        public ParticleSystemScalingMode scalingMode { get { return main.scalingMode; } set { var m = main; m.scalingMode = value; } }

        ///<summary>Does this system support Automatic Culling?</summary>
        ///<remarks>Internally, each Particle System has 2 modes of operating: procedural and non-procedural.
        ///
        ///In procedural mode, it is possible to know the state of a Particle System for any point in time (past and future) whereas a non-procedural system is unpredictable. This means that it is possible to quickly fast forward (and rewind) a procedural system to any point in time.
        ///
        ///When a system goes out of the view of any camera, it is culled. When this occurs, the procedural system stops updating. It will efficiently fast forward to the new point in time when the system becomes visible again. A non-procedural system cannot do this, so it must continue updating itself even when offscreen, due to its unpredictable nature.
        ///
        ///In order to support Automatic Culling, you can only use a subset of the Particle System modules and properties. For example, using the Limit Velocity over Lifetime module will disable Automatic Culling. Additionally, modifying any properties from script whilst the system is playing will also disable Automatic Culling.
        ///
        ///To discover if you are using any properties that disable this feature, a small speech bubble appears in the upper right corner of the Inspector. The tooltip for this icon gives you details about why Automatic Culling is disabled.</remarks>
        [Obsolete("automaticCullingEnabled property is deprecated. Use proceduralSimulationSupported instead (UnityUpgradable) -> proceduralSimulationSupported", true)]
        public bool automaticCullingEnabled { get { return proceduralSimulationSupported; } }
    }

    ///<summary>Method extension for Physics in Particle System.</summary>
    public static partial class ParticlePhysicsExtensions
    {
        ///<summary>Deprecated: Use the overload that takes a List. That overload doesn't create garbage.</summary>
        [Obsolete("GetCollisionEvents function using ParticleCollisionEvent[] is deprecated. Use List<ParticleCollisionEvent> instead.", false)]
        public static int GetCollisionEvents(this ParticleSystem ps, GameObject go, ParticleCollisionEvent[] collisionEvents)
        {
            if (go == null) throw new ArgumentNullException("go");
            if (collisionEvents == null) throw new ArgumentNullException("collisionEvents");

            return ParticleSystemExtensionsImpl.GetCollisionEventsDeprecated(ps, go, collisionEvents);
        }
    }

    ///<summary>Information about a particle collision.</summary>
    public partial struct ParticleCollisionEvent
    {
        ///<summary>The <see cref="T:UnityEngine.Collider" /> for the GameObject struck by the particles.</summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("collider property is deprecated. Use colliderComponent instead, which supports Collider and Collider2D components (UnityUpgradable) -> colliderComponent", true)]
        public Component collider
        {
            get { throw new InvalidOperationException("collider property is deprecated. Use colliderComponent instead, which supports Collider and Collider2D components"); }
        }
    }
}
