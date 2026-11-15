using UnityEngine;
using UnityEngine.EventSystems;
using ZombieTD.Towers;

namespace ZombieTD.UI
{
    /// <summary>One card in the unit tray. Dragging it starts a placement through the UnitPlacer.</summary>
    public class UnitCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        WeaponData data;
        UnitPlacer placer;
        CanvasGroup group;
        bool dragging;

        public WeaponData Data => data;

        public void Init(WeaponData weapon, UnitPlacer unitPlacer)
        {
            data = weapon;
            placer = unitPlacer;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>Dims the card when the player can't afford it.</summary>
        public void Refresh(bool affordable) => group.alpha = affordable ? 1f : 0.45f;

        public void OnBeginDrag(PointerEventData e)
        {
            dragging = placer.CanStartDrag(data);
            if (dragging) placer.BeginDrag(data);
        }

        public void OnDrag(PointerEventData e)
        {
            if (dragging) placer.Drag(e.position);
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (!dragging) return;
            dragging = false;
            placer.EndDrag(e.position);
        }
    }
}
