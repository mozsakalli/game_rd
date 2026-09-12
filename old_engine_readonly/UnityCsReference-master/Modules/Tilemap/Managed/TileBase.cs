// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;

namespace UnityEngine.Tilemaps
{
    ///<summary>Base class for a tile in the <see cref="Tilemap" />.</summary>
    ///<remarks>Inherit from this to implement your custom tile to be placed in a <see cref="Tilemap" /> component.</remarks>
    [RequiredByNativeCode]
    public abstract class TileBase : ScriptableObject
    {
        private EntityId m_CachedEntityId;
        ///<summary>The cached EntityId of the <see cref="TileBase" />.</summary>
        ///<example>
        ///  <code><![CDATA[using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        ///public static class TilemapExample
        ///{
        ///    public static void TileCachedEntityIdExample(TileBase tile)
        ///    {
        ///        Debug.Log($"The cached EntityId for the Tile is equal to GetEntityId: ({(tile.cachedEntityId == tile.GetEntityId()).ToString()})");
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public EntityId cachedEntityId => m_CachedEntityId;

        ///<summary>This function is called when the <see cref="TileBase" /> is loaded. Override this to initialize any data for the <see cref="TileBase" />.</summary>
        ///<remarks>
        ///  <para>See <see cref="M:UnityEngine.ScriptableObject.OnEnable" />.</para>
        ///  <para>The example overrides <see cref="OnEnable" /> to initialize data for a scripted <see cref="TileBase" />.</para>
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[using Unity.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        ///public static class TilemapExample
        ///{
        ///    public class AnimatedEntityIdTile : Tile
        ///    {
        ///        public struct AnimatedEntityIdData
        ///        {
        ///            public TileData m_TileData;
        ///
        ///            public NativeArray<EntityId> m_AnimatedSpriteEntityIds;
        ///            public float m_Speed;
        ///            public float m_AnimationStartTime;
        ///            public TileAnimationFlags m_TileAnimationFlags;
        ///
        ///            public void Dispose()
        ///            {
        ///                if (m_AnimatedSpriteEntityIds.IsCreated)
        ///                    m_AnimatedSpriteEntityIds.Dispose();
        ///            }
        ///        }
        ///
        ///        private AnimatedEntityIdData m_AnimatedEntityIdData;
        ///        private Sprite[] m_AnimatedSprites;
        ///        private float m_Speed;
        ///        private float m_AnimationStartTime;
        ///        private TileAnimationFlags m_TileAnimationFlags;
        ///
        ///        public override void OnEnable()
        ///        {
        ///            base.OnEnable();
        ///
        ///            m_AnimatedEntityIdData.m_TileData = new TileData()
        ///            {
        ///                sprite = this.sprite,
        ///                color = this.color,
        ///                transform = this.transform,
        ///                gameObject = this.gameObject,
        ///                colliderType = this.colliderType,
        ///                flags = this.flags,
        ///            };
        ///            if (m_AnimatedSprites != null && m_AnimatedSprites.Length > 0)
        ///            {
        ///                m_AnimatedEntityIdData.m_AnimatedSpriteEntityIds = new NativeArray<EntityId>(m_AnimatedSprites.Length, Allocator.Persistent);
        ///                for (var i = 0; i < m_AnimatedSprites.Length; i++)
        ///                {
        ///                    m_AnimatedEntityIdData.m_AnimatedSpriteEntityIds[i] = m_AnimatedSprites[i] != null ? m_AnimatedSprites[i].GetEntityId() : EntityId.None;
        ///                }
        ///            }
        ///            m_AnimatedEntityIdData.m_Speed = this.m_Speed;
        ///            m_AnimatedEntityIdData.m_AnimationStartTime = this.m_AnimationStartTime;
        ///            m_AnimatedEntityIdData.m_TileAnimationFlags = this.m_TileAnimationFlags;
        ///        }
        ///
        ///        public override void OnDisable()
        ///        {
        ///            m_AnimatedEntityIdData.Dispose();
        ///
        ///            base.OnDisable();
        ///        }
        ///    }
        ///}]]></code>
        ///</example>
        public virtual void OnEnable()
        {
            m_CachedEntityId = GetEntityId();
        }
        ///<summary>This function is called when the <see cref="TileBase" /> goes out of scope. Override this to deinitialize any data for the <see cref="TileBase" />.</summary>
        ///<remarks>
        ///  <para>See <see cref="M:UnityEngine.ScriptableObject.OnDisable" />.</para>
        ///  <para>The example overrides <see cref="OnDisable" /> to initialize data for a scripted <see cref="TileBase" />.</para>
        ///</remarks>
        ///<example>
        ///  <code><![CDATA[using Unity.Collections;
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        ///public static class TilemapExample
        ///{
        ///    public class AnimatedEntityIdTile : Tile
        ///    {
        ///        public struct AnimatedEntityIdData
        ///        {
        ///            public TileData m_TileData;
        ///
        ///            public NativeArray<EntityId> m_AnimatedSpriteEntityIds;
        ///            public float m_Speed;
        ///            public float m_AnimationStartTime;
        ///            public TileAnimationFlags m_TileAnimationFlags;
        ///
        ///            public void Dispose()
        ///            {
        ///                if (m_AnimatedSpriteEntityIds.IsCreated)
        ///                    m_AnimatedSpriteEntityIds.Dispose();
        ///            }
        ///        }
        ///
        ///        private AnimatedEntityIdData m_AnimatedEntityIdData;
        ///        private Sprite[] m_AnimatedSprites;
        ///        private float m_Speed;
        ///        private float m_AnimationStartTime;
        ///        private TileAnimationFlags m_TileAnimationFlags;
        ///
        ///        public override void OnEnable()
        ///        {
        ///            base.OnEnable();
        ///
        ///            m_AnimatedEntityIdData.m_TileData = new TileData()
        ///            {
        ///                sprite = this.sprite,
        ///                color = this.color,
        ///                transform = this.transform,
        ///                gameObject = this.gameObject,
        ///                colliderType = this.colliderType,
        ///                flags = this.flags,
        ///            };
        ///            if (m_AnimatedSprites != null && m_AnimatedSprites.Length > 0)
        ///            {
        ///                m_AnimatedEntityIdData.m_AnimatedSpriteEntityIds = new NativeArray<EntityId>(m_AnimatedSprites.Length, Allocator.Persistent);
        ///                for (var i = 0; i < m_AnimatedSprites.Length; i++)
        ///                {
        ///                    m_AnimatedEntityIdData.m_AnimatedSpriteEntityIds[i] = m_AnimatedSprites[i] != null ? m_AnimatedSprites[i].GetEntityId() : EntityId.None;
        ///                }
        ///            }
        ///            m_AnimatedEntityIdData.m_Speed = this.m_Speed;
        ///            m_AnimatedEntityIdData.m_AnimationStartTime = this.m_AnimationStartTime;
        ///            m_AnimatedEntityIdData.m_TileAnimationFlags = this.m_TileAnimationFlags;
        ///        }
        ///
        ///        public override void OnDisable()
        ///        {
        ///            m_AnimatedEntityIdData.Dispose();
        ///
        ///            base.OnDisable();
        ///        }
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public virtual void OnDisable() { }

        ///<summary>This method is called when the tile is refreshed.</summary>
        ///<remarks>Implement this and call <see cref="Tilemap.RefreshTile" /> on all affected [tiles](xref:Tilemap-ScriptableTiles-TileBase) on the <see cref="Tilemap" /> including the tile at the given position to refresh them. This is also useful if the placement of a tile affects the properties of neighboring tiles.</remarks>
        ///<param name="position">Position of the Tile on the <see cref="Tilemap" />.</param>
        ///<param name="tilemap">The <see cref="Tilemap" /> the tile is present on.</param>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        /// // Tile that displays a Sprite when it is alone and a different Sprite when it is orthogonally adjacent to the same NeighourTile
        ///[CreateAssetMenu]
        ///public class NeighbourTile : TileBase
        ///{
        ///    public Sprite spriteA;
        ///    public Sprite spriteB;
        ///
        ///    public override void RefreshTile(Vector3Int position, ITilemap tilemap)
        ///    {
        ///        for (int yd = -1; yd <= 1; yd++)
        ///        {
        ///            Vector3Int location = new Vector3Int(position.x, position.y + yd, position.z);
        ///            if (IsNeighbour(location, tilemap))
        ///                tilemap.RefreshTile(location);
        ///        }
        ///        for (int xd = -1; xd <= 1; xd++)
        ///        {
        ///            Vector3Int location = new Vector3Int(position.x + xd, position.y, position.z);
        ///            if (IsNeighbour(location, tilemap))
        ///                tilemap.RefreshTile(location);
        ///        }
        ///    }
        ///
        ///    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        ///    {
        ///        tileData.sprite = spriteA;
        ///        for (int yd = -1; yd <= 1; yd += 2)
        ///        {
        ///            Vector3Int location = new Vector3Int(position.x, position.y + yd, position.z);
        ///            if (IsNeighbour(location, tilemap))
        ///                tileData.sprite = spriteB;
        ///        }
        ///        for (int xd = -1; xd <= 1; xd += 2)
        ///        {
        ///            Vector3Int location = new Vector3Int(position.x + xd, position.y, position.z);
        ///            if (IsNeighbour(location, tilemap))
        ///                tileData.sprite = spriteB;
        ///        }
        ///    }
        ///
        ///    private bool IsNeighbour(Vector3Int position, ITilemap tilemap)
        ///    {
        ///        TileBase tile = tilemap.GetTile(position);
        ///        return (tile != null && tile == this);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [RequiredByNativeCode]
        public virtual void RefreshTile(Vector3Int position, ITilemap tilemap) { tilemap.RefreshTile(position); }

        ///<summary>Retrieves any tile rendering data from the scripted tile.</summary>
        ///<remarks>Implement this and fill in the <see cref="TileData" /> to have the <see cref="Tilemap" /> to render the tile.</remarks>
        ///<param name="position">Position of the Tile on the <see cref="Tilemap" />.</param>
        ///<param name="tilemap">The <see cref="Tilemap" /> the tile is present on.</param>
        ///<param name="tileData">Data to render the tile.</param>
        ///<returns>Whether the call was successful.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        /// // Tile that repeats two sprites in checkerboard pattern
        ///[CreateAssetMenu]
        ///public class CheckerboardTile : TileBase
        ///{
        ///    public Sprite spriteA;
        ///    public Sprite spriteB;
        ///
        ///    public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        ///    {
        ///        bool evenCell = Mathf.Abs(position.y + position.x) % 2 > 0;
        ///        tileData.sprite = evenCell ? spriteA : spriteB;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [RequiredByNativeCode]
        public virtual void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData) {}
        private TileData GetTileDataNoRef(Vector3Int position, ITilemap tilemap)
        {
            TileData tileData = new TileData();
            GetTileData(position, tilemap, ref tileData);
            return tileData;
        }

        ///<summary>Retrieves any tile animation data from the scripted tile.</summary>
        ///<remarks>Implement this and fill in the <see cref="TileAnimationData" /> to have the <see cref="Tilemap" /> run an animation for the tile.</remarks>
        ///<param name="position">Position of the Tile on the <see cref="Tilemap" />.</param>
        ///<param name="tilemap">The <see cref="Tilemap" /> the tile is present on.</param>
        ///<param name="tileAnimationData">Data to run an animation on the tile.</param>
        ///<returns>Whether the call was successful.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        /// // Tile that plays an animated loops of sprites
        ///[CreateAssetMenu]
        ///public class AnimatedTile : TileBase
        ///{
        ///    public Sprite[] m_AnimatedSprites;
        ///    public float m_AnimationSpeed = 1f;
        ///    public float m_AnimationStartTime;
        ///
        ///    public override void GetTileData(Vector3Int location, ITilemap tileMap, ref TileData tileData)
        ///    {
        ///        if (m_AnimatedSprites != null && m_AnimatedSprites.Length > 0)
        ///        {
        ///            tileData.sprite = m_AnimatedSprites[m_AnimatedSprites.Length - 1];
        ///        }
        ///    }
        ///
        ///    public override bool GetTileAnimationData(Vector3Int location, ITilemap tileMap, ref TileAnimationData tileAnimationData)
        ///    {
        ///        if (m_AnimatedSprites != null && m_AnimatedSprites.Length > 0)
        ///        {
        ///            tileAnimationData.animatedSprites = m_AnimatedSprites;
        ///            tileAnimationData.animationSpeed = m_AnimationSpeed;
        ///            tileAnimationData.animationStartTime = m_AnimationStartTime;
        ///            return true;
        ///        }
        ///        return false;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public virtual bool GetTileAnimationData(Vector3Int position, ITilemap tilemap, ref TileAnimationData tileAnimationData) { return false; }

        [RequiredByNativeCode]
        private void GetTileAnimationDataRef(Vector3Int position, ITilemap tilemap, ref Sprite[] tileAnimationData_AnimatedSprites, ref float tileAnimationData_AnimationSpeed, ref float tileAnimationData_AnimationStartTime, ref int tileAnimationData_Flags, ref bool hasAnimation)
        {
            TileAnimationData tileAnimationData = new TileAnimationData
            {
                animatedSprites = tileAnimationData_AnimatedSprites,
                animationSpeed = tileAnimationData_AnimationSpeed,
                animationStartTime = tileAnimationData_AnimationStartTime,
                flags = (TileAnimationFlags)tileAnimationData_Flags
            };

            hasAnimation = GetTileAnimationData(position, tilemap, ref tileAnimationData);

            tileAnimationData_AnimatedSprites = tileAnimationData.animatedSprites;
            tileAnimationData_AnimationSpeed = tileAnimationData.animationSpeed;
            tileAnimationData_AnimationStartTime = tileAnimationData.animationStartTime;
            tileAnimationData_Flags = (int)tileAnimationData.flags;
        }

        ///<summary>StartUp is called on the first frame of the running Scene.</summary>
        ///<remarks>Use this to set values for the instantiated <see cref="GameObject" /> or run any logic at the beginning of the Scene.</remarks>
        ///<param name="position">Position of the Tile on the <see cref="Tilemap" />.</param>
        ///<param name="tilemap">The <see cref="Tilemap" /> the tile is present on.</param>
        ///<param name="go">The <see cref="GameObject" /> instantiated for the Tile.</param>
        ///<returns>Whether the call was successful.</returns>
        ///<example>
        ///  <code><![CDATA[
        ///using UnityEngine;
        ///using UnityEngine.Tilemaps;
        ///
        /// // Tile that instantiates a GameObject on Start and assigns a random rotation to the instanced GameObject
        ///[CreateAssetMenu]
        ///public class RandomRotationStartupTile : TileBase
        ///{
        ///    public Sprite m_Sprite;
        ///    public GameObject m_Prefab;
        ///
        ///    public override void GetTileData(Vector3Int location, ITilemap tilemap, ref TileData tileData)
        ///    {
        ///        tileData.sprite = m_Sprite;
        ///        tileData.gameObject = m_Prefab;
        ///    }
        ///
        ///    public override bool StartUp(Vector3Int location, ITilemap tilemap, GameObject go)
        ///    {
        ///        if (go != null)
        ///        {
        ///            go.transform.rotation = Random.rotation;
        ///        }
        ///        return true;
        ///    }
        ///}
        ///]]></code>
        ///</example>
        [RequiredByNativeCode]
        public virtual bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go) { return false; }

        [RequiredByNativeCode]
        private void StartUpRef(Vector3Int position, ITilemap tilemap, GameObject go, ref bool startUpInvokedByUser) { startUpInvokedByUser = StartUp(position, tilemap, go); }

    }
}
