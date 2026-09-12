// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Bindings;

namespace UnityEditor.Scripting.ScriptCompilation
{
    interface IVersionDefinesConsoleLogs
    {
        void LogVersionDefineError(EntityId assetEntityId, ExpressionNotValidException validationError);
        void ClearVersionDefineErrors();
    }

    [NativeHeader("Editor/Src/ScriptCompilation/VersionDefinesConsoleLogs.h")]
    class VersionDefinesConsoleLogs : IVersionDefinesConsoleLogs
    {
        // assetEntityId associates the console message with the asmdef so double-clicking selects it in the
        // Project window. It may be EntityId.None (e.g. in batch mode), which the native side tolerates.
        public void LogVersionDefineError(EntityId assetEntityId, ExpressionNotValidException validationError)
        {
            InternalLogVersionDefineError(validationError, assetEntityId);
        }

        public void ClearVersionDefineErrors()
        {
            InternalClearVersionDefineErrors();
        }

        [FreeFunction(nameof(InternalLogVersionDefineError))]
        static extern void InternalLogVersionDefineError(Exception ex, UnityEngine.EntityId assetInstanceID);


        [FreeFunction(nameof(InternalClearVersionDefineErrors))]
        static extern void InternalClearVersionDefineErrors();
    }
}
