using UnityEditor;
using UnityEngine;
using ZombieTD.Core;

namespace ZombieTD.EditorTools
{
    /// <summary>Draws the linked component's settings right inside this object's Inspector.</summary>
    [CustomEditor(typeof(InspectorLink))]
    public class InspectorLinkEditor : Editor
    {
        Editor targetEditor;

        void OnDisable()
        {
            if (targetEditor != null) DestroyImmediate(targetEditor);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("note"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("target"));
            serializedObject.ApplyModifiedProperties();

            Component linked = ((InspectorLink)target).Target;
            if (linked == null)
            {
                EditorGUILayout.HelpBox("Link a component here to edit its settings from this object.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            bool isAsset = AssetDatabase.Contains(linked);
            EditorGUILayout.LabelField(isAsset ? $"Settings (saved in the '{linked.gameObject.name}' prefab)" : "Settings (on the scene object)",
                EditorStyles.boldLabel);

            CreateCachedEditor(linked, null, ref targetEditor);
            EditorGUI.BeginChangeCheck();
            targetEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck() && isAsset) EditorUtility.SetDirty(linked); // make sure prefab edits get saved
        }
    }
}
