using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Keeps Game view sharp: 4x MSAA on the active URP asset and SMAA on the main camera.
    /// Only raises settings that are lower than this, so anything you set higher is left alone.
    /// </summary>
    static class RenderQualityFixes
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            EditorApplication.delayCall += FixAsset;
            EditorApplication.delayCall += FixCamera;
            EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += FixCamera;
        }

        static void FixAsset()
        {
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp) || urp.msaaSampleCount >= 4) return;
            urp.msaaSampleCount = 4;
            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();
            Debug.Log("[ZombieTD] URP MSAA set to 4x.");
        }

        static void FixCamera()
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data.antialiasing != AntialiasingMode.None) return;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;
            EditorSceneManager.MarkSceneDirty(cam.gameObject.scene);
            Debug.Log("[ZombieTD] Main camera anti-aliasing set to SMAA (save the scene to keep it).");
        }
    }
}
