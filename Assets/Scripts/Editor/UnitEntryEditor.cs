using UnityEditor;
using UnityEngine;
using ZombieTD.Towers;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Inspector for a unit in the Hierarchy: shows the unit's stats inline so they can be edited without hunting for
    /// the asset. Changes are saved into the unit's WeaponData asset.
    /// </summary>
    [CustomEditor(typeof(UnitEntry))]
    public class UnitEntryEditor : Editor
    {
        Editor statsEditor;

        void OnDisable()
        {
            if (statsEditor != null) DestroyImmediate(statsEditor);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("data"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("inTray"));
            serializedObject.ApplyModifiedProperties();

            WeaponData data = ((UnitEntry)target).Data;
            if (data == null)
            {
                EditorGUILayout.HelpBox("Assign this unit's WeaponData asset (Assets/Settings/Units).", MessageType.Info);
                return;
            }

            EditorGUILayout.Space();
            float dps = data.damage * data.fireRate;
            int perPad = data.slotCapacity <= 1 ? 1 : ZombieTD.Maze.WallSlot.DefaultCapacity; // soldiers share a pad (20 by default)
            EditorGUILayout.LabelField($"About {dps:0.#} damage per second per soldier  |  {dps * perPad:0} for a pad full of them " +
                                       $"({perPad})", EditorStyles.boldLabel);
            if (data.magazineSize > 0)
            {
                // Reloading: firing time per magazine vs reload time.
                float firing = data.magazineSize / Mathf.Max(0.01f, data.fireRate);
                float uptime = firing / (firing + data.reloadTime);
                EditorGUILayout.LabelField($"Fires {firing:0.#} s, reloads {data.reloadTime:0.#} s  ->  about {dps * uptime:0.#} per soldier sustained " +
                                           $"({uptime * 100f:0}% of the time firing)", EditorStyles.miniLabel);
            }
            if (data.spreadDegrees > 0f)
                EditorGUILayout.LabelField("(Less in practice: spread bullets can miss.)", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Stats (saved in the unit's asset)", EditorStyles.boldLabel);
            CreateCachedEditor(data, null, ref statsEditor);
            statsEditor.OnInspectorGUI();
        }
    }
}
