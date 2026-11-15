using UnityEditor;
using UnityEngine;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Converts built-in-pipeline materials (Standard / Legacy / Mobile shaders) under a folder to URP Lit,
    /// keeping textures, tint, normal/metal/AO/emission maps and transparency. Custom shaders are skipped.
    /// Only touches materials still on a built-in shader, so it is safe to run repeatedly.
    /// </summary>
    public static class KitMaterialConverter
    {
        const string KitFolder = "Assets/Creepy_Cat";

        [MenuItem("Tools/Convert Scifi Kit Materials to URP")]
        static void ConvertMenu()
        {
            Debug.Log($"[ZombieTD] {ConvertFolder(KitFolder)}");
        }

        /// <summary>Returns a short summary of what was converted.</summary>
        public static string ConvertFolder(string folder)
        {
            if (!AssetDatabase.IsValidFolder(folder)) return $"{folder} not found, nothing to convert.";
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return "URP Lit shader not found.";

            int converted = 0, skipped = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || !IsBuiltIn(m.shader)) { if (m != null && m.shader != lit) skipped++; continue; }
                Convert(m, lit);
                converted++;
            }
            AssetDatabase.SaveAssets();
            return $"Converted {converted} material(s) to URP Lit; skipped {skipped} with custom shaders.";
        }

        static bool IsBuiltIn(Shader s)
        {
            string n = s.name;
            return n == "Standard" || n == "Standard (Specular setup)" || n.StartsWith("Legacy Shaders/")
                || n.StartsWith("Mobile/") || n == "Diffuse" || n == "Bumped Diffuse";
        }

        static void Convert(Material m, Shader lit)
        {
            // Read everything first: switching shader hides the old properties.
            Texture main = Tex(m, "_MainTex");
            Vector2 tiling = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            Vector2 offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture normal = Tex(m, "_BumpMap");
            float bumpScale = m.HasProperty("_BumpScale") ? m.GetFloat("_BumpScale") : 1f;
            Texture metal = Tex(m, "_MetallicGlossMap");
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float gloss = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0.5f;
            float glossScale = m.HasProperty("_GlossMapScale") ? m.GetFloat("_GlossMapScale") : 1f;
            Texture occlusion = Tex(m, "_OcclusionMap");
            float occlusionStrength = m.HasProperty("_OcclusionStrength") ? m.GetFloat("_OcclusionStrength") : 1f;
            bool emissive = m.IsKeywordEnabled("_EMISSION");
            Texture emissionMap = Tex(m, "_EmissionMap");
            Color emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            int mode = m.HasProperty("_Mode") ? Mathf.RoundToInt(m.GetFloat("_Mode")) : 0; // 0 opaque 1 cutout 2 fade 3 transparent
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;

            m.shader = lit;
            m.SetTexture("_BaseMap", main);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);

            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bumpScale);
            SetKeyword(m, "_NORMALMAP", normal != null);

            m.SetTexture("_MetallicGlossMap", metal);
            m.SetFloat("_Metallic", metal != null ? 1f : metallic);
            m.SetFloat("_Smoothness", metal != null ? glossScale : gloss);
            SetKeyword(m, "_METALLICSPECGLOSSMAP", metal != null);

            m.SetTexture("_OcclusionMap", occlusion);
            m.SetFloat("_OcclusionStrength", occlusionStrength);
            SetKeyword(m, "_OCCLUSIONMAP", occlusion != null);

            bool emits = emissive && (emissionMap != null || emission.maxColorComponent > 0.01f);
            m.SetTexture("_EmissionMap", emissionMap);
            m.SetColor("_EmissionColor", emission);
            SetKeyword(m, "_EMISSION", emits);
            m.globalIlluminationFlags = emits ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.None;

            ApplyBlendMode(m, mode, cutoff);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }

        static void ApplyBlendMode(Material m, int mode, float cutoff)
        {
            bool cutout = mode == 1, transparent = mode >= 2;
            m.SetFloat("_Surface", transparent ? 1f : 0f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_SrcBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.SrcAlpha : 1f);
            m.SetFloat("_DstBlend", transparent ? (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : 0f);
            m.SetFloat("_ZWrite", transparent ? 0f : 1f);
            SetKeyword(m, "_SURFACE_TYPE_TRANSPARENT", transparent);
            SetKeyword(m, "_ALPHATEST_ON", cutout);
            m.SetOverrideTag("RenderType", transparent ? "Transparent" : cutout ? "TransparentCutout" : "Opaque");
            m.renderQueue = transparent ? 3000 : cutout ? 2450 : 2000;
        }

        static Texture Tex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;

        static void SetKeyword(Material m, string keyword, bool on)
        {
            if (on) m.EnableKeyword(keyword);
            else m.DisableKeyword(keyword);
        }
    }
}
