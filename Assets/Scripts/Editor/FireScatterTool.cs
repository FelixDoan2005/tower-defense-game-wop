using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Tools > Fire. Scatters the Vefects fire prefabs over the level as ordinary, saved scene objects under a "Fires"
    /// root: floor fires on the road, flames pouring out of facade windows (at the same spots as the fire-glow lights),
    /// and smoky fires on the back of the roofs. Move, scale, duplicate or delete any of them afterwards.
    /// </summary>
    public static class FireScatterTool
    {
        const string Dir = "Assets/Vefects/Free Fire VFX URP/Particles/";

        // Counts and sizes (the camera is ~100+ units up, so effects are scaled up to read from there).
        // Placement is random on every run (Reset gives a fresh pattern): loose clusters plus a few strays.
        const int GroundFires = 10;
        const float GroundScale = 1.6f;
        const float FacadeScale = 2.5f;
        const float FacadeChance = 0.32f; // chance that any fire-glow window actually has flames
        const int RoofFires = 2;          // roofs stay mostly clear: that's where the soldiers go
        const float RoofScale = 3f;

        static System.Random rng;

        [MenuItem("Tools/Fire/Scatter Fire (only if none exists)")]
        static void Scatter() => Run(false);

        [MenuItem("Tools/Fire/Reset Fire (deletes your changes)")]
        static void Reset() => Run(true);

        static void Run(bool reset)
        {
            GameObject existing = GameObject.Find("Fires");
            if (existing != null && !reset)
            {
                Selection.activeGameObject = existing;
                Debug.Log("[ZombieTD] Fires already exist; selected. Use Reset Fire to rebuild them.");
                return;
            }

            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (level == null) { Debug.LogError("[ZombieTD] No LevelGenerator in the open scene."); return; }
            if (!HasPack()) { Debug.LogError($"[ZombieTD] Fire pack not found at {Dir}"); return; }

            if (existing != null) Undo.DestroyObjectImmediate(existing);

            rng = new System.Random(System.Environment.TickCount);
            LevelData data = level.Level;
            // A fresh, unblocked grid so police cars don't distort where the road and roofs are.
            MazeGrid grid = LevelLayouts.Build(data, level.transform.position);
            Vector3 origin = level.transform.position;

            var root = new GameObject("Fires").transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Scatter fire");
            int count = 0;
            count += Ground(root, grid, origin);
            count += Facades(root, grid, data, origin);
            count += Roofs(root, grid, data, origin);

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log($"[ZombieTD] {count} fires placed. Move or delete any of them, then save the scene (Ctrl+S).");
        }

        // ---------------------------------------------------------------- ground

        static int Ground(Transform root, MazeGrid g, Vector3 origin)
        {
            if (!RoadBounds(g, out int minX, out int minZ, out int maxX, out int maxZ)) return 0;
            bool alongX = (maxX - minX) >= (maxZ - minZ);
            float cs = g.CellSize;
            float aMin = (alongX ? minX : minZ) * cs, aMax = ((alongX ? maxX : maxZ) + 1) * cs;
            float cMin = (alongX ? minZ : minX) * cs, cMax = ((alongX ? maxZ : maxX) + 1) * cs;
            float margin = cs * 0.6f;

            Transform group = Group(root, "GroundFires");
            string[] names = { "VFX_Fire_Floor_01", "VFX_Fire_Floor_02", "VFX_Fire_01_Small", "VFX_Fire_Floor_01_Smoke", "VFX_Fire_Floor_02_Smoke" };
            // One or two loose clusters (a wreck burning) plus strays, never evenly spaced.
            int clusters = 1 + rng.Next(2);
            var centres = new float[clusters];
            for (int c = 0; c < clusters; c++) centres[c] = Mathf.Lerp(0.15f, 0.85f, R());

            int placed = 0;
            for (int i = 0; i < GroundFires; i++)
            {
                bool stray = R() < 0.35f;
                float t = stray ? Mathf.Lerp(0.06f, 0.94f, R()) : Mathf.Clamp(centres[rng.Next(clusters)] + (R() - 0.5f) * 0.14f, 0.05f, 0.95f);
                float along = Mathf.Lerp(aMin, aMax, t);
                float across = Mathf.Lerp(cMin + margin, cMax - margin, R());
                Vector3 p = alongX ? new Vector3(along, 0.03f, across) : new Vector3(across, 0.03f, along);
                if (Spawn(group, names[rng.Next(names.Length)], origin + p, GroundScale * Mathf.Lerp(0.6f, 1.5f, R()), R() * 360f)) placed++;
            }
            return placed;
        }

        // ---------------------------------------------------------------- facades

        static int Facades(Transform root, MazeGrid g, LevelData data, Vector3 origin)
        {
            Transform group = Group(root, "FacadeFires");
            string[] names = { "VFX_Fire_01_Big_Smoke", "VFX_Fire_01_Medium_Smoke", "VFX_Fire_01_Medium_Simple", "VFX_Fire_01_Small_Smoke", "VFX_Fire_01_Big_Simple" };
            float cs = g.CellSize;

            // Collect every road-facing facade tile, grouped into straight lines (one per wall face along the road).
            var lines = new Dictionary<long, List<(int along, Vector3 center, Vector2Int normal)>>();
            for (int z = 0; z < g.Height; z++)
            for (int x = 0; x < g.Width; x++)
            {
                if (g.IsWalkable(x, z)) continue;
                foreach (Vector2Int d in new[] { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) })
                {
                    if (!g.IsWalkable(x + d.x, z + d.y)) continue;
                    bool faceAlongZ = d.x != 0; // a face looking along X runs along Z
                    long key = ((long)(d.x + 1) * 3 + (d.y + 1)) * 100000 + (faceAlongZ ? x : z);
                    if (!lines.TryGetValue(key, out var list)) lines[key] = list = new List<(int, Vector3, Vector2Int)>();
                    list.Add((faceAlongZ ? z : x, new Vector3((x + 0.5f + d.x * 0.5f) * cs, 0f, (z + 0.5f + d.y * 0.5f) * cs), d));
                }
            }

            int placed = 0;
            float maxY = Mathf.Max(4f, data.wallHeight - 5f); // keep flames below the roofline
            foreach (var kv in lines)
            {
                var segs = kv.Value;
                segs.Sort((a, b) => a.along.CompareTo(b.along));
                int first = segs[0].along, last = segs[segs.Count - 1].along;

                // A few burning stretches per facade, each a random length, with unburnt gaps in between.
                int zones = 1 + rng.Next(3);
                var zoneStart = new int[zones];
                var zoneLen = new int[zones];
                for (int i = 0; i < zones; i++)
                {
                    zoneStart[i] = first + rng.Next(Mathf.Max(1, last - first + 1));
                    zoneLen[i] = 2 + rng.Next(5);
                }

                foreach (var s in segs)
                {
                    bool burning = false;
                    for (int i = 0; i < zones; i++) if (s.along >= zoneStart[i] && s.along < zoneStart[i] + zoneLen[i]) burning = true;
                    if (R() > (burning ? FacadeChance * 2f : FacadeChance * 0.12f)) continue;

                    int fires = 1 + (R() < 0.3f ? 1 : 0);
                    for (int f = 0; f < fires; f++)
                    {
                        Vector3 tangent = new Vector3(s.normal.y, 0f, s.normal.x);
                        Vector3 p = s.center + new Vector3(s.normal.x, 0f, s.normal.y) * 0.5f
                                  + tangent * ((R() - 0.5f) * cs)
                                  + Vector3.up * Mathf.Lerp(2f, maxY, R() * R()); // more fires low down, some high up
                        if (Spawn(group, names[rng.Next(names.Length)], origin + p, FacadeScale * Mathf.Lerp(0.5f, 1.5f, R()), R() * 360f)) placed++;
                    }
                }
            }
            return placed;
        }

        // ---------------------------------------------------------------- roofs

        // Smoky fires on the back row of the roof, behind the tiles where soldiers and turrets stand.
        static int Roofs(Transform root, MazeGrid g, LevelData data, Vector3 origin)
        {
            Transform group = Group(root, "RoofFires");
            float cs = g.CellSize;

            // Every back-row roof tile is a candidate; pick a few at random.
            var candidates = new List<Vector2Int>();
            for (int z = 0; z < g.Height; z++)
            for (int x = 0; x < g.Width; x++)
                if (!g.IsWalkable(x, z) && NearRoad(g, x, z, 3) && !NearRoad(g, x, z, 2)) candidates.Add(new Vector2Int(x, z));

            string[] names = { "VFX_Fire_01_Big_Smoke", "VFX_Fire_01_Medium_Smoke" };
            int placed = 0;
            for (int i = 0; i < RoofFires && candidates.Count > 0; i++)
            {
                int pick = rng.Next(candidates.Count);
                Vector2Int c = candidates[pick];
                candidates.RemoveAt(pick);
                Vector3 p = new Vector3((c.x + 0.5f + (R() - 0.5f) * 0.6f) * cs, data.wallHeight, (c.y + 0.5f + (R() - 0.5f) * 0.6f) * cs);
                if (Spawn(group, names[rng.Next(names.Length)], origin + p, RoofScale * Mathf.Lerp(0.7f, 1.3f, R()), R() * 360f)) placed++;
            }
            return placed;
        }

        // ---------------------------------------------------------------- helpers

        static bool Spawn(Transform parent, string prefabName, Vector3 worldPos, float scale, float yaw)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}{prefabName}.prefab");
            if (prefab == null) { Debug.LogWarning($"[ZombieTD] Missing fire prefab: {prefabName}"); return false; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            return true;
        }

        static Transform Group(Transform root, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static bool HasPack() => AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "VFX_Fire_01_Big.prefab") != null;

        static bool NearRoad(MazeGrid g, int x, int z, int reach)
        {
            for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
                if (g.IsWalkable(x + dx, z + dz)) return true;
            return false;
        }

        static bool RoadBounds(MazeGrid g, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = int.MaxValue;
            maxX = maxZ = int.MinValue;
            for (int z = 0; z < g.Height; z++)
            for (int x = 0; x < g.Width; x++)
            {
                if (!g.IsWalkable(x, z)) continue;
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
            }
            return maxX >= minX;
        }

        static float R() => (float)rng.NextDouble();
    }
}
