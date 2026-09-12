// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Asynchronous load request from an <see cref="AssetBundle" />.</summary>
    ///<seealso cref="AsyncOperation" />
    [StructLayout(LayoutKind.Sequential)]
    [RequiredByNativeCode]
    [NativeHeader("Modules/AssetBundle/Public/AssetBundleLoadAssetOperation.h")]
    public class AssetBundleRequest : ResourceRequest
    {
        ///<exclude />
        [NativeMethod("GetLoadedAsset")]
        protected override extern Object GetResult();

        ///<summary>Asset object being loaded (RO).</summary>
        ///<remarks>Note that accessing asset before <see cref="AsyncOperation.isDone" /> is true will stall the loading process.</remarks>
        public new Object asset { get { return GetResult(); } }

        ///<summary>Asset objects with sub assets being loaded. (RO)</summary>
        ///<remarks>Note that accessing asset before <see cref="AsyncOperation.isDone" /> is true will stall the loading process.</remarks>
        public extern Object[] allAssets
        {
            [NativeMethod("GetAllLoadedAssets")]
            get;
        }

        ///<exclude />
        public AssetBundleRequest() { }

        private AssetBundleRequest(IntPtr ptr) : base(ptr)
        { }

        new internal static class BindingsMarshaller
        {
            public static AssetBundleRequest ConvertToManaged(IntPtr ptr) => new AssetBundleRequest(ptr);
            public static IntPtr ConvertToNative(AssetBundleRequest request) => request.m_Ptr;
        }
    }
}
