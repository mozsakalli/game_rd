// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Asynchronous <see cref="AssetBundle" /> recompression from one compression method and level to another.</summary>
    ///<seealso cref="AssetBundle.RecompressAssetBundleAsync" />
    ///<seealso cref="AsyncOperation" />
    [StructLayout(LayoutKind.Sequential)]
    [RequiredByNativeCode]
    [NativeHeader("Modules/AssetBundle/Public/AssetBundleRecompressOperation.h")]
    public class AssetBundleRecompressOperation : AsyncOperation
    {
        ///<summary>A string describing the recompression operation result (RO).</summary>
        public extern string humanReadableResult
        {
            [NativeMethod("GetResultStr")]
            get;
        }

        ///<summary>Path of the <see cref="AssetBundle" /> being recompressed (RO).</summary>
        public extern string inputPath
        {
            [NativeMethod("GetInputPath")]
            get;
        }

        ///<summary>Path of the resulting recompressed <see cref="AssetBundle" /> (RO).</summary>
        public extern string outputPath
        {
            [NativeMethod("GetOutputPath")]
            get;
        }

        ///<summary>Result of the recompression operation.</summary>
        public extern AssetBundleLoadResult result
        {
            [NativeMethod("GetResult")]
            get;
        }

        ///<summary>True if the recompress operation is complete and was successful, otherwise false (RO).</summary>
        ///<remarks>Note that accessing before <see cref="AsyncOperation.isDone" /> will return false.</remarks>
        public extern bool success
        {
            [NativeMethod("GetSuccess")]
            get;
        }

        ///<exclude />
        public AssetBundleRecompressOperation() { }

        private AssetBundleRecompressOperation(IntPtr ptr) : base(ptr)
        { }

        new internal static class BindingsMarshaller
        {
            public static AssetBundleRecompressOperation ConvertToManaged(IntPtr ptr) => new AssetBundleRecompressOperation(ptr);
            public static IntPtr ConvertToNative(AssetBundleRecompressOperation op) => op.m_Ptr;
        }
    }
}
