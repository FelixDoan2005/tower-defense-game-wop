using UnityEngine;
using ZombieTD.CameraSystem;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.Maze
{
    /// <summary>
    /// The base's blast doors across the goal. They start open, slam shut when wave 1 begins,
    /// and collapse when the base health (owned by GameManager, shown on the HUD) reaches zero.
    /// Zombies in the goal cells press against the doors and damage them (see Zombie/ZombieManager).
    /// Built and configured by LevelGenerator.
    /// </summary>
    public class BaseGate : MonoBehaviour
    {
        enum Phase { Open, Closing, Closed, Collapsing, Fallen }

        [SerializeField] Transform doorA;
        [SerializeField] Transform doorB;
        [SerializeField, Tooltip("Local offset from closed to open (doors slide into the walls).")] Vector3 openOffsetA;
        [SerializeField] Vector3 openOffsetB;
        [SerializeField, Tooltip("Unit direction from the road toward the outside of the map.")] Vector3 outward = Vector3.left;
        [SerializeField] float health = 600f;
        [SerializeField] float thickness = 1.2f;

        [Header("Feel")]
        [SerializeField] float closeDuration = 2.5f;
        [SerializeField] float slamShake = 0.45f;
        [SerializeField] float collapseDuration = 0.9f;
        [SerializeField] float collapseShake = 0.8f;

        Phase phase;
        float timer;
        Vector3 closedA, closedB;
        WaveSpawner spawner;

        public bool IsClosed => phase == Phase.Closed;

        /// <summary>Called by LevelGenerator right after building the door objects (closed position).</summary>
        public void Configure(Transform a, Transform b, Vector3 offsetA, Vector3 offsetB, Vector3 outwardDir,
            float maxHealth, float doorThickness)
        {
            doorA = a;
            doorB = b;
            openOffsetA = offsetA;
            openOffsetB = offsetB;
            outward = outwardDir;
            health = maxHealth;
            thickness = doorThickness;
        }

        void Start()
        {
            closedA = doorA.localPosition;
            closedB = doorB.localPosition;
            SetClosedAmount(0f);
            phase = Phase.Open;

            GameManager gm = GameManager.Instance;
            if (gm != null)
            {
                gm.SetBaseMaxHealth(health);
                gm.StateChanged += OnStateChanged;
            }
            if (ZombieManager.Instance != null) ZombieManager.Instance.GateStandoff = thickness;

            spawner = FindAnyObjectByType<WaveSpawner>(); // one-time lookup
            if (spawner != null) spawner.WaveStarted += OnWaveStarted;
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= OnStateChanged;
            if (spawner != null) spawner.WaveStarted -= OnWaveStarted;
        }

        void OnWaveStarted(int wave)
        {
            if (phase != Phase.Open) return;
            phase = Phase.Closing;
            timer = 0f;
        }

        void OnStateChanged(GameState state)
        {
            if (state != GameState.Lost || phase == Phase.Collapsing || phase == Phase.Fallen) return;
            SetClosedAmount(1f);
            phase = Phase.Collapsing;
            timer = 0f;
            CameraRig.Shake(collapseShake);
        }

        void Update()
        {
            switch (phase)
            {
                case Phase.Closing:
                {
                    timer += Time.deltaTime;
                    float k = Mathf.Clamp01(timer / closeDuration);
                    SetClosedAmount(k * k); // accelerate into the slam
                    if (k >= 1f)
                    {
                        phase = Phase.Closed;
                        CameraRig.ShakeAt(transform.position, slamShake);
                    }
                    break;
                }
                case Phase.Collapsing:
                {
                    // Unscaled: the game freezes on defeat but the doors should still fall.
                    timer += Time.unscaledDeltaTime;
                    float k = Mathf.Clamp01(timer / collapseDuration);
                    Topple(doorA, closedA, k * k);
                    Topple(doorB, closedB, k * k);
                    if (k >= 1f) phase = Phase.Fallen;
                    break;
                }
            }
        }

        void SetClosedAmount(float closed)
        {
            doorA.localPosition = closedA + openOffsetA * (1f - closed);
            doorB.localPosition = closedB + openOffsetB * (1f - closed);
        }

        // Tips a door outward (into the base) and lets it sink as it falls.
        void Topple(Transform door, Vector3 closedPos, float k)
        {
            float h = door.localScale.y;
            door.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.Slerp(Vector3.up, outward, k * 0.95f));
            door.localPosition = closedPos + outward * (k * h * 0.5f) + Vector3.down * (k * h * 0.4f);
        }
    }
}
