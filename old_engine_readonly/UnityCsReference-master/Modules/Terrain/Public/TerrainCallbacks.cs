// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Scripting;
using UnityEngine.Scripting.APIUpdating;
using Unity.Scripting.LifecycleManagement;

namespace UnityEngine
{
    ///<summary>This static class provides events that Unity triggers when Terrain data changes.</summary>
    [MovedFrom("UnityEngine.Experimental.TerrainAPI")]
    public static partial class TerrainCallbacks
    {
        ///<summary>Use this delegate type with <see cref="heightmapChanged" /> to monitor all changes to the Terrain heightmap.</summary>
        ///<param name="terrain">The Terrain object that references a changed TerrainData asset.</param>
        ///<param name="heightRegion">The heightmap region that changed, in samples.</param>
        ///<param name="synched">Indicates whether the changes were fully synchronized back to CPU memory.</param>
        public delegate void HeightmapChangedCallback(Terrain terrain, RectInt heightRegion, bool synched);
        ///<summary>Use this delegate type with <see cref="textureChanged" /> to monitor all the changes to Terrain textures.</summary>
        ///<param name="terrain">The Terrain object that references a changed TerrainData asset.</param>
        ///<param name="textureName">The name of the texture that changed.</param>
        ///<param name="texelRegion">The region of the Terrain texture that changed, in texel coordinates.</param>
        ///<param name="synched">Indicates whether the changes were fully synchronized back to CPU memory.</param>
        public delegate void TextureChangedCallback(Terrain terrain, string textureName, RectInt texelRegion, bool synched);

        ///<summary>This event is triggered after there are changes to Terrain height data.</summary>
        ///<remarks>The <c>synched</c> parameter indicates whether the changes were fully synchronized back to CPU memory.</remarks>
        ///<seealso cref="HeightmapChangedCallback" />
        [AutoStaticsCleanupOnCodeReload]
        public static event HeightmapChangedCallback heightmapChanged;
        ///<summary>This event is triggered after there are changes to Terrain textures.</summary>
        ///<remarks>The <c>synched</c> parameter indicates whether the changes were fully synchronized back to CPU memory.</remarks>
        ///<seealso cref="TextureChangedCallback" />
        [AutoStaticsCleanupOnCodeReload]
        public static event TextureChangedCallback textureChanged;

        [RequiredByNativeCode]
        internal static void InvokeHeightmapChangedCallback(TerrainData terrainData, RectInt heightRegion, bool synched)
        {
            if (heightmapChanged != null)
            {
                foreach (var user in terrainData.users)
                    heightmapChanged.Invoke(user, heightRegion, synched);
            }
        }

        [RequiredByNativeCode]
        internal static void InvokeTextureChangedCallback(TerrainData terrainData, string textureName, RectInt texelRegion, bool synched)
        {
            if (textureChanged != null)
            {
                foreach (var user in terrainData.users)
                    textureChanged.Invoke(user, textureName, texelRegion, synched);
            }
        }
    }
}
