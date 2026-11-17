using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.UI
{
    /// <summary>
    /// The big "START WAVE" button at the bottom of the screen. Shown while the game waits for the player (before
    /// the first wave and between waves); pressing it (or Space) sends the next wave. Built in code, no scene wiring.
    /// </summary>
    public class StartWaveButton : MonoBehaviour
    {
        WaveSpawner spawner;
        GameObject button;
        TMP_Text label;
        int shownWave = -1;

        void Start()
        {
            spawner = FindAnyObjectByType<WaveSpawner>();
            if (spawner == null) { enabled = false; return; }

            var canvasGo = new GameObject("StartWaveCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 21;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            button = new GameObject("StartWave", typeof(RectTransform), typeof(Image), typeof(Button));
            var rt = (RectTransform)button.transform;
            rt.SetParent(canvasGo.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(380f, 84f);
            button.GetComponent<Image>().color = new Color(0.12f, 0.62f, 0.28f, 0.95f);
            button.GetComponent<Button>().onClick.AddListener(() => spawner.SkipCountdown());

            var textGo = new GameObject("Label", typeof(RectTransform));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = textGo.AddComponent<TextMeshProUGUI>();
            t.alignment = TextAlignmentOptions.Center;
            t.fontSize = 38;
            t.fontStyle = FontStyles.Bold;
            t.color = Color.white;
            t.raycastTarget = false;
            label = t;
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            bool show = gm != null && gm.State == GameState.Playing && spawner.ManualStart && !spawner.IsWaveActive;
            if (button.activeSelf != show) button.SetActive(show);
            if (show && gm.Wave + 1 != shownWave)
            {
                shownWave = gm.Wave + 1;
                label.text = $"START WAVE {shownWave}";
            }
        }
    }
}
