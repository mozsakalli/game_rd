// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.UIToolkit.Editor;

/// <summary>
/// - Implements reference-counted selection tracking
/// - Creates and manages StyleSheetSelection ScriptableObjects
/// - Returns EntityId from selection objects for hierarchy integration
/// - Handles lifecycle with AcquireInstanceId() and ReleaseInstanceId()
/// </summary>
internal sealed partial class StyleSheetSelectionHandler : IStyleSheetSelectionHandler
{
    private readonly struct RefCountedSelection
    {
        public RefCountedSelection(UISelectionObject selectionObject, int count, bool alive = true)
        {
            this.SelectionObject = selectionObject;
            this.Count = count;
            this.Alive = alive;
        }

        public readonly UISelectionObject SelectionObject;
        public readonly int Count;
        public readonly bool Alive;

        public RefCountedSelection Acquire()
            => new(SelectionObject, Count + 1);

        public RefCountedSelection Release()
            => new(SelectionObject, Count - 1);

        public RefCountedSelection Kill()
            => new(SelectionObject, Count, false);
    }

    [AutoStaticsCleanupOnCodeReload]
    private static readonly Dictionary<(StyleSheet styleSheet, bool isReadOnly), RefCountedSelection> s_SelectionMappings = new();

    private StyleSheetSelection Acquire(StyleSheet styleSheet, bool isReadOnly)
    {
        var key = (styleSheet, isReadOnly);
        if (s_SelectionMappings.TryGetValue(key, out var refCounted))
        {
            s_SelectionMappings[key] = refCounted.Acquire();
            var existing = (StyleSheetSelection)refCounted.SelectionObject;
            existing.IsReadOnly = isReadOnly;
            return existing;
        }

        var selectionObject = ScriptableObject.CreateInstance<StyleSheetSelection>();
        selectionObject.hideFlags |= HideFlags.DontUnloadUnusedAsset | HideFlags.DontSaveInEditor;
        selectionObject.StyleSheet = styleSheet;
        selectionObject.IsReadOnly = isReadOnly;
        s_SelectionMappings[key] = new RefCountedSelection(selectionObject, 1);
        return selectionObject;
    }

    private void Remap(StyleSheet styleSheet, UISelectionObject instance)
    {
        // Remapping only applies to editable selections as inherited (read-only) stylesheets are never remapped.
        var key = (styleSheet, false);
        if (s_SelectionMappings.TryGetValue(key, out var refCounted))
        {
            if (refCounted.Count > 0 || !refCounted.Alive)
                Debug.LogError("Trying to remap something that is already mapped");
            return;
        }

        if (instance is StyleSheetSelection styleSheetSelection)
        {
            styleSheetSelection.StyleSheet = styleSheet;
        }

        s_SelectionMappings[key] = new RefCountedSelection(instance, 1);
    }

    private bool Release(StyleSheet styleSheet, bool isReadOnly)
    {
        var key = (styleSheet, isReadOnly);
        if (s_SelectionMappings.TryGetValue(key, out var refCounted))
        {
            if (refCounted.Count == 1 || !refCounted.Alive)
            {
                if (refCounted.Alive)
                {
                    Undo.ClearUndo(refCounted.SelectionObject);
                    Object.DestroyImmediate(refCounted.SelectionObject);
                }
                s_SelectionMappings.Remove(key);
                return true;
            }

            s_SelectionMappings[key] = refCounted.Release();
        }

        return false;
    }

    public EntityId AcquireInstanceId(StyleSheet styleSheet, bool isReadOnly)
    {
        var selection = Acquire(styleSheet, isReadOnly);
        return selection.GetEntityId();
    }

    public void ReleaseInstanceId(StyleSheet styleSheet, bool isReadOnly)
    {
        Release(styleSheet, isReadOnly);
    }

    public void Remap(List<StyleSheetRemap> remappings)
    {
        foreach (var remap in remappings)
        {
            var key = (remap.Previous, false);
            if (s_SelectionMappings.TryGetValue(key, out var selection))
            {
                s_SelectionMappings[key] = selection.Kill();
                Remap(remap.Remapped, selection.SelectionObject);
                Release(remap.Previous, false);
            }
        }
    }

    public void Clear()
    {
        foreach (var kvp in s_SelectionMappings)
        {
            if (kvp.Value.Alive)
            {
                Undo.ClearUndo(kvp.Value.SelectionObject);
                Object.DestroyImmediate(kvp.Value.SelectionObject);
            }
        }
        s_SelectionMappings.Clear();
    }
}
