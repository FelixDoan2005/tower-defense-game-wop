using UnityEngine;
using UnityEngine.EventSystems;
using ZombieTD.Towers;

namespace ZombieTD.UI
{
    /// <summary>One card in the unit tray. Each click adds one of this unit to the selected pad.</summary>
    public class UnitCard : MonoBehaviour, IPointerClickHandler
    {
        WeaponData data;
        UnitPlacer placer;
        CanvasGroup group;

        public WeaponData Data => data;

        public void Init(WeaponData weapon, UnitPlacer unitPlacer)
        {
            data = weapon;
            placer = unitPlacer;
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        /// <summary>Dims the card when it can't be added right now (no pad selected, pad full or wrong kind, can't afford).</summary>
        public void Refresh(bool usable) => group.alpha = usable ? 1f : 0.4f;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) placer.TryAddToSelected(data);
        }
    }
}
