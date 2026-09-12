// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using Unity.Properties;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
using UnityEngine.UIElements.StyleSheets;

namespace Unity.UIToolkit.Editor
{
    [UxmlElement]
    internal partial class GridTrackListInlineField : VisualElement, IPropertyMappedAffordanceField
    {
        public static readonly BindingId valueProperty = nameof(value);

        readonly GridTrackAxisEditor m_Axis;
        readonly FieldAffordanceElement m_Affordance;
        readonly OverrideBarManipulator m_OverrideBar;
        readonly HashSet<ITrackablePropertyProvider> m_TrackedProviders = new();

        List<GridTrackSize> m_Value = new();

        const string k_UssPath = "UIToolkitAuthoring/Inspector/Controls/GridTrackList.uss";

        [UxmlAttribute]
        public string label
        {
            get => m_Axis.title;
            set => m_Axis.title = value;
        }

        // grid-auto-columns / -rows (implicit tracks) cannot use repeat() per the spec, so implicit fields
        // set this false to hide the per-row repeat selector.
        [UxmlAttribute]
        public bool allowRepeat
        {
            get => m_Axis.allowRepeat;
            set => m_Axis.allowRepeat = value;
        }

        public GridTrackListInlineField()
        {
            AddToClassList(ussClassName);

            if (EditorGUIUtility.Load(k_UssPath) is StyleSheet uss)
                styleSheets.Add(uss);

            m_Axis = new GridTrackAxisEditor(string.Empty); // title is set via the label attribute
            m_Axis.changed += OnAxisChanged;
            Add(m_Axis);

            m_Affordance = new FieldAffordanceElement();
            m_Axis.affordanceSlot.Add(m_Affordance);
            m_Axis.AddManipulator(new ContextualMenuManipulator(evt => m_Affordance.OnContextualMenuPopulate(evt)));

            m_OverrideBar = new OverrideBarManipulator();
            m_Axis.AddManipulator(m_OverrideBar);

            RegisterCallback<TrackPropertyEvent>(OnTrackProperty);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        [CreateProperty]
        public List<GridTrackSize> value
        {
            get => m_Value;
            set
            {
                if (SequenceEqual(m_Value, value))
                    return;
                m_Value = value != null ? new List<GridTrackSize>(value) : new List<GridTrackSize>();
                m_Axis.SetTracks(m_Value);
            }
        }

        // Only one property is bound to this field, so it owns the single affordance regardless of the id.
        public void GetAffordanceElements(StylePropertyId propertyId, List<FieldAffordanceElement> elements)
            => elements.Add(m_Affordance);

        void OnAxisChanged(List<GridTrackSize> list)
        {
            if (SequenceEqual(m_Value, list))
                return;
            m_Value = new List<GridTrackSize>(list);
            NotifyPropertyChanged(valueProperty);
        }

        void OnTrackProperty(TrackPropertyEvent evt)
        {
            if (evt.provider != null && m_TrackedProviders.Add(evt.provider))
                evt.provider.OnTrackedPropertyChanged += OnTrackedPropertyChanged;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            foreach (var provider in m_TrackedProviders)
                provider.OnTrackedPropertyChanged -= OnTrackedPropertyChanged;
            m_TrackedProviders.Clear();
        }

        // Single bound property, so any tracked-property signal here is this field's -> drive the one bar.
        void OnTrackedPropertyChanged(ITrackablePropertyProvider provider, string propertyName, TrackedPropertyType type)
            => m_OverrideBar.IsOverridden = type == TrackedPropertyType.MarkOverride;

        static bool SequenceEqual(List<GridTrackSize> a, List<GridTrackSize> b)
        {
            if (a == null || b == null)
                return ReferenceEquals(a, b);
            if (a.Count != b.Count)
                return false;
            for (int i = 0; i < a.Count; ++i)
                if (!a[i].Equals(b[i]))
                    return false;
            return true;
        }

        public static readonly string ussClassName = "grid-track-list-inline-field";
    }
}
