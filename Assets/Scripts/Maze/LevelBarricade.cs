using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Plans the default police-barricade layout for a corridor level: two staggered columns of cars across the
    /// road (A on every other lane, B two tiles back on the lanes A left open). Pure data, no objects are created;
    /// the editor tool turns the plan into real cars you can then move by hand. Positions are grid-local.
    /// </summary>
    public static class LevelBarricade
    {
        public struct CarPlan
        {
            public Vector3 localPosition;
            public float yaw;
        }

        public static List<CarPlan> Plan(MazeGrid grid, LevelData level)
        {
            var plans = new List<CarPlan>();
            if (!RoadBounds(grid, out int minX, out int minZ, out int maxX, out int maxZ)) return plans;

            bool alongX = (maxX - minX) >= (maxZ - minZ);
            int aMin = alongX ? minX : minZ, aMax = alongX ? maxX : maxZ;
            int cMin = alongX ? minZ : minX, cMax = alongX ? maxZ : maxX;
            int len = aMax - aMin + 1;

            // Measured from the goal end toward the spawn end.
            Vector3 goal = Vector3.zero;
            foreach (Vector2Int c in grid.GoalCells) goal += new Vector3(c.x, 0f, c.y);
            if (grid.GoalCells.Count > 0) goal /= grid.GoalCells.Count;
            float goalAlong = alongX ? goal.x : goal.z;
            int dir = goalAlong <= (aMin + aMax) * 0.5f ? 1 : -1;
            int start = dir > 0 ? aMin : aMax;

            int offset = Mathf.RoundToInt(level.barricadeFraction * (len - 1));
            int lo = aMin + 2, hi = aMax - 2;
            int aCol = Mathf.Clamp(start + dir * offset, dir > 0 ? lo : lo + 2, dir > 0 ? hi - 2 : hi);
            int bCol = Mathf.Clamp(aCol + dir * 2, lo, hi);

            float cs = grid.CellSize;
            for (int c = cMin; c <= cMax; c++)
            {
                int col = (c - cMin) % 2 == 0 ? aCol : bCol;
                int cx = alongX ? col : c, cz = alongX ? c : col;

                float jx = (Hash(cx, cz, 1) - 0.5f) * 0.15f * cs, jz = (Hash(cx, cz, 2) - 0.5f) * 0.15f * cs;
                // Cars lie across the road, nose angled a little either way, like they were abandoned in a hurry.
                float yaw = (Hash(cx, cz, 3) < 0.5f ? 0f : 180f) + (Hash(cx, cz, 4) - 0.5f) * 2f * level.barricadeTiltDegrees;
                plans.Add(new CarPlan
                {
                    localPosition = new Vector3((cx + 0.5f) * cs + jx, 0.02f, (cz + 0.5f) * cs + jz),
                    yaw = yaw,
                });
            }
            return plans;
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

        static float Hash(int a, int b, int s)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ (uint)(s * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }
    }
}
