using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Enemies;
using ZombieTD.Maze;

namespace ZombieTD.Towers
{
    /// <summary>
    /// Base for anything placed on a pad: finds a target through the SpatialGrid, turns toward it and fires at
    /// WeaponData.fireRate. Subclasses implement Fire(). Range is measured on the XZ plane since units stand on roofs.
    /// While a unit is being dragged it is disabled (it just follows the cursor); Place() switches it on.
    /// </summary>
    public abstract class Tower : MonoBehaviour
    {
        [SerializeField] protected WeaponData weapon;

        [Header("Rig")]
        [SerializeField, Tooltip("Rotates around Y to face the target. Defaults to the unit root.")] protected Transform yawPivot;

        [Header("Targeting")]
        [SerializeField] TargetMode targetMode = TargetMode.First;
        [SerializeField] float retargetInterval = 0.2f;

        [Header("Line of fire")]
        [SerializeField, Tooltip("Targets on the far side of the road are always preferred over near-side ones.")]
        bool preferFarSide = true;

        [Header("Audio (optional)")]
        [SerializeField] AudioSource audioSource;

        const float NearSidePenalty = 100000f; // makes any far-side target beat any near-side one
        const float SampleStep = 0.6f;

        protected Zombie target;
        PooledObject pooled;
        float cooldown, retargetTimer;
        int ammo;            // shots left in the magazine (int.MaxValue when the weapon never reloads)
        float reloadLeft;    // seconds of reloading remaining
        float hitPoints;     // what zombies chew through (road units only)

        /// <summary>Remaining hit points / max, for a health display.</summary>
        public float HealthFraction => weapon != null && weapon.health > 0f ? hitPoints / weapon.health : 1f;

        // Road geometry around this unit's pad, found when it is placed.
        MazeGrid grid;
        Vector3 towardRoad;      // unit vector from the pad toward the road
        float farSideDistance;   // distance (along towardRoad) beyond which a target is across the road's centre line
        float roofEdgeDistance;  // distance (along towardRoad) from the unit to the roof edge above the road
        bool hasRoadInfo;

        protected bool HasRoadInfo => hasRoadInfo;
        protected Vector3 TowardRoad => towardRoad;
        protected float RoofEdgeDistance => roofEdgeDistance;
        /// <summary>Height of the roof surface this unit stands on.</summary>
        protected float RoofLevel => Slot != null ? Slot.MountPoint.y : transform.position.y;

        public WeaponData Weapon => weapon;
        public WallSlot Slot { get; private set; }
        public TargetMode TargetMode { get => targetMode; set => targetMode = value; }
        /// <summary>True while the magazine is being swapped: the unit tracks targets but doesn't fire.</summary>
        public bool IsReloading => reloadLeft > 0f;

        protected virtual void Awake()
        {
            pooled = GetComponent<PooledObject>();
        }

        /// <summary>Starts a unit that was just spawned for dragging; it follows the cursor until placed.</summary>
        public void BeginCarry(WeaponData data)
        {
            if (data != null) weapon = data;
            Slot = null;
            target = null;
            enabled = false;
        }

        /// <summary>Puts the unit on a pad (in the next free spot of the squad) and switches it on.</summary>
        public void Place(WallSlot slot)
        {
            Slot = slot;
            int index = slot.Add(this, weapon);
            transform.position = slot.FormationPoint(weapon, index);
            if (weapon.slotCapacity <= 1 || weapon.padKind == PadKind.Wall) transform.rotation = Quaternion.LookRotation(slot.Forward); // a big unit faces the way its pad does
            // Roof soldiers pick random targets so the fire is spread over the whole horde; units on the road keep their own mode.
            targetMode = slot.IsGround ? weapon.defaultTargetMode : TargetMode.Random;
            hitPoints = weapon.health;
            target = null;
            cooldown = 0f;
            retargetTimer = Random.value * retargetInterval; // stagger searches across units

            // Start with a partly full magazine so a squad placed together doesn't all reload at the same moment.
            reloadLeft = 0f;
            ammo = weapon.magazineSize > 0 ? Mathf.Max(1, Mathf.RoundToInt(weapon.magazineSize * Random.Range(0.5f, 1f))) : int.MaxValue;

            FindRoadInfo();
            enabled = true;
        }

        // Magazine empty: stop firing for the reload time (a little different for each soldier, so squads don't sync up).
        void StartReload()
        {
            reloadLeft = Mathf.Max(0.1f, weapon.reloadTime) * Random.Range(0.85f, 1.15f);
            cooldown = 0f;
            if (Slot != null) Slot.RefreshLabel();
        }

        // Which way the road lies from this pad, and where the middle of the road is.
        void FindRoadInfo()
        {
            hasRoadInfo = false;
            ZombieManager zm = ZombieManager.Instance;
            grid = zm != null ? zm.Grid : null;
            if (grid == null || Slot == null) return;
            if (Slot.IsGround) return; // on the road itself: no roof edge, no far/near side, nothing in the way of shots

            // The wall tile this pad sits on (pads are free-standing objects now, so it comes from their position).
            if (!grid.WorldToCell(Slot.MountPoint, out int padX, out int padZ)) return;
            Vector2Int c = new Vector2Int(padX, padZ);
            Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            for (int k = 1; k <= 3; k++)
                foreach (Vector2Int d in dirs)
                {
                    if (!grid.IsWalkable(c.x + d.x * k, c.y + d.y * k)) continue;

                    int width = 0;
                    while (grid.IsWalkable(c.x + d.x * (k + width), c.y + d.y * (k + width))) width++;

                    towardRoad = new Vector3(d.x, 0f, d.y);
                    // Distance from the unit to the first road tile's centre, minus half a tile = the road's near edge.
                    float toFirst = Vector3.Dot(grid.CellToWorld(c.x + d.x * k, c.y + d.y * k) - transform.position, towardRoad);
                    roofEdgeDistance = toFirst - grid.CellSize * 0.5f;
                    farSideDistance = roofEdgeDistance + width * grid.CellSize * 0.5f;
                    hasRoadInfo = true;
                    return;
                }
        }

        /// <summary>Where a shot at the given point leaves the unit. Override for a model-specific muzzle.</summary>
        protected virtual Vector3 ShotOrigin(Vector3 toward)
        {
            Vector3 d = toward - transform.position;
            d.y = 0f;
            d = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
            return transform.position + Vector3.up * 1.5f + d * 0.5f;
        }

        // False if the straight line from origin to target passes through a building: i.e. dips below the roof
        // surface while still over a wall tile (it would cut through the facade or the roof edge).
        bool ClearShot(Vector3 origin, Vector3 targetPoint)
        {
            if (grid == null || Slot == null || Slot.IsGround) return true;
            if (weapon.shootOverOwnBuilding) return true; // long shots arc over the rooftops: from a roof, any flat line down the street would clip the buildings beside it
            float roofY = Slot.MountPoint.y;
            Vector3 delta = targetPoint - origin;
            float horizontal = Mathf.Sqrt(delta.x * delta.x + delta.z * delta.z);
            int steps = Mathf.Min(400, Mathf.CeilToInt(horizontal / SampleStep));
            bool overOwnBuilding = weapon.shootOverOwnBuilding; // long shots skip the wall run the unit stands on, until the line reaches open road
            for (int i = 1; i < steps; i++)
            {
                Vector3 p = origin + delta * (i / (float)steps);
                bool inCell = grid.WorldToCell(p, out int x, out int z);
                bool wall = inCell && grid.IsBuilding(x, z);                 // police cars don't count: bullets fly over them
                if (inCell && !wall) overOwnBuilding = false;               // over the road now: any building after this really is in the way
                if (p.y >= roofY) continue;                                  // still above the roof: clear
                if (wall && !overOwnBuilding) return false; // inside a building
            }
            return true;
        }

        /// <summary>
        /// Zombies hurt this unit. Returns the damage left over if it broke (so a pad can pass it to the next unit).
        /// A broken unit is removed from its pad with a puff of its hit effect.
        /// </summary>
        public float Hurt(float amount)
        {
            if (weapon == null || Slot == null) return 0f;
            if (amount < hitPoints) { hitPoints -= amount; return 0f; }

            float leftover = amount - hitPoints;
            hitPoints = 0f;
            PooledVfx.Spawn(weapon.hitVfx, transform.position + Vector3.up * 0.5f, Quaternion.identity);
            Remove();
            return leftover;
        }

        /// <summary>Frees the pad (if any) and returns the unit to the pool.</summary>
        public void Remove()
        {
            if (Slot != null) Slot.Remove(this);
            Slot = null;
            target = null;
            reloadLeft = 0f;
            enabled = false;
            if (pooled != null) pooled.Release();
            else Destroy(gameObject);
        }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            if (weapon == null || dt <= 0f) return;

            cooldown -= dt;
            retargetTimer -= dt;

            if (reloadLeft > 0f)
            {
                reloadLeft -= dt;
                if (reloadLeft <= 0f)
                {
                    reloadLeft = 0f;
                    ammo = weapon.magazineSize;
                    if (Slot != null) Slot.RefreshLabel();
                }
            }

            if (target != null && (!target.IsAlive || !InRange(target.Position))) target = null;
            if (target == null || (retargetTimer <= 0f && targetMode != TargetMode.Random)) // a random pick is kept until it dies or leaves range
            {
                retargetTimer = retargetInterval;
                target = FindTarget();
            }

            if (target == null) { if (cooldown < 0f) cooldown = 0f; return; }

            Vector3 aim = target.AimPoint;
            if (!ClearShot(ShotOrigin(aim), aim))
            {
                // The zombie moved somewhere the shot would pass through a wall: drop it and look again.
                target = null;
                retargetTimer = 0f;
                return;
            }
            if (!AimAt(aim, dt)) { if (cooldown < 0f) cooldown = 0f; return; }

            // Still tracking the target while reloading, just not firing.
            if (reloadLeft > 0f) { if (cooldown < 0f) cooldown = 0f; return; }

            float interval = 1f / Mathf.Max(0.01f, weapon.fireRate);
            int shots = 0;
            while (cooldown <= 0f && shots < 4 && target != null)
            {
                Fire(target, aim);
                cooldown += interval;
                shots++;
                if (target != null && !target.IsAlive) target = null;

                if (weapon.magazineSize > 0 && --ammo <= 0) { StartReload(); break; } // empty: reload
            }
        }

        /// <summary>Apply damage and spawn effects. Called once per shot.</summary>
        protected abstract void Fire(Zombie target, Vector3 aimPoint);

        protected bool InRange(Vector3 p)
        {
            float dx = p.x - transform.position.x, dz = p.z - transform.position.z;
            return dx * dx + dz * dz <= weapon.range * weapon.range;
        }

        /// <summary>Rotates toward a point. Returns true when aimed within tolerance.</summary>
        protected bool AimAt(Vector3 point, float dt)
        {
            Transform yp = yawPivot != null ? yawPivot : transform;
            Vector3 flat = point - yp.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return true;

            Quaternion want = Quaternion.LookRotation(flat);
            yp.rotation = Quaternion.RotateTowards(yp.rotation, want, weapon.turnSpeed * dt);
            return Quaternion.Angle(yp.rotation, want) <= weapon.aimTolerance;
        }

        /// <summary>Sound and camera shake. Call from Fire().</summary>
        protected void PlayFireFeedback()
        {
            if (audioSource != null && weapon.fireSound != null) audioSource.PlayOneShot(weapon.fireSound, weapon.fireVolume);
            if (weapon.shakeOnFire > 0f) ZombieTD.CameraSystem.CameraRig.ShakeAt(transform.position, weapon.shakeOnFire);
        }

        // Scans the cell rows covering the range circle and scores candidates by target mode.
        Zombie FindTarget()
        {
            ZombieManager zm = ZombieManager.Instance;
            if (zm == null) return null;
            SpatialGrid sg = zm.Spatial;

            Vector3 c = transform.position;
            float r = weapon.range, r2 = r * r, invR2 = 1f / (r2 + 1f);
            sg.GetCellRect(c, r, out int x0, out int z0, out int x1, out int z1);
            Zombie[] items = sg.Items;
            Zombie best = null;
            float bestScore = float.MaxValue;

            for (int z = z0; z <= z1; z++)
            {
                int end = sg.RowEnd(z, x1);
                for (int i = sg.RowStart(z, x0); i < end; i++)
                {
                    Zombie zb = items[i];
                    if (!zb.IsAlive) continue;
                    float dx = zb.Position.x - c.x, dz = zb.Position.z - c.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 > r2) continue;

                    float score;
                    switch (targetMode)
                    {
                        case TargetMode.First: score = zb.PathDistance + d2 * invR2; break; // distance breaks ties
                        case TargetMode.Strongest: score = -zb.Health; break;
                        case TargetMode.Random: score = Random.value; break;
                        default: score = d2; break;
                    }

                    // Far side of the road first; the near side (right under our own wall) only if nothing else is in range.
                    if (preferFarSide && hasRoadInfo && Vector3.Dot(zb.Position - c, towardRoad) < farSideDistance)
                        score += NearSidePenalty;

                    // Only check the line of fire for candidates that would actually win.
                    if (score < bestScore && ClearShot(ShotOrigin(zb.AimPoint), zb.AimPoint)) { bestScore = score; best = zb; }
                }
            }

            return best;
        }
    }
}
