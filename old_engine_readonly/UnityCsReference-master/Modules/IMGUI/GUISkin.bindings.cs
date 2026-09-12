// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;

namespace UnityEngine
{
    ///<summary>General settings for how the GUI behaves.</summary>
    ///<remarks>These are shared by all elements in a <see cref="GUISkin" />.</remarks>
    [NativeHeader("Modules/IMGUI/GUISkin.bindings.h")]
    public partial class GUISettings
    {
        private static extern float Internal_GetCursorFlashSpeed();
    }
}
