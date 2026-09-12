// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor;

/// <summary>
/// A single old-instance -> new-instance remap produced when an authored <see cref="VisualElement"/>
/// is re-created by a clone. Consumed by the hierarchy node handlers to keep their node maps stable.
/// </summary>
internal readonly struct VisualElementRemap
{
    public VisualElementRemap(VisualElement previous, VisualElement remapped)
    {
        Previous = previous;
        Remapped = remapped;
    }

    public readonly VisualElement Previous;
    public readonly VisualElement Remapped;
}
