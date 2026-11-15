using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>Ordered list of levels. Index 0 is the first level played.</summary>
    [CreateAssetMenu(menuName = "ZombieTD/Level Catalog", fileName = "LevelCatalog")]
    public class LevelCatalog : ScriptableObject
    {
        public LevelData[] levels = new LevelData[0];

        public int Count => levels != null ? levels.Length : 0;
        public LevelData Get(int index) => Count == 0 ? null : levels[Mathf.Clamp(index, 0, Count - 1)];
    }
}
