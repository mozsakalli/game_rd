// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Unity.Multiplayer.PlayMode.Editor
{
    // View and Layout definitions as required by internal Unity.Windowlayouts.
    // The exception to the rule - property keys prefixed with "mppm_"
    [Serializable]
    internal class DynamicLayout
    {
        [Serializable]
        internal class DynamicView
        {
            [JsonInclude, JsonPropertyName("class_name"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string ClassName { get; internal set; }

            [JsonInclude, JsonPropertyName("horizontal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Horizontal { get; internal set; }

            [JsonInclude, JsonPropertyName("vertical"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Vertical { get; internal set; }

            [JsonInclude, JsonPropertyName("tabs"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Tabs { get; internal set; }

            [JsonInclude, JsonPropertyName("size"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public float Size { get; internal set; }

            [JsonInclude, JsonPropertyName("children"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public List<DynamicView> Children { get; internal set; }

            [JsonInclude, JsonPropertyName("mppm_id"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string Id { get; internal set; }

            [JsonInclude, JsonPropertyName("mppm_panel"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public string Panel { get; internal set; }
        }

        [JsonInclude, JsonPropertyName("restore_saved_layout"), JsonRequired]
        public bool RestoreSavedLayout { get; internal set; }

        [JsonInclude, JsonPropertyName("top_view"), JsonRequired]
        public DynamicView TopView { get; internal set; }

        [JsonInclude, JsonPropertyName("center_view"), JsonRequired]
        public DynamicView CenterView { get; internal set; }

        internal static string Serialize(ParsingSystemDelegates parsing, DynamicLayout layout)
        {
            return parsing.SerializeObjectFunc(layout);
        }

        internal static bool TryDeserialize(ParsingSystemDelegates parsing, string data, out DynamicLayout layout)
        {
            if (string.IsNullOrEmpty(data))
            {
                layout = null;
                return false;
            }

            try
            {
                layout = (DynamicLayout)parsing.DeserializeObjectFunc(data, typeof(DynamicLayout));
            }
            catch (JsonException e)
            {
                MppmLog.Warning($"Dynamic layout De-serialization failure: {e.Message}");
                layout = null;
            }

            return layout != null;
        }

        // Trims this dynamic layout and removes the views that were toggled off in the provided
        // layout flags (if they still exist)
        internal bool TrimDynamicLayout(LayoutFlags flags)
        {
            return TrimDisabledPanelsInLayoutFlags(flags, CenterView);
        }

        // Perform DFS, iterate to the nodes of the trees (layout panel controls)  and trim them off
        // if found disabled in the given layout flags
        private bool TrimDisabledPanelsInLayoutFlags(LayoutFlags layoutFlags, DynamicView view)
        {
            // A leaf view is considered a panel - determine if we should remove it.
            if (view.Children == null || view.Children.Count == 0)
            {
                return ShouldRemoveView(view, layoutFlags);
            }

            // Continue iterating through the tree
            var children = view.Children.ToArray();
            foreach (DynamicView child in children)
            {
                if (TrimDisabledPanelsInLayoutFlags(layoutFlags, child))
                {
                    view.Children.Remove(child);
                }
            }

            return view.Children.Count == 0;
        }

        // Determine if a view panel is disabled in the provided layoutFlags
        private bool ShouldRemoveView(DynamicView view, LayoutFlags layoutFlags)
        {
            // If there's no defined panel, it's misconfigured - remove it.
            if (view.Panel == null)
            {
                return true;
            }

            // Attempt to grab the corresponding flag for the given Panel
            // and strip the view out if we don't recognize it.
            LayoutFlags viewFlag = LayoutFlagsUtil.GetFlagForQualifiedName(view.Panel);
            if (viewFlag == LayoutFlags.None)
            {
                MppmLog.Warning($"Parsed unknown Panel in DynamicLayout {layoutFlags}");
                return true;
            }

            // Else populate the ClassName field required by native (Trunk)
            // to inflate the corresponding views.
            string[] words = view.Panel.Split('.');
            view.ClassName = words[^1];

            // Finally return if the layout flags have enabled this view.
            return !layoutFlags.HasFlag(viewFlag);
        }
    }
}
