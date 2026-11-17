using System;
using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Pure data for the level: a Width x Height grid of tiles that are either walkable (corridor) or wall.
    /// Cell (0,0) has its min corner at Origin; cells lie on the XZ plane.
    /// </summary>
    public class MazeGrid
    {
        readonly bool[] walkable;
        readonly bool[] obstacle; // blocked tiles that are cars on the road rather than buildings

        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        public Vector3 Origin { get; }
        public int CellCount => walkable.Length;

        /// <summary>Cells that count as "reached the base". Several cells form a gate.</summary>
        public List<Vector2Int> GoalCells { get; } = new List<Vector2Int>(4);
        public List<Vector2Int> SpawnCells { get; } = new List<Vector2Int>(4);

        /// <summary>Raised whenever a cell's walkability changes (flow fields listen to this).</summary>
        public event Action Changed;

        public MazeGrid(int width, int height, float cellSize, Vector3 origin)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            walkable = new bool[width * height];
            obstacle = new bool[width * height];
        }

        public int Index(int x, int z) => z * Width + x;
        public bool InBounds(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Height;

        public bool IsWalkable(int x, int z) => InBounds(x, z) && walkable[z * Width + x];
        public bool IsWalkable(int index) => (uint)index < (uint)walkable.Length && walkable[index];

        public bool IsWalkableWorld(Vector3 p)
        {
            int i = WorldToIndex(p);
            return i >= 0 && walkable[i];
        }

        public void SetWalkable(int x, int z, bool value)
        {
            int i = Index(x, z);
            if (value) obstacle[i] = false;
            if (walkable[i] == value) return;
            walkable[i] = value;
            Changed?.Invoke();
        }

        /// <summary>Marks a blocked tile as a low obstacle (a police car), not a building: bullets fly over it.</summary>
        public void MarkObstacle(int x, int z)
        {
            if (InBounds(x, z) && !walkable[Index(x, z)]) obstacle[Index(x, z)] = true;
        }

        /// <summary>True for a wall/building tile (blocked and not just a car sitting on the road).</summary>
        public bool IsBuilding(int x, int z) => InBounds(x, z) && !walkable[z * Width + x] && !obstacle[z * Width + x];

        /// <summary>World position of the cell center at ground level.</summary>
        public Vector3 CellToWorld(int x, int z) =>
            new Vector3(Origin.x + (x + 0.5f) * CellSize, Origin.y, Origin.z + (z + 0.5f) * CellSize);

        public Vector3 CellToWorld(int index) => CellToWorld(index % Width, index / Width);

        public bool WorldToCell(Vector3 p, out int x, out int z)
        {
            x = Mathf.FloorToInt((p.x - Origin.x) / CellSize);
            z = Mathf.FloorToInt((p.z - Origin.z) / CellSize);
            return InBounds(x, z);
        }

        /// <summary>Cell index under a world position, or -1 if outside the grid.</summary>
        public int WorldToIndex(Vector3 p) => WorldToCell(p, out int x, out int z) ? z * Width + x : -1;

        public Bounds WorldBounds
        {
            get
            {
                Vector3 size = new Vector3(Width * CellSize, 0f, Height * CellSize);
                return new Bounds(Origin + size * 0.5f, size);
            }
        }
    }
}
