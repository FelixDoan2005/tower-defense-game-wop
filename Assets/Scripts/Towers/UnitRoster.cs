using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Towers
{
    /// <summary>
    /// The "Units" folder in the Hierarchy. The build tray shows every enabled UnitEntry under this object, in
    /// Hierarchy order, so dragging units up or down here reorders the tray cards.
    /// </summary>
    public class UnitRoster : MonoBehaviour
    {
        /// <summary>Units for the tray: enabled entries with a stats asset and a unit prefab, in Hierarchy order.</summary>
        public WeaponData[] Collect()
        {
            UnitEntry[] entries = GetComponentsInChildren<UnitEntry>(false);
            var list = new List<WeaponData>(entries.Length);
            foreach (UnitEntry e in entries)
                if (e.InTray && e.Data != null && e.Data.unitPrefab != null) list.Add(e.Data);
            return list.ToArray();
        }
    }
}
