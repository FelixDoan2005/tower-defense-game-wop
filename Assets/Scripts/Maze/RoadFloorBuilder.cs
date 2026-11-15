using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>
    /// Builds the road surface from the sci-fi kit's Floor_02 texture sheet (one combined mesh).
    /// Corridor levels get a symmetric pattern of 2-unit strips (plain / dashed rails / yellow grates)
    /// running along the road; other layouts get bordered square plates on every road tile.
    /// UV rectangles below are positions inside that sheet. Load time only; grid-local coordinates.
    /// </summary>
    public static class RoadFloorBuilder
    {
        // Strip tiles live in the sheet's top-right quarter; their long axis runs along V.
        static readonly Vector2 StripV = new Vector2(0.522f, 0.979f);
        static readonly Vector2 RailsU = new Vector2(0.508f, 0.660f);   // dashed rails on both edges
        static readonly Vector2 GrateU = new Vector2(0.675f, 0.826f);   // yellow grate steps
        static readonly Vector2 PlainU = new Vector2(0.839f, 0.992f);   // plain panel, thin rail lines
        // Bordered square plate: top-left quarter.
        static readonly Rect Plate = new Rect(0.004f, 0.506f, 0.489f, 0.489f);

        const float StripWidth = 2f;    // world units across the road, per strip
        const float StripLength = 6f;   // world units along the road, per repeat
        const float Lift = 0.02f;       // above the ground slab to avoid z-fighting

        enum Strip { Plain, Rails, Grate }

        public static Mesh Build(MazeGrid grid, LevelData level)
        {
            var verts = new List<Vector3>(2048);
            var uvs = new List<Vector2>(2048);
            var tris = new List<int>(3072);

            if (level.layout == LevelLayout.Corridor) Strips(grid, verts, uvs, tris);
            else Plates(grid, verts, uvs, tris);

            if (verts.Count == 0) return null;
            var mesh = new Mesh { name = "RoadFloor", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            var normals = new Vector3[verts.Count];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // ---------------------------------------------------------------- corridor strips

        static void Strips(MazeGrid g, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            if (!RoadBounds(g, out int minX, out int minZ, out int maxX, out int maxZ)) return;
            float cs = g.CellSize;
            float x0 = minX * cs, x1 = (maxX + 1) * cs, z0 = minZ * cs, z1 = (maxZ + 1) * cs;

            // The road runs along its longer side; "along" and "across" are measured on that axis.
            bool alongX = (x1 - x0) >= (z1 - z0);
            float aStart = alongX ? x0 : z0, aEnd = alongX ? x1 : z1;
            float cStart = alongX ? z0 : x0, cEnd = alongX ? z1 : x1;

            int strips = Mathf.Max(1, Mathf.RoundToInt((cEnd - cStart) / StripWidth));
            float sw = (cEnd - cStart) / strips;
            int segments = Mathf.CeilToInt((aEnd - aStart) / StripLength);

            for (int s = 0; s < segments; s++)
            {
                float a0 = aStart + s * StripLength, a1 = Mathf.Min(a0 + StripLength, aEnd);
                float frac = (a1 - a0) / StripLength; // last piece may be cut short
                bool crossing = Hash(s, 3) % 5 == 0;  // now and then the middle becomes a grate crossing

                for (int i = 0; i < strips; i++)
                {
                    float c0 = cStart + i * sw, c1 = c0 + sw;
                    Vector2 uRange = StripU(PatternAt(i, strips, crossing));
                    float vEnd = Mathf.Lerp(StripV.x, StripV.y, frac);

                    // Strip's V axis lies along the road; U across it.
                    Vector3 p00 = Pos(alongX, a0, c0), p01 = Pos(alongX, a0, c1), p11 = Pos(alongX, a1, c1), p10 = Pos(alongX, a1, c0);
                    Quad(verts, uvs, tris, p00, p01, p11, p10,
                        new Vector2(uRange.x, StripV.x), new Vector2(uRange.y, StripV.x),
                        new Vector2(uRange.y, vEnd), new Vector2(uRange.x, vEnd), alongX);
                }
            }
        }

        // Mirrored across the road: plain, rails, plain, grate, plain... centred on the middle.
        static Strip PatternAt(int i, int n, bool crossing)
        {
            int j = Mathf.Min(i, n - 1 - i);
            if (crossing && j >= (n - 1) / 2 - 1) return Strip.Grate;
            switch (j % 4)
            {
                case 1: return Strip.Rails;
                case 3: return Strip.Grate;
                default: return Strip.Plain;
            }
        }

        static Vector2 StripU(Strip s) => s == Strip.Rails ? RailsU : s == Strip.Grate ? GrateU : PlainU;

        static Vector3 Pos(bool alongX, float along, float across) =>
            alongX ? new Vector3(along, Lift, across) : new Vector3(across, Lift, along);

        // ---------------------------------------------------------------- other layouts: plates

        static void Plates(MazeGrid g, List<Vector3> verts, List<Vector2> uvs, List<int> tris)
        {
            float cs = g.CellSize;
            for (int z = 0; z < g.Height; z++)
            for (int x = 0; x < g.Width; x++)
            {
                if (!g.IsWalkable(x, z)) continue;
                int rot = Hash(x, z) % 4; // plates are symmetric, so random quarter turns break up repetition
                Vector2[] c =
                {
                    new Vector2(Plate.xMin, Plate.yMin), new Vector2(Plate.xMin, Plate.yMax),
                    new Vector2(Plate.xMax, Plate.yMax), new Vector2(Plate.xMax, Plate.yMin),
                };
                Vector3 a = new Vector3(x * cs, Lift, z * cs), b = new Vector3(x * cs, Lift, (z + 1) * cs);
                Vector3 d = new Vector3((x + 1) * cs, Lift, (z + 1) * cs), e = new Vector3((x + 1) * cs, Lift, z * cs);
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(d); verts.Add(e);
                for (int k = 0; k < 4; k++) uvs.Add(c[(k + rot) % 4]);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
                tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
            }
        }

        // ---------------------------------------------------------------- helpers

        // Corners p00/p01/p11/p10 are (along0,across0), (along0,across1), (along1,across1), (along1,across0).
        // Winding is flipped when the road runs along Z so the face always points up.
        static void Quad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 p00, Vector3 p01, Vector3 p11, Vector3 p10,
            Vector2 uv00, Vector2 uv01, Vector2 uv11, Vector2 uv10, bool alongX)
        {
            int i = verts.Count;
            verts.Add(p00); verts.Add(p01); verts.Add(p11); verts.Add(p10);
            uvs.Add(uv00); uvs.Add(uv01); uvs.Add(uv11); uvs.Add(uv10);
            if (alongX) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
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

        static int Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
