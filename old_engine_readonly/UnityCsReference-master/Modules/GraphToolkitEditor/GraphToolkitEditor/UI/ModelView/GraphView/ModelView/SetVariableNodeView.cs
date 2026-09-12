// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License


namespace Unity.GraphToolkit.Editor
{
    [UnityRestricted]
    class SetVariableNodeView : CollapsibleInOutNodeView
    {
        const string k_TypeIconStylesheet = "TypeIcons.uss";

        VariableNodeModel VariableNodeModel => Model as VariableNodeModel;
        ChildView m_LastUsedMainPort;
        NodeTitlePart m_NodeTitlePart;
        NodeColorLinePart m_NodeColorLinePart;

        /// <inheritdoc/>
        public override bool HasModelDependenciesChanged() => Model is VariableNodeModel;

        /// <inheritdoc/>
        public override void AddModelDependencies()
        {
            if (Model is VariableNodeModel variableNodeModel)
                Dependencies.AddModelDependency(variableNodeModel.VariableDeclarationModel);
        }

        /// <inheritdoc />
        protected override void PostBuildUI()
        {
            base.PostBuildUI();
            m_NodeTitlePart = PartList.GetPart(titleIconContainerPartName) as NodeTitlePart;
            m_NodeTitlePart?.Root.AddPackageStylesheet(k_TypeIconStylesheet);

            m_NodeColorLinePart = PartList.GetPart(topColorLineContainerPartName) as NodeColorLinePart;
        }

        /// <inheritdoc />
        public override void UpdateUIFromModel(UpdateFromModelVisitor visitor)
        {
            base.UpdateUIFromModel(visitor);

            Border.Highlighted = ShouldBeHighlighted();
            SetIconAndColor();
        }

        /// <inheritdoc/>
        public override bool HasBackwardsDependenciesChanged()
        {
            if (NodeModel is not VariableNodeModel variableNode)
                return false;

            var mainPort = variableNode.InputPort ?? variableNode.OutputPort;
            return m_LastUsedMainPort != mainPort?.GetView(RootView);
        }

        /// <inheritdoc/>
        public override void AddBackwardDependencies()
        {
            base.AddBackwardDependencies();

            if (NodeModel is not VariableNodeModel variableNode)
                return;

            var mainPortModel = variableNode.InputPort ?? variableNode.OutputPort;
            var mainPort = mainPortModel?.GetView(RootView);
            if (mainPort != null)
            {
                Dependencies.AddBackwardDependency(mainPort, DependencyTypes.Style);
                m_LastUsedMainPort = mainPort;
            }
        }

        /// <inheritdoc />
        public override void ActivateRename()
        {
            // The only moment that a variable node is renamable is when creating it from a port
            // AND it is only renamable from its declaration on the BB, not on the node itself
            if (NodeModel is VariableNodeModel variableNode && GraphView.Window is GraphViewEditorWindow window)
            {
                var variableDeclarationModel = variableNode.VariableDeclarationModel;
                if (variableDeclarationModel != null)
                {
                    var variableDeclarationField = variableDeclarationModel.GetView<BlackboardField>(window.BlackboardView, BlackboardCreationContext.VariableCreationContext);
                    variableDeclarationField?.ActivateRename();
                }
            }
        }

        void SetIconAndColor()
        {
            // Set the icon and color of the node using USS classes based on the variable type first.
            if (m_NodeTitlePart != null)
            {
                RootView.TypeHandleInfos.AddUssClasses(GraphElementHelper.iconDataTypeClassPrefix, m_NodeTitlePart.Icon, VariableNodeModel.DataType);
            }

            if (m_NodeColorLinePart != null)
            {
                m_NodeColorLinePart.OverrideColor();
                m_NodeColorLinePart.Root.AddToClassList(GraphElementHelper.colorLineDatatTypeClassPrefix + RootView.TypeHandleInfos.GetUssName(VariableNodeModel.DataType));
            }

            // If the type has a specific data type style, override the icon and color with it.
            var overrideIcon = true;
            var resolvedType = VariableNodeModel.DataType.Resolve();
            var typeStyle = GraphElementModel.GraphModel.GetDataTypeStyle(resolvedType);

            if (!typeStyle.HasValue)
            {
                typeStyle = GraphElementModel.GraphModel.GetDataTypeStyle(resolvedType.GetCollectionElementType());
                overrideIcon = false;
            }
            if (typeStyle.HasValue)
            {
                m_NodeColorLinePart?.OverrideColor(typeStyle.Value.color);

                var icon = m_NodeTitlePart?.Icon;
                if (icon != null)
                {
                    icon.tintColor = typeStyle.Value.color;
                    if (typeStyle.Value.icon != null && overrideIcon)
                        icon.image = typeStyle.Value.icon;
                }
            }
        }
    }
}
