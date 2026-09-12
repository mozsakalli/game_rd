// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the device supports touchscreen.</summary>
    ///<seealso cref="AndroidConfiguration.touchScreen" />
    public enum AndroidTouchScreen : int
    {
        ///<summary>Mirrors the Android property value <c>TOUCHSCREEN_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#TOUCHSCREEN_UNDEFINED"&gt;TOUCHSCREEN_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>TOUCHSCREEN_NOTOUCH</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#TOUCHSCREEN_NOTOUCH"&gt;TOUCHSCREEN_NOTOUCH&lt;/a&gt;.</remarks>
        NoTouch = 1,
        ///<summary>Mirrors the Android property value <c>TOUCHSCREEN_FINGER</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#TOUCHSCREEN_FINGER"&gt;TOUCHSCREEN_FINGER&lt;/a&gt;.</remarks>
        Finger = 3
    }
}
