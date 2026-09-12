// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the 5-way or DPAD navigation methods are available on the device.</summary>
    ///<seealso cref="AndroidConfiguration.navigationHidden" />
    public enum AndroidNavigationHidden : int
    {
        ///<summary>Mirrors the Android property value <c>NAVIGATIONHIDDEN_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATIONHIDDEN_UNDEFINED"&gt;NAVIGATIONHIDDEN_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>NAVIGATIONHIDDEN_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATIONHIDDEN_NO"&gt;NAVIGATIONHIDDEN_NO&lt;/a&gt;.</remarks>
        No = 1,
        ///<summary>Mirrors the Android property value <c>NAVIGATIONHIDDEN_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#NAVIGATIONHIDDEN_YES"&gt;NAVIGATIONHIDDEN_YES&lt;/a&gt;.</remarks>
        Yes = 2
    }
}
