using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Enemies;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Builds the "Zombies" folder in the Hierarchy once:
    ///   Zombies > Types > Zombie   (the zombie's health, speed, size-related and gate-damage settings)
    ///   Zombies > Waves            (horde size, spawn rate and wave growth)
    /// Each entry shows the real settings inline. Delete the folder and it stays deleted;
    /// Tools > Zombies > Rebuild Hierarchy Folder brings it back.
    /// </summary>
    static class ZombieHierarchy
    {
        const string RootName = "Zombies";

        [MenuItem("Tools/Zombies/Rebuild Hierarchy Folder")]
        static void Rebuild()
        {
            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            if (!Build()) Debug.LogWarning("[ZombieTD] Couldn't build the Zombies folder: no WaveSpawner with a zombie prefab in the open scene.");
        }

        /// <summary>First load only. Returns true if it built the folder.</summary>
        public static bool EnsureOnce()
        {
            string key = "ZombieTD.ZombieFolderBuilt." + Application.dataPath;
            if (EditorPrefs.GetBool(key, false)) return false;
            if (!Build()) return false; // not ready yet (e.g. no spawner in this scene); try again next load
            EditorPrefs.SetBool(key, true);
            return true;
        }

        static bool Build()
        {
            if (GameObject.Find(RootName) != null) return false;

            var spawner = Object.FindAnyObjectByType<WaveSpawner>();
            if (spawner == null) return false;
            var prefab = new SerializedObject(spawner).FindProperty("zombiePrefab").objectReferenceValue as Zombie;
            if (prefab == null) return false;

            var root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Zombies folder");
            var types = new GameObject("Types").transform;
            types.SetParent(root, false);

            Link(types, prefab.gameObject.name, prefab, "Select to edit this zombie: health, speed, damage to the gate, crowding, bob. " +
                "(Its size is the prefab's scale.)");
            Link(root, "Waves", spawner, "Horde size, spawn rate and how fast waves grow. 'Horde Scale' multiplies the number of zombies.");

            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            Debug.Log("[ZombieTD] Zombie folder ready in the Hierarchy: Zombies > Types, Zombies > Waves (save the scene to keep it).");
            return true;
        }

        static void Link(Transform parent, string name, Component target, string note)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var link = go.AddComponent<InspectorLink>();
            var so = new SerializedObject(link);
            so.FindProperty("target").objectReferenceValue = target;
            so.FindProperty("note").stringValue = note;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
