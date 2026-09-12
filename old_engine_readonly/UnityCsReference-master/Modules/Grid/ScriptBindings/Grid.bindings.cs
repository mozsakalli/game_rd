// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;

namespace UnityEngine
{
    [RequireComponent(typeof(Transform))]
    [NativeHeader("Modules/Grid/Public/GridMarshalling.h")]
    [NativeHeader("Modules/Grid/Public/Grid.h")]
    [NativeClass("Grid", PersistentTypeId = 0x094D1FCA)]
    public sealed partial class Grid : GridLayout
    {
        ///<summary>The size of each cell in the <see cref="Grid" />.</summary>
        public new extern Vector3 cellSize
        {
            [FreeFunction("GridBindings::GetCellSize", HasExplicitThis = true)]
            get;
            [FreeFunction("GridBindings::SetCellSize", HasExplicitThis = true)]
            set;
        }

        ///<summary>The size of the gap between each cell in the <see cref="Grid" />.</summary>
        public new extern Vector3 cellGap
        {
            [FreeFunction("GridBindings::GetCellGap", HasExplicitThis = true)]
            get;
            [FreeFunction("GridBindings::SetCellGap", HasExplicitThis = true)]
            set;
        }

        ///<summary>Cell shape and packing that the grid uses when converting cell positions to local space.</summary>
        ///<remarks>See <see cref="GridLayout.CellLayout" /> for the available layouts, which include rectangle, hexagon, and isometric shapes.</remarks>
        public new extern GridLayout.CellLayout cellLayout
        {
            get;
            set;
        }

        ///<summary>Cell swizzle order that the grid applies when converting cell positions to local space.</summary>
        ///<remarks>Swizzling reorders the cell axes. The default <see cref="GridLayout.CellSwizzle.XYZ" /> keeps cell X, Y, and Z mapped to local X, Y, and Z. <see cref="GridLayout.CellSwizzle.XZY" /> swaps the Y and Z axes, which is useful for placing a 2D grid on the XZ ground plane of a 3D scene. See <see cref="GridLayout.CellSwizzle" /> for all available orders.</remarks>
        public new extern GridLayout.CellSwizzle cellSwizzle
        {
            get;
            set;
        }

        internal extern Vector3 inverseCellStride
        {
            [FreeFunction("GridBindings::GetInverseCellStride", HasExplicitThis = true)]
            get;
        }

        ///<summary>Swizzles the given position with the given swizzle order.</summary>
        ///<param name="swizzle">Determines the rearrangement order for the swizzle.</param>
        ///<param name="position">Position to swizzle.</param>
        ///<returns>The swizzled position.</returns>
        ///<seealso cref="GridLayout.CellSwizzle" />
        [FreeFunction("GridBindings::CellSwizzle")]
        public extern static Vector3 Swizzle(GridLayout.CellSwizzle swizzle, Vector3 position);

        ///<summary>Does the inverse swizzle of the given position for given swizzle order.</summary>
        ///<param name="swizzle">Determines the rearrangement order for the inverse swizzle.</param>
        ///<param name="position">Position to inverse swizzle.</param>
        ///<returns>The inversed swizzled position.</returns>
        ///<seealso cref="GridLayout.CellSwizzle" />
        [FreeFunction("GridBindings::InverseCellSwizzle")]
        public extern static Vector3 InverseSwizzle(GridLayout.CellSwizzle swizzle, Vector3 position);
    }
}
