// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the screen can display wide range of color gamut or not.</summary>
    ///<seealso cref="AndroidConfiguration.colorModeWideColorGamut" />
    public enum AndroidColorModeWideColorGamut : int
    {
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_WIDE_COLOR_GAMUT_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_WIDE_COLOR_GAMUT_UNDEFINED"&gt;COLOR_MODE_WIDE_COLOR_GAMUT_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_WIDE_COLOR_GAMUT_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_WIDE_COLOR_GAMUT_NO"&gt;COLOR_MODE_WIDE_COLOR_GAMUT_NO&lt;/a&gt;.</remarks>
        No = 1,
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_WIDE_COLOR_GAMUT_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_WIDE_COLOR_GAMUT_YES"&gt;COLOR_MODE_WIDE_COLOR_GAMUT_YES&lt;/a&gt;.</remarks>
        Yes = 2,
    }
}
