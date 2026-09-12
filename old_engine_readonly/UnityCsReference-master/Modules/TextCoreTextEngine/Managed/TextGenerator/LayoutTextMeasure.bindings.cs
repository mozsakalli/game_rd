// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;

namespace UnityEngine.TextCore.Text
{
    [NativeHeader("Modules/TextCoreTextEngine/Native/LayoutTextMeasure.h")]
    [VisibleToOtherModules("UnityEngine.UIElementsModule")]
    internal static class LayoutTextMeasureNative
    {
        [FreeFunction("GetTGIMeasuredWidths", IsThreadSafe = true)]
        [VisibleToOtherModules("UnityEngine.UIElementsModule")]
        internal static extern void GetTGIMeasuredWidths(IntPtr tgiPtr, out float measuredWidth, out float roundedWidth, out float pointScaleFactor);
    }
}
