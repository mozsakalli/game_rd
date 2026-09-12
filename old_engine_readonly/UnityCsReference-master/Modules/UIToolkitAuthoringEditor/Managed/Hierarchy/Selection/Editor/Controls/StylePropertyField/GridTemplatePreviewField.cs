// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System.Collections.Generic;
using Unity.Properties;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor
{
     [UxmlElement]
    internal partial class GridTemplatePreviewField : VisualElement
    {
        public static readonly BindingId columnsProperty = nameof(columns);
        public static readonly BindingId rowsProperty = nameof(rows);

        readonly GridTemplatePreview m_Preview;
        List<GridTrackSize> m_Columns = new();
        List<GridTrackSize> m_Rows = new();

        public GridTemplatePreviewField()
        {
            AddToClassList(ussClassName);
            style.marginLeft = 20;
            style.marginRight = 9;
            style.marginTop = 4;
            style.marginBottom = 4;

            var label = new Label("Preview");
            label.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            label.style.marginBottom = 2;
            Add(label);

            m_Preview = new GridTemplatePreview();
            Add(m_Preview);
        }

        [CreateProperty]
        public List<GridTrackSize> columns
        {
            get => m_Columns;
            set
            {
                m_Columns = value ?? new List<GridTrackSize>();
                m_Preview.SetColumns(m_Columns);
            }
        }

        [CreateProperty]
        public List<GridTrackSize> rows
        {
            get => m_Rows;
            set
            {
                m_Rows = value ?? new List<GridTrackSize>();
                m_Preview.SetRows(m_Rows);
            }
        }

        public static readonly string ussClassName = "grid-template-preview-field";
    }
}
