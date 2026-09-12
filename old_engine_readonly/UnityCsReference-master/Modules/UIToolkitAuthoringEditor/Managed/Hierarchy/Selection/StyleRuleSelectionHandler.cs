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

internal sealed partial class StyleRuleSelectionHandler : IStyleRuleSelectionHandler
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
    private static readonly Dictionary<(StyleRule rule, bool isReadOnly), RefCountedSelection> s_SelectionMappings = new();

    private StyleRuleSelection Acquire(StyleRule rule, bool isReadOnly)
    {
        var key = (rule, isReadOnly);
        if (s_SelectionMappings.TryGetValue(key, out var refCounted))
        {
            s_SelectionMappings[key] = refCounted.Acquire();
            var existing = (StyleRuleSelection)refCounted.SelectionObject;
            existing.IsReadOnly = isReadOnly;
            return existing;
        }

        var selectionObject = ScriptableObject.CreateInstance<StyleRuleSelection>();
        selectionObject.hideFlags |= HideFlags.DontUnloadUnusedAsset | HideFlags.DontSaveInEditor;
        selectionObject.StyleRule = rule;
        selectionObject.IsReadOnly = isReadOnly;
        s_SelectionMappings[key] = new RefCountedSelection(selectionObject, 1);
        return selectionObject;
    }

    private void Remap(StyleRule rule, UISelectionObject instance)
    {
        // Remapping only applies to editable selections as inherited (read-only) style rules are never remapped.
        var key = (rule, false);
        if (s_SelectionMappings.TryGetValue(key, out var refCounted))
        {
            if (refCounted.Count > 0 || !refCounted.Alive)
                Debug.LogError("Trying to remap something that is already mapped");
            return;
        }

        if (instance is StyleRuleSelection styleRuleSelection)
        {
            styleRuleSelection.StyleRule = rule;
        }

        s_SelectionMappings[key] = new RefCountedSelection(instance, 1);
    }

    private bool Release(StyleRule rule, bool isReadOnly)
    {
        var key = (rule, isReadOnly);
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

    public EntityId AcquireInstanceId(StyleRule rule, bool isReadOnly)
    {
        var selection = Acquire(rule, isReadOnly);
        return selection.GetEntityId();
    }

    public void ReleaseInstanceId(StyleRule rule, bool isReadOnly)
    {
        Release(rule, isReadOnly);
    }

    public void Remap(List<StyleRuleRemap> remappings)
    {
        foreach (var remap in remappings)
        {
            var key = (remap.Previous, false);
            if (s_SelectionMappings.TryGetValue(key, out var selection))
            {
                if (!ReferenceEquals(remap.Previous, remap.Remapped))
                {
                    s_SelectionMappings[key] = selection.Kill();
                    Remap(remap.Remapped, selection.SelectionObject);
                    Release(remap.Previous, false);
                }

                // Force the selection object to notify observers
                if (selection.SelectionObject is not StyleRuleSelection styleRuleSelection)
                    continue;

                styleRuleSelection.StyleRule = null;
                styleRuleSelection.StyleRule = remap.Remapped;
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
