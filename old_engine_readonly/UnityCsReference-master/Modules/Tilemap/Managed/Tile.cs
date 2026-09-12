// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;

namespace UnityEngine.Tilemaps
{
    ///<summary>Class for a default tile in the <see cref="Tilemap" />.</summary>
    ///<remarks>This inherits from <see cref="TileBase" /> and represents a default tile to be placed in a <see cref="Tilemap" />. It implements <see cref="TileBase.GetTileData" /> for simple rendering of a <see cref="Sprite" /> in the tile map.</remarks>
    [Serializable]
    [RequiredByNativeCode]
    [HelpURL("https://docs.unity3d.com/Manual/Tilemap-TileAsset.html")]
    public class Tile : TileBase
    {
        ///<summary>Enum for determining what collider shape is generated for this <see cref="Tile" /> by the <see cref="TilemapCollider2D" />.</summary>
        public enum ColliderType { ///<summary>No collider shape is generated for the <see cref="Tile" /> by the <see cref="TilemapCollider2D" />.</summary>
None = 0, ///<summary>The Sprite outline is used as the collider shape for the <see cref="Tile" /> by the <see cref="TilemapCollider2D" />.</summary>
Sprite = 1, ///<summary>The grid layout boundary outline is used as the collider shape for the <see cref="Tile" /> by the <see cref="TilemapCollider2D" />.</summary>
Grid = 2 }

        ///<summary>
        ///  <see cref="Sprite" /> to be rendered at the <see cref="Tile" />.</summary>
        public Sprite sprite
        {
            get { return m_Sprite; }
            set
            {
                m_Sprite = value;
                m_SpriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None;
            }
        }
        ///<summary>
        ///  <see cref="Color" /> of the <see cref="Tile" />.</summary>
        public Color color { get { return m_Color; } set { m_Color = value; } }
        ///<summary>
        ///  <see cref="Matrix4x4">Transform matrix</see> of the <see cref="Tile" />.</summary>
        public Matrix4x4 transform { get { return m_Transform; } set { m_Transform = value; } }
        ///<summary>
        ///  <see cref="GameObject" /> of the <see cref="Tile" />.</summary>
        ///<remarks>This <see cref="GameObject" /> will be instantiated at the start of the Scene at the <see cref="Tile" />.</remarks>
        public GameObject gameObject
        {
            get { return m_InstancedGameObject; }
            set
            {
                m_InstancedGameObject = value;
                m_InstancedGameObjectEntityId = m_InstancedGameObject != null ? m_InstancedGameObject.GetEntityId() : EntityId.None;
            }
        }
        ///<summary>
        ///  <see cref="TileFlags" /> of the <see cref="Tile" />.</summary>
        public TileFlags flags { get { return m_Flags; } set { m_Flags = value; } }
        ///<exclude />
        public ColliderType colliderType { get { return m_ColliderType; } set { m_ColliderType = value; } }

        [SerializeField]
        private Sprite m_Sprite;
        [SerializeField]
        private Color m_Color = Color.white;
        [SerializeField]
        private Matrix4x4 m_Transform = Matrix4x4.identity;
        [SerializeField]
        private GameObject m_InstancedGameObject;
        [SerializeField]
        private TileFlags m_Flags = TileFlags.LockColor;
        [SerializeField]
        private ColliderType m_ColliderType = ColliderType.Sprite;
        private EntityId m_SpriteEntityId;
        private EntityId m_InstancedGameObjectEntityId;

        ///<remarks>Unity calls this method when the Tile is created.</remarks>
        ///<seealso cref="M:UnityEngine.ScriptableObject.OnEnable" />
        public override void OnEnable()
        {
            base.OnEnable();

            m_SpriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None;
            m_InstancedGameObjectEntityId = m_InstancedGameObject != null ? m_InstancedGameObject.GetEntityId() : EntityId.None;
        }

        ///<remarks>Unity calls this method when the Tile is loaded or when a value is changed in the Inspector.</remarks>
        ///<seealso cref="M:UnityEngine.ScriptableObject.OnValidate" />
        public virtual void OnValidate()
        {
            m_SpriteEntityId = m_Sprite != null ? m_Sprite.GetEntityId() : EntityId.None;
            m_InstancedGameObjectEntityId = m_InstancedGameObject != null ? m_InstancedGameObject.GetEntityId() : EntityId.None;
        }

        ///<summary>Retrieves the tile rendering data for the <see cref="Tile" />.</summary>
        ///<param name="position">Position of the Tile on the <see cref="Tilemap" />.</param>
        ///<param name="tilemap">The <see cref="Tilemap" /> the tile is present on.</param>
        ///<param name="tileData">Data to render the tile. This is filled with <see cref="Tile" />, <see cref="Tile.color" /> and <see cref="Tile.transform" />.</param>
        ///<returns>Whether the call was successful. This returns true for <see cref="Tile" />.</returns>
        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            tileData.spriteEntityId = m_SpriteEntityId;
            tileData.color = m_Color;
            tileData.transform = m_Transform;
            tileData.gameObjectEntityId = m_InstancedGameObjectEntityId;
            tileData.flags = m_Flags;
            tileData.colliderType = m_ColliderType;
        }
    }
}
