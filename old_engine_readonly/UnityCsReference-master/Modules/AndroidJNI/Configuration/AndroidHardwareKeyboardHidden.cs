// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether the physical keyboard is available.</summary>
    ///<seealso cref="AndroidConfiguration.hardKeyboardHidden" />
    public enum AndroidHardwareKeyboardHidden : int
    {
        ///<summary>Mirrors the Android property value <c>HARDKEYBOARDHIDDEN_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#HARDKEYBOARDHIDDEN_UNDEFINED"&gt;HARDKEYBOARDHIDDEN_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>HARDKEYBOARDHIDDEN_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#HARDKEYBOARDHIDDEN_NO"&gt;HARDKEYBOARDHIDDEN_NO&lt;/a&gt;.</remarks>
        No = 1,
        ///<summary>Mirrors the Android property value <c>HARDKEYBOARDHIDDEN_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#HARDKEYBOARDHIDDEN_YES"&gt;HARDKEYBOARDHIDDEN_YES&lt;/a&gt;.</remarks>
        Yes = 2
    }
}
