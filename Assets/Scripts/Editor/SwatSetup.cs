using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZombieTD.Towers;
using Object = UnityEngine.Object;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Builds four units from the Low Poly Character Series SWAT pack (a skin + a gun each) the first time it runs:
    ///   SWAT Rifleman (skin 1, assault rifle)  - cheaper and weaker than the military riflemen
    ///   Sniper        (skin 2, recon rifle)    - slow, long range, a big fast thick bullet with a boom
    ///   Shotgunner    (skin 3, shotgun)        - deadly with area damage on the road, weak on a roof
    ///   Cop           (skin 4, pistol)         - very cheap filler for spare space
    /// These characters ship with no animations, so they stand in their default pose holding the gun and rock back on
    /// every shot. Assets are only created when missing, so your tweaks to them are kept.
    /// </summary>
    static class SwatSetup
    {
        const string Pack = "Assets/Low Poly Character Series 1_SWAT";
        const string Out = "Assets/Prefabs/Swat";
        const string Wfx = "Assets/JMO Assets/WarFX/_Effects";

        class Spec
        {
            public string asset, display, weapon;
            public int skin, cost;
            public float weaponLength;
            public Action<WeaponData> stats;
            public Func<WeaponData, WeaponData> effects; // receives the rifleman (for shared effects) and fills this unit's
        }

        static Spec[] Specs() => new[]
        {
            new Spec
            {
                asset = "SwatRifleman", display = "SWAT Rifleman", skin = 1, weapon = "AR_W_Black", weaponLength = 0.7f, cost = 25,
                stats = d =>
                {
                    d.range = 20f; d.fireRate = 3.5f; d.damage = 3f;          // weaker and cheaper than the military rifleman (4 dmg, $35)
                    d.magazineSize = 20; d.reloadTime = 2.5f;
                    d.turnSpeed = 480f; d.aimTolerance = 9f; d.knockback = 0.1f; d.shakeOnFire = 0.001f;
                },
            },
            new Spec
            {
                asset = "Sniper", display = "Sniper", skin = 2, weapon = "Recon_M", weaponLength = 0.95f, cost = 90,
                stats = d =>
                {
                    d.range = 400f; d.shootOverOwnBuilding = true; d.fireRate = 0.5f; d.damage = 60f;          // one heavy round every two seconds
                    d.magazineSize = 12; d.reloadTime = 2.5f;
                    d.turnSpeed = 300f; d.aimTolerance = 4f; d.knockback = 0.6f;
                    d.splashRadius = 1.3f; d.splashDamageFraction = 0.35f;     // the boom also hurts what stands next to the target
                    d.tracerLife = 0.2f;                                     // a bullet you can see travel
                    d.shakeOnFire = 0.02f; d.shakeOnImpact = 0.06f;
                },
            },
            new Spec
            {
                asset = "Shotgunner", display = "Shotgunner", skin = 3, weapon = "ShotGun_P", weaponLength = 0.8f, cost = 45,
                stats = d =>
                {
                    d.range = 12f; d.fireRate = 1.2f; d.damage = 3f;           // per pellet
                    d.pellets = 7; d.spreadDegrees = 12f; d.hitRadius = 0.5f;
                    d.magazineSize = 6; d.reloadTime = 2.6f;
                    d.turnSpeed = 400f; d.aimTolerance = 14f; d.knockback = 0.5f; d.shakeOnFire = 0.003f;
                    d.splashRadius = 1.8f; d.splashDamageFraction = 0.6f;      // the blast clears out a clump
                    d.damageOnGround = 2.5f; d.splashOnGround = 1f;            // on the road: much more effective, with area damage
                    d.damageOnRoof = 0.3f; d.splashOnRoof = 0f;                // on a roof: far weaker, no area damage
                },
            },
            new Spec
            {
                asset = "Cop", display = "Cop", skin = 4, weapon = "Pistol_F", weaponLength = 0.3f, cost = 12,
                stats = d =>
                {
                    d.range = 14f; d.fireRate = 2f; d.damage = 2f;            // cheap filler for the spare space on a pad
                    d.magazineSize = 12; d.reloadTime = 1.8f;
                    d.turnSpeed = 420f; d.aimTolerance = 10f; d.knockback = 0.05f; d.shakeOnFire = 0.0005f;
                },
            },
        };

        /// <summary>Creates any missing SWAT-pack units. Cheap when everything already exists.</summary>
        public static void EnsureUnits()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Prefabs/Characters/Character_SWAT_A_1.prefab") == null) return; // pack not imported

            Spec[] specs = Specs();
            if (specs.All(s => File.Exists($"{SoldierSetup.UnitsDir}/{s.asset}.asset"))) return; // all there

            WeaponData rifle = SoldierSetup.EnsureAssets(); // shared effects, tracer and the unit folder
            if (rifle == null) return;

            Debug.Log($"[ZombieTD] SWAT pack materials: {KitMaterialConverter.ConvertFolder(Pack)}"); // Standard -> URP Lit
            SoldierSetup.EnsureFolder("Assets/Prefabs", "Swat");

            foreach (Spec s in specs) EnsureUnit(s, rifle);
            AssetDatabase.SaveAssets();
        }

        static void EnsureUnit(Spec s, WeaponData rifle)
        {
            string path = $"{SoldierSetup.UnitsDir}/{s.asset}.asset";
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<WeaponData>();
                data.displayName = s.display;
                data.cost = s.cost;
                data.slotCapacity = 20;
                data.formationSpacing = 0.5f;
                data.defaultTargetMode = TargetMode.First;
                s.stats(data);
                data.reloadVersion = 1;
                data.setupVersion = 4;
                AssetDatabase.CreateAsset(data, path);
            }

            if (data.unitPrefab == null) data.unitPrefab = BuildUnitPrefab(s);

            // Effects: each unit gets the flash and blast that suit its gun; the rest are the rifleman's.
            switch (s.asset)
            {
                case "Sniper":
                    if (data.muzzleVfx == null) data.muzzleVfx = SoldierSetup.EnsureEffect($"{Wfx}/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE3.prefab", $"{Out}/Sniper_MuzzleFlash.prefab", 1.2f);
                    if (data.hitVfx == null) data.hitVfx = SoldierSetup.EnsureEffect($"{Wfx}/Explosions/WFX_Explosion Small.prefab", $"{Out}/Sniper_Boom.prefab", 0.45f);
                    if (data.tracerPrefab == null) data.tracerPrefab = EnsureSniperTracer();
                    break;
                case "Shotgunner":
                    if (data.muzzleVfx == null) data.muzzleVfx = SoldierSetup.EnsureEffect($"{Wfx}/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE2.prefab", $"{Out}/Shotgun_MuzzleFlash.prefab", 0.9f);
                    if (data.hitVfx == null) data.hitVfx = SoldierSetup.EnsureEffect($"{Wfx}/Explosions/WFX_Explosion Small.prefab", $"{Out}/Shotgun_Blast.prefab", 0.3f);
                    if (data.tracerPrefab == null) data.tracerPrefab = rifle.tracerPrefab;
                    break;
                case "Cop":
                    if (data.muzzleVfx == null) data.muzzleVfx = SoldierSetup.EnsureEffect($"{Wfx}/MuzzleFlashes/4Planes/WFX_MF 4P RIFLE1.prefab", $"{Out}/Pistol_MuzzleFlash.prefab", 0.25f);
                    if (data.hitVfx == null) data.hitVfx = rifle.hitVfx;
                    if (data.tracerPrefab == null) data.tracerPrefab = rifle.tracerPrefab;
                    break;
                default: // SWAT rifleman
                    if (data.muzzleVfx == null) data.muzzleVfx = rifle.muzzleVfx;
                    if (data.hitVfx == null) data.hitVfx = rifle.hitVfx;
                    if (data.tracerPrefab == null) data.tracerPrefab = rifle.tracerPrefab;
                    break;
            }
            EditorUtility.SetDirty(data);
        }

        // A fat, hot, long-lasting tracer: the sniper's bullet is meant to be seen.
        static Tracer EnsureSniperTracer()
        {
            string path = $"{Out}/Sniper_Tracer.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<Tracer>();

            string matPath = "Assets/Materials/SniperTracer.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", new Color(2.2f, 1.4f, 0.5f)); // very bright HDR: blooms hard
                AssetDatabase.CreateAsset(mat, matPath);
            }

            var go = new GameObject("Sniper_Tracer");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.1f; // about twice a rifle tracer
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            go.AddComponent<Tracer>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return saved.GetComponent<Tracer>();
        }

        // ---------------------------------------------------------------- unit prefab

        // Root (SoldierTower) > Visual (scaled to the soldier height) > the SWAT skin, with the gun in its right hand.
        static Tower BuildUnitPrefab(Spec s)
        {
            string path = $"{Out}/{s.asset}.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<Tower>();

            var skin = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Prefabs/Characters/Character_SWAT_A_{s.skin}.prefab");
            if (skin == null) { Debug.LogError($"[ZombieTD] SWAT skin {s.skin} not found."); return null; }

            var root = new GameObject(s.asset);
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(skin, visual);
            model.name = "Model";

            // Size it like the other soldiers and stand it on the ground.
            Bounds b = SoldierSetup.MeasureRenderers(model);
            if (b.size.y > 0.01f)
            {
                model.transform.localPosition = new Vector3(0f, -b.min.y, 0f);
                visual.localScale = Vector3.one * (SoldierSetup.UnitHeight / b.size.y);
            }
            else Debug.LogWarning($"[ZombieTD] Couldn't measure {s.asset}; scale its Visual child by hand.");

            Transform gun = AttachWeapon(root, model.transform, s.weapon, s.weaponLength);

            var tower = root.AddComponent<SoldierTower>();
            var so = new SerializedObject(tower);
            so.FindProperty("hitScatter").floatValue = 0.08f;
            so.FindProperty("leanAngle").floatValue = 12f;
            if (gun != null)
            {
                so.FindProperty("muzzleAnchor").objectReferenceValue = gun;
                so.FindProperty("barrelReach").floatValue = s.weaponLength * 0.5f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return saved.GetComponent<Tower>();
        }

        // Puts the gun in the right hand at the default pose, sized to its length and pointing forward.
        static Transform AttachWeapon(GameObject root, Transform model, string weaponName, float length)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}/Prefabs/Weapons/{weaponName}.prefab");
            if (prefab == null) { Debug.LogWarning($"[ZombieTD] Weapon {weaponName} not found."); return null; }

            // Measure a clean copy: its longest side becomes the barrel direction.
            GameObject tmp = Object.Instantiate(prefab);
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;
            Bounds wb = SoldierSetup.MeasureRenderers(tmp);
            Object.DestroyImmediate(tmp);

            float longest = Mathf.Max(wb.size.x, wb.size.y, wb.size.z);
            float scale = longest > 0.0001f ? length / longest : 1f;
            Quaternion axisFix = wb.size.x >= wb.size.y && wb.size.x >= wb.size.z ? Quaternion.Euler(0f, -90f, 0f)   // X -> Z
                               : wb.size.y >= wb.size.z ? Quaternion.Euler(90f, 0f, 0f)                              // Y -> Z
                               : Quaternion.identity;

            var gun = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            gun.name = "Weapon";
            gun.transform.localScale = Vector3.one * scale;
            gun.transform.rotation = axisFix;

            Transform hand = FindRightHand(model);
            Vector3 at = (hand != null ? hand.position : root.transform.position + Vector3.up * 0.5f) + Vector3.forward * (length * 0.25f);
            gun.transform.position = at;
            gun.transform.SetParent(hand != null ? hand : model, true); // keeps this pose when it becomes a child of the hand
            return gun.transform;
        }

        static Transform FindRightHand(Transform model)
        {
            Transform best = null;
            int bestScore = 0;
            Transform[] all = model.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                string n = t.name.ToLowerInvariant();
                if (!n.Contains("hand")) continue;
                if (n.Contains("index") || n.Contains("thumb") || n.Contains("pinky") || n.Contains("ring") || n.Contains("middle") || n.Contains("finger")) continue;
                if (n.Contains("left") || n.EndsWith(".l") || n.EndsWith("_l") || n.EndsWith(" l")) continue;

                int score = n.EndsWith(".r") || n.EndsWith("_r") || n.EndsWith(" r") || n.Contains("right") ? 3 : 1;
                if (n.StartsWith("c_")) score--; // prefer the skinned bone over its control rig
                if (score > bestScore) { bestScore = score; best = t; }
            }

            if (best == null)
                Debug.LogWarning("[ZombieTD] No right-hand bone found on the SWAT model; the gun is placed at his side. Bones: " +
                                 string.Join(", ", all.Select(t => t.name).Take(80)));
            return best;
        }
    }
}
