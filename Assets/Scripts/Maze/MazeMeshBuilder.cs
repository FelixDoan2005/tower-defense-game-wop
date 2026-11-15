using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Adds every wall tile to a MeshBuilder, emitting only exposed faces. Each "building" (a run of
    /// BuildingLength tiles along a facade) gets its own facade variant so the street doesn't repeat.
    /// A low parapet runs along every roof edge that overlooks the road. Coordinates are grid-local.
    /// </summary>
    public static class MazeMeshBuilder
    {
        const int BuildingLength = 5; // tiles per building

        public static void AddWalls(MazeGrid grid, float height, MeshBuilder mb, float parapetHeight, float parapetThickness)
        {
            float s = grid.CellSize;
            int variants = mb.FacadeVariants;
            for (int z = 0; z < grid.Height; z++)
            for (int x = 0; x < grid.Width; x++)
            {
                if (grid.IsWalkable(x, z)) continue;

                var min = new Vector3(x * s, 0f, z * s);
                var max = new Vector3(min.x + s, height, min.z + s);
                mb.AddFace(BoxFace.Top, min, max);

                // Sides only where the neighbour is open or outside the grid.
                bool px = !IsWall(grid, x + 1, z), nx = !IsWall(grid, x - 1, z);
                bool pz = !IsWall(grid, x, z + 1), nz = !IsWall(grid, x, z - 1);
                if (px) mb.AddFace(BoxFace.PosX, min, max, Variant(z / BuildingLength, 1, x, variants));
                if (nx) mb.AddFace(BoxFace.NegX, min, max, Variant(z / BuildingLength, 2, x, variants));
                if (pz) mb.AddFace(BoxFace.PosZ, min, max, Variant(x / BuildingLength, 3, z, variants));
                if (nz) mb.AddFace(BoxFace.NegZ, min, max, Variant(x / BuildingLength, 4, z, variants));

                if (parapetHeight <= 0f) continue;
                float t = Mathf.Min(parapetThickness, s * 0.4f), top = height + parapetHeight;
                if (px) mb.AddRoofBox(new Vector3(max.x - t, height, min.z), new Vector3(max.x, top, max.z));
                if (nx) mb.AddRoofBox(new Vector3(min.x, height, min.z), new Vector3(min.x + t, top, max.z));
                if (pz) mb.AddRoofBox(new Vector3(min.x, height, max.z - t), new Vector3(max.x, top, max.z));
                if (nz) mb.AddRoofBox(new Vector3(min.x, height, min.z), new Vector3(max.x, top, min.z + t));
            }
        }

        public static bool IsWall(MazeGrid g, int x, int z) => g.InBounds(x, z) && !g.IsWalkable(x, z);

        // Stable pseudo-random facade style for one building (same for every tile of that building's face).
        static int Variant(int segment, int side, int row, int variants)
        {
            if (variants <= 1) return 0;
            unchecked
            {
                uint h = (uint)(segment * 73856093) ^ (uint)(side * 19349663) ^ (uint)(row * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h % (uint)variants);
            }
        }
    }
}
