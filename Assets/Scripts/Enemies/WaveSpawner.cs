using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using ZombieTD.Core;
using ZombieTD.Maze;

namespace ZombieTD.Enemies
{
    /// <summary>
    /// Runs waves: countdown, then spawns zombies at a fixed rate from the spawn cells (round robin).
    /// Count, rate and zombie stats grow exponentially per wave. Space skips the countdown.
    /// </summary>
    public class WaveSpawner : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("maze")] LevelGenerator level;
        [SerializeField] ZombieManager zombies;
        [SerializeField] Zombie zombiePrefab;

        [Header("Waves")]
        [SerializeField, Min(1)] int totalWaves = 20;
        [SerializeField, Tooltip("On: waves only start when you press Start Wave (or Space), so you can build first. Off: the countdown below starts them.")]
        bool manualStart = true;
        [SerializeField] float firstWaveDelay = 15f;
        [SerializeField] float timeBetweenWaves = 10f;

        [Header("Count")]
        [SerializeField] int baseCount = 40;
        [SerializeField] float countGrowth = 1.22f;
        [SerializeField, Tooltip("Spawning pauses while this many are alive.")] int maxAlive = 4000;

        [Header("Spawn rate (zombies per second)")]
        [SerializeField] float baseSpawnRate = 10f;
        [SerializeField] float spawnRateGrowth = 1.12f;
        [SerializeField] float maxSpawnRate = 120f;

        [Header("Per-wave zombie scaling")]
        [SerializeField] float healthGrowth = 1.12f;
        [SerializeField] float speedGrowth = 1.015f;
        [SerializeField] float rewardGrowth = 1.0f;

        [Header("Pooling")]
        [SerializeField] int prewarmCount = 1500;

        [Header("Horde size (multiplies the numbers above)")]
        [SerializeField, Min(1f), Tooltip("Zombie count and spawn rate are multiplied by this: tiny zombies, huge crowds.")] float hordeScale = 6f;
        [SerializeField, Tooltip("Alive-zombie cap used if it is higher than Max Alive.")] int hordeMaxAlive = 6000;
        [SerializeField, Tooltip("Pool size created at startup if it is higher than Prewarm Count.")] int hordePrewarm = 3000;

        public int CurrentWave { get; private set; }
        public int TotalWaves => totalWaves;
        public bool IsWaveActive { get; private set; }
        public float Countdown { get; private set; }
        /// <summary>True when waves wait for the player to press Start.</summary>
        public bool ManualStart => manualStart;
        public int RemainingToSpawn { get; private set; }

        /// <summary>Raised with the wave number when a wave begins spawning.</summary>
        public event System.Action<int> WaveStarted;

        MazeGrid grid;
        float spawnRate, spawnAccumulator, healthMul, speedMul, rewardMul;
        int spawnCursor;
        bool startRequested;

        void Start()
        {
            if (zombies == null) zombies = ZombieManager.Instance;
            grid = level != null ? level.Grid : zombies.Grid;
            PoolManager.Instance.Prewarm(zombiePrefab.gameObject, Mathf.Max(prewarmCount, hordePrewarm));
            Countdown = firstWaveDelay;
            GameManager.Instance.SetWave(0, totalWaves);
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing) return;
            float dt = Time.deltaTime;

            if (!IsWaveActive)
            {
                Keyboard kb = Keyboard.current;
                bool space = kb != null && kb.spaceKey.wasPressedThisFrame;
                if (manualStart)
                {
                    if (startRequested || space) StartWave(CurrentWave + 1);
                    return;
                }
                Countdown -= dt;
                if (Countdown <= 0f || space) StartWave(CurrentWave + 1);
                return;
            }

            if (RemainingToSpawn > 0)
            {
                spawnAccumulator += spawnRate * dt;
                while (spawnAccumulator >= 1f && RemainingToSpawn > 0 && zombies.ActiveCount < Mathf.Max(maxAlive, hordeMaxAlive))
                {
                    SpawnOne();
                    spawnAccumulator -= 1f;
                    RemainingToSpawn--;
                }
                if (spawnAccumulator > 1f) spawnAccumulator = 1f; // no burst after hitting the cap
            }
            else if (zombies.ActiveCount == 0)
            {
                IsWaveActive = false;
                if (CurrentWave >= totalWaves) gm.Win();
                else Countdown = timeBetweenWaves;
            }
        }

        /// <summary>Starts the next wave immediately (e.g. from a UI button).</summary>
        public void SkipCountdown()
        {
            if (!IsWaveActive) { Countdown = 0f; startRequested = true; }
        }

        void StartWave(int wave)
        {
            CurrentWave = wave;
            int k = wave - 1;
            RemainingToSpawn = Mathf.RoundToInt(baseCount * Mathf.Pow(countGrowth, k) * hordeScale);
            spawnRate = Mathf.Min(maxSpawnRate * hordeScale, baseSpawnRate * Mathf.Pow(spawnRateGrowth, k) * hordeScale);
            healthMul = Mathf.Pow(healthGrowth, k);
            speedMul = Mathf.Pow(speedGrowth, k);
            rewardMul = Mathf.Pow(rewardGrowth, k);
            spawnAccumulator = 1f; // first zombie appears right away
            IsWaveActive = true;
            startRequested = false;
            GameManager.Instance.SetWave(wave, totalWaves);
            WaveStarted?.Invoke(wave);
        }

        void SpawnOne()
        {
            var spawns = grid.SpawnCells;
            Vector2Int c = spawns[spawnCursor++ % spawns.Count];
            float j = grid.CellSize * 0.3f;
            Vector3 p = grid.CellToWorld(c.x, c.y) + new Vector3(Random.Range(-j, j), 0f, Random.Range(-j, j));
            Zombie z = PoolManager.Instance.Spawn(zombiePrefab, p, Quaternion.identity);
            z.Spawn(zombies, p, healthMul, speedMul, rewardMul);
        }
    }
}
