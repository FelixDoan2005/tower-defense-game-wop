using UnityEngine;
using ZombieTD.Core;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Builds the level's light rig from its layout (works for any layout):
    ///  - flickering fire glow on the facades facing the road,
    ///  - red/blue emergency strobes near the spawn and gate ends,
    ///  - cold floodlights over the gate, aimed down the road,
    ///  - one sweeping helicopter searchlight.
    /// All positions are grid-local (children of the level's "Generated" root). Load time only.
    /// </summary>
    public static class LevelLighting
    {
        static readonly Vector2Int[] Dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };

        public static void Build(Transform parent, MazeGrid grid, LevelData level)
        {
            var root = new GameObject("Lights").transform;
            root.SetParent(parent, false);

            Vector3 goal = Centroid(grid, grid.GoalCells);
            Vector3 spawn = Centroid(grid, grid.SpawnCells);
            Vector3 along = spawn - goal;
            along.y = 0f;
            along = along.sqrMagnitude > 0.01f ? along.normalized : Vector3.right;
            Vector3 side = new Vector3(-along.z, 0f, along.x);
            float cs = grid.CellSize;

            if (level.fireLightSpacing > 0) FacadeLights(root, grid, level, level.fireLightSpacing, level.maxFireLights, 0);
            if (level.windowGlowSpacing > 0) FacadeLights(root, grid, level, level.windowGlowSpacing, level.maxWindowGlow, 1);
            if (level.beaconSpacing > 0) Beacons(root, grid, level);
            if (level.roofLightSpacing > 0) RoofFloodlights(root, grid, level);

            // Extra strobe pair halfway along the road (a wrecked emergency vehicle).
            if (level.strobes)
            {
                Vector3 mid = (goal + spawn) * 0.5f;
                Strobe(root, "StrobeMidRed", mid + side * cs * 0.9f + Vector3.up * 3f, 0.1f);
                Strobe(root, "StrobeMidBlue", mid - side * cs * 0.9f + Vector3.up * 3f, 0.6f);
            }

            // Emergency strobes: a red/blue pair just inside each end of the road.
            if (level.strobes)
            {
                Strobe(root, "StrobeGoalRed", goal + along * cs * 2f + side * cs * 0.8f + Vector3.up * 4f, 0f);
                Strobe(root, "StrobeGoalBlue", goal + along * cs * 2f - side * cs * 0.8f + Vector3.up * 4f, 0.5f);
                Strobe(root, "StrobeSpawnRed", spawn - along * cs * 2f + side * cs * 0.8f + Vector3.up * 4f, 0.25f);
                Strobe(root, "StrobeSpawnBlue", spawn - along * cs * 2f - side * cs * 0.8f + Vector3.up * 4f, 0.75f);
            }

            // Floodlight over the gate, aimed down the road toward the zombies.
            if (level.gateFloodlights)
            {
                Vector3 pos = goal + along * cs * 1.5f + Vector3.up * (level.wallHeight * 0.9f);
                Light f = NewLight(root, "GateFloodlight", pos, LightType.Spot, new Color(0.78f, 0.9f, 1f), 28f, 70f);
                f.spotAngle = 75f;
                f.innerSpotAngle = 35f;
                f.transform.rotation = Quaternion.LookRotation(along + Vector3.down * 0.55f);
            }

            // Sweeping searchlight, high above the middle of the road.
            if (level.searchlight)
            {
                Vector3 mid = (goal + spawn) * 0.5f + Vector3.up * (level.wallHeight + 45f);
                Light s = NewLight(root, "Searchlight", mid, LightType.Spot, new Color(0.9f, 0.95f, 1f), 55f, 140f);
                s.spotAngle = 26f;
                s.innerSpotAngle = 12f;
                s.shadows = LightShadows.Soft; // the one light that casts shadows: the beam raking over the facades
                s.gameObject.AddComponent<Searchlight>().Configure(goal + along * cs * 3f, spawn - along * cs * 3f);
            }
        }

        /// <summary>A spot on a facade: wall-face point (grid-local), outward direction toward the road, and a 0..1 random value.</summary>
        public struct FacadeSpot
        {
            public Vector3 face;
            public Vector2Int normal;
            public float h;
        }

        /// <summary>
        /// Evenly spread spots along the road-facing facades at varying heights.
        /// kind 0 = fire windows; kind 1 = lit-office windows (offset so the two don't share tiles).
        /// </summary>
        public static System.Collections.Generic.List<FacadeSpot> FacadeSpots(MazeGrid g, LevelData level, int spacing, int max, int kind)
        {
            var spots = new System.Collections.Generic.List<FacadeSpot>();
            if (spacing <= 0) return spots;
            float cs = g.CellSize;
            int counter = kind * 2;
            for (int z = 0; z < g.Height && spots.Count < max; z++)
            for (int x = 0; x < g.Width && spots.Count < max; x++)
            {
                if (g.IsWalkable(x, z)) continue;
                foreach (Vector2Int d in Dirs)
                {
                    if (!g.IsWalkable(x + d.x, z + d.y)) continue;
                    if (counter++ % spacing != 0) continue;

                    float h = Hash(x, z, d.x * 3 + d.y + kind * 17);
                    float y = Mathf.Lerp(2.5f, Mathf.Max(3f, level.wallHeight - 3f), h);
                    spots.Add(new FacadeSpot
                    {
                        face = new Vector3((x + 0.5f + d.x * 0.5f) * cs, y, (z + 0.5f + d.y * 0.5f) * cs),
                        normal = d,
                        h = h,
                    });
                    break;
                }
            }
            return spots;
        }

        // Lights hovering in front of the facades. kind 0 = flickering orange fire; kind 1 = steady glow from lit offices.
        static void FacadeLights(Transform root, MazeGrid g, LevelData level, int spacing, int max, int kind)
        {
            foreach (FacadeSpot s in FacadeSpots(g, level, spacing, max, kind))
            {
                Vector3 pos = s.face + new Vector3(s.normal.x, 0f, s.normal.y) * 1.2f;
                if (kind == 0)
                {
                    Light l = NewLight(root, "FireGlow", pos, LightType.Point, new Color(1f, 0.5f, 0.16f), Mathf.Lerp(5f, 8f, s.h), 15f);
                    l.gameObject.AddComponent<FireLight>();
                }
                else
                {
                    Color c = s.h < 0.5f ? new Color(1f, 0.82f, 0.55f) : new Color(0.65f, 0.8f, 1f);
                    NewLight(root, "WindowGlow", pos, LightType.Point, c, 2.5f, 10f);
                }
            }
        }

        // Military floodlights: bright white pools over the roofs where turrets and soldiers go.
        // Each sits on a short mast set one tile back from the road edge, so it covers the first two rows.
        static void RoofFloodlights(Transform root, MazeGrid g, LevelData level)
        {
            float cs = g.CellSize, top = level.wallHeight;
            int counter = 0, placed = 0;
            for (int z = 0; z < g.Height && placed < level.maxRoofLights; z++)
            for (int x = 0; x < g.Width && placed < level.maxRoofLights; x++)
            {
                if (g.IsWalkable(x, z)) continue;
                foreach (Vector2Int d in Dirs)
                {
                    if (!g.IsWalkable(x + d.x, z + d.y)) continue;
                    if (counter++ % level.roofLightSpacing != 0) continue;

                    // Back off the road edge toward the middle of the roof, if that cell is roof too.
                    bool deeper = !g.IsWalkable(x - d.x, z - d.y) && g.InBounds(x - d.x, z - d.y);
                    float back = deeper ? 0.5f : 0f;
                    Vector3 mast = new Vector3((x + 0.5f - d.x * back) * cs, top, (z + 0.5f - d.y * back) * cs);

                    Light l = NewLight(root, "RoofFloodlight", mast + Vector3.up * 4.2f, LightType.Spot,
                        new Color(1f, 0.78f, 0.4f), 9f, 13f); // warm yellow
                    l.spotAngle = 105f;
                    l.innerSpotAngle = 60f;
                    l.transform.rotation = Quaternion.LookRotation(Vector3.down);
                    if (level.roofFixtures) Fixture(root, mast);
                    placed++;
                    break;
                }
            }
        }

        // Placeholder lamp mast: thin pole with a glowing head. Swap for a real model later.
        static void Fixture(Transform root, Vector3 baseLocal)
        {
            var mast = new GameObject("FloodlightMast").transform;
            mast.SetParent(root, false);
            mast.localPosition = baseLocal;

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            StripCollider(pole);
            pole.transform.SetParent(mast, false);
            pole.transform.localPosition = new Vector3(0f, 2f, 0f);
            pole.transform.localScale = new Vector3(0.18f, 2f, 0.18f);
            pole.GetComponent<Renderer>().sharedMaterial = PoleMaterial();

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            StripCollider(head);
            head.transform.SetParent(mast, false);
            head.transform.localPosition = new Vector3(0f, 4.05f, 0f);
            head.transform.localScale = new Vector3(1.1f, 0.3f, 0.7f);
            head.GetComponent<Renderer>().sharedMaterial = LampMaterial();
        }

        static Material lampMat, poleMat;

        static Material LampMaterial()
        {
            if (lampMat == null)
                lampMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "FloodlightLamp", color = new Color(2.2f, 1.5f, 0.6f) }; // warm HDR: blooms
            return lampMat;
        }

        static Material PoleMaterial()
        {
            if (poleMat == null)
                poleMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FloodlightPole", color = new Color(0.12f, 0.13f, 0.14f) };
            return poleMat;
        }

        static void StripCollider(GameObject go)
        {
            Collider c = go.GetComponent<Collider>();
            if (c == null) return;
            if (Application.isPlaying) Object.Destroy(c);
            else Object.DestroyImmediate(c);
        }

        // Slow red warning beacons on the roof edges.
        static void Beacons(Transform root, MazeGrid g, LevelData level)
        {
            float cs = g.CellSize;
            int counter = 0, placed = 0;
            for (int z = 0; z < g.Height && placed < level.maxBeacons; z++)
            for (int x = 0; x < g.Width && placed < level.maxBeacons; x++)
            {
                if (g.IsWalkable(x, z)) continue;
                foreach (Vector2Int d in Dirs)
                {
                    if (!g.IsWalkable(x + d.x, z + d.y)) continue;
                    if (counter++ % level.beaconSpacing != 0) continue;
                    Vector3 pos = new Vector3((x + 0.5f + d.x * 0.3f) * cs, level.wallHeight + 1.5f, (z + 0.5f + d.y * 0.3f) * cs);
                    Light l = NewLight(root, "Beacon", pos, LightType.Point, new Color(1f, 0.1f, 0.05f), 6f, 10f);
                    l.gameObject.AddComponent<LightStrobe>().Configure(new Color(1f, 0.1f, 0.05f), Color.black,
                        Hash(x, z, 5), 6f, 0.5f);
                    placed++;
                    break;
                }
            }
        }

        static void Strobe(Transform root, string name, Vector3 pos, float phase)
        {
            bool red = name.EndsWith("Red");
            Light l = NewLight(root, name, pos, LightType.Point, red ? new Color(1f, 0.08f, 0.05f) : new Color(0.1f, 0.25f, 1f), 9f, 18f);
            l.gameObject.AddComponent<LightStrobe>().Configure(
                red ? new Color(1f, 0.08f, 0.05f) : new Color(0.1f, 0.25f, 1f), new Color(0f, 0f, 0f), phase, 9f);
        }

        static Light NewLight(Transform parent, string name, Vector3 localPos, LightType type, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            Light l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        static Vector3 Centroid(MazeGrid g, System.Collections.Generic.List<Vector2Int> cells)
        {
            if (cells.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (Vector2Int c in cells) sum += new Vector3((c.x + 0.5f) * g.CellSize, 0f, (c.y + 0.5f) * g.CellSize);
            return sum / cells.Count;
        }

        // Stable 0..1 value from a position, so the same level always lights the same way.
        public static float Hash(int x, int z, int s)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(z * 19349663) ^ (uint)(s * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }
    }
}
