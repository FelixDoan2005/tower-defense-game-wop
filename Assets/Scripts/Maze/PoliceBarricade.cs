using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Holds the police cars you place by hand (they're ordinary scene objects, saved with the scene).
    /// When the level builds, every active child car blocks the road tiles under it so zombies path around.
    /// Move, rotate, scale, duplicate or delete the children freely; the blocked tiles follow.
    /// Create the starting layout with Tools > Police Barricade > Build.
    /// </summary>
    public class PoliceBarricade : MonoBehaviour
    {
        /// <summary>Marks road tiles under each car as blocked. Returns how many tiles were blocked.</summary>
        public int BlockTiles(MazeGrid grid)
        {
            var blockedTiles = new List<Vector2Int>();
            foreach (Transform car in transform)
            {
                if (!car.gameObject.activeInHierarchy) continue;
                Bounds footprint = LocalFootprint(car);

                if (!grid.WorldToCell(car.position, out int cx, out int cz)) continue;
                Block(grid, cx, cz, blockedTiles);

                // Any other tile whose centre lies under the car's body (cars bigger than a tile, or sliding between tiles).
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (!grid.InBounds(x, z)) continue;
                    Vector3 local = car.InverseTransformPoint(grid.CellToWorld(x, z));
                    if (local.x >= footprint.min.x && local.x <= footprint.max.x &&
                        local.z >= footprint.min.z && local.z <= footprint.max.z)
                        Block(grid, x, z, blockedTiles);
                }
            }

            // Safety net: never let the cars seal the road, or the whole horde stands still with nowhere to go.
            if (blockedTiles.Count > 0 && !HasRoute(grid)) ReopenARoute(grid, blockedTiles);
            return blockedTiles.Count;
        }

        /// <summary>A placement slot on the road behind a car (the gate side).</summary>
        public struct GroundSlot
        {
            public Vector2Int cell;
            /// <summary>Grid-local centre on the ground.</summary>
            public Vector3 localCentre;
            /// <summary>Direction the squad faces (toward the zombies, i.e. toward the car).</summary>
            public Vector2Int facing;
        }

        /// <summary>
        /// One slot per car, on the gate side of the car (behind it, away from the zombies), just clear of its bumper,
        /// with the squad facing the car so it works as cover. Skipped for a car whose spot is off the road or
        /// overlaps another car or pad. Call after BlockTiles.
        /// </summary>
        public List<GroundSlot> PlanSlots(MazeGrid grid, LevelData level, bool inFront = false, float halfOverride = 0f)
        {
            var slots = new List<GroundSlot>();
            if (grid.SpawnCells.Count == 0 || grid.GoalCells.Count == 0) return slots;

            // Front = the direction zombies come from (goal -> spawn), snapped to a grid axis.
            Vector2 spawn = Vector2.zero, goal = Vector2.zero;
            foreach (Vector2Int c in grid.SpawnCells) spawn += c;
            foreach (Vector2Int c in grid.GoalCells) goal += c;
            Vector2 d = spawn / grid.SpawnCells.Count - goal / grid.GoalCells.Count;
            Vector2Int front = Mathf.Abs(d.x) >= Mathf.Abs(d.y)
                ? new Vector2Int(d.x >= 0f ? 1 : -1, 0)
                : new Vector2Int(0, d.y >= 0f ? 1 : -1);
            Vector3 frontVec = new Vector3(front.x, 0f, front.y);
            float half = halfOverride > 0f ? halfOverride : PlacementPads.HalfPad(grid, level);

            // The road's width, from where the spawns are spread: a pad centre must be inside it.
            int crossMin = int.MaxValue, crossMax = int.MinValue;
            foreach (Vector2Int c in grid.SpawnCells)
            {
                int cross = front.x != 0 ? c.y : c.x;
                crossMin = Mathf.Min(crossMin, cross);
                crossMax = Mathf.Max(crossMax, cross);
            }

            // World-space bodies of all active cars (to keep pads from sitting on top of another car).
            var cars = new List<Transform>();
            var bodies = new List<Bounds>();
            foreach (Transform car in transform)
            {
                if (!car.gameObject.activeInHierarchy) continue;
                Renderer[] rs = car.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(car.position, new Vector3(2f, 2f, 2f));
                if (rs.Length > 0)
                {
                    b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                }
                cars.Add(car);
                bodies.Add(b);
            }

            for (int i = 0; i < cars.Count; i++)
            {
                // The pad sits behind the car (gate side), just clear of its rear bumper, so the car is cover.
                float reach = Mathf.Abs(front.x) > 0 ? bodies[i].extents.x : bodies[i].extents.z;
                // inFront: on the zombie side instead (the Shotgun Wall pads), so the wall stands between the horde and the car.
                Vector3 world = cars[i].position + (inFront ? frontVec : -frontVec) * (reach + half + 0.25f);

                if (!grid.WorldToCell(world, out int cx, out int cz)) continue;
                int crossCell = front.x != 0 ? cz : cx;
                if (crossCell < crossMin || crossCell > crossMax) continue; // off the road

                // Don't put a pad on top of another car, or on top of another pad.
                bool blocked = false;
                for (int j = 0; j < cars.Count && !blocked; j++)
                {
                    if (j == i) continue;
                    Bounds b = bodies[j];
                    blocked = Mathf.Abs(world.x - b.center.x) < half + b.extents.x && Mathf.Abs(world.z - b.center.z) < half + b.extents.z;
                }
                foreach (GroundSlot s in slots)
                {
                    Vector3 other = grid.Origin + s.localCentre;
                    if (Mathf.Abs(world.x - other.x) < half * 1.4f && Mathf.Abs(world.z - other.z) < half * 1.4f) { blocked = true; break; }
                }
                if (blocked) continue;

                slots.Add(new GroundSlot
                {
                    cell = new Vector2Int(cx, cz),
                    localCentre = new Vector3(world.x - grid.Origin.x, 0f, world.z - grid.Origin.z),
                    facing = front,
                });
            }
            return slots;
        }

        /// <summary>
        /// Wall pads: a row of pads across the whole road, side by side, just in front of the frontmost car (zombie side).
        /// Each is about targetWidth wide (the road is split evenly), so they never overlap. Returns grid-local centres.
        /// </summary>
        public List<GroundSlot> PlanWallLine(MazeGrid grid, LevelData level, float depth, float targetWidth, out float width)
        {
            var slots = new List<GroundSlot>();
            width = targetWidth;
            if (grid.SpawnCells.Count == 0 || grid.GoalCells.Count == 0) return slots;

            Vector2 spawn = Vector2.zero, goal = Vector2.zero;
            foreach (Vector2Int c in grid.SpawnCells) spawn += c;
            foreach (Vector2Int c in grid.GoalCells) goal += c;
            Vector2 d = spawn / grid.SpawnCells.Count - goal / grid.GoalCells.Count;
            bool alongX = Mathf.Abs(d.x) >= Mathf.Abs(d.y);
            Vector2Int front = alongX ? new Vector2Int(d.x >= 0f ? 1 : -1, 0) : new Vector2Int(0, d.y >= 0f ? 1 : -1);
            float sign = alongX ? front.x : front.y;

            // The road's extent across (from where the spawns are spread), in world units.
            int crossMin = int.MaxValue, crossMax = int.MinValue;
            foreach (Vector2Int c in grid.SpawnCells)
            {
                int cross = alongX ? c.y : c.x;
                crossMin = Mathf.Min(crossMin, cross);
                crossMax = Mathf.Max(crossMax, cross);
            }
            float crossOrigin = alongX ? grid.Origin.z : grid.Origin.x;
            float roadStart = crossOrigin + crossMin * grid.CellSize;
            float roadWidth = (crossMax - crossMin + 1) * grid.CellSize;

            // The frontmost point of any car along the front direction: the wall line starts just beyond it.
            float lineEdge = float.NegativeInfinity;
            bool anyCar = false;
            foreach (Transform car in transform)
            {
                if (!car.gameObject.activeInHierarchy) continue;
                Renderer[] rs = car.GetComponentsInChildren<Renderer>();
                Bounds b = new Bounds(car.position, new Vector3(2f, 2f, 2f));
                if (rs.Length > 0)
                {
                    b = rs[0].bounds;
                    for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                }
                float edge = alongX ? (sign > 0 ? b.max.x : -b.min.x) : (sign > 0 ? b.max.z : -b.min.z);
                lineEdge = Mathf.Max(lineEdge, edge);
                anyCar = true;
            }
            if (!anyCar) return slots;

            float along = sign * (lineEdge + 0.25f + depth * 0.5f);

            int count = Mathf.Max(1, Mathf.RoundToInt(roadWidth / targetWidth));
            width = roadWidth / count;
            for (int k = 0; k < count; k++)
            {
                float cross = roadStart + (k + 0.5f) * width;
                Vector3 world = alongX ? new Vector3(along, grid.Origin.y, cross) : new Vector3(cross, grid.Origin.y, along);
                if (!grid.WorldToCell(world, out int cx, out int cz)) continue;
                slots.Add(new GroundSlot
                {
                    cell = new Vector2Int(cx, cz),
                    localCentre = new Vector3(world.x - grid.Origin.x, 0f, world.z - grid.Origin.z),
                    facing = front,
                });
            }
            return slots;
        }

        static void Block(MazeGrid grid, int x, int z, List<Vector2Int> list)
        {
            if (!grid.IsWalkable(x, z)) return;
            var cell = new Vector2Int(x, z);
            if (grid.SpawnCells.Contains(cell) || grid.GoalCells.Contains(cell)) return;
            grid.SetWalkable(x, z, false);
            grid.MarkObstacle(x, z); // a car, not a building: shots pass over it
            list.Add(cell);
        }

        // Opens the fewest tiles needed so spawns can reach the gate, and says which, so the cars can be moved.
        static void ReopenARoute(MazeGrid grid, List<Vector2Int> blocked)
        {
            // First try one tile at a time. Of the tiles whose removal restores a route, take the one nearest the
            // middle of the road, so the horde funnels through the centre rather than hugging one wall.
            float centre = RoadCentre(grid, out bool crossIsZ);
            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < blocked.Count; i++)
            {
                grid.SetWalkable(blocked[i].x, blocked[i].y, true);
                bool ok = HasRoute(grid);
                grid.SetWalkable(blocked[i].x, blocked[i].y, false);
                if (!ok) continue;

                float d = Mathf.Abs((crossIsZ ? blocked[i].y : blocked[i].x) - centre);
                if (d < bestDistance) { bestDistance = d; bestIndex = i; }
            }
            if (bestIndex >= 0)
            {
                Vector2Int tile = blocked[bestIndex];
                grid.SetWalkable(tile.x, tile.y, true);
                blocked.RemoveAt(bestIndex);
                Debug.LogWarning($"[ZombieTD] The police cars were sealing the road, so zombies couldn't reach the gate. " +
                                 $"Opened tile ({tile.x},{tile.y}) in the middle of the road for them. Move a car to leave a real gap.");
                return;
            }

            // No single tile is enough: reopen tiles one by one until there is a route.
            int opened = 0;
            for (int i = blocked.Count - 1; i >= 0 && !HasRoute(grid); i--)
            {
                grid.SetWalkable(blocked[i].x, blocked[i].y, true);
                blocked.RemoveAt(i);
                opened++;
            }
            Debug.LogWarning($"[ZombieTD] The police cars were sealing the road; opened {opened} tiles so zombies can reach the gate. Move the cars to leave a real gap.");
        }

        // Middle of the road across its width, from where the spawns are spread out.
        static float RoadCentre(MazeGrid g, out bool crossIsZ)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            foreach (Vector2Int s in g.SpawnCells)
            {
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minZ = Mathf.Min(minZ, s.y); maxZ = Mathf.Max(maxZ, s.y);
            }
            if (g.SpawnCells.Count == 0) { crossIsZ = true; return g.Height * 0.5f; }
            crossIsZ = (maxZ - minZ) >= (maxX - minX); // spawns spread along Z => the road's width runs along Z
            return crossIsZ ? (minZ + maxZ) * 0.5f : (minX + maxX) * 0.5f;
        }

        // Can any spawn reach any goal tile? Same movement rules as the flow field (8 directions, no corner cutting).
        static bool HasRoute(MazeGrid g)
        {
            if (g.SpawnCells.Count == 0 || g.GoalCells.Count == 0) return true;
            var seen = new bool[g.CellCount];
            var queue = new Queue<int>();
            foreach (Vector2Int s in g.SpawnCells)
                if (g.IsWalkable(s.x, s.y)) { seen[g.Index(s.x, s.y)] = true; queue.Enqueue(g.Index(s.x, s.y)); }

            int w = g.Width;
            while (queue.Count > 0)
            {
                int c = queue.Dequeue(), cx = c % w, cz = c / w;
                foreach (Vector2Int goal in g.GoalCells)
                    if (goal.x == cx && goal.y == cz) return true;

                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (!g.IsWalkable(nx, nz)) continue;
                    if (dx != 0 && dz != 0 && (!g.IsWalkable(nx, cz) || !g.IsWalkable(cx, nz))) continue; // no corner cutting
                    int ni = g.Index(nx, nz);
                    if (seen[ni]) continue;
                    seen[ni] = true;
                    queue.Enqueue(ni);
                }
            }
            return false;
        }

        // Combined renderer bounds in the car's own (unscaled) local space.
        static Bounds LocalFootprint(Transform car)
        {
            Renderer[] rs = car.GetComponentsInChildren<Renderer>();
            bool any = false;
            var b = new Bounds(Vector3.zero, Vector3.zero);
            foreach (Renderer r in rs)
            {
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = lb.center + Vector3.Scale(lb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = car.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            if (!any) b = new Bounds(Vector3.zero, new Vector3(2f, 2f, 4.5f));
            b.Expand(new Vector3(-0.2f, 0f, -0.2f)); // slight shrink: only clearly covered tiles are blocked
            return b;
        }
    }
}
