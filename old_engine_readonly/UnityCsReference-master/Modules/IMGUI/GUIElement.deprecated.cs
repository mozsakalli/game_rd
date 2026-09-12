// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    ///<summary>Base class for images &amp; text strings displayed in a GUI.</summary>
    ///<remarks>This class holds the base functionality for any GUI elements.</remarks>
    [ExcludeFromPreset]
    [ExcludeFromObjectFactory]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    [Obsolete("GUIElement has been removed. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead.", true)]
    public sealed class GUIElement
    {
        static void FeatureRemoved() { throw new Exception("GUIElement has been removed from Unity. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead."); }

        ///<summary>Is a point on screen inside the element?</summary>
        ///<remarks>Returns true if the <c>screenPosition</c> is contained in this GUIElement. <c>screenPosition</c> is specified in
        ///screen coordinates, like the values returned by the <see cref="Input.mousePosition" /> property.
        ///If no <c>camera</c> is given a camera filling the entire game window will be assumed.
        ///
        ///Note that if the position is inside the element, <c>true</c> will be returned even if
        ///the game object belongs to Ignore Raycast layer (normally mouse events are not sent
        ///to Ignore Raycast objects).</remarks>
        ///<seealso cref="GUILayer.HitTest" />
        [Obsolete("GUIElement has been removed. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead.", true)]
        public bool HitTest(Vector3 screenPosition)
        {
            FeatureRemoved();
            return false;
        }

        ///<summary>Is a point on screen inside the element?</summary>
        ///<remarks>Returns true if the <c>screenPosition</c> is contained in this GUIElement. <c>screenPosition</c> is specified in
        ///screen coordinates, like the values returned by the <see cref="Input.mousePosition" /> property.
        ///If no <c>camera</c> is given a camera filling the entire game window will be assumed.
        ///
        ///Note that if the position is inside the element, <c>true</c> will be returned even if
        ///the game object belongs to Ignore Raycast layer (normally mouse events are not sent
        ///to Ignore Raycast objects).</remarks>
        ///<seealso cref="GUILayer.HitTest" />
        [Obsolete("GUIElement has been removed. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead.", true)]
        public bool HitTest(Vector3 screenPosition, [Internal.DefaultValue("null")] Camera camera)
        {
            FeatureRemoved();
            return false;
        }

        ///<summary>Returns bounding rectangle of <see cref="GUIElement" /> in screen coordinates.</summary>
        ///<remarks>If no <c>camera</c> is given a camera filling the entire game window will be assumed.</remarks>
        [Obsolete("GUIElement has been removed. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead.", true)]
        public Rect GetScreenRect([Internal.DefaultValue("null")] Camera camera)
        {
            FeatureRemoved();
            return new Rect(0, 0, 0, 0);
        }

        ///<summary>Returns bounding rectangle of <see cref="GUIElement" /> in screen coordinates.</summary>
        ///<remarks>If no <c>camera</c> is given a camera filling the entire game window will be assumed.</remarks>
        [Obsolete("GUIElement has been removed. Consider using https://docs.unity3d.com/ScriptReference/UIElements.Image.html, https://docs.unity3d.com/ScriptReference/UIElements.TextElement.html or TextMeshPro instead.", true)]
        public Rect GetScreenRect()
        {
            FeatureRemoved();
            return new Rect(0, 0, 0, 0);
        }
    }
}
