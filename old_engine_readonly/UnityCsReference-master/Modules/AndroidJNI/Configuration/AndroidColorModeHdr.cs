// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the screen can display a wide range brightness levels.</summary>
    ///<seealso cref="AndroidConfiguration.colorModeHdr" />
    public enum AndroidColorModeHdr : int
    {
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_HDR_UNDEFINED</c></summary>
        ///<remarks>For information about the the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_HDR_UNDEFINED"&gt;COLOR_MODE_HDR_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_HDR_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_HDR_NO"&gt;COLOR_MODE_HDR_NO&lt;/a&gt;.</remarks>
        No = 4,
        ///<summary>Mirrors the Android property value <c>COLOR_MODE_HDR_YES</c></summary>
        ///<remarks>For information about the the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#COLOR_MODE_HDR_YES"&gt;COLOR_MODE_HDR_YES&lt;/a&gt;.</remarks>
        Yes = 8,
    }
}
