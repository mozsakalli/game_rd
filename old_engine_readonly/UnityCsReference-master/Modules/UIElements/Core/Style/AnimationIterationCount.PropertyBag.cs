// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Properties;

namespace UnityEngine.UIElements
{
    public partial struct AnimationIterationCount
    {
        internal class PropertyBag : ContainerPropertyBag<AnimationIterationCount>
        {
            class ValueProperty : Property<AnimationIterationCount, float>
            {
                public override string Name { get; } = nameof(value);
                public override bool IsReadOnly { get; } = false;
                public override float GetValue(ref AnimationIterationCount container) => container.value;
                public override void SetValue(ref AnimationIterationCount container, float value) => throw new System.InvalidOperationException();
            }

            class InfiniteProperty : Property<AnimationIterationCount, bool>
            {
                public override string Name { get; } = nameof(IsInfinite);
                public override bool IsReadOnly { get; } = true;
                public override bool GetValue(ref AnimationIterationCount container) => container.IsInfinite();
                public override void SetValue(ref AnimationIterationCount container, bool value) => throw new System.InvalidOperationException();
            }

            public PropertyBag()
                : base(2)
            {
                AddProperty(new ValueProperty());
                AddProperty(new InfiniteProperty());
            }
        }
    }
}
