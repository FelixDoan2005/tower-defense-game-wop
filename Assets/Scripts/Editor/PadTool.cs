using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Tools > Pads. Creates every placement pad as its own scene object (a prefab instance with a WallSlot) under a
    /// "PlacementPadSet" root. Move, rotate, duplicate or delete any pad in the Scene view; when the scene has pads of
    /// its own, the level uses exactly those and never regenerates them. With no pads, a default set is generated.
    /// </summary>
    public static class PadTool
    {
        const string Dir = "Assets/Prefabs/Pads";
        const string PrefabPath = Dir + "/PlacementPad.prefab";
        const string MeshPath = Dir + "/PadPlate.asset";
        const string RootName = "PlacementPadSet";
        const float GroundLift = 0.08f;

        [MenuItem("Tools/Pads/Build Editable Pads (only if none exist)")]
        static void Build() => Run(false);

        [MenuItem("Tools/Pads/Reset Pads to default layout (deletes your changes)")]
        static void Reset() => Run(true);

        /// <summary>Builds the pads the first time a level loads in a project. Returns true if it did.</summary>
        public static bool EnsureOnce(LevelGenerator level)
        {
            string key = "ZombieTD.PadsBuilt." + Application.dataPath;
            if (EditorPrefs.GetBool(key, false)) return false;
            EditorPrefs.SetBool(key, true); // set first, so a failure can't make this loop on every reload
            if (HasAuthoredPads(level)) return false;
            return Run(false, level);
        }

        // Wall pads: a line across the road just in front of the police cars (zombie side), each about half the corridor,
        // side by side so they never overlap. The Shotgun Wall goes on these.
        const int WallLayers = 3;
        const float WallLayerGap = 2.8f, WallTargetWidth = 10f, WallGap = 0.3f;
        const float WallDepth = WallLayers * WallLayerGap + 0.4f; // three layers, one behind the other

        static int AddFrontPads(GameObject prefab, Transform root, LevelGenerator level, MazeGrid grid)
        {
            var barricade = Object.FindAnyObjectByType<PoliceBarricade>();
            if (barricade == null) return 0;
            LevelData data = level.Level;
            Vector3 origin = level.transform.position;
            int n = 0;
            foreach (PoliceBarricade.GroundSlot g in barricade.PlanWallLine(grid, data, WallDepth, WallTargetWidth, out float width))
            {
                Spawn(prefab, root, $"Pad_CarFront_{++n:00}", origin + g.localCentre + Vector3.up * GroundLift,
                    new Vector3(g.facing.x, 0f, g.facing.y), WallDepth * 0.5f - 1.2f, true, width * 0.5f, width - WallGap); // front layer near the zombie side
                var so = new SerializedObject(root.GetChild(root.childCount - 1).GetComponent<WallSlot>());
                so.FindProperty("kind").enumValueIndex = (int)PadKind.Wall;
                so.FindProperty("capacity").intValue = WallLayers;
                so.FindProperty("columns").intValue = 1;          // one wall per row, rows behind each other
                so.FindProperty("spacing").floatValue = WallLayerGap;
                so.FindProperty("plateDepth").floatValue = WallDepth;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return n;
        }

        /// <summary>
        /// Lays out the wall pads in the scene's pad set (once per layout version): removes the old wall pads and
        /// builds a clean line of them. Move them freely afterwards.
        /// </summary>
        public static void EnsureFrontPadsOnce(LevelGenerator level)
        {
            string key = "ZombieTD.FrontPadLine3." + Application.dataPath;
            if (EditorPrefs.GetBool(key, false)) return;
            GameObject root = GameObject.Find(RootName);
            if (root == null || level == null || level.Level == null) return; // pads not built yet: Run() adds them
            EditorPrefs.SetBool(key, true);

            foreach (WallSlot s in root.GetComponentsInChildren<WallSlot>(true))
                if (s.Kind == PadKind.Wall) Object.DestroyImmediate(s.gameObject);

            GameObject prefab = EnsurePrefab();
            if (prefab == null) return;
            MazeGrid grid = LevelLayouts.Build(level.Level, level.transform.position);
            int n = AddFrontPads(prefab, root.transform, level, grid);
            if (n > 0)
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
                Debug.Log($"[ZombieTD] {n} wall pads laid out across the road in front of the police cars (Pad_CarFront_##). Save the scene.");
            }
        }

        static void Run(bool reset) => Run(reset, Object.FindAnyObjectByType<LevelGenerator>());

        static bool Run(bool reset, LevelGenerator level)
        {
            if (level == null) { Debug.LogError("[ZombieTD] No LevelGenerator in the open scene."); return false; }

            GameObject existing = GameObject.Find(RootName);
            if (existing != null && !reset)
            {
                Selection.activeGameObject = existing;
                Debug.Log("[ZombieTD] Pads already exist; selected. Use Reset Pads to default layout to rebuild them.");
                return false;
            }
            if (existing != null) Undo.DestroyObjectImmediate(existing);

            GameObject prefab = EnsurePrefab();
            if (prefab == null) return false;

            LevelData data = level.Level;
            MazeGrid grid = LevelLayouts.Build(data, level.transform.position); // fresh grid: police cars don't distort the plan
            Vector3 origin = level.transform.position;

            var root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Build pads");

            // Roof pads.
            int n = 0;
            foreach (PlacementPads.PadInfo p in PlacementPads.Plan(grid, data))
            {
                Vector3 pos = origin + PlacementPads.MountLocal(grid, data, p) + Vector3.up * (data.wallHeight + PlacementPads.Lift);
                Spawn(prefab, root, $"Pad_Roof_{++n:00}", pos, new Vector3(p.toRoad.x, 0f, p.toRoad.y),
                    PlacementPads.FrontOffset(grid, data), false, 2f, data.padSize);
            }

            // Pads on the road behind each police car.
            int cars = 0;
            var barricade = Object.FindAnyObjectByType<PoliceBarricade>();
            if (barricade != null)
            {
                float half = PlacementPads.HalfPad(grid, data);
                foreach (PoliceBarricade.GroundSlot g in barricade.PlanSlots(grid, data))
                    Spawn(prefab, root, $"Pad_Car_{++cars:00}", origin + g.localCentre + Vector3.up * GroundLift,
                        new Vector3(g.facing.x, 0f, g.facing.y), PlacementPads.FrontOffset(grid, data), true, half + 0.4f, data.padSize);
            }

            int fronts = AddFrontPads(prefab, root, level, grid);

            level.Generate(); // refresh the preview: it now shows these pads instead of generating its own
            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log($"[ZombieTD] {n} roof pads, {cars} car pads and {fronts} wall pads created under '{RootName}'. " +
                      "Move/rotate/duplicate/delete them freely, then save the scene (Ctrl+S).");
            return true;
        }

        static void Spawn(GameObject prefab, Transform root, string name, Vector3 position, Vector3 forward,
            float frontOffset, bool ground, float hitHalf, float plateSize)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.name = name;
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(forward);

            var slot = go.GetComponent<WallSlot>();
            var so = new SerializedObject(slot);
            so.FindProperty("frontOffset").floatValue = frontOffset;
            so.FindProperty("ground").boolValue = ground;
            so.FindProperty("hitHalf").floatValue = hitHalf;
            so.FindProperty("plateSize").floatValue = plateSize;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static bool HasAuthoredPads(LevelGenerator level)
        {
            foreach (WallSlot s in Object.FindObjectsByType<WallSlot>(FindObjectsSortMode.None))
                if (!s.transform.IsChildOf(level.transform)) return true;
            return false;
        }

        // ---------------------------------------------------------------- prefab + plate mesh

        static GameObject EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Prefabs", "Pads");
            Mesh mesh = EnsureMesh();

            var root = new GameObject("PlacementPad");
            var plate = new GameObject("Plate");
            plate.transform.SetParent(root.transform, false);
            plate.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = plate.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { PadMaterial("PadFill", new Color(0.07f, 0.4f, 0.46f)), PadMaterial("PadEdge", new Color(0.35f, 1.1f, 1.2f)) };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var slot = root.AddComponent<WallSlot>();
            slot.Configure(1f, false, 2f, 2.6f, plate.transform);

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        // A unit-size plate: a fill square with a border ring. The pad's plate size scales it.
        static Mesh EnsureMesh()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (existing != null) return existing;

            const float half = 0.5f, edge = 0.2f / 2.6f, inner = half - edge;
            var verts = new List<Vector3>();
            var fill = new List<int>();
            var border = new List<int>();
            Quad(verts, fill, -inner, -inner, inner, inner);
            Quad(verts, border, -half, -half, half, -inner);
            Quad(verts, border, -half, inner, half, half);
            Quad(verts, border, -half, -inner, -inner, inner);
            Quad(verts, border, inner, -inner, half, inner);

            var mesh = new Mesh { name = "PadPlate" };
            mesh.SetVertices(verts);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(fill, 0);
            mesh.SetTriangles(border, 1);
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, MeshPath);
            return mesh;
        }

        static void Quad(List<Vector3> verts, List<int> tris, float x0, float z0, float x1, float z1)
        {
            int i = verts.Count;
            verts.Add(new Vector3(x0, 0f, z0));
            verts.Add(new Vector3(x0, 0f, z1));
            verts.Add(new Vector3(x1, 0f, z1));
            verts.Add(new Vector3(x1, 0f, z0));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }

        // Same flat unlit materials the generated pads use (created once, then yours to edit).
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
