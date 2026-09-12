// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine;
using UnityEngine.Bindings;
using UnityObject = UnityEngine.Object;

namespace UnityEditor
{
    public partial class PlayerSettings : UnityObject
    {
        [NativeHeader("Runtime/Misc/PlayerSettings.h")]
        public static class Windows
        {
            [NativeProperty("WindowsCopyVCRuntime")]
            public static extern bool copyVCRuntime
            {
                [StaticAccessor("GetPlayerSettings().GetEditorOnly()", StaticAccessorType.Dot)]
                get;

                [StaticAccessor("GetPlayerSettings().GetEditorOnlyForUpdate()", StaticAccessorType.Dot)]
                set;
            }
        }
    }
}
