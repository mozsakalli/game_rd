// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
namespace UnityEngine
{
    ///<summary>Information about what animation clips is played and its weight.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [Obsolete("Use AnimatorClipInfo instead (UnityUpgradable) -> AnimatorClipInfo", true)]
    public struct AnimationInfo
    {
        ///<summary>Animation clip that is played.</summary>
        public AnimationClip clip { get { return default(AnimationClip); }  }
        ///<summary>The weight of the animation clip.</summary>
        ///<remarks>The weight is the concatenation of the blendtree weight and the transition weight.</remarks>
        public float weight { get { return 0.0f; } }
    }

    ///<summary>Manages, controls, and evaluates the animation of a GameObject.</summary>
    ///<remarks>The Animator is the main <see cref="Component" /> in the Mecanim animation system. The Animator evaluates <see cref="AnimationClip">Animation Clips</see> and manages <see cref="T:UnityEditor.Animations.AnimatorState">Animator States</see> in <see cref="T:UnityEditor.Animations.AnimatorStateMachine">Animator State Machines</see>.
    ///
    ///**Control the Animator with an AnimatorController**
    ///
    ///Typically, you configure an Animator with an <see cref="T:UnityEditor.Animations.AnimatorController" /> asset. This asset determines which animation plays. To learn how to build an <see cref="T:UnityEditor.Animations.AnimatorController" />, consult [Animator Controller](xref:class-AnimatorController).
    ///Once configured with an <see cref="T:UnityEditor.Animations.AnimatorController" />, you can influence the flow of the state machine through the following methods:
    ///
    ///- Use <see cref="Animator.SetFloat" />, <see cref="Animator.SetInteger" />, <see cref="Animator.SetBool" />, or <see cref="Animator.SetTrigger" />, through <see cref="T:UnityEngine.AnimatorControllerParameter" />, to trigger an <see cref="T:UnityEditor.Animations.AnimatorController" /> transition.
    ///- Use <see cref="Animator.Play" />, <see cref="Animator.PlayInFixedTime" />, <see cref="M:UnityEngine.Animator.CrossFade" />, or <see cref="Animator.CrossFadeInFixedTime" /> to force the <see cref="T:UnityEditor.Animations.AnimatorController" /> to a specific state.
    ///- Use <see cref="Animator.SetLayerWeight" /> to modify the weight of an <see cref="T:UnityEditor.Animations.AnimatorControllerLayer" />.
    ///
    ///For more advanced use cases, you can control an Animator with the [Playables API](xref:Playables).
    ///
    ///**Animator execution**
    ///
    ///By default, an Animator evaluates on each frame, following <see cref="Time.deltaTime" />. On a frame where none of the  paired <see cref="Renderer">Renderers</see> are visible, the Animator only updates the position of the root GameObject. No other transforms or component properties are updated.
    ///To change this default behavior, use one of the following methods:
    ///
    ///- Use <see cref="Animator.updateMode" /> to choose how the Animator updates.
    ///- Use <see cref="Animator.cullingMode" /> to select what the Animator updates when none of the associated Renderers are visible.
    ///- Use <see cref="Animator.Update" /> to evaluate the Animator immediately. This is independent of the update mode.
    ///
    ///**Root Motion**
    ///
    ///Root Motion refers to the cumulative displacement of a GameObject hierarchy. For more information, consult [How Root Motion works](xref:RootMotion).
    ///
    ///When <see cref="Animator.applyRootMotion" /> is true, the Animator does the following on each frame:
    ///
    /// - Automatically calculates the displacement of the Root joint for the frame.
    /// - Adds this displacement to the position and rotation of the GameObject with the Animator Component.
    ///For information on how to override this behavior, consult [Scripting Root Motion](xref:ScriptingRootMotion).
    ///
    ///**Generic and Humanoid animation**
    ///
    ///The Animator evaluates two types of <see cref="AnimationClip">Animation Clips</see>: Generic and Humanoid.
    ///
    ///A Generic animation clip contains multiple animation curves where each curve animates a property of either a <see cref="Transform" /> or a <see cref="MonoBehaviour" />. A Generic clip is authored for and animates a specific <see cref="GameObject" /> hierarchy. If you attempt to use a Generic clip on a different <see cref="GameObject" /> hierarchy, it might not play back as expected.
    ///
    ///A Humanoid animation clip is designed for human or human-like bipedal <see cref="GameObject" /> hierarchies. To use a Humanoid clip on a bipedal <see cref="GameObject" /> hierarchy, you must configure an Animator with a [Humanoid Avatar](xref:AvatarCreationandSetup).
    ///
    ///You can reuse the same Humanoid clip on any Animator configured with a Humanoid <see cref="Avatar" /> to reduce runtime memory usage and build size. However, this increases CPU usage. Expect a 15 to 20 percent increase in the time spent animating a GameObject hierarchy when evaluating Humanoid AnimationClips.
    ///
    ///To determine if the benefits of using Humanoid clips is worth the cost in CPU peformance, perform your own experiments on your target platforms.
    ///
    ///It is recommended that you exclusively use Humanoid clips or Generic clips.
    ///
    ///**Inverse Kinematics**
    ///
    ///The Animator class includes inverse kinematics methods that you can use to configure dynamic interactions between a humanoid and scene objects. Consult [Inverse Kinematics](xref: InverseKinematics) for steps and an example.
    ///
    ///**SetTarget**
    ///
    ///When you want a character to interact with an object that is too far to reach with inverse kinematics, use <see cref="Animator.SetTarget" /> to adjust the position and rotation of a character over time to ensure its hand or foot reaches the object.
    ///
    ///**Bindings and performance**
    ///
    ///To track the properties that an Animator must write to, the Animator Component builds an internal collection of bindings. Each binding is built from the <see cref="T:UnityEditor.EditorCurveBinding" /> of each  <see cref="AnimationClip" /> associated with the Animator through assets and custom graphs.
    ///
    ///From this collection of bindings, the Animator builds an internal <see cref="T:UnityEngine.Animations.AnimationStream" /> which defines the size of the buffers to allocate for <see cref="AnimationClip" /> evaluation.
    ///
    ///After an Animator allocates its buffers, it iterates through each binding and searches for the appropriate <see cref="Component" /> in the corresponding <see cref="GameObject" /> hierarchy. The Animator keeps a reference to each binding so it can be written to in subsequent frames.
    ///
    ///This operation is called Rebinding, and it can be triggered by different events:
    ///
    ///- First initialization of the Animator Component when loading a <see cref="T:UnityEngine.SceneManagement.Scene" /> or instantiating a <c>Prefab</c>.
    ///- Changing the <see cref="T:UnityEditor.Animations.AnimatorController" /> or <see cref="AnimatorOverrideController" /> in <see cref="P:UnityEngine.Animator.runtimeAnimatorController" />.
    ///- Making changes to an <see cref="AnimatorOverrideController" /> set in <see cref="Animator.runtimeAnimatorController" />.
    ///- Making changes to a <see cref="T:UnityEngine.Playables.PlayableGraph" /> connected to the Animator.
    ///- Manually invoking <see cref="Animator.Rebind" />.
    ///- Enabling the <see cref="GameObject" /> to which the Animator Component is attached.
    ///
    ///**Avoid and minimize Rebind**
    ///
    ///Use the following strategies to avoid and minimize the occurrence of the Rebind operation:
    ///
    ///- The <see cref="T:UnityEditor.Animations.AnimatorController" /> asset is already optimized to create a known set of bindings at Edit time. The Rebind operation, triggered by changes to <see cref="P:UnityEngine.Animator.runtimeAnimatorController" />, only needs to bind the properties of the <see cref="T:UnityEngine.Animations.AnimationStream" /> with scene objects. If you need to change the <see cref="AnimationClip">AnimationClips</see> that the Animator evaluates during runtime, then switching to a new <see cref="T:UnityEditor.Animations.AnimatorController" /> asset is the strategy which will incur the smallest Rebind cost. Note that this will reset the state of the state machine to the default state(s) of the new AnimatorController.
    ///- Prioritize prebuilt <see cref="AnimatorOverrideController">AnimatorOverrideControllers</see> over dynamically built ones. Use an Animator Override Controller asset to change clips at runtime without resetting the state of the state machine. If you use an Animator Override Controller built at Edit time, the Rebind operation has the same cost as changing an <see cref="T:UnityEditor.Animations.AnimatorController" />. If you dynamically build an <see cref="T:UnityEditor.Animations.AnimatorController" /> at Runtime, the Rebind operation iterates over each clip in the <see cref="AnimatorOverrideController" /> because the bindings are unknown.
    ///- <see cref="T:UnityEngine.Playables.PlayableGraph">PlayableGraphs</see> execute a Rebind operation after every change to the graph, and large graphs have a significant Rebind cost. There are two optimization strategies you can apply: either maintain a small graph and update it as needed, or build a large graph and avoid changes as much as possible. Performance depends on the complexity of both your clips and your graph; experiment to determine which strategy is better suited for your use case.
    ///- If you disable a <see cref="GameObject" /> for pooling purposes, the Animator performs the Rebind operation when the GameObject is activated again. This might cancel the performance gained by pooling. It is recommended that you disable the components on a <see cref="GameObject" /> instead of disabling the <see cref="GameObject" /> itself. When you disable an Animator, it pauses evaluation but keeps the internal state intact.
    ///- When you add a new GameObject to a hierarchy associated with an Animator Component, you must manually invoke <see cref="Animator.Rebind" /> so that the Animator recognizes and is able to write to the new GameObject. If you add many new GameObjects, it is recommended that you add the GameObjects first and invoke <see cref="Animator.Rebind" /> once instead of invoking this method multiple times.
    ///
    ///
    ///**Default values**
    ///
    ///You can [configure states to write default values](xref:class-State) in the <see cref="T:UnityEditor.Animations.AnimatorController" />. When you enable <see cref="P:UnityEditor.Animations.AnimatorState.writeDefaultValues" /> and evaluate an <see cref="T:UnityEditor.Animations.AnimatorState" />, the Animator writes the default values for the properties that are not animated by the Animation Clips of that state.
    ///
    ///These default values are collected from the Scene when the Animator is first initialized and whenever a Rebind operation completes. If a Rebind occurs during evaluation, the Animator collects the current state of the properties in the scene as new default values. This might lead to problematic results because the new default values might be arbitrary.
    ///
    ///If you want to perform a Rebind operation and some of the states in your <see cref="T:UnityEditor.Animations.AnimatorController" /> rely on default values, use <see cref="Animator.writeDefaultValuesOnDisable" /> to ensure that the Animator restores all animated properties from their original values. This ensures consistent default values across the lifetime of the Animator component.
    ///
    ///However, writing default values back to the Scene also has a performance costs. If you are experiencing performance issues, consider not relying on default values or avoiding the Rebind operation.
    ///
    ///You can also manually restore the default values of animated properties with <see cref="Animator.WriteDefaultValues" />.
    ///
    ///**Recording system**
    ///
    ///The Animator includes a recording system that you can use to record and play back a maximum of ten thousand frames of animated properties. Consult <see cref="Animator.StartRecording" /> and <see cref="Animator.StartPlayback" /> for more information.
    ///
    ///**Other performance considerations**
    ///
    ///The Mecanim Animation System is complex. The choices that you make can affect the performance of your game. The following lists some things you should consider when you design your game:
    ///
    ///
    ///- The Rebind operation is resource intensive and might lead to CPU spikes. Use a single AnimatorController and avoid Rebind operations as much as possible. This generally results in more stable performance.
    ///- When you use an AnimatorController, Unity evaluates each non-synchronized state machine at every frame. This includes layers set to a weight of zero. To improve performance, avoid unused AnimatorController layers.
    ///- The Mecanim Animation System evaluates and updates Animator Components using parallel execution which divides the workload across multiple CPU cores. When you use <see cref="Animator.Update" /> to manually evaluate an Animator Component, Mecanim does not use parallel execution. To manually control the execution and benefit from parallel evaluation, bundle together multiple Animators in a <see cref="T:UnityEngine.Playables.PlayableGraph" /> and manually update the <see cref="T:UnityEngine.Playables.PlayableGraph" />. This takes advantage of parallel execution while still maintaining manual control.
    ///- Unity's <see cref="Transform" /> System only allows a single thread to write to a <see cref="Transform" /> hierarchy (a Root GameObject and its children) at a time. If you group multiple Animators under the same root <see cref="GameObject" />, this prevents Mecanim from taking advantage of multi-threading when parallel updating <see cref="Transform">Transforms</see> and might result in reduced performance. It is recommended that you avoid grouping Animators in hierarchies of GameObjects, unless necessary for parenting reasons.
    ///- <see cref="StateMachineBehaviour" /> introduces multiple synchronization points with the main thread. In some cases, callbacks can prevent parallel evaluation of multiple state machines. To avoid this issue, use <see cref="StateMachineBehaviour" /> sparingly.
    ///- To maintain determinism, the Animator writes every animated property at every frame regardless of whether the property value has changed. This can cause known performance issues when animating <see cref="RectTransform" /> components. To avoid these issues, use the <see cref="Animation" /> Component to animate <see cref="RectTransform" />.
    ///- Since the AnimatorController is a state machine, it continuously evaluates whether transitions must be taken. This evaluation occurs even when the AnimatorController reaches the end of the current state. This means that an idle Animator consumes CPU. For single-shot animations, and for GameObjects that are rarely animated, use the <see cref="Animation" /> Component or the [Playables API](xref:Playables).
    ///- The Humanoid system has a performance overhead. To avoid this overhead, use Generic animations wherever possible.```</remarks>
    public partial class Animator
    {
        ///<summary>Gets the list of AnimatorClipInfo currently played by the current state.</summary>
        ///<param name="layerIndex">The layer's index.</param>
        ///<seealso cref="AnimatorClipInfo" />
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("GetCurrentAnimationClipState is obsolete. Use GetCurrentAnimatorClipInfo instead (UnityUpgradable) -> GetCurrentAnimatorClipInfo(*)", true)]
        public AnimationInfo[] GetCurrentAnimationClipState(int layerIndex) { return null; }

        ///<summary>Gets the list of AnimatorClipInfo currently played by the next state.</summary>
        ///<remarks>Only valid during a transition.</remarks>
        ///<param name="layerIndex">The layer's index.</param>
        ///<seealso cref="AnimatorClipInfo" />
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("GetNextAnimationClipState is obsolete. Use GetNextAnimatorClipInfo instead (UnityUpgradable) -> GetNextAnimatorClipInfo(*)", true)]
        public AnimationInfo[] GetNextAnimationClipState(int layerIndex) { return null; }


        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Stop is obsolete. Use Animator.enabled = false instead", true)]
        public void Stop() {}
    }
}
