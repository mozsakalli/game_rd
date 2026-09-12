// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;
using UnityEngine.Rendering;
using UnityEngine.Scripting;
using Unity.Scripting.LifecycleManagement;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

//Keep this namespace to be compatible with visual effect graph package 7.0.1
//There was an unexpected useless "using UnityEngine.Experimental.VFX;" in VFXMotionVector.cs
namespace UnityEngine.Experimental.VFX
{
    internal static class VFXManager
    {
    }
}

namespace UnityEngine.VFX
{
    ///<summary>Represents settings that specify how the Visual Effect Graph should handle an XR Camera.</summary>
    [RequiredByNativeCode]
    public struct VFXCameraXRSettings
    {
        ///<summary>The number of views the camera has in total. For a normal Camera, this is 1. For a Camera in XR, this is 2.</summary>
        public uint viewTotal;
        ///<summary>The number of views to render in the pass. In Unity, there are different methods of rendering a Camera in XR. For multiple pass rendering, <c>viewTotal</c> is 2 and viewCount will be 1. For other XR rendering methods, both <c>viewTotal</c> and <c>viewCount</c> are 2.</summary>
        public uint viewCount;
        ///<summary>Indicates where to start rendering views in this pass. Currently, the Visual Effect Graph uses this for multiple pass XR rendering. In this case, the first pass value is 0 and the second pass is 1. For other XR rendering methods, this is 0.</summary>
        public uint viewOffset;
    }

    ///<summary>This structure provides runtime information on how Unity batches a <see cref="VisualEffectAsset" />.</summary>
    [RequiredByNativeCode]
    public struct VFXBatchedEffectInfo
    {
        ///<summary>The <see cref="VisualEffectAsset" /> associated with this Batched Effect Info.</summary>
        public VisualEffectAsset vfxAsset;
        ///<summary>The number of active batches the <see cref="VisualEffectAsset" /> uses.</summary>
        public uint activeBatchCount;
        ///<summary>The number of inactive batches that are allocated for later reuse.</summary>
        public uint inactiveBatchCount;
        ///<summary>The number of active <see cref="VisualEffect" /> instances of this <see cref="VisualEffectAsset" />.</summary>
        public uint activeInstanceCount;
        ///<summary>The number of <see cref="VisualEffect" /> instances that are not batched.</summary>
        public uint unbatchedInstanceCount;
        ///<summary>The total number of <see cref="VisualEffect" /> that can be instanciated with the current allocated batches.</summary>
        public uint totalInstanceCapacity;
        ///<summary>The maximum number of <see cref="VisualEffect" /> that can be instanciated in a single batch.</summary>
        public uint maxInstancePerBatchCapacity;
        ///<summary>The GPU size, in bytes, that the batches of this <see cref="VisualEffectAsset" /> use.</summary>
        public ulong totalGPUSizeInBytes;
        ///<summary>The CPU size, in bytes, that the batches of this <see cref="VisualEffectAsset" /> use.</summary>
        public ulong totalCPUSizeInBytes;
    }

    [RequiredByNativeCode]
    internal struct VFXBatchInfo
    {
        public uint capacity;
        public uint activeInstanceCount;
    }

    ///<summary>Use this class to set a number of properties that control VisualEffect behavior within your Unity Project.</summary>
    [RequiredByNativeCode]
    [NativeHeader("Modules/VFX/Public/VFXManager.h")]
    [NativeHeader("Modules/VFX/Public/ScriptBindings/VFXManagerBindings.h")]
    [StaticAccessor("GetVFXManager()", StaticAccessorType.Dot)]
    public static partial class VFXManager
    {
        ///<exclude />
        extern public static VisualEffect[] GetComponents();
        extern internal static ScriptableObject runtimeResources { get; }

        ///<summary>The fixed interval in which the frame rate updates. The tick rate is in seconds.</summary>
        ///<remarks>At run-time, <see cref="Time.timeScale" /> affects the <c>fixedDeltaTime</c> and <c>deltaTime</c> intervals. These values are less than or equal to <see cref="VFX.VFXManager.maxDeltaTime" />.
        ///
        ///The <c>fixedDeltaTime</c> interval can be an integer multiple of <see cref="VFX.VFXManager.fixedTimeStep" />.</remarks>
        extern public static float fixedTimeStep { get; set; }
        ///<summary>The maximum allowed delta time for an update interval. This limit affects <c>fixedDeltaTime</c> and <c>deltaTime</c>. The tick rate is in seconds.</summary>
        ///<remarks>At run-time, <see cref="Time.timeScale" /> affects the <c>fixedDeltaTime</c> and <c>deltaTime</c> intervals. These values are less than or equal to <see cref="VFX.VFXManager.maxDeltaTime" />.
        ///
        ///The <c>fixedDeltaTime</c> interval can be an integer multiple of <see cref="VFX.VFXManager.fixedTimeStep" />.</remarks>
        extern public static float maxDeltaTime { get; set; }

        extern internal static uint maxCapacity { get; set; }
        extern internal static float maxScrubTime { get; set; }
        ///<summary>This property describes the folder path where "VFXCommon.cginc" and visual effect template are. (This location relies on scriptable render pipeline).</summary>
        extern internal static string renderPipeSettingsPath { get; }

        extern internal static uint batchEmptyLifetime { get; set; }

        extern internal static ScriptableObject editorResources { get; }
        extern internal static void ResyncMaterials([NotNull] VisualEffectAsset asset);
        extern internal static bool renderInSceneView { get; set; }
        // Re-initialized only on code reload (VisualEffectAssetEditorUtility static ctor).
        [AutoStaticsCleanupOnCodeReload]
        internal static bool activateVFX { get; set; }

        extern internal static void CleanupEmptyBatches(bool force = false);

        ///<summary>Deallocates all empty batches used in the VFX runtime.</summary>
        ///<remarks>Unity automatically keeps empty batches to reuse. This uses additional memory to avoid multiple allocations or deallocations.
        ///Use this function to force Unity to deallocate empty batches and reclaim memory.
        ///
        ///Note: You can configure the amount of time empty batches are kept around in the VFX Project Settings.</remarks>
        public static void FlushEmptyBatches()
        {
            CleanupEmptyBatches(true);
        }

        ///<summary>Gets information on how a Visual Effect Asset is batched.</summary>
        ///<param name="vfx">The Visual Effect Asset</param>
        ///<returns>A <see cref="VFXBatchedEffectInfo" /> instance.</returns>
        extern public static VFXBatchedEffectInfo GetBatchedEffectInfo([NotNull] VisualEffectAsset vfx);

        ///<summary>Gets batch information of all active Visual Effect Assets.</summary>
        ///<param name="infos">The List that this function populates with the <see cref="VFXBatchedEffectInfo" />.</param>
        [FreeFunction(Name = "VFXManagerBindings::GetBatchedEffectInfos", HasExplicitThis = false)]
        extern public static void GetBatchedEffectInfos([NotNull][Out] List<VFXBatchedEffectInfo> infos);

        extern internal static VFXBatchInfo GetBatchInfo(VisualEffectAsset vfx, uint batchIndex);

        private static readonly VFXCameraXRSettings kDefaultCameraXRSettings = new VFXCameraXRSettings { viewTotal = 1, viewCount = 1, viewOffset = 0 };

        ///<summary>Use this method to prepare and process per-Camera VFX commands for this frame.</summary>
        ///<remarks>Scriptable Render Pipelines (SRP) are responsible for calling this function. The High Definition and Universal Render Pipelines implement this call but you must do it manually if you create your own SRP.
        ///This function is equivalent to <see cref="VFXManager.PrepareCamera" />(cam) followed by <see cref="VFXManager.ProcessCameraCommand" />(cam, null).</remarks>
        ///<param name="cam">The Camera to prepare for processing VFX commands.</param>
        [Obsolete("Use explicit PrepareCamera and ProcessCameraCommand instead")]
        public static void ProcessCamera(Camera cam)
        {
            PrepareCamera(cam, kDefaultCameraXRSettings);
            Internal_ProcessCameraCommand(cam, null, kDefaultCameraXRSettings, IntPtr.Zero, IntPtr.Zero);
        }

        ///<summary>Use this method to prepare per-Camera VFX commands for this frame.</summary>
        ///<remarks>This function updates Materials that the VisualEffect uses internally, but does not execute any rendering commands. To execute rendering commands, call <see cref="VFXManager.ProcessCamera" />.
        ///
        ///Scriptable Render Pipelines (SRP) are responsible for calling this function. The High Definition and Universal Render Pipelines implement this call.
        ///
        ///If you create your own SRP, you should typically call this function before culling.</remarks>
        ///<param name="cam">The Camera to prepare for processing VFX commands.</param>
        public static void PrepareCamera(Camera cam)
        {
            PrepareCamera(cam, kDefaultCameraXRSettings);
        }

        ///<summary>Use this method to prepare per-Camera VFX commands for this frame.</summary>
        ///<remarks>This function updates Materials that the VisualEffect uses internally, but does not execute any rendering commands. To execute rendering commands, call <see cref="VFXManager.ProcessCamera" />.
        ///
        ///Scriptable Render Pipelines (SRP) are responsible for calling this function. The High Definition and Universal Render Pipelines implement this call.
        ///
        ///If you create your own SRP, you should typically call this function before culling.</remarks>
        ///<param name="cam">The Camera to prepare for processing VFX commands.</param>
        ///<param name="camXRSettings">The XR settings that the Visual Effect Graph uses to prepare the Camera.</param>
        extern public static void PrepareCamera([NotNull] Camera cam, VFXCameraXRSettings camXRSettings);

        ///<summary>Use this method to process per-Camera VFX commands for the current frame.</summary>
        ///<remarks>The current Scriptable Render Pipeline implementation is responsible for calling this function. The High Definition and Universal Render Pipelines implement this call but you must do it manually if you create your own Scriptable Render Pipeline.</remarks>
        ///<param name="cam">The Camera to process the VFX commands for.</param>
        ///<param name="cmd">The CommandBuffer to push commands to (can be null).</param>
        ///<seealso cref="VFXManager.PrepareCamera" />
        [Obsolete("Use ProcessCameraCommand with CullingResults to allow culling of VFX per camera")]
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd)
        {
            Internal_ProcessCameraCommand(cam, cmd, kDefaultCameraXRSettings, IntPtr.Zero, IntPtr.Zero);
        }

        ///<summary>Use this method to process per-Camera VFX commands for the current frame.</summary>
        ///<remarks>The current Scriptable Render Pipeline implementation is responsible for calling this function. The High Definition and Universal Render Pipelines implement this call but you must do it manually if you create your own Scriptable Render Pipeline.</remarks>
        ///<param name="cam">The Camera to process the VFX commands for.</param>
        ///<param name="cmd">The CommandBuffer to push commands to (can be null).</param>
        ///<param name="camXRSettings">The XR settings that the Visual Effect Graph uses to process the Camera commands.</param>
        ///<seealso cref="VFXManager.PrepareCamera" />
        [Obsolete("Use ProcessCameraCommand with CullingResults to allow culling of VFX per camera")]
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings)
        {
            Internal_ProcessCameraCommand(cam, cmd, camXRSettings, IntPtr.Zero, IntPtr.Zero);
        }

        ///<summary>Use this method to process per-Camera VFX commands for the current frame.</summary>
        ///<remarks>The current Scriptable Render Pipeline implementation is responsible for calling this function. The High Definition and Universal Render Pipelines implement this call but you must do it manually if you create your own Scriptable Render Pipeline.</remarks>
        ///<param name="cam">The Camera to process the VFX commands for.</param>
        ///<param name="cmd">The CommandBuffer to push commands to (can be null).</param>
        ///<param name="camXRSettings">The XR settings that the Visual Effect Graph uses to process the Camera commands.</param>
        ///<param name="results">The CullingResults for the current camera, used to cull per-camera VFX commands.</param>
        ///<seealso cref="VFXManager.PrepareCamera" />
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, Rendering.CullingResults results)
        {
            Internal_ProcessCameraCommand(cam, cmd, camXRSettings, results.ptr, IntPtr.Zero);
        }

        ///<summary>Use this method to process per-Camera VFX commands for the current frame.</summary>
        ///<remarks>The current Scriptable Render Pipeline implementation is responsible for calling this function. The High Definition and Universal Render Pipelines implement this call but you must do it manually if you create your own Scriptable Render Pipeline.</remarks>
        ///<param name="cam">The Camera to process the VFX commands for.</param>
        ///<param name="cmd">The CommandBuffer to push commands to (can be null).</param>
        ///<param name="camXRSettings">The XR settings that the Visual Effect Graph uses to process the Camera commands.</param>
        ///<param name="results">The CullingResults for the current camera, used to cull per-camera VFX commands.</param>
        ///<param name="customPassResults">Additional CullingResults coming from custom passes, if any.</param>
        ///<seealso cref="VFXManager.PrepareCamera" />
        public static void ProcessCameraCommand(Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, Rendering.CullingResults results, Rendering.CullingResults customPassResults)
        {
            Internal_ProcessCameraCommand(cam, cmd, camXRSettings, results.ptr, customPassResults.ptr);
        }

        extern private static void Internal_ProcessCameraCommand([NotNull] Camera cam, CommandBuffer cmd, VFXCameraXRSettings camXRSettings, IntPtr cullResults, IntPtr customPassCullResults);
        ///<summary>Queries which buffers the VFX Manager needs for the given Camera.</summary>
        ///<remarks>Use this call to make your custom SRP support screen space effects in VFX Graph.</remarks>
        ///<param name="cam">The Camera for which to query needed buffers.</param>
        ///<returns>A list of all needed buffer flags.</returns>
        extern public static VFXCameraBufferTypes IsCameraBufferNeeded([NotNull] Camera cam);
        ///<summary>Use this method to set the buffer of a given type for this Camera. This allows the VFX Manager to use the buffer.</summary>
        ///<remarks>In custom Scriptable Render Pipelines, this buffer allows the VFXManager to use buffer behaviors for the Camera, for example depth collisions.
        ///The buffer must be available during the VFX update of the next frame.
        ///To query the need for a buffer, call <see cref="VFXManager.IsCameraBufferNeeded" />.</remarks>
        ///<param name="cam">The Camera to set the buffer for.</param>
        ///<param name="type">The type of buffer to set.</param>
        ///<param name="buffer">The buffer to set.</param>
        ///<param name="x">X offset of the viewport in the buffer.</param>
        ///<param name="y">Y offset of the viewport in the buffer.</param>
        ///<param name="width">Width of the viewport in the buffer.</param>
        ///<param name="height">Height of the viewport in the buffer.</param>
        extern public static void SetCameraBuffer([NotNull] Camera cam, VFXCameraBufferTypes type, Texture buffer, int x, int y, int width, int height);

        ///<summary>Enables or disables Ray Tracing for all Visual Effects.</summary>
        ///<remarks>When enabled, the effects containing  Ray-traced outputs will be added to the Ray Tracing Accelaration Structure.</remarks>
        ///<param name="enabled">Whether Ray Tracing is enabled or not.</param>
        extern public static void SetRayTracingEnabled(bool enabled);
        ///<summary>Request the construction of AABB buffers by the Visual Effects for the current frame.</summary>
        extern public static void RequestRtasAabbConstruction();
    }
}
