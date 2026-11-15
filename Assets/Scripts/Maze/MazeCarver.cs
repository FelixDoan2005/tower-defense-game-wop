using System.Collections.Generic;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Iterative recursive-backtracker on a "block" maze: rooms sit on odd tiles, walls on even tiles,
    /// so walls are a full tile thick and wide enough to stand turrets on.
    /// </summary>
    public static class MazeCarver
    {
        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DZ = { 0, 0, 1, -1 };

        /// <param name="loopChance">Chance to knock out extra walls, adding alternate routes.</param>
        public static void Carve(MazeGrid grid, int seed, float loopChance)
        {
            var rng = new System.Random(seed);
            int roomsX = (grid.Width - 1) / 2;
            int roomsZ = (grid.Height - 1) / 2;
            var visited = new bool[roomsX * roomsZ];
            var stack = new Stack<int>(roomsX * roomsZ);
            var options = new int[4];

            visited[0] = true;
            grid.SetWalkable(1, 1, true);
            stack.Push(0);

            while (stack.Count > 0)
            {
                int cur = stack.Peek();
                int rx = cur % roomsX, rz = cur / roomsX;

                int n = 0;
                for (int d = 0; d < 4; d++)
                {
                    int nx = rx + DX[d], nz = rz + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= roomsX || nz >= roomsZ) continue;
                    if (!visited[nz * roomsX + nx]) options[n++] = d;
                }

                if (n == 0) { stack.Pop(); continue; }

                int dir = options[rng.Next(n)];
                int tx = rx + DX[dir], tz = rz + DZ[dir];
                // Open the wall tile between the rooms, then the new room itself.
                grid.SetWalkable(2 * rx + 1 + DX[dir], 2 * rz + 1 + DZ[dir], true);
                grid.SetWalkable(2 * tx + 1, 2 * tz + 1, true);
                visited[tz * roomsX + tx] = true;
                stack.Push(tz * roomsX + tx);
            }

            if (loopChance <= 0f) return;

            // Remove some interior walls that separate two rooms to create loops.
            for (int z = 1; z < grid.Height - 1; z++)
            for (int x = 1; x < grid.Width - 1; x++)
            {
                bool between = (x % 2 == 0) != (z % 2 == 0); // exactly one coordinate even
                if (!between || grid.IsWalkable(x, z)) continue;
                if (rng.NextDouble() < loopChance) grid.SetWalkable(x, z, true);
            }
        }
    }
}
