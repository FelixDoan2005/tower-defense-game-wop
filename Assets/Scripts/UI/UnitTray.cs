using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ZombieTD.Core;
using ZombieTD.Maze;
using ZombieTD.Towers;

namespace ZombieTD.UI
{
    /// <summary>
    /// The unit tray along the top of the screen: compact cards grouped under small headers (Soldiers / Walls) plus a
    /// pad panel, built in code at startup so it needs no scene wiring. Click a pad, then click cards to add units to
    /// it (one per click); Clear empties the pad and refunds it before the wave starts.
    /// </summary>
    public class UnitTray : MonoBehaviour
    {
        [SerializeField] UnitPlacer placer;

        [Header("Layout")]
        [SerializeField, Tooltip("Distance from the top of the screen (reference pixels at 1920x1080).")] float topOffset = 95f;
        [SerializeField] Vector2 cardSize = new Vector2(112f, 92f);
        [SerializeField] float spacing = 8f;
        [SerializeField] float groupSpacing = 22f;

        [Header("Colours")]
        [SerializeField] Color panelColor = new Color(0.04f, 0.05f, 0.07f, 0.8f);
        [SerializeField] Color cardColor = new Color(0.1f, 0.16f, 0.2f, 0.95f);
        [SerializeField] Color costColor = new Color(1f, 0.85f, 0.3f);
        [SerializeField] Color headerColor = new Color(1f, 1f, 1f, 0.55f);

        readonly List<UnitCard> cards = new List<UnitCard>();
        TMP_Text padText;
        Button clearButton;
        CanvasGroup clearGroup;
        WallSlot shownPad;
        int shownCount = -1;

        void Start()
        {
            if (placer == null) placer = FindAnyObjectByType<UnitPlacer>(); // one-time fallback
            WeaponData[] units = placer != null ? placer.Buildable : null;
            if (units == null || units.Length == 0) return;

            EnsureEventSystem();
            if (GetComponent<StartWaveButton>() == null) gameObject.AddComponent<StartWaveButton>(); // "Start wave" button: build first, then go
            RectTransform tray = BuildCanvasAndPanel();
            placer.TrayRect = tray;

            BuildGroup(tray, "SOLDIERS", units, PadKind.Soldier);
            BuildGroup(tray, "WALLS", units, PadKind.Wall);
            BuildPadPanel(tray);
        }

        void Update()
        {
            if (placer == null) return;

            // A handful of cards: cheap enough to check every frame.
            foreach (UnitCard c in cards) c.Refresh(placer.CanAddToSelected(c.Data));

            WallSlot pad = placer.Selected;
            int count = pad != null ? pad.Count : -1;
            if (pad != shownPad || count != shownCount)
            {
                shownPad = pad;
                shownCount = count;
                padText.text = pad == null ? "Click a pad" : $"{pad.Count} / {pad.Capacity}";
            }
            bool canClear = placer.CanClearSelected;
            clearButton.interactable = canClear;
            clearGroup.alpha = canClear ? 1f : 0.4f;
        }

        // ---------------------------------------------------------------- UI building

        static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        RectTransform BuildCanvasAndPanel()
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

            // The panel fits its content, so it never grows wider than the cards it holds.
            var panel = new GameObject("Tray", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var rt = (RectTransform)panel.transform;
            rt.SetParent(canvasGo.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -topOffset);
            panel.GetComponent<Image>().color = panelColor;

            HorizontalLayoutGroup layout = panel.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = groupSpacing;
            layout.padding = new RectOffset(12, 12, 8, 10);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            ContentSizeFitter fit = panel.GetComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return rt;
        }

        // One column per group: a small header on top, the group's cards in a row underneath.
        void BuildGroup(RectTransform tray, string title, WeaponData[] units, PadKind kind)
        {
            var members = new List<WeaponData>();
            foreach (WeaponData u in units) if (u != null && u.padKind == kind) members.Add(u);
            if (members.Count == 0) return;

            var group = new GameObject(title + "Group", typeof(RectTransform), typeof(VerticalLayoutGroup));
            group.transform.SetParent(tray, false);
            VerticalLayoutGroup v = group.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;

            TMP_Text header = Label(group.transform, "Header", title, 16, headerColor, TextAlignmentOptions.Center);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 4f;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            var row = new GameObject("Row", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(group.transform, false);
            HorizontalLayoutGroup h = row.GetComponent<HorizontalLayoutGroup>();
            h.spacing = spacing;
            h.childAlignment = TextAnchor.UpperCenter;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            foreach (WeaponData u in members) cards.Add(BuildCard((RectTransform)row.transform, u));
        }

        // The selected pad: how full it is, and the Clear button.
        void BuildPadPanel(RectTransform tray)
        {
            var group = new GameObject("PadGroup", typeof(RectTransform), typeof(VerticalLayoutGroup));
            group.transform.SetParent(tray, false);
            VerticalLayoutGroup v = group.GetComponent<VerticalLayoutGroup>();
            v.spacing = 4f;
            v.childAlignment = TextAnchor.UpperCenter;
            v.childControlWidth = v.childControlHeight = true;
            v.childForceExpandWidth = v.childForceExpandHeight = false;

            TMP_Text header = Label(group.transform, "Header", "SELECTED PAD", 16, headerColor, TextAlignmentOptions.Center);
            header.fontStyle = FontStyles.Bold;
            header.characterSpacing = 4f;
            header.gameObject.AddComponent<LayoutElement>().preferredHeight = 20f;

            padText = Label(group.transform, "Count", "Click a pad", 22, Color.white, TextAlignmentOptions.Center);
            padText.fontStyle = FontStyles.Bold;
            var textLe = padText.gameObject.AddComponent<LayoutElement>();
            textLe.preferredHeight = 34f;
            textLe.preferredWidth = 150f;

            var btn = new GameObject("ClearButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(CanvasGroup));
            btn.transform.SetParent(group.transform, false);
            btn.GetComponent<Image>().color = new Color(0.65f, 0.16f, 0.14f, 0.95f);
            var le = btn.GetComponent<LayoutElement>();
            le.preferredWidth = 150f;
            le.preferredHeight = 40f;
            clearButton = btn.GetComponent<Button>();
            clearButton.onClick.AddListener(() => placer.ClearSelected());
            clearGroup = btn.GetComponent<CanvasGroup>();

            TMP_Text label = Label(btn.transform, "Label", "CLEAR + REFUND", 17, Color.white, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            SetAnchors(label, Vector2.zero, Vector2.one);
        }

        UnitCard BuildCard(RectTransform parent, WeaponData data)
        {
            var go = new GameObject(data.displayName + "Card", typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = cardColor; // raycast target: receives the drag events
            LayoutElement le = go.GetComponent<LayoutElement>();
            le.preferredWidth = cardSize.x;
            le.preferredHeight = cardSize.y;

            bool hasIcon = data.icon != null;
            if (hasIcon)
            {
                var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var irt = (RectTransform)icon.transform;
                irt.SetParent(go.transform, false);
                irt.anchorMin = new Vector2(0.15f, 0.45f);
                irt.anchorMax = new Vector2(0.85f, 0.95f);
                irt.offsetMin = irt.offsetMax = Vector2.zero;
                Image img = icon.GetComponent<Image>();
                img.sprite = data.icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }

            // Name: shrinks to fit and wraps to two lines at most, so it can never run into the next card.
            TMP_Text name = Label(go.transform, "Name", data.displayName, 20, Color.white, TextAlignmentOptions.Center);
            SetAnchors(name, new Vector2(0.04f, hasIcon ? 0.3f : 0.34f), new Vector2(0.96f, hasIcon ? 0.5f : 0.95f));
            name.fontStyle = FontStyles.Bold;
            name.enableAutoSizing = true;
            name.fontSizeMin = 11;
            name.fontSizeMax = 22;
            name.textWrappingMode = TextWrappingModes.Normal;
            name.overflowMode = TextOverflowModes.Ellipsis;

            TMP_Text cost = Label(go.transform, "Cost", "$" + data.cost, 24, costColor, TextAlignmentOptions.Center);
            SetAnchors(cost, new Vector2(0f, 0.02f), new Vector2(1f, 0.32f));
            cost.fontStyle = FontStyles.Bold;

            UnitCard card = go.AddComponent<UnitCard>();
            card.Init(data, placer);
            return card;
        }

        static void SetAnchors(TMP_Text t, Vector2 min, Vector2 max)
        {
            var rt = t.rectTransform;
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static TMP_Text Label(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.raycastTarget = false; // let the card underneath take the drag
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }
    }
}
