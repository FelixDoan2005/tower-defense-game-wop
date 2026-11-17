using TMPro;
using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.UI
{
    /// <summary>
    /// Minimal HUD. Polls GameManager.Version so text only rebuilds when something changed,
    /// and uses TMP's SetText(format, args), which does not allocate.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [SerializeField] TMP_Text healthText;
        [SerializeField] TMP_Text moneyText;
        [SerializeField] TMP_Text waveText;
        [SerializeField] TMP_Text zombiesText;
        [SerializeField] TMP_Text messageText;

        [SerializeField] WaveSpawner spawner;
        [SerializeField] ZombieManager zombies;

        int lastVersion = -1, lastCountdown = -1, lastAlive = -1;
        bool lastWaveActive;
        string manualFormat;
        string waveFormat, countdownFormat; // built once per level (contain the level name)

        void Start()
        {
            if (zombies == null) zombies = ZombieManager.Instance;
            string title = string.IsNullOrEmpty(LevelProgress.CurrentName)
                ? string.Empty
                : $"Level {LevelProgress.CurrentIndex + 1}: {LevelProgress.CurrentName}   -   ";
            waveFormat = title + "Wave {0} / {1}";
            manualFormat = title + "Wave {0} / {1}   -   build your defence, then press Start  [Space]";
            countdownFormat = title + "Wave {0} / {1}   -   next wave in {2}s  [Space]";
            if (GameManager.Instance != null) GameManager.Instance.StateChanged += OnStateChanged;
            if (messageText != null) messageText.text = string.Empty;

            // Old scenes still have the controls help text; it no longer applies, so hide it.
            Transform help = transform.Find("Help");
            if (help != null) help.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.StateChanged -= OnStateChanged;
        }

        void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            bool waveActive = spawner != null && spawner.IsWaveActive;
            int countdown = spawner != null ? Mathf.CeilToInt(Mathf.Max(0f, spawner.Countdown)) : 0;

            if (gm.Version != lastVersion || waveActive != lastWaveActive || (!waveActive && countdown != lastCountdown))
            {
                lastVersion = gm.Version;
                lastWaveActive = waveActive;
                lastCountdown = countdown;

                healthText.SetText("Gate  {0:0}/{1:0}", Mathf.Ceil(gm.BaseHealth), gm.MaxBaseHealth);
                moneyText.SetText("$ {0}", gm.Money);
                int total = spawner != null ? spawner.TotalWaves : gm.TotalWaves;
                if (waveActive || gm.State != GameState.Playing)
                    waveText.SetText(waveFormat, gm.Wave, total);
                else if (spawner != null && spawner.ManualStart)
                    waveText.SetText(manualFormat, gm.Wave + 1, total);
                else
                    waveText.SetText(countdownFormat, gm.Wave, total, countdown);
            }

            int alive = zombies != null ? zombies.ActiveCount : 0;
            if (alive != lastAlive)
            {
                lastAlive = alive;
                if (zombiesText != null) zombiesText.SetText("Zombies  {0}", alive); // the counter can be deleted from the scene
            }
        }

        void OnStateChanged(GameState state)
        {
            if (messageText == null) return;
            string won = LevelProgress.HasNextLevel
                ? "VICTORY\n<size=50%>N: next level     R: replay</size>"
                : "ALL LEVELS CLEARED\n<size=50%>R: replay</size>";
            messageText.text = state == GameState.Won ? won
                : state == GameState.Lost ? "THE GATE HAS FALLEN\n<size=50%>Press R to try again</size>"
                : string.Empty;
        }
    }
}
