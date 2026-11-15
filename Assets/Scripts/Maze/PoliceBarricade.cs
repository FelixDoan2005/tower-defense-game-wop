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
            int blocked = 0;
            foreach (Transform car in transform)
            {
                if (!car.gameObject.activeInHierarchy) continue;
                Bounds footprint = LocalFootprint(car);

                if (!grid.WorldToCell(car.position, out int cx, out int cz)) continue;
                blocked += Block(grid, cx, cz);

                // Any other tile whose centre lies under the car's body (cars bigger than a tile, or sliding between tiles).
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = cx + dx, z = cz + dz;
                    if (!grid.InBounds(x, z)) continue;
                    Vector3 local = car.InverseTransformPoint(grid.CellToWorld(x, z));
                    if (local.x >= footprint.min.x && local.x <= footprint.max.x &&
                        local.z >= footprint.min.z && local.z <= footprint.max.z)
                        blocked += Block(grid, x, z);
                }
            }
            return blocked;
        }

        static int Block(MazeGrid grid, int x, int z)
        {
            if (!grid.IsWalkable(x, z)) return 0;
            if (grid.SpawnCells.Contains(new Vector2Int(x, z)) || grid.GoalCells.Contains(new Vector2Int(x, z))) return 0;
            grid.SetWalkable(x, z, false);
            return 1;
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
