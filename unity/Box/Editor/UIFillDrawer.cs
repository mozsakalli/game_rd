using UnityEditor;
using UnityEngine;

namespace Digitoy.Box.Editor
{
    /// <summary>UIFill inspector: one row (type + colour) for Solid; type + gradient + direction for Gradient.</summary>
    [CustomPropertyDrawer(typeof(UIFill))]
    sealed class UIFillDrawer : PropertyDrawer
    {
        const float TypeWidth = 76f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var type = (UIFillType)property.FindPropertyRelative("type").enumValueIndex;
            float line = EditorGUIUtility.singleLineHeight, gap = EditorGUIUtility.standardVerticalSpacing;
            return type == UIFillType.Gradient ? line * 2 + gap : line;
        }

        public override void OnGUI(Rect pos, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(pos, label, property);
            var typeProp = property.FindPropertyRelative("type");
            float line = EditorGUIUtility.singleLineHeight, gap = EditorGUIUtility.standardVerticalSpacing;

            var row = new Rect(pos.x, pos.y, pos.width, line);
            row = EditorGUI.PrefixLabel(row, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var typeRect = new Rect(row.x, row.y, TypeWidth, line);
            var valueRect = new Rect(row.x + TypeWidth + 4, row.y, row.width - TypeWidth - 4, line);
            EditorGUI.PropertyField(typeRect, typeProp, GUIContent.none);

            if ((UIFillType)typeProp.enumValueIndex == UIFillType.Solid)
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("color"), GUIContent.none);
            }
            else
            {
                EditorGUI.PropertyField(valueRect, property.FindPropertyRelative("gradient"), GUIContent.none);
                var dirRect = new Rect(row.x, row.y + line + gap, row.width, line);
                var horiz = property.FindPropertyRelative("horizontal");
                int sel = EditorGUI.Popup(dirRect, horiz.boolValue ? 1 : 0, new[] { "Vertical (top → bottom)", "Horizontal (left → right)" });
                horiz.boolValue = sel == 1;
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }
    }
}
