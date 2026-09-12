// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Playables;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>A <see cref="Playable" /> that can run a custom, multi-threaded animation job.</summary>
    ///<remarks>This playable allows to create a custom C# job that will give read and write access to the <see cref="AnimationStream" /> during the animation process pass in the <see cref="PlayableGraph" />. The C# job must implement the interface <see cref="IAnimationJob" />.
    ///
    ///NOTE: You can use <see cref="PlayableExtensions" /> methods with AnimationScriptPlayable objects.</remarks>
    ///<seealso cref="IAnimationJob" />
    ///<seealso cref="AnimationScriptPlayable.Create" />
    [MovedFrom("UnityEngine.Experimental.Animations")]
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationScriptPlayable.bindings.h")]
    [NativeHeader("Runtime/Director/Core/HPlayableGraph.h")]
    [NativeHeader("Runtime/Director/Core/HPlayable.h")]
    [StaticAccessor("AnimationScriptPlayableBindings", StaticAccessorType.DoubleColon)]
    [RequiredByNativeCode]
    public struct AnimationScriptPlayable : IAnimationJobPlayable, IEquatable<AnimationScriptPlayable>
    {
        private PlayableHandle m_Handle;

        static readonly AnimationScriptPlayable m_NullPlayable = new AnimationScriptPlayable(PlayableHandle.Null);
        ///<exclude />
        public static AnimationScriptPlayable Null { get { return m_NullPlayable; } }

        ///<summary>Creates an <see cref="AnimationScriptPlayable" /> in the <see cref="PlayableGraph" />.</summary>
        ///<remarks>
        ///  <para>This playable contains a job implementing an <see cref="IAnimationJob" />. This interface defines two methods that will be called while processing the <see cref="PlayableGraph" />.
        ///
        ///Here is an example of how to create an <see cref="AnimationScriptPlayable" /> with a simple <see cref="IAnimationJob" />:</para>
        ///  <para />
        ///</remarks>
        ///<param name="graph">The PlayableGraph object that will own the AnimationScriptPlayable.</param>
        ///<param name="jobData">The <see cref="IAnimationJob" /> to execute when processing the playable.</param>
        ///<param name="inputCount">The number of inputs on the playable.</param>
        ///<returns>A new <see cref="AnimationScriptPlayable" /> linked to the <see cref="PlayableGraph" />.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Playables;
        ///using UnityEngine.Animations;
        ///
        ///public struct AnimationJob : IAnimationJob
        ///{
        ///    public int userData;
        ///
        ///    public void ProcessRootMotion(AnimationStream stream)
        ///    {
        ///        // This method is called during the root motion process pass.
        ///    }
        ///
        ///    public void ProcessAnimation(AnimationStream stream)
        ///    {
        ///        // This method is called during the animation process pass.
        ///        Debug.Log(string.Format("Value of the userData: {0}", userData));
        ///    }
        ///}
        ///
        ///[RequireComponent(typeof(Animator))]
        ///public class AnimationScriptExample : MonoBehaviour
        ///{
        ///    PlayableGraph m_Graph;
        ///    AnimationScriptPlayable m_AnimationScriptPlayable;
        ///
        ///    void OnEnable()
        ///    {
        ///        m_Graph = PlayableGraph.Create("AnimationScriptExample");
        ///        var output = AnimationPlayableOutput.Create(m_Graph, "ouput", GetComponent<Animator>());
        ///
        ///        var animationJob = new AnimationJob();
        ///        m_AnimationScriptPlayable = AnimationScriptPlayable.Create(m_Graph, animationJob);
        ///
        ///        output.SetSourcePlayable(m_AnimationScriptPlayable);
        ///        m_Graph.Play();
        ///    }
        ///
        ///    void Update()
        ///    {
        ///        var animationJob = m_AnimationScriptPlayable.GetJobData<AnimationJob>();
        ///        ++animationJob.userData;
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
        ///<seealso cref="IAnimationJob" />
        ///<seealso cref="AnimatorJobExtensions" />
        public static AnimationScriptPlayable Create<T>(PlayableGraph graph, T jobData, int inputCount = 0)
            where T : struct, IAnimationJob
        {
            var handle = CreateHandle<T>(graph, inputCount);
            var playable = new AnimationScriptPlayable(handle);
            playable.SetJobData(jobData);
            return playable;
        }

        private static PlayableHandle CreateHandle<T>(PlayableGraph graph, int inputCount)
            where T : struct, IAnimationJob
        {
            IntPtr jobReflectionData = ProcessAnimationJobStruct<T>.GetJobReflectionData();

            PlayableHandle handle = PlayableHandle.Null;
            if (!CreateHandleInternal(graph, ref handle, jobReflectionData))
                return PlayableHandle.Null;

            handle.SetInputCount(inputCount);

            return handle;
        }

        internal AnimationScriptPlayable(PlayableHandle handle)
        {
            if (handle.IsValid())
            {
                if (!handle.IsPlayableOfType<AnimationScriptPlayable>())
                    throw new InvalidCastException("Can't set handle: the playable is not an AnimationScriptPlayable.");
            }

            m_Handle = handle;
        }

        ///<exclude />
        public PlayableHandle GetHandle()
        {
            return m_Handle;
        }

        private void CheckJobTypeValidity<T>()
        {
            var jobType = GetHandle().GetJobType();
            if (jobType != typeof(T))
                throw new ArgumentException(string.Format("Wrong type: the given job type ({0}) is different from the creation job type ({1}).", typeof(T).FullName, jobType.FullName));
        }

        ///<summary>Gets the job data contained in the playable.</summary>
        ///<remarks>The generic type must be the same as the one used for the creation of the playable, otherwise an <c>ArgumentException</c> is raised.</remarks>
        ///<returns>Returns the <see cref="IAnimationJob" /> data contained in the playable.</returns>
        ///<seealso cref="AnimationScriptPlayable.Create" />
        public unsafe T GetJobData<T>()
            where T : struct, IAnimationJob
        {
            CheckJobTypeValidity<T>();

            T data;
            UnsafeUtility.CopyPtrToStructure<T>((void*)GetHandle().GetJobData(), out data);
            return data;
        }

        ///<summary>Sets a new job data in the playable.</summary>
        ///<remarks>The generic type must be the same as the one used for the creation of the playable, otherwise an <c>ArgumentException</c> is raised.</remarks>
        ///<param name="jobData">The new <see cref="IAnimationJob" /> data to set in the playable.</param>
        ///<seealso cref="AnimationScriptPlayable.Create" />
        public unsafe void SetJobData<T>(T jobData)
            where T : struct, IAnimationJob
        {
            CheckJobTypeValidity<T>();

            UnsafeUtility.CopyStructureToPtr(ref jobData, (void*)GetHandle().GetJobData());
        }

        ///<exclude />
        public static implicit operator Playable(AnimationScriptPlayable playable)
        {
            return new Playable(playable.GetHandle());
        }

        ///<exclude />
        public static explicit operator AnimationScriptPlayable(Playable playable)
        {
            return new AnimationScriptPlayable(playable.GetHandle());
        }

        ///<exclude />
        public bool Equals(AnimationScriptPlayable other)
        {
            return GetHandle() == other.GetHandle();
        }

        ///<summary>Sets the new value for processing the inputs or not.</summary>
        ///<remarks>In some cases, like for custom mixers, it is wanted to have full control over which inputs are processed or not. This method allows to set that: when set to <c>true</c> (the default value), all the inputs are processed before processing the current <see cref="AnimationScriptPlayable" />; when set to <c>false</c>, the playable inputs aren't processed and the user can force to process specific inputs using <see cref="AnimationStream.GetInputStream" /> on the <c>stream</c> provided with the methods in <see cref="IAnimationJob" />.</remarks>
        ///<param name="value">The new value for processing the inputs or not.</param>
        ///<seealso cref="AnimationStream" />
        public void SetProcessInputs(bool value)
        {
            SetProcessInputsInternal(GetHandle(), value);
        }

        ///<summary>Returns whether the playable inputs will be processed or not.</summary>
        ///<returns>
        ///  <c>true</c> if the inputs will be processed; <c>false</c> otherwise.</returns>
        ///<seealso cref="AnimationScriptPlayable.SetProcessInputs" />
        public bool GetProcessInputs()
        {
            return GetProcessInputsInternal(GetHandle());
        }

        [NativeMethod(ThrowsException = true)]
        extern private static bool CreateHandleInternal(PlayableGraph graph, ref PlayableHandle handle, IntPtr jobReflectionData);

        [NativeMethod(ThrowsException = true)]
        extern private static void SetProcessInputsInternal(PlayableHandle handle, bool value);

        [NativeMethod(ThrowsException = true)]
        extern private static bool GetProcessInputsInternal(PlayableHandle handle);
    }
}
