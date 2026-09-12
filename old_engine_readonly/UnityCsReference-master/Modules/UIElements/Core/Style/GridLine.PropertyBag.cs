// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Properties;

namespace UnityEngine.UIElements
{
    public partial struct GridLine
    {
        internal class PropertyBag : ContainerPropertyBag<GridLine>
        {
            class PlacementProperty : Property<GridLine, GridLinePlacement>
            {
                public override string Name { get; } = nameof(placement);
                public override bool IsReadOnly { get; } = true;
                public override GridLinePlacement GetValue(ref GridLine container) => container.placement;
                public override void SetValue(ref GridLine container, GridLinePlacement value) => throw new System.InvalidOperationException();
            }

            class LineProperty : Property<GridLine, int>
            {
                public override string Name { get; } = nameof(line);
                public override bool IsReadOnly { get; } = true;
                public override int GetValue(ref GridLine container) => container.line;
                public override void SetValue(ref GridLine container, int value) => throw new System.InvalidOperationException();
            }

            class SpanProperty : Property<GridLine, int>
            {
                public override string Name { get; } = nameof(span);
                public override bool IsReadOnly { get; } = true;
                public override int GetValue(ref GridLine container) => container.span;
                public override void SetValue(ref GridLine container, int value) => throw new System.InvalidOperationException();
            }

            public PropertyBag()
                : base(3)
            {
                AddProperty(new PlacementProperty());
                AddProperty(new LineProperty());
                AddProperty(new SpanProperty());
            }
        }
    }
}
