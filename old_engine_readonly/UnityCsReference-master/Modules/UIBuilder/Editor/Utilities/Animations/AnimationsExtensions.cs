// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.UIElements.StyleSheets;

namespace Unity.UI.Builder
{
    static class AnimationsExtensions
    {
        // The animation shorthand plus its longhands. Mirrors TransitionsExtensions.IsTransitionId: the
        // Builder edits animations through StyleAnimationListView, so the per-item fields registered under
        // these longhand names must be kept out of the generic single-value style refresh, which can't
        // interpret the comma-separated list values these properties now hold.
        public static bool IsAnimationId(this StylePropertyId id)
        {
            switch (id)
            {
                case StylePropertyId.Animation:
                case StylePropertyId.AnimationDelay:
                case StylePropertyId.AnimationDirection:
                case StylePropertyId.AnimationDuration:
                case StylePropertyId.AnimationIterationCount:
                case StylePropertyId.AnimationPlayStates:
                case StylePropertyId.AnimationNames:
                case StylePropertyId.UnityAnimationClip:
                    return true;
            }

            return false;
        }
    }
}
