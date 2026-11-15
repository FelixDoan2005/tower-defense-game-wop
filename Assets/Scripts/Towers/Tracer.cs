using UnityEngine;
using ZombieTD.Core;

namespace ZombieTD.Towers
{
    /// <summary>Pooled bullet tracer: a 2-point line that thins out and returns to the pool.</summary>
    [RequireComponent(typeof(LineRenderer))]
    public class Tracer : MonoBehaviour
    {
        LineRenderer line;
        PooledObject pooled;
        float baseWidth, life, age;

        void Awake()
        {
            line = GetComponent<LineRenderer>();
            pooled = GetComponent<PooledObject>();
            baseWidth = line.widthMultiplier;
            line.positionCount = 2;
            line.useWorldSpace = true;
        }

        public void Play(Vector3 from, Vector3 to, float lifetime)
        {
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            life = Mathf.Max(0.01f, lifetime);
            age = 0f;
            line.widthMultiplier = baseWidth;
        }

        void Update()
        {
            age += Time.deltaTime;
            if (age >= life)
            {
                if (pooled != null) pooled.Release();
                else gameObject.SetActive(false);
                return;
            }
            line.widthMultiplier = baseWidth * (1f - age / life);
        }
    }
}
