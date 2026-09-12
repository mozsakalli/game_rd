// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Rendering;

namespace UnityEngine
{
    public partial class Terrain
    {
        ///<summary>The type of the material used to render a terrain object. Could be one of the built-in types or custom.</summary>
        [Obsolete("Enum type MaterialType is not used any more.", false)]
        public enum MaterialType
        {
            ///<summary>A built-in material that uses the standard physically-based lighting model. Inputs supported: smoothness, metallic / specular, normal.</summary>
            ///<remarks>The actual built-in shader used is Nature/Terrain/Standard.</remarks>
            BuiltInStandard = 0,
            ///<summary>A built-in material that uses the legacy Lambert (diffuse) lighting model and has optional normal map support.</summary>
            ///<remarks>The actual built-in shader used is Nature/Terrain/Diffuse.</remarks>
            BuiltInLegacyDiffuse,
            ///<summary>A built-in material that uses the legacy BlinnPhong (specular) lighting model and has optional normal map support.</summary>
            ///<remarks>The actual built-in shader used is Nature/Terrain/Specular.</remarks>
            BuiltInLegacySpecular,
            ///<summary>Use a custom material given by <see cref="Terrain.materialTemplate" />.</summary>
            Custom
        }

        ///<exclude />
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("splatmapDistance is deprecated, please use basemapDistance instead. (UnityUpgradable) -> basemapDistance", true)]
        public float splatmapDistance
        {
            get { return basemapDistance; }
            set { basemapDistance = value; }
        }

        ///<summary>Should terrain cast shadows?.</summary>
        ///<remarks>::ref::castShadows is obsolete. Instead you should use <see cref="shadowCastingMode" /> which allows setting all shadow casting modes. Setting castShadows to true sets shadowCastingMode to <see cref="Rendering.ShadowCastingMode.TwoSided" />. Setting it to false sets shadowCastingMode to <see cref="Rendering.ShadowCastingMode.Off" />.</remarks>
        [Obsolete("castShadows is deprecated, please use shadowCastingMode instead.")]
        public bool castShadows
        {
            get { return shadowCastingMode != ShadowCastingMode.Off; }
            set { shadowCastingMode = value ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off; }
        }

        ///<summary>The type of the material used to render the terrain. Could be one of the built-in types or custom. See <see cref="Terrain.MaterialType" />.</summary>
        ///<remarks>If you want to use a custom material, set this to <see cref="Terrain.MaterialType.Custom" />, then assign a material to <see cref="Terrain.materialTemplate" />. <see cref="Terrain.MaterialType.Custom" /> with <see cref="Terrain.materialTemplate" /> == null is identical to <see cref="Terrain.MaterialType.BuiltInLegacyDiffuse" />.</remarks>
        [Obsolete("Property materialType is not used any more. Set materialTemplate directly.", false)]
        public MaterialType materialType
        {
            get { return MaterialType.Custom; }
            set {}
        }

        ///<summary>The specular color of the terrain.</summary>
        ///<remarks>You can use it to control the overall specular color across the whole terrain when <see cref="Terrain.materialType" /> is <see cref="Terrain.MaterialType.BuiltInLegacySpecular" />.</remarks>
        [Obsolete("Property legacySpecular is not used any more. Set materialTemplate directly.", false)]
        public Color legacySpecular
        {
            get { return Color.gray; }
            set {}
        }

        ///<summary>The shininess value of the terrain.</summary>
        ///<remarks>You can use it to control the overall shininess value across the whole terrain when <see cref="Terrain.materialType" /> is <see cref="Terrain.MaterialType.BuiltInLegacySpecular" />.
        ///
        ///
        ///The valid range of this value is 0.0f to 1.0f.</remarks>
        [Obsolete("Property legacyShininess is not used any more. Set materialTemplate directly.", false)]
        public float legacyShininess
        {
            get { return 0.078125f; }
            set {}
        }

        ///<summary>Update the terrain's LOD and vegetation information after making changes with <see cref="TerrainData.SetHeightsDelayLOD" />.</summary>
        [Obsolete("Use TerrainData.SyncHeightmap to notify all Terrain instances using the TerrainData.", false)]
        public void ApplyDelayedHeightmapModification()
        {
            terrainData?.SyncHeightmap();
        }
    }
}
