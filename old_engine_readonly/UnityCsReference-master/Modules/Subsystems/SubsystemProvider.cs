// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine.SubsystemsImplementation
{
    ///<summary>A provider that supplies data to a subsystem, generally for platform-specific implementations.</summary>
    ///<remarks>This is typically for use in platform-support packages.</remarks>
    public abstract class SubsystemProvider
    {
        ///<exclude />
        public bool running => m_Running;
        internal bool m_Running;
    }

    ///<exclude />
    public abstract class SubsystemProvider<TSubsystem> : SubsystemProvider
        where TSubsystem : SubsystemWithProvider, new()
    {
        ///<exclude />
        protected internal virtual bool TryInitialize() => true;
        ///<exclude />
        public abstract void Start();
        ///<exclude />
        public abstract void Stop();
        ///<exclude />
        public abstract void Destroy();
    }
}
