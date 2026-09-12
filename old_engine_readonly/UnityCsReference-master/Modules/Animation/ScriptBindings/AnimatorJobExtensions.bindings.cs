// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Internal;
using UnityEngine.Scripting.APIUpdating;

using Unity.Jobs;

namespace UnityEngine.Animations
{
    ///<summary>The type of custom stream property to create using BindCustomStreamProperty</summary>
    [MovedFrom("UnityEngine.Experimental.Animations")]
    public enum CustomStreamPropertyType
    {
        ///<summary>A float value.</summary>
        Float = BindType.Float,
        ///<summary>A boolean value.</summary>
        Bool = BindType.Bool,
        ///<summary>An integer value.</summary>
        Int = BindType.Int
    }

    ///<summary>Static class providing extension methods for <see cref="Animator" /> and the animation C# jobs.</summary>
    ///<remarks>The extension methods in this class can directly be used on an <see cref="Animator" />.</remarks>
    ///<seealso cref="IAnimationJobPlayable" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimatorJobExtensions.bindings.h")]
    [NativeHeader("Modules/Animation/Animator.h")]
    [NativeHeader("Modules/Animation/Director/AnimationStreamHandles.h")]
    [NativeHeader("Modules/Animation/Director/AnimationSceneHandles.h")]
    [NativeHeader("Modules/Animation/Director/AnimationStream.h")]
    [StaticAccessor("AnimatorJobExtensionsBindings", StaticAccessorType.DoubleColon)]
    public static class AnimatorJobExtensions
    {
        ///<summary>Creates a dependency between animator jobs and the job represented by the supplied job handle. To add multiple job dependencies, call this method for each job that need to run before the Animator's jobs.</summary>
        ///<remarks>After each update the <see cref="Animator" /> dependencies are flushed.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="jobHandle">The <see cref="JobHandle" /> of the job that needs to run before animator jobs.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Animations;
        ///using UnityEngine.Playables;
        ///
        ///using Unity.Collections;
        ///using Unity.Jobs;
        ///
        ///public class MyMonoBehaviour : MonoBehaviour
        ///{
        ///    NativeArray<int> input0;
        ///    NativeArray<int> input1;
        ///    NativeArray<int> output;
        ///
        ///    PlayableGraph graph;
        ///    Animator animator;
        ///
        ///    public struct SumDataForJob : IJob
        ///    {
        ///        [ReadOnly]
        ///        public NativeArray<int> input0;
        ///
        ///        [ReadOnly]
        ///        public NativeArray<int> input1;
        ///
        ///        public NativeArray<int> output;
        ///
        ///        public void Execute()
        ///        {
        ///            for (var i = 0; i < output.Length; ++i)
        ///                output[i] = input0[i] + input1[i];
        ///        }
        ///    }
        ///
        ///    public struct MyAnimationJob : IAnimationJob
        ///    {
        ///        [ReadOnly]
        ///        public NativeArray<int> input;
        ///
        ///        public float            sum;
        ///
        ///        public void ProcessRootMotion(AnimationStream stream)
        ///        {
        ///            sum = 0;
        ///            for (var i = 0; i < input.Length; ++i)
        ///                sum += input[i];
        ///        }
        ///
        ///        public void ProcessAnimation(AnimationStream stream) {}
        ///    }
        ///
        ///    public void Start()
        ///    {
        ///        input0 = new NativeArray<int>(10, Allocator.Persistent);
        ///        input1 = new NativeArray<int>(10, Allocator.Persistent);
        ///        output = new NativeArray<int>(10, Allocator.Persistent);
        ///
        ///        for (var i = 0; i < output.Length; i++)
        ///        {
        ///            input0[i] = i;
        ///            input1[i] = 10 * i;
        ///            output[i] = 0;
        ///        }
        ///
        ///        animator = gameObject.AddComponent<Animator>();
        ///
        ///        graph = PlayableGraph.Create();
        ///        var myAnimationJob = new MyAnimationJob();
        ///        myAnimationJob.input = output;
        ///
        ///        var scriptPlayable = AnimationScriptPlayable.Create(graph, myAnimationJob);
        ///        var playableOutput = AnimationPlayableOutput.Create(graph, "output", animator);
        ///
        ///        playableOutput.SetSourcePlayable(scriptPlayable);
        ///        graph.Play();
        ///    }
        ///
        ///    public void Update()
        ///    {
        ///        SumDataForJob sumJob;
        ///        sumJob.input0 = input0;
        ///        sumJob.input1 = input1;
        ///        sumJob.output = output;
        ///
        ///        var jobHandle = sumJob.Schedule();
        ///        animator.AddJobDependency(jobHandle);
        ///    }
        ///
        ///    public void OnDestroy()
        ///    {
        ///        graph.Destroy();
        ///        input0.Dispose();
        ///        input1.Dispose();
        ///        output.Dispose();
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public static void AddJobDependency(this Animator animator, JobHandle jobHandle)
        {
            InternalAddJobDependency(animator, jobHandle);
        }

        ///<summary>Create a TransformStreamHandle representing the new binding between the <see cref="Animator" /> and a <see cref="Transform" /> already bound to the <see cref="Animator" />.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to bind.</param>
        ///<returns>Returns the TransformStreamHandle that represents the new binding.</returns>
        public static TransformStreamHandle BindStreamTransform(this Animator animator, Transform transform)
        {
            TransformStreamHandle transformStreamHandle = new TransformStreamHandle();
            InternalBindStreamTransform(animator, transform, out transformStreamHandle);
            return transformStreamHandle;
        }

        ///<summary>Create a PropertyStreamHandle representing the new binding on the <see cref="Component" /> property of a <see cref="Transform" /> already bound to the <see cref="Animator" />.</summary>
        ///<remarks>You can bind a property that doesn't exist yet. For example you can bind a property on a <see cref="MonoBehaviour" /> that will be added later dynamically. In this case, you need to manually resolve the handle after adding the <see cref="MonoBehaviour" /> on the <see cref="GameObject" />, using <see cref="ResolveAllStreamHandles" />.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to target.</param>
        ///<param name="type">The <see cref="Component" /> type.</param>
        ///<param name="property">The property to bind.</param>
        ///<returns>Returns the PropertyStreamHandle that represents the new binding.</returns>
        public static PropertyStreamHandle BindStreamProperty(this Animator animator, Transform transform, Type type, string property)
        {
            return BindStreamProperty(animator, transform, type, property, false);
        }

        ///<summary>Create a custom property in the <see cref="AnimationStream" /> to pass extra data to downstream animation jobs in your graph. Custom properties created in the <see cref="AnimationStream" /> do not exist in the scene.</summary>
        ///<remarks>You can create custom properties in the <see cref="AnimationStream" /> that do not exist in the scene. This can be useful when you want to communicate extra data to downstream animation jobs in your graph.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="property">The name of the property.</param>
        ///<param name="type">The type of property to create (float, integer or boolean).</param>
        ///<returns>Returns the PropertyStreamHandle that represents the new binding.</returns>
        public static PropertyStreamHandle BindCustomStreamProperty(this Animator animator, string property, CustomStreamPropertyType type)
        {
            PropertyStreamHandle propertyStreamHandle = new PropertyStreamHandle();
            InternalBindCustomStreamProperty(animator, property, type, out propertyStreamHandle);
            return propertyStreamHandle;
        }

        ///<summary>Create a PropertyStreamHandle representing the new binding on the <see cref="Component" /> property of a <see cref="Transform" /> already bound to the <see cref="Animator" />.</summary>
        ///<remarks>You can bind a property that doesn't exist yet. For example you can bind a property on a <see cref="MonoBehaviour" /> that will be added later dynamically. In this case, you need to manually resolve the handle after adding the <see cref="MonoBehaviour" /> on the <see cref="GameObject" />, using <see cref="ResolveAllStreamHandles" />.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to target.</param>
        ///<param name="type">The <see cref="Component" /> type.</param>
        ///<param name="property">The property to bind.</param>
        ///<param name="isObjectReference">isObjectReference need to be set to true if the property to bind does animate an Object like <see cref="SpriteRenderer.sprite" />.</param>
        ///<returns>Returns the PropertyStreamHandle that represents the new binding.</returns>
        public static PropertyStreamHandle BindStreamProperty(this Animator animator, Transform transform, Type type, string property, [DefaultValue("false")] bool isObjectReference)
        {
            PropertyStreamHandle propertyStreamHandle = new PropertyStreamHandle();
            InternalBindStreamProperty(animator, transform, type, property, isObjectReference, out propertyStreamHandle);
            return propertyStreamHandle;
        }

        ///<summary>Create a TransformSceneHandle representing the new binding between the <see cref="Animator" /> and a <see cref="Transform" /> in the Scene.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to bind.</param>
        ///<returns>Returns the TransformSceneHandle that represents the new binding.</returns>
        public static TransformSceneHandle BindSceneTransform(this Animator animator, Transform transform)
        {
            TransformSceneHandle transformSceneHandle = new TransformSceneHandle();
            InternalBindSceneTransform(animator, transform, out transformSceneHandle);
            return transformSceneHandle;
        }

        ///<summary>Create a PropertySceneHandle representing the new binding on the <see cref="Component" /> property of a <see cref="Transform" /> in the Scene.</summary>
        ///<remarks>You can bind a property that doesn't exist yet. For example you can bind a property on a <see cref="MonoBehaviour" /> that will be added later dynamically. In this case, you need to manually resolve the handle after adding the <see cref="MonoBehaviour" /> on the <see cref="GameObject" />, using <see cref="ResolveAllSceneHandles" />.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to target.</param>
        ///<param name="type">The <see cref="Component" /> type.</param>
        ///<param name="property">The property to bind.</param>
        ///<returns>Returns the PropertySceneHandle that represents the new binding.</returns>
        public static PropertySceneHandle BindSceneProperty(this Animator animator, Transform transform, Type type, string property)
        {
            return BindSceneProperty(animator, transform, type, property, false);
        }

        ///<summary>Create a PropertySceneHandle representing the new binding on the <see cref="Component" /> property of a <see cref="Transform" /> in the Scene.</summary>
        ///<remarks>You can bind a property that doesn't exist yet. For example you can bind a property on a <see cref="MonoBehaviour" /> that will be added later dynamically. In this case, you need to manually resolve the handle after adding the <see cref="MonoBehaviour" /> on the <see cref="GameObject" />, using <see cref="ResolveAllSceneHandles" />.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="transform">The <see cref="Transform" /> to target.</param>
        ///<param name="type">The <see cref="Component" /> type.</param>
        ///<param name="property">The property to bind.</param>
        ///<param name="isObjectReference">isObjectReference need to be set to true if the property to bind does access an Object like <see cref="SpriteRenderer.sprite" />.</param>
        ///<returns>Returns the PropertySceneHandle that represents the new binding.</returns>
        public static PropertySceneHandle BindSceneProperty(this Animator animator, Transform transform, Type type, string property, [DefaultValue("false")] bool isObjectReference)
        {
            PropertySceneHandle propertySceneHandle = new PropertySceneHandle();
            InternalBindSceneProperty(animator, transform, type, property, isObjectReference, out propertySceneHandle);
            return propertySceneHandle;
        }

        ///<summary>Open a new stream on the <see cref="Animator" />.</summary>
        ///<remarks>The stream opened this way must be closed using <see cref="CloseAnimationStream" />.</remarks>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="stream">The new stream.</param>
        ///<returns>Returns whether or not the stream has been opened.</returns>
        public static bool OpenAnimationStream(this Animator animator, ref AnimationStream stream)
        {
            return InternalOpenAnimationStream(animator, ref stream);
        }

        ///<summary>Close a stream that has been opened using <see cref="OpenAnimationStream" />.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        ///<param name="stream">The stream to close.</param>
        public static void CloseAnimationStream(this Animator animator, ref AnimationStream stream)
        {
            InternalCloseAnimationStream(animator, ref stream);
        }

        ///<summary>Newly created handles are always resolved lazily on the next access when the jobs are run. To avoid a cpu spike while evaluating the jobs you can manually resolve all handles from the main thread.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        public static void ResolveAllStreamHandles(this Animator animator)
        {
            InternalResolveAllStreamHandles(animator);
        }

        ///<summary>Newly created handles are always resolved lazily on the next access when the jobs are run. To avoid a cpu spike while evaluating the jobs you can manually resolve all handles from the main thread.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        public static void ResolveAllSceneHandles(this Animator animator)
        {
            InternalResolveAllSceneHandles(animator);
        }

        internal static void UnbindAllHandles(this Animator animator)
        {
            InternalUnbindAllStreamHandles(animator);
            InternalUnbindAllSceneHandles(animator);
        }

        ///<summary>Removes all PropertyStreamHandles and TransformStreamHandles associated with the <see cref="Animator" /> instance. Use this method to manage the lifecycle of stream handles when the animated hierarchy changes.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        public static void UnbindAllStreamHandles(this Animator animator)
        {
            InternalUnbindAllStreamHandles(animator);
        }

        ///<summary>Removes all PropertySceneHandles and TransformSceneHandles associated with the <see cref="Animator" /> instance. Use this method to manage the lifecycle of scene handles when the animated hierarchy changes.</summary>
        ///<param name="animator">The <see cref="Animator" /> instance that calls this method.</param>
        public static void UnbindAllSceneHandles(this Animator animator)
        {
            InternalUnbindAllSceneHandles(animator);
        }

        extern private static void InternalAddJobDependency([NotNull] Animator animator, JobHandle jobHandle);

        extern private static void InternalBindStreamTransform([NotNull] Animator animator, [NotNull] Transform transform, out TransformStreamHandle transformStreamHandle);

        extern private static void InternalBindStreamProperty([NotNull] Animator animator, [NotNull] Transform transform, [NotNull] Type type, [NotNull] string property, bool isObjectReference, out PropertyStreamHandle propertyStreamHandle);

        extern private static void InternalBindCustomStreamProperty([NotNull] Animator animator, [NotNull] string property, CustomStreamPropertyType propertyType, out PropertyStreamHandle propertyStreamHandle);

        extern private static void InternalBindSceneTransform([NotNull] Animator animator, [NotNull] Transform transform, out TransformSceneHandle transformSceneHandle);

        extern private static void InternalBindSceneProperty([NotNull] Animator animator, [NotNull] Transform transform, [NotNull] Type type, [NotNull] string property, bool isObjectReference, out PropertySceneHandle propertySceneHandle);

        extern private static bool InternalOpenAnimationStream([NotNull] Animator animator, ref AnimationStream stream);

        extern private static void InternalCloseAnimationStream([NotNull] Animator animator, ref AnimationStream stream);

        extern private static void InternalResolveAllStreamHandles([NotNull] Animator animator);

        extern private static void InternalResolveAllSceneHandles([NotNull] Animator animator);

        extern private static void InternalUnbindAllStreamHandles([NotNull] Animator animator);

        extern private static void InternalUnbindAllSceneHandles([NotNull] Animator animator);
    }
}
