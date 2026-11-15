using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.Towers
{
    /// <summary>
    /// Stationary rifleman: turns to face a zombie, leans over the parapet and fires single rifle shots (instant hit)
    /// with a muzzle flash on his gun, a tracer and a hit effect, playing the "Shoot" animation on every shot.
    /// </summary>
    public class SoldierTower : Tower
    {
        [Header("Soldier")]
        [SerializeField] Animator animator;
        [SerializeField, Tooltip("Animator state to play on each shot.")] string shootState = "Shoot";

        [Header("Gun")]
        [SerializeField, Tooltip("A bone/object on the weapon (found automatically by the setup tool). Shots and the flash come from here.")]
        Transform muzzleAnchor;
        [SerializeField, Tooltip("Pushes the shot origin forward from the anchor toward the target, to reach the barrel tip.")] float barrelReach = 0.45f;
        [SerializeField, Tooltip("Used when there is no anchor: height above the unit's feet.")] float muzzleHeight = 0.65f;
        [SerializeField, Tooltip("Used when there is no anchor: distance in front of the unit.")] float muzzleForward = 0.6f;
        [SerializeField] float tracerLifetime = 0.07f;
        [SerializeField, Tooltip("Random scatter of the visual hit point around the target.")] float hitScatter = 0.08f;

        [Header("Lean")]
        [SerializeField, Tooltip("How far he leans forward over the parapet while he has a target (degrees).")] float leanAngle = 20f;
        [SerializeField] float leanSpeed = 90f;

        int shootHash;
        Transform visual;
        float lean;

        protected override void Awake()
        {
            base.Awake();
            if (animator == null) animator = GetComponentInChildren<Animator>();
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
            float want = target != null ? leanAngle : 0f;
            lean = Mathf.MoveTowards(lean, want, leanSpeed * dt);
            visual.localRotation = Quaternion.Euler(lean, 0f, 0f); // pivots at his feet, so his head and gun swing out over the edge
        }

        // The gun: the weapon bone (leaning and recoil move it), pushed a little toward the target for the barrel tip.
        protected override Vector3 ShotOrigin(Vector3 toward)
        {
            Vector3 d = toward - transform.position;
            d.y = 0f;
            d = d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;

            if (muzzleAnchor != null) return muzzleAnchor.position + d * barrelReach;
            return transform.position + Vector3.up * muzzleHeight + d * muzzleForward;
        }

        protected override void Fire(Zombie target, Vector3 aimPoint)
        {
            Vector3 dir = aimPoint - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
            Vector3 muzzle = ShotOrigin(aimPoint);
            Vector3 hit = aimPoint + Random.insideUnitSphere * hitScatter;

            if (weapon.knockback > 0f) target.Knockback(dir * weapon.knockback);
            target.TakeDamage(weapon.damage);

            if (weapon.tracerPrefab != null)
            {
                Tracer t = PoolManager.Instance.Spawn(weapon.tracerPrefab, muzzle, Quaternion.identity);
                t.Play(muzzle, hit, tracerLifetime);
            }

            Vector3 toShooter = muzzle - hit;
            PooledVfx.Spawn(weapon.hitVfx, hit, toShooter.sqrMagnitude > 0f ? Quaternion.LookRotation(toShooter) : Quaternion.identity);
            Vector3 shotDir = hit - muzzle;
            PooledVfx.Spawn(weapon.muzzleVfx, muzzle, shotDir.sqrMagnitude > 0f ? Quaternion.LookRotation(shotDir) : transform.rotation);

            if (animator != null) animator.CrossFadeInFixedTime(shootHash, 0.04f, 0, 0f);
            PlayFireFeedback();
        }
    }
}
