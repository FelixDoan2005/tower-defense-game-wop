using System.Collections.Generic;
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

        internal const string Dir = "Assets/Prefabs/Soldier";
        internal const string UnitsDir = "Assets/Settings/Units";
        internal const float UnitHeight = 1.0f; // soldier height in world units: tiny, about zombie size (a zombie is 0.9)
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
            EnsureReloadDefaults();
            SwatSetup.EnsureUnits(); // SWAT rifleman, sniper, shotgunner and cop, once their pack is imported
            ShieldWallPalette.EnsureWallUnit(); // the Shotgun Wall (five shotgunners with shields), built from the shotgunner

            var existingPlacer = Object.FindAnyObjectByType<UnitPlacer>();
            if (existingPlacer != null)
            {
                AddMissingUnits(existingPlacer); // e.g. the machine gunner, once its pack is imported
                EnsureRoster(existingPlacer);
                return false;
            }
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

            AddMissingUnits(placer);
            EnsureRoster(placer);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log("[ZombieTD] Unit placement added to the scene (UnitSystem). Save the scene to keep it.");
            return true;
        }

        // Starting ammo for each unit, applied once (never overwrites values you've changed since).
        //   Rifleman:       20 rounds, 2.5 s reload  -> fires about 5.7 s, reloads 2.5 s
        //   Machine Gunner: 150 rounds, 4 s reload   -> fires 5 s, reloads 4 s (a big drum, a slow change)
        static void EnsureReloadDefaults()
        {
            ApplyReload("Rifleman", 20, 2.5f);
            ApplyReload("MachineGunner", 150, 4f);
        }

        static void ApplyReload(string assetName, int magazine, float reload)
        {
            var d = AssetDatabase.LoadAssetAtPath<WeaponData>($"{UnitsDir}/{assetName}.asset");
            if (d == null || d.reloadVersion >= 1) return;
            d.magazineSize = magazine;
            d.reloadTime = reload;
            d.reloadVersion = 1;
            EditorUtility.SetDirty(d);
            AssetDatabase.SaveAssets();
        }

        // The "Units" folder in the Hierarchy: Units > Soldiers > one object per unit (with its stats shown inline),
        // plus an empty Units > Turrets for later. Each unit is added once, so one you delete stays deleted
        // (untick "In Tray" to hide a unit instead). The tray follows this folder's order.
        static void EnsureRoster(UnitPlacer placer)
        {
            var units = new List<WeaponData>();
            foreach (string file in new[] { "Rifleman", "MachineGunner", "SwatRifleman", "Cop", "Shotgunner", "Sniper", "ShotgunWall" })
            {
                var d = AssetDatabase.LoadAssetAtPath<WeaponData>($"{UnitsDir}/{file}.asset");
                if (d != null) units.Add(d);
            }
            if (units.Count == 0) return;

            var roster = Object.FindAnyObjectByType<UnitRoster>();
            bool changed = false;

            // Create the folders the first time (and again only if the whole "Units" object is gone and units are unregistered).
            var missing = new List<WeaponData>();
            foreach (WeaponData u in units)
                if (!EditorPrefs.GetBool(RosterKey(u), false)) missing.Add(u);
            if (roster == null && missing.Count == 0) return; // the user deleted the folder on purpose

            if (roster == null)
            {
                roster = new GameObject("Units").AddComponent<UnitRoster>();
                changed = true;
            }
            Transform soldiers = roster.transform.Find("Soldiers");
            if (soldiers == null) { soldiers = new GameObject("Soldiers").transform; soldiers.SetParent(roster.transform, false); changed = true; }
            if (roster.transform.Find("Turrets") == null)
            {
                new GameObject("Turrets").transform.SetParent(roster.transform, false); // empty for now: the big turrets go here
                changed = true;
            }

            foreach (WeaponData u in missing)
            {
                EditorPrefs.SetBool(RosterKey(u), true);
                var go = new GameObject(u.displayName);
                go.transform.SetParent(soldiers, false);
                var entry = go.AddComponent<UnitEntry>();
                var eso = new SerializedObject(entry);
                eso.FindProperty("data").objectReferenceValue = u;
                eso.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            var pso = new SerializedObject(placer);
            SerializedProperty prop = pso.FindProperty("roster");
            if (prop.objectReferenceValue != roster)
            {
                prop.objectReferenceValue = roster;
                pso.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(roster.gameObject.scene);
                Debug.Log("[ZombieTD] Unit folders ready in the Hierarchy: Units > Soldiers. Select a unit to edit its fire rate and stats (save the scene to keep the folders).");
            }
        }

        static string RosterKey(WeaponData d) => "ZombieTD.RosterEntry." + Application.dataPath + "." + d.name;

        // Makes sure every unit we know how to build is in the tray (the machine gunner, once its pack is imported).
        static void AddMissingUnits(UnitPlacer placer)
        {
            // Fast path (runs on every level reload): already created and already in the tray, so touch nothing.
            var known = AssetDatabase.LoadAssetAtPath<WeaponData>($"{UnitsDir}/MachineGunner.asset");
            var so = new SerializedObject(placer);
            SerializedProperty list = so.FindProperty("buildable");
            if (known != null && known.setupVersion >= MachineGunnerVersion)
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == known) return;

            WeaponData machineGunner = EnsureMachineGunner();
            if (machineGunner == null) return;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == machineGunner) return;

            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = machineGunner;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(placer.gameObject.scene);
            Debug.Log("[ZombieTD] Machine Gunner added to the unit tray (save the scene to keep it).");
        }

        // ---------------------------------------------------------------- machine gunner

        const string MachineGunPrefab = "Assets/PBR Machine Gun/Prefabs/Machine Gun.prefab";
        const float MachineGunLength = 0.7f; // world units; the soldier is 1.0 tall

        /// <summary>Creates the machine gunner (soldier holding the machine gun) if its pack is imported. Null if not.</summary>
        public static WeaponData EnsureMachineGunner()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MachineGunPrefab) == null) return null;
            WeaponData rifle = EnsureAssets(); // shares the rifleman's animator, effects and tracer
            if (rifle == null) return null;

            string dataPath = $"{UnitsDir}/MachineGunner.asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(dataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<WeaponData>();
                data.displayName = "Machine Gunner";
                data.cost = 60;                // per gunner; ten fit on one pad
                data.slotCapacity = 10;
                data.formationSpacing = 0.6f;
                data.range = 28f;
                ApplyMachineGunStats(data);
                data.turnSpeed = 360f;
                data.aimTolerance = 14f;       // doesn't need a perfect aim; the spread is the point
                data.knockback = 0.05f;
                data.defaultTargetMode = TargetMode.First;
                data.shakeOnFire = 0.0005f;
                data.setupVersion = MachineGunnerVersion;
                AssetDatabase.CreateAsset(data, dataPath);
            }
            else if (data.setupVersion < MachineGunnerVersion)
            {
                RefreshMachineGunnerPrefab(); // first build used materials that can't render in URP and a tilted gun
                ApplyMachineGunStats(data);   // much faster fire rate
                data.setupVersion = MachineGunnerVersion;
            }

            if (data.unitPrefab == null) data.unitPrefab = BuildSoldierPrefab(EnsureController(), "MachineGunner", $"{Dir}/MachineGunner.prefab", true);
            if (data.muzzleVfx == null) data.muzzleVfx = rifle.muzzleVfx;
            if (data.hitVfx == null) data.hitVfx = rifle.hitVfx;
            if (data.tracerPrefab == null) data.tracerPrefab = rifle.tracerPrefab;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return data;
        }

        const int MachineGunnerVersion = 6; // bump to redo the gunner's look/stats on existing assets once

        // Very high fire rate, tiny damage per bullet, wide-ish scatter: a hose of bullets that sprays the crowd.
        static void ApplyMachineGunStats(WeaponData data)
        {
            data.fireRate = 30f;        // three times what it was, nearly nine times the rifleman
            data.damage = 1.5f;
            data.spreadDegrees = 3.5f;  // small spread: stray bullets hit the crowd around the target
            data.hitRadius = 0.45f;
            data.effectEvery = 5;       // flash, tracer and hit effect on every 5th bullet keeps it cheap
        }
        const string GunTextures = "Assets/PBR Machine Gun/Art/Textures";

        // The pack's materials use a Built-in-pipeline shader, which can't render in URP (no texture shows).
        // These are URP Lit materials built from the same textures. The "mask map" is R=metal, G=occlusion, A=smoothness,
        // which is exactly what URP Lit reads from its metallic and occlusion slots.
        static Material GunMaterial(string name, string folder, string suffix)
        {
            string path = $"{Dir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;

            string maskPath = $"{GunTextures}/{folder}/MaskMap_{suffix}.png";
            var imp = AssetImporter.GetAtPath(maskPath) as TextureImporter;
            if (imp != null && imp.sRGBTexture) { imp.sRGBTexture = false; imp.SaveAndReimport(); } // mask is data, not colour

            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{GunTextures}/{folder}/BaseMap_{suffix}.png"));
            m.SetColor("_BaseColor", Color.white);

            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{GunTextures}/{folder}/Normal_{suffix}.png");
            m.SetTexture("_BumpMap", normal);
            if (normal != null) m.EnableKeyword("_NORMALMAP");

            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
            m.SetTexture("_MetallicGlossMap", mask);
            m.SetTexture("_OcclusionMap", mask);
            m.SetFloat("_Metallic", 1f);
            m.SetFloat("_Smoothness", 1f);
            if (mask != null) { m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.EnableKeyword("_OCCLUSIONMAP"); }

            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // Swaps the pack's materials on the gun for the URP ones (the scope gets its own).
        static void ApplyGunMaterials(GameObject gun)
        {
            Material body = GunMaterial("MG_Gun", "Gun", "Gun");
            Material scope = GunMaterial("MG_Scope", "Scope", "Scope");
            foreach (Renderer r in gun.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = mats[i] != null && mats[i].name.ToLowerInvariant().Contains("scope") ? scope : body;
                r.sharedMaterials = mats;
            }
        }

        // Hides the rifle that is part of the soldier's own mesh (its own material slot) so only the machine gun shows.
        static void HideBuiltInRifle(GameObject model)
        {
            string path = $"{Dir}/HiddenRifle.mat";
            var hidden = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (hidden == null)
            {
                hidden = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                hidden.SetColor("_BaseColor", new Color(0f, 0f, 0f, 0f)); // alpha 0 + alpha clip = nothing is drawn
                hidden.SetFloat("_AlphaClip", 1f);
                hidden.SetFloat("_Cutoff", 0.5f);
                hidden.EnableKeyword("_ALPHATEST_ON");
                hidden.renderQueue = 2450;
                AssetDatabase.CreateAsset(hidden, path);
            }

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("weapon")) { mats[i] = hidden; changed = true; }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // Redoes the machine gunner's materials on an already-built prefab (it was first built with the pack's BRP materials).
        static void RefreshMachineGunnerPrefab()
        {
            string path = $"{Dir}/MachineGunner.prefab";
            if (!File.Exists(path)) return;

            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Transform gun = null, model = null;
                foreach (Transform t in contents.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "MachineGun") gun = t;
                    else if (t.name == "Model") model = t;
                }
                if (gun != null) ApplyGunMaterials(gun.gameObject);
                if (model != null)
                {
                    HideBuiltInRifle(model.gameObject);
                    if (gun != null) PoseGunInHand(gun, model); // re-aim it against the idle pose
                }
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Debug.Log("[ZombieTD] Machine gunner updated: gun textured, soldier's own rifle hidden, gun re-aimed forward.");
        }

        // Puts the machine gun in the soldier's right hand, sized to the soldier and pointing forward.
        // Returns the gun's root so it can be used as the muzzle point.
        static Transform AttachMachineGun(GameObject rootGo, Transform model)
        {
            var gunPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MachineGunPrefab);
            var gun = (GameObject)PrefabUtility.InstantiatePrefab(gunPrefab);
            gun.name = "MachineGun";
            ApplyGunMaterials(gun);
            HideBuiltInRifle(model.gameObject); // the machine gun replaces his rifle
            PoseGunInHand(gun.transform, model);
            return gun.transform;
        }

        // Parents the gun to his right hand so that, in his idle (combat) pose, it is the right size and points straight
        // ahead. The pose is read from a throwaway copy of the model with the idle animation applied, because the bind
        // pose the model starts in has the hand turned differently and would leave the gun tilted in play.
        static void PoseGunInHand(Transform gun, Transform model)
        {
            Transform hand = FindHand(model);
            if (hand == null) { Debug.LogWarning("[ZombieTD] No right-hand bone found; the machine gun is left where it is."); return; }

            GunFit(out Quaternion axisFix, out float gunScale);

            // Throwaway copy of the model in the idle pose, under a parent scaled like the real "Visual".
            Transform visual = model.parent;
            var tempParent = new GameObject("TempPose");
            tempParent.transform.localScale = visual != null ? visual.localScale : Vector3.one;
            GameObject temp = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), tempParent.transform);
            temp.transform.localPosition = model.localPosition;
            temp.transform.localRotation = model.localRotation;

            AnimationClip idle = LoadClip(IdlePath);
            if (idle != null) idle.SampleAnimation(temp, 0f);
            Transform tempHand = FindHand(temp.transform);

            // Wanted pose in the idle: at the hand, nudged forward, long axis pointing +Z (the way he faces).
            Vector3 forward = Vector3.forward;
            Vector3 wantPos = tempHand.position + forward * 0.12f;
            Quaternion wantRot = Quaternion.LookRotation(forward) * axisFix;

            gun.SetParent(hand, false);
            gun.localPosition = tempHand.InverseTransformPoint(wantPos);
            gun.localRotation = Quaternion.Inverse(tempHand.rotation) * wantRot;
            float handScale = Mathf.Max(0.0001f, hand.lossyScale.x);
            gun.localScale = Vector3.one * (gunScale / handScale);

            Object.DestroyImmediate(tempParent);
        }

        static Transform FindHand(Transform root)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name.ToLowerInvariant().EndsWith("r hand")) return t;
            return null;
        }

        // Scale that makes the gun MachineGunLength long, and the rotation that points its longest axis along +Z.
        static void GunFit(out Quaternion axisFix, out float scale)
        {
            GameObject tmp = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MachineGunPrefab));
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;
            Bounds b = MeasureRenderers(tmp);
            Object.DestroyImmediate(tmp);

            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            scale = longest > 0.0001f ? MachineGunLength / longest : 1f;
            axisFix = b.size.x >= b.size.y && b.size.x >= b.size.z ? Quaternion.Euler(0f, -90f, 0f)   // X -> Z
                    : b.size.y >= b.size.z ? Quaternion.Euler(90f, 0f, 0f)                            // Y -> Z
                    : Quaternion.identity;
            Debug.Log($"[ZombieTD] Machine gun size {b.size:F2}; its longest side is turned to point forward. " +
                      "If the barrel points backward, rotate the 'MachineGun' child 180 degrees.");
        }

        // A copy of the soldier's body material in an orange-brown tint, so gunners read differently from riflemen.
        static void TintBody(GameObject model, string materialName, Color tint)
        {
            Renderer r = model.GetComponentInChildren<SkinnedMeshRenderer>();
            if (r == null) r = model.GetComponentInChildren<Renderer>();
            if (r == null) return;

            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null || !mats[i].name.ToLowerInvariant().Contains("soldier")) continue;
                string path = $"{Dir}/{materialName}.mat";
                var copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(mats[i]);
                    copy.SetColor("_BaseColor", tint);
                    AssetDatabase.CreateAsset(copy, path);
                }
                mats[i] = copy;
                r.sharedMaterials = mats;
                return;
            }
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
        internal static GameObject EnsureEffect(string sourcePath, string outPath, float scale)
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
        static Tower EnsureSoldierPrefab(AnimatorController controller) =>
            BuildSoldierPrefab(controller, "Rifleman", $"{Dir}/Rifleman.prefab", false);

        // Builds (or loads) a soldier unit. With machineGun set he holds the machine gun and wears a tinted uniform.
        static Tower BuildSoldierPrefab(AnimatorController controller, string unitName, string path, bool machineGun)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<Tower>();

            var root = new GameObject(unitName);
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

            // Machine gunner: tinted uniform and the machine gun in his hand (after sizing, so the gun doesn't change his height).
            Transform gun = null;
            if (machineGun)
            {
                TintBody(model, "MachineGunner_Body", new Color(1f, 0.72f, 0.4f));
                gun = AttachMachineGun(root, model.transform);
            }

            var tower = root.AddComponent<SoldierTower>();
            var so = new SerializedObject(tower);
            so.FindProperty("animator").objectReferenceValue = anim;
            so.FindProperty("muzzleHeight").floatValue = MuzzleHeight;
            so.FindProperty("muzzleForward").floatValue = MuzzleForward;
            so.FindProperty("hitScatter").floatValue = 0.08f;
            so.FindProperty("barrelReach").floatValue = BarrelReach;
            if (gun != null)
            {
                so.FindProperty("muzzleAnchor").objectReferenceValue = gun; // flash and tracers come from the machine gun
                so.FindProperty("barrelReach").floatValue = MachineGunLength * 0.5f;
            }
            else AssignGunAnchor(so, model.transform);
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

        internal static Bounds MeasureRenderers(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds();
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        internal static void EnsureFolder(string parent, string name)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{name}")) AssetDatabase.CreateFolder(parent, name);
        }
    }
}
