// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngine.Playables;

using UnityObject = UnityEngine.Object;

namespace UnityEngine.Animations
{
    ///<summary>An implementation of <see cref="IPlayable" /> that controls an animation mixer.</summary>
    ///<remarks>NOTE: You can use <see cref="PlayableExtensions" /> methods with AnimationMixerPlayable objects.</remarks>
    [NativeHeader("Modules/Animation/ScriptBindings/AnimationMixerPlayable.bindings.h")]
    [NativeHeader("Modules/Animation/Director/AnimationMixerPlayable.h")]
    [NativeHeader("Runtime/Director/Core/HPlayable.h")]
    [StaticAccessor("AnimationMixerPlayableBindings", StaticAccessorType.DoubleColon)]
    [RequiredByNativeCode]
    public struct AnimationMixerPlayable : IPlayable, IEquatable<AnimationMixerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimationMixerPlayable m_NullPlayable = new AnimationMixerPlayable(PlayableHandle.Null);
        ///<summary>Returns an invalid AnimationMixerPlayable.</summary>
        public static AnimationMixerPlayable Null { get { return m_NullPlayable; } }

        ///<exclude />
        [Obsolete("normalizeWeights is obsolete. It has no effect and will be removed.")]
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount, bool normalizeWeights)
        {
            return Create(graph, inputCount);
        }

        ///<summary>Creates an <see cref="AnimationMixerPlayable" /> in the <see cref="PlayableGraph" />.</summary>
        ///<param name="graph">The <see cref="PlayableGraph" /> that will contain the new <see cref="AnimationMixerPlayable" />.</param>
        ///<param name="inputCount">The number of inputs that the mixer will update.</param>
        ///<returns>Returns a new <see cref="AnimationMixerPlayable" /> linked to the <see cref="PlayableGraph" />.</returns>
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount = 0)
        {
            var handle = CreateHandle(graph, inputCount);
            return new AnimationMixerPlayable(handle);
        }

        private static PlayableHandle CreateHandle(PlayableGraph graph, int inputCount = 0)
        {
            PlayableHandle handle = PlayableHandle.Null;
            if (!CreateHandleInternal(graph, ref handle))
                return PlayableHandle.Null;
            handle.SetInputCount(inputCount);
            return handle;
        }

        internal AnimationMixerPlayable(PlayableHandle handle)
        {
            if (handle.IsValid())
            {
                if (!handle.IsPlayableOfType<AnimationMixerPlayable>())
                    throw new InvalidCastException("Can't set handle: the playable is not an AnimationMixerPlayable.");
            }

            m_Handle = handle;
        }

        ///<exclude />
        public PlayableHandle GetHandle()
        {
            return m_Handle;
        }

        ///<exclude />
        public static implicit operator Playable(AnimationMixerPlayable playable)
        {
            return new Playable(playable.GetHandle());
        }

        ///<exclude />
        public static explicit operator AnimationMixerPlayable(Playable playable)
        {
            return new AnimationMixerPlayable(playable.GetHandle());
        }

        ///<exclude />
        public bool Equals(AnimationMixerPlayable other)
        {
            return GetHandle() == other.GetHandle();
        }

        [NativeMethod(ThrowsException = true)]
        extern private static bool CreateHandleInternal(PlayableGraph graph, ref PlayableHandle handle);
    }
}
