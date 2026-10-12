using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Digitoy.Box.Editor
{
    /// <summary>Shader is found by name at runtime → keep it in Always Included Shaders so builds don't strip it.</summary>
    sealed class UIBoxBuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => EnsureShaderIncluded();

        [InitializeOnLoadMethod]
        static void EnsureShaderIncluded()
        {
            var shader = Shader.Find(UIBoxMaterials.ShaderName);
            if (shader == null)
                return;
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            if (gs == null)
                return;
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            if (arr == null)
                return;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                    return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
