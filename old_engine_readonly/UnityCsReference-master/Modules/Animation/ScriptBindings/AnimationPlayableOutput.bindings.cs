// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Playables;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEngine.Animations
{
    ///<summary>Describes how an <see cref="AnimationStream" /> is initialized</summary>
    ///<remarks>On every frame, the values in the <see cref="AnimationStream" /> must be reinitialized. <see cref="AnimationStreamSource" /> describes which values should be used: the default values as stored in the <see cref="Animator" />, or the result of previous inputs.</remarks>
    [MovedFrom("UnityEngine.Experimental.Animations")]
    public enum AnimationStreamSource
    {
        ///<summary>
        ///  <see cref="AnimationStream" /> will be initialized with the default values from the <see cref="Animator" />.</summary>
        ///<remarks>Before it is modified during <see cref="IAnimationJob.ProcessAnimation" /> or <see cref="IAnimationJob.ProcessRootMotion" />, the <see cref="AnimationStream" /> contains the default values of the associated <see cref="Animator" />.
        ///
        ///This is the default behaviour for an <see cref="AnimationPlayableOutput" />.</remarks>
        ///<seealso cref="AnimationPlayableOutput.SetAnimationStreamSource" />
        DefaultValues,
        ///<summary>
        ///  <see cref="AnimationStream" /> will be initialized with the values from the previous <see cref="AnimationPlayableOutput" /> connected to the same <see cref="Animator" />.</summary>
        ///<remarks>Before it is modified during <see cref="IAnimationJob.ProcessAnimation" /> or <see cref="IAnimationJob.ProcessRootMotion" />, the <see cref="AnimationStream" /> contains the values written by any previous inputs.</remarks>
        ///<seealso cref="AnimationPlayableOutput.SetAnimationStreamSource" />
        PreviousInputs
    }

    ///<summary>A <see cref="IPlayableOutput" /> implementation that connects the <see cref="PlayableGraph" /> to an <see cref="Animator" /> in the Scene.</summary>
    ///<remarks>**NOTE:** You can use <see cref="PlayableOutputExtensions" /> methods on AnimationPlayableOutput objects.</remarks>
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationPlayableOutput.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationPlayableOutput.h")]
    [NativeHeader("Modules/Animation/Animator.h")]
    [NativeHeader("Runtime/Director/Core/HPlayableGraph.h")]
    [NativeHeader("Runtime/Director/Core/HPlayableOutput.h")]
    [StaticAccessor("AnimationPlayableOutputBindings", StaticAccessorType.DoubleColon)]
    [RequiredByNativeCode]
    public struct AnimationPlayableOutput : IPlayableOutput
    {
        private PlayableOutputHandle m_Handle;

        ///<summary>Creates an <see cref="AnimationPlayableOutput" /> in the <see cref="PlayableGraph" />.</summary>
        ///<remarks>The <see cref="Animator" /> plays the source <see cref="Playable" /> of the <see cref="AnimationPlayableOutput" />. This source Playable can be set with SetSourcePlayable.</remarks>
        ///<param name="graph">The <see cref="PlayableGraph" /> that will contain the <see cref="AnimationPlayableOutput" />.</param>
        ///<param name="name">The name of the output.</param>
        ///<param name="target">The <see cref="Animator" /> that will process the <see cref="PlayableGraph" />.</param>
        ///<returns>A new <see cref="AnimationPlayableOutput" /> attached to the <see cref="PlayableGraph" />.</returns>
        public static AnimationPlayableOutput Create(PlayableGraph graph, string name, Animator target)
        {
            PlayableOutputHandle handle;
            if (!AnimationPlayableGraphExtensions.InternalCreateAnimationOutput(ref graph, name, out handle))
                return AnimationPlayableOutput.Null;

            AnimationPlayableOutput output = new AnimationPlayableOutput(handle);
            output.SetTarget(target);

            return output;
        }

        internal AnimationPlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid())
            {
                if (!handle.IsPlayableOutputOfType<AnimationPlayableOutput>())
                    throw new InvalidCastException("Can't set handle: the playable is not an AnimationPlayableOutput.");
            }

            m_Handle = handle;
        }

        ///<exclude />
        public static AnimationPlayableOutput Null
        {
            get { return new AnimationPlayableOutput(PlayableOutputHandle.Null); }
        }

        ///<exclude />
        public PlayableOutputHandle GetHandle()
        {
            return m_Handle;
        }

        ///<exclude />
        public static implicit operator PlayableOutput(AnimationPlayableOutput output)
        {
            return new PlayableOutput(output.GetHandle());
        }

        ///<exclude />
        public static explicit operator AnimationPlayableOutput(PlayableOutput output)
        {
            return new AnimationPlayableOutput(output.GetHandle());
        }

        ///<summary>Returns the <see cref="Animator" /> that plays the animation graph.</summary>
        ///<returns>The targeted <see cref="Animator" />.</returns>
        public Animator GetTarget()
        {
            return InternalGetTarget(ref m_Handle);
        }

        ///<summary>Sets the <see cref="Animator" /> that plays the animation graph.</summary>
        ///<param name="value">The targeted <see cref="Animator" />.</param>
        public void SetTarget(Animator value)
        {
            InternalSetTarget(ref m_Handle, value);
        }

        ///<summary>Gets the stream source of the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<returns>Returns the <see cref="AnimationStreamSource" /> of the output.</returns>
        ///<seealso cref="AnimationStreamSource" />
        public AnimationStreamSource GetAnimationStreamSource()
        {
            return InternalGetAnimationStreamSource(m_Handle);
        }

        ///<summary>Sets the stream source for the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<remarks>When setting the <see cref="AnimationStreamSource" /> of the output to <see cref="AnimationStreamSource.DefaultValues" />, the <see cref="AnimationStream" /> of this output initalizes every frame with the default values of the <see cref="Animator" />.
        ///
        ///When setting the <see cref="AnimationStreamSource" /> of the output to <see cref="AnimationStreamSource.PreviousInputs" />, the <see cref="AnimationStream" /> of this output initalizes every frame with the result of any previously evaluated outputs on the same <see cref="Animator" />.
        ///
        ///If you use the graph connected to an <see cref="AnimationPlayableOutput" /> to post-process the result of other Animation graphs connected to the same <see cref="Animator" />, you should use <see cref="AnimationStreamSource.PreviousInputs" />. For example, if you use the <see cref="AnimationStream" /> to build an Inverse Kinematics constraint to post-process the built-in <see cref="T:UnityEditor.Animations.AnimatorController" />, your <see cref="AnimationPlayableOutput" /> should be set to <see cref="AnimationStreamSource.PreviousInputs" />.
        ///
        ///In order to start the <see cref="AnimationStream" /> from a blank slate, you should use <see cref="AnimationStreamSource.DefaultValues" />.
        ///For example, to build a custom animation source starting from the default pose, the <see cref="AnimationPlayableOutput" /> should be set to <see cref="AnimationStreamSource.DefaultValues" />.</remarks>
        ///<param name="streamSource">The <see cref="AnimationStreamSource" /> to apply on this output.</param>
        ///<seealso cref="AnimationStreamSource" />
        public void SetAnimationStreamSource(AnimationStreamSource streamSource)
        {
            InternalSetAnimationStreamSource(m_Handle, streamSource);
        }

        ///<summary>Gets the priority index of the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<remarks>Default sorting order is set to 100.</remarks>
        ///<returns>Returns the sorting order of the output.</returns>
        public ushort GetSortingOrder()
        {
            return (ushort)InternalGetSortingOrder(m_Handle);
        }

        ///<summary>Sets the sorting order for the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<param name="sortingOrder">The sorting order to apply to this output.</param>
        public void SetSortingOrder(ushort sortingOrder)
        {
            InternalSetSortingOrder(m_Handle, (int)sortingOrder);
        }

        [NativeMethod(ThrowsException = true)]
        extern private static Animator InternalGetTarget(ref PlayableOutputHandle handle);

        [NativeMethod(ThrowsException = true)]
        extern private static void InternalSetTarget(ref PlayableOutputHandle handle, Animator target);

        [NativeMethod(ThrowsException = true)]
        extern private static AnimationStreamSource InternalGetAnimationStreamSource(PlayableOutputHandle output);
        [NativeMethod(ThrowsException = true)]
        extern private static void InternalSetAnimationStreamSource(PlayableOutputHandle output, AnimationStreamSource streamSource);

        [NativeMethod(ThrowsException = true)]
        extern private static int InternalGetSortingOrder(PlayableOutputHandle output);
        [NativeMethod(ThrowsException = true)]
        extern private static void InternalSetSortingOrder(PlayableOutputHandle output, int sortingOrder);
    }
}
