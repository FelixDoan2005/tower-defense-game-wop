using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using ZombieTD.Maze;

// Namespace is CameraSystem (not "Camera") so it never shadows UnityEngine.Camera.
namespace ZombieTD.CameraSystem
{
    /// <summary>
    /// Steep top-down RTS camera. The rig's transform is the focus point on the ground; the camera
    /// orbits it at a fixed pitch. WASD/arrows + screen-edge pan, wheel zoom, middle-mouse drag, Q/E rotate.
    /// Uses unscaled time so it still works when the game is frozen. Weapons call Shake/ShakeAt.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public static CameraRig Instance { get; private set; }

        [SerializeField] Camera cam;
        [SerializeField, FormerlySerializedAs("maze"),
         Tooltip("Optional: clamps panning to the level and takes pitch/zoom from its LevelData.")]
        LevelGenerator level;

        [Header("View")]
        [SerializeField, Range(30f, 89f)] float pitch = 70f;
        [SerializeField] float yaw;
        [SerializeField, Range(20f, 90f)] float fieldOfView = 40f;

        [Header("Zoom")]
        [SerializeField] float minDistance = 25f;
        [SerializeField] float maxDistance = 160f;
        [SerializeField] float startDistance = 90f;
        [SerializeField, Range(0.01f, 0.5f)] float zoomStep = 0.15f;
        [SerializeField] float zoomSmoothing = 12f;

        [Header("Pan")]
        [SerializeField, Tooltip("World units per second, per unit of zoom distance.")] float panSpeed = 0.9f;
        [SerializeField] bool edgePan = true;
        [SerializeField] float edgeSize = 12f;
        [SerializeField] bool middleMouseDrag = true;
        [SerializeField] float boundsPadding = 10f;
        [SerializeField, Tooltip("Only allow panning left/right (and zoom). Rotation is disabled too.")] bool horizontalOnly = true;
        [SerializeField, Tooltip("Apply the horizontal-only lock to corridor levels only; other layouts pan freely.")] bool lockCorridorsOnly = true;
        [SerializeField, Tooltip("While locked, keep the near (bottom) building's base on the bottom edge of the screen at every zoom level.")]
        bool anchorBottomEdge = true;
        [SerializeField, Range(0f, 0.3f), Tooltip("Gap between the screen's bottom edge and the building, as a fraction of screen height.")]
        float bottomEdgeMargin = 0.02f;

        [Header("Rotate")]
        [SerializeField] bool allowRotate = true;
        [SerializeField] float rotateSpeed = 90f;

        [Header("Shake")]
        [SerializeField] float maxShakeOffset = 2f;
        [SerializeField] float maxShakeRoll = 2f;
        [SerializeField] float shakeFrequency = 22f;
        [SerializeField, Tooltip("Trauma lost per second.")] float traumaDecay = 1.6f;
        [SerializeField, Tooltip("ShakeAt fades to zero at this distance from the focus.")] float shakeFalloff = 80f;

        float distance, targetDistance, trauma;
        Bounds bounds;
        bool hasBounds;
        bool lockedHorizontal;

        void Awake()
        {
            Instance = this;
            if (cam == null) cam = GetComponentInChildren<Camera>();
            distance = targetDistance = Mathf.Clamp(startDistance, minDistance, maxDistance);
        }

        void Start()
        {
            cam.fieldOfView = fieldOfView;
            if (level != null)
            {
                LevelData d = level.Level;
                pitch = d.cameraPitch;
                bottomEdgeMargin = d.cameraBottomMargin; // per level, so each camera view can be framed on its own
                minDistance = d.cameraMinDistance;
                maxDistance = Mathf.Max(d.cameraMaxDistance, minDistance);
                distance = targetDistance = Mathf.Clamp(d.cameraStartDistance, minDistance, maxDistance);

                lockedHorizontal = horizontalOnly && (!lockCorridorsOnly || d.layout == LevelLayout.Corridor);
                if (lockedHorizontal) yaw = 0f; // keep the road running left-right on screen

                bounds = level.Grid.WorldBounds;
                hasBounds = true;
                transform.position = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z);
            }
            ApplyTransform(0f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Adds screen shake (0..1 trauma; effect scales with trauma squared).</summary>
        public static void Shake(float amount)
        {
            if (Instance != null) Instance.trauma = Mathf.Min(1f, Instance.trauma + amount);
        }

        /// <summary>Shake that weakens with distance from where the camera is looking.</summary>
        public static void ShakeAt(Vector3 worldPos, float amount)
        {
            if (Instance == null) return;
            Vector3 d = worldPos - Instance.transform.position;
            d.y = 0f;
            float k = 1f - Mathf.Clamp01(d.magnitude / Instance.shakeFalloff);
            if (k > 0f) Shake(amount * k);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            HandleInput(dt);
            ApplyTransform(dt);
        }

        void HandleInput(float dt)
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Vector2 move = Vector2.zero;

            if (kb != null)
            {
                if (lockedHorizontal)
                {
                    // Locked view: A and D only. No arrows, no zoom, no mouse panning, no rotation.
                    if (kb.dKey.isPressed) move.x += 1f;
                    if (kb.aKey.isPressed) move.x -= 1f;
                }
                else
                {
                    if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                    if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                    if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                    if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                    if (allowRotate)
                    {
                        if (kb.qKey.isPressed) yaw += rotateSpeed * dt;
                        if (kb.eKey.isPressed) yaw -= rotateSpeed * dt;
                    }
                }
            }

            if (mouse != null && !lockedHorizontal)
            {
                Vector2 mp = mouse.position.ReadValue();
                bool inside = mp.x >= 0f && mp.y >= 0f && mp.x <= Screen.width && mp.y <= Screen.height;

                if (edgePan && inside && Application.isFocused && !mouse.middleButton.isPressed)
                {
                    if (mp.x < edgeSize) move.x -= 1f;
                    else if (mp.x > Screen.width - edgeSize) move.x += 1f;
                    if (mp.y < edgeSize) move.y -= 1f;
                    else if (mp.y > Screen.height - edgeSize) move.y += 1f;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (inside && scroll != 0f)
                    targetDistance = Mathf.Clamp(targetDistance * (1f - Mathf.Sign(scroll) * zoomStep), minDistance, maxDistance);

                if (middleMouseDrag && mouse.middleButton.isPressed)
                {
                    // Convert pixel motion to ground motion so the ground "sticks" to the cursor.
                    Vector2 delta = mouse.delta.ReadValue();
                    float worldPerPixel = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Screen.height;
                    Pan(-delta.x * worldPerPixel, -delta.y * worldPerPixel / Mathf.Sin(pitch * Mathf.Deg2Rad));
                }
            }

            if (move.sqrMagnitude > 1f) move.Normalize();
            float speed = panSpeed * distance * dt;
            if (move != Vector2.zero) Pan(move.x * speed, move.y * speed);
        }

        void Pan(float right, float forward)
        {
            if (lockedHorizontal) forward = 0f; // W/S, top/bottom edge and vertical drag do nothing
            Vector3 p = transform.position + Quaternion.Euler(0f, yaw, 0f) * new Vector3(right, 0f, forward);
            if (hasBounds)
            {
                p.x = Mathf.Clamp(p.x, bounds.min.x - boundsPadding, bounds.max.x + boundsPadding);
                p.z = Mathf.Clamp(p.z, bounds.min.z - boundsPadding, bounds.max.z + boundsPadding);
            }
            transform.position = p;
        }

        // Solves for the focus Z that puts the ground point at the map's near edge on the bottom of the screen.
        // Camera at pitch p looks along (0,-sin p, cos p) (yaw is 0 while locked). With dz = edgeZ - focusZ,
        // the point's view-space ratio y/z = dz*sin p / (dz*cos p + distance) must equal k = (2m-1)*tan(fov/2).
        void AnchorBottomEdge()
        {
            float p = pitch * Mathf.Deg2Rad;
            float k = (2f * bottomEdgeMargin - 1f) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float denom = Mathf.Sin(p) - k * Mathf.Cos(p);
            if (Mathf.Abs(denom) < 0.0001f) return;
            float dz = k * distance / denom;
            Vector3 pos = transform.position;
            pos.z = bounds.min.z - dz;
            transform.position = pos;
        }

        void ApplyTransform(float dt)
        {
            distance = dt > 0f ? Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-zoomSmoothing * dt)) : targetDistance;

            if (lockedHorizontal && anchorBottomEdge && hasBounds) AnchorBottomEdge();

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 pos = transform.position - rot * Vector3.forward * distance;
            Quaternion roll = Quaternion.identity;

            if (trauma > 0f)
            {
                float s = trauma * trauma;
                float t = Time.unscaledTime * shakeFrequency;
                var offset = new Vector3(Mathf.PerlinNoise(t, 0.1f) * 2f - 1f, Mathf.PerlinNoise(0.7f, t) * 2f - 1f, 0f);
                pos += rot * offset * (maxShakeOffset * s);
                roll = Quaternion.Euler(0f, 0f, (Mathf.PerlinNoise(t + 13f, t) * 2f - 1f) * maxShakeRoll * s);
                trauma = Mathf.Max(0f, trauma - traumaDecay * dt);
            }

            cam.transform.SetPositionAndRotation(pos, rot * roll);
        }
    }
}
