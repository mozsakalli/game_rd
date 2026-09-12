// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the type of navigation methods used on the device.</summary>
    ///<seealso cref="AndroidConfiguration.navigation" />
    public enum AndroidNavigation : int
    {
        ///<summary>Mirrors the Android property value <c>NAVIGATION_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATION_UNDEFINED"&gt;NAVIGATION_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>NAVIGATION_NONAV</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATION_NONAV"&gt;NAVIGATION_NONAV&lt;/a&gt;.</remarks>
        NoNav = 1,
        ///<summary>Mirrors the Android property value <c>NAVIGATION_DPAD</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATION_DPAD"&gt;NAVIGATION_DPAD&lt;/a&gt;.</remarks>
        Dpad = 2,
        ///<summary>Mirrors the Android property value <c>NAVIGATION_TRACKBALL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATION_TRACKBALL"&gt;NAVIGATION_TRACKBALL&lt;/a&gt;.</remarks>
        TrackBall = 3,
        ///<summary>Mirrors the Android property value <c>NAVIGATION_WHEEL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATION_WHEEL"&gt;NAVIGATION_WHEEL&lt;/a&gt;.</remarks>
        Wheel = 4,
    }
}
