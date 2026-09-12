// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.UIElements;

namespace UnityEditor.Overlays
{
    class OverlayInsertIndicator : VisualElement
    {
        class InsertVisual : VisualElement
        {
            internal const string className = "unity-overlay-insert-visual";
            const string k_VerticalState = className + "--vertical";
            const string k_Horizontal = className + "--horizontal";
            const string k_VisualClass = className + "__renderer";

            readonly VisualElement m_Target;
            readonly VisualElement m_Renderer;
            bool m_Vertical;

            public InsertVisual(VisualElement target)
            {
                AddToClassList(className);
                m_Target = target;
                m_Target.RegisterCallback<GeometryChangedEvent>((evt) => UpdatePosition());

                Add(m_Renderer = new VisualElement());
                m_Renderer.AddToClassList(k_VisualClass);
                m_Renderer.pickingMode = PickingMode.Ignore;
            }

            public void SetVertical(bool vertical)
            {
                m_Vertical = vertical;
                EnableInClassList(k_VerticalState, vertical);
                EnableInClassList(k_Horizontal, !vertical);
            }

            void UpdatePosition()
            {
                if (parent == null)
                    return;

                var targetRect = parent.WorldToLocal(m_Target.worldBound);
                style.left = targetRect.position.x;
                style.top = targetRect.position.y;
                style.width = !m_Vertical ? targetRect.width : StyleKeyword.Null;
                style.height = m_Vertical ? targetRect.height : StyleKeyword.Null;
                style.display = DisplayStyle.Flex;
            }
        }

        public enum InsertIndicatorStyle
        {
            Normal,
            Toolbar,
            DynamicPanel
        }

        const string k_ClassName = "unity-overlay-insert-indicator";
        const string k_VerticalState = k_ClassName + "--vertical";
        const string k_Horizontal = k_ClassName + "--horizontal";
        const string k_InToolbarClass = InsertVisual.className + "--in-toolbar";
        const string k_InDynamicPanelClass = InsertVisual.className + "--in-dynamic-panel";
        const string k_BeforeFirstVisible = InsertVisual.className + "--before-first-visible";
        const string k_BeforeSpacer = InsertVisual.className + "--before-spacer";
        const string k_AfterSpacer = InsertVisual.className + "--after-spacer";

        readonly InsertVisual m_Visual;
        readonly VisualElement m_RenderOnTopParent;

        public OverlayInsertIndicator(VisualElement renderOnTopParent)
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(k_ClassName);

            m_RenderOnTopParent = renderOnTopParent;
            m_Visual = new InsertVisual(this);
            RegisterCallback<AttachToPanelEvent>(OnAttach);
            RegisterCallback<DetachFromPanelEvent>(OnDetach);
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            m_RenderOnTopParent.Add(m_Visual);
            m_Visual.style.display = DisplayStyle.None; // We don't want the visual to show before the size has been set
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            m_Visual.RemoveFromHierarchy();
        }

        public void Setup(bool vertical, InsertIndicatorStyle insertIndicatorStyle, bool beforeFirstVisible, bool beforeSpacer)
        {
            style.width = StyleKeyword.Null;
            style.height = StyleKeyword.Null;
            style.alignSelf = StyleKeyword.Null;
            EnableInClassList(k_VerticalState, vertical);
            EnableInClassList(k_Horizontal, !vertical);
            m_Visual.EnableInClassList(k_InToolbarClass, insertIndicatorStyle == InsertIndicatorStyle.Toolbar);
            m_Visual.EnableInClassList(k_InDynamicPanelClass, insertIndicatorStyle == InsertIndicatorStyle.DynamicPanel);
            m_Visual.EnableInClassList(k_BeforeFirstVisible, beforeFirstVisible);
            m_Visual.EnableInClassList(k_BeforeSpacer, beforeSpacer);
            m_Visual.EnableInClassList(k_AfterSpacer, !beforeSpacer);
            m_Visual.SetVertical(vertical);
        }
    }
}
