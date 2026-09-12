// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine.Bindings;
using UnityEngine.Rendering;
using UnityEngine.Scripting;
using UnityEngineInternal;

namespace UnityEngine
{
    ///<summary>Asynchronous load request for an <see cref="AssetBundle" />.</summary>
    ///<seealso cref="AssetBundle.LoadFromFileAsync" />
    ///<seealso cref="AssetBundle.LoadFromMemoryAsync" />
    ///<seealso cref="AssetBundle.LoadFromStreamAsync" />
    ///<seealso cref="AsyncOperation" />
    [StructLayout(LayoutKind.Sequential)]
    [RequiredByNativeCode]
    [NativeHeader("Modules/AssetBundle/Public/AssetBundleLoadFromAsyncOperation.h")]
    public class AssetBundleCreateRequest : AsyncOperation
    {
        ///<summary>Asset object being loaded (RO).</summary>
        ///<remarks>Note that accessing asset before <see cref="AsyncOperation.isDone" /> is true will stall the loading process.</remarks>
        public extern UnityEngine.AssetBundle assetBundle
        {
            [NativeMethod("GetAssetBundleBlocking")]
            get;
        }

        [NativeMethod("SetEnableCompatibilityChecks")]
        private extern void SetEnableCompatibilityChecks(bool set);
        internal void DisableCompatibilityChecks()
        {
            SetEnableCompatibilityChecks(false);
        }

        ///<exclude />
        public AssetBundleCreateRequest() { }

        private AssetBundleCreateRequest(IntPtr ptr) : base(ptr)
        { }

        new internal static class BindingsMarshaller
        {
            public static AssetBundleCreateRequest ConvertToManaged(IntPtr ptr) => new AssetBundleCreateRequest(ptr);
            public static IntPtr ConvertToNative(AssetBundleCreateRequest assetBundleCreateRequest) => assetBundleCreateRequest.m_Ptr;
        }
    }
}
