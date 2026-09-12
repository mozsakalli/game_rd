// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the screen shape is rounded or not.</summary>
    ///<seealso cref="AndroidConfiguration.screenLayoutRound" />
    public enum AndroidScreenLayoutRound : int
    {
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_ROUND_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_ROUND_UNDEFINED"&gt;SCREENLAYOUT_ROUND_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_ROUND_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_ROUND_NO"&gt;SCREENLAYOUT_ROUND_NO&lt;/a&gt;.</remarks>
        No = 256,
        ///<summary>Mirrors the Android property value <c>SCREENLAYOUT_ROUND_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#SCREENLAYOUT_ROUND_YES"&gt;SCREENLAYOUT_ROUND_YES&lt;/a&gt;.</remarks>
        Yes = 512
    }
}
