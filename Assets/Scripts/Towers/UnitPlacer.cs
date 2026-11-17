using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using ZombieTD.Core;
using ZombieTD.Enemies;
using ZombieTD.Maze;

namespace ZombieTD.Towers
{
    /// <summary>
    /// Click-to-build placement. Click a pad to select it (it lights up), then click a unit card in the tray: each
    /// click adds one of that unit to the pad (click a cop twice for two cops). Clear empties the selected pad and
    /// refunds everything on it, before a wave starts. Click empty ground or right-click to deselect.
    /// </summary>
    public class UnitPlacer : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Camera cam;
        [SerializeField] LevelGenerator level;
        [SerializeField, Tooltip("The 'Units' folder in the Hierarchy. When set, the tray shows its enabled units in Hierarchy order.")]
        UnitRoster roster;
        [SerializeField, Tooltip("Used only when there is no Units folder.")] WeaponData[] buildable;
        [SerializeField, Tooltip("Colour of the selected pad's highlight.")] Color selectedColor = new Color(0.4f, 1.8f, 2.2f);

        WaveSpawner spawner;
        Renderer marker;
        MaterialPropertyBlock block;
        readonly List<Tower> scratch = new List<Tower>(32);

        public WeaponData[] Buildable => roster != null ? roster.Collect() : buildable;
        /// <summary>The tray's screen area (clicks over it never change the selection).</summary>
        public RectTransform TrayRect { get; set; }
        /// <summary>The pad clicked last (null when nothing is selected).</summary>
        public WallSlot Selected { get; private set; }

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            block = new MaterialPropertyBlock();
            CreateMarker();
        }

        void Start()
        {
            spawner = FindAnyObjectByType<WaveSpawner>(); // one-time lookup
        }

        void CreateMarker()
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "PadHighlight";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            marker = q.GetComponent<Renderer>();
            marker.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.receiveShadows = false;
            block.SetColor(BaseColorId, selectedColor);
            marker.SetPropertyBlock(block);
            q.SetActive(false);
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing) { Select(null); return; }

            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            if (mouse.rightButton.wasPressedThisFrame) Select(null);
            if (mouse.leftButton.wasPressedThisFrame)
            {
                // Clicks on the tray / buttons belong to the UI, not to the map.
                bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                if (!overUi)
                {
                    Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
                    level.TryGetSlotFromRay(ray, out WallSlot slot);
                    Select(slot);
                }
            }

            if (Selected != null) ShowMarker(Selected);
        }

        void Select(WallSlot slot)
        {
            Selected = slot;
            if (slot == null && marker != null) marker.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- build / clear

        /// <summary>True if one more of this unit fits on the selected pad and can be paid for.</summary>
        public bool CanAddToSelected(WeaponData data)
        {
            GameManager gm = GameManager.Instance;
            return Selected != null && gm != null && gm.State == GameState.Playing && data != null && data.unitPrefab != null
                   && Selected.CanAdd(data) && gm.CanAfford(data.cost);
        }

        /// <summary>Adds one unit to the selected pad (spends its cost). Returns true if it was added.</summary>
        public bool TryAddToSelected(WeaponData data)
        {
            if (!CanAddToSelected(data) || !GameManager.Instance.TrySpend(data.cost)) return false;
            Tower unit = PoolManager.Instance.Spawn(data.unitPrefab, Selected.MountPoint, Quaternion.identity);
            unit.BeginCarry(data);
            unit.Place(Selected);
            return true;
        }

        /// <summary>Can the selected pad be emptied right now? Only before a wave starts, and only if something is on it.</summary>
        public bool CanClearSelected => Selected != null && Selected.Count > 0 && (spawner == null || !spawner.IsWaveActive);

        /// <summary>Removes everything on the selected pad and gives the money back.</summary>
        public void ClearSelected()
        {
            if (!CanClearSelected) return;
            scratch.Clear();
            scratch.AddRange(Selected.Units);
            foreach (Tower t in scratch)
            {
                if (t == null) continue;
                if (t.Weapon != null) GameManager.Instance.Refund(t.Weapon.cost);
                t.Remove();
            }
            scratch.Clear();
        }

        // ---------------------------------------------------------------- highlight

        void ShowMarker(WallSlot slot)
        {
            Transform t = marker.transform;
            Vector3 f = slot.Forward;
            t.position = slot.MountPoint + Vector3.up * 0.06f;
            t.rotation = Quaternion.Euler(0f, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, 0f) * Quaternion.Euler(90f, 0f, 0f); // flat, facing up, aligned to the pad
            t.localScale = new Vector3(slot.PlateSize + 0.5f, slot.PlateDepth + 0.5f, 1f);
            marker.gameObject.SetActive(true);
        }
    }
}
