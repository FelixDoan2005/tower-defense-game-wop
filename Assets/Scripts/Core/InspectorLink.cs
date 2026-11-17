using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>
    /// A Hierarchy entry that shows another component's settings inline in its own Inspector, so they can be edited
    /// from a tidy folder in the Hierarchy (Zombies > Types > Zombie, Zombies > Waves) without hunting for the prefab
    /// or the scene object. The settings stay on the original component; this only displays and edits them.
    /// </summary>
    public class InspectorLink : MonoBehaviour
    {
        [SerializeField, Tooltip("The component whose settings are shown here (e.g. the Zombie on the zombie prefab, or the Wave Spawner).")]
        Component target;
        [SerializeField, TextArea(1, 3)] string note;

        public Component Target => target;
        public string Note => note;
    }
}
