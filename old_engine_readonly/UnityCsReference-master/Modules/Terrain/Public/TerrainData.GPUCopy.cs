// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using CopyTextureSupport = UnityEngine.Rendering.CopyTextureSupport;
using GraphicsDeviceType = UnityEngine.Rendering.GraphicsDeviceType;
using UnityEngine;

namespace UnityEngine
{
    public partial class TerrainData
    {
        private static bool SupportsCopyTextureBetweenRTAndTexture
        {
            get
            {
                const CopyTextureSupport kRT2TexAndTex2RT = CopyTextureSupport.RTToTexture | CopyTextureSupport.TextureToRT;
                return (SystemInfo.copyTextureSupport & kRT2TexAndTex2RT) == kRT2TexAndTex2RT;
            }
        }

        ///<summary>Copies the specified part of the active <see cref="RenderTexture" /> to the Terrain heightmap texture.</summary>
        ///<remarks>This functions calls <see cref="DirtyHeightmapRegion" /> internally and sends out the OnTerrainChanged message accordingly.
        ///                
        ///               The range of expected height values for the active <see cref="RenderTexture" /> is between <c>0</c> and <c>0.5</c>. This is unlike <see cref="TerrainData.SetHeights" />, which expects height values between <c>0</c> and <c>1</c>.</remarks>
        ///<param name="sourceRect">The part of the active Render Texture to copy.</param>
        ///<param name="dest">The X and Y coordinates of the heightmap texture to copy into.</param>
        ///<param name="syncControl">Controls how CPU synchronization is performed.</param>
        ///<seealso cref="TerrainHeightmapSyncControl" />
        ///<seealso cref="DirtyHeightmapRegion" />
        ///<seealso cref="SyncHeightmap" />
        public void CopyActiveRenderTextureToHeightmap(RectInt sourceRect, Vector2Int dest, TerrainHeightmapSyncControl syncControl)
        {
            var source = RenderTexture.active;
            if (source == null)
                throw new InvalidOperationException("Active RenderTexture is null.");

            if (sourceRect.x < 0 || sourceRect.y < 0 || sourceRect.xMax > source.width || sourceRect.yMax > source.height)
                throw new ArgumentOutOfRangeException("sourceRect");
            else if (dest.x < 0 || dest.x + sourceRect.width > heightmapResolution)
                throw new ArgumentOutOfRangeException("dest.x");
            else if (dest.y < 0 || dest.y + sourceRect.height > heightmapResolution)
                throw new ArgumentOutOfRangeException("dest.y");

            Internal_CopyActiveRenderTextureToHeightmap(sourceRect, dest.x, dest.y, syncControl);
            TerrainCallbacks.InvokeHeightmapChangedCallback(this, new RectInt(dest.x, dest.y, sourceRect.width, sourceRect.height), syncControl == TerrainHeightmapSyncControl.HeightAndLod);
        }

        ///<summary>Marks the specified part of the heightmap as dirty.</summary>
        ///<remarks>Use this function only after you manually change the GPU part of the heightmap texture by rendering into it, or by using <see cref="Graphics.CopyTexture" />. Use the <c>syncControl</c> parameter to control how you want Unity to perform CPU synchronization. Unity queues the reading back of unsynchronized data (height data, LOD data, or both) until the next call to <see cref="SyncHeightmap" />.
        ///
        ///If the current active <see cref="RenderTexture" /> contains your changes, and you want to copy a part of it into the heightmap texture, use <see cref="CopyActiveRenderTextureToHeightmap" /> instead.
        ///
        ///This function sends out the OnTerrainChanged message with <see cref="TerrainChangedFlags.Heightmap" /> if you pass <see cref="TerrainHeightmapSyncControl.HeightAndLod" /> to the <c>syncControl</c> parameter. If you pass <see cref="TerrainHeightmapSyncControl.HeightOnly" /> to the <c>syncControl</c> parameter, it sends out the OnTerrainChanged message with <see cref="TerrainChangedFlags.DelayedHeightmapUpdate" />.</remarks>
        ///<param name="region">The rectangular region to mark as dirty.</param>
        ///<param name="syncControl">Controls how CPU synchronization is performed.</param>
        public void DirtyHeightmapRegion(RectInt region, TerrainHeightmapSyncControl syncControl)
        {
            int resolution = heightmapResolution;
            if (region.x < 0 || region.x >= resolution)
                throw new ArgumentOutOfRangeException("region.x");
            else if (region.width <= 0 || region.xMax > resolution)
                throw new ArgumentOutOfRangeException("region.width");
            if (region.y < 0 || region.y >= resolution)
                throw new ArgumentOutOfRangeException("region.y");
            else if (region.height <= 0 || region.yMax > resolution)
                throw new ArgumentOutOfRangeException("region.height");

            Internal_DirtyHeightmapRegion(region.x, region.y, region.width, region.height, syncControl);
            TerrainCallbacks.InvokeHeightmapChangedCallback(this, region, syncControl == TerrainHeightmapSyncControl.HeightAndLod);
        }

        ///<summary>The name for the Terrain alpha map textures.</summary>
        ///<remarks>Use this name when you call <see cref="CopyActiveRenderTextureToTexture" />, <see cref="DirtyTextureRegion" />, <see cref="SyncTexture" />, or <see cref="TerrainCallbacks.textureChanged" /> to identify the Terrain alpha map textures.</remarks>
        public static string AlphamapTextureName => "alphamap";
        ///<summary>The name for the Terrain holes Texture.</summary>
        ///<remarks>Use this name when you call <see cref="CopyActiveRenderTextureToTexture" />, <see cref="DirtyTextureRegion" />, <see cref="SyncTexture" />, or <see cref="TerrainCallbacks.textureChanged" /> to identify the Terrain holes Texture.</remarks>
        public static string HolesTextureName => "holes";

        ///<summary>Copies the specified part of the active <see cref="RenderTexture" /> to the Terrain texture.</summary>
        ///<remarks>If the <c>allowDelayedCPUSync</c> parameter is set to <c>true</c>, and the platform supports copying between a <see cref="RenderTexture" /> and a <see cref="Texture2D" />, Unity performs a GPU copy from the active RenderTexture to the Terrain texture. This is sufficient for Terrain rendering, but you will need to call <see cref="SyncTexture" /> afterward to synchronize the CPU part of the texture.
        ///
        ///If the <c>allowDelayedCPUSync</c> parameter is set to <c>false</c>, or the platform doesn't support copying between textures, Unity immediately reads back the content of the active RenderTexture, and updates both the CPU and GPU parts of the Terrain texture.
        ///
        ///Unity recommends you create the source Render Texture to copy in the format that <see cref="Terrain.heightmapRenderTextureFormat" /> specifies, and call the HLSL function <c>PackHeightmap</c> before you write to the source render texture. To use <c>PackHeightmap</c>, make sure you have the include directive <c>#include "UnityCG.cginc"</c> in your shader.</remarks>
        ///<param name="textureName">The name of the Terrain texture to copy into.</param>
        ///<param name="textureIndex">The index of the Terrain texture to copy into.</param>
        ///<param name="sourceRect">The part of the active Render Texture to copy.</param>
        ///<param name="dest">The X and Y coordinates of the Terrain texture to copy into.</param>
        ///<param name="allowDelayedCPUSync">Specifies whether to allow delayed CPU synchronization of the texture.</param>
        ///<seealso cref="DirtyTextureRegion" />
        ///<seealso cref="SyncTexture" />
        public void CopyActiveRenderTextureToTexture(string textureName, int textureIndex, RectInt sourceRect, Vector2Int dest, bool allowDelayedCPUSync)
        {
            if (String.IsNullOrEmpty(textureName))
                throw new ArgumentNullException("textureName");

            var source = RenderTexture.active;
            if (source == null)
                throw new InvalidOperationException("Active RenderTexture is null.");

            int textureWidth = 0;
            int textureHeight = 0;

            if (textureName == HolesTextureName)
            {
                if (textureIndex != 0)
                    throw new ArgumentOutOfRangeException("textureIndex");
                else if (source == holesTexture)
                    throw new ArgumentException("source", "Active RenderTexture cannot be holesTexture.");
                textureWidth = textureHeight = holesResolution;
            }
            else if (textureName == AlphamapTextureName)
            {
                if (textureIndex < 0 || textureIndex >= alphamapTextureCount)
                    throw new ArgumentOutOfRangeException("textureIndex");
                textureWidth = textureHeight = alphamapResolution;
            }
            else
            {
                // TODO: Support generic terrain textures.
                throw new ArgumentException($"Unrecognized terrain texture name: \"{textureName}\"");
            }

            if (sourceRect.x < 0 || sourceRect.y < 0 || sourceRect.xMax > source.width || sourceRect.yMax > source.height)
                throw new ArgumentOutOfRangeException("sourceRect");
            else if (dest.x < 0 || dest.x + sourceRect.width > textureWidth)
                throw new ArgumentOutOfRangeException("dest.x");
            else if (dest.y < 0 || dest.y + sourceRect.height > textureHeight)
                throw new ArgumentOutOfRangeException("dest.y");

            if (textureName == HolesTextureName)
            {
                Internal_CopyActiveRenderTextureToHoles(sourceRect, dest.x, dest.y, allowDelayedCPUSync);
                return;
            }

            var dstTexture = GetAlphamapTexture(textureIndex);

            // Delay synching back (using ReadPixels) if CopyTexture can be used.
            // TODO: Checking the format compatibility is difficult as it varies by platforms. For instance copying between ARGB32 RT and RGBA32 Tex seems to be fine on all tested platforms...
            // If the user has a global mipmap limit, use readpixels, as the missing mips prevent us from using Graphics.CopyTexture.
            allowDelayedCPUSync = allowDelayedCPUSync && SupportsCopyTextureBetweenRTAndTexture && QualitySettings.globalTextureMipmapLimit == 0;
            if (allowDelayedCPUSync)
            {
                if (dstTexture.mipmapCount > 1)
                {
                    // Composes mip0 in a RT with full mipchain.
                    var tmp = RenderTexture.GetTemporary(new RenderTextureDescriptor(dstTexture.width, dstTexture.height, source.graphicsFormat, source.depthStencilFormat)
                    {
                        sRGB = false,
                        useMipMap = true,
                        autoGenerateMips = false
                    });
                    if (!tmp.IsCreated())
                        tmp.Create();

                    Graphics.CopyTexture(dstTexture, 0, 0, tmp, 0, 0);
                    Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceRect.y, sourceRect.width, sourceRect.height, tmp, 0, 0, dest.x, dest.y);

                    // Generate the mips on the GPU
                    tmp.GenerateMips();

                    // Copy the full mipchain back to the alphamap texture
                    Graphics.CopyTexture(tmp, dstTexture);

                    RenderTexture.ReleaseTemporary(tmp);
                }
                else
                {
                    Graphics.CopyTexture(source, 0, 0, sourceRect.x, sourceRect.y, sourceRect.width, sourceRect.height, dstTexture, 0, 0, dest.x, dest.y);
                }

                // TODO: Support generic terrain textures.
                Internal_MarkAlphamapDirtyRegion(textureIndex, dest.x, dest.y, sourceRect.width, sourceRect.height);
            }
            else
            {
                dstTexture.ReadPixels(new Rect(sourceRect.x, sourceRect.y, sourceRect.width, sourceRect.height), dest.x, dest.y);
                dstTexture.Apply(true);

                // TODO: Check if the texture is previously marked dirty?
                // TODO: Support generic terrain textures.
                Internal_ClearAlphamapDirtyRegion(textureIndex);
            }

            TerrainCallbacks.InvokeTextureChangedCallback(this, textureName, new RectInt(dest.x, dest.y, sourceRect.width, sourceRect.height), !allowDelayedCPUSync);
        }

        ///<summary>Marks the specified part of the Terrain texture as dirty.</summary>
        ///<remarks>Use this function only after you manually change the GPU part of the Terrain texture, such as by using <see cref="Graphics.CopyTexture" />. Set the <c>allowDelayedCPUSync</c> parameter to <c>true</c> if you want Unity to perform immediate synchronization of the CPU part. If you set it to <c>false</c>, Unity queues the reading back of the dirty region until the next call to <see cref="SyncTexture" />.
        ///
        ///If the current active <see cref="RenderTexture" /> contains your changes, and you want to copy a part of it into the Terrain texture, use <see cref="CopyActiveRenderTextureToTexture" /> instead.</remarks>
        ///<param name="textureName">The name of the Terrain texture.</param>
        ///<param name="region">The rectangular region to mark as dirty.</param>
        ///<param name="allowDelayedCPUSync">Specifies whether to allow delayed CPU synchronization of the texture.</param>
        public void DirtyTextureRegion(string textureName, RectInt region, bool allowDelayedCPUSync)
        {
            if (String.IsNullOrEmpty(textureName))
                throw new ArgumentNullException("textureName");

            int resolution = 0;

            if (textureName == AlphamapTextureName)
            {
                resolution = alphamapResolution;
            }
            else if (textureName == HolesTextureName)
            {
                resolution = holesResolution;
            }
            else
            {
                // TODO: Support generic terrain textures.
                throw new ArgumentException($"Unrecognized terrain texture name: \"{textureName}\"");
            }

            if (region.x < 0 || region.x >= resolution)
                throw new ArgumentOutOfRangeException("region.x");
            else if (region.width <= 0 || region.xMax > resolution)
                throw new ArgumentOutOfRangeException("region.width");
            if (region.y < 0 || region.y >= resolution)
                throw new ArgumentOutOfRangeException("region.y");
            else if (region.height <= 0 || region.yMax > resolution)
                throw new ArgumentOutOfRangeException("region.height");

            if (textureName == HolesTextureName)
            {
                Internal_DirtyHolesRegion(region.x, region.y, region.width, region.height, allowDelayedCPUSync);
                return;
            }

            // TODO: Support generic terrain textures.
            Internal_MarkAlphamapDirtyRegion(-1, region.x, region.y, region.width, region.height);

            if (!allowDelayedCPUSync)
                SyncTexture(textureName);
            else
                TerrainCallbacks.InvokeTextureChangedCallback(this, textureName, region, false);
        }

        ///<summary>Performs synchronization queued by previous calls to <see cref="CopyActiveRenderTextureToTexture" /> and <see cref="DirtyTextureRegion" />, which makes CPU data of the Terrain textures up to date.</summary>
        ///<param name="textureName">The name of the Terrain texture to synchronize.</param>
        public void SyncTexture(string textureName)
        {
            if (String.IsNullOrEmpty(textureName))
                throw new ArgumentNullException("textureName");

            // For now the textureName should always equal to "alphamap".
            if (textureName == AlphamapTextureName)
                Internal_SyncAlphamaps();
            else if (textureName == HolesTextureName)
            {
                if (IsHolesTextureCompressed())
                    throw new InvalidOperationException("Holes texture is compressed. Compressed holes texture can not be read back from GPU. Use TerrainData.enableHolesTextureCompression to disable holes texture compression.");

                Internal_SyncHoles();
            }
            else
            {
                // TODO: Support generic terrain textures.
                throw new ArgumentException($"Unrecognized terrain texture name: \"{textureName}\"");
            }
        }
    }
}
