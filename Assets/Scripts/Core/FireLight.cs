using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>
    /// Flickering point light for fires. Drop the FireLight prefab next to a fire effect.
    /// Each instance gets its own random phase so neighbouring fires don't pulse in sync.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class FireLight : MonoBehaviour
    {
        [SerializeField] float baseIntensity = 6f;
        [SerializeField, Range(0f, 1f)] float flicker = 0.35f;
        [SerializeField] float speed = 9f;
        [SerializeField, Tooltip("Small position jitter makes shadows dance.")] float wobble = 0.15f;

        Light lightSource;
        Vector3 origin;
        float seed;

        void Awake()
        {
            lightSource = GetComponent<Light>();
            origin = transform.localPosition;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float t = Time.time * speed + seed;
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.3f, seed + 5f) * 0.3f;
            lightSource.intensity = baseIntensity * (1f - flicker + flicker * 2f * n);
            if (wobble > 0f)
                transform.localPosition = origin + new Vector3(Mathf.PerlinNoise(t, 1f) - 0.5f, 0f, Mathf.PerlinNoise(2f, t) - 0.5f) * wobble;
        }
    }
}
