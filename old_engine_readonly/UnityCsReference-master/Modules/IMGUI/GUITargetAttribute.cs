// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using System.Runtime.CompilerServices;
using UnityEngine.Scripting;

namespace UnityEngine
{
    ///<summary>Allows to control for which display the OnGUI is called.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class GUITargetAttribute : Attribute
    {
        internal int displayMask;

        ///<summary>Default constructor initializes the attribute for OnGUI to be called for all available displays.</summary>
        public GUITargetAttribute() { displayMask = -1; }

        ///<summary>Default constructor initializes the attribute for OnGUI to be called for all available displays.</summary>
        ///<param name="displayIndex">Display index.</param>
        public GUITargetAttribute(int displayIndex)
        {
            displayMask = 1 << displayIndex;
        }

        ///<summary>Default constructor initializes the attribute for OnGUI to be called for all available displays.</summary>
        ///<param name="displayIndex">Display index.</param>
        ///<param name="displayIndex1">Display index.</param>
        public GUITargetAttribute(int displayIndex, int displayIndex1)
        {
            displayMask = (1 << displayIndex) | (1 << displayIndex1);
        }

        ///<summary>Default constructor initializes the attribute for OnGUI to be called for all available displays.</summary>
        ///<param name="displayIndex">Display index.</param>
        ///<param name="displayIndex1">Display index.</param>
        ///<param name="displayIndexList">Display index list.</param>
        public GUITargetAttribute(int displayIndex, int displayIndex1, params int[] displayIndexList)
        {
            displayMask = (1 << displayIndex) | (1 << displayIndex1);
            for (int i = 0; i < displayIndexList.Length; i++)
                displayMask |= 1 << displayIndexList[i];
        }

        [RequiredByNativeCode]
        static int GetGUITargetAttrValue(Type klass, string methodName)
        {
            var method = klass.GetMethod(methodName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method != null)
            {
                object[] attrs = method.GetCustomAttributes(true);
                if (attrs != null)
                {
                    for (int i = 0; i < attrs.Length; ++i)
                    {
                        if (attrs[i].GetType() != typeof(GUITargetAttribute))
                            continue;

                        GUITargetAttribute attr = attrs[i] as GUITargetAttribute;
                        return attr.displayMask;
                    }
                }
            }

            return -1;
        }
    }
}
