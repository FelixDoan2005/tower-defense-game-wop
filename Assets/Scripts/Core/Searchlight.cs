using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>
    /// Helicopter-style searchlight: a spot light high above that sweeps its beam back and forth
    /// between two ground points (in this object's parent space), with a little wander.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class Searchlight : MonoBehaviour
    {
        [SerializeField] Vector3 sweepFrom;
        [SerializeField] Vector3 sweepTo;
        [SerializeField, Tooltip("Sweeps per second (0.05 = one pass every 20 s).")] float sweepSpeed = 0.07f;
        [SerializeField, Tooltip("Sideways wander of the beam, in world units.")] float wander = 6f;
        [SerializeField] float wanderSpeed = 0.9f;

        Transform parent;
        float seed;

        void Awake()
        {
            parent = transform.parent;
            seed = Random.value * 10f;
        }

        public void Configure(Vector3 from, Vector3 to)
        {
            sweepFrom = from;
            sweepTo = to;
        }

        void Update()
        {
            float s = 0.5f + 0.5f * Mathf.Sin(Time.time * sweepSpeed * Mathf.PI * 2f + seed);
            Vector3 target = Vector3.Lerp(sweepFrom, sweepTo, s);

            // Wander perpendicular to the sweep direction.
            Vector3 along = (sweepTo - sweepFrom).normalized;
            Vector3 side = new Vector3(-along.z, 0f, along.x);
            target += side * (Mathf.Sin(Time.time * wanderSpeed + seed * 3f) * wander);

            Vector3 world = parent != null ? parent.TransformPoint(target) : target;
            Vector3 dir = world - transform.position;
            if (dir.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(dir);
        }
    }
}
