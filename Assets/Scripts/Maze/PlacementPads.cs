using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Decides which roof tiles are placement pads (spaced out along the road) and builds their visuals.
    /// Units stand in the middle of the tile, flat on the roof (no ledges or overhangs).
    /// Load time only; grid-local coordinates.
    /// </summary>
    public static class PlacementPads
    {
        /// <summary>How far the pad plate floats above the roof surface (avoids z-fighting).</summary>
        public const float Lift = 0.05f;

        const float EdgeWidth = 0.2f;

        public struct PadInfo
        {
            public Vector2Int cell;
            /// <summary>Unit direction from this tile toward the road (grid axes).</summary>
            public Vector2Int toRoad;
            /// <summary>1 = next to the road; 2-3 = further back.</summary>
            public int row;
        }

        // ---------------------------------------------------------------- planning

        /// <summary>
        /// Wall tiles within padRows of the road, thinned to every padSpacing tiles along the facade.
        /// Second and third rows are shifted by half a spacing so pads don't stack in neat columns.
        /// </summary>
        public static List<PadInfo> Plan(MazeGrid g, LevelData level)
        {
            var pads = new List<PadInfo>();
            int rows = Mathf.Clamp(level.padRows, 1, 3);
            int spacing = Mathf.Max(2, level.padSpacing);

            for (int z = 0; z < g.Height; z++)
            for (int x = 0; x < g.Width; x++)
            {
                if (g.IsWalkable(x, z)) continue;
                if (!RoadRow(g, x, z, rows, out int row, out Vector2Int toRoad)) continue;

                int along = toRoad.y != 0 ? x : z; // road above/below => facade runs along X
                int offset = (row - 1) * (spacing / 2);
                if (Mod(along - offset, spacing) == 0)
                    pads.Add(new PadInfo { cell = new Vector2Int(x, z), toRoad = toRoad, row = row });
            }
            return pads;
        }

        /// <summary>Half the pad plate's width.</summary>
        public static float HalfPad(MazeGrid g, LevelData level) => Mathf.Min(level.padSize, g.CellSize - 0.2f) * 0.5f;

        /// <summary>
        /// Grid-local centre of the pad (ground level; add the roof height yourself). On the road-edge row the pad is
        /// pushed toward the road until its plate touches the parapet, so the squad stands right up against it.
        /// </summary>
        public static Vector3 MountLocal(MazeGrid g, LevelData level, PadInfo p)
        {
            float cs = g.CellSize;
            Vector3 centre = new Vector3((p.cell.x + 0.5f) * cs, 0f, (p.cell.y + 0.5f) * cs);
            if (p.row == 1)
            {
                float shift = Mathf.Max(0f, cs * 0.5f - level.parapetThickness - HalfPad(g, level));
                centre += new Vector3(p.toRoad.x, 0f, p.toRoad.y) * shift;
            }
            return centre;
        }

        /// <summary>How far in front of the pad centre the front row stands (toward the road): just behind the parapet.</summary>
        public static float FrontOffset(MazeGrid g, LevelData level) => Mathf.Max(0.2f, HalfPad(g, level) - 0.3f);

        // Which row from the road this wall tile is on (1 = next to the road), and which way the road lies.
        static bool RoadRow(MazeGrid g, int x, int z, int maxRows, out int row, out Vector2Int toRoad)
        {
            row = 0;
            toRoad = Vector2Int.zero;
            for (int k = 1; k <= maxRows; k++)
            {
                if (SeesRoad(g, x, z, 0, k)) { row = k; toRoad = new Vector2Int(0, 1); return true; }
                if (SeesRoad(g, x, z, 0, -k)) { row = k; toRoad = new Vector2Int(0, -1); return true; }
                if (SeesRoad(g, x, z, k, 0)) { row = k; toRoad = new Vector2Int(1, 0); return true; }
                if (SeesRoad(g, x, z, -k, 0)) { row = k; toRoad = new Vector2Int(-1, 0); return true; }
            }
            return false;
        }

        // Road exactly (dx,dz) away with only wall in between.
        static bool SeesRoad(MazeGrid g, int x, int z, int dx, int dz)
        {
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
            int sx = dx == 0 ? 0 : (dx > 0 ? 1 : -1), sz = dz == 0 ? 0 : (dz > 0 ? 1 : -1);
            for (int i = 1; i < steps; i++)
                if (g.IsWalkable(x + sx * i, z + sz * i) || !g.InBounds(x + sx * i, z + sz * i)) return false;
            return g.IsWalkable(x + dx, z + dz);
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;

        // ---------------------------------------------------------------- pad plates

        /// <summary>One mesh for all roof pad plates. Submesh 0 = fill, submesh 1 = border.</summary>
        public static Mesh BuildMesh(MazeGrid g, List<PadInfo> pads, LevelData level)
        {
            var centres = new List<Vector3>(pads.Count);
            foreach (PadInfo p in pads) centres.Add(MountLocal(g, level, p));
            return BuildPlates(centres, HalfPad(g, level), level.wallHeight + Lift);
        }

        /// <summary>One mesh of square pad plates (fill + border) at the given grid-local centres, all at height y.</summary>
        public static Mesh BuildPlates(List<Vector3> centres, float half, float y)
        {
            var verts = new List<Vector3>(centres.Count * 20);
            var fill = new List<int>(centres.Count * 6);
            var edge = new List<int>(centres.Count * 24);
            float inner = half - EdgeWidth;

            foreach (Vector3 c in centres)
            {
                float cx = c.x, cz = c.z;
                Quad(verts, fill, cx - inner, cz - inner, cx + inner, cz + inner, y);                 // fill
                Quad(verts, edge, cx - half, cz - half, cx + half, cz - inner, y);                    // near border
                Quad(verts, edge, cx - half, cz + inner, cx + half, cz + half, y);                    // far border
                Quad(verts, edge, cx - half, cz - inner, cx - inner, cz + inner, y);                  // left border
                Quad(verts, edge, cx + inner, cz - inner, cx + half, cz + inner, y);                  // right border
            }

            var mesh = new Mesh { name = "PlacementPads", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.SetNormals(normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(fill, 0);
            mesh.SetTriangles(edge, 1);
            mesh.RecalculateBounds();
            return mesh;
        }

        // Upward-facing rectangle (same winding as the other floor quads).
        static void Quad(List<Vector3> verts, List<int> tris, float x0, float z0, float x1, float z1, float y)
        {
            int i = verts.Count;
            verts.Add(new Vector3(x0, y, z0));
            verts.Add(new Vector3(x0, y, z1));
            verts.Add(new Vector3(x1, y, z1));
            verts.Add(new Vector3(x1, y, z0));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
    }
}
