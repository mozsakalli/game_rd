// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace Unity.Burst
{
    ///<summary>Use this attribute to exclude a method or property from being compiled to native code by the Burst compiler.</summary>
    ///<remarks>By default, Burst compiles all methods in jobs decorated with the <c>[BurstCompile]</c> attribute. You can use the <c>[BurstDiscard]</c> attribute on a method or property to exclude code from Burst compilation in situations where it can only run in .NET runtimes. For example, you can use <c>[BurstDiscard]</c> to exclude methods that use managed objects to perform logging, or methods that check the validity of something only valid in a managed environment.</remarks>
    ///<example nocheck="true">
    ///  <code><![CDATA[
    ///using Unity.Burst;
    ///using Unity.Collections;
    ///using Unity.Jobs;
    ///using UnityEngine;
    ///
    ///[BurstCompile]
    ///public struct MyJob : IJob
    ///{
    ///    // ...
    ///
    ///    [BurstDiscard]
    ///    public void NotExecutedInNative()
    ///    {
    ///        Debug.Log("This is a log from a managed job");
    ///    }
    ///
    ///    public void Execute()
    ///    {
    ///        // The following method call will not be compiled
    ///        NotExecutedInNative();
    ///    }
    ///}
    ///]]></code>
    ///</example>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
    public class BurstDiscardAttribute : Attribute
    {
    }
}
