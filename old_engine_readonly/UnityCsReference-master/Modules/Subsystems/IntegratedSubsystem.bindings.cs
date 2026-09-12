// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>An IntegratedSubsystem is initialized from an <see cref="IntegratedSubsystemDescriptor" /> for a given Subsystem (Example, Input, Environment, Display, etc.) and provides an interface to interact with that given IntegratedSubsystem until it is Destroyed. After an IntegratedSubsystem is created it can be Started or Stopped to turn on and off functionality (and preserve performance). The base type for IntegratedSubsystem only exposes this functionality; this class is designed to be a base class for derived classes that expose more functionality specific to a given IntegratedSubsystem.
    ///
    ///            Note: initializing a second IntegratedSubsystem from the same IntegratedSubsystemDescriptor will return a reference to the existing IntegratedSubsystem as only one IntegratedSubsystem is currently allowed for a single IntegratedSubsystem provider.</summary>
    ///<remarks>New subsystems should not derive from IntegratedSubsystem. IntegratedSubsystem is a managed wrapper over a native C++ subsystem and exists only to support subsystems that are still tied to native code. Implement new subsystems as managed-only by deriving from <see cref="UnityEngine.SubsystemsImplementation.SubsystemWithProvider" /> instead.</remarks>
    [UsedByNativeCode]
    [StructLayout(LayoutKind.Sequential)]
    [NativeHeader("Modules/Subsystems/Subsystem.h")]
    public class IntegratedSubsystem : ISubsystem
    {
        [VisibleToOtherModules("UnityEngine.XRModule")]
        internal IntPtr m_Ptr;

        internal ISubsystemDescriptor m_SubsystemDescriptor;

        extern internal void SetHandle([UnityMarshalAs(NativeType.ScriptingObjectPtr)] IntegratedSubsystem subsystem);
        ///<summary>Starts an instance of a subsystem.</summary>
        ///<remarks>Once the instance is started, the subsystem representing this instance is active and can be interacted with.</remarks>
        extern public void Start();
        ///<summary>Stops an instance of a subsystem.</summary>
        ///<remarks>Once the instance is stopped, the subsystem representing this instance is no longer active and should not consume performance.</remarks>
        extern public void Stop();
        ///<summary>Destroys this instance of a subsystem.</summary>
        ///<remarks>Also unloads all resources acquired during initialization step. Should be called when this instance of a subsystem is no longer needed.
        ///
        ///                Note: Once a Subsystem is Destroyed, script can still hold a reference but calling a method on it will result in a NullArgumentException.</remarks>
        public void Destroy()
        {
            IntPtr removedPtr = m_Ptr;
            SubsystemManager.RemoveIntegratedSubsystemByPtr(m_Ptr);
            SubsystemBindings.DestroySubsystem(removedPtr);
            m_Ptr = IntPtr.Zero;
        }

        ///<summary>Whether or not the subsystem is running.</summary>
        public bool running => valid && IsRunning();

        internal bool valid => m_Ptr != IntPtr.Zero;

        extern internal bool IsRunning();

        internal static class BindingsMarshaller
        {
            public static IntPtr ConvertToNative(IntegratedSubsystem integratedSubsystem) => integratedSubsystem.m_Ptr;
        }
    }

    ///<exclude />
    [UsedByNativeCode("Subsystem_TSubsystemDescriptor")]
    public partial class IntegratedSubsystem<TSubsystemDescriptor> : IntegratedSubsystem
        where TSubsystemDescriptor : ISubsystemDescriptor
    {
        ///<exclude />
        public TSubsystemDescriptor subsystemDescriptor => (TSubsystemDescriptor)m_SubsystemDescriptor;
    }

    internal static class SubsystemBindings
    {
        internal static extern void DestroySubsystem(IntPtr nativePtr);
    }
}
