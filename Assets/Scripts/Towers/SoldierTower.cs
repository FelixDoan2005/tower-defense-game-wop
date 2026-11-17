using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.Towers
{
    /// <summary>
    /// Stationary soldier: turns to face a zombie, leans over the parapet and fires instant-hit shots with a muzzle
    /// flash on his gun, a tracer and a hit effect, playing the "Shoot" animation on every shot.
    /// With WeaponData.spreadDegrees above 0 (machine gunners) shots scatter in a cone: a miss can land on a
    /// neighbouring zombie in the crowd, or on empty ground.
    /// </summary>
    public class SoldierTower : Tower
    {
        static readonly Zombie[] HitBuffer = new Zombie[64]; // shared scratch buffer, main thread only

        [Header("Soldier")]
        [SerializeField] Animator animator;
        [SerializeField, Tooltip("Animator state to play on each shot.")] string shootState = "Shoot";

        [Header("Gun")]
        [SerializeField, Tooltip("A bone/object on the weapon (found automatically by the setup tool). Shots and the flash come from here.")]
        Transform muzzleAnchor;
        [SerializeField, Tooltip("More guns for a unit made of several men (the Shotgun Wall): shots take turns coming from each.")]
        Transform[] extraMuzzles;
        [SerializeField, Tooltip("Pushes the shot origin forward from the anchor toward the target, to reach the barrel tip.")] float barrelReach = 0.45f;
        [SerializeField, Tooltip("Used when there is no anchor: height above the unit's feet.")] float muzzleHeight = 0.65f;
        [SerializeField, Tooltip("Used when there is no anchor: distance in front of the unit.")] float muzzleForward = 0.6f;
        [SerializeField] float tracerLifetime = 0.07f;
        [SerializeField, Tooltip("Random scatter of the visual hit point around the target.")] float hitScatter = 0.08f;

        [Header("Lean")]
        [SerializeField, Tooltip("How far he leans forward over the parapet while he has a target (degrees).")] float leanAngle = 20f;
        [SerializeField] float leanSpeed = 90f;
        [SerializeField, Tooltip("Units without an animator (the SWAT characters) rock back this many degrees on every shot, so they still look like they fire.")]
        float recoilKick = 5f;

        int shootHash;
        int shotCount;
        Transform visual;
        float lean, kick;

        protected override void Awake()
        {
            base.Awake();
            if (animator == null) animator = GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController == null) animator = null; // a bare avatar plays nothing; use the recoil kick instead
            shootHash = Animator.StringToHash(shootState);
            visual = transform.Find("Visual");
        }

        protected override void Update()
        {
            base.Update();
            UpdateLean(Time.deltaTime);
        }

        // Leans forward over the wall while engaging, straightens up when idle.
        void UpdateLean(float dt)
        {
            if (visual == null) return;
            float want = target != null && !IsReloading ? leanAngle : 0f; // stands up straight while reloading
            lean = Mathf.MoveTowards(lean, want, leanSpeed * dt);
            kick = Mathf.MoveTowards(kick, 0f, 12f * dt); // recoil settles back in a fraction of a second
            visual.localRotation = Quaternion.Euler(lean - kick * recoilKick, 0f, 0f); // pivots at his feet, so his head and gun swing out over the edge
        }

        // The gun: the weapon bone (leaning and recoil move it), pushed a little toward the target for the barrel tip.
        protected override Vector3 ShotOrigin(Vector3 toward)
        {
            Vector3 d = toward - transform.position;
            d.y = 0f;
            d = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;

            if (muzzleAnchor != null)
            {
                // With several guns, each shot comes from the next one in turn.
                int guns = 1 + (extraMuzzles != null ? extraMuzzles.Length : 0);
                int pick = guns > 1 ? shotCount % guns : 0;
                Transform gun = pick == 0 || extraMuzzles[pick - 1] == null ? muzzleAnchor : extraMuzzles[pick - 1];
                return gun.position + d * barrelReach;
            }
            return transform.position + Vector3.up * muzzleHeight + d * muzzleForward;
        }

        protected override void Fire(Zombie target, Vector3 aimPoint)
        {
            shotCount++;
            bool showEffects = weapon.effectEvery <= 1 || shotCount % weapon.effectEvery == 0;

            Vector3 dir = aimPoint - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
            Vector3 muzzle = ShotOrigin(aimPoint);

            // Standing on the road or on a roof changes how hard this unit hits (a shotgun is a street weapon).
            bool onRoad = Slot != null && Slot.IsGround;
            float damage = weapon.damage * (onRoad ? weapon.damageOnGround : weapon.damageOnRoof);
            float splashRadius = weapon.splashRadius * (onRoad ? weapon.splashOnGround : weapon.splashOnRoof);

            // A shotgun fires several pellets at once, each scattering on its own inside the cone.
            int pellets = Mathf.Max(1, weapon.pellets);
            Vector3 firstImpact = aimPoint;
            Zombie firstVictim = null;
            for (int p = 0; p < pellets; p++)
            {
                // Who this pellet actually hits, and where it lands.
                Zombie victim = target;
                Vector3 impact = aimPoint + Random.insideUnitSphere * hitScatter;
                if (weapon.spreadDegrees > 0f) ScatterShot(muzzle, aimPoint, out victim, out impact);
                if (p == 0) { firstImpact = impact; firstVictim = victim; }

                if (victim != null)
                {
                    if (weapon.knockback > 0f) victim.Knockback(dir * weapon.knockback);
                    victim.TakeDamage(damage);
                }

                if (showEffects)
                {
                    if (weapon.tracerPrefab != null)
                    {
                        Tracer t = PoolManager.Instance.Spawn(weapon.tracerPrefab, muzzle, Quaternion.identity);
                        t.Play(muzzle, impact, weapon.tracerLife > 0f ? weapon.tracerLife : tracerLifetime);
                    }
                    if (victim != null && pellets > 1 && p > 0 && p < 3) // a few pellet strikes show; the rest are just tracers
                    {
                        Vector3 toShooter = muzzle - impact;
                        PooledVfx.Spawn(weapon.hitVfx, impact, toShooter.sqrMagnitude > 0f ? Quaternion.LookRotation(toShooter) : Quaternion.identity);
                    }
                }
            }

            // One blast for the whole shot, where the first pellet landed.
            if (splashRadius > 0f) Splash(firstImpact, splashRadius, damage * weapon.splashDamageFraction, firstVictim);
            if (weapon.shakeOnImpact > 0f) ZombieTD.CameraSystem.CameraRig.ShakeAt(firstImpact, weapon.shakeOnImpact);

            if (showEffects)
            {
                if (firstVictim != null || splashRadius > 0f) // an area weapon shows its blast even when the shot lands between zombies
                {
                    Vector3 toShooter = muzzle - firstImpact;
                    PooledVfx.Spawn(weapon.hitVfx, firstImpact, toShooter.sqrMagnitude > 0f ? Quaternion.LookRotation(toShooter) : Quaternion.identity);
                }
                Vector3 shotDir = firstImpact - muzzle;
                PooledVfx.Spawn(weapon.muzzleVfx, muzzle, shotDir.sqrMagnitude > 0f ? Quaternion.LookRotation(shotDir) : transform.rotation);
            }

            // The shoot animation restarts only with the effects: at 30 shots a second, restarting it every bullet would freeze it on frame 1.
            if (animator != null && showEffects) animator.CrossFadeInFixedTime(shootHash, 0.04f, 0, 0f);
            else if (animator == null) kick = 1f; // no animation: kick the whole body back instead
            PlayFireFeedback();
        }

        // Hurts everything near the impact, less toward the edge, and shoves it outward a little.
        void Splash(Vector3 centre, float radius, float damage, Zombie skip)
        {
            ZombieManager zm = ZombieManager.Instance;
            if (zm == null || damage <= 0f) return;

            int n = zm.Spatial.Query(centre, radius, HitBuffer);
            for (int i = 0; i < n; i++)
            {
                Zombie z = HitBuffer[i];
                HitBuffer[i] = null;
                if (z == skip || !z.IsAlive) continue;

                Vector3 away = z.Position - centre;
                away.y = 0f;
                float d = away.magnitude;
                float falloff = 1f - 0.6f * Mathf.Clamp01(d / radius);
                if (weapon.knockback > 0f && d > 0.01f) z.Knockback(away / d * (weapon.knockback * falloff));
                z.TakeDamage(damage * falloff);
            }
        }

        // Fires inside a cone around the target direction. The bullet lands where it reaches zombie height (or at the
        // target's distance if it flies level), and hits whichever zombie is closest to that spot, if any is close enough.
        void ScatterShot(Vector3 origin, Vector3 aimPoint, out Zombie victim, out Vector3 impact)
        {
            Vector3 toAim = aimPoint - origin;
            float distance = toAim.magnitude;
            Vector3 axis = distance > 0.0001f ? toAim / distance : transform.forward;

            Vector3 perp = Vector3.Cross(axis, Vector3.up);
            if (perp.sqrMagnitude < 0.0001f) perp = Vector3.right;
            perp.Normalize();
            float angle = weapon.spreadDegrees * Mathf.Sqrt(Random.value); // uniform over the cone
            Vector3 dir = Quaternion.AngleAxis(Random.value * 360f, axis) * (Quaternion.AngleAxis(angle, perp) * axis);

            float t = dir.y < -0.01f ? (aimPoint.y - origin.y) / dir.y : distance;
            t = Mathf.Clamp(t, 0.1f, distance * 1.25f);
            Vector3 landing = origin + dir * t;

            victim = null;
            impact = landing;
            ZombieManager zm = ZombieManager.Instance;
            if (zm == null) return;

            int n = zm.Spatial.Query(landing, weapon.hitRadius, HitBuffer);
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Zombie z = HitBuffer[i];
                HitBuffer[i] = null;
                float d = (z.AimPoint - landing).sqrMagnitude;
                if (d < best) { best = d; victim = z; }
            }
            if (victim != null) impact = victim.AimPoint;
        }
    }
}
