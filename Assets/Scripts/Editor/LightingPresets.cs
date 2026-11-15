using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZombieTD.EditorTools
{
    public enum LightingPreset { Day, Night }

    /// <summary>
    /// Switches the open scene between a bright day (see everything while building) and the night mood.
    /// Menu: Tools > Lighting > Day / Night. Also used by Setup Main Scene (defaults to Day for now).
    /// </summary>
    public static class LightingPresets
    {
        [MenuItem("Tools/Lighting/Day (see everything)")]
        static void DayMenu() => ApplyAndDirty(LightingPreset.Day);

        [MenuItem("Tools/Lighting/Night")]
        static void NightMenu() => ApplyAndDirty(LightingPreset.Night);

        static void ApplyAndDirty(LightingPreset preset)
        {
            Apply(preset);
            EditorSceneManager.MarkAllScenesDirty();
        }

        public static void Apply(LightingPreset preset)
        {
            bool day = preset == LightingPreset.Day;
            Light sun = FindOrCreateSun();

            sun.gameObject.name = day ? "Sun" : "Moonlight";
            sun.type = LightType.Directional;
            sun.color = day ? new Color(1f, 0.96f, 0.88f) : new Color(0.65f, 0.75f, 1f);
            sun.intensity = day ? 1.4f : 1.0f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = day ? 0.9f : 0.85f;
            sun.transform.rotation = day ? Quaternion.Euler(50f, -30f, 0f) : Quaternion.Euler(55f, -30f, 0f);

            RenderSettings.sun = sun;
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = day ? new Color(0.62f, 0.68f, 0.78f) : new Color(0.28f, 0.34f, 0.5f);
            RenderSettings.ambientEquatorColor = day ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.2f, 0.23f, 0.32f);
            RenderSettings.ambientGroundColor = day ? new Color(0.3f, 0.28f, 0.25f) : new Color(0.1f, 0.11f, 0.15f);

            // Night fog fades far skyscrapers into haze; it's off by day so everything is crisp.
            RenderSettings.fog = !day;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.07f, 0.09f, 0.14f);
            RenderSettings.fogStartDistance = 250f; // pushed out so the playfield stays clear
            RenderSettings.fogEndDistance = 700f;

            // Night grade (strong bloom, cool tint) is mostly faded out by day.
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
                v.weight = day ? 0.25f : 1f;

            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = day ? new Color(0.5f, 0.6f, 0.72f) : new Color(0.07f, 0.09f, 0.14f);
            }
        }

        static Light FindOrCreateSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return new GameObject("Sun").AddComponent<Light>();
        }
    }
}
