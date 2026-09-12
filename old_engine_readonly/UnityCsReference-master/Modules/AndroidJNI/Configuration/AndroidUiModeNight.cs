// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the device screen is in a special mode, such as a night mode.</summary>
    ///<seealso cref="AndroidConfiguration.uiModeNight" />
    public enum AndroidUIModeNight : int
    {
        ///<summary>Mirrors the Android property value <c>UI_MODE_NIGHT_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_NIGHT_UNDEFINED"&gt;UI_MODE_NIGHT_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>UI_MODE_NIGHT_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_NIGHT_NO"&gt;UI_MODE_NIGHT_NO&lt;/a&gt;.</remarks>
        No = 16,
        ///<summary>Mirrors the Android property value <c>UI_MODE_NIGHT_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_NIGHT_YES"&gt;UI_MODE_NIGHT_YES&lt;/a&gt;.</remarks>
        Yes = 32
    }
}
