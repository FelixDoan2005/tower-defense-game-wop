using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZombieTD.Core
{
    /// <summary>Generic component pool. Instantiates from a source only when the stack is empty.</summary>
    public class ObjectPool<T> where T : Component
    {
        readonly T source;
        readonly Transform parent;
        readonly Action<T> onCreated;
        readonly Stack<T> free;

        public int CountInactive => free.Count;

        public ObjectPool(T source, Transform parent, Action<T> onCreated = null)
        {
            this.source = source;
            this.parent = parent;
            this.onCreated = onCreated;
            free = new Stack<T>(64);
        }

        /// <summary>Creates inactive instances up front.</summary>
        public void Prewarm(int count)
        {
            for (int i = 0; i < count; i++)
            {
                T item = Create();
                item.gameObject.SetActive(false);
                free.Push(item);
            }
        }

        T Create()
        {
            T item = Object.Instantiate(source, parent);
            onCreated?.Invoke(item);
            return item;
        }

        /// <summary>Takes an instance from the pool (or creates one) and activates it.</summary>
        public T Get(Vector3 position, Quaternion rotation)
        {
            T item = free.Count > 0 ? free.Pop() : Create();
            item.transform.SetPositionAndRotation(position, rotation);
            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>Deactivates the instance and returns it to the pool.</summary>
        public void Release(T item)
        {
            item.gameObject.SetActive(false);
            free.Push(item);
        }
    }
}
