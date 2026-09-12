// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

namespace UnityEditor;

/// <summary>
/// Interface for Derived platform SDK platform providers to implement.
/// </summary>
public interface IPlatformProvider
{
    /// <summary>
    /// The version of the platform provider.
    /// </summary>
    int version { get; }
}

/// <summary>
/// Class representing a preconfigured settings variant for a Derived platform SDK platform.
/// </summary>
public class SDKPreconfiguredSettingsVariant
{
    public string displayName { get; }
    public string description { get; }
    public string tooltip { get; }
    public bool selectedInitially { get; }
    public UnityEngine.GUID platformGuid { get; }

    public SDKPreconfiguredSettingsVariant(string displayName, string description, string tooltip, bool selectedInitially = false, UnityEngine.GUID platformGuid = default)
    {
        this.displayName = displayName;
        this.description = description;
        this.tooltip = tooltip;
        this.selectedInitially = selectedInitially;
        this.platformGuid = platformGuid;
    }
}
