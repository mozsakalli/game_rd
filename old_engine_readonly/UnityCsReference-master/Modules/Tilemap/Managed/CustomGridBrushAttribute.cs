// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    ///<summary>Attribute to define the class as a grid brush and to make it available in the palette window.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class CustomGridBrushAttribute : Attribute
    {
        private bool m_HideAssetInstances;
        private bool m_HideDefaultInstance;
        private bool m_DefaultBrush;
        private string m_DefaultName;

        ///<summary>Hide all asset instances of this brush in the tile palette window.</summary>
        public bool hideAssetInstances
        {
            get { return m_HideAssetInstances; }
        }

        ///<summary>Hide the default instance of brush in the tile palette window.</summary>
        ///<remarks>In addition to asset instances of brush class, Unity creates a default instance of every brush to be shown in the palette window dropdown. When hideDefaultInstance=true, the default instance will not be created.</remarks>
        public bool hideDefaultInstance
        {
            get { return m_HideDefaultInstance; }
        }

        ///<summary>If set to true, brush will replace Unity built-in brush as the default brush in palette window.
        ///
        ///Only one class at any one time should set defaultBrush to true.</summary>
        public bool defaultBrush
        {
            get { return m_DefaultBrush; }
        }

        ///<summary>Name of the default instance of this brush.</summary>
        ///<remarks>In addition to asset instances of brush class, Unity creates a default instance of every brush to be shown in the palette window dropdown. This is the display name of that instance.
        ///
        ///If the defaultName is not set, the type name will be used by default.</remarks>
        public string defaultName
        {
            get { return m_DefaultName; }
        }

        ///<summary>Attribute to define the class as a grid brush and to make it available in the palette window.</summary>
        public CustomGridBrushAttribute()
        {
            m_HideAssetInstances = false;
            m_HideDefaultInstance = false;
            m_DefaultBrush = false;
            m_DefaultName = "";
        }

        ///<summary>Attribute to define the class as a grid brush and to make it available in the palette window.</summary>
        ///<param name="defaultBrush">If set to true, brush will replace Unity built-in brush as the default brush in palette window.</param>
        ///<param name="defaultName">Name of the default instance of this brush.</param>
        ///<param name="hideAssetInstances">Hide all asset instances of this brush in the tile palette window.</param>
        ///<param name="hideDefaultInstance">Hide the default instance of brush in the tile palette window.</param>
        public CustomGridBrushAttribute(bool hideAssetInstances, bool hideDefaultInstance, bool defaultBrush, string defaultName)
        {
            this.m_HideAssetInstances = hideAssetInstances;
            this.m_HideDefaultInstance = hideDefaultInstance;
            this.m_DefaultBrush = defaultBrush;
            this.m_DefaultName = defaultName;
        }
    }
}
