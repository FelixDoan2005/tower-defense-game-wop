using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ZombieTD.Towers;

namespace ZombieTD.Maze
{
    /// <summary>
    /// A placement pad on a roof. Holds a squad of the same unit type (up to the unit's slot capacity), arranged
    /// in a small formation; a big turret has capacity 1 and takes the whole pad.
    /// </summary>
    public class WallSlot : MonoBehaviour
    {
        const int Columns = 4;

        [SerializeField] Vector2Int cell;

        Vector3 forward = Vector3.forward;   // from the pad toward the road
        float frontOffset;                   // how far in front of the pad centre the front row stands
        readonly List<Tower> units = new List<Tower>(10);
        TextMeshPro label;

        public Vector2Int Cell => cell;
        public int CellIndex { get; private set; }
        /// <summary>Centre of the pad (roof level).</summary>
        public Vector3 MountPoint => transform.position;
        /// <summary>The unit type standing here (null when empty).</summary>
        public WeaponData UnitType { get; private set; }
        public int Count => units.Count;
        public bool IsOccupied => units.Count > 0;

        public void Init(int x, int z, int index, Vector2Int towardRoad, float frontRowOffset)
        {
            cell = new Vector2Int(x, z);
            CellIndex = index;
            forward = new Vector3(towardRoad.x, 0f, towardRoad.y);
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            frontOffset = frontRowOffset;
        }

        /// <summary>Unit vector from the pad toward the road.</summary>
        public Vector3 TowardRoad => forward;

        /// <summary>True if one more of this unit fits here (empty pad, or same type with room left).</summary>
        public bool CanAdd(WeaponData unit)
        {
            if (unit == null) return false;
            return units.Count == 0 || (UnitType == unit && units.Count < Mathf.Max(1, unit.slotCapacity));
        }

        /// <summary>Adds a unit and returns its position index in the formation.</summary>
        public int Add(Tower unit, WeaponData type)
        {
            UnitType = type;
            units.Add(unit);
            UpdateLabel();
            return units.Count - 1;
        }

        public void Remove(Tower unit)
        {
            if (!units.Remove(unit)) return;
            if (units.Count == 0) UnitType = null;
            UpdateLabel();
        }

        /// <summary>
        /// Where the unit with this formation index stands. Rows run along the wall; the first row is pressed up
        /// against the parapet and later rows fill in behind it.
        /// </summary>
        public Vector3 FormationPoint(WeaponData unit, int index)
        {
            if (unit == null || unit.slotCapacity <= 1) return MountPoint;

            int cap = unit.slotCapacity, cols = Mathf.Min(Columns, cap);
            int col = index % cols, row = index / cols;
            float s = unit.formationSpacing;
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            // Tiny deterministic wobble so the squad doesn't look like a parade grid.
            float jx = (Hash(index, 1) - 0.5f) * s * 0.3f, jz = (Hash(index, 2) - 0.5f) * s * 0.2f;
            float across = (col - (cols - 1) * 0.5f) * s + jx;
            float depth = frontOffset - row * s + jz;
            return MountPoint + side * across + forward * depth;
        }

        // "3/10" lying on the roof just in front of the pad; shown only while units are standing here.
        void UpdateLabel()
        {
            if (units.Count == 0 || UnitType == null || UnitType.slotCapacity <= 1)
            {
                if (label != null) label.gameObject.SetActive(false);
                return;
            }

            if (label == null)
            {
                var go = new GameObject("Count");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.15f, -1.75f);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                label = go.AddComponent<TextMeshPro>();
                label.rectTransform.sizeDelta = new Vector2(3f, 1.1f);
                label.alignment = TextAlignmentOptions.Center;
                label.enableAutoSizing = true;
                label.fontSizeMin = 0.5f;
                label.fontSizeMax = 6f;
                label.fontStyle = FontStyles.Bold;
                label.color = Color.white;
            }

            label.gameObject.SetActive(true);
            label.text = $"{units.Count}/{UnitType.slotCapacity}";
        }

        static float Hash(int a, int s)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(s * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xffff) / 65535f;
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.red : Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(1.5f, 0.1f, 1.5f));
        }
    }
}
