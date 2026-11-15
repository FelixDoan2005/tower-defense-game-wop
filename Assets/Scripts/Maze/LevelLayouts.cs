using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>Turns a LevelData into a MazeGrid (walkability + spawn and goal cells). Load time only.</summary>
    public static class LevelLayouts
    {
        public static MazeGrid Build(LevelData d, Vector3 origin)
        {
            MazeGrid grid;
            switch (d.layout)
            {
                case LevelLayout.RandomMaze: grid = RandomMaze(d, origin); break;
                case LevelLayout.Ascii: grid = Ascii(d, origin); break;
                default: grid = Corridor(d, origin); break;
            }

            if (grid.SpawnCells.Count == 0 || grid.GoalCells.Count == 0)
                Debug.LogError($"[ZombieTD] Level '{d.displayName}' needs at least one spawn and one goal cell.");
            return grid;
        }

        // Rows of fortress | road | fortress. Spawns on the right edge, goal (gate) on the left edge.
        static MazeGrid Corridor(LevelData d, Vector3 origin)
        {
            int w = d.corridorLength, wt = d.wallThickness, cw = d.corridorWidth;
            var g = new MazeGrid(w, wt * 2 + cw, d.cellSize, origin);
            for (int z = wt; z < wt + cw; z++)
            {
                for (int x = 0; x < w; x++) g.SetWalkable(x, z, true);
                g.SpawnCells.Add(new Vector2Int(w - 1, z));
                g.GoalCells.Add(new Vector2Int(0, z));
            }
            return g;
        }

        // Spawns on the right edge, goal on the left edge, to match the corridor's direction.
        static MazeGrid RandomMaze(LevelData d, Vector3 origin)
        {
            int w = d.mazeWidth | 1, h = d.mazeHeight | 1;
            var g = new MazeGrid(w, h, d.cellSize, origin);
            int seed = d.randomSeed && Application.isPlaying ? System.Environment.TickCount : d.seed;
            MazeCarver.Carve(g, seed, d.loopChance);

            for (int i = 0; i < d.spawnCount; i++)
            {
                int z = OddNear((i + 1) * h / (d.spawnCount + 1), h);
                var cell = new Vector2Int(w - 1, z);
                if (g.SpawnCells.Contains(cell)) continue;
                g.SetWalkable(cell.x, z, true); // open the border next to room (w-2, z)
                g.SpawnCells.Add(cell);
            }

            int gz = OddNear(h / 2, h);
            g.SetWalkable(0, gz, true);
            g.GoalCells.Add(new Vector2Int(0, gz));
            return g;
        }

        // '#' wall, '.' road, 'S' spawn, 'G' goal. Anything else is wall. First line is the top (+Z).
        static MazeGrid Ascii(LevelData d, Vector3 origin)
        {
            string[] lines = d.asciiMap.Replace("\r", "").Trim('\n').Split('\n');
            int h = lines.Length, w = 0;
            foreach (string l in lines) w = Mathf.Max(w, l.Length);

            var g = new MazeGrid(Mathf.Max(1, w), Mathf.Max(1, h), d.cellSize, origin);
            for (int row = 0; row < h; row++)
            {
                int z = h - 1 - row;
                string line = lines[row];
                for (int x = 0; x < line.Length; x++)
                {
                    char c = line[x];
                    if (c != '.' && c != 'S' && c != 'G') continue;
                    g.SetWalkable(x, z, true);
                    if (c == 'S') g.SpawnCells.Add(new Vector2Int(x, z));
                    else if (c == 'G') g.GoalCells.Add(new Vector2Int(x, z));
                }
            }
            return g;
        }

        static int OddNear(int v, int size)
        {
            v = Mathf.Clamp(v, 1, size - 2);
            return v % 2 == 0 ? v - 1 : v;
        }
    }
}
