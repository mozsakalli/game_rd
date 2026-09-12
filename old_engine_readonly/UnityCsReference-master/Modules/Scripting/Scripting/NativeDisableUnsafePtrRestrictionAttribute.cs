// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;

namespace Unity.Collections.LowLevel.Unsafe
{
    // This lives here because Burst's FunctionPointer<T> needs it
    ///<summary>Enable the use of unsafe pointers in jobs.</summary>
    ///<remarks>By default, unsafe pointers aren't allowed in jobs because it isn't possible for the Job Debugger to gurantee race condition free 
    ///behavior. This attribute lets you explicitly disable the restriction on a job.</remarks>
    ///<example nocheck="true">
    ///  <code><![CDATA[struct MyJob : IJob
    ///{
    ///    [NativeDisableUnsafePtrRestriction]
    ///    int* myCustomPointer;
    ///
    ///
    ///    void Execute()
    ///    {
    ///        ...
    ///    }
    ///}]]></code>
    ///</example>
    [RequiredByNativeCode]
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NativeDisableUnsafePtrRestrictionAttribute : Attribute
    {}
}
