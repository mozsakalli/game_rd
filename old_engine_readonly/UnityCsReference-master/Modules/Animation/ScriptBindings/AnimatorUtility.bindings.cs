// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Various utilities for animator manipulation.</summary>
    [NativeHeader("Modules/Animation/ScriptBindings/AnimatorUtility.bindings.h")]
    public class AnimatorUtility
    {
        ///<summary>This function will remove all transform hierarchy under GameObject, the animator will write directly transform matrices into the skin mesh matrices saving many CPU cycles.</summary>
        ///<remarks>You can optionally provide a list of transform name, this function will create a flattened hierarchy of these transform under GameObject.
        ///
        ///A call to this function at runtime will re-initialize the animator.</remarks>
        ///<param name="go">GameObject to Optimize.</param>
        ///<param name="exposedTransforms">List of transform name to expose.</param>
        ///<seealso cref="AnimatorUtility.OptimizeTransformHierarchy" />
        ///<seealso cref="Animator.hasTransformHierarchy" />
        [FreeFunction("AnimatorUtilityBindings::OptimizeTransformHierarchy")]
        extern public static void OptimizeTransformHierarchy([NotNull] GameObject go, string[] exposedTransforms);

        ///<summary>This function will recreate all transform hierarchy under GameObject.</summary>
        ///<remarks>A call to this function at runtime will re-initialize the animator.</remarks>
        ///<param name="go">GameObject to Deoptimize.</param>
        ///<seealso cref="AnimatorUtility.OptimizeTransformHierarchy" />
        ///<seealso cref="Animator.hasTransformHierarchy" />
        [FreeFunction("AnimatorUtilityBindings::DeoptimizeTransformHierarchy")]
        extern public static void DeoptimizeTransformHierarchy([NotNull] GameObject go);
    }
}
