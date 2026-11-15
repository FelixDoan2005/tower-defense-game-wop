using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ZombieTD.Core
{
    public enum GameState { Playing, Won, Lost }

    /// <summary>Owns base (gate) health, money, wave number and win/lose state.</summary>
    [DefaultExecutionOrder(-90)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField, Tooltip("Used when the level has no gate; a BaseGate overrides it.")] float baseMaxHealth = 600f;
        [SerializeField] int startingMoney = 300;
        [SerializeField, Tooltip("Freeze time on win/lose (camera keeps working).")] bool freezeOnGameOver = true;

        public float BaseHealth { get; private set; }
        public float MaxBaseHealth => baseMaxHealth;
        public int Money { get; private set; }
        public int Wave { get; private set; }
        public int TotalWaves { get; private set; }
        public int Kills { get; private set; }
        public GameState State { get; private set; } = GameState.Playing;

        /// <summary>Bumped on any stat change. UI polls this instead of reacting to every kill.</summary>
        public int Version { get; private set; }

        public event Action<GameState> StateChanged;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Time.timeScale = 1f;
            BaseHealth = baseMaxHealth;
            Money = startingMoney;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (State == GameState.Playing) return;
            Keyboard kb = Keyboard.current;
            if (kb == null) return;
            if (kb.rKey.wasPressedThisFrame) Restart();
            else if (State == GameState.Won && kb.nKey.wasPressedThisFrame && LevelProgress.HasNextLevel) NextLevel();
        }

        public void NextLevel()
        {
            LevelProgress.CurrentIndex++;
            Restart();
        }

        /// <summary>Sets max and current base health (called by the level's gate at startup).</summary>
        public void SetBaseMaxHealth(float max)
        {
            baseMaxHealth = Mathf.Max(1f, max);
            BaseHealth = baseMaxHealth;
            Version++;
        }

        public void DamageBase(float amount)
        {
            if (State != GameState.Playing) return;
            BaseHealth = Mathf.Max(0f, BaseHealth - amount);
            Version++;
            if (BaseHealth <= 0f) SetState(GameState.Lost);
        }

        public void RegisterKill(int reward)
        {
            Kills++;
            Money += reward;
            Version++;
        }

        public void AddMoney(int amount)
        {
            Money += amount;
            Version++;
        }

        public bool CanAfford(int cost) => Money >= cost;

        public bool TrySpend(int cost)
        {
            if (Money < cost) return false;
            Money -= cost;
            Version++;
            return true;
        }

        public void SetWave(int wave, int total)
        {
            Wave = wave;
            TotalWaves = total;
            Version++;
        }

        public void Win()
        {
            if (State == GameState.Playing) SetState(GameState.Won);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        void SetState(GameState s)
        {
            State = s;
            Version++;
            if (freezeOnGameOver && s != GameState.Playing) Time.timeScale = 0f;
            StateChanged?.Invoke(s);
        }
    }
}
