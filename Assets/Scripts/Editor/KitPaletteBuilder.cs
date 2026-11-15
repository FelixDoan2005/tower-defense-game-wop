using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZombieTD.Maze;

namespace ZombieTD.EditorTools
{
    /// <summary>
    /// Tools > Build Scifi Kit Palette: lays out every prefab of the kit in an evenly spaced grid, one row group per
    /// category with a label under each piece, so you can browse the whole pack in the Scene view.
    /// Built into the open scene under "KitPalette" (rebuilt each run; delete the object when done).
    /// </summary>
    public static class KitPaletteBuilder
    {
        const string KitRoot = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Prefabs";
        const int Columns = 10;
        const string RootName = "KitPalette";

        class Item
        {
            public GameObject go;
            public string name;
            public Bounds bounds;
            public float scale = 1f;
        }

        [MenuItem("Tools/Build Scifi Kit Palette")]
        static void Build()
        {
            if (!AssetDatabase.IsValidFolder(KitRoot))
            {
                Debug.LogError($"[ZombieTD] Kit folder not found: {KitRoot}");
                return;
            }

            // Make sure the kit's materials work in URP first, otherwise everything shows up pink.
            Debug.Log($"[ZombieTD] Palette: {KitMaterialConverter.ConvertFolder("Assets/Creepy_Cat")}");

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);

            var root = new GameObject(RootName).transform;

            // Pass 1: instantiate everything at the origin and measure it.
            var categories = new SortedDictionary<string, List<Item>>();
            var maxDims = new List<float>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { KitRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.enabled = false; // kit lights/sun off

                string category = Path.GetFileName(Path.GetDirectoryName(path));
                var item = new Item { go = go, name = prefab.name, bounds = Measure(go) };
                if (!categories.TryGetValue(category, out List<Item> list)) categories[category] = list = new List<Item>();
                list.Add(item);
                maxDims.Add(Mathf.Max(item.bounds.size.x, item.bounds.size.z));
            }

            // Uniform cell from the 90th percentile footprint so a few giants don't spread everything apart.
            maxDims.Sort();
            float p90 = maxDims.Count > 0 ? maxDims[Mathf.Min(maxDims.Count - 1, Mathf.FloorToInt(maxDims.Count * 0.9f))] : 6f;
            float cell = Mathf.Clamp(p90 + 2f, 5f, 26f);

            // Pass 2: lay out in a grid, category by category.
            Vector3 origin = FindOrigin();
            root.position = origin;
            int total = 0, shrunk = 0, rowIndex = 0;
            var rowsUsed = 0;
            foreach (KeyValuePair<string, List<Item>> cat in categories)
            {
                cat.Value.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                Label(root, $"{cat.Key.ToUpperInvariant()}  ({cat.Value.Count})", new Vector3(-cell * 0.5f, 0.05f, (rowIndex + 0.5f) * cell),
                    cell * 5f, 3f, 14f, TextAlignmentOptions.Right, new Color(1f, 0.75f, 0.2f));

                for (int i = 0; i < cat.Value.Count; i++)
                {
                    Item it = cat.Value[i];
                    int col = i % Columns, row = rowIndex + i / Columns;
                    Vector3 center = new Vector3((col + 0.5f) * cell + cell, 0f, (row + 0.5f) * cell);

                    float footprint = Mathf.Max(it.bounds.size.x, it.bounds.size.z, it.bounds.size.y * 0.5f);
                    float fit = cell - 1.5f;
                    it.scale = footprint > fit ? fit / footprint : 1f;
                    if (it.scale < 1f) shrunk++;

                    var holder = new GameObject(it.name).transform;
                    holder.SetParent(root, false);
                    holder.localPosition = center;
                    it.go.transform.SetParent(holder, false);
                    it.go.transform.localScale = Vector3.one * it.scale;
                    // Centre the model over the cell with its lowest point on the ground.
                    it.go.transform.localPosition = new Vector3(-it.bounds.center.x, -it.bounds.min.y, -it.bounds.center.z) * it.scale;

                    string label = it.scale < 1f ? $"{it.name}\n<size=70%>shown at {it.scale:0.00}x</size>" : it.name;
                    Label(holder, label, new Vector3(0f, 0.05f, -cell * 0.5f + 0.8f), cell, 1.4f, 5f, TextAlignmentOptions.Center, Color.white);
                    total++;
                }
                rowIndex += Mathf.CeilToInt(cat.Value.Count / (float)Columns) + 1; // blank row between categories
                rowsUsed = rowIndex;
            }

            // Dark base plate so the models and labels read against the sky.
            var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plate.name = "PaletteBase";
            Object.DestroyImmediate(plate.GetComponent<Collider>());
            plate.transform.SetParent(root, false);
            float width = (Columns + 2) * cell, depth = Mathf.Max(1, rowsUsed) * cell;
            plate.transform.localScale = new Vector3(width, 0.2f, depth);
            plate.transform.localPosition = new Vector3(width * 0.5f - cell * 0.5f, -0.11f, depth * 0.5f);
            plate.GetComponent<Renderer>().sharedMaterial = PlateMaterial();

            Selection.activeGameObject = root.gameObject;
            EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            Debug.Log($"[ZombieTD] Palette: {total} pieces in {categories.Count} categories, {cell:0.#}-unit cells" +
                      $"{(shrunk > 0 ? $", {shrunk} oversized pieces shown scaled down (labelled)" : "")}. Placed at {origin}.");
        }

        // Combined world bounds of all renderers (prefab at origin, identity transform).
        static Bounds Measure(GameObject go)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(Vector3.up * 0.5f, Vector3.one);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // Beside the level (past its far wall) when one exists, else a bit away from the origin.
        static Vector3 FindOrigin()
        {
            var level = Object.FindAnyObjectByType<LevelGenerator>();
            if (level != null && level.Grid != null)
            {
                Bounds b = level.Grid.WorldBounds;
                return new Vector3(b.min.x, 0f, b.max.z + 60f);
            }
            return new Vector3(0f, 0f, 60f);
        }

        static void Label(Transform parent, string text, Vector3 localPos, float width, float height, float maxSize,
            TextAlignmentOptions align, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // lies flat, readable from the camera
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            t.rectTransform.sizeDelta = new Vector2(width, height);
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.5f;
            t.fontSizeMax = maxSize;
            t.fontStyle = FontStyles.Bold;
        }

        static Material PlateMaterial()
        {
            const string path = "Assets/Materials/PaletteBase.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", new Color(0.16f, 0.17f, 0.19f));
            m.SetFloat("_Smoothness", 0.1f);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
