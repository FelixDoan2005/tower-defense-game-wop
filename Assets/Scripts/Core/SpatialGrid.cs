using System;
using UnityEngine;
using ZombieTD.Enemies;

namespace ZombieTD.Core
{
    /// <summary>
    /// Uniform XZ grid that buckets zombies by cell. Rebuilt once per frame with a counting sort,
    /// so items in a cell are contiguous and cells in the same row are contiguous too.
    /// A radius query is therefore just a few array spans: no physics, no allocations.
    /// </summary>
    public class SpatialGrid
    {
        readonly Vector3 origin;
        readonly float invCellSize;
        readonly int cols, rows;
        readonly int[] cellStart; // length cols*rows + 1
        readonly int[] cursor;    // scratch: per-cell counts, then write cursors

        Zombie[] items = new Zombie[0];
        int[] itemCell = new int[0];
        int count;

        public int Columns => cols;
        public int Rows => rows;
        public float CellSize { get; }
        public int Count => count;
        /// <summary>Zombies sorted by cell. Valid range for a cell is [CellStart, CellEnd).</summary>
        public Zombie[] Items => items;

        public SpatialGrid(Vector3 origin, float width, float depth, float cellSize)
        {
            this.origin = origin;
            CellSize = cellSize;
            invCellSize = 1f / cellSize;
            cols = Mathf.Max(1, Mathf.CeilToInt(width * invCellSize));
            rows = Mathf.Max(1, Mathf.CeilToInt(depth * invCellSize));
            cellStart = new int[cols * rows + 1];
            cursor = new int[cols * rows];
        }

        public int CellX(float worldX) => Mathf.Clamp((int)Mathf.Floor((worldX - origin.x) * invCellSize), 0, cols - 1);
        public int CellZ(float worldZ) => Mathf.Clamp((int)Mathf.Floor((worldZ - origin.z) * invCellSize), 0, rows - 1);
        public int CellIndexOf(Vector3 p) => CellZ(p.z) * cols + CellX(p.x);

        public int CellStart(int cell) => cellStart[cell];
        public int CellEnd(int cell) => cellStart[cell + 1];
        /// <summary>First item index of the span covering cells x0..x1 in row z.</summary>
        public int RowStart(int z, int x0) => cellStart[z * cols + x0];
        /// <summary>One past the last item index of the span covering cells x0..x1 in row z.</summary>
        public int RowEnd(int z, int x1) => cellStart[z * cols + x1 + 1];

        /// <summary>Cell rectangle (inclusive) covering a circle on the XZ plane.</summary>
        public void GetCellRect(Vector3 center, float radius, out int x0, out int z0, out int x1, out int z1)
        {
            x0 = CellX(center.x - radius);
            x1 = CellX(center.x + radius);
            z0 = CellZ(center.z - radius);
            z1 = CellZ(center.z + radius);
        }

        /// <summary>Re-buckets all active zombies. Allocates only when capacity grows.</summary>
        public void Rebuild(Zombie[] source, int n)
        {
            if (items.Length < n)
            {
                int cap = Mathf.NextPowerOfTwo(n);
                items = new Zombie[cap];
                itemCell = new int[cap];
            }

            Array.Clear(cursor, 0, cursor.Length);
            for (int i = 0; i < n; i++)
            {
                Zombie z = source[i];
                int c = CellIndexOf(z.Position);
                itemCell[i] = c;
                z.SpatialCell = c;
                cursor[c]++;
            }

            int run = 0;
            int cells = cursor.Length;
            for (int c = 0; c < cells; c++)
            {
                cellStart[c] = run;
                run += cursor[c];
                cursor[c] = cellStart[c];
            }
            cellStart[cells] = run;

            for (int i = 0; i < n; i++)
                items[cursor[itemCell[i]]++] = source[i];

            if (count > n) Array.Clear(items, n, count - n); // drop stale references
            count = n;
        }

        /// <summary>Fills results with living zombies within radius (XZ). Returns how many were written.</summary>
        public int Query(Vector3 center, float radius, Zombie[] results)
        {
            GetCellRect(center, radius, out int x0, out int z0, out int x1, out int z1);
            float r2 = radius * radius;
            int found = 0;
            for (int z = z0; z <= z1; z++)
            {
                int end = RowEnd(z, x1);
                for (int i = RowStart(z, x0); i < end; i++)
                {
                    Zombie zb = items[i];
                    if (!zb.IsAlive) continue;
                    float dx = zb.Position.x - center.x;
                    float dz = zb.Position.z - center.z;
                    if (dx * dx + dz * dz > r2) continue;
                    results[found++] = zb;
                    if (found == results.Length) return found;
                }
            }
            return found;
        }
    }
}
