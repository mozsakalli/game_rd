// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;

namespace UnityEngine.Rendering
{
    [NativeHeader("Runtime/GfxDevice/GraphicsApiValidationBindings.h")]
    public static class GraphicsApiValidation
    {
        [FreeFunction("IsGraphicsApiValidationSupported", IsThreadSafe = false)]
        public static extern bool IsValidationSupported();

        [FreeFunction("ClearGraphicsApiValidationErrors", IsThreadSafe = false)]
        public static extern void ClearValidationErrors();

        [FreeFunction("GetGraphicsApiValidationErrorCount", IsThreadSafe = false)]
        public static extern int GetValidationErrorCount();

        [FreeFunction("GetGraphicsApiValidationError", IsThreadSafe = false)]
        public static extern string GetValidationError(int index);

        [FreeFunction("GetGraphicsApiValidationErrorsDroppedCount", IsThreadSafe = false)]
        public static extern int GetValidationErrorsDroppedCount();

        [FreeFunction("SetGraphicsApiValidationErrorLoggingSuppressed", IsThreadSafe = false)]
        public static extern void SetValidationErrorLoggingSuppressed(bool suppressed);

        [FreeFunction("IsGraphicsApiValidationErrorLoggingSuppressed", IsThreadSafe = false)]
        public static extern bool IsValidationErrorLoggingSuppressed();

        [FreeFunction("IsGraphicsApiValidationRequested", IsThreadSafe = false)]
        public static extern bool IsValidationRequested();

        [FreeFunction("IsGraphicsApiValidationActive", IsThreadSafe = false)]
        public static extern bool IsValidationActive();
    }
}
