using UnityEngine;

namespace ZombieTD.Towers
{
    public enum TargetMode
    {
        /// <summary>Nearest to the unit.</summary>
        Closest,
        /// <summary>Furthest along the road (closest to the gate).</summary>
        First,
        /// <summary>Highest current health.</summary>
        Strongest,
    }

    /// <summary>
    /// Everything about one placeable unit: what it costs, how it shoots and which effects it uses.
    /// To add a unit: Create > ZombieTD > Weapon Data, point it at a unit prefab, add it to the UnitPlacer list.
    /// Fields are public because this is a plain data container.
    /// </summary>
    [CreateAssetMenu(menuName = "ZombieTD/Weapon Data", fileName = "NewUnit")]
    public class WeaponData : ScriptableObject
    {
        [Header("Shop")]
        public string displayName = "Unit";
        public int cost = 100;
        [Tooltip("The unit that gets placed (a prefab with a Tower component).")] public Tower unitPrefab;
        [Tooltip("Optional card picture.")] public Sprite icon;
        [Min(1), Tooltip("How many of this unit fit on one pad. Soldiers stack up to 10; a big turret takes the whole pad (1).")]
        public int slotCapacity = 10;
        [Min(0.1f), Tooltip("Gap between units standing together on a pad, in world units.")] public float formationSpacing = 0.6f;
        [HideInInspector, Tooltip("Used by the setup tool to update older assets once.")] public int setupVersion;

        [Header("Targeting")]
        [Tooltip("Horizontal (XZ) range in world units.")] public float range = 24f;
        [Tooltip("Shots per second.")] public float fireRate = 3.5f;
        public TargetMode defaultTargetMode = TargetMode.First;
        [Tooltip("Turn speed in degrees per second.")] public float turnSpeed = 540f;
        [Tooltip("Fires only when within this many degrees of the target.")] public float aimTolerance = 8f;

        [Header("Damage")]
        public float damage = 9f;
        public float knockback;

        [Header("Effects")]
        public GameObject muzzleVfx;
        public GameObject hitVfx;
        public Tracer tracerPrefab;
        public float shakeOnFire;
        public AudioClip fireSound;
        [Range(0f, 1f)] public float fireVolume = 0.6f;
    }
}
