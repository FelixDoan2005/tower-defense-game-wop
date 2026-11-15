using System.Collections.Generic;
using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>Tag on every pooled instance so it knows which pool to return to.</summary>
    public class PooledObject : MonoBehaviour
    {
        internal ObjectPool<PooledObject> Pool;

        /// <summary>Returns this object to its pool (or destroys it if it was never pooled).</summary>
        public void Release()
        {
            if (Pool != null) Pool.Release(this);
            else Destroy(gameObject);
        }
    }

    /// <summary>
    /// Central pool registry: one pool per prefab, created on first use (or via Prewarm).
    /// Gameplay code calls Spawn/Despawn instead of Instantiate/Destroy.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PoolManager : MonoBehaviour
    {
        public static PoolManager Instance { get; private set; }

        readonly Dictionary<GameObject, ObjectPool<PooledObject>> pools = new Dictionary<GameObject, ObjectPool<PooledObject>>(32);
        Transform templates; // inactive holder so template clones never run Awake/OnEnable

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            var holder = new GameObject("Templates");
            holder.SetActive(false);
            holder.transform.SetParent(transform, false);
            templates = holder.transform;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Creates a pool and fills it ahead of time so there are no hitches mid-wave.</summary>
        public void Prewarm(GameObject prefab, int count)
        {
            if (prefab == null || pools.ContainsKey(prefab)) return;
            CreatePool(prefab, count);
        }

        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!pools.TryGetValue(prefab, out ObjectPool<PooledObject> pool)) pool = CreatePool(prefab, 0);
            return pool.Get(position, rotation).gameObject;
        }

        /// <summary>Spawns a pooled instance and returns the requested component on it.</summary>
        public T Spawn<T>(T prefab, Vector3 position, Quaternion rotation) where T : Component
        {
            return Spawn(prefab.gameObject, position, rotation).GetComponent<T>();
        }

        /// <summary>Returns an instance to its pool.</summary>
        public static void Despawn(GameObject instance)
        {
            if (instance == null) return;
            PooledObject tag = instance.GetComponent<PooledObject>();
            if (tag != null) tag.Release();
            else Object.Destroy(instance);
        }

        ObjectPool<PooledObject> CreatePool(GameObject prefab, int prewarm)
        {
            Transform root = new GameObject("Pool_" + prefab.name).transform;
            root.SetParent(transform, false);

            // Inactive template clone that carries the PooledObject tag; pool instances clone this.
            // Created under the inactive holder, then deactivated itself so its clones start inactive.
            GameObject template = Instantiate(prefab, templates);
            template.name = prefab.name + " (Template)";
            template.SetActive(false);
            PooledObject tag = template.GetComponent<PooledObject>();
            if (tag == null) tag = template.AddComponent<PooledObject>();

            ObjectPool<PooledObject> pool = null;
            pool = new ObjectPool<PooledObject>(tag, root, item => item.Pool = pool);
            pools.Add(prefab, pool);
            pool.Prewarm(prewarm);
            return pool;
        }
    }
}
