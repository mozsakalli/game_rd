// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;

namespace UnityEditor.Build.Content
{
    // A lightweight handle onto one node in a BuildArtifactMetadataCollection. It holds only the
    // owning collection and the node id; every property read calls back into the collection. It is
    // valid only while that collection is alive (reads throw ObjectDisposedException otherwise).
    internal readonly struct BuildArtifactMetadata
    {
        private readonly BuildArtifactMetadataCollection m_Collection;
        private readonly BuildArtifactMetadataId m_Id;

        internal BuildArtifactMetadata(BuildArtifactMetadataCollection collection, BuildArtifactMetadataId id)
        {
            m_Collection = collection;
            m_Id = id;
        }

        public BuildArtifactMetadataId Id => m_Id;
        public string Category => m_Collection.GetCategory(m_Id);
        public Hash128 ArtifactCAH => m_Collection.GetArtifactCAH(m_Id);
        public ulong ArtifactSize => m_Collection.GetArtifactSize(m_Id);
        public BuildArtifactMetadataId[] References => m_Collection.GetReferences(m_Id);
        public UCBPKeyValuePair[] KeyValuePairs => m_Collection.GetKeyValuePairs(m_Id);

        // UDS hash of this node's type-specific metadata (e.g. the FileWriteMetaData of a content file).
        // Resolve the payload via the matching internal API, such as
        // UnifiedBuildPipelineInternalApi.FileWriteMetaDataToJson.
        public bool HasTypeSpecificMetadata => m_Collection.HasTypeSpecificMetadata(m_Id);
        public Hash128 TypeSpecificMetadata => m_Collection.GetTypeSpecificMetadata(m_Id);
    }
}
