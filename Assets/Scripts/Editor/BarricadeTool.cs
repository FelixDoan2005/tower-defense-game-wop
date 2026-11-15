using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Tools > Police Barricade. Creates the starting layout of police cars as ordinary, saved scene objects under a
    /// "PoliceBarricade" root. After that you move/rotate/duplicate/delete the cars yourself; the game blocks the
    /// road tiles under whatever cars are there.
    /// </summary>
    public static class BarricadeTool
    {
        const string CarPath = "Assets/Police Car & Helicopter/Prefabs/Interceptor.prefab";

        [MenuItem("Tools/Police Barricade/Build (only if none exists)")]
        static void Build() => Run(false);

        [MenuItem("Tools/Police Barricade/Reset to default layout (deletes your moves)")]
        static void Reset() => Run(true);

        static void Run(bool reset)
        {
            var existing = Object.FindAnyObjectByType<PoliceBarricade>();
            if (existing != null && !reset)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ZombieTD] A PoliceBarricade already exists; it was selected. Move its cars freely. " +
                          "Use Reset to default layout to rebuild it.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarPath);
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (prefab == null) { Debug.LogError($"[ZombieTD] Police car prefab not found at {CarPath}"); return; }
            if (level == null) { Debug.LogError("[ZombieTD] No LevelGenerator in the open scene."); return; }

            if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
            Debug.Log($"[ZombieTD] Police car materials: {KitMaterialConverter.ConvertFolder("Assets/Police Car & Helicopter")}");

            level.Generate(); // fresh grid so the plan isn't distorted by tiles blocked earlier
            LevelData data = level.Level;
            var plans = LevelBarricade.Plan(level.Grid, data);
            if (plans.Count == 0) { Debug.LogWarning("[ZombieTD] Couldn't plan a barricade for this level."); return; }

            var root = new GameObject("PoliceBarricade");
            root.AddComponent<PoliceBarricade>();
            Undo.RegisterCreatedObjectUndo(root, "Build police barricade");
            root.transform.position = Vector3.zero;

            for (int i = 0; i < plans.Count; i++)
            {
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                car.name = $"PoliceCar_{i}";
                car.transform.position = level.transform.position + plans[i].localPosition;
                car.transform.rotation = Quaternion.Euler(0f, plans[i].yaw, 0f);
                car.transform.localScale = Vector3.one * data.barricadeCarScale;
                if (data.barricadeStrobes) AddStrobes(car.transform, data.barricadeCarScale);
            }

            level.Generate(); // preview with the new cars blocking their tiles
            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[ZombieTD] Police barricade built: {plans.Count} cars. Move them freely, then save the scene (Ctrl+S).");
        }

        // Red and blue lights above the roof, flashing out of step.
        static void AddStrobes(Transform car, float scale)
        {
            Color red = new Color(1f, 0.08f, 0.05f), blue = new Color(0.1f, 0.25f, 1f);
            for (int i = 0; i < 2; i++)
            {
                var go = new GameObject(i == 0 ? "StrobeRed" : "StrobeBlue");
                go.transform.SetParent(car, false);
                go.transform.localPosition = new Vector3(i == 0 ? -0.45f : 0.45f, 1.7f, 0.1f);
                go.transform.localScale = Vector3.one / Mathf.Max(0.01f, scale); // light size independent of car scale
                Light l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 11f;
                l.intensity = 7f;
                l.color = i == 0 ? red : blue;
                l.shadows = LightShadows.None;
                go.AddComponent<LightStrobe>().Configure(i == 0 ? red : blue, Color.black, i == 0 ? 0f : 0.5f, 7f);
            }
        }
    }
}
