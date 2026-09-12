// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.ProjectAuditor.Editor.UI.Framework;
using UnityEditor;
using UnityEngine;

namespace Unity.ProjectAuditor.Editor.UI
{
    internal class CodeDomainReloadView : CodeDiagnosticView
    {
        public CodeDomainReloadView(ViewManager viewManager) : base(viewManager)
        {
        }

        public override void DrawFilters()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(SharedContents.Show, GUILayout.ExpandWidth(true), ProjectAuditorWindow.LayoutSize.FilterOptionsLabelWidth);

                EditorGUI.BeginChangeCheck();
                m_Table.showIgnoredIssues = EditorGUILayout.ToggleLeft(SharedContents.ShowIgnoredIssues, m_Table.showIgnoredIssues, GUILayout.Width(170));
                if (EditorGUI.EndChangeCheck())
                {
                    m_ViewManager.OnIgnoredIssuesVisibilityChanged?.Invoke(m_Table.showIgnoredIssues);
                    MarkDirty();
                }
            }
        }
    }
}
