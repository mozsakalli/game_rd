// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;

namespace UnityEngine
{
    ///<summary>Class internally used to pass layout options into <see cref="GUILayout" /> functions. You don't use these directly, but construct them with the layouting functions in the <see cref="GUILayout" /> class.</summary>
    ///<remarks>
    ///  <see cref="GUILayout.MaxHeight" />, <see cref="GUILayout.ExpandWidth" />, <see cref="GUILayout.ExpandHeight" />.</remarks>
    ///<seealso cref="GUILayout.Width" />
    ///<seealso cref="GUILayout.Height" />
    ///<seealso cref="GUILayout.MinWidth" />
    ///<seealso cref="GUILayout.MaxWidth" />
    ///<seealso cref="GUILayout.MinHeight" />
    public sealed class GUILayoutOption
    {
        internal enum Type
        {
            fixedWidth, fixedHeight, minWidth, maxWidth, minHeight, maxHeight, stretchWidth, stretchHeight,
            // These are just for the spacing variables
            alignStart, alignMiddle, alignEnd, alignJustify, equalSize, spacing
        }
        // *undocumented*
        ///<exclude />
        internal Type type;
        // *undocumented*
        ///<exclude />
        internal object value;
        // *undocumented*
        ///<exclude />
        internal GUILayoutOption(Type type, object value)
        {
            this.type = type;
            this.value = value;
        }
    }
}
