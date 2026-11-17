using UnityEngine;

namespace ZombieTD.Towers
{
    /// <summary>
    /// One unit in the Hierarchy (under Units > Soldiers or Units > Turrets). Select it to edit that unit's stats
    /// right in the Inspector: fire rate, damage, spread, cost, range and so on. The numbers are stored in the unit's
    /// WeaponData asset, so they are shared with everything that uses it.
    /// </summary>
    public class UnitEntry : MonoBehaviour
    {
        [SerializeField, Tooltip("The unit's stats asset (Assets/Settings/Units).")] WeaponData data;
        [SerializeField, Tooltip("Untick to take this unit off the build tray without deleting it.")] bool inTray = true;

        public WeaponData Data => data;
        public bool InTray => inTray;
    }
}
