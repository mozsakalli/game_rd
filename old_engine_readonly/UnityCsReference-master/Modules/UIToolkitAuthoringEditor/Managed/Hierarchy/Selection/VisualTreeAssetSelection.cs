// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Properties;
using UnityEngine.UIElements;

namespace Unity.UIToolkit.Editor;

internal class VisualTreeAssetSelection : UISelectionObject
{
    public static readonly BindingId PanelComponentProperty = nameof(PanelComponent);
    public static readonly BindingId PanelSettingsProperty = nameof(PanelSettings);

    private IPanelComponent m_PanelComponent;
    private PanelSettings m_PanelSettings;

    [CreateProperty]
    public IPanelComponent PanelComponent
    {
        get => m_PanelComponent;
        set
        {
            if (m_PanelComponent == value)
                return;
            m_PanelComponent = value;
            Notify(PanelComponentProperty);
        }
    }

    [CreateProperty]
    public PanelSettings PanelSettings
    {
        get => m_PanelSettings;
        set
        {
            if (m_PanelSettings == value)
                return;
            m_PanelSettings = value;
            Notify(PanelSettingsProperty);
        }
    }
}
