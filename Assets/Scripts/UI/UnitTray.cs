using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ZombieTD.Core;
using ZombieTD.Towers;

namespace ZombieTD.UI
{
    /// <summary>
    /// The unit tray along the top of the screen: one card per buildable unit, built in code at startup so it
    /// needs no scene wiring. Drag a card onto a glowing roof pad to place that unit.
    /// </summary>
    public class UnitTray : MonoBehaviour
    {
        [SerializeField] UnitPlacer placer;

        [Header("Layout")]
        [SerializeField, Tooltip("Distance from the top of the screen (reference pixels at 1920x1080).")] float topOffset = 95f;
        [SerializeField] Vector2 cardSize = new Vector2(150f, 135f);
        [SerializeField] float spacing = 14f;

        [Header("Colours")]
        [SerializeField] Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        [SerializeField] Color cardColor = new Color(0.1f, 0.16f, 0.2f, 0.95f);
        [SerializeField] Color costColor = new Color(1f, 0.85f, 0.3f);

        readonly List<UnitCard> cards = new List<UnitCard>();
        int lastVersion = -1;

        void Start()
        {
            if (placer == null) placer = FindAnyObjectByType<UnitPlacer>(); // one-time fallback
            WeaponData[] units = placer != null ? placer.Buildable : null;
            if (units == null || units.Length == 0) return;

            EnsureEventSystem();
            RectTransform tray = BuildCanvasAndPanel(units.Length);
            placer.TrayRect = tray;
            foreach (WeaponData u in units) cards.Add(BuildCard(tray, u));
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Version == lastVersion) return;
            lastVersion = gm.Version;
            foreach (UnitCard c in cards) c.Refresh(gm.CanAfford(c.Data.cost));
        }

        // ---------------------------------------------------------------- UI building

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        RectTransform BuildCanvasAndPanel(int count)
        {
            var canvasGo = new GameObject("UnitTrayCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = new GameObject("Tray", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup));
            var rt = (RectTransform)panel.transform;
            rt.SetParent(canvasGo.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);
            float pad = 12f;
            rt.sizeDelta = new Vector2(count * cardSize.x + (count - 1) * spacing + pad * 2f, cardSize.y + pad * 2f);
            panel.GetComponent<Image>().color = panelColor;

            HorizontalLayoutGroup layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset((int)pad, (int)pad, (int)pad, (int)pad);
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            return rt;
        }

        UnitCard BuildCard(RectTransform tray, WeaponData data)
        {
            var go = new GameObject(data.displayName + "Card", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(tray, false);
            go.GetComponent<Image>().color = cardColor; // raycast target: receives the drag events
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredWidth = cardSize.x;
            le.preferredHeight = cardSize.y;

            if (data.icon != null)
            {
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var irt = (RectTransform)icon.transform;
                irt.SetParent(go.transform, false);
                irt.anchorMin = new Vector2(0.15f, 0.38f);
                irt.anchorMax = new Vector2(0.85f, 0.92f);
                irt.offsetMin = irt.offsetMax = Vector2.zero;
                Image img = icon.GetComponent<Image>();
                img.sprite = data.icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            TMP_Text name = Label(go.transform, "Name", data.displayName, 26, Color.white,
                new Vector2(0f, 0.36f), new Vector2(1f, data.icon != null ? 0.6f : 0.85f), data.icon == null);
            TMP_Text cost = Label(go.transform, "Cost", "$" + data.cost, 30, costColor,
                new Vector2(0f, 0.02f), new Vector2(1f, 0.36f), false);
            name.fontStyle = FontStyles.Bold;
            cost.fontStyle = FontStyles.Bold;

            UnitCard card = go.AddComponent<UnitCard>();
            card.Init(data, placer);
            return card;
        }

        static TMP_Text Label(Transform parent, string name, string text, float size, Color color,
            Vector2 anchorMin, Vector2 anchorMax, bool centreVertically)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = centreVertically ? TextAlignmentOptions.Center : TextAlignmentOptions.Midline;
            t.raycastTarget = false; // let the card underneath take the drag
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }
    }
}
