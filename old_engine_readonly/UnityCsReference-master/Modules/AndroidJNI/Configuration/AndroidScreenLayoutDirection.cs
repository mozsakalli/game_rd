// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the screen layout direction.</summary>
    ///<seealso cref="AndroidConfiguration.screenLayoutDirection" />
    public enum AndroidScreenLayoutDirection : int
    {
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_LAYOUTDIR_LTR</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_LAYOUTDIR_LTR"&gt;SCREENLAYOUT_LAYOUTDIR_LTR&lt;/a&gt;.</remarks>
        LTR = 64,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_LAYOUTDIR_RTL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_LAYOUTDIR_RTL"&gt;SCREENLAYOUT_LAYOUTDIR_RTL&lt;/a&gt;.</remarks>
        RTL = 128
    }
}
