// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;

namespace UnityEngine
{
    // The rendering mode for particle systems
    ///<summary>The rendering mode for particle systems.</summary>
    ///<remarks>This is used by the <see cref="ParticleSystemRenderer" /> to determine how to render the particles.</remarks>
    public enum ParticleSystemRenderMode
    {
        ///<summary>Render particles as billboards facing the active camera. (Default)</summary>
        Billboard = 0,              // Render particles as billboards facing the player.
        ///<summary>Stretch particles in the direction of motion.</summary>
        Stretch = 1,                // Stretch particles in the direction of motion.
        ///<summary>Render particles as billboards always facing up along the y-Axis.</summary>
        HorizontalBillboard = 2,    // Render particles as billboards always facing up along the y-Axis.
        ///<summary>Render particles as billboards always facing the player, but not pitching along the x-Axis.</summary>
        VerticalBillboard = 3,      // Render particles as billboards always facing the player, but not pitching along the x-Axis.
        ///<summary>Render particles as meshes.</summary>
        Mesh = 4,                   // Render particles as meshes.
        ///<summary>Do not render particles.</summary>
        ///<remarks>Can be useful when using the Trails or Lights Modules, for example.</remarks>
        None = 5                    // Don't render particles. (e.g. useful when using the Trail or Lights Module)
    }

    // The mesh distribution options for particle systems
    ///<summary>Sets which method Unity uses to randomly assign Meshes to particles.</summary>
    ///<remarks>The <see cref="ParticleSystemRenderer" /> uses this to determine how often to randomly select each Mesh.</remarks>
    public enum ParticleSystemMeshDistribution
    {
        ///<summary>Use a uniform random value to give each Mesh an equal chance to appear.</summary>
        UniformRandom = 0,          // Each mesh has an equal change of being chosen.
        ///<summary>Use per-Mesh weights to make some Meshes appear more often than others. A higher weight value increases the chance of choosing the associated Mesh.</summary>
        ///<remarks>To specify the weights, use <see cref="ParticleSystemRenderer.SetMeshWeightings" />.</remarks>
        NonUniformRandom = 1        // Each mesh has a weighting that affects how likely it is to be chosen.
    }

    // The sorting mode for particle systems
    ///<summary>The sorting mode for particle systems.</summary>
    public enum ParticleSystemSortMode
    {
        ///<summary>No sorting.</summary>
        None = 0,                   // No sorting.
        ///<summary>Sort based on distance to the camera position. For orthographic cameras, this mode is the same as sorting by depth.</summary>
        Distance = 1,               // Sort based on distance.
        ///<summary>Sort the oldest particles to the front.</summary>
        OldestInFront = 2,          // Sort the oldest particles to the front.
        ///<summary>Sort the youngest particles to the front.</summary>
        YoungestInFront = 3,        // Sort the youngest particles to the front.
        ///<summary>Sort based on depth from the camera plane.</summary>
        Depth = 4,                  // Sort based on depth.
        ///<summary>Sort based on reverse distance to the camera position. For orthographic cameras, this mode is the same as sorting by depth.</summary>
        DistanceReverse = 5,        // Sort based on distance (backwards).
        ///<summary>Sort based on reverse depth from the camera plane.</summary>
        DepthReverse = 6,           // Sort based on depth (backwards).
    }

    // The world collision quality
    ///<summary>Quality of world collisions. Medium and low quality are approximate and may leak particles.</summary>
    public enum ParticleSystemCollisionQuality
    {
        ///<summary>The most accurate world collisions.</summary>
        High = 0,
        ///<summary>Approximate world collisions.</summary>
        Medium = 1,
        ///<summary>Fastest and most approximate world collisions.</summary>
        Low = 2
    }

    // The rendering space for particle systems
    ///<summary>How particles are aligned when rendered.</summary>
    public enum ParticleSystemRenderSpace
    {
        ///<summary>Particles face the camera plane.</summary>
        View = 0,                   // Particles face the camera plane.
        ///<summary>Particles align with the world.</summary>
        World = 1,                  // Particles align with the world.
        ///<summary>Particles align with their local transform.</summary>
        Local = 2,                  // Particles align with their local transform.
        ///<summary>Particles face the eye position.</summary>
        Facing = 3,                 // Particles face the eye position.
        ///<summary>Particles are aligned to their direction of travel.</summary>
        Velocity = 4                // Particles are aligned based on their velocity.
    }

    // The particle curve mode
    ///<summary>The particle curve mode.</summary>
    ///<remarks>This is used by <see cref="ParticleSystem.MinMaxCurve" /> to determine which mode to evaluate curves in.</remarks>
    public enum ParticleSystemCurveMode
    {
        ///<summary>Use a single constant for the <see cref="ParticleSystem.MinMaxCurve" />.</summary>
        ///<remarks>The value returned will always be the same.</remarks>
        Constant = 0,               // Emit using a single value.
        ///<summary>Use a single curve for the <see cref="ParticleSystem.MinMaxCurve" />.</summary>
        ///<remarks>The curve is evaluated at the input value (for example, the age of the Particle System or the velocity of the particle), and so will always give the same result for the same input value.</remarks>
        Curve = 1,                  // Emit based on a curve.
        ///<summary>Use a random value between 2 curves for the <see cref="ParticleSystem.MinMaxCurve" />.</summary>
        ///<remarks>Two curves will each be evaluated at the input value (for example, Particle System age or particle speed, depending on the module), and a random value between the two results will be computed and returned. Thus, some control over the overall value is retained, but the result is still randomized.</remarks>
        TwoCurves = 2,              // Emit based on a random value between 2 curves.
        ///<summary>Use a random value between 2 constants for the <see cref="ParticleSystem.MinMaxCurve" />.</summary>
        ///<remarks>The value returned will be chosen at each evaluation as a random value between the two given constants, and will not be dependent on the variable factor (for instance, Particle System age, or particle speed).</remarks>
        TwoConstants = 3            // Emit based on a random value between 2 constants.
    }

    // The particle gradient mode
    ///<summary>The particle gradient mode.</summary>
    ///<remarks>This is used by <see cref="ParticleSystem.MinMaxGradient" /> to determine which mode to evaluate color gradients in.</remarks>
    public enum ParticleSystemGradientMode
    {
        ///<summary>Use a single color for the <see cref="ParticleSystem.MinMaxGradient" />.</summary>
        Color = 0,                  // Emit using a single color.
        ///<summary>Use a single color gradient for the <see cref="ParticleSystem.MinMaxGradient" />.</summary>
        Gradient = 1,               // Emit based on a color gradient.
        ///<summary>Use a random value between 2 colors for the <see cref="ParticleSystem.MinMaxGradient" />.</summary>
        TwoColors = 2,              // Emit based on a random value between 2 colors.
        ///<summary>Use a random value between 2 color gradients for the <see cref="ParticleSystem.MinMaxGradient" />.</summary>
        TwoGradients = 3,           // Emit based on a random value between 2 color gradients.
        ///<summary>Define a list of colors in the <see cref="ParticleSystem.MinMaxGradient" />, to be chosen from at random.</summary>
        ///<remarks>The gradient is evaluated at a random point. The color returned is uninterpolated between color keys. Each color key in the gradient effectively changes the gradient to a fixed color for values between that key and the following key.</remarks>
        RandomColor = 4             // Emit by picking a random color from a list.
    }

    // The emission shape
    ///<summary>The emission shape.</summary>
    ///<remarks>This is used by the <see cref="ParticleSystem.ShapeModule" /> to determine how to sort the particles.</remarks>
    public enum ParticleSystemShapeType
    {
        ///<summary>Emit from a sphere.</summary>
        Sphere = 0,                 // Emit from the volume of a sphere.
        ///<summary>Emit from the surface of a sphere.</summary>
        [Obsolete("SphereShell is deprecated and does nothing. Please use ShapeModule.radiusThickness instead, to control edge emission.", false)]
        SphereShell = 1,            // Emit from the surface of a sphere.
        ///<summary>Emit from a half-sphere.</summary>
        Hemisphere = 2,             // Emit from the volume of a half-sphere.
        ///<summary>Emit from the surface of a half-sphere.</summary>
        [Obsolete("HemisphereShell is deprecated and does nothing. Please use ShapeModule.radiusThickness instead, to control edge emission.", false)]
        HemisphereShell = 3,        // Emit from the surface of a half-sphere.
        ///<summary>Emit from the base of a cone.</summary>
        Cone = 4,                   // Emit from the base surface of a cone.
        ///<summary>Emit from the volume of a box.</summary>
        Box = 5,                    // Emit from the volume of a box.
        ///<summary>Emit from a mesh.</summary>
        Mesh = 6,                   // Emit from a mesh.
        ///<summary>Emit from the base surface of a cone.</summary>
        [Obsolete("ConeShell is deprecated and does nothing. Please use ShapeModule.radiusThickness instead, to control edge emission.", false)]
        ConeShell = 7,              // Emit from the base surface of a cone.
        ///<summary>Emit from a cone.</summary>
        ConeVolume = 8,             // Emit from the volume of a cone.
        ///<summary>Emit from the surface of a cone.</summary>
        [Obsolete("ConeVolumeShell is deprecated and does nothing. Please use ShapeModule.radiusThickness instead, to control edge emission.", false)]
        ConeVolumeShell = 9,        // Emit from the surface of a cone.
        ///<summary>Emit from a circle.</summary>
        Circle = 10,                // Emit from a circle.
        ///<summary>Emit from the edge of a circle.</summary>
        [Obsolete("CircleEdge is deprecated and does nothing. Please use ShapeModule.radiusThickness instead, to control edge emission.", false)]
        CircleEdge = 11,            // Emit from the edge of a circle.
        ///<summary>Emit from an edge.</summary>
        SingleSidedEdge = 12,       // Emit from an edge.
        ///<summary>Emit from a mesh renderer.</summary>
        MeshRenderer = 13,          // Emit from a mesh renderer.
        ///<summary>Emit from a skinned mesh renderer.</summary>
        SkinnedMeshRenderer = 14,   // Emit from a skinned mesh renderer.
        ///<summary>Emit from the surface of a box.</summary>
        BoxShell = 15,              // Emit from the surface of a box.
        ///<summary>Emit from the edges of a box.</summary>
        BoxEdge = 16,               // Emit from the edges of a box.
        ///<summary>Emit from a Donut.</summary>
        Donut = 17,                 // Emit in a donut volume.
        ///<summary>Emit from a rectangle.</summary>
        Rectangle = 18,             // Emit from a rectangle.
        ///<summary>Emit from a sprite.</summary>
        Sprite = 19,                // Emit from a Sprite.
        ///<summary>Emit from a sprite renderer.</summary>
        SpriteRenderer = 20         // Emit from a SpriteRenderer.
    }

    // The mesh emission type
    ///<summary>The mesh emission type.</summary>
    public enum ParticleSystemMeshShapeType
    {
        ///<summary>Emit from the vertices of the mesh.</summary>
        Vertex = 0,                 // Emit from the vertices of the mesh.
        ///<summary>Emit from the edges of the mesh.</summary>
        Edge = 1,                   // Emit from the edges of the mesh.
        ///<summary>Emit from the surface of the mesh.</summary>
        Triangle = 2                // Emit from the surface of the mesh.
    }

    // The texture channel used for discarding particles
    ///<summary>The texture channel.</summary>
    public enum ParticleSystemShapeTextureChannel
    {
        ///<summary>The red channel.</summary>
        Red = 0,
        ///<summary>The green channel.</summary>
        Green = 1,
        ///<summary>The blue channel.</summary>
        Blue = 2,
        ///<summary>The alpha channel.</summary>
        Alpha = 3
    }

    // The animation mode
    ///<summary>The animation mode.</summary>
    public enum ParticleSystemAnimationMode
    {
        ///<summary>Use a regular grid to construct a sequence of animation frames.</summary>
        Grid = 0,                   // A regular grid of frames.
        ///<summary>Use a list of sprites to construct a sequence of animation frames.</summary>
        ///<remarks>Defines the sprites that are added to Texture Sheet Animation.</remarks>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///
        /// // A particle sprite example.
        /// // The gameobject this script is attached to must have the
        /// // ParticleSystem attached.  The TextureSheetAnimation mode
        /// // is set to Sprites.  This script adds a single texture to
        /// // the ParticleSystem.
        ///
        ///[RequireComponent(typeof(ParticleSystem))]
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    public Texture2D tex;
        ///    private ParticleSystem ps;
        ///    private Sprite sprite;
        ///
        ///    void Start()
        ///    {
        ///        ps = GetComponent<ParticleSystem>();
        ///        sprite = Sprite.Create(tex, new Rect(0.0f, 0.0f, tex.width, tex.height), Vector2.zero);
        ///
        ///        var textureSheetAnimation = ps.textureSheetAnimation;
        ///        textureSheetAnimation.enabled = true;
        ///        textureSheetAnimation.mode = ParticleSystemAnimationMode.Sprites;
        ///        textureSheetAnimation.AddSprite(sprite);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        ///<seealso cref="ParticleSystemAnimationMode.Grid" />
        Sprites = 1                 // Sprite frames.
    }

    // The animation time mode
    ///<summary>Control how animation frames are selected.</summary>
    ///<seealso cref="ParticleSystem.TextureSheetAnimationModule.timeMode" />
    ///<seealso cref="ParticleSystem.TextureSheetAnimationModule.frameOverTime" />
    ///<seealso cref="ParticleSystem.TextureSheetAnimationModule.fps" />
    ///<seealso cref="ParticleSystem.TextureSheetAnimationModule.speedRange" />
    public enum ParticleSystemAnimationTimeMode
    {
        ///<summary>Select animation frames based on the particle ages.</summary>
        Lifetime = 0,               // Based on the lifetimes of the particles.
        ///<summary>Select animation frames based on the particle speeds.</summary>
        Speed = 1,                  // Based on the speed of the particles.
        ///<summary>Select animation frames sequentially at a constant rate of the specified frames per second.</summary>
        FPS = 2                     // A constant FPS.
    }

    // The animation type
    ///<summary>The animation type.</summary>
    ///<remarks>Controls how texture sheet animations play.</remarks>
    ///<seealso cref="ParticleSystem.TextureSheetAnimationModule.animation" />
    public enum ParticleSystemAnimationType
    {
        ///<summary>Animate over the whole texture sheet from left to right, top to bottom.</summary>
        WholeSheet = 0,
        ///<summary>Animate a single row in the sheet from left to right.</summary>
        SingleRow = 1
    }

    // The animation row mode
    ///<summary>The mode used for selecting rows of an animation in the Texture Sheet Animation Module.</summary>
    public enum ParticleSystemAnimationRowMode
    {
        ///<summary>Use a specific row for all particles.</summary>
        Custom = 0,                 // The same row is used for all particles.
        ///<summary>Use a random row for each particle.</summary>
        Random = 1,                 // Row is selected randomly per-particle.
        ///<summary>Use the mesh index as the row, so that meshes can be mapped to specific animation frames.</summary>
        MeshIndex = 2               // Row is derived from mesh index.
    }

    // The collision type
    ///<summary>The type of collisions to use for a given Particle System.</summary>
    public enum ParticleSystemCollisionType
    {
        ///<summary>Collide with a list of planes.</summary>
        Planes = 0,
        ///<summary>Collide with the world geometry.</summary>
        World = 1
    }

    // The collision mode
    ///<summary>Whether to use 2D or 3D colliders for particle collisions.</summary>
    public enum ParticleSystemCollisionMode
    {
        ///<summary>Use 3D colliders to collide particles against.</summary>
        Collision3D = 0,
        ///<summary>Use 2D colliders to collide particles against.</summary>
        Collision2D = 1
    }

    // The overlap action
    ///<summary>What action to perform when the particle trigger module passes a test.</summary>
    public enum ParticleSystemOverlapAction
    {
        ///<summary>Do nothing.</summary>
        Ignore = 0,
        ///<summary>Kill all particles that pass this test.</summary>
        Kill = 1,
        ///<summary>Send the OnParticleTrigger command to the Particle System's script.</summary>
        Callback = 2
    }

    // How many colliders are stored for each particle in trigger events
    ///<summary>Whether collider information is available when using the <see cref="M:UnityEngine.ParticlePhysicsExtensions.GetTriggerParticles" /> method.</summary>
    public enum ParticleSystemColliderQueryMode
    {
        ///<summary>
        ///  <see cref="M:UnityEngine.ParticlePhysicsExtensions.GetTriggerParticles" /> does not return any information about which colliders each particle is interacting with.</summary>
        Disabled,
        ///<summary>
        ///  <see cref="M:UnityEngine.ParticlePhysicsExtensions.GetTriggerParticles" /> may only return one collider that each particle is interacting with.</summary>
        One,
        ///<summary>
        ///  <see cref="M:UnityEngine.ParticlePhysicsExtensions.GetTriggerParticles" /> returns all colliders that each particle is interacting with.</summary>
        All
    }

    // The simulation space for particle systems
    ///<summary>Defines the coordinate space in which particles are simulated.</summary>
    ///<remarks>
    ///  <para>The simulation space determines how particles' positions are calculated relative to their environment.
    ///This property controls whether particles are simulated in local space, world space, or relative to a custom transform. The space is defined by the <c>ParticleSystemSimulationSpace</c> enum.
    ///
    ///For custom space simulation, use <see cref="ParticleSystem.MainModule.customSimulationSpace" /> to assign a specific Transform as the reference.</para>
    ///  <para />
    ///</remarks>
    ///<example>
    ///  <code><![CDATA[using UnityEngine;
    ///
    ///public class ParticleSimulationExample : MonoBehaviour
    ///{
    ///    [SerializeField] private ParticleSystem particleSystem;
    ///    [SerializeField] private Transform customTransform;
    ///
    ///    void Start()
    ///    {
    ///        var mainModule = particleSystem.main;
    ///
    ///        // Set the simulation space to Custom and assign a transform.
    ///        mainModule.simulationSpace = ParticleSystemSimulationSpace.Custom;
    ///        mainModule.customSimulationSpace = customTransform;
    ///    }
    ///}]]></code>
    ///</example>
    ///<seealso cref="ParticleSystem.MainModule" />
    public enum ParticleSystemSimulationSpace
    {
        ///<summary>Simulate particles in local space.</summary>
        Local = 0,                  // Use local simulation space.
        ///<summary>Simulate particles in world space.</summary>
        World = 1,                  // Use world simulation space.
        ///<summary>Simulate particles relative to a custom transform component, defined by <see cref="ParticleSystem.MainModule.customSimulationSpace" />.</summary>
        Custom = 2                  // Use custom simulation space, relative to a custom transform component.
    }

    // What action to take when a Particle Systme finishes emitting
    ///<summary>The behavior to apply when calling <see cref="ParticleSystem.Stop">Stop</see>.</summary>
    public enum ParticleSystemStopBehavior
    {
        ///<summary>Stops Particle System emitting and removes all existing emitted particles.</summary>
        StopEmittingAndClear = 0,   // Stop emitting and remove existing particles.
        ///<summary>Stops Particle System emitting any further particles. All existing particles will remain until they expire.</summary>
        StopEmitting = 1            // Stop emitting and allow existing particles to finish.
    }

    // The scaling mode for particle systems
    ///<summary>Control how particle systems apply transform scale.</summary>
    public enum ParticleSystemScalingMode
    {
        ///<summary>Scale the Particle System using the entire transform hierarchy.</summary>
        Hierarchy = 0,              // Use full hierarchy scale.
        ///<summary>Scale the Particle System using only its own transform scale. (Ignores parent scale).</summary>
        Local = 1,                  // Use only the local scaling.
        ///<summary>Only apply transform scale to the shape component, which controls where
        ///        particles are spawned, but does not affect their size or movement.</summary>
        Shape = 2                   // Only apply scaling to the Shape module.
    }

    // The action to perform when a particle system stops
    ///<summary>The action to perform when the Particle System stops.</summary>
    public enum ParticleSystemStopAction
    {
        ///<summary>Do nothing.</summary>
        None = 0,
        ///<summary>Disable the GameObject containing the Particle System.</summary>
        Disable = 1,
        ///<summary>Destroy the GameObject containing the Particle System.</summary>
        Destroy = 2,
        ///<summary>Call <see cref="M:UnityEngine.MonoBehaviour.OnParticleSystemStopped" /> on all scripts attached to the same GameObject.</summary>
        Callback = 3                // Calls OnParticleSystemStopped.
    }

    // The action to perform when a particle system is offscreen
    ///<summary>The action to perform when the Particle System is offscreen.</summary>
    public enum ParticleSystemCullingMode
    {
        ///<summary>For looping effects, the simulation is paused when offscreen, and for one-shot effects, the simulation will continue playing.</summary>
        Automatic = 0,
        ///<summary>Pause the Particle System simulation when it is offscreen, and perform an extra simulation when the system comes back onscreen, creating the impression that it was never paused.
        ///                
        ///Use <see cref="ParticleSystemCullingMode.AlwaysSimulate" /> instead if the particle system moves, especially if the **Simulation Space** is set to **World**. Otherwise the bounds Unity uses for culling might not be accurate.</summary>
        PauseAndCatchup = 1,
        ///<summary>Pause the Particle System simulation when it is offscreen.</summary>
        Pause = 2,
        ///<summary>Continue simulating the Particle System when it is offscreen.</summary>
        AlwaysSimulate = 3
    }

    // The emitter velocity mode for particle systems
    ///<summary>Control how a Particle System calculates its velocity.</summary>
    public enum ParticleSystemEmitterVelocityMode
    {
        ///<summary>Calculate the Particle System velocity by using the Transform component.</summary>
        Transform = 0,              // Use the Transform component for calculating velocity
        ///<summary>Calculate the Particle System velocity by using a Rigidbody or Rigidbody2D component, if one exists on the GameObject.</summary>
        Rigidbody = 1,              // Use the Rigidbody or Rigidbody2D component for calculating velocity.
        ///<summary>When the Particle System calculates its velocity, it instead uses the custom value set in <see cref="ParticleSystem.MainModule.emitterVelocity" />.</summary>
        Custom = 2                  // The value is driven by user script, a property in the inspector or animation.
    }

    // Which physics gravity to apply
    ///<summary>Options for which physics system to use the gravity setting from.</summary>
    public enum ParticleSystemGravitySource
    {
        ///<summary>Use gravity from the 3D physics system.</summary>
        Physics3D = 0,
        ///<summary>Use gravity from the 2D physics system.</summary>
        Physics2D = 1
    };

    // The mode used for velocity inheritence
    ///<summary>How to apply emitter velocity to particles.</summary>
    public enum ParticleSystemInheritVelocityMode
    {
        ///<summary>Each particle inherits the emitter's velocity on the frame when it was initially emitted.</summary>
        ///<remarks>Each particle then maintains this constant velocity throughout its lifetime. The magnitude of the velocity can be modified by the curve multiplier.</remarks>
        Initial = 0,                // Emitter velocity is inherited over the particle's lifetime using the emitter velocity when the particle was born.
        ///<summary>Each particle's velocity is set to the emitter's current velocity value, every frame.</summary>
        ///<remarks>If the curve multiplier's default value (a constant value of 1) is not modified, this gives the effect of the particles sticking with the emitter. The magnitude of the inherited velocity can be modified by the curve multiplier.</remarks>
        Current = 1                 // Emitter velocity is inherited over the particle's lifetime using the current emitter velocity.
    }

    // The types of trigger events
    ///<summary>The different types of particle triggers.</summary>
    public enum ParticleSystemTriggerEventType
    {
        ///<summary>Trigger when particles are inside the collision volume.</summary>
        ///<remarks>This action will be performed every frame while particles are inside the trigger volume.</remarks>
        Inside = 0,                 // Triggered when particles are inside the collision volume.
        ///<summary>Trigger when particles are outside the collision volume.</summary>
        ///<remarks>This action will be performed every frame while particles are outside the trigger volume.</remarks>
        Outside = 1,                // Triggered when particles are outside the collision volume.
        ///<summary>Trigger when particles enter the collision volume.</summary>
        Enter = 2,                  // Triggered when particles enter the collision volume.
        ///<summary>Trigger when particles leave the collision volume.</summary>
        Exit = 3                    // Triggered when particles leave the collision volume.
    }

    // The custom streams
    ///<summary>All possible Particle System vertex shader inputs.</summary>
    [UsedByNativeCode]
    public enum ParticleSystemVertexStream
    {
        ///<summary>The position of each particle vertex, in world space.</summary>
        Position,
        ///<summary>The vertex normal of each particle.</summary>
        Normal,
        ///<summary>The tangent vector for each particle (for normal mapping).</summary>
        Tangent,
        ///<summary>The color of each particle.</summary>
        Color,
        ///<summary>The first UV stream of each particle.</summary>
        UV,
        ///<summary>The second UV stream of each particle.</summary>
        UV2,
        ///<summary>The third UV stream of each particle (only for meshes).</summary>
        UV3,
        ///<summary>The fourth UV stream of each particle (only for meshes).</summary>
        UV4,
        ///<summary>The amount to blend between animated texture frames, from 0 to 1.</summary>
        AnimBlend,
        ///<summary>The current animation frame index of each particle.</summary>
        AnimFrame,
        ///<summary>The center position of the entire particle, in world space.</summary>
        Center,
        ///<summary>The vertex ID of each particle.</summary>
        VertexID,
        ///<summary>The X axis size of each particle.</summary>
        SizeX,
        ///<summary>The X and Y axis sizes of each particle.</summary>
        SizeXY,
        ///<summary>The 3D size of each particle.</summary>
        SizeXYZ,
        ///<summary>The Z axis rotation of each particle.</summary>
        Rotation,
        ///<summary>The 3D rotation of each particle.</summary>
        Rotation3D,
        ///<summary>The Z axis rotational speed of each particle.</summary>
        RotationSpeed,
        ///<summary>The 3D rotational speed of each particle.</summary>
        RotationSpeed3D,
        ///<summary>The velocity of each particle, in world space.</summary>
        Velocity,
        ///<summary>The speed of each particle, calculated by taking the magnitude of the velocity.</summary>
        Speed,
        ///<summary>The normalized age of each particle, from 0 to 1.</summary>
        AgePercent,
        ///<summary>The reciprocal of the starting lifetime, in seconds (1.0f / startLifetime).</summary>
        InvStartLifetime,
        ///<summary>A random number for each particle, which remains constant during their lifetime.</summary>
        StableRandomX,
        ///<summary>Two random numbers for each particle, which remain constant during their lifetime.</summary>
        StableRandomXY,
        ///<summary>Three random numbers for each particle, which remain constant during their lifetime.</summary>
        StableRandomXYZ,
        ///<summary>Four random numbers for each particle, which remain constant during their lifetime.</summary>
        StableRandomXYZW,
        ///<summary>A random number for each particle, which changes during their lifetime.</summary>
        VaryingRandomX,
        ///<summary>Two random numbers for each particle, which change during their lifetime.</summary>
        VaryingRandomXY,
        ///<summary>Three random numbers for each particle, which change during their lifetime.</summary>
        VaryingRandomXYZ,
        ///<summary>Four random numbers for each particle, which change during their lifetime.</summary>
        VaryingRandomXYZW,
        ///<summary>One custom value for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom1X,
        ///<summary>Two custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom1XY,
        ///<summary>Three custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom1XYZ,
        ///<summary>Four custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom1XYZW,
        ///<summary>One custom value for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom2X,
        ///<summary>Two custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom2XY,
        ///<summary>Three custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom2XYZ,
        ///<summary>Four custom values for each particle, defined by the Custom Data Module, or <see cref="ParticleSystem.SetCustomParticleData" />.</summary>
        Custom2XYZW,
        ///<summary>The accumulated X axis noise, over the lifetime of the particle.</summary>
        NoiseSumX,
        ///<summary>The accumulated X and Y axis noise, over the lifetime of the particle.</summary>
        NoiseSumXY,
        ///<summary>The accumulated 3D noise, over the lifetime of the particle.</summary>
        NoiseSumXYZ,
        ///<summary>The X axis noise on the current frame.</summary>
        NoiseImpulseX,
        ///<summary>The X and Y axis noise on the current frame.</summary>
        NoiseImpulseXY,
        ///<summary>The 3D noise on the current frame.</summary>
        NoiseImpulseXYZ,
        ///<summary>The index of the mesh used by the current particle.</summary>
        MeshIndex,
        ///<summary>The index of the current particle in the particle data array.</summary>
        ParticleIndex,
        ///<summary>The color of each particle, packed in a special format to allow decoding on GPUs that do not support bit-packing operations.</summary>
        ///<remarks>To unpack the color, use the following code: `float4 color = float4(floor(colorPacked.x) / 255, frac(colorPacked.x) / 0.999, floor(colorPacked.y) / 255, frac(colorPacked.y) / 0.999);`.</remarks>
        ColorPackedAsTwoFloats,
        ///<summary>The axis of rotation used by mesh particles when not using 3D rotation.</summary>
        MeshAxisOfRotation,
        ///<summary>The center of the next trail position, connected to the current position.</summary>
        NextTrailCenter,
        ///<summary>The center of the previous trail position, connected to the current position.</summary>
        PreviousTrailCenter,
        ///<summary>The percentage along the trail, in the range 0-1.</summary>
        PercentageAlongTrail,
        ///<summary>The width of the trail.</summary>
        TrailWidth,
    }

    // The available vertex streams
    ///<summary>Which stream of custom particle data to set.</summary>
    ///<seealso cref="ParticleSystem.SetCustomParticleData" />
    ///<seealso cref="ParticleSystem.GetCustomParticleData" />
    public enum ParticleSystemCustomData
    {
        ///<summary>The first stream of custom per-particle data.</summary>
        Custom1,
        ///<summary>The second stream of custom per-particle data.</summary>
        Custom2
    }

    // The custom stream modes
    ///<summary>Which mode CustomDataModule uses to generate its data.</summary>
    public enum ParticleSystemCustomDataMode
    {
        ///<summary>Don't generate any data.</summary>
        Disabled,
        ///<summary>Generate data using <see cref="ParticleSystem.MinMaxCurve" />.</summary>
        Vector,
        ///<summary>Generate data using <see cref="ParticleSystem.MinMaxGradient" />.</summary>
        Color
    }

    // The number of dimensions used for noise
    ///<summary>The quality of the generated noise.</summary>
    public enum ParticleSystemNoiseQuality
    {
        ///<summary>Low quality 1D noise.</summary>
        Low = 0,
        ///<summary>Medium quality 2D noise.</summary>
        Medium = 1,
        ///<summary>High quality 3D noise.</summary>
        High = 2
    }

    // The various types of subemitter
    ///<summary>The events that cause new particles to be spawned.</summary>
    public enum ParticleSystemSubEmitterType
    {
        ///<summary>Spawns new particles when particles from the parent system are born.</summary>
        Birth = 0,
        ///<summary>Spawns new particles when particles from the parent system collide with something.</summary>
        Collision = 1,
        ///<summary>Spawns new particles when particles from the parent system die.</summary>
        Death = 2,
        ///<summary>Spawns new particles when particles from the parent system pass conditions in the Trigger Module.</summary>
        Trigger = 3,
        ///<summary>Spawns new particles when triggered from script using <see cref="ParticleSystem.TriggerSubEmitter" />.</summary>
        Manual = 4
    }

    // The subemitter properties
    ///<summary>The properties of sub-emitter particles.</summary>
    [Flags]
    public enum ParticleSystemSubEmitterProperties
    {
        ///<summary>When spawning new particles, do not inherit any properties from the parent particles.</summary>
        InheritNothing = 0,
        ///<summary>When spawning new particles, inherit all available properties from the parent particles.</summary>
        InheritEverything = InheritColor | InheritSize | InheritRotation | InheritLifetime | InheritDuration,
        ///<summary>When spawning new particles, multiply the start color by the color of the parent particles.</summary>
        InheritColor = 1 << 0,
        ///<summary>When spawning new particles, multiply the start size by the size of the parent particles.</summary>
        InheritSize = 1 << 1,
        ///<summary>When spawning new particles, add the start rotation to the rotation of the parent particles.</summary>
        InheritRotation = 1 << 2,
        ///<summary>New particles will have a shorter lifespan, the closer their parent particles are to death.</summary>
        InheritLifetime = 1 << 3,
        ///<summary>When spawning new particles, use the duration and age properties from the parent system, when sampling MainModule curves in the Sub-Emitter.</summary>
        InheritDuration = 1 << 4,
    }

    // The mode used for generating Particle Trails (Shuriken).
    ///<summary>Choose how Particle Trails are generated.</summary>
    public enum ParticleSystemTrailMode
    {
        ///<summary>Makes a trail behind each particle as the particle moves.</summary>
        PerParticle = 0,            // Trails are generated from each particle.
        ///<summary>Draws a line between each particle, connecting the youngest particle to the oldest.</summary>
        Ribbon = 1                  // Trails are rendered between each particle.
    }

    // The mode applied to the U coordiante on Particle Trails
    ///<summary>Choose how textures are applied to Particle Trails.</summary>
    public enum ParticleSystemTrailTextureMode
    {
        ///<summary>Map the texture once along the entire length of the trail.</summary>
        Stretch = 0,                // Stretch the texture over the entire trail length.
        ///<summary>Repeat the texture along the trail. To set the tiling rate, use <see cref="Material.SetTextureScale" />.</summary>
        Tile = 1,                   // Repeat the texture along the trail.
        ///<summary>Map the texture once along the entire length of the trail, assuming all vertices are evenly spaced.</summary>
        DistributePerSegment = 2,   // Stretch the texture over the entire trail, but treat each segment as though it is of equal length.
        ///<summary>Repeat the texture along the trail, repeating at a rate of once per trail segment. To adjust the tiling rate, use <see cref="Material.SetTextureScale" />.</summary>
        RepeatPerSegment = 3,       // Repeat the texture along the trail, at a rate of one repetition per segment.
        ///<summary>Trails do not change the texture coordinates of existing points when they add or remove points.</summary>
        Static = 4                  // UVs don't move when points are added/removed from the trails.
    }

    // The mode used to generate new points in a shape
    ///<summary>The mode used to generate new points in a shape.</summary>
    public enum ParticleSystemShapeMultiModeValue
    {
        ///<summary>Generate points randomly. (Default)</summary>
        Random = 0,                 // Generate points randomly.
        ///<summary>Animate the emission point around the shape.</summary>
        Loop = 1,                   // Animate the emission point around the shape.
        ///<summary>Animate the emission point around the shape, alternating between clockwise and counter-clockwise directions.</summary>
        PingPong = 2,               // Animate the emission point around the shape, alternating between clockwise and counter-clockwise directions.
        ///<summary>Distribute new particles around the shape evenly.</summary>
        BurstSpread = 3             // Distribute new particles around the shape evenly.
    }

    // Ring Buffer modes
    ///<summary>Control how particles are removed from the Particle System.</summary>
    public enum ParticleSystemRingBufferMode
    {
        ///<summary>Particles are removed when their age exceeds their lifetime.</summary>
        Disabled = 0,
        ///<summary>Particle ages pause at the end of their lifetime until they need to be removed. Particles are removed when creating new particles would exceed the Max Particles property.</summary>
        ///<remarks>If they reach the end of the lifetime before being replaced, they remain paused at their final lifetime value. This means that any lifetime based properties, such as curves, use the final value on the curve.</remarks>
        PauseUntilReplaced = 1,     // When particles reach the end of their life, pause until replaced.
        ///<summary>Particle ages loop until they need to be removed. Particles are removed when creating new particles would exceed the Max Particles property.</summary>
        ///<remarks>When using this mode, particle ages will loop in the range specified by <see cref="ParticleSystem.MainModule.ringBufferLoopRange" />. When they need to be removed, they stop looping, but continue to play until their age reaches their lifetime value. This looping behaviour means that any lifetime based properties, such as curves, repeat the portion of the curve specified by the loop range.</remarks>
        LoopUntilReplaced = 2       // When particles reach the fade out time, loop back to the fade in time. When replaced, play to the end of their life before actually being replaced.
    }

    // Select whether to use a layer mask or an explicit list when deciding which GameObjects to use with certain Particle System effects.
    ///<summary>The particle GameObject filtering mode that specifies which objects are used by specific Particle System modules.</summary>
    public enum ParticleSystemGameObjectFilter
    {
        ///<summary>Include objects based on a layer mask, where all objects that match the mask are included.</summary>
        LayerMask = 0,
        ///<summary>Include objects based on an explicitly provided list.</summary>
        List = 1,
        ///<summary>Include objects based on both a layer mask and an explicitly provided list.</summary>
        LayerMaskAndList = 2
    }

    // Supported force field types
    ///<summary>The type of shape used for influencing particles in the Force Field Component.</summary>
    public enum ParticleSystemForceFieldShape
    {
        ///<summary>Influence particles inside a sphere shape.</summary>
        Sphere = 0,
        ///<summary>Influence particles inside a hemisphere shape.</summary>
        Hemisphere = 1,
        ///<summary>Influence particles inside a cylinder shape.</summary>
        Cylinder = 2,
        ///<summary>Influence particles inside a box shape.</summary>
        Box = 3
    }
	
    // Mesh baking options
    ///<summary>Configure how a Particle System is baked into a mesh.</summary>
    [Flags]
    public enum ParticleSystemBakeMeshOptions
    {
        ///<summary>Bake the Transform rotation and scale into the mesh.</summary>
        BakeRotationAndScale = 1 << 0,
        ///<summary>Bake the Transform position into the mesh.</summary>
        BakePosition = 1 << 1,

        ///<summary>The default baking options.</summary>
        Default = 0
    }

    // Texture baking options
    ///<summary>Configure how a Particle System is baked into a texture.</summary>
    [Flags]
    public enum ParticleSystemBakeTextureOptions
    {
        ///<summary>Bake the Transform rotation and scale into the texture.</summary>
        BakeRotationAndScale = 1 << 0,
        ///<summary>Bake the Transform position into the texture.</summary>
        BakePosition = 1 << 1,
        ///<summary>Bake each vertex of each particle (i.e. 4 vertices per billboard).</summary>
        PerVertex = 1 << 2,                 // Bake each vertex of each particle (i.e. 4 vertices per billboard)
        ///<summary>Only bake each particle, instead of each vertex of each particle (i.e. 1 vertex per billboard).</summary>
        ///<remarks>This can be a useful optimization if you are not using any per-vertex data such as UV's or Positions.</remarks>
        PerParticle = 1 << 3,               // Only bake each particle (i.e. 1 vertex per billboard) Useful if not using any per-vertex data such as UVs or Positions.
        ///<summary>Instead of only baking triangle indices into the indices texture, bake a 2 channel index texture containing triangle indices and particle indices.</summary>
        IncludeParticleIndices = 1 << 4,    // Instead of baking triangle indices for rendering, bake a 2 channel index texture containing triangle indices and particle indices

        ///<summary>The default baking options.</summary>
        Default = PerVertex
    }
}

namespace UnityEngine.Rendering
{
    // Control which UV channels are affected by the Texture Animation Module
    ///<summary>A flag representing each UV channel.</summary>
    [Flags]
    public enum UVChannelFlags
    {
        ///<summary>First UV channel.</summary>
        UV0 = 1,
        ///<summary>Second UV channel.</summary>
        UV1 = 2,
        ///<summary>Third UV channel.</summary>
        UV2 = 4,
        ///<summary>Fourth UV channel.</summary>
        UV3 = 8
    }
}
