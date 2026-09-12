// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace Unity.UI.Builder
{
    /// <summary>
    /// USS property names for the animation shorthand and its six longhands. These match the property
    /// names declared in uss-properties.json (and the strings the Builder tests assert against).
    /// </summary>
    static class AnimationStyleNames
    {
        public const string Animation = "animation";
        public const string Clip = "animation-name";
        public const string Duration = "animation-duration";
        public const string Delay = "animation-delay";
        public const string IterationCount = "animation-iteration-count";
        public const string Direction = "animation-direction";
        public const string PlayState = "animation-play-state";
    }
}
