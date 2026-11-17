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
        /// <summary>A random zombie in range (used by roof soldiers, so fire is spread everywhere).</summary>
        Random,
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
        [Tooltip("Which pads take this unit: Soldier = roof pads and pads behind cars; Wall = the big pads in front of the cars.")]
        public ZombieTD.Maze.PadKind padKind = ZombieTD.Maze.PadKind.Soldier;
        [Min(1), Tooltip("Soldiers: leave above 1. They share a pad with any other soldiers (the pad sets how many, 20 by default). " +
                         "Set to 1 for a big turret that needs a whole pad to itself.")]
        public int slotCapacity = 20;
        [Min(0.1f), Tooltip("Gap between units standing together on a pad, in world units.")] public float formationSpacing = 0.6f;
        [HideInInspector, Tooltip("Used by the setup tool to update older assets once.")] public int setupVersion;

        [Header("Durability")]
        [Min(1f), Tooltip("Hit points. Only units on the road (pads by the police cars) get attacked: zombies chew on them when they reach them.")]
        public float health = 40f;

        [Header("Targeting")]
        [Tooltip("Horizontal (XZ) range in world units.")] public float range = 24f;
        [Tooltip("Shots per second.")] public float fireRate = 3.5f;
        public TargetMode defaultTargetMode = TargetMode.First;
        [Tooltip("Turn speed in degrees per second.")] public float turnSpeed = 540f;
        [Tooltip("Fires only when within this many degrees of the target.")] public float aimTolerance = 8f;

        [Tooltip("For long-range units on a roof (snipers): shots arc over the rooftops, so buildings never block them (zombies are always on the road). " +
                 "Without it, a shallow shot along the street clips the buildings beside it and is refused.")]
        public bool shootOverOwnBuilding;

        [Header("Damage")]
        public float damage = 9f;
        public float knockback;

        [Header("Splash (area damage)")]
        [Min(0f), Tooltip("0 = none. Otherwise everything within this radius of where the shot lands is hurt too.")] public float splashRadius;
        [Range(0f, 1f), Tooltip("Splash damage as a fraction of the shot's damage (it also fades toward the edge).")] public float splashDamageFraction = 0.5f;

        [Header("Where it stands (multipliers)")]
        [Min(0f), Tooltip("Damage multiplier when the unit stands on a roof pad.")] public float damageOnRoof = 1f;
        [Min(0f), Tooltip("Damage multiplier when the unit stands on the road (the pads behind the police cars).")] public float damageOnGround = 1f;
        [Min(0f), Tooltip("Splash radius multiplier on a roof. 0 turns splash off up there.")] public float splashOnRoof = 1f;
        [Min(0f), Tooltip("Splash radius multiplier on the road.")] public float splashOnGround = 1f;

        [Header("Ammo")]
        [Min(0), Tooltip("Shots per magazine. Each soldier has his own. 0 = never reloads.")] public int magazineSize = 30;
        [Min(0.1f), Tooltip("Seconds to reload. Each soldier's reload varies a little so a squad doesn't all reload at once.")] public float reloadTime = 2.5f;
        [HideInInspector] public int reloadVersion; // used by the setup tool to apply the starting ammo values once

        [Header("Accuracy")]
        [Min(0f), Tooltip("0 = every shot hits its target. Above 0, shots scatter inside a cone this wide (degrees); a miss can hit a zombie standing near the target, or nothing.")]
        public float spreadDegrees;
        [Range(1, 16), Tooltip("Bullets per shot. A shotgun fires several, each scattering on its own inside the cone. Damage is per bullet.")] public int pellets = 1;
        [Min(0.05f), Tooltip("How close a stray shot must land to a zombie to hit it.")] public float hitRadius = 0.45f;
        [Min(1), Tooltip("Show the muzzle flash, tracer and hit effect on every Nth shot (keeps very fast guns cheap).")] public int effectEvery = 1;

        [Header("Effects")]
        public GameObject muzzleVfx;
        public GameObject hitVfx;
        public Tracer tracerPrefab;
        [Min(0f), Tooltip("How long the tracer stays visible, in seconds. 0 = the unit's default (a quick flash). Longer + thicker = a bullet you can follow.")]
        public float tracerLife;
        public float shakeOnFire;
        [Tooltip("Camera shake where the shot lands (fades with distance from the view).")] public float shakeOnImpact;
        public AudioClip fireSound;
        [Range(0f, 1f)] public float fireVolume = 0.6f;
    }
}
