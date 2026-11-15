using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>Plays all child particle systems on enable and returns itself to the pool after a lifetime.</summary>
    public class PooledVfx : MonoBehaviour
    {
        [SerializeField] float lifetime = 1f;

        ParticleSystem[] systems;
        PooledObject pooled;
        float timer;

        void Awake()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            pooled = GetComponent<PooledObject>();
        }

        void OnEnable()
        {
            timer = lifetime;
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Clear(false);
                systems[i].Play(false);
            }
        }

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            if (pooled != null) pooled.Release();
            else gameObject.SetActive(false);
        }

        /// <summary>Null-safe helper: spawns a pooled effect if a prefab is assigned.</summary>
        public static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;
            PoolManager pm = PoolManager.Instance;
            if (pm != null) pm.Spawn(prefab, position, rotation);
        }
    }
}
