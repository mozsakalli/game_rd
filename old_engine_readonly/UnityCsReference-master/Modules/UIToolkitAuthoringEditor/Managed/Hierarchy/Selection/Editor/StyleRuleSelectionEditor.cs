// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor;

[CustomEditor(typeof(StyleRuleSelection))]
class StyleRuleSelectionEditor : UISelectionEditor
{
    StyleRuleInspector m_Inspector;
    StyleRuleHeader m_Header;

    StyleRuleSelection Target => (StyleRuleSelection)target;

    protected override UIInspector Inspector => m_Inspector;

    protected override StyleInspectorAnimationRecordingContext CreateRecordingContext()
        => StyleInspectorAnimationRecordingContext.TryCreateForRule(Target.StyleRule);

    internal override VisualElement CreateInspectorHeaderGUI()
    {
        m_Header = new StyleRuleHeader { name = "Header" };
        m_Header.Rule = Target.StyleRule;
        m_Header.IsReadOnly = Target.IsReadOnly;
        m_Header.SetBinding(StyleRuleHeader.RuleProperty, new DataBinding
        {
            dataSource = Target,
            dataSourcePath = StyleRuleSelection.StyleRuleProperty,
            bindingMode = BindingMode.ToTarget
        });
        m_Header.SetBinding(StyleRuleHeader.IsReadOnlyProperty, new DataBinding
        {
            dataSource = Target,
            dataSourcePath = StyleRuleSelection.IsReadOnlyProperty,
            bindingMode = BindingMode.ToTarget
        });
        return m_Header;
    }

    public override VisualElement CreateInspectorGUI()
    {
        m_Inspector = new StyleRuleInspector { StyleRule = Target.StyleRule, IsReadOnly = Target.IsReadOnly };
        m_Inspector.SetBinding(StyleRuleInspector.StyleRuleProperty, new DataBinding
        {
            dataSource = Target,
            dataSourcePath = StyleRuleSelection.StyleRuleProperty,
            bindingMode = BindingMode.ToTarget
        });
        m_Inspector.SetBinding(StyleRuleInspector.IsReadOnlyProperty, new DataBinding
        {
            dataSource = Target,
            dataSourcePath = StyleRuleSelection.IsReadOnlyProperty,
            bindingMode = BindingMode.ToTarget
        });
        m_Inspector.InitializeSearchField(m_Header.SearchField);
        ApplyState();
        return m_Inspector;
    }
}
