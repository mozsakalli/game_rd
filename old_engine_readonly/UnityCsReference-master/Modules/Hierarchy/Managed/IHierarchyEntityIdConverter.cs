// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.ComponentModel;
using UnityEngine;

namespace Unity.Hierarchy
{
    public static partial class HierarchyExtensions
    {
        #region Marked as obsolete error in 6.7
        [Obsolete("GetNode is obsolete, use Hierarchy.GetNodeFromEntityId instead.", true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static HierarchyNode GetNode(this Hierarchy hierarchy, EntityId entityId) => throw null;

        [Obsolete("GetNodes is obsolete, use Hierarchy.GetNodesFromEntityIds instead.", true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void GetNodes(this Hierarchy hierarchy, ReadOnlySpan<EntityId> entityIds, Span<HierarchyNode> outNodes) => throw null;

        [Obsolete("GetEntityId is obsolete, use Hierarchy.GetEntityIdFromNode instead.", true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static EntityId GetEntityId(this Hierarchy hierarchy, in HierarchyNode node) => throw null;

        [Obsolete("GetEntityIds is obsolete, use Hierarchy.GetEntityIdsFromNodes instead.", true)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static void GetEntityIds(this Hierarchy hierarchy, ReadOnlySpan<HierarchyNode> nodes, Span<EntityId> outEntityIds) => throw null;
        #endregion
    }
}
