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

        static void Block(MazeGrid grid, int x, int z, List<Vector2Int> list)
        {
            if (!grid.IsWalkable(x, z)) return;
            var cell = new Vector2Int(x, z);
            if (grid.SpawnCells.Contains(cell) || grid.GoalCells.Contains(cell)) return;
            grid.SetWalkable(x, z, false);
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
