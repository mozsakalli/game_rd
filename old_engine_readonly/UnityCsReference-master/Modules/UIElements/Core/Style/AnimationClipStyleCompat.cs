// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    public partial interface IStyle
    {
        /// <summary>
        /// The animation clip applied to the element. Reads and writes the first entry of
        /// <see cref="animationNames"/>.
        /// </summary>
        StyleUIAnimationClip unityAnimationClip
        {
            get
            {
                var list = animationNames;
                if (list.keyword != StyleKeyword.Undefined)
                    return new StyleUIAnimationClip(list.keyword);
                var v = list.value;
                return new StyleUIAnimationClip(v != null && v.Count > 0 ? v[0] : null);
            }
            set
            {
                if (value.keyword != StyleKeyword.Undefined)
                    animationNames = new StyleList<UIAnimationClip>(value.keyword);
                else
                    animationNames = new StyleList<UIAnimationClip>(new List<UIAnimationClip> { value.value });
            }
        }

        /// <summary>
        /// Whether the element's animation is running or paused. Reads and writes the first entry of
        /// <see cref="animationPlayStates"/>.
        /// </summary>
        StyleEnum<AnimationPlayState> animationPlayState
        {
            get
            {
                var list = animationPlayStates;
                if (list.keyword != StyleKeyword.Undefined)
                    return new StyleEnum<AnimationPlayState>(list.keyword);
                var v = list.value;
                return new StyleEnum<AnimationPlayState>(v != null && v.Count > 0 ? v[0] : default);
            }
            set
            {
                if (value.keyword != StyleKeyword.Undefined)
                    animationPlayStates = new StyleList<AnimationPlayState>(value.keyword);
                else
                    animationPlayStates = new StyleList<AnimationPlayState>(new List<AnimationPlayState> { value.value });
            }
        }
    }

    public partial interface IResolvedStyle
    {
        /// <summary>
        /// The resolved animation clip applied to the element (the first entry of
        /// <see cref="animationNames"/>).
        /// </summary>
        UIAnimationClip unityAnimationClip
        {
            get
            {
                foreach (var clip in animationNames)
                    return clip;
                return default;
            }
        }

        /// <summary>
        /// The resolved play state of the element's animation (the first entry of
        /// <see cref="animationPlayStates"/>).
        /// </summary>
        AnimationPlayState animationPlayState
        {
            get
            {
                foreach (var playState in animationPlayStates)
                    return playState;
                return default;
            }
        }
    }
}
