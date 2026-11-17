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

            // Placement pad materials (editable assets so the colours can be tuned in the Inspector).
            SerializedProperty padFill = so.FindProperty("padFillMaterial");
            SerializedProperty padEdge = so.FindProperty("padEdgeMaterial");
            if (padFill.objectReferenceValue == null || padEdge.objectReferenceValue == null)
            {
                padFill.objectReferenceValue = PadMaterial("PadFill", new Color(0.07f, 0.4f, 0.46f));
                padEdge.objectReferenceValue = PadMaterial("PadEdge", new Color(0.35f, 1.1f, 1.2f));
                changed = true;
            }

            ZombieSetup.EnsureTiny(); // tiny horde zombies (one-time change to the prefab)
            if (ZombieHierarchy.EnsureOnce()) changed = true; // "Zombies" folder in the Hierarchy (first load only)
            if (PadTool.EnsureOnce(level)) changed = true; // placement pads as individual scene objects (first load only)

            PadTool.EnsureFrontPadsOnce(level); // wall pads in front of the police cars (scenes built before the Shotgun Wall)

            // Rifleman + drag-and-drop placement (only if the soldier pack is imported and the scene has none yet).
            if (SoldierSetup.EnsureSystem(level)) changed = true;

            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            Debug.Log("[ZombieTD] Level visuals applied automatically (save the scene to keep them).");
        }

        // Flat unlit colour (ignores lighting so pads stay readable at night); created once, then yours to edit.
        static Material PadMaterial(string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
