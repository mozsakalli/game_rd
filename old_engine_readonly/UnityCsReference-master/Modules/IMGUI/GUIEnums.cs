// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    ///<summary>Scaling mode to draw textures with.</summary>
    public enum ScaleMode
    {
        ///<summary>Stretches the texture to fill the complete rectangle passed in to GUI.DrawTexture.</summary>
        StretchToFill = 0,
        ///<summary>Scales the texture, maintaining aspect ratio, so it completely covers the <c>position</c> rectangle passed to GUI.DrawTexture. If the texture is being draw to a rectangle with a different aspect ratio than the original, the image is cropped.</summary>
        ScaleAndCrop = 1,
        ///<summary>Scales the texture, maintaining aspect ratio, so it completely fits withing the <c>position</c> rectangle passed to GUI.DrawTexture.</summary>
        ScaleToFit = 2
    }

    ///<summary>Used by GUIUtility.GetControlID to inform the IMGUI system if a given control can get keyboard focus. This allows the IMGUI system to give focus appropriately when a user presses tab for cycling between controls.</summary>
    public enum FocusType
    {
        [Obsolete("FocusType.Native now behaves the same as FocusType.Passive in all OS cases. (UnityUpgradable) -> Passive", false)]
        Native = 0,
        ///<summary>This control can receive keyboard focus.</summary>
        Keyboard = 1,
        ///<summary>This control can not receive keyboard focus.</summary>
        Passive = 2
    }
}
