using System.Collections.Generic;
using UnityEngine;
using ZombieTD.Core;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Builds the current level at startup from a LevelData: grid, one combined fortress mesh
    /// (plain walls), ground, spawn/goal markers and WallSlots.
    /// The level comes from levelOverride if set, otherwise LevelCatalog[LevelProgress.CurrentIndex].
    /// Keep this object unrotated and unscaled; its position is the grid's min corner.
    /// In edit mode it also builds a preview (Level Override, else the first catalog level) that is never
    /// saved into the scene and refreshes whenever this component or a LevelData asset changes.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(-200)]
    public class LevelGenerator : MonoBehaviour
    {
        const string RootName = "Generated";

        [SerializeField] LevelCatalog catalog;
        [SerializeField, Tooltip("If set, always builds this level (handy while iterating on one level).")]
        LevelData levelOverride;

        [Header("Materials (fallbacks are created if empty)")]
        [SerializeField, Tooltip("Facade styles; each building along the road picks one.")] Material[] wallVariants;
        [SerializeField, Tooltip("Used only if Wall Variants is empty.")] Material wallMaterial;
        [SerializeField, Tooltip("Roofs and parapets.")] Material wallTopMaterial;
        [SerializeField] Material groundMaterial;
        [SerializeField, Tooltip("Kit floor sheet material for the patterned road (see Tools > Build Kit Road Floor).")] Material floorMaterial;        [SerializeField, Tooltip("Placement pad fill (flat colour, ignores lighting so pads read at night).")] Material padFillMaterial;
        [SerializeField, Tooltip("Placement pad border.")] Material padEdgeMaterial;
        [SerializeField] Material spawnMaterial;
        [SerializeField] Material goalMaterial;
        [SerializeField] Material gateMaterial;

        const float GateThickness = 1.2f;

        MazeGrid grid;
        LevelData level;
        readonly List<WallSlot> slotList = new List<WallSlot>(64);
        Transform slotRoot;
        bool usingAuthoredPads;

        /// <summary>The level data grid. Generates on first access if needed.</summary>
        public MazeGrid Grid
        {
            get
            {
                if (grid == null) Generate();
                return grid;
            }
        }

        public LevelData Level
        {
            get
            {
                if (level == null) Generate();
                return level;
            }
        }

        public float WallHeight => Level.wallHeight;
        public float CellSize => Level.cellSize;

        void Awake()
        {
            // Always rebuild on Play so the edit-mode preview never leaks into gameplay.
            if (Application.isPlaying) Generate();
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            level = ResolveLevel();
            if (Application.isPlaying) LevelProgress.CurrentName = level.displayName;
            grid = LevelLayouts.Build(level, transform.position);
            BuildVisuals();
#if UNITY_EDITOR
            if (!Application.isPlaying) MarkDontSave(transform.Find(RootName));
#endif
        }

#if UNITY_EDITOR
        /// <summary>Set by editor tooling: fills in missing materials before the preview builds.</summary>
        public static System.Action<LevelGenerator> EditorAutoAssign;

        bool previewQueued;

        void OnEnable()
        {
            if (Application.isPlaying) return;
            LevelData.EditorChanged += OnLevelDataChanged;
            QueuePreview();
        }

        void OnDisable() => LevelData.EditorChanged -= OnLevelDataChanged;

        void OnValidate() => QueuePreview(); // a field on this component changed

        void OnLevelDataChanged(LevelData changed)
        {
            if (changed == levelOverride || (levelOverride == null && catalog != null && changed == catalog.Get(0)))
                QueuePreview();
        }

        // Objects can't be created inside OnValidate, so build on the next editor tick.
        void QueuePreview()
        {
            if (Application.isPlaying || previewQueued) return;
            previewQueued = true;
            UnityEditor.EditorApplication.delayCall += BuildPreview;
        }

        void BuildPreview()
        {
            previewQueued = false;
            if (this == null || !isActiveAndEnabled || UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode) return;
            EditorAutoAssign?.Invoke(this);
            Generate();
        }

        // Preview objects are rebuilt on load, so keep them out of the scene file.
        static void MarkDontSave(Transform root)
        {
            if (root == null) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
            Mesh fortress = MeshOf(root, "Fortress");
            if (fortress != null) fortress.hideFlags = HideFlags.DontSave;
            Mesh floor = MeshOf(root, "RoadFloor");
            if (floor != null) floor.hideFlags = HideFlags.DontSave;
            Mesh pads = MeshOf(root, "PlacementPads");
            if (pads != null) pads.hideFlags = HideFlags.DontSave;
            Mesh carPads = MeshOf(root, "BarricadePads");
            if (carPads != null) carPads.hideFlags = HideFlags.DontSave;
        }
#endif

        LevelData ResolveLevel()
        {
            if (!Application.isPlaying)
            {
                // Edit-mode preview: don't touch the play-session progress.
                if (levelOverride != null) return levelOverride;
                if (catalog != null && catalog.Get(0) != null) return catalog.Get(0);
                return ScriptableObject.CreateInstance<LevelData>();
            }
            if (levelOverride != null)
            {
                LevelProgress.LevelCount = 1;
                return levelOverride;
            }
            if (catalog != null && catalog.Count > 0)
            {
                LevelProgress.LevelCount = catalog.Count;
                LevelProgress.CurrentIndex = Mathf.Clamp(LevelProgress.CurrentIndex, 0, catalog.Count - 1);
                LevelData d = catalog.Get(LevelProgress.CurrentIndex);
                if (d != null) return d;
            }
            Debug.LogWarning("[ZombieTD] No level assigned; using default corridor settings.");
            LevelProgress.LevelCount = 1;
            return ScriptableObject.CreateInstance<LevelData>();
        }

        /// <summary>
        /// The pad under the cursor. Every pad is tested at its own height (roof pads high up, pads in front of the
        /// police cars on the ground), and the one the camera ray reaches first wins.
        /// </summary>
        public bool TryGetSlotFromRay(Ray ray, out WallSlot slot)
        {
            slot = null;
            if (grid == null) Generate();
            if (slotList == null || ray.direction.y > -0.0001f) return false;

            float bestT = float.MaxValue;
            foreach (WallSlot s in slotList)
            {
                Vector3 c = s.MountPoint;
                float t = (c.y - ray.origin.y) / ray.direction.y;
                if (t < 0f || t >= bestT) continue;
                Vector3 p = ray.origin + ray.direction * t;
                if (Mathf.Abs(p.x - c.x) <= s.HitHalf && Mathf.Abs(p.z - c.z) <= s.HitHalf) { bestT = t; slot = s; }
            }
            return slot != null;
        }

        void BuildVisuals()
        {
            Transform old = transform.Find(RootName);
            if (old != null)
            {
                DestroySafe(MeshOf(old, "Fortress")); // generated meshes aren't freed with their GameObjects
                DestroySafe(MeshOf(old, "RoadFloor"));
                DestroySafe(MeshOf(old, "PlacementPads"));
                DestroySafe(MeshOf(old, "BarricadePads"));
                DestroySafe(old.gameObject);
            }

            var root = new GameObject(RootName).transform;
            root.SetParent(transform, false);
            float cs = grid.CellSize, worldW = grid.Width * cs, worldD = grid.Height * cs;

            // Walls: one mesh, one draw call per material (one per facade style, plus the roof).
            var styles = new List<Material>();
            if (wallVariants != null)
                foreach (Material v in wallVariants) if (v != null) styles.Add(v);
            if (styles.Count == 0) styles.Add(OrFallback(wallMaterial, new Color(0.2f, 0.2f, 0.22f)));

            var mb = new MeshBuilder(styles.Count);
            MazeMeshBuilder.AddWalls(grid, level.wallHeight, mb, level.parapetHeight, level.parapetThickness);
            List<PlacementPads.PadInfo> pads = PlacementPads.Plan(grid, level);
            Mesh mesh = mb.ToMesh("Fortress");

            styles.Add(OrFallback(wallTopMaterial, new Color(0.1f, 0.1f, 0.11f)));
            var walls = new GameObject("Fortress") { layer = LayerOrDefault("Wall") };
            walls.transform.SetParent(root, false);
            walls.AddComponent<MeshFilter>().sharedMesh = mesh;
            walls.AddComponent<MeshRenderer>().sharedMaterials = styles.ToArray();
            walls.AddComponent<MeshCollider>().sharedMesh = mesh;

            // Ground slab with a margin around the map.
            float m = level.groundMargin;
            GameObject ground = Box(root, "Ground", new Vector3(worldW * 0.5f, -0.1f, worldD * 0.5f),
                new Vector3(worldW + m * 2f, 0.2f, worldD + m * 2f), OrFallback(groundMaterial, new Color(0.4f, 0.38f, 0.34f)), true);
            ground.layer = LayerOrDefault("Ground");

            // Patterned road surface from the kit floor sheet.
            if (floorMaterial != null && level.kitFloor)
            {
                Mesh floorMesh = RoadFloorBuilder.Build(grid, level);
                if (floorMesh != null)
                {
                    var floor = new GameObject("RoadFloor") { layer = LayerOrDefault("Ground") };
                    floor.transform.SetParent(root, false);
                    floor.AddComponent<MeshFilter>().sharedMesh = floorMesh;
                    floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                }
            }

            // Flat markers on the spawn and goal cells.
            Material spawnMat = OrFallback(spawnMaterial, new Color(0.45f, 0.08f, 0.08f));
            foreach (Vector2Int c in grid.SpawnCells)
                Box(root, "Spawn", Local(c) + Vector3.up * 0.03f, new Vector3(cs * 0.9f, 0.06f, cs * 0.9f), spawnMat, false);

            if (!BuildGate(root))
            {
                Material goalMat = OrFallback(goalMaterial, new Color(0.28f, 0.17f, 0.09f));
                foreach (Vector2Int c in grid.GoalCells)
                    Box(root, "Goal", Local(c) + Vector3.up * 0.03f, new Vector3(cs * 0.9f, 0.06f, cs * 0.9f), goalMat, false);
            }

            BuildSlots(root, pads);
            if (level.addLights) LevelLighting.Build(root, grid, level);

            // Last: hand-placed police cars block the road tiles under them (after floor/slots/lights are placed).
            if (level.policeBarricade)
            {
                var barricade = FindAnyObjectByType<PoliceBarricade>();
                if (barricade != null)
                {
                    barricade.BlockTiles(grid);
                    BuildBarricadeSlots(root, barricade); // a squad pad in front of each car
                }
            }
        }

        // Two sliding blast doors across the goal cells, flush with the map edge. Only for goals on an edge.
        bool BuildGate(Transform root)
        {
            var goals = grid.GoalCells;
            if (!level.hasGate || goals.Count == 0) return false;

            bool minX = true, maxX = true, minZ = true, maxZ = true;
            foreach (Vector2Int c in goals)
            {
                minX &= c.x == 0; maxX &= c.x == grid.Width - 1;
                minZ &= c.y == 0; maxZ &= c.y == grid.Height - 1;
            }
            bool alongZ = minX || maxX; // doors span along Z when the goal is on the left/right edge
            if (!alongZ && !minZ && !maxZ) return false;

            int lo = int.MaxValue, hi = int.MinValue;
            foreach (Vector2Int c in goals)
            {
                int a = alongZ ? c.y : c.x;
                lo = Mathf.Min(lo, a);
                hi = Mathf.Max(hi, a);
            }

            float cs = grid.CellSize, t = GateThickness, h = level.wallHeight * level.gateHeightFraction;
            float edge = alongZ ? (minX ? 0f : grid.Width * cs) : (minZ ? 0f : grid.Height * cs);
            float inward = (alongZ ? minX : minZ) ? 1f : -1f; // from the edge into the map
            float across = edge + inward * t * 0.5f;
            float a0 = lo * cs, a1 = (hi + 1) * cs, mid = (a0 + a1) * 0.5f, half = (a1 - a0) * 0.5f;

            var gateGo = new GameObject("BaseGate");
            gateGo.transform.SetParent(root, false);
            Material mat = OrFallback(gateMaterial, new Color(0.16f, 0.17f, 0.19f));
            Transform doorA = Box(gateGo.transform, "DoorA", Axis(alongZ, across, h * 0.5f, (a0 + mid) * 0.5f),
                Axis(alongZ, t, h, half), mat, false).transform;
            Transform doorB = Box(gateGo.transform, "DoorB", Axis(alongZ, across, h * 0.5f, (mid + a1) * 0.5f),
                Axis(alongZ, t, h, half), mat, false).transform;

            Vector3 along = Axis(alongZ, 0f, 0f, 1f);
            Vector3 outward = Axis(alongZ, -inward, 0f, 0f);
            gateGo.AddComponent<BaseGate>().Configure(doorA, doorB, -along * half, along * half, outward,
                level.gateHealth, t);
            return true;
        }

        // Vector from (across-axis, y, along-axis) components.
        static Vector3 Axis(bool alongZ, float across, float y, float along) =>
            alongZ ? new Vector3(across, y, along) : new Vector3(along, y, across);

        // Placement pads: spaced-out roof tiles, each with a WallSlot (the logical spot) and a glowing plate (the visual).
        void BuildSlots(Transform root, List<PlacementPads.PadInfo> pads)
        {
            slotList.Clear();
            usingAuthoredPads = false;

            // Pads you placed in the scene (Tools > Pads) take over completely: their positions are never regenerated.
            foreach (WallSlot s in FindObjectsByType<WallSlot>(FindObjectsSortMode.None))
                if (!s.transform.IsChildOf(transform)) slotList.Add(s);
            if (slotList.Count > 0) { usingAuthoredPads = true; return; }

            // No pads in the scene: generate a default set so the game still works.
            slotRoot = new GameObject("WallSlots").transform;
            slotRoot.SetParent(root, false);

            foreach (PlacementPads.PadInfo p in pads)
            {
                var go = new GameObject("Slot");
                go.transform.SetParent(slotRoot, false);
                go.transform.position = grid.Origin + PlacementPads.MountLocal(grid, level, p) + Vector3.up * (level.wallHeight + PlacementPads.Lift);
                go.transform.rotation = Quaternion.LookRotation(new Vector3(p.toRoad.x, 0f, p.toRoad.y));
                WallSlot slot = go.AddComponent<WallSlot>();
                slot.Configure(PlacementPads.FrontOffset(grid, level), false, 2f, level.padSize, null);
                slotList.Add(slot);
            }

            if (!level.showPads || pads.Count == 0) return;
            MakePadVisual(root, "PlacementPads", PlacementPads.BuildMesh(grid, pads, level));
        }

        // Default pads on the road behind each police car (only when the scene has no pads of its own).
        void BuildBarricadeSlots(Transform root, PoliceBarricade barricade)
        {
            if (usingAuthoredPads || slotRoot == null) return;
            List<PoliceBarricade.GroundSlot> planned = barricade.PlanSlots(grid, level);
            if (planned.Count == 0) return;

            float half = PlacementPads.HalfPad(grid, level);
            const float groundLift = 0.08f; // above the road floor (0.02) so the plate always shows
            var centres = new List<Vector3>(planned.Count);
            foreach (PoliceBarricade.GroundSlot g in planned)
            {
                var go = new GameObject("CarSlot");
                go.transform.SetParent(slotRoot, false);
                go.transform.position = grid.Origin + g.localCentre + Vector3.up * groundLift;
                go.transform.rotation = Quaternion.LookRotation(new Vector3(g.facing.x, 0f, g.facing.y));
                WallSlot slot = go.AddComponent<WallSlot>();
                slot.Configure(PlacementPads.FrontOffset(grid, level), true, half + 0.4f, level.padSize, null);
                slotList.Add(slot);
                centres.Add(g.localCentre);
            }

            if (level.showPads) MakePadVisual(root, "BarricadePads", PlacementPads.BuildPlates(centres, half, groundLift));
        }

        void MakePadVisual(Transform root, string name, Mesh mesh)
        {
            var visual = new GameObject(name);
            visual.transform.SetParent(root, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[]
            {
                UnlitFallback(padFillMaterial, new Color(0.07f, 0.4f, 0.46f)),   // subtle teal fill
                UnlitFallback(padEdgeMaterial, new Color(0.35f, 1.1f, 1.2f)),    // brighter border (slight bloom)
            };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Material UnlitFallback(Material m, Color color)
        {
            if (m != null) return m;
            return new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = color };
        }

        // Grid-local center of a cell at ground level.
        Vector3 Local(Vector2Int c) => new Vector3((c.x + 0.5f) * grid.CellSize, 0f, (c.y + 0.5f) * grid.CellSize);

        static GameObject Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (!keepCollider) DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Mesh MeshOf(Transform root, string childName)
        {
            Transform f = root.Find(childName);
            MeshFilter mf = f != null ? f.GetComponent<MeshFilter>() : null;
            return mf != null ? mf.sharedMesh : null;
        }

        static Material OrFallback(Material m, Color color)
        {
            if (m != null) return m;
            return new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        }

        static int LayerOrDefault(string layerName)
        {
            int l = LayerMask.NameToLayer(layerName);
            return l < 0 ? 0 : l;
        }

        static void DestroySafe(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
