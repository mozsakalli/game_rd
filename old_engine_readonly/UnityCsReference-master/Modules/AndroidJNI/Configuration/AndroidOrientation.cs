// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the orientation of the screen.</summary>
    ///<seealso cref="AndroidConfiguration.orientation" />
    public enum AndroidOrientation : int
    {
        ///<summary>Mirrors the Android property value <c>ORIENTATION_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#ORIENTATION_UNDEFINED"&gt;ORIENTATION_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>ORIENTATION_PORTRAIT</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#ORIENTATION_PORTRAIT"&gt;ORIENTATION_PORTRAIT&lt;/a&gt;.</remarks>
        Portrait = 1,
        ///<summary>Mirrors the Android property value <c>ORIENTATION_LANDSCAPE</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#ORIENTATION_LANDSCAPE"&gt;ORIENTATION_LANDSCAPE&lt;/a&gt;.</remarks>
        Landscape = 2
    }
}
