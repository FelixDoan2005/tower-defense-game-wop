using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Generates the ruined-city wall look procedurally: three black modern glass-tower facade styles
    /// (albedo + glossy map + emission) and a scorched rooftop texture, plus URP materials for them.
    /// Each facade texture covers 16x16 world units: an 8x8 grid of window bays with a structural column every
    /// 4 bays. Window states: dark glass, lit, blinds, broken panes (cracks + hole), blown-out and burning
    /// (orange glow with soot streaks above). Replace any PNG in Assets/Materials/CityWalls with your own art.
    /// </summary>
    public static class CityWallBuilder
    {
        const string Dir = "Assets/Materials/CityWalls";
        const int Size = 1024, Cells = 8, CellPx = Size / Cells;

        enum Win { Dark, Lit, Blinds, Broken, Fire, Blown, Emergency }

        class Style
        {
            public string name;
            public int seed;
            public float lit, blinds, broken, fire, blown, emergency;
        }

        // A: mostly intact tower  B: gutted and burning  C: dark, dead tower with a few survivors
        static readonly Style[] Styles =
        {
            new Style { name = "A", seed = 11, lit = 0.17f, blinds = 0.05f, broken = 0.08f, fire = 0.01f, blown = 0.03f, emergency = 0.02f },
            new Style { name = "B", seed = 23, lit = 0.04f, blinds = 0.02f, broken = 0.20f, fire = 0.07f, blown = 0.12f, emergency = 0.01f },
            new Style { name = "C", seed = 37, lit = 0.07f, blinds = 0.03f, broken = 0.12f, fire = 0.02f, blown = 0.08f, emergency = 0.06f },
        };

        static readonly Color[] LitColors =
        {
            new Color(1f, 0.82f, 0.55f), new Color(0.65f, 0.8f, 1f), new Color(0.95f, 0.95f, 0.9f), new Color(0.6f, 1f, 0.75f),
        };

        public class Result
        {
            public Material[] facades;
            public Material roof;
        }

        [MenuItem("Tools/City Walls/Regenerate and Apply to Scene")]
        static void RegenerateMenu()
        {
            Result r = EnsureMaterials(true);
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (level == null) { Debug.LogWarning("[ZombieTD] No LevelGenerator in the open scene; materials were generated only."); return; }
            Apply(level, r);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[ZombieTD] City walls regenerated and applied to the open scene.");
        }

        /// <summary>Wires the generated materials into a LevelGenerator (which rebuilds its preview).</summary>
        public static void Apply(LevelGenerator level, Result r)
        {
            var so = new SerializedObject(level);
            SerializedProperty arr = so.FindProperty("wallVariants");
            arr.arraySize = r.facades.Length;
            for (int i = 0; i < r.facades.Length; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = r.facades[i];
            so.FindProperty("wallMaterial").objectReferenceValue = r.facades[0];
            so.FindProperty("wallTopMaterial").objectReferenceValue = r.roof;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Creates textures/materials if missing (or always when force is set) and returns them.</summary>
        public static Result EnsureMaterials(bool force)
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();

            var result = new Result { facades = new Material[Styles.Length] };
            for (int i = 0; i < Styles.Length; i++)
            {
                string n = Styles[i].name;
                string alb = $"{Dir}/Facade_{n}_Albedo.png", emi = $"{Dir}/Facade_{n}_Emission.png", mg = $"{Dir}/Facade_{n}_Metal.png";
                if (force || !File.Exists(alb) || !File.Exists(emi) || !File.Exists(mg)) GenerateFacade(Styles[i], alb, emi, mg);
                result.facades[i] = FacadeMaterial($"CityWall_{n}", alb, emi, mg);
            }

            string roofPath = $"{Dir}/Roof_Albedo.png";
            if (force || !File.Exists(roofPath)) GenerateRoof(roofPath);
            result.roof = RoofMaterial("CityRoof", roofPath);
            AssetDatabase.SaveAssets();
            return result;
        }

        // ------------------------------------------------------------------ facade

        static void GenerateFacade(Style p, string albPath, string emiPath, string metPath)
        {
            var rng = new System.Random(p.seed);
            var st = new Win[Cells, Cells];
            var litCol = new Color[Cells, Cells];
            var hole = new Vector3[Cells, Cells];    // x,y = hole centre in cell space, z = radius
            var crackAng = new float[Cells, Cells];

            for (int cy = 0; cy < Cells; cy++)
            for (int cx = 0; cx < Cells; cx++)
            {
                float r = (float)rng.NextDouble();
                Win w = Win.Dark;
                float acc = 0f;
                if (r < (acc += p.lit)) w = Win.Lit;
                else if (r < (acc += p.blinds)) w = Win.Blinds;
                else if (r < (acc += p.broken)) w = Win.Broken;
                else if (r < (acc += p.fire) && cy > 0) w = Win.Fire;
                else if (r < (acc += p.blown)) w = Win.Blown;
                else if (r < (acc += p.emergency)) w = Win.Emergency;
                st[cx, cy] = w;
                litCol[cx, cy] = LitColors[rng.Next(LitColors.Length)] * (0.7f + 0.3f * (float)rng.NextDouble());
                hole[cx, cy] = new Vector3(0.3f + 0.4f * (float)rng.NextDouble(), 0.35f + 0.4f * (float)rng.NextDouble(), 0.16f + 0.2f * (float)rng.NextDouble());
                crackAng[cx, cy] = (float)rng.NextDouble() * Mathf.PI * 2f;
            }

            // Dirty streaks that run down whole columns of the facade.
            var grime = new float[Size / 6 + 1];
            for (int i = 0; i < grime.Length; i++) grime[i] = (float)rng.NextDouble() < 0.3f ? (float)rng.NextDouble() : 0f;

            var alb = new Color32[Size * Size];
            var emi = new Color32[Size * Size];
            var met = new Color32[Size * Size];

            for (int py = 0; py < Size; py++)
            for (int px = 0; px < Size; px++)
            {
                int cx = px / CellPx, cy = py / CellPx;
                float u = (px % CellPx + 0.5f) / CellPx, v = (py % CellPx + 0.5f) / CellPx;
                Win w = st[cx, cy];
                float noise = Hash(px, py, p.seed);

                Color a, e = Color.black;
                float metal, smooth;

                bool inWin = u > 0.09f && u < 0.91f && v > 0.20f && v < 0.90f;
                bool column = cx % 4 == 0 && u < 0.05f; // structural column at the left edge of every 4th bay
                if (!inWin || column)
                {
                    float shade = 0.05f + 0.012f * noise + (v < 0.2f ? 0.012f : 0f); // slightly lighter spandrel band
                    a = new Color(shade, shade * 1.03f, shade * 1.1f);
                    metal = 0.8f; smooth = 0.4f;

                    if (w == Win.Fire && !column)
                    {
                        // Fire light spilling onto the frame around the window.
                        float dx = Mathf.Max(0.09f - u, u - 0.91f, 0f), dy = Mathf.Max(0.20f - v, v - 0.90f, 0f);
                        float halo = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / 0.13f);
                        e = new Color(1f, 0.35f, 0.06f) * (halo * halo * 0.55f);
                    }
                }
                else
                {
                    // Dark reflective glass with a soft diagonal sheen.
                    float t = Mathf.Repeat(u * 0.7f + v * 0.5f + cx * 0.37f + cy * 0.21f, 1f);
                    float band = Mathf.Clamp01(1f - Mathf.Abs(t - 0.5f) * 5f);
                    a = new Color(0.014f + band * 0.02f, 0.019f + band * 0.028f, 0.03f + band * 0.045f);
                    metal = 0.7f; smooth = 0.95f;

                    switch (w)
                    {
                        case Win.Lit:
                        {
                            Color c = litCol[cx, cy];
                            float k = 0.45f + 0.55f * v;
                            if (v > 0.8f) k *= 1.25f;                                  // ceiling light strip
                            if (v < 0.42f && v > 0.3f && Mathf.Repeat(u * 3f + cx, 1f) > 0.4f) k *= 0.35f; // desks
                            e = c * k;
                            break;
                        }
                        case Win.Blinds:
                            e = litCol[cx, cy] * ((py / 5) % 2 == 0 ? 0.7f : 0.12f);
                            break;
                        case Win.Emergency:
                            e = new Color(1f, 0.08f, 0.05f) * (0.35f + 0.25f * v);
                            break;
                        case Win.Broken:
                            ShadeBroken(u, v, hole[cx, cy], crackAng[cx, cy], litCol[cx, cy], noise, ref a, ref e, ref smooth, ref metal, px, py, p.seed);
                            break;
                        case Win.Fire:
                        {
                            // Glass gone, whole bay glowing: hot at the bottom, flickering bands.
                            float flick = 0.75f + 0.5f * Hash(px / 6, py / 14, p.seed + 3);
                            Color hot = Color.Lerp(new Color(1f, 0.62f, 0.14f), new Color(1f, 0.16f, 0.02f), v);
                            e = hot * ((0.5f + 0.7f * (1f - v)) * flick);
                            a = new Color(0.01f, 0.01f, 0.01f); smooth = 0.1f; metal = 0f;
                            break;
                        }
                        case Win.Blown:
                            // Empty burnt-out bay: black, with the odd glowing ember.
                            a = new Color(0.006f, 0.006f, 0.007f); smooth = 0.1f; metal = 0f;
                            if (Hash(px / 3, py / 3, p.seed + 9) > 0.985f) e = new Color(1f, 0.4f, 0.1f) * 0.8f;
                            else e = new Color(0.12f, 0.03f, 0.01f) * (1f - v) * 0.5f;
                            break;
                    }
                }

                // Soot above bays that are on fire or burnt out (wraps so the texture tiles).
                Win below = st[cx, (cy + Cells - 1) % Cells];
                if (below == Win.Fire || below == Win.Blown)
                {
                    float k = Mathf.Clamp01(1f - Mathf.Abs(u - 0.5f) * 1.7f) * Mathf.Clamp01(1.25f - v * 1.25f);
                    a *= 1f - 0.75f * k;
                    smooth *= 1f - 0.6f * k;
                }

                // Weathering: streaks under the sill and down whole columns.
                float g = grime[px / 6];
                if (g > 0f) { a *= 1f - g * 0.4f * (1f - v * 0.5f); smooth *= 1f - g * 0.3f; }

                int idx = py * Size + px;
                alb[idx] = ToColor32(a);
                emi[idx] = ToColor32(e);
                met[idx] = new Color32((byte)(Mathf.Clamp01(metal) * 255f), 0, 0, (byte)(Mathf.Clamp01(smooth) * 255f));
            }

            WritePng(albPath, alb, Size, Size);
            WritePng(emiPath, emi, Size, Size);
            WritePng(metPath, met, Size, Size);
            ConfigureImporter(albPath, true);
            ConfigureImporter(emiPath, true);
            ConfigureImporter(metPath, false); // data texture: no sRGB
        }

        // Shattered pane: jagged hole around a centre, radial cracks and a ring crack on what's left.
        static void ShadeBroken(float u, float v, Vector3 hole, float crackStart, Color lit, float noise,
            ref Color a, ref Color e, ref float smooth, ref float metal, int px, int py, int seed)
        {
            float dx = (u - hole.x) * 1.0f, dy = (v - hole.y) * 1.2f;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            float jag = 0.7f + 0.6f * Hash((int)((ang + Mathf.PI) * 6f), (int)(hole.x * 97f), seed + 5);
            float r = hole.z * jag;

            if (d < r)
            {
                a = new Color(0.007f, 0.007f, 0.008f); smooth = 0.1f; metal = 0f;
                if (hole.x > 0.5f) e = lit * 0.18f * (0.6f + 0.4f * v); // some broken bays still have a dim light behind
                return;
            }

            bool crack = false;
            for (int k = 0; k < 5 && !crack; k++)
            {
                float da = Mathf.Abs(Mathf.DeltaAngle(ang * Mathf.Rad2Deg, (crackStart + k * 1.25f) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
                if (da < 1.4f && d * Mathf.Sin(da) < 0.007f && d < 0.7f) crack = true;
            }
            if (!crack && Mathf.Abs(d - (r + 0.09f)) < 0.005f) crack = true;
            if (crack) { a = new Color(0.34f, 0.37f, 0.4f); smooth = 1f; metal = 0f; }
        }

        // ------------------------------------------------------------------ roof

        static void GenerateRoof(string path)
        {
            const int S = 512;
            var rng = new System.Random(5);
            var px = new Color32[S * S];
            var scorch = new Vector3[10];
            for (int i = 0; i < scorch.Length; i++)
                scorch[i] = new Vector3((float)rng.NextDouble() * S, (float)rng.NextDouble() * S, 25f + (float)rng.NextDouble() * 60f);

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float n = Hash(x, y, 7);
                float c = 0.2f + 0.05f * n; // light concrete so lit roofs are easy to read
                if (x % 128 < 2 || y % 128 < 2) c += 0.035f; // panel seams
                if (Hash(x / 4, y / 4, 8) > 0.97f) c += 0.05f; // gravel highlights
                foreach (Vector3 s in scorch)
                {
                    float dx = Mathf.Min(Mathf.Abs(x - s.x), S - Mathf.Abs(x - s.x)), dy = Mathf.Min(Mathf.Abs(y - s.y), S - Mathf.Abs(y - s.y));
                    float k = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / s.z);
                    c *= 1f - 0.7f * k * k;
                }
                px[y * S + x] = ToColor32(new Color(c, c * 1.02f, c * 1.06f));
            }
            WritePng(path, px, S, S);
            ConfigureImporter(path, true);
        }

        // ------------------------------------------------------------------ materials

        static Material FacadeMaterial(string name, string alb, string emi, string met)
        {
            Material m = LoadOrCreate(name);
            m.SetTexture("_BaseMap", Load(alb));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", Load(met));
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetTexture("_EmissionMap", Load(emi));
            m.SetColor("_EmissionColor", Color.white * 2.5f); // >1 so lit windows and fires bloom
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material RoofMaterial(string name, string alb)
        {
            Material m = LoadOrCreate(name);
            m.SetTexture("_BaseMap", Load(alb));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.12f);
            m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material LoadOrCreate(string name)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ------------------------------------------------------------------ helpers

        static Texture2D Load(string path) => AssetDatabase.LoadAssetAtPath<Texture2D>(path);

        static void WritePng(string path, Color32[] pixels, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        static void ConfigureImporter(string path, bool sRGB)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) return;
            imp.sRGBTexture = sRGB;
            imp.alphaSource = sRGB ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = false;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;
            imp.mipmapEnabled = true;
            imp.maxTextureSize = 2048;
            imp.SaveAndReimport();
        }

        static Color32 ToColor32(Color c) => new Color32(
            (byte)(Mathf.Clamp01(c.r) * 255f + 0.5f), (byte)(Mathf.Clamp01(c.g) * 255f + 0.5f),
            (byte)(Mathf.Clamp01(c.b) * 255f + 0.5f), 255);

        // Cheap deterministic hash noise in 0..1.
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777215f;
            }
        }
    }
}
