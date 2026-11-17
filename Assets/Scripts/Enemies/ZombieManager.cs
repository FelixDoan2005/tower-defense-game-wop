using System;
using UnityEngine;
using UnityEngine.Serialization;
using ZombieTD.Core;
using ZombieTD.Maze;

namespace ZombieTD.Enemies
{
    /// <summary>
    /// Owns the active zombie list, the flow field and the spatial grid.
    /// Ticks every zombie in one tight loop (no per-zombie Update), then rebuckets them for targeting.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ZombieManager : MonoBehaviour
    {
        public static ZombieManager Instance { get; private set; }

        [SerializeField, FormerlySerializedAs("maze")] LevelGenerator level;
        [SerializeField, Tooltip("Bucket size for targeting queries (world units).")] float spatialCellSize = 4f;
        [SerializeField] int initialCapacity = 2048;

        [Header("Attacks on units")]
        [SerializeField, Tooltip("Damage per second each zombie touching a pad on the road deals to the units on it.")]
        float damagePerZombie = 3f;

        [SerializeField, Tooltip("Zombies this close to a unit standing on the road leave the path and go for it.")]
        float unitAggroRadius = 14f;

        static readonly Zombie[] contactBuffer = new Zombie[256];
        Zombie[] active;
        int count;
        float gateDps; // summed by attacking zombies during the tick loop

        public MazeGrid Grid { get; private set; }
        public FlowField Flow { get; private set; }
        public SpatialGrid Spatial { get; private set; }
        public int ActiveCount => count;
        /// <summary>Zombies attacking the gate this frame.</summary>
        public int GateAttackers { get; private set; }
        /// <summary>How far in front of the map edge zombies stop (gate door thickness). Set by BaseGate.</summary>
        public float GateStandoff { get; set; }

        void Awake()
        {
            Instance = this;
            if (level == null) level = FindAnyObjectByType<LevelGenerator>(); // one-time fallback
            Grid = level.Grid;
            Flow = new FlowField(Grid);
            Flow.Recompute();
            Spatial = new SpatialGrid(Grid.Origin, Grid.Width * Grid.CellSize, Grid.Height * Grid.CellSize, spatialCellSize);
            active = new Zombie[initialCapacity];
        }

        void OnDestroy()
        {
            Flow?.Dispose();
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            float t = Time.time;

            Flow.EnsureUpToDate();

            gateDps = 0f;
            GateAttackers = 0;

            // Backwards so a zombie removing itself (swap-remove) never skips anyone.
            for (int i = count - 1; i >= 0; i--)
                active[i].Tick(dt, t);

            // One gate hit per frame for the whole crowd.
            if (gateDps > 0f && GameManager.Instance != null) GameManager.Instance.DamageBase(gateDps * dt);

            Spatial.Rebuild(active, count);
            ChewOnRoadUnits(dt);
        }

        /// <summary>The nearest occupied road pad within the aggro radius, for a zombie at p. Few pads, so a plain loop is cheap.</summary>
        internal bool FindBait(Vector3 p, out Vector3 position, out float contactDistance)
        {
            var pads = WallSlot.UnderAttack;
            float best = unitAggroRadius * unitAggroRadius;
            position = default;
            contactDistance = 0f;
            bool found = false;
            for (int i = 0; i < pads.Count; i++)
            {
                WallSlot pad = pads[i];
                if (pad == null) continue;
                Vector3 m = pad.MountPoint;
                float dx = m.x - p.x, dz = m.z - p.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= best) continue;
                best = d2;
                position = m;
                contactDistance = pad.ContactRadius;
                found = true;
            }
            return found;
        }

        // Zombies that reach a pad on the road attack whoever stands there. One spatial query per occupied pad.
        void ChewOnRoadUnits(float dt)
        {
            var pads = WallSlot.UnderAttack;
            for (int i = pads.Count - 1; i >= 0; i--)
            {
                WallSlot pad = pads[i];
                if (pad == null) { pads.RemoveAt(i); continue; }
                int n = Spatial.Query(pad.MountPoint, pad.ContactRadius + 1.2f, contactBuffer);
                for (int k = 0; k < n; k++) contactBuffer[k] = null;
                if (n > 0) pad.Damage(n * damagePerZombie * dt);
            }
        }

        internal void AddGateDamage(float dps)
        {
            gateDps += dps;
            GateAttackers++;
        }

        internal void Register(Zombie z)
        {
            if (count == active.Length) Array.Resize(ref active, count * 2);
            z.ManagerIndex = count;
            active[count++] = z;
        }

        internal void Unregister(Zombie z)
        {
            int i = z.ManagerIndex;
            if (i < 0) return;
            int last = --count;
            Zombie moved = active[last];
            active[i] = moved;
            moved.ManagerIndex = i;
            active[last] = null;
            z.ManagerIndex = -1;
        }
    }
}
