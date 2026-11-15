using UnityEngine;

namespace ZombieTD.Maze
{
    /// <summary>A buildable spot on top of a wall tile. Holds at most one occupant (e.g. a tower).</summary>
    public class WallSlot : MonoBehaviour
    {
        [SerializeField] Vector2Int cell;

        public Vector2Int Cell => cell;
        public int CellIndex { get; private set; }
        public GameObject Occupant { get; set; }
        public bool IsOccupied => Occupant != null;
        /// <summary>Where a tower's root should sit (top center of the wall).</summary>
        public Vector3 MountPoint => transform.position;

        public void Init(int x, int z, int index)
        {
            cell = new Vector2Int(x, z);
            CellIndex = index;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = IsOccupied ? Color.red : Color.cyan;
            Gizmos.DrawWireCube(transform.position, new Vector3(1.5f, 0.1f, 1.5f));
        }
    }
}
