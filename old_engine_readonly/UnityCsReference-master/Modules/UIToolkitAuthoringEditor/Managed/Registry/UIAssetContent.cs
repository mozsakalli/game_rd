// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor;
using UnityEditor.UIElements;
using UnityEditor.UIElements.StyleSheets;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor;

/// <summary>
/// Restores an asset's unsaved (edited) content back into its live instance from exported UXML/USS text.
/// Used by <see cref="UIAssetRegistry"/> to implement "keep my changes" after an external reimport has
/// replaced the in-memory asset with the on-disk version.
/// </summary>
static class UIAssetContent
{
    public static void RestoreFromText(UnityEngine.Object asset, string text)
    {
        if (asset == null || string.IsNullOrEmpty(text))
            return;

        // An asset's name comes from its file, not from the UXML/USS text we are re-importing, so restore it
        // around the overwrite. Both of the paths this mirrors (the UI Builder's RestoreUnsavedChanges and
        // RestoreAssetsFromBackup) do the same.
        var originalName = asset.name;

        switch (asset)
        {
            case VisualTreeAsset vta:
                RestoreVisualTreeAsset(vta, text);
                break;
            case StyleSheet styleSheet:
                // The USS importer parses the text straight into the existing instance.
                new StyleSheetImporterImpl().Import(styleSheet, text);
                styleSheet.RequestRebuild();
                break;
            default:
                return;
        }

        asset.name = originalName;

        // The restored in-memory content differs from what is on disk, so the asset is unsaved. Flag it so
        // the dirty engine (which gates on EditorUtility.IsDirty) reports it modified and the "*" persists.
        EditorUtility.SetDirty(asset);
    }

    static void RestoreVisualTreeAsset(VisualTreeAsset target, string uxml)
    {
        new UXMLImporterImpl().ImportXmlFromString(uxml, out var source);
        if (source == null)
            return;

        // Keep target's own inlineSheet instance — live clones/bindings reference it — so preserve its
        // identity and overwrite only its contents (mirrors UI Builder's VisualTreeAsset.DeepOverwrite,
        // case 1263454). FromJsonOverwrite would otherwise swap in the source's inline sheet.
        var originalInlineSheet = target.inlineSheet;

        var json = JsonUtility.ToJson(source);
        JsonUtility.FromJsonOverwrite(json, target);
        target.SetupReferences();

        target.inlineSheet = originalInlineSheet;
        if (source.inlineSheet != null)
        {
            if (target.inlineSheet != null)
                OverwriteStyleSheet(source.inlineSheet, target.inlineSheet);
            else
                target.inlineSheet = CopyStyleSheet(source.inlineSheet);
        }
    }

    static void OverwriteStyleSheet(StyleSheet source, StyleSheet target)
    {
        var json = JsonUtility.ToJson(source);
        JsonUtility.FromJsonOverwrite(json, target);
        target.RequestRebuild();
    }

    static StyleSheet CopyStyleSheet(StyleSheet source)
    {
        var copy = StyleSheetUtility.CreateInstanceWithHideFlags();
        OverwriteStyleSheet(source, copy);
        return copy;
    }
}
