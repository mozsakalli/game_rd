using System.Collections.Generic;

namespace DigitoyEngine;

public sealed partial class SceneDoc
{
    static readonly string[][] LayoutBoxGroups =
    {
        new[] { "padding", "padLeft", "padTop", "padRight", "padBottom" },
        new[] { "border", "borderLeft", "borderTop", "borderRight", "borderBottom" },
        new[] { "radius", "radiusTL", "radiusTR", "radiusBR", "radiusBL" },
        new[] { "slice9", "slice9Left", "slice9Top", "slice9Right", "slice9Bottom" },
    };

    static void MigrateLayoutBox(CompDoc component)
    {
        if (component.Type != nameof(LayoutBox))
            return;
        foreach (var group in LayoutBoxGroups)
        {
            bool hasGrouped = component.Props.Exists(p => p.Key == group[0]);
            var values = new string[] { "0", "0", "0", "0" };
            bool found = false;
            for (int axis = 0; axis < 4; axis++)
            {
                string oldName = group[axis + 1];
                int index = component.Props.FindIndex(p => p.Key == oldName);
                if (index < 0)
                    continue;
                values[axis] = component.Props[index].Value.Scalar;
                component.Props.RemoveAt(index);
                found = true;
            }
            if (found && !hasGrouped)
                component.Props.Add(new(group[0], DocNode.Scal(string.Join(" ", values))));
        }
    }

    // Legacy prefab deltas change one axis, preserving the other source axes.
    internal static bool ApplyLegacyLayoutBoxOverride(CompDoc component, string property, DocNode value)
    {
        if (component.Type != nameof(LayoutBox))
            return false;
        foreach (var group in LayoutBoxGroups)
        {
            for (int axis = 0; axis < 4; axis++)
            {
                if (property != group[axis + 1])
                    continue;
                int index = component.Props.FindIndex(p => p.Key == group[0]);
                Vec4 current = index < 0 ? default
                    : SerializedType.ParseVec4(component.Props[index].Value.Scalar);
                float next = (float)SerializedType.Parse(value.Scalar, SerializedType.Kind.Float, typeof(float), null);
                switch (axis)
                {
                    case 0: current.x = next; break;
                    case 1: current.y = next; break;
                    case 2: current.z = next; break;
                    case 3: current.w = next; break;
                }
                var pair = new KeyValuePair<string, DocNode>(group[0],
                    DocNode.Scal(SerializedType.Format(current, SerializedType.Kind.Vec4)));
                if (index < 0) component.Props.Add(pair);
                else component.Props[index] = pair;
                return true;
            }
        }
        return false;
    }
}
