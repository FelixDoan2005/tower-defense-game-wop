using UnityEngine;

namespace ZombieTD.Core
{
    /// <summary>
    /// Emergency-vehicle style strobe: alternates between two colours with sharp flashes.
    /// Give a red light and a blue light opposite phase offsets for a police/ambulance look.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class LightStrobe : MonoBehaviour
    {
        [SerializeField] Color colorA = new Color(1f, 0.08f, 0.05f);
        [SerializeField] Color colorB = new Color(0.1f, 0.25f, 1f);
        [SerializeField] float rate = 2.2f;
        [SerializeField] float peakIntensity = 9f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of each half-cycle the light stays on.")] float onFraction = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("Shifts this strobe within the cycle (0.5 = opposite).")] float phase;

        Light lightSource;

        void Awake() => lightSource = GetComponent<Light>();

        /// <summary>Called by the level builder to set up a strobe without touching serialized fields.</summary>
        public void Configure(Color a, Color b, float cyclePhase, float intensity, float flashesPerSecond = -1f)
        {
            if (flashesPerSecond > 0f) rate = flashesPerSecond;
            colorA = a;
            colorB = b;
            phase = cyclePhase;
            peakIntensity = intensity;
        }

        void Update()
        {
            float p = Time.time * rate + phase;
            int half = Mathf.FloorToInt(p * 2f) & 1;
            float frac = Mathf.Repeat(p * 2f, 1f);
            lightSource.color = half == 0 ? colorA : colorB;
            // Sharp double-pulse at the start of each half, then nearly dark.
            float pulse = frac < onFraction ? 1f - frac / onFraction * 0.3f : 0.03f;
            lightSource.intensity = peakIntensity * pulse;
        }
    }
}
