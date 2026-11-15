using System.IO;
using UnityEditor;
using UnityEngine;
using ZombieTD.Enemies;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Makes the zombie prefab a tiny horde member: a half-height capsule with no arms (one renderer per zombie,
    /// which matters with thousands on screen) and stats scaled to match. Runs automatically; changes the prefab once.
    /// Edit the prefab afterwards and your values are kept.
    /// </summary>
    static class ZombieSetup
    {
        const string PrefabPath = "Assets/Prefabs/Zombie.prefab";
        const string DeathVfxPath = "Assets/VFX/ZombieDeath.prefab";

        // A zombie is about half a soldier: 0.44 tall.
        static readonly Vector3 BodyScale = new Vector3(0.2f, 0.22f, 0.2f);
        const float GroundOffset = 0.22f; // capsule half-height

        public static void EnsureTiny()
        {
            if (!File.Exists(PrefabPath)) return;

            GameObject contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                // Already done: the body is at (or below) the target size.
                if (contents.transform.localScale.y <= BodyScale.y + 0.001f) return;

                contents.transform.localScale = BodyScale;
                for (int i = contents.transform.childCount - 1; i >= 0; i--)
                    if (contents.transform.GetChild(i).name == "Arm") Object.DestroyImmediate(contents.transform.GetChild(i).gameObject);

                var zombie = contents.GetComponent<Zombie>();
                if (zombie != null)
                {
                    var so = new SerializedObject(zombie);
                    Set(so, "baseHealth", 12f);         // tiny and many: each one dies fast
                    Set(so, "baseSpeed", 2f);
                    Set(so, "attackDps", 0.12f);        // hundreds at the gate at once, so each hits lightly
                    Set(so, "bodyRadius", 0.1f);
                    Set(so, "wobbleAmount", 0.25f);
                    Set(so, "separationRadius", 0.22f);
                    Set(so, "groundOffset", GroundOffset);
                    Set(so, "bobAmount", 0.02f);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                Debug.Log("[ZombieTD] Zombie prefab shrunk to a tiny horde member (0.44 tall, no arms).");
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }

            ShrinkDeathEffect();
        }

        static void ShrinkDeathEffect()
        {
            if (!File.Exists(DeathVfxPath)) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(DeathVfxPath);
            contents.transform.localScale = Vector3.one * 0.4f;
            PrefabUtility.SaveAsPrefabAsset(contents, DeathVfxPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        static void Set(SerializedObject so, string field, float value)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p != null) p.floatValue = value;
            else Debug.LogWarning($"[ZombieTD] Zombie field '{field}' not found.");
        }
    }
}
