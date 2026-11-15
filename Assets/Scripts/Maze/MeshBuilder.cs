using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieTD.Maze
{
    public enum BoxFace { PosX, NegX, PosZ, NegZ, Top }

    /// <summary>
    /// Accumulates axis-aligned box faces into one mesh. Submeshes: 0..variants-1 = wall facades (one per
    /// facade style), last = roofs/parapets. UVs are world-space so textures tile seamlessly across
    /// neighbouring cells: sides repeat every SideTile units, roofs every TopTile units. Load time only.
    /// </summary>
    public class MeshBuilder
    {
        /// <summary>World units covered by one facade texture (both directions).</summary>
        public float SideTile = 16f;
        /// <summary>World units covered by one roof texture.</summary>
        public float TopTile = 8f;

        readonly List<Vector3> verts = new List<Vector3>(4096);
        readonly List<Vector3> normals = new List<Vector3>(4096);
        readonly List<Vector2> uvs = new List<Vector2>(4096);
        readonly List<int>[] sides;
        readonly List<int> roofs = new List<int>(4096);

        public MeshBuilder(int facadeVariants = 1)
        {
            sides = new List<int>[Mathf.Max(1, facadeVariants)];
            for (int i = 0; i < sides.Length; i++) sides[i] = new List<int>(4096);
        }

        public int FacadeVariants => sides.Length;

        /// <summary>Top and four sides of a box, all in the roof submesh (parapets, rooftop clutter).</summary>
        public void AddRoofBox(Vector3 min, Vector3 max)
        {
            AddFace(BoxFace.Top, min, max, 0, true);
            AddFace(BoxFace.PosX, min, max, 0, true);
            AddFace(BoxFace.NegX, min, max, 0, true);
            AddFace(BoxFace.PosZ, min, max, 0, true);
            AddFace(BoxFace.NegZ, min, max, 0, true);
        }

        /// <summary>
        /// Adds one outward-facing face of the box spanning min..max. Tops always use the roof submesh;
        /// sides use facade variant `variant` unless roofStyle is set.
        /// </summary>
        public void AddFace(BoxFace face, Vector3 min, Vector3 max, int variant = 0, bool roofStyle = false)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            float st = roofStyle ? TopTile : SideTile;
            List<int> list = roofStyle ? roofs : sides[Mathf.Clamp(variant, 0, sides.Length - 1)];

            switch (face)
            {
                case BoxFace.Top:
                    Quad(new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0),
                        Vector3.up, x0 / TopTile, x1 / TopTile, z0 / TopTile, z1 / TopTile, roofs);
                    break;
                case BoxFace.PosX:
                    Quad(new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x1, y0, z1),
                        Vector3.right, z0 / st, z1 / st, y0 / st, y1 / st, list);
                    break;
                case BoxFace.NegX: // seen from -X, "right" is -Z, so U runs backwards
                    Quad(new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), new Vector3(x0, y0, z0),
                        Vector3.left, -z1 / st, -z0 / st, y0 / st, y1 / st, list);
                    break;
                case BoxFace.PosZ: // seen from +Z, "right" is -X
                    Quad(new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), new Vector3(x0, y0, z1),
                        Vector3.forward, -x1 / st, -x0 / st, y0 / st, y1 / st, list);
                    break;
                case BoxFace.NegZ:
                    Quad(new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y0, z0),
                        Vector3.back, x0 / st, x1 / st, y0 / st, y1 / st, list);
                    break;
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = sides.Length + 1;
            for (int i = 0; i < sides.Length; i++) mesh.SetTriangles(sides[i], i);
            mesh.SetTriangles(roofs, sides.Length);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        // Corners are bottom-left, top-left, top-right, bottom-right seen from the front (clockwise).
        void Quad(Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br, Vector3 n,
            float u0, float u1, float v0, float v1, List<int> tris)
        {
            int i = verts.Count;
            verts.Add(bl); verts.Add(tl); verts.Add(tr); verts.Add(br);
            normals.Add(n); normals.Add(n); normals.Add(n); normals.Add(n);
            uvs.Add(new Vector2(u0, v0)); uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0));
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
        }
    }
}
