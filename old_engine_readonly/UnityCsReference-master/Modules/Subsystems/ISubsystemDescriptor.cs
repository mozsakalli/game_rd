// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEngine
{
    ///<summary>A subsystem descriptor is metadata about a subsystem which can be inspected before loading / initializing a subsystem.</summary>
    public interface ISubsystemDescriptor
    {
        ///<summary>A unique string that identifies the subsystem that this Descriptor can create.</summary>
        string id { get; }
        ///<summary>Creates an <see cref="ISubsystem" /> from this descriptor.</summary>
        ///<returns>An instance of <see cref="ISubsystem" />.</returns>
        ISubsystem Create();
    }
}
