using UnityEditor;

namespace Digitoy.Box.Editor
{
    /// <summary>Hides Graphic's Color/Material/OnCullStateChanged (unused by UIBox); everything else default.</summary>
    [CustomEditor(typeof(UIBox)), CanEditMultipleObjects]
    sealed class UIBoxEditor : UnityEditor.Editor
    {
        static readonly string[] Hidden = { "m_Script", "m_Color", "m_Material", "m_OnCullStateChanged" };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, Hidden);
            serializedObject.ApplyModifiedProperties();
        }
    }
}
