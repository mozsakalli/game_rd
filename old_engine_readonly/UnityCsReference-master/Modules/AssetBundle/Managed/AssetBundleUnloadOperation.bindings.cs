// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.Bindings;
using UnityEngine.Scripting;
using UnityEngineInternal;

namespace UnityEngine
{
    ///<summary>Async unload operation for an <see cref="AssetBundle" />.</summary>
    [StructLayout(LayoutKind.Sequential)]
    [RequiredByNativeCode]
    [NativeHeader("Modules/AssetBundle/Public/AssetBundleUnloadOperation.h")]
    public class AssetBundleUnloadOperation : AsyncOperation
    {
        ///<summary>Synchronously waits for the operation to complete.</summary>
        ///<remarks>Call this function if you need to wait on the main thread until the <see cref="AssetBundleUnloadOperation" /> completes. When you call <c>WaitForCompletion</c>, the Unity runtime also waits for all other pending asynchronous operations to complete.</remarks>
        [NativeMethod("WaitForCompletion")]
        public extern void WaitForCompletion();

        ///<exclude />
        public AssetBundleUnloadOperation() { }

        private AssetBundleUnloadOperation(IntPtr ptr) : base(ptr)
        { }

        new internal static class BindingsMarshaller
        {
            public static AssetBundleUnloadOperation ConvertToManaged(IntPtr ptr) => new AssetBundleUnloadOperation(ptr);
            public static IntPtr ConvertToNative(AssetBundleUnloadOperation assetBundleUnloadOperation) => assetBundleUnloadOperation.m_Ptr;
        }
    }
}
