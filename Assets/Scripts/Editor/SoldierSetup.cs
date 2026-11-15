using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Maze;
using ZombieTD.Towers;
using ZombieTD.UI;
using Object = UnityEngine.Object;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Builds the rifleman from the imported Low Poly Soldiers and WarFX packs (animator, prefab, muzzle flash,
    /// bullet impact, tracer, weapon data) and adds the drag-and-drop placement system to the scene.
    /// Runs automatically when a LevelGenerator loads; also available as Tools > Units > Set Up Rifleman and Placement.
    /// Assets are only created when missing, so your tweaks to them are kept.
    /// </summary>
    public static class SoldierSetup
    {
        const string ModelPath = "Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX";
        const string IdlePath = "Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX";
        const string ShootPath = "Assets/LowPolySoldiers_demo/animation/demo_combat_shoot.FBX";
        const string MuzzleSource = "Assets/JMO Assets/WarFX/_Effects/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE1.prefab";
        const string HitSource = "Assets/JMO Assets/WarFX/_Effects/Bullet Impacts/WFX_BImpact SoftBody.prefab";

        const string Dir = "Assets/Prefabs/Soldier";
        const string UnitsDir = "Assets/Settings/Units";
        const float UnitHeight = 1.0f; // soldier height in world units: tiny, about zombie size (a zombie is 0.9)
        const int SetupVersion = 4;    // bump to re-apply the sizing/balance below to existing assets once
        const float MuzzleScale = 0.5f, HitScale = 0.3f, TracerWidth = 0.05f;
        const float MuzzleHeight = 0.65f, MuzzleForward = 0.6f; // fallback rifle tip for a 1.0-tall soldier (no gun bone found)
        const float BarrelReach = 0.45f;                        // from the hand to the end of the rifle barrel

        [MenuItem("Tools/Units/Set Up Rifleman and Placement")]
        static void Menu()
        {
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (level == null) { Debug.LogError("[ZombieTD] No LevelGenerator in the open scene."); return; }
            WeaponData rifleman = EnsureAssets();
            if (rifleman == null) return;
            if (!EnsureSystem(level)) Debug.Log("[ZombieTD] The scene already has a UnitPlacer; nothing to add.");
        }

        /// <summary>Adds the UnitPlacer + UnitTray to the open scene if missing. Returns true if it added them.</summary>
        public static bool EnsureSystem(LevelGenerator level)
        {
            MigrateExisting(); // older rifleman assets get updated once, even if the scene already has the system
            if (Object.FindAnyObjectByType<UnitPlacer>() != null) return false;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return false; // soldier pack not imported

            WeaponData rifleman = EnsureAssets();
            if (rifleman == null) return false;

            var go = new GameObject("UnitSystem");
            var placer = go.AddComponent<UnitPlacer>();
            var so = new SerializedObject(placer);
            so.FindProperty("cam").objectReferenceValue = Camera.main;
            so.FindProperty("level").objectReferenceValue = level;
            SerializedProperty list = so.FindProperty("buildable");
            list.arraySize = 1;
            list.GetArrayElementAtIndex(0).objectReferenceValue = rifleman;
            so.ApplyModifiedPropertiesWithoutUndo();

            var tray = go.AddComponent<UnitTray>();
            var tso = new SerializedObject(tray);
            tso.FindProperty("placer").objectReferenceValue = placer;
            tso.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[ZombieTD] Unit placement added to the scene (UnitSystem). Save the scene to keep it.");
            return true;
        }

        // ---------------------------------------------------------------- assets

        /// <summary>Creates any missing rifleman assets and returns the WeaponData (null if the packs are missing).</summary>
        public static WeaponData EnsureAssets()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null)
            {
                Debug.LogError($"[ZombieTD] Soldier model not found at {ModelPath}");
                return null;
            }

            EnsureFolder("Assets/Prefabs", "Soldier");
            EnsureFolder("Assets/Settings", "Units");

            AnimatorController controller = EnsureController();
            var muzzle = EnsureEffect(MuzzleSource, $"{Dir}/Soldier_MuzzleFlash.prefab", MuzzleScale);
            var hit = EnsureEffect(HitSource, $"{Dir}/Soldier_BulletHit.prefab", HitScale);
            Tracer tracer = EnsureTracer();
            Tower prefab = EnsureSoldierPrefab(controller);

            string path = $"{UnitsDir}/Rifleman.asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<WeaponData>();
                data.displayName = "Rifleman";
                data.cost = 35;               // per soldier; ten fit on one pad
                data.slotCapacity = 10;
                data.formationSpacing = 0.6f;
                data.range = 24f;
                data.fireRate = 3.5f;
                data.damage = 4f;
                data.turnSpeed = 540f;
                data.aimTolerance = 8f;
                data.knockback = 0.15f;
                data.defaultTargetMode = TargetMode.First;
                data.shakeOnFire = 0.0015f;
                data.setupVersion = SetupVersion;
                AssetDatabase.CreateAsset(data, path);
            }
            else if (data.setupVersion < SetupVersion)
            {
                MigrateToTinySoldiers(data);
            }
            // Fill in references if they're empty (never overwrites ones you changed).
            if (data.unitPrefab == null) data.unitPrefab = prefab;
            if (data.muzzleVfx == null) data.muzzleVfx = muzzle;
            if (data.hitVfx == null) data.hitVfx = hit;
            if (data.tracerPrefab == null) data.tracerPrefab = tracer;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        static void MigrateExisting()
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>($"{UnitsDir}/Rifleman.asset");
            if (data == null || data.setupVersion >= SetupVersion) return;
            MigrateToTinySoldiers(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
        }

        // One-time update of assets created at the old (big, single) soldier size to tiny squad soldiers.
        static void MigrateToTinySoldiers(WeaponData data)
        {
            data.cost = 35;
            data.damage = 4f;
            data.knockback = 0.15f;
            data.shakeOnFire = 0.0015f;
            data.slotCapacity = 10;
            data.formationSpacing = 0.6f;
            data.setupVersion = SetupVersion;

            // Soldier: rescale the model and move the rifle tip.
            string soldierPath = $"{Dir}/Rifleman.prefab";
            if (File.Exists(soldierPath))
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(soldierPath);
                Transform visual = contents.transform.Find("Visual");
                Transform model = visual != null ? visual.Find("Model") : null;
                if (model != null)
                {
                    float height = MeasureRenderers(model.gameObject).size.y;
                    if (height > 0.01f) visual.localScale *= UnitHeight / height;
                }
                var tower = contents.GetComponent<SoldierTower>();
                if (tower != null && model != null)
                {
                    var so = new SerializedObject(tower);
                    so.FindProperty("muzzleHeight").floatValue = MuzzleHeight;
                    so.FindProperty("muzzleForward").floatValue = MuzzleForward;
                    so.FindProperty("hitScatter").floatValue = 0.08f;
                    so.FindProperty("barrelReach").floatValue = BarrelReach;
                    AssignGunAnchor(so, model);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(contents, soldierPath);
                PrefabUtility.UnloadPrefabContents(contents);
            }

            RescaleRoot($"{Dir}/Soldier_MuzzleFlash.prefab", MuzzleScale);
            RescaleRoot($"{Dir}/Soldier_BulletHit.prefab", HitScale);

            string tracerPath = $"{Dir}/Soldier_Tracer.prefab";
            if (File.Exists(tracerPath))
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(tracerPath);
                LineRenderer lr = contents.GetComponent<LineRenderer>();
                if (lr != null) lr.widthMultiplier = TracerWidth;
                PrefabUtility.SaveAsPrefabAsset(contents, tracerPath);
                PrefabUtility.UnloadPrefabContents(contents);
            }
            Debug.Log("[ZombieTD] Rifleman updated to tiny squad soldiers (35$ each, 10 per pad).");
        }

        static void RescaleRoot(string prefabPath, float scale)
        {
            if (!File.Exists(prefabPath)) return;
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            contents.transform.localScale = Vector3.one * scale;
            PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
        }

        // Idle (loops) and Shoot (plays once on every shot, then back to idle).
        static AnimatorController EnsureController()
        {
            string path = $"{Dir}/SoldierAnimator.controller";
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (existing != null) return existing;

            AnimationClip idle = LoadClip(IdlePath), shoot = LoadClip(ShootPath);
            if (idle == null || shoot == null) Debug.LogWarning("[ZombieTD] Soldier animation clips not found; the animator will be empty.");

            AnimatorController ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
            AnimatorState idleState = sm.AddState("Idle");
            idleState.motion = idle;
            sm.defaultState = idleState;
            AnimatorState shootState = sm.AddState("Shoot");
            shootState.motion = shoot;
            AnimatorStateTransition back = shootState.AddTransition(idleState);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.08f;
            EditorUtility.SetDirty(ctrl);
            return ctrl;
        }

        static AnimationClip LoadClip(string fbxPath) =>
            AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        // Copy of a WarFX effect that is safe to pool: its self-destruct scripts and "destroy when stopped" are removed.
        static GameObject EnsureEffect(string sourcePath, string outPath, float scale)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(outPath);
            if (existing != null) return existing;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (src == null) { Debug.LogWarning($"[ZombieTD] WarFX effect not found: {sourcePath}"); return null; }

            GameObject copy = Object.Instantiate(src); // plain clone (not a prefab instance) so components can be removed
            copy.name = Path.GetFileNameWithoutExtension(outPath);
            foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "CFX_AutoDestructShuriken") Object.DestroyImmediate(mb);

            float life = 0.5f;
            foreach (ParticleSystem ps in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule m = ps.main;
                m.stopAction = ParticleSystemStopAction.None;
                life = Mathf.Max(life, m.duration + m.startLifetime.constantMax);
            }

            var vfx = copy.AddComponent<PooledVfx>();
            var so = new SerializedObject(vfx);
            so.FindProperty("lifetime").floatValue = Mathf.Clamp(life, 0.5f, 3f);
            so.ApplyModifiedPropertiesWithoutUndo();

            copy.transform.localScale = Vector3.one * scale;
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, outPath);
            Object.DestroyImmediate(copy);
            return saved;
        }

        static Tracer EnsureTracer()
        {
            string path = $"{Dir}/Soldier_Tracer.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<Tracer>();

            string matPath = "Assets/Materials/Tracer.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", new Color(3f, 2.4f, 1f)); // HDR so it blooms
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var go = new GameObject("Soldier_Tracer");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = TracerWidth;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            go.AddComponent<Tracer>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved.GetComponent<Tracer>();
        }

        // Root "Rifleman" (SoldierTower) > Visual (scaled to UnitHeight, feet at y=0) > the FBX model with the animator.
        static Tower EnsureSoldierPrefab(AnimatorController controller)
        {
            string path = $"{Dir}/Rifleman.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<Tower>();

            var root = new GameObject("Rifleman");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, visual);
            model.name = "Model";

            Animator anim = model.GetComponent<Animator>();
            if (anim == null) anim = model.AddComponent<Animator>();
            anim.runtimeAnimatorController = controller;
            anim.applyRootMotion = false;

            // Size it and stand it on the ground.
            Bounds b = MeasureRenderers(model);
            if (b.size.y > 0.01f)
            {
                model.transform.localPosition = new Vector3(0f, -b.min.y, 0f);
                visual.localScale = Vector3.one * (UnitHeight / b.size.y);
            }
            else Debug.LogWarning("[ZombieTD] Couldn't measure the soldier model; scale the Visual child by hand.");

            var tower = root.AddComponent<SoldierTower>();
            var so = new SerializedObject(tower);
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("muzzleHeight").floatValue = MuzzleHeight;
            so.FindProperty("muzzleForward").floatValue = MuzzleForward;
            so.FindProperty("hitScatter").floatValue = 0.08f;
            so.FindProperty("barrelReach").floatValue = BarrelReach;
            AssignGunAnchor(so, model.transform);
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<Tower>();
        }

        // Finds the weapon on the soldier's skeleton by name and wires it up as the point shots and the muzzle flash use.
        // If no bone looks like a weapon, the bone names are logged so the right one can be picked by hand.
        static void AssignGunAnchor(SerializedObject so, Transform model)
        {
            Transform best = null;
            int bestScore = 0;
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                string n = t.name.ToLowerInvariant();
                int score = n.Contains("muzzle") || n.Contains("barrel") ? 4
                          : n.Contains("weapon") || n.Contains("gun") || n.Contains("rifle") ? 3
                          : n.EndsWith("r hand") || n.EndsWith("right hand") || n.EndsWith("hand_r") ? 2 // the rifle is held in the right hand
                          : 0;
                if (score > bestScore) { bestScore = score; best = t; }
            }

            if (best != null)
            {
                so.FindProperty("muzzleAnchor").objectReferenceValue = best;
                Debug.Log($"[ZombieTD] Rifleman gun point: '{best.name}'. (Change 'Muzzle Anchor' on the prefab's SoldierTower to use another bone.)");
            }
            else
            {
                so.FindProperty("muzzleAnchor").objectReferenceValue = null;
                Debug.LogWarning("[ZombieTD] No weapon bone found on the soldier; the muzzle flash uses a fixed point in front of him. " +
                                 "Bones: " + string.Join(", ", all.Select(t => t.name).Take(60)) +
                                 "  -> assign one to 'Muzzle Anchor' on the Rifleman prefab's SoldierTower.");
            }
        }

        static Bounds MeasureRenderers(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds();
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
