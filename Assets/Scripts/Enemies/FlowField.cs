using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.Enemies
{
    /// <summary>
    /// BFS from the goal over walkable cells. Each cell stores its distance to the goal and the
    /// neighbouring cell to move to next (8-way, no corner cutting). One field serves every zombie.
    /// Marks itself dirty when the grid changes; call EnsureUpToDate once per frame.
    /// </summary>
    public class FlowField
    {
        public const int Unreachable = int.MaxValue;

        // First four entries are orthogonal, last four diagonal.
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        readonly MazeGrid grid;
        readonly int[] distance;
        readonly int[] next;
        readonly int[] queue;
        readonly Vector3[] goalPush; // goal cells: direction toward the map edge (where the gate is)
        bool dirty = true;

        /// <summary>Incremented every recompute.</summary>
        public int Version { get; private set; }

        public FlowField(MazeGrid grid)
        {
            this.grid = grid;
            int n = grid.CellCount;
            distance = new int[n];
            next = new int[n];
            queue = new int[n];
            goalPush = new Vector3[n];
            grid.Changed += MarkDirty;
        }

        public void Dispose() => grid.Changed -= MarkDirty;
        public void MarkDirty() => dirty = true;

        public void EnsureUpToDate()
        {
            if (dirty) Recompute();
        }

        public void Recompute()
        {
            dirty = false;
            Version++;
            int w = grid.Width, n = grid.CellCount;
            for (int i = 0; i < n; i++) { distance[i] = Unreachable; next[i] = -1; goalPush[i] = Vector3.zero; }

            // Multi-source BFS (uniform cost, 4-way) from every walkable goal cell.
            int head = 0, tail = 0;
            var goals = grid.GoalCells;
            for (int i = 0; i < goals.Count; i++)
            {
                int gx = goals[i].x, gz = goals[i].y;
                int gi = grid.Index(gx, gz);
                if (!grid.IsWalkable(gi) || distance[gi] == 0) continue;
                distance[gi] = 0;
                queue[tail++] = gi;

                // Push toward any neighbour outside the map: that's where the gate stands.
                Vector3 push = Vector3.zero;
                for (int d = 0; d < 4; d++)
                    if (!grid.InBounds(gx + DX[d], gz + DZ[d])) push += new Vector3(DX[d], 0f, DZ[d]);
                goalPush[gi] = push.normalized;
            }
            while (head < tail)
            {
                int c = queue[head++];
                int cx = c % w, cz = c / w, nd = distance[c] + 1;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (!grid.IsWalkable(nx, nz)) continue;
                    int ni = nz * w + nx;
                    if (distance[ni] != Unreachable) continue;
                    distance[ni] = nd;
                    queue[tail++] = ni;
                }
            }

            // Pick the lowest-distance neighbour (diagonals only when both sides are open).
            for (int c = 0; c < n; c++)
            {
                if (distance[c] == Unreachable || distance[c] == 0) continue;
                int cx = c % w, cz = c / w;
                int best = -1, bestD = distance[c], ties = 0;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], nz = cz + DZ[d];
                    if (!grid.IsWalkable(nx, nz)) continue;
                    if (d >= 4 && (!grid.IsWalkable(nx, cz) || !grid.IsWalkable(cx, nz))) continue;
                    int ni = nz * w + nx;
                    if (distance[ni] < bestD) { bestD = distance[ni]; best = ni; ties = 1; }
                    else if (best >= 0 && distance[ni] == bestD)
                    {
                        // Equally good neighbours: pick one at random (stable per tile) instead of always the same
                        // direction, so crowds don't all drift to one side. Reservoir sampling keeps it uniform.
                        ties++;
                        if (TieHash(c, ties) % ties == 0) best = ni;
                    }
                }
                next[c] = best;
            }
        }

        public bool IsGoal(int index) => index >= 0 && distance[index] == 0;

        /// <summary>For goal cells on the map edge: unit direction toward the edge (the gate). Zero otherwise.</summary>
        public Vector3 GoalPushAt(int index) => goalPush[index];

        static int TieHash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }

        /// <summary>Steps to the goal from this cell, or Unreachable.</summary>
        public int DistanceAt(int index) => distance[index];

        /// <summary>Index of the cell to move into next, or -1 at the goal / when unreachable.</summary>
        public int NextAt(int index) => next[index];

        /// <summary>Normalized XZ direction from a cell toward its next cell (zero if none).</summary>
        public Vector3 DirectionAt(int index)
        {
            int nx = next[index];
            if (nx < 0) return Vector3.zero;
            Vector3 d = grid.CellToWorld(nx) - grid.CellToWorld(index);
            return d.normalized;
        }
    }
}
