// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.Android
{
    ///<summary>Options to indicate the user interface mode of the device.</summary>
    ///<seealso cref="AndroidConfiguration.uiModeType" />
    public enum AndroidUIModeType : int
    {
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_UNDEFINED</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_UNDEFINED"&gt;UI_MODE_TYPE_UNDEFINED&lt;/a&gt;.</remarks>
        Undefined = 0,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_NORMAL</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_NORMAL"&gt;UI_MODE_TYPE_NORMAL&lt;/a&gt;.</remarks>
        Normal = 1,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_DESK</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_DESK"&gt;UI_MODE_TYPE_DESK&lt;/a&gt;.</remarks>
        Desk = 2,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_CAR</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_CAR"&gt;UI_MODE_TYPE_CAR&lt;/a&gt;.</remarks>
        Car = 3,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_TELEVISION</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_TELEVISION"&gt;UI_MODE_TYPE_TELEVISION&lt;/a&gt;.</remarks>
        Television = 4,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_APPLIANCE</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_APPLIANCE"&gt;UI_MODE_TYPE_APPLIANCE&lt;/a&gt;.</remarks>
        Appliance = 5,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_WATCH</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_WATCH"&gt;UI_MODE_TYPE_WATCH&lt;/a&gt;.</remarks>
        Watch = 6,
        ///<summary>Mirrors the Android property value <c>UI_MODE_TYPE_VR_HEADSET</c>.</summary>
        ///<remarks>For information about the property value, refer to the Android developer documentation on &lt;a href="https://developer.android.com/reference/android/content/res/Configuration#UI_MODE_TYPE_VR_HEADSET"&gt;UI_MODE_TYPE_VR_HEADSET&lt;/a&gt;.</remarks>
        VrHeadset = 7
    }
}
