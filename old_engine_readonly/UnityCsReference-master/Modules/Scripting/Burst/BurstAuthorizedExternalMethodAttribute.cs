// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;

namespace Unity.Burst
{
    ///<summary>The BurstAuthorizedExternalMethod attribute lets you mark a function as being authorized for Burst to call from within a static constructor.</summary>
    ///<remarks>Normally, Burst will not call into an external method while in a static constructor, because the static constructor may be called multiple times
    ///                and there is no guarantee that any particular external function is "pure" (has no side effects when called twice). The BurstAuthorizedExternalMethod
    ///                signifies that a function is "pure," in the sense that the end result of calling it multiple times, is the same as if you had called it only once.
    ///                This indicates that it is safe for Burst to call from a static constructor.</remarks>
    [RequireAttributeUsages]
    [AttributeUsage(AttributeTargets.Method)]
    public class BurstAuthorizedExternalMethodAttribute : Attribute
    {
    }
}
