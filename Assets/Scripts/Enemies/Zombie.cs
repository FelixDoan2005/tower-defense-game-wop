using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Maze;

namespace ZombieTD.Enemies
{
    /// <summary>
    /// One horde member. Has no Update of its own: ZombieManager ticks every zombie in one loop.
    /// Steers toward the next flow-field cell, offset into its own "lane", with wobble and light separation.
    /// </summary>
    public class Zombie : MonoBehaviour
    {
        [Header("Stats (scaled per wave by WaveSpawner)")]
        [SerializeField] float baseHealth = 30f;
        [SerializeField] float baseSpeed = 2.4f;
        [SerializeField, Range(0f, 0.5f)] float speedVariance = 0.2f;
        [SerializeField, Tooltip("Damage per second dealt to the gate while pressed against it.")] float attackDps = 1f;
        [SerializeField] int baseReward = 2;

        [Header("Movement")]
        [SerializeField, Range(0f, 0.45f), Tooltip("Max sideways offset from the corridor center, as a fraction of a cell.")]
        float laneSpread = 0.3f;
        [SerializeField] float bodyRadius = 0.2f;
        [SerializeField] float wobbleAmount = 0.3f;
        [SerializeField] float wobbleFrequency = 1.3f;
        [SerializeField] float separationRadius = 0.45f;
        [SerializeField] float separationStrength = 1.2f;
        [SerializeField] int maxNeighbourChecks = 6;
        [SerializeField] float turnSharpness = 10f;
        [SerializeField] float knockbackDamping = 7f;

        [Header("Visual")]
        [SerializeField, Tooltip("Height of the pivot above the ground (capsule half height).")] float groundOffset = 0.45f;
        [SerializeField] float bobAmount = 0.04f;
        [SerializeField] float bobFrequency = 10f;
        [SerializeField] GameObject deathVfx;

        public bool IsAlive { get; private set; }
        public float Health { get; private set; }
        public float MaxHealth { get; private set; }
        /// <summary>Ground-level position used by all gameplay logic.</summary>
        public Vector3 Position { get; private set; }
        public Vector3 Velocity { get; private set; }
        /// <summary>Cells left to the goal (lower = further along the path).</summary>
        public int PathDistance { get; private set; }
        /// <summary>True while in a goal cell, hammering on the gate.</summary>
        public bool IsAttackingGate { get; private set; }
        /// <summary>Body center, where weapons should aim.</summary>
        public Vector3 AimPoint => new Vector3(Position.x, Position.y + groundOffset, Position.z);

        internal int ManagerIndex = -1;
        internal int SpatialCell = -1;

        Transform tr;
        PooledObject pooled;
        ZombieManager manager;
        MazeGrid grid;
        FlowField flow;
        float speed, phase, lane;
        int reward;
        Vector3 knockback;
        Quaternion facing = Quaternion.identity;

        void Awake()
        {
            tr = transform;
            pooled = GetComponent<PooledObject>();
        }

        /// <summary>Called by WaveSpawner right after taking the zombie from the pool.</summary>
        public void Spawn(ZombieManager mgr, Vector3 groundPos, float healthMul, float speedMul, float rewardMul)
        {
            manager = mgr;
            grid = mgr.Grid;
            flow = mgr.Flow;

            MaxHealth = baseHealth * healthMul;
            Health = MaxHealth;
            speed = baseSpeed * speedMul * (1f + Random.Range(-speedVariance, speedVariance));
            IsAttackingGate = false;
            reward =Mathf.Max(1, Mathf.RoundToInt(baseReward * rewardMul));
            phase = Random.value * 100f;
            lane = Random.Range(-laneSpread, laneSpread) * grid.CellSize;
            knockback = Vector3.zero;
            Velocity = Vector3.zero;
            Position = groundPos;
            SpatialCell = -1;
            IsAlive = true;

            int cell = grid.WorldToIndex(groundPos);
            PathDistance = cell >= 0 ? flow.DistanceAt(cell) : FlowField.Unreachable;

            Vector3 dir = cell >= 0 ? flow.DirectionAt(cell) : Vector3.forward;
            facing = dir.sqrMagnitude > 0f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            tr.SetPositionAndRotation(AimPoint, facing);

            mgr.Register(this);
        }

        /// <summary>Per-frame movement, driven by ZombieManager.</summary>
        public void Tick(float dt, float time)
        {
            int cell = grid.WorldToIndex(Position);
            Vector3 desired = Vector3.zero;
            int nextCell = cell >= 0 ? flow.NextAt(cell) : -1;
            IsAttackingGate = flow.IsGoal(cell);

            if (IsAttackingGate)
            {
                // At the goal: shove toward the gate until touching it, and damage it.
                manager.AddGateDamage(attackDps);
                Vector3 push = flow.GoalPushAt(cell);
                if (push != Vector3.zero)
                {
                    Vector3 edge = grid.CellToWorld(cell) + push * (grid.CellSize * 0.5f);
                    float gap = Vector3.Dot(edge - Position, push) - manager.GateStandoff - bodyRadius;
                    if (gap > 0.05f) desired = push;
                    facing = Quaternion.Slerp(facing, Quaternion.LookRotation(push), 1f - Mathf.Exp(-turnSharpness * dt));
                }
            }
            else if (nextCell >= 0)
            {
                PathDistance = flow.DistanceAt(cell);
                Vector3 to = grid.CellToWorld(nextCell);
                Vector3 step = to - grid.CellToWorld(cell);
                step.Normalize();
                var side = new Vector3(step.z, 0f, -step.x);

                // Aim at our lane inside the next cell; the path stays inside the open cells.
                Vector3 toTarget = to + side * lane - Position;
                toTarget.y = 0f;
                float mag = toTarget.magnitude;
                desired = mag > 0.001f ? toTarget / mag : step;
                desired += side * (Mathf.Sin(time * wobbleFrequency + phase) * wobbleAmount);
            }

            // Anything standing on the road gets attacked: zombies nearby break off and go for it, then stand and chew.
            if (!IsAttackingGate && manager.FindBait(Position, out Vector3 bait, out float contact))
            {
                Vector3 toBait = bait - Position;
                toBait.y = 0f;
                float d = toBait.magnitude;
                desired = d > contact ? toBait / d : Vector3.zero;
            }

            desired += Separation() * separationStrength;
            float len = desired.magnitude;
            if (len > 1f) desired /= len;

            Vector3 move = desired * speed + knockback;
            knockback *= Mathf.Exp(-knockbackDamping * dt);

            Vector3 newPos = ResolveWalls(Position, Position + move * dt);
            Velocity = (newPos - Position) / dt;
            Position = newPos;

            Vector3 flat = Velocity;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.04f)
                facing = Quaternion.Slerp(facing, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-turnSharpness * dt));

            float bobRate = IsAttackingGate ? bobFrequency * 1.6f : bobFrequency; // frantic pounding at the gate
            float bob = Mathf.Abs(Mathf.Sin(time * bobRate + phase)) * bobAmount;
            tr.SetPositionAndRotation(new Vector3(Position.x, Position.y + groundOffset + bob, Position.z), facing);
        }

        public void TakeDamage(float amount)
        {
            if (!IsAlive) return;
            Health -= amount;
            if (Health <= 0f) Die(true);
        }

        /// <summary>Adds a horizontal shove (decays over time; walls still block it).</summary>
        public void Knockback(Vector3 impulse)
        {
            if (!IsAlive) return;
            impulse.y = 0f;
            knockback += impulse;
        }

        // Axis-separated move so zombies slide along walls instead of sticking.
        Vector3 ResolveWalls(Vector3 from, Vector3 to)
        {
            Vector3 p = from;
            if (to.x != from.x)
            {
                float probe = to.x + (to.x > from.x ? bodyRadius : -bodyRadius);
                if (grid.IsWalkableWorld(new Vector3(probe, from.y, from.z))) p.x = to.x;
            }
            if (to.z != from.z)
            {
                float probe = to.z + (to.z > from.z ? bodyRadius : -bodyRadius);
                if (grid.IsWalkableWorld(new Vector3(p.x, from.y, probe))) p.z = to.z;
            }
            return p;
        }

        // Pushes away from a few zombies in the same spatial bucket (from last frame's rebuild).
        Vector3 Separation()
        {
            if (SpatialCell < 0) return Vector3.zero;
            SpatialGrid sg = manager.Spatial;
            int start = sg.CellStart(SpatialCell);
            int n = sg.CellEnd(SpatialCell) - start;
            if (n <= 1) return Vector3.zero;

            Zombie[] items = sg.Items;
            float r = separationRadius, r2 = r * r;
            int offset = (ManagerIndex & 0x7fffffff) % n; // spread which neighbours each zombie looks at
            int checks = Mathf.Min(n, maxNeighbourChecks + 1);
            float px = 0f, pz = 0f;

            for (int k = 0; k < checks; k++)
            {
                Zombie o = items[start + (offset + k) % n];
                if (ReferenceEquals(o, this) || !o.IsAlive) continue;
                float dx = Position.x - o.Position.x, dz = Position.z - o.Position.z;
                float d2 = dx * dx + dz * dz;
                if (d2 >= r2) continue;
                if (d2 < 0.0001f) { px += phase % 1f - 0.5f; continue; } // exactly overlapping: nudge
                float d = Mathf.Sqrt(d2);
                float wgt = (1f - d / r) / d;
                px += dx * wgt;
                pz += dz * wgt;
            }
            return new Vector3(px, 0f, pz);
        }

        void Die(bool killed)
        {
            if (killed)
            {
                GameManager gm = GameManager.Instance;
                if (gm != null) gm.RegisterKill(reward);
                PooledVfx.Spawn(deathVfx, AimPoint, Quaternion.identity);
            }
            Despawn();
        }

        void Despawn()
        {
            IsAlive = false;
            manager.Unregister(this);
            if (pooled != null) pooled.Release();
            else gameObject.SetActive(false);
        }
    }
}
