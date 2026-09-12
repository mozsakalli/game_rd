// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace UnityEngine.Animations
{
    ///<summary>Use this attribute to indicate that a property will be evaluated as a discrete value during animation playback.</summary>
    ///<remarks>When a property is assigned the DiscreteEvaluation attribute, it is evaluated as a constant value during animation playback. This means that the property's value is neither interpolated between keys nor blended between clips. This affects the evaluation of related AnimationCurves and disables editing the Tangent mode in the Animation window: the <see cref="T:UnityEditor.AnimationUtility.TangentMode" /> is set to Constant.
    ///
    ///Note: The DiscreteEvaluation attribute only supports positive integer values. If an Animation clip is created with a property that is assigned the DiscreteEvaluation attribute and this attribute is modified or removed, the Animation clip cannot be reused.</remarks>
    [RequiredByNativeCode]
    [AttributeUsage(AttributeTargets.Field)]
    public class DiscreteEvaluationAttribute : Attribute
    {
    }

    internal static class DiscreteEvaluationAttributeUtilities
    {
        public static int ConvertFloatToDiscreteInt(float f)
        {
            unsafe
            {
                float* fp = &f;
                int* i = (int*)fp;
                return *i;
            }
        }

        public static float ConvertDiscreteIntToFloat(int f)
        {
            unsafe
            {
                int* fp = &f;
                float* i = (float*)fp;
                return *i;
            }
        }
    }
}
