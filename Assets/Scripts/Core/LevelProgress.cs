using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>Which level is being played. Static so it survives the scene reload between levels.</summary>
    public static class LevelProgress
    {
        public static int CurrentIndex;
        public static int LevelCount = 1;
        public static string CurrentName = "";

        public static bool HasNextLevel => CurrentIndex + 1 < LevelCount;

        // Runs once when Play starts (not on scene reloads), so every session begins at level 1
        // even with domain reload disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            CurrentIndex = 0;
            LevelCount = 1;
            CurrentName = "";
        }
    }
}
