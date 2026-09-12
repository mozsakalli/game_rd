// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>Interface implemented by both <see cref="Subsystem" /> and <see cref="IntegratedSubsystem" /> which provides control over the state of either.</summary>
    public interface ISubsystem
    {
        ///<summary>Will be true if asking the subsytem to start was successful. False in the case that the subsystem has stopped, was asked to stop or has not been started yet.</summary>
        bool running { get; }

        ///<summary>Starts an instance of a subsystem.</summary>
        ///<remarks>Once the instance is started, the subsystem representing this instance is active and can be interacted with.</remarks>
        void Start();
        ///<summary>Stops an instance of a subsystem.</summary>
        ///<remarks>Once the instance is stopped, the subsystem representing this instance is no longer active and should not consume performance.</remarks>
        void Stop();
        ///<summary>Destroys this instance of a subsystem.</summary>
        ///<remarks>Also unloads all resources acquired during initialization step. Should be called when this instance of a subsystem is no longer needed.
        ///
        ///                Note: Once a Subsystem is Destroyed, script can still hold a reference but calling a method on it will result in a NullArgumentException and output in the console.</remarks>
        void Destroy();
    }
}
