// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEngine.Scripting;

namespace UnityEngine.Animations
{
    ///<summary>Use this interface to prevent a class from being removed in the Animation Clip Preview window. The class must inherit from <see cref="MonoBehaviour" />.</summary>
    [UsedByNativeCode]
    public interface IAnimationPreviewable
    {
        ///<summary>Called every frame before rendering the Preview window. Use this method to update any class logic.</summary>
        void OnPreviewUpdate();
    }
}
