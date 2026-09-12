// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>Base class for authoring data on a grid with grid painting tools like paint, erase, pick, select and fill.</summary>
    ///<remarks>Inheriting this class and/or creating brush asset instances from your inherited class, you can create custom brushes which react to high level grid events like paint, erase, pick, select and fill.</remarks>
    ///<example>
    ///  <code><![CDATA[
    ///using UnityEngine;
    ///
    /// // Paints two Prefabs in checkerboard pattern
    ///[CreateAssetMenu]
    ///public class CheckerboardBrush : GridBrushBase
    ///{
    ///    public GameObject prefabA;
    ///    public GameObject prefabB;
    ///
    ///    public override void Paint(GridLayout grid, GameObject brushTarget, Vector3Int position)
    ///    {
    ///        bool evenCell = Mathf.Abs(position.y + position.x) % 2 > 0;
    ///        GameObject toBeInstantiated = evenCell ? prefabA : prefabB;
    ///
    ///        if (toBeInstantiated != null)
    ///        {
    ///            GameObject newInstance = Instantiate(toBeInstantiated, grid.CellToWorld(position), Quaternion.identity);
    ///            newInstance.transform.SetParent(brushTarget.transform);
    ///        }
    ///    }
    ///}
    ///]]></code>
    ///</example>
    public abstract class GridBrushBase : ScriptableObject
    {
        ///<summary>Options for the Tool mode of the <see cref="GridBrushBase" />.</summary>
        public enum Tool { ///<summary>Identifies a Select Tool.</summary>
///<remarks>Tool for selecting an area from a grid.
///
///See also <see cref="GridBrushBase.Select" />.</remarks>
Select, ///<summary>Identifies a Move Tool.</summary>
///<remarks>Tool for moving a selected area from a grid.
///
///See also <see cref="GridBrushBase.MoveStart" />, <see cref="GridBrushBase.Move" />, <see cref="GridBrushBase.MoveEnd" />.</remarks>
Move, ///<summary>Identifies a Paint Tool.</summary>
///<remarks>Tool for painting cells with the brush.
///
///See also <see cref="GridBrushBase.Paint" />.</remarks>
Paint, ///<summary>Identifies a Box Tool.</summary>
///<remarks>Tool for filling an area with the brush.
///
///See also <see cref="GridBrushBase.BoxFill" />.</remarks>
Box, ///<summary>Identifies a Pick Tool.</summary>
///<remarks>Tool for picking an area from a grid.
///
///See also <see cref="GridBrushBase.Pick" />.</remarks>
Pick, ///<summary>Identifies an Erase Tool.</summary>
///<remarks>Tool for erasing a single cell with the brush.
///
///See also <see cref="GridBrushBase.Erase" />.</remarks>
Erase, ///<summary>Identifies a Flood Fill Tool.</summary>
///<remarks>Tool for flood filling logically connected cells with a brush.
///
///See also <see cref="GridBrushBase.FloodFill" />.</remarks>
FloodFill, ///<summary>Identifies a Custom Tool.</summary>
///<remarks>Scriptable Tool with customizable behavior.</remarks>
Other }
        ///<summary>Rotate tiles in the <see cref="GridBrushBase" /> in this direction.</summary>
        public enum RotationDirection { ///<summary>Rotates tiles clockwise.</summary>
Clockwise = 0, ///<summary>Rotates tiles counter-clockwise.</summary>
CounterClockwise = 1 }
        ///<summary>Flip tiles in the <see cref="GridBrushBase" /> along this axis.</summary>
        public enum FlipAxis { ///<summary>Flip the brush in the X Axis.</summary>
X = 0, ///<summary>Flip the brush in the Y Axis.</summary>
Y = 1 }

        ///<summary>Paints data into a grid within the given bounds.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the paint operation. By default the currently selected GameObject.</param>
        ///<param name="position">The coordinates of the cell to paint data to.</param>
        public virtual void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) {}
        ///<summary>Erases data on a grid within the given bounds.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the erase operation. By default the currently selected GameObject.</param>
        ///<param name="position">The coordinates of the cell to erase data from.</param>
        public virtual void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) {}

        ///<summary>Box fills tiles and GameObjects into given bounds within the selected layers.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of box fill operation. By default the currently selected GameObject.</param>
        ///<param name="position">The bounds to box fill data to.</param>
        public virtual void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            for (int z = position.zMin; z < position.zMax; z++)
            {
                for (int y = position.yMin; y < position.yMax; y++)
                {
                    for (int x = position.xMin; x < position.xMax; x++)
                    {
                        Paint(gridLayout, brushTarget, new Vector3Int(x, y, z));
                    }
                }
            }
        }

        ///<summary>Erases data on a grid within the given bounds.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the erase operation. By default the currently selected GameObject.</param>
        ///<param name="position">The bounds to erase data from.</param>
        public virtual void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            for (int z = position.zMin; z < position.zMax; z++)
            {
                for (int y = position.yMin; y < position.yMax; y++)
                {
                    for (int x = position.xMin; x < position.xMax; x++)
                    {
                        Erase(gridLayout, brushTarget, new Vector3Int(x, y, z));
                    }
                }
            }
        }

        ///<summary>Select an area of a grid.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Targets of paint operation. By default the currently selected GameObject.</param>
        ///<param name="position">Area to get selected.</param>
        public virtual void Select(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) {}
        ///<summary>Flood fills data onto a grid given the starting coordinates of the cell.</summary>
        ///<remarks>Flood fill all the cells that are logically connected with the starting position.</remarks>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Targets of flood fill operation. By default the currently selected GameObject.</param>
        ///<param name="position">Starting position of the flood fill.</param>
        public virtual void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position) {}
        ///<summary>Rotates all tiles on the grid brush with the given <see cref="RotationDirection" />.</summary>
        ///<param name="direction">Direction to rotate by.</param>
        ///<param name="layout">CellLayout for rotating.</param>
        public virtual void Rotate(RotationDirection direction, GridLayout.CellLayout layout) {}
        ///<summary>Flips the grid brush in the given <see cref="FlipAxis" />.</summary>
        ///<param name="flip">Axis to flip by.</param>
        ///<param name="layout">CellLayout for flipping.</param>
        public virtual void Flip(FlipAxis flip, GridLayout.CellLayout layout) {}
        ///<summary>Picks data from a grid given the coordinates of the cells.</summary>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the paint operation. By default the currently selected GameObject.</param>
        ///<param name="position">The coordinates of the cells to paint data from.</param>
        ///<param name="pivot">Pivot of the picking brush.</param>
        public virtual void Pick(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, Vector3Int pivot) {}
        ///<summary>Move is called when user moves the area previously selected with the selection marquee.</summary>
        ///<remarks>See also: <see cref="GridBrushBase.MoveStart" />, <see cref="GridBrushBase.MoveEnd" /> and <see cref="GridBrushBase.Select" />.</remarks>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the move operation. By default the currently selected GameObject.</param>
        ///<param name="from">Source bounds of the move.</param>
        ///<param name="to">Target bounds of the move.</param>
        public virtual void Move(GridLayout gridLayout, GameObject brushTarget, BoundsInt from, BoundsInt to) {}
        ///<summary>MoveEnd is called when user starts moving the area previously selected with the selection marquee.</summary>
        ///<remarks>See also: <see cref="GridBrushBase.Move" />, <see cref="GridBrushBase.MoveEnd" /> and <see cref="GridBrushBase.Select" />.</remarks>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        ///<param name="brushTarget">Target of the move operation. By default the currently selected GameObject.</param>
        ///<param name="position">Position where the move operation has started.</param>
        public virtual void MoveStart(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) {}
        ///<summary>MoveEnd is called when user has ended the move of the area previously selected with the selection marquee.</summary>
        ///<remarks>See also: <see cref="GridBrushBase.MoveStart" />, <see cref="GridBrushBase.Move" /> and <see cref="GridBrushBase.Select" />.</remarks>
        ///<param name="position">Layers affected by the move operation.</param>
        ///<param name="brushTarget">Target of the move operation. By default the currently selected GameObject.</param>
        ///<param name="gridLayout">
        ///  <see cref="Grid" /> used for layout.</param>
        public virtual void MoveEnd(GridLayout gridLayout, GameObject brushTarget, BoundsInt position) {}

        ///<summary>Changes the Z position of the <see cref="GridBrushBase" />.</summary>
        ///<param name="change">Modify the Z position of <see cref="GridBrushBase" /> by this value.</param>
        public virtual void ChangeZPosition(int change) {}
        ///<summary>Resets Z position changes of the <see cref="GridBrushBase" />.</summary>
        public virtual void ResetZPosition() {}
    }
}
