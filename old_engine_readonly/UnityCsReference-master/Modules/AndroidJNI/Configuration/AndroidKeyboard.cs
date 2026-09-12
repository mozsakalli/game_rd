// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the type of keyboard the device is using.</summary>
    ///<seealso cref="AndroidConfiguration.keyboard" />
    public enum AndroidKeyboard : int
    {
        ///<summary>Mirrors the Android property value <c>KEYBOARD_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARD_UNDEFINED"&gt;KEYBOARD_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>KEYBOARD_NOKEYS</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARD_NOKEYS"&gt;KEYBOARD_NOKEYS&lt;/a&gt;.</remarks>
        NoKeys = 1,
        ///<summary>Mirrors the Android property value <c>KEYBOARD_QWERTY</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARD_QWERTY"&gt;KEYBOARD_QWERTY&lt;/a&gt;.</remarks>
        Qwerty = 2,
        ///<summary>Mirrors the Android property value <c>KEYBOARD_12KEY</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#KEYBOARD_12KEY"&gt;KEYBOARD_12KEY&lt;/a&gt;.</remarks>
        _12Key = 3
    }
}
