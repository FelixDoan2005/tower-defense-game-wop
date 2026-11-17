using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Core;
using ZombieTD.Maze;
using ZombieTD.Towers;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Tools > Palette > Build Shotgunner Shield Wall. A visual mock-up (no gameplay yet) of a single shield wall:
    /// one row of standing shotgunners, each holding a ballistic shield (SWAT pack) out in front of him with his left
    /// arm, shields touching side by side. Built on a plate beside the level so it can be judged.
    /// The pack has no animations, so each left arm is bent to the shield with a small two-bone solver
    /// (Shoulder_L > Elbow_L > Hand_L) and the result is baked into the pose.
    /// </summary>
    public static class ShieldWallPalette
    {
        const string RootName = "ShieldWallPalette";
        const string ShotgunnerPath = "Assets/Prefabs/Swat/Shotgunner.prefab";
        const string ShieldPath = "Assets/Low Poly Character Series 1_SWAT/Prefabs/Character_Kits/Ballistic Shield_A.prefab";

        // Look and feel: change these and rebuild.
        const int Soldiers = 10;
        const float ShieldHeight = 0.72f;    // world units (a soldier is 1.0 tall)
        const float ShieldLean = 6f;         // degrees the top leans out
        const float HandBelowShoulder = 0.30f; // where on the shield the hand grips, below the shoulder
        const float ShieldDistance = 0.90f;  // how far in front of his body the shield stands (limited by how far the arm reaches)
        const float BodyLean = 12f;          // degrees he leans forward into the shield: reaches further, and looks like he's pushing it out
        const float GripSide = 0.30f;        // where along the shield he grips it: 0 = middle, 0.5 = its left edge (a fraction of its width)
        const float HoldReach = 1.0f;        // how much of full arm length is used: 1 = the arm reaches fully straight
        const float Spacing = 0.30f;         // gap between neighbouring shields, in world units (0 = touching)

        static GameObject shieldPrefab;
        static Quaternion shieldFix;         // turns the shield so its face points forward and its length points up
        static float shieldScale, shieldWidth;

        [MenuItem("Tools/Palette/Build Shotgunner Shield Wall")]
        static void Build()
        {
            SwatSetup.EnsureUnits(); // make sure the Shotgunner prefab exists
            var soldier = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunnerPath);
            shieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShieldPath);
            if (soldier == null || shieldPrefab == null)
            {
                Debug.LogError("[ZombieTD] Need the Shotgunner prefab (Assets/Prefabs/Swat) and the SWAT pack's Ballistic Shield. Is the SWAT pack imported?");
                return;
            }
            Debug.Log($"[ZombieTD] Shield materials: {KitMaterialConverter.ConvertFolder("Assets/Low Poly Character Series 1_SWAT")}");
            MeasureShield();

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            Vector3 origin = FindOrigin();
            var root = new GameObject(RootName).transform;
            root.position = origin;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Shield wall palette");

            Vector3 facing = Vector3.back; // the "enemy" is toward the camera, so we see their faces and shields
            float step = shieldWidth + Spacing;
            Plate(root, Mathf.Max(14f, Soldiers * step + 6f), 9f);
            Label(root, "Single shield wall: all standing, left arm on the shield", new Vector3(0f, 0.06f, -3.6f), 14f, 1.1f);

            for (int i = 0; i < Soldiers; i++)
                Man(soldier, root, new Vector3((i - (Soldiers - 1) * 0.5f) * step, 0f, 0f), facing, $"Man_{i + 1}");

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log($"[ZombieTD] Shield wall palette built at {origin} (selected; the Scene view is framed on it). Delete the '{RootName}' object when done.");
        }

        // ---------------------------------------------------------------- one man

        static GameObject Man(GameObject soldierPrefab, Transform parent, Vector3 local, Vector3 facing, string name)
        {
            var man = (GameObject)PrefabUtility.InstantiatePrefab(soldierPrefab, parent);
            man.name = name;
            foreach (SoldierTower t in man.GetComponentsInChildren<SoldierTower>(true)) t.enabled = false; // visuals only

            // The skin comes with a ballistic shield strapped to its back; he's holding one now, so hide the spare.
            foreach (Transform t in man.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("SWAT_A_Ballistic Shield")) t.gameObject.SetActive(false);

            man.transform.localPosition = local;
            man.transform.rotation = parent.rotation * Quaternion.LookRotation(facing);

            Transform upper = Find(man.transform, "Shoulder_L"), fore = Find(man.transform, "Elbow_L"), hand = Find(man.transform, "Hand_L");
            if (upper == null || fore == null || hand == null)
            {
                Debug.LogWarning($"[ZombieTD] {name}: no Shoulder_L / Elbow_L / Hand_L bones; his arm is left as it is.");
                return man;
            }

            // The shield stands right in front of his body, and his left arm comes out to grip it near its left side.
            Vector3 f = man.transform.forward;
            Vector3 left = -Vector3.Cross(Vector3.up, f);

            // Lean the upper body into it first: his shoulder (and so his whole arm) moves forward, so the shield can go further out.
            Transform spine = Find(man.transform, "Spine_01");
            if (spine != null) spine.RotateAround(spine.position, Vector3.Cross(Vector3.up, f), BodyLean);

            Vector3 shoulder = upper.position;
            float maxReach = (upper.position - fore.position).magnitude + (fore.position - hand.position).magnitude;
            maxReach *= HoldReach;

            float handY = shoulder.y - HandBelowShoulder;
            Vector3 body = man.transform.position;
            float sideOffset = shieldWidth * GripSide;
            const float backFace = 0.03f; // the grip is on the shield's back face

            // Start with the shield ShieldDistance in front of his body; if the grip is then out of reach, bring it in.
            Vector3 centre = new Vector3(body.x, handY, body.z) + f * ShieldDistance;
            Vector3 grip = centre + left * sideOffset - f * backFace;
            float over = Vector3.Distance(shoulder, grip) - maxReach;
            if (over > 0f)
            {
                centre -= f * over;
                grip = centre + left * sideOffset - f * backFace;
            }

            Shield(parent, centre, f, man.name);
            Vector3 elbowHint = shoulder + left * 0.5f + Vector3.down * 0.3f - f * 0.1f; // elbow bends out to the side and down
            ReachWith(upper, fore, hand, grip, elbowHint);
            return man;
        }

        // ---------------------------------------------------------------- the placeable unit

        const string WallPrefabPath = "Assets/Prefabs/Swat/ShotgunWall.prefab";
        const string WallAssetPath = SoldierSetup.UnitsDir + "/ShotgunWall.asset";
        const int WallMen = 14;      // about 10 units wide: half the corridor (20)
        const int WallPrefabVersion = 2; // bump to rebuild the prefab once (stats you tuned on the asset are kept)

        /// <summary>
        /// Creates the Shotgun Wall unit (prefab: five shotgunners with shields, plus its WeaponData) if missing.
        /// Needs the Shotgunner prefab, so call after SwatSetup.EnsureUnits.
        /// </summary>
        internal static void EnsureWallUnit()
        {
            var data = AssetDatabase.LoadAssetAtPath<WeaponData>(WallAssetPath);
            string versionKey = "ZombieTD.ShotgunWallPrefab." + Application.dataPath;
            if (data != null && data.unitPrefab != null && EditorPrefs.GetInt(versionKey, 0) >= WallPrefabVersion) return;

            var soldier = AssetDatabase.LoadAssetAtPath<GameObject>(ShotgunnerPath);
            shieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShieldPath);
            var shotgunner = AssetDatabase.LoadAssetAtPath<WeaponData>(SoldierSetup.UnitsDir + "/Shotgunner.asset");
            if (soldier == null || shieldPrefab == null || shotgunner == null) return; // SWAT pack not imported yet

            KitMaterialConverter.ConvertFolder("Assets/Low Poly Character Series 1_SWAT");
            MeasureShield();

            // Build at the origin of an empty scene-less object: the arm solver works in world space.
            var root = new GameObject("ShotgunWall");
            var visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            float step = shieldWidth + Spacing;
            var muzzles = new System.Collections.Generic.List<Transform>();
            for (int i = 0; i < WallMen; i++)
            {
                GameObject man = Man(soldier, visual, new Vector3((i - (WallMen - 1) * 0.5f) * step, 0f, 0f), Vector3.forward, $"Man_{i + 1}");
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(man)) PrefabUtility.UnpackPrefabInstance(man, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (SoldierTower t in man.GetComponentsInChildren<SoldierTower>(true)) Object.DestroyImmediate(t); // the wall is one unit
                foreach (PooledObject p in man.GetComponentsInChildren<PooledObject>(true)) Object.DestroyImmediate(p);
                Transform gun = Find(man.transform, "Weapon");
                if (gun != null) muzzles.Add(gun);
            }
            foreach (Transform t in visual.GetComponentsInChildren<Transform>(true))
                if (t != null && t.gameObject != null && PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject))
                    PrefabUtility.UnpackPrefabInstance(t.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); // the shields

            var tower = root.AddComponent<SoldierTower>();
            var so = new SerializedObject(tower);
            so.FindProperty("leanAngle").floatValue = 0f;
            so.FindProperty("recoilKick").floatValue = 1.2f;
            so.FindProperty("barrelReach").floatValue = 0.4f;
            so.FindProperty("hitScatter").floatValue = 0.15f;
            if (muzzles.Count > 0)
            {
                so.FindProperty("muzzleAnchor").objectReferenceValue = muzzles[0];
                SerializedProperty extra = so.FindProperty("extraMuzzles");
                extra.arraySize = muzzles.Count - 1;
                for (int i = 1; i < muzzles.Count; i++) extra.GetArrayElementAtIndex(i - 1).objectReferenceValue = muzzles[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, WallPrefabPath);
            Object.DestroyImmediate(root);

            EditorPrefs.SetInt(versionKey, WallPrefabVersion);
            bool created = data == null;
            if (created)
            {
                data = ScriptableObject.CreateInstance<WeaponData>();
                AssetDatabase.CreateAsset(data, WallAssetPath);
            }
            data.unitPrefab = saved.GetComponent<Tower>();
            if (!created) { EditorUtility.SetDirty(data); AssetDatabase.SaveAssets(); Debug.Log($"[ZombieTD] Shotgun Wall rebuilt with {WallMen} men."); return; }

            data.displayName = "Shotgun Wall";
            data.cost = 200;
            data.unitPrefab = saved.GetComponent<Tower>();
            data.padKind = PadKind.Wall;
            data.slotCapacity = 3;                       // up to three walls per pad, layered behind each other
            data.health = 1500f;                          // shields: it takes a crowd a while to chew through
            data.formationSpacing = 2.8f;
            data.range = 30f; data.fireRate = 4f; data.damage = 9f; data.pellets = 8;
            data.defaultTargetMode = TargetMode.Closest;
            data.turnSpeed = 0f; data.aimTolerance = 180f; // a wall doesn't turn: it fires at whatever is in front
            data.knockback = 0.6f; data.shakeOnFire = 0.004f;
            data.spreadDegrees = 12f; data.hitRadius = 0.5f;
            data.splashRadius = 2.4f; data.splashDamageFraction = 0.6f;
            data.damageOnGround = 2.5f; data.splashOnGround = 1f; // always on the road
            data.magazineSize = 30; data.reloadTime = 3f;
            data.muzzleVfx = shotgunner.muzzleVfx;
            data.hitVfx = shotgunner.hitVfx;
            data.tracerPrefab = shotgunner.tracerPrefab;
            data.reloadVersion = 1;
            data.setupVersion = 4;
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log("[ZombieTD] Shotgun Wall unit created (Assets/Settings/Units/ShotgunWall.asset). Drag it onto the pads in front of the police cars.");
        }

        // Standing in front of him, square to the front, leaning out a little. Returns its world bounds.
        static Bounds Shield(Transform parent, Vector3 centre, Vector3 f, string owner)
        {
            var holder = new GameObject("Shield_" + owner).transform;
            holder.SetParent(parent, false);
            holder.position = centre;
            holder.rotation = Quaternion.AngleAxis(ShieldLean, Vector3.Cross(Vector3.up, f)) * Quaternion.LookRotation(f);

            var shield = (GameObject)PrefabUtility.InstantiatePrefab(shieldPrefab, holder);
            shield.name = "Shield";
            shield.transform.localRotation = shieldFix;
            shield.transform.localScale = Vector3.one * shieldScale;
            shield.transform.localPosition = Vector3.zero;

            Bounds sb = SoldierSetup.MeasureRenderers(shield);
            shield.transform.position += centre - sb.center; // centre it on the grip point
            return SoldierSetup.MeasureRenderers(shield);
        }

        // ---------------------------------------------------------------- the arm

        // Two-bone solver. Bends upper > fore so the hand lands on the target, the elbow pointing toward the hint.
        // Works in world space, so it doesn't care how the bones' own axes are set up.
        static void ReachWith(Transform upper, Transform fore, Transform hand, Vector3 target, Vector3 hint)
        {
            Vector3 s = upper.position;
            float l1 = Vector3.Distance(s, fore.position), l2 = Vector3.Distance(fore.position, hand.position);

            Vector3 toTarget = target - s;
            float dist = Mathf.Clamp(toTarget.magnitude, Mathf.Abs(l1 - l2) + 0.001f, l1 + l2 - 0.001f);
            Vector3 dir = toTarget.normalized;

            // Angle at the shoulder between the line to the target and the upper arm (law of cosines).
            float cosA = (l1 * l1 + dist * dist - l2 * l2) / (2f * l1 * dist);
            float angle = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));

            Vector3 bend = Vector3.ProjectOnPlane(hint - s, dir);
            if (bend.sqrMagnitude < 0.0001f) bend = Vector3.ProjectOnPlane(Vector3.down, dir);
            bend.Normalize();

            Vector3 elbowAt = s + (dir * Mathf.Cos(angle) + bend * Mathf.Sin(angle)) * l1;

            // Turn the upper arm to point at the new elbow, then the forearm to point at the target.
            upper.rotation = Quaternion.FromToRotation(fore.position - s, elbowAt - s) * upper.rotation;
            fore.rotation = Quaternion.FromToRotation(hand.position - fore.position, target - fore.position) * fore.rotation;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        // ---------------------------------------------------------------- the shield's own shape

        // Finds which way the shield model faces: its thinnest side is the face (turned to point forward), its longest
        // side is its height (turned to point up). Also gives the scale that makes it ShieldHeight tall.
        static void MeasureShield()
        {
            GameObject tmp = Object.Instantiate(shieldPrefab);
            tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            tmp.transform.localScale = Vector3.one;
            Bounds b = SoldierSetup.MeasureRenderers(tmp);
            Object.DestroyImmediate(tmp);

            Vector3 size = b.size;
            int thin = 0;
            for (int i = 1; i < 3; i++) if (size[i] < size[thin]) thin = i;
            int tall = thin == 0 ? 1 : 0;
            for (int i = 0; i < 3; i++) if (i != thin && size[i] > size[tall]) tall = i;
            int wide = 3 - thin - tall;

            // LookRotation(t, u) maps world Z to t and world Y to u; its inverse sends the model's axes to Z and Y.
            shieldFix = Quaternion.Inverse(Quaternion.LookRotation(Axis(thin), Axis(tall)));
            shieldScale = size[tall] > 0.0001f ? ShieldHeight / size[tall] : 1f;
            shieldWidth = size[wide] * shieldScale;
            Debug.Log($"[ZombieTD] Shield model {size:F2}: face = axis {thin}, height = axis {tall}; scaled to {ShieldHeight} tall, {shieldWidth:0.00} wide.");
        }

        static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        // ---------------------------------------------------------------- layout helpers

        static void Plate(Transform root, float width, float depth)
        {
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "Plate";
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(root, false);
            plate.transform.localScale = new Vector3(width, 3f, depth);
            plate.transform.localPosition = new Vector3(0f, -1.5f, 0f);

            const string path = "Assets/Materials/PaletteBase.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.19f));
                AssetDatabase.CreateAsset(mat, path);
            }
            plate.GetComponent<Renderer>().sharedMaterial = mat;
        }

        static void Label(Transform parent, string text, Vector3 local, float width, float height)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies flat, readable from the camera
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(width, height);
            t.text = text;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.5f;
            t.fontSizeMax = 6f;
            t.fontStyle = FontStyles.Bold;
        }

        // Just in front of the map (on the camera's side), clear of the kit palette that sits behind it.
        static Vector3 FindOrigin()
        {
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (level != null && level.Grid != null)
            {
                Bounds b = level.Grid.WorldBounds;
                return new Vector3(b.center.x, 0f, b.min.z - 30f);
            }
            return new Vector3(0f, 0f, -30f);
        }
    }
}
