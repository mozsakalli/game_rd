// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;
using System;

namespace UnityEngine
{

    ///<summary>Provides support for off-screen touch input, such as a touchpad.</summary>
    [NativeHeader("Runtime/Input/GetInput.h")]
    public class AndroidInput
    {
        // Hide constructor
        private AndroidInput() {}

        ///<summary>Returns an object representing the status of a specific touch on a secondary touchpad (doesn't allocate temporary variables).</summary>
        public static Touch GetSecondaryTouch(int index)
        {
            return new Touch();
        }


        ///<summary>Number of secondary touches. Guaranteed not to change throughout the frame (RO).</summary>
        public static int touchCountSecondary
        {
            get { return GetTouchCount_Bindings(); }
        }

        [FreeFunction]
        [NativeConditional("PLATFORM_ANDROID")]
        internal static extern int GetTouchCount_Bindings();

        ///<summary>Indicates whether the system provides secondary touch input.</summary>
        public static bool secondaryTouchEnabled
        {
            get { return IsInputDeviceEnabled_Bindings(); }
        }

        [FreeFunction]
        [NativeConditional("PLATFORM_ANDROID")]
        internal static extern bool IsInputDeviceEnabled_Bindings();

        ///<summary>Indicates the width of the secondary touchpad.</summary>
        public static int secondaryTouchWidth
        {
            get { return GetTouchpadWidth(); }
        }

        [FreeFunction]
        [NativeConditional("PLATFORM_ANDROID")]
        internal static extern int GetTouchpadWidth();

        ///<summary>Indicates the height of the secondary touchpad.</summary>
        public static int secondaryTouchHeight
        {
            get { return GetTouchpadHeight(); }
        }

        [FreeFunction]
        [NativeConditional("PLATFORM_ANDROID")]
        internal static extern int GetTouchpadHeight();
    }
}
