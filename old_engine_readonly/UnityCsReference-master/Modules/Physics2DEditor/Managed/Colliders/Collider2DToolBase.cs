// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using Unity.Scripting.LifecycleManagement;
using UnityEditor.EditorTools;
using UnityEngine;

namespace UnityEditor
{
    abstract class Collider2DToolbase : EditorTool
    {
        [NoAutoStaticsCleanup] // cached editor GUIContent icon; safe to persist across code reload as it holds no user-code references
        private static GUIContent m_EditModeButton;
        public override GUIContent toolbarIcon
        {
            get
            {
                if (m_EditModeButton == null)
                {
                    m_EditModeButton = new GUIContent(
                        EditorGUIUtility.IconContent("EditCollider").image,
                        EditorGUIUtility.TrTextContent("Edit the collider geometry.").text
                    );
                }

                return m_EditModeButton;
            }
        }
    }
}
