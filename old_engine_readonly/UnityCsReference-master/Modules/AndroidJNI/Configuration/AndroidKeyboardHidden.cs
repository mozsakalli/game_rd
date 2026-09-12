// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate whether any keyboard is available for use on the device.</summary>
    ///<seealso cref="AndroidConfiguration.keyboardHidden" />
    public enum AndroidKeyboardHidden : int
    {
        ///<summary>Mirrors the Android property value <c>KEYBOARDHIDDEN_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARDHIDDEN_UNDEFINED"&gt;KEYBOARDHIDDEN_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>KEYBOARDHIDDEN_NO</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARDHIDDEN_NO"&gt;KEYBOARDHIDDEN_NO&lt;/a&gt;.</remarks>
        No = 1,
        ///<summary>Mirrors the Android property value <c>KEYBOARDHIDDEN_YES</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARDHIDDEN_YES"&gt;KEYBOARDHIDDEN_YES&lt;/a&gt;.</remarks>
        Yes = 2
    }
}
