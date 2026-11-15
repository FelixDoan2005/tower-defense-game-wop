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

        public int CurrentWave { get; private set; }
        public int TotalWaves => totalWaves;
        public bool IsWaveActive { get; private set; }
        public float Countdown { get; private set; }
        public int RemainingToSpawn { get; private set; }

        /// <summary>Raised with the wave number when a wave begins spawning.</summary>
        public event System.Action<int> WaveStarted;

        MazeGrid grid;
        float spawnRate, spawnAccumulator, healthMul, speedMul, rewardMul;
        int spawnCursor;

        void Start()
        {
            if (zombies == null) zombies = ZombieManager.Instance;
            grid = level != null ? level.Grid : zombies.Grid;
            PoolManager.Instance.Prewarm(zombiePrefab.gameObject, prewarmCount);
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
                Countdown -= dt;
                Keyboard kb = Keyboard.current;
                if (Countdown <= 0f || (kb != null && kb.spaceKey.wasPressedThisFrame))
                    StartWave(CurrentWave + 1);
                return;
            }

            if (RemainingToSpawn > 0)
            {
                spawnAccumulator += spawnRate * dt;
                while (spawnAccumulator >= 1f && RemainingToSpawn > 0 && zombies.ActiveCount < maxAlive)
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
            if (!IsWaveActive) Countdown = 0f;
        }

        void StartWave(int wave)
        {
            CurrentWave = wave;
            int k = wave - 1;
            RemainingToSpawn = Mathf.RoundToInt(baseCount * Mathf.Pow(countGrowth, k));
            spawnRate = Mathf.Min(maxSpawnRate, baseSpawnRate * Mathf.Pow(spawnRateGrowth, k));
            healthMul = Mathf.Pow(healthGrowth, k);
            speedMul = Mathf.Pow(speedGrowth, k);
            rewardMul = Mathf.Pow(rewardGrowth, k);
            spawnAccumulator = 1f; // first zombie appears right away
            IsWaveActive = true;
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
