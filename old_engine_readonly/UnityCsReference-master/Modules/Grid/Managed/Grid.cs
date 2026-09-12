// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>Grid is the base class for plotting a layout of uniformly spaced points and lines.</summary>
    ///<remarks>The Grid component stores dimensional data of the layout of the grid and provides helper functions to retrieve information about the grid, such as the conversion between the cell location and local space location of items within the grid.
    ///
    ///The layout of the Grid component is in the XY plane with the origin of the grid always beginning at (0, 0) and the X and Y coordinates of the grid only as positive values.
    ///
    ///Implements the interface <see cref="GridLayout" />.</remarks>
    public partial class Grid
    {
        ///<summary>Get the logical center coordinate of a grid cell in local space.</summary>
        ///<remarks>In a rectangular grid layout, a call to <see cref="GridLayout.CellToLocal" /> with <see cref="Vector3Int" /> parameter, returns a <see cref="Vector3" /> coordinate that represents the bottom-left of the cell. This is mathematically correct, but when for example instantiating a GameObject into the grid, you often prefer the center of the cell instead.</remarks>
        ///<param name="position">Grid cell position.</param>
        ///<returns>Center of the cell transformed into local space coordinates.</returns>
        ///<example>
        ///  <code><![CDATA[
        /// // Snap the GameObject to parent Grid center of cell
        ///using UnityEngine;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        Grid grid = transform.parent.GetComponent<Grid>();
        ///        Vector3Int cellPosition = grid.LocalToCell(transform.localPosition);
        ///        transform.localPosition = grid.GetCellCenterLocal(cellPosition);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public Vector3 GetCellCenterLocal(Vector3Int position) 
        { 
            Vector3 cs = cellSize;
            Vector3 ics = inverseCellStride;
            Vector3 localCellCenter = GetLayoutCellCenter();
            Vector3 relativeCellCenter = new Vector3(localCellCenter.x * cs.x * ics.x, localCellCenter.y * cs.y * ics.y, localCellCenter.z * cs.z * ics.z);
            return CellToLocalInterpolated(position) + CellToLocalInterpolated(relativeCellCenter); 
        }
        ///<summary>Get the logical center coordinate of a grid cell in world space.</summary>
        ///<remarks>In a rectangular grid layout, a call to <see cref="GridLayout.CellToWorld" /> with <see cref="Vector3Int" /> parameter, returns a <see cref="Vector3" /> coordinate that represents the bottom-left of the cell. This is mathematically correct, but when for example instantiating a GameObject into the grid, you often prefer the center of the cell instead.</remarks>
        ///<param name="position">Grid cell position.</param>
        ///<returns>Center of the cell transformed into world space coordinates.</returns>
        ///<example>
        ///  <code><![CDATA[
        /// // Snap the GameObject to parent Grid center of cell
        ///using UnityEngine;
        ///
        ///public class ExampleClass : MonoBehaviour
        ///{
        ///    void Start()
        ///    {
        ///        Grid grid = transform.parent.GetComponent<Grid>();
        ///        Vector3Int cellPosition = grid.WorldToCell(transform.position);
        ///        transform.position = grid.GetCellCenterWorld(cellPosition);
        ///    }
        ///}
        ///]]></code>
        ///</example>
        public Vector3 GetCellCenterWorld(Vector3Int position) { return LocalToWorld(GetCellCenterLocal(position)); }
    }
}
