// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
namespace UnityEngine
{
    partial class AssetBundle
    {
        ///<summary>Loads an asset bundle from a disk.</summary>
        ///<remarks>Method CreateFromFile is obsolete and has been renamed to <see cref="LoadFromFile" />.</remarks>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Method CreateFromFile has been renamed to LoadFromFile (UnityUpgradable) -> LoadFromFile(*)", true)]
        public static AssetBundle CreateFromFile(string path) { return null; }

        ///<summary>Asynchronously create an AssetBundle from a memory region.</summary>
        ///<remarks>Method CreateFromMemory is obsolete and has been renamed to <see cref="LoadFromMemoryAsync" /></remarks>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Method CreateFromMemory has been renamed to LoadFromMemoryAsync (UnityUpgradable) -> LoadFromMemoryAsync(*)", true)]
        public static AssetBundleCreateRequest CreateFromMemory(byte[] binary) { return null; }

        ///<summary>Synchronously create an AssetBundle from a memory region.</summary>
        ///<remarks>Method CreateFromMemoryImmediate is obsolete and has been renamed to <see cref="LoadFromMemory" />.</remarks>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        [Obsolete("Method CreateFromMemoryImmediate has been renamed to LoadFromMemory (UnityUpgradable) -> LoadFromMemory(*)", true)]
        public static AssetBundle CreateFromMemoryImmediate(byte[] binary) { return null; }
    }
}
