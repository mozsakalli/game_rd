// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    ///<summary>A Subsystem is initialized from a <see cref="UnityEngine.SubsystemsImplementation.SubsystemDescriptorWithProvider" /> for a given Subsystem (Example, Input, Display, etc.) and provides an interface to interact with that given Subsystem until it is Destroyed. After a Subsystem is created it can be Started or Stopped to turn on and off functionality (and improve performance). The base type for subsystems only exposes this functionality; this class is designed to be a base class for derived classes that expose more functionality specific to a given Subsystem.
    ///
    ///            Note: initializing a second Subsystem from the same SubsystemDescriptor will return a reference to the existing Subsystem as only one Subsystem is currently allowed for a single Subsystem provider.
    ///
    ///This subsystem base-class is deprecated. If you are creating a new subsystem type, derive from <see cref="UnityEngine.SubsystemsImplementation.SubsystemWithProvider" /> instead.</summary>
    [Obsolete("Use SubsystemWithProvider instead.", false)]
    public abstract class Subsystem : ISubsystem
    {
        ///<summary>Whether or not the subsystem is running.</summary>
        abstract public bool running { get; }

        ///<summary>Starts an instance of a subsystem.</summary>
        ///<remarks>Once the instance is started, the subsystem representing this instance is active and can be interacted with.</remarks>
        abstract public void Start();
        ///<summary>Stops an instance of a subsystem.</summary>
        ///<remarks>Once the instance is stopped, the subsystem representing this instance is no longer active and should not consume performance.</remarks>
        abstract public void Stop();

        ///<summary>Destroys this instance of a subsystem.</summary>
        ///<remarks>Also unloads all resources acquired during initialization step. Should be called when this instance of a subsystem is no longer needed.
        ///
        ///                Note: Once a Subsystem is Destroyed, script can still hold a reference but calling a method on it will result in a NullArgumentException and output in the console.</remarks>
        public void Destroy()
        {
            if (SubsystemManager.RemoveDeprecatedSubsystem(this))
                OnDestroy();
        }

        abstract protected void OnDestroy();

        internal ISubsystemDescriptor m_SubsystemDescriptor;
    }

    ///<exclude />
    [Obsolete("Use SubsystemWithProvider<> instead.", false)]
    public abstract class Subsystem<TSubsystemDescriptor>
#pragma warning disable CS0618
        : Subsystem
#pragma warning restore CS0618
        where TSubsystemDescriptor : ISubsystemDescriptor
    {
        ///<summary>Returns the <see cref="SubsystemDescriptor" /> for the given <see cref="Subsystem" />.</summary>
        public TSubsystemDescriptor SubsystemDescriptor => (TSubsystemDescriptor)m_SubsystemDescriptor;
    }
}
