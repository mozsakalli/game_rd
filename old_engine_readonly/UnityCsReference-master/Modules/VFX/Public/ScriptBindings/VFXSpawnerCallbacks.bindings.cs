// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine.VFX
{
    ///<summary>This abstract class provides a way to implement custom spawner block in C#.</summary>
    [System.Serializable]
    [RequiredByNativeCode]
    public abstract class VFXSpawnerCallbacks : ScriptableObject
    {
        ///<summary>Unity invokes this method when a parent spawner system triggers Play.</summary>
        ///<param name="state">The spawner state.</param>
        ///<param name="vfxValues">The values of expression (input properties for a spawner block).</param>
        ///<param name="vfxComponent">The visual effect.</param>
        public abstract void OnPlay(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
        ///<summary>Unity invokes this method when a parent spawner system triggers Update.</summary>
        ///<remarks>Unity invokes Update even if <see cref="VFX.VFXSpawnerState.playing" /> is false.</remarks>
        ///<param name="state">The spawner state.</param>
        ///<param name="vfxValues">The values of expression (input properties for a spawner block).</param>
        ///<param name="vfxComponent">The visual effect.</param>
        public abstract void OnUpdate(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
        ///<summary>Unity invokes this method when a parent spawner system triggers Stop.</summary>
        ///<param name="state">The spawner state.</param>
        ///<param name="vfxValues">The values of expression (input properties for a spawner block).</param>
        ///<param name="vfxComponent">The visual effect.</param>
        public abstract void OnStop(VFXSpawnerState state, VFXExpressionValues vfxValues, VisualEffect vfxComponent);
    }
}
