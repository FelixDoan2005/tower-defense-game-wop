using UnityEngine;

namespace ZombieTD.Maze
{
    public enum LevelLayout
    {
        /// <summary>Straight horizontal road between two tall fortress walls. Spawn right, gate left.</summary>
        Corridor,
        /// <summary>Procedural recursive-backtracker maze.</summary>
        RandomMaze,
        /// <summary>Hand-drawn text map (see asciiMap legend).</summary>
        Ascii,
    }

    /// <summary>
    /// One level: layout, scale, decoration and camera framing. Create via Create > ZombieTD > Level
    /// and add it to the LevelCatalog. Defaults describe the intro corridor level.
    /// </summary>
    [CreateAssetMenu(menuName = "ZombieTD/Level", fileName = "NewLevel")]
    public class LevelData : ScriptableObject
    {
        public string displayName = "The Gate";
        public LevelLayout layout = LevelLayout.Corridor;

        [Header("Scale")]
        [Min(1f)] public float cellSize = 4f;
        [Min(1f), Tooltip("At a 62° camera the near wall hides ~0.53x its height of road, so tall walls need a wide road.")]
        public float wallHeight = 14f;

        [Header("Corridor layout (tiles)")]
        [Min(4)] public int corridorLength = 44;
        [Min(1)] public int corridorWidth = 5;
        [Min(1), Tooltip("Thickness of each defensive wall.")] public int wallThickness = 3;

        [Header("Random maze layout (tiles, odd)")]
        [Min(5)] public int mazeWidth = 31;
        [Min(5)] public int mazeHeight = 31;
        public int seed = 1337;
        public bool randomSeed;
        [Range(0f, 0.5f)] public float loopChance = 0.08f;
        [Min(1)] public int spawnCount = 3;

        [Header("ASCII layout")]
        [TextArea(6, 30), Tooltip("# wall   . road   S spawn   G goal/gate. Top line = far side of the map.")]
        public string asciiMap =
            "##########\n" +
            "G........S\n" +
            "##########";

        [Header("Building")]
        [Min(1), Tooltip("Wall tiles this close to a road get a build slot (1 = edge row only).")] public int slotReach = 2;
        [Tooltip("Extra ground around the map, in world units.")] public float groundMargin = 8f;

        [Header("Road floor")]
        [Tooltip("Pattern the road with the sci-fi kit floor strips (needs the Floor Material assigned on the Level object).")]
        public bool kitFloor = true;

        [Header("Police barricade (corridor levels)")]
        [Tooltip("Second line of defence: parked police cars staggered across the road. Needs the Police Car Prefab on the Level object.")]
        public bool policeBarricade = true;
        [Range(0.05f, 0.95f), Tooltip("Where along the road it sits, measured from the gate (0) toward the spawn (1).")]
        public float barricadeFraction = 0.3f;
        [Min(0.1f), Tooltip("Scale of each car. 0.8 makes a car roughly fill one 4-unit road tile.")] public float barricadeCarScale = 0.8f;
        [Range(0f, 60f), Tooltip("How far each car can be skewed from straight across the road.")] public float barricadeTiltDegrees = 25f;
        public bool barricadeStrobes = true;

        [Header("Rooftops")]
        [Min(0f), Tooltip("Low wall along roof edges that overlook the road (cover for soldiers). 0 = none.")]
        public float parapetHeight = 0.9f;
        [Min(0.1f)] public float parapetThickness = 0.6f;

        [Header("Lights")]
        [Tooltip("Master switch for the generated light rig.")] public bool addLights = true;
        [Min(0), Tooltip("A fire-glow light every N road-facing wall tiles (0 = none).")] public int fireLightSpacing = 3;
        [Min(0)] public int maxFireLights = 40;
        [Min(0), Tooltip("Steady cool/warm light spilling from lit office windows, every N tiles (0 = none).")] public int windowGlowSpacing = 4;
        [Min(0)] public int maxWindowGlow = 30;
        [Min(0), Tooltip("Blinking red warning beacon on rooftops, every N tiles (0 = none).")] public int beaconSpacing = 7;
        [Min(0)] public int maxBeacons = 16;
        [Min(0), Tooltip("Military floodlight over the rooftops every N road-facing tiles (0 = none).")] public int roofLightSpacing = 6;
        [Min(0)] public int maxRoofLights = 20;
        public bool roofFixtures = true;
        public bool strobes = true;
        public bool gateFloodlights = true;
        public bool searchlight = true;

        [Header("Base gate")]
        [Tooltip("Blast doors across the goal when the goal is on the map edge. Zombies pile up and attack it.")]
        public bool hasGate = true;
        [Min(1f)] public float gateHealth = 600f;
        [Range(0.3f, 1f), Tooltip("Door height as a fraction of wall height.")] public float gateHeightFraction = 0.85f;

        [Header("Camera")]
        [Range(30f, 90f)] public float cameraPitch = 62f;
        public float cameraStartDistance = 120f;
        public float cameraMinDistance = 40f;
        public float cameraMaxDistance = 200f;

#if UNITY_EDITOR
        /// <summary>Editor only: lets LevelGenerator refresh its preview when this asset is edited.</summary>
        public static event System.Action<LevelData> EditorChanged;
        void OnValidate() => EditorChanged?.Invoke(this);
#endif
    }
}
