// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the aspect ratio of the screen is taller or wider than normal.</summary>
    ///<seealso cref="AndroidConfiguration.screenLayoutLong" />
    public enum AndroidScreenLayoutLong : int
    {
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_LONG_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_LONG_UNDEFINED"&gt;SCREENLAYOUT_LONG_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_LONG_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_LONG_NO"&gt;SCREENLAYOUT_LONG_NO&lt;/a&gt;.</remarks>
        No = 16,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_LONG_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_LONG_YES"&gt;SCREENLAYOUT_LONG_YES&lt;/a&gt;.</remarks>
        Yes = 32
    }
}
