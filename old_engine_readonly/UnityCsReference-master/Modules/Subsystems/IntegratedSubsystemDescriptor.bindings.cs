// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.InteropServices;
using UnityEngine.Bindings;
using UnityEngine.Scripting;

namespace UnityEngine
{
    internal interface ISubsystemDescriptorImpl : ISubsystemDescriptor
    {
        IntPtr ptr { get; set; }
    }

    ///<summary>Information about a subsystem that can be queried before creating a subsystem instance.</summary>
    ///<remarks>This describes a native-backed <see cref="IntegratedSubsystem" />. New subsystems should be managed-only; use <see cref="UnityEngine.SubsystemsImplementation.SubsystemWithProvider" /> and its descriptor instead.</remarks>
    [UsedByNativeCode("SubsystemDescriptorBase")]
    [StructLayout(LayoutKind.Sequential)]
    public abstract class IntegratedSubsystemDescriptor : ISubsystemDescriptorImpl
    {
        [VisibleToOtherModules("UnityEngine.XRModule")]
        internal IntPtr m_Ptr;

        ///<summary>A unique string that identifies the subsystem that this Descriptor can create.</summary>
        public string id => SubsystemDescriptorBindings.GetId(m_Ptr);

        IntPtr ISubsystemDescriptorImpl.ptr
        {
            get => m_Ptr;
            set => m_Ptr = value;
        }

        ISubsystem ISubsystemDescriptor.Create() => CreateImpl();
        internal abstract ISubsystem CreateImpl();
    }

    ///<exclude />
    [NativeHeader("Modules/Subsystems/SubsystemDescriptor.h")]
    [UsedByNativeCode("SubsystemDescriptor")]
    [StructLayout(LayoutKind.Sequential)]
    public class IntegratedSubsystemDescriptor<TSubsystem> : IntegratedSubsystemDescriptor
        where TSubsystem : IntegratedSubsystem
    {
        internal override ISubsystem CreateImpl()
        {
            return this.Create();
        }

        ///<summary>Creates a <see cref="Subsystem" /> from this descriptor.</summary>
        ///<returns>Instance of subsystem.</returns>
        public TSubsystem Create()
        {
            IntPtr ptr = SubsystemDescriptorBindings.Create(m_Ptr);
            var subsystem = (TSubsystem)SubsystemManager.GetIntegratedSubsystemByPtr(ptr);

            if (subsystem != null)
                subsystem.m_SubsystemDescriptor = this;
            return subsystem;
        }
    }

    internal static class SubsystemDescriptorBindings
    {
        // These are here instead of any of the above types because
        // generic types are not supported by the bindings system
        public static extern IntPtr Create(IntPtr descriptorPtr);
        public static extern string GetId(IntPtr descriptorPtr);
    }
}
