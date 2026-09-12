// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using RequiredByNativeCodeAttribute = UnityEngine.Scripting.RequiredByNativeCodeAttribute;
using System;
using UnityEngine.Bindings;

namespace UnityEngine
{
    public partial class AsyncOperation : YieldInstruction
    {
        [VisibleToOtherModules]
        internal IntPtr m_Ptr;

#pragma warning disable UA5000 // The Avoid Finalizer Analyzer produces compile errors for any new finalizers. This pre-existing finalizer declaration has been suppressed, but should be rewritten if possible.
        ~AsyncOperation()
        {
            InternalDestroy(m_Ptr);
        }
#pragma warning restore UA5000

        private System.Action<AsyncOperation> m_completeCallback;

        [RequiredByNativeCode]
        internal void InvokeCompletionEvent()
        {
            if (m_completeCallback != null)
            {
                m_completeCallback(this);
                m_completeCallback = null;
            }
        }

        public event System.Action<AsyncOperation> completed
        {
            add
            {
                if (isDone)
                {
                    value(this);
                }
                else
                {
                    m_completeCallback += value;
                }
            }
            remove
            {
                m_completeCallback -= value;
            }
        }
    }
}
