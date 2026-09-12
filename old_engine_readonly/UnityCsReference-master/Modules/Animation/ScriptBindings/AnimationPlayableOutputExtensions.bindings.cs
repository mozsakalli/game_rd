// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Animations;

namespace UnityEngine.Experimental.Animations
{
    ///<summary>Static class providing experimental extension methods for <see cref="AnimationPlayableOutput" /> .</summary>
    ///<remarks>The extension methods in this class can directly be used on an <see cref="AnimationPlayableOutput" />.</remarks>
    ///<seealso cref="AnimationPlayableOutput" />
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationPlayableOutputExtensions.bindings.h")]
    [NativeHeader("Modules/Animation/AnimatorDefines.h")]
    [StaticAccessor("AnimationPlayableOutputExtensionsBindings", StaticAccessorType.DoubleColon)]
    public static class AnimationPlayableOutputExtensions
    {
        ///<summary>Gets the stream source of the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<param name="output">The <see cref="AnimationPlayableOutput" /> instance that calls this method.</param>
        ///<returns>Returns the <see cref="AnimationStreamSource" /> of the output.</returns>
        ///<seealso cref="AnimationStreamSource" />
        [Obsolete("Use AnimationPlayableOutput.GetAnimationStreamSource() instead")]
        public static AnimationStreamSource GetAnimationStreamSource(this AnimationPlayableOutput output)
            => output.GetAnimationStreamSource();

        ///<summary>Sets the stream source for the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<remarks>When setting the <see cref="AnimationStreamSource" /> of the output to <see cref="AnimationStreamSource.DefaultValues" />, the <see cref="AnimationStream" /> of this output initalizes every frame with the default values of the <see cref="Animator" />.
        ///
        ///When setting the <see cref="AnimationStreamSource" /> of the output to <see cref="AnimationStreamSource.PreviousInputs" />, the <see cref="AnimationStream" /> of this output initalizes every frame with the result of any previously evaluated outputs on the same <see cref="Animator" />.
        ///
        ///If you use the graph connected to an <see cref="AnimationPlayableOutput" /> to post-process the result of other Animation graphs connected to the same <see cref="Animator" />, you should use <see cref="AnimationStreamSource.PreviousInputs" />. For example, if you use the <see cref="AnimationStream" /> to build an Inverse Kinematics constraint to post-process the built-in <see cref="T:UnityEditor.Animations.AnimatorController" />, your <see cref="AnimationPlayableOutput" /> should be set to <see cref="AnimationStreamSource.PreviousInputs" />.
        ///
        ///In order to start the <see cref="AnimationStream" /> from a blank slate, you should use <see cref="AnimationStreamSource.DefaultValues" />.
        ///For example, to build a custom animation source starting from the default pose, the <see cref="AnimationPlayableOutput" /> should be set to <see cref="AnimationStreamSource.DefaultValues" />.</remarks>
        ///<param name="output">The <see cref="AnimationPlayableOutput" /> instance that calls this method.</param>
        ///<param name="streamSource">The <see cref="AnimationStreamSource" /> to apply on this output.</param>
        ///<seealso cref="AnimationStreamSource" />
        [Obsolete("Use AnimationPlayableOutput.SetAnimationStreamSource(AnimationStreamSource) instead")]
        public static void SetAnimationStreamSource(this AnimationPlayableOutput output, AnimationStreamSource streamSource)
            => output.SetAnimationStreamSource(streamSource);

        ///<summary>Gets the priority index of the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<remarks>Default sorting order is set to 100.</remarks>
        ///<param name="output">The <see cref="AnimationPlayableOutput" /> instance that calls this method.</param>
        ///<returns>Returns the sorting order of the output.</returns>
        [Obsolete("Use AnimationPlayableOutput.GetSortingOrder() instead")]
        public static ushort GetSortingOrder(this AnimationPlayableOutput output)
            => output.GetSortingOrder();

        ///<summary>Sets the sorting order for the specified <see cref="AnimationPlayableOutput" />.</summary>
        ///<param name="output">The <see cref="AnimationPlayableOutput" /> instance that calls this method.</param>
        ///<param name="sortingOrder">The sorting order to apply to this output.</param>
        [Obsolete("Use AnimationPlayableOutput.SetSortingOrder(ushort) instead")]
        public static void SetSortingOrder(this AnimationPlayableOutput output, ushort sortingOrder)
            => output.SetSortingOrder(sortingOrder);
    }
}
