// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine.Scripting;
using UnityEngine.SubsystemsImplementation;

namespace UnityEngine
{
    ///<summary>Information about a subsystem that can be queried before creating a subsystem instance.
    ///
    ///This subsystem descriptor base-class is deprecated. If you are creating a new subsystem type, derive from <see cref="SubsystemDescriptorWithProvider" /> instead.</summary>
    [Obsolete("Use SubsystemDescriptorWithProvider instead.", false)]
    public abstract class SubsystemDescriptor : ISubsystemDescriptor
    {
        ///<summary>A unique string that identifies the subsystem that this Descriptor can create.</summary>
        public string id { get; set; }
        ///<summary>The System.Type of the subsystem implementation associated with this descriptor.</summary>
        public Type subsystemImplementationType { get; set; }

        ISubsystem ISubsystemDescriptor.Create() => CreateImpl();
        internal abstract ISubsystem CreateImpl();
    }

#pragma warning disable CS0618
    ///<exclude />
    [Obsolete("Use SubsystemDescriptorWithProvider<> instead.", false)]
    public class SubsystemDescriptor<TSubsystem> : SubsystemDescriptor
        where TSubsystem : Subsystem
#pragma warning restore CS0618
    {
        internal override ISubsystem CreateImpl() => this.Create();

        ///<summary>Creates a <see cref="Subsystem" /> from this descriptor.</summary>
        ///<returns>Instance of subsystem.</returns>
        public TSubsystem Create()
        {
            TSubsystem subsystem = SubsystemManager.FindDeprecatedSubsystemByDescriptor(this) as TSubsystem;
            if (subsystem != null)
                return subsystem;

            subsystem = Activator.CreateInstance(subsystemImplementationType) as TSubsystem;
            subsystem.m_SubsystemDescriptor = this;

            SubsystemManager.AddDeprecatedSubsystem(subsystem);
            return subsystem;
        }
    }

    // used in the subsystem-registration package
    internal static class Internal_SubsystemDescriptors
    {
#pragma warning disable CS0618
        [RequiredByNativeCode]
        internal static void Internal_AddDescriptor(SubsystemDescriptor descriptor) => SubsystemDescriptorStore.RegisterDeprecatedDescriptor(descriptor);
#pragma warning restore CS0618
    }
}
