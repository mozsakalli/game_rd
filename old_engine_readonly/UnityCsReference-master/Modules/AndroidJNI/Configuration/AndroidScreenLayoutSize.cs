// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the size of the device screen.</summary>
    ///<seealso cref="AndroidConfiguration.screenLayoutSize" />
    public enum AndroidScreenLayoutSize : int
    {
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_SIZE_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_SIZE_UNDEFINED"&gt;SCREENLAYOUT_SIZE_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_SIZE_SMALL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_SIZE_SMALL"&gt;SCREENLAYOUT_SIZE_SMALL&lt;/a&gt;.</remarks>
        Small = 1,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_SIZE_NORMAL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_SIZE_NORMAL"&gt;SCREENLAYOUT_SIZE_NORMAL&lt;/a&gt;.</remarks>
        Normal = 2,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_SIZE_LARGE</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_SIZE_LARGE"&gt;SCREENLAYOUT_SIZE_LARGE&lt;/a&gt;.</remarks>
        Large = 3,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_SIZE_XLARGE</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_SIZE_XLARGE"&gt;SCREENLAYOUT_SIZE_XLARGE&lt;/a&gt;.</remarks>
        XLarge = 4
    }
}
