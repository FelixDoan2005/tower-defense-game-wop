using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Fills in the level's generated looks automatically whenever a LevelGenerator loads in the editor:
    /// city wall materials and the kit road floor are created and assigned if they are missing.
    /// Anything you've already assigned yourself is left alone.
    /// </summary>
    static class AutoApplyVisuals
    {

        [InitializeOnLoadMethod]
        static void Register() => LevelGenerator.EditorAutoAssign = Apply;

        static void Apply(LevelGenerator level)
        {
            var so = new SerializedObject(level);
            bool changed = false;

            // Wall variants + roof.
            SerializedProperty variants = so.FindProperty("wallVariants");
            SerializedProperty roof = so.FindProperty("wallTopMaterial");
            if (variants.arraySize == 0 || roof.objectReferenceValue == null)
            {
                CityWallBuilder.Result city = CityWallBuilder.EnsureMaterials(false);
                variants.arraySize = city.facades.Length;
                for (int i = 0; i < city.facades.Length; i++) variants.GetArrayElementAtIndex(i).objectReferenceValue = city.facades[i];
                so.FindProperty("wallMaterial").objectReferenceValue = city.facades[0];
                roof.objectReferenceValue = city.roof;
                changed = true;
            }

            // Kit road floor.
            SerializedProperty floor = so.FindProperty("floorMaterial");
            if (floor.objectReferenceValue == null)
            {
                Material m = RoadFloorSetup.EnsureMaterial();
                if (m != null) { floor.objectReferenceValue = m; changed = true; }
            }

            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            Debug.Log("[ZombieTD] Level visuals applied automatically (save the scene to keep them).");
        }
    }
}
