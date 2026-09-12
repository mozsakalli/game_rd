// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.Animations
{
    ///<summary>Use this attribute in a script to mark a property as non-animatable.</summary>
    ///<remarks>A non-animatable property cannot have key animation (not keyable). It does not appear as animatable in the Animation window and it cannot be animated in the <see cref="Animator" /> component or the <see cref="Animation" /> component.</remarks>
    [RequiredByNativeCode]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class)]
    public class NotKeyableAttribute : Attribute
    {
    }
}
