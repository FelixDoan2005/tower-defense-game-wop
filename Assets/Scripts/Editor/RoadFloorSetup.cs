using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Builds the RoadFloor material from the sci-fi kit's Floor_02 texture sheet (albedo, normal, metal, AO),
    /// tinted down for the night city, and assigns it to the scene's LevelGenerator.
    /// </summary>
    public static class RoadFloorSetup
    {
        const string MaterialPath = "Assets/Materials/RoadFloor.mat";

        [MenuItem("Tools/Build Kit Road Floor")]
        static void BuildMenu()
        {
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            Material m = EnsureMaterial();
            if (m == null) return;
            if (level == null) { Debug.LogWarning("[ZombieTD] No LevelGenerator in the open scene; material created only."); return; }
            Apply(level, m);
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[ZombieTD] Kit road floor applied.");
        }

        public static void Apply(LevelGenerator level, Material floor)
        {
            var so = new SerializedObject(level);
            so.FindProperty("floorMaterial").objectReferenceValue = floor;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Returns the floor material, creating it from the kit textures if needed (null if the kit is missing).</summary>
        public static Material EnsureMaterial()
        {
            Texture2D albedo = Find("HD_Floor_02_Dif_Style_02_Alb");
            if (albedo == null)
            {
                Debug.LogError("[ZombieTD] Kit texture HD_Floor_02_Dif_Style_02_Alb not found; is Assets/Creepy_Cat imported?");
                return null;
            }

            var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, MaterialPath);
            }

            m.SetTexture("_BaseMap", albedo);
            m.SetColor("_BaseColor", new Color(0.62f, 0.64f, 0.68f)); // darker than the raw kit so it sits in the night city

            Texture2D normal = Find("HD_Floor_02_Dif_Style_02_Norm");
            m.SetTexture("_BumpMap", normal);
            SetKeyword(m, "_NORMALMAP", normal != null);

            Texture2D metal = Find("HD_Floor_02_Dif_Style_02_Metal");
            m.SetTexture("_MetallicGlossMap", metal);
            m.SetFloat("_Metallic", metal != null ? 1f : 0f);
            m.SetFloat("_Smoothness", metal != null ? 0.85f : 0.4f);
            SetKeyword(m, "_METALLICSPECGLOSSMAP", metal != null);

            Texture2D ao = Find("HD_Floor_02_Dif_Style_02_AO");
            m.SetTexture("_OcclusionMap", ao);
            SetKeyword(m, "_OCCLUSIONMAP", ao != null);

            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        static Texture2D Find(string name)
        {
            foreach (string guid in AssetDatabase.FindAssets(name + " t:Texture2D"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }
    }
}
