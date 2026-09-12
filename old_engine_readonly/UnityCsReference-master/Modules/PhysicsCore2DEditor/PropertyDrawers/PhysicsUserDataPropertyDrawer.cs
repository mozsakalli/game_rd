// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using UnityEditor.UIElements;
using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using static Unity.U2D.Physics.Scripting2D;

namespace Unity.U2D.Physics.Editor
{
    /// <summary>
    /// Builds the UI Toolkit fields for a <see cref="PhysicsUserData"/> serialized property.
    /// The entity id is presented as the object it references, alongside the mask, float, int, long, Vector3Int, and bool slots.
    /// Add the returned element directly to embed the fields inline, or wrap it in a <see cref="Foldout"/> for a collapsible section.
    /// </summary>
    public static class PhysicsUserDataInspector
    {
        /// <summary>
        /// Create the editable fields for a <see cref="PhysicsUserData"/> property, with no wrapping foldout.
        /// </summary>
        /// <param name="property">The serialized <see cref="PhysicsUserData"/> property to build the fields for.</param>
        /// <returns>A container with the referenced object field and the mask, float, int, long, Vector3Int, and bool fields.</returns>
        public static VisualElement CreateFields(SerializedProperty property)
        {
            var body = new VisualElement();

            // The entity id is presented as the object it references.
            var entityIdProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_EntityId));

            var objectField = new ObjectField("Object") { value = PhysicsGlobal_GetObject(entityIdProperty.entityIdValue) };
            objectField.tooltip = GetEntityTooltip(entityIdProperty.entityIdValue);
            objectField.AddToClassList(ObjectField.alignedFieldUssClassName);
            objectField.RegisterValueChangedCallback(evt =>
            {
                entityIdProperty.entityIdValue = evt.newValue != null ? evt.newValue.GetEntityId() : EntityId.None;
                entityIdProperty.serializedObject.ApplyModifiedProperties();

                // Update the tooltip.
                objectField.tooltip = GetEntityTooltip(entityIdProperty.entityIdValue);
            });
            body.Add(objectField);

            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_PhysicsMask))));
            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_Float))));
            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_Int))));
            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_Int64))));
            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_Vector3Int))));
            body.Add(new PropertyField(property.FindPropertyRelative(nameof(PhysicsUserData.m_Bool))));

            return body;
        }

        static string GetEntityTooltip(EntityId entityId)
        {
            if (entityId == EntityId.None)
                return "None";

            var obj = PhysicsGlobal_GetObject(entityId);
            if (obj == null)
                return "Invalid EntityId";

            return $"EntityId: {entityId.ToString()} - \"{obj.name}\" ({obj.GetType()})";
        }
    }

    [CustomPropertyDrawer(typeof(PhysicsUserData))]
    sealed class PhysicsUserDataPropertyDrawer : PropertyDrawer
    {
        #region UITK

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            var foldout = new Foldout { text = property.displayName, value = false, viewDataKey = typeof(PhysicsUserDataPropertyDrawer).ToString() };
            root.Add(foldout);
            foldout.Add(PhysicsUserDataInspector.CreateFields(property));
            return root;
        }

        #endregion

        #region IMGUI

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            if (!property.isExpanded)
                return EditorGUIUtility.singleLineHeight;

            return EditorGUIUtility.singleLineHeight
                + (EditorGUIUtility.standardVerticalSpacing + EditorGUIUtility.singleLineHeight) * 7;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            label = EditorGUI.BeginProperty(position, label, property);

            var foldoutRect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;

                var entityIdProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_EntityId));
                var physicsMaskProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_PhysicsMask));
                var floatProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_Float));
                var intProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_Int));
                var int64Property = property.FindPropertyRelative(nameof(PhysicsUserData.m_Int64));
                var vector3IntProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_Vector3Int));
                var boolProperty = property.FindPropertyRelative(nameof(PhysicsUserData.m_Bool));

                float y = foldoutRect.yMax + EditorGUIUtility.standardVerticalSpacing;
                var lineHeight = EditorGUIUtility.singleLineHeight;
                var spacing = EditorGUIUtility.standardVerticalSpacing;

                var obj = PhysicsGlobal_GetObject(entityIdProperty.entityIdValue);
                EditorGUI.BeginChangeCheck();
                var newObj = EditorGUI.ObjectField(new Rect(position.x, y, position.width, lineHeight), new GUIContent("Object"), obj, typeof(UnityEngine.Object), true);
                if (EditorGUI.EndChangeCheck())
                    entityIdProperty.entityIdValue = newObj != null ? newObj.GetEntityId() : EntityId.None;
                y += lineHeight + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), physicsMaskProperty, false);
                y += lineHeight + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), floatProperty, false);
                y += lineHeight + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), intProperty, false);
                y += lineHeight + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), int64Property, false);
                y += lineHeight + spacing;

                EditorGUI.PropertyField(new Rect(position.x, y, position.width, lineHeight), boolProperty, false);

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        #endregion
    }
}
