using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ZombieTD.Towers;

namespace ZombieTD.Maze
{
    /// <summary>What a placement pad accepts.</summary>
    public enum PadKind { Soldier, Wall }

    /// <summary>
    /// A placement pad. Holds a squad of soldiers (any mix of soldier types, up to the pad's capacity) in a small
    /// formation. A unit whose Slot Capacity is 1 (a big turret) needs the whole pad to itself and never shares.
    /// Move, rotate or duplicate the pad object freely: its position is where the squad stands, and its forward
    /// direction (the white arrow in the Scene view) is the way the front row faces.
    /// </summary>
    public class WallSlot : MonoBehaviour
    {
        /// <summary>Soldiers per pad unless a pad says otherwise.</summary>
        public const int DefaultCapacity = 20;

        [Header("What fits")]
        [SerializeField, Tooltip("Soldier = roof pads and pads behind cars (squads of soldiers). Wall = the big pads in front of the cars, for a Shotgun Wall.")]
        PadKind kind = PadKind.Soldier;

        [Header("Squad")]
        [SerializeField, Min(1), Tooltip("Most soldiers this pad holds (any mix of soldier types).")]
        int capacity = DefaultCapacity;
        [SerializeField, Range(1, 10), Tooltip("Soldiers per row. Rows fill in behind the front row.")]
        int columns = 5;
        [SerializeField, Min(0.1f), Tooltip("Gap between soldiers standing together.")]
        float spacing = 0.5f;
        [SerializeField, Tooltip("How far in front of the pad centre the front row stands (along the pad's forward direction).")]
        float frontOffset = 1f;

        [Header("Placement")]
        [SerializeField, Tooltip("Half-size of the square area that counts as 'over this pad' while dragging a unit.")]
        float hitHalf = 2f;
        [SerializeField, Tooltip("Pad on the road (not on a roof): no roof-edge or far-side logic for its soldiers.")]
        bool ground;

        [Header("Look")]
        [SerializeField, Tooltip("Width of the glowing plate. Scales the 'Plate' child; the drop area is set separately.")]
        float plateSize = 2.6f;
        [SerializeField, Min(0f), Tooltip("Front-to-back size of the plate. 0 = square (same as Plate Size). The Shotgun Wall pads are wide and shallow.")]
        float plateDepth;
        [SerializeField] Transform plate;

        readonly List<Tower> units = new List<Tower>(10);
        TextMeshPro label;

        /// <summary>Centre of the pad.</summary>
        public Vector3 MountPoint => transform.position;
        /// <summary>The way the front row faces (the pad's forward, flattened).</summary>
        public Vector3 Forward
        {
            get
            {
                Vector3 f = transform.forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }
        /// <summary>The first unit type placed here (null when empty). Soldiers can be mixed after it.</summary>
        public WeaponData UnitType { get; private set; }
        public int Capacity => capacity;
        public PadKind Kind => kind;
        public int Count => units.Count;
        /// <summary>The units standing on this pad (front first).</summary>
        public IReadOnlyList<Tower> Units => units;
        public bool IsOccupied => units.Count > 0;
        public bool IsGround => ground;
        public float HitHalf => hitHalf;
        public float PlateSize => plateSize;
        public float PlateDepth => plateDepth > 0f ? plateDepth : plateSize;
        /// <summary>How close to the pad centre zombies come before they stop and attack (covers the whole depth of a wall's layers).</summary>
        public float ContactRadius => Mathf.Max(hitHalf * 0.6f, PlateDepth * 0.5f);

        /// <summary>Sets everything for a pad created by code or by the pad tool.</summary>
        public void Configure(float frontRowOffset, bool onGround, float dropHalfSize, float plateWidth, Transform plateChild,
            PadKind padKind = PadKind.Soldier)
        {
            kind = padKind;
            frontOffset = frontRowOffset;
            ground = onGround;
            hitHalf = dropHalfSize;
            plateSize = plateWidth;
            plate = plateChild;
            ApplyPlateSize();
        }

        void OnValidate() => ApplyPlateSize();

        void ApplyPlateSize()
        {
            if (plate != null) plate.localScale = new Vector3(plateSize, 1f, plateDepth > 0f ? plateDepth : plateSize);
        }

        /// <summary>
        /// True if one more of this unit fits: an empty pad, or a pad of soldiers with room left (soldier types mix freely).
        /// A one-per-pad unit (Slot Capacity 1, like a turret) only goes on an empty pad and blocks anything else after it.
        /// </summary>
        public bool CanAdd(WeaponData unit)
        {
            if (unit == null || unit.padKind != kind) return false; // soldiers go on soldier pads, a Shotgun Wall on wall pads
            if (units.Count == 0) return true;
            if (IsExclusive(unit) || IsExclusive(UnitType)) return false;
            return units.Count < capacity;
        }

        static bool IsExclusive(WeaponData unit) => unit != null && unit.slotCapacity <= 1;

        /// <summary>Pads on the road that currently hold units: the ones zombies attack.</summary>
        public static readonly List<WallSlot> UnderAttack = new List<WallSlot>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => UnderAttack.Clear();

        void OnDisable() => UnderAttack.Remove(this);

        /// <summary>Adds a unit and returns its position index in the formation.</summary>
        public int Add(Tower unit, WeaponData type)
        {
            UnitType = type;
            units.Add(unit);
            if (ground && !UnderAttack.Contains(this)) UnderAttack.Add(this);
            UpdateLabel();
            return units.Count - 1;
        }

        public void Remove(Tower unit)
        {
            if (!units.Remove(unit)) return;
            if (units.Count == 0) { UnitType = null; UnderAttack.Remove(this); }
            UpdateLabel();
        }

        /// <summary>Zombie damage to whoever stands here: the front row goes first, and what a broken unit can't absorb carries on to the next.</summary>
        public void Damage(float amount)
        {
            for (int i = 0; i < units.Count && amount > 0f;)
            {
                Tower t = units[i];
                if (t == null) { units.RemoveAt(i); continue; }
                int before = units.Count;
                amount = t.Hurt(amount);
                if (units.Count == before) i++; // it survived; a broken unit removed itself, so the next one is now at i
            }
        }

        /// <summary>
        /// Where the unit with this formation index stands. Rows run across the pad; the first row is at the front
        /// (pressed up against the parapet or the car) and later rows fill in behind it.
        /// </summary>
        public Vector3 FormationPoint(WeaponData unit, int index)
        {
            if (unit == null || IsExclusive(unit)) return MountPoint; // a turret sits in the middle
            return FormationPoint(index);
        }

        /// <summary>Position of the nth soldier: rows of "columns", the first row at the front.</summary>
        public Vector3 FormationPoint(int index)
        {
            int cols = Mathf.Max(1, Mathf.Min(columns, capacity));
            int col = index % cols, row = index / cols;
            Vector3 fwd = Forward, side = new Vector3(fwd.z, 0f, -fwd.x);
            // Tiny deterministic wobble so the squad doesn't look like a parade grid.
            float jx = (Hash(index, 1) - 0.5f) * spacing * 0.3f, jz = (Hash(index, 2) - 0.5f) * spacing * 0.2f;
            if (kind == PadKind.Wall) jx = jz = 0f; // walls stand in neat layers
            float across = (col - (cols - 1) * 0.5f) * spacing + jx;
            float depth = frontOffset - row * spacing + jz;
            return MountPoint + side * across + fwd * depth;
        }

        /// <summary>Redraws the squad counter (called when a soldier starts or finishes reloading).</summary>
        public void RefreshLabel() => UpdateLabel();

        // "3/10" lying on the roof just in front of the pad (with "R2" while two of them are reloading);
        // shown only while units are standing here.
        void UpdateLabel()
        {
            if (units.Count == 0 || UnitType == null || IsExclusive(UnitType) || kind == PadKind.Wall)
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

            int reloading = 0;
            foreach (Tower u in units) if (u != null && u.IsReloading) reloading++;

            label.gameObject.SetActive(true);
            label.text = reloading > 0 ? $"{units.Count}/{capacity}  R{reloading}" : $"{units.Count}/{capacity}";
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

        // Scene view helpers: the drop area, the facing arrow, and where 10 soldiers would stand.
        void OnDrawGizmos()
        {
            Vector3 p = transform.position;
            Gizmos.color = ground ? new Color(1f, 0.8f, 0.2f, 0.9f) : new Color(0.3f, 1f, 1f, 0.9f);
            Gizmos.DrawWireCube(p + Vector3.up * 0.05f, new Vector3(hitHalf * 2f, 0.02f, hitHalf * 2f));

            Gizmos.color = Color.white;
            Gizmos.DrawRay(p, Forward * (frontOffset + 0.8f));

            Gizmos.color = new Color(1f, 1f, 1f, 0.7f);
            for (int i = 0; i < capacity; i++) Gizmos.DrawWireSphere(FormationPoint(i) + Vector3.up * 0.3f, 0.15f);
        }
    }
}
