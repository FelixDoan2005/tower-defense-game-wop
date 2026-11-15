using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Maze;

namespace ZombieTD.Towers
{
    /// <summary>
    /// Drag-and-drop placement. A unit card in the tray calls BeginDrag/Drag/EndDrag. While dragging, the real unit
    /// follows the cursor and snaps to the pad under it; the pad lights up green (can place) or red (taken/too expensive).
    /// Dropping on a free pad spends the money and places the unit; dropping anywhere else cancels.
    /// </summary>
    public class UnitPlacer : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Camera cam;
        [SerializeField] LevelGenerator level;
        [SerializeField] WeaponData[] buildable;
        [SerializeField] Color validColor = new Color(0.25f, 1.6f, 0.5f);
        [SerializeField] Color invalidColor = new Color(1.7f, 0.25f, 0.2f);

        Tower carried;
        WeaponData carriedData;
        WallSlot hover;
        Renderer marker;
        MaterialPropertyBlock block;
        Color shownColor;

        public WeaponData[] Buildable => buildable;
        /// <summary>The tray's screen area; dropping a unit back on it cancels the placement.</summary>
        public RectTransform TrayRect { get; set; }
        public bool IsCarrying => carried != null;

        void Awake()
        {
            if (cam == null) cam = Camera.main;
            block = new MaterialPropertyBlock();
            CreateMarker();
        }

        void CreateMarker()
        {
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "PadHighlight";
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, false);
            q.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // lie flat, facing up
            marker = q.GetComponent<Renderer>();
            marker.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            marker.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            marker.receiveShadows = false;
            q.SetActive(false);
        }

        // ---------------------------------------------------------------- drag API

        public bool CanStartDrag(WeaponData data)
        {
            GameManager gm = GameManager.Instance;
            return gm != null && gm.State == GameState.Playing && data != null && data.unitPrefab != null && gm.CanAfford(data.cost);
        }

        public void BeginDrag(WeaponData data)
        {
            if (carried != null) Cancel();
            if (!CanStartDrag(data)) return;

            carriedData = data;
            carried = PoolManager.Instance.Spawn(data.unitPrefab, Vector3.zero, Quaternion.identity);
            carried.BeginCarry(data);
            hover = null;
        }

        public void Drag(Vector2 screenPos)
        {
            if (carried == null) return;

            Ray ray = cam.ScreenPointToRay(screenPos);
            level.TryGetSlotFromRay(ray, out hover);

            if (hover != null)
            {
                bool valid = IsValid(hover);
                // Shows where this soldier would stand in the squad.
                carried.transform.position = valid ? hover.FormationPoint(carriedData, hover.Count) : hover.MountPoint;
                ShowMarker(hover, valid ? validColor : invalidColor);
            }
            else
            {
                // Not over a pad: follow the cursor across the roof height, marker hidden.
                if (RayToRoofPlane(ray, out Vector3 p)) carried.transform.position = p;
                marker.gameObject.SetActive(false);
            }
        }

        /// <summary>Ends the drag. Returns true if the unit was placed.</summary>
        public bool EndDrag(Vector2 screenPos)
        {
            if (carried == null) return false;
            marker.gameObject.SetActive(false);

            bool overTray = TrayRect != null && RectTransformUtility.RectangleContainsScreenPoint(TrayRect, screenPos, null);
            if (!overTray && hover != null && IsValid(hover) && GameManager.Instance.TrySpend(carriedData.cost))
            {
                carried.Place(hover);
                carried = null;
                hover = null;
                return true;
            }

            Cancel();
            return false;
        }

        void Cancel()
        {
            if (carried != null) carried.Remove();
            carried = null;
            hover = null;
            if (marker != null) marker.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- helpers

        // Empty pad, or a squad of the same unit with room left; and enough money.
        bool IsValid(WallSlot slot) => slot.CanAdd(carriedData) && GameManager.Instance != null && GameManager.Instance.CanAfford(carriedData.cost);

        bool RayToRoofPlane(Ray ray, out Vector3 point)
        {
            point = default;
            if (ray.direction.y > -0.0001f) return false;
            float t = (level.transform.position.y + level.WallHeight - ray.origin.y) / ray.direction.y;
            if (t < 0f) return false;
            point = ray.origin + ray.direction * t;
            return true;
        }

        void ShowMarker(WallSlot slot, Color color)
        {
            Transform t = marker.transform;
            t.position = slot.MountPoint + Vector3.up * 0.05f;
            float s = level.Level.padSize + 0.5f;
            t.localScale = new Vector3(s, s, 1f);
            marker.gameObject.SetActive(true);

            if (color == shownColor) return;
            shownColor = color;
            block.SetColor(BaseColorId, color);
            marker.SetPropertyBlock(block);
        }
    }
}
