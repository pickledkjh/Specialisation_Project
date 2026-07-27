using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Editor tool: Tools > Battlefield > Build Simple Battlefield. Generates the city
/// arena from the CartoonLowPolyCityLite pack - ground, building rings, props,
/// boundary walls and the altitude ceiling. Re-run to regenerate, Remove to delete.
/// Also converts the pack's Standard-shader materials to URP so they don't render
/// magenta.
/// </summary>
public static class BattlefieldBuilder
{
    private const string RootName = "City Arena";
    private const string PackPath = "Assets/CartoonLowPolyCityLite";
    private const int Seed = 20260722; // change for a different layout

    private const float ClearRadius = 22f;    // untouched fighting space
    private const float WallRadius = 70f;     // invisible boundary (matches ArenaLimits.Radius)
    private const float CeilingHeight = 34f;  // hard altitude limit (matches ArenaLimits.Ceiling)
    private const float WallHeight = CeilingHeight + 6f;

    // Ground texture (from the imported Stone_textures pack). Swap the path for any
    // of its variants - Cool/Warm/Grey, Ground2, ground3 - and rebuild.
    private const string GroundTexturePath = "Assets/Stone_textures/Ground1_Grey.png";
    private const float GroundMetersPerTile = 6f;

    [MenuItem("Tools/Battlefield/Build Simple Battlefield")]
    public static void Build()
    {
        FixPackMaterialsForURP();

        GameObject old = GameObject.Find(RootName);
        if (old != null) Object.DestroyImmediate(old);

        GameObject house1 = LoadPrefab("Prefabs/House_01.prefab");
        GameObject house2 = LoadPrefab("Prefabs/House_16.prefab");
        GameObject lightPole = LoadPrefab("Prefabs/Light_01.prefab");
        GameObject car = LoadPrefab("Prefabs/Car_03.prefab");
        GameObject trash = LoadPrefab("Prefabs/Trash_01.prefab");

        if (house1 == null && house2 == null)
        {
            Debug.LogError("[Battlefield] Could not find the city pack prefabs under " + PackPath + "/Prefabs - is the pack folder intact?");
            return;
        }

        System.Random rng = new System.Random(Seed);
        GameObject root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Battlefield");

        BuildGround(root.transform);

        GameObject[] houses = house1 != null && house2 != null
            ? new[] { house1, house2 }
            : new[] { house1 ?? house2 };

        // Inner cover ring - big enough gaps to dash through
        PlaceRing(houses, root.transform, rng, count: 10, radius: 30f, radiusJitter: 3f,
                  minH: 6f, maxH: 14f, faceCenter: true, collider: true);

        // Middle ring - cover during ranged play
        PlaceRing(houses, root.transform, rng, count: 18, radius: 46f, radiusJitter: 5f,
                  minH: 6f, maxH: 16f, faceCenter: true, collider: true);

        // Outer ring - still inside the (larger) walls
        PlaceRing(houses, root.transform, rng, count: 20, radius: 60f, radiusJitter: 5f,
                  minH: 7f, maxH: 17f, faceCenter: true, collider: true);

        // Backdrop ring beyond the walls - pure scenery for depth
        PlaceRing(houses, root.transform, rng, count: 24, radius: 82f, radiusJitter: 6f,
                  minH: 8f, maxH: 18f, faceCenter: true, collider: false);

        // Distant SKYLINE ring - tall houses so the horizon reads as a real city
        PlaceRing(houses, root.transform, rng, count: 20, radius: 102f, radiusJitter: 8f,
                  minH: 14f, maxH: 28f, faceCenter: true, collider: false);

        // Street dressing
        if (lightPole != null)
            PlaceRing(new[] { lightPole }, root.transform, rng, count: 10, radius: 24f, radiusJitter: 0.5f,
                      minH: 3.5f, maxH: 6f, faceCenter: false, collider: false);
        if (car != null)
            Scatter(car, root.transform, rng, count: 10, minR: 26f, maxR: 58f, minH: 1.2f, maxH: 2.2f, collider: true);
        if (trash != null)
            Scatter(trash, root.transform, rng, count: 11, minR: 25f, maxR: 60f, minH: 0.6f, maxH: 1.5f, collider: false);

        BuildBoundaryWalls(root.transform);

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Battlefield] Built. Center clear to " + ClearRadius + "u, walls at " + WallRadius +
                  "u. Re-run the menu item to regenerate, Tools -> Battlefield -> Remove to delete. Save the scene to keep it.");
    }

    [MenuItem("Tools/Battlefield/Remove Battlefield")]
    public static void Remove()
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Battlefield] Removed.");
        }
    }

    // ---------------- pieces ----------------

    private static void BuildGround(Transform root)
    {
        // Big plate slightly below the existing 70x70 Plane so there is floor out to
        // the walls and beyond (prevents falling off / seeing the void at range).
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root, false);
        ground.transform.position = new Vector3(0f, -0.05f, 0f);
        ground.transform.localScale = new Vector3(32f, 1f, 32f); // 320 x 320

        // Stone ground from the imported texture pack; plain gray fallback if the
        // texture is missing. The center Plane gets the same look so it all matches.
        Material plateMat = MakeGroundMaterial(320f, "Stone Ground (wide)");
        if (plateMat != null)
            ground.GetComponent<MeshRenderer>().sharedMaterial = plateMat;

        GameObject centerPlane = GameObject.Find("Plane");
        if (centerPlane != null)
        {
            MeshRenderer planeRenderer = centerPlane.GetComponent<MeshRenderer>();
            if (planeRenderer != null)
            {
                Bounds pb = planeRenderer.bounds;
                Material planeMat = MakeGroundMaterial(Mathf.Max(pb.size.x, 10f), "Stone Ground");
                if (planeMat != null)
                {
                    Undo.RecordObject(planeRenderer, "Ground material");
                    planeRenderer.sharedMaterial = planeMat;
                }
            }
        }
    }

    // URP material with the stone texture tiled at a consistent world scale
    private static Material MakeGroundMaterial(float worldSize, string name)
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return null;

        Material m = new Material(urpLit) { name = name };
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexturePath);
        if (tex != null)
        {
            float tiles = Mathf.Max(1f, worldSize / GroundMetersPerTile);
            m.SetTexture("_BaseMap", tex);
            m.SetTextureScale("_BaseMap", new Vector2(tiles, tiles));
            m.SetColor("_BaseColor", Color.white);
        }
        else
        {
            Debug.LogWarning("[Battlefield] Ground texture not found at " + GroundTexturePath + " - using plain gray.");
            m.SetColor("_BaseColor", new Color(0.28f, 0.30f, 0.32f));
        }
        // Low smoothness so the stone doesn't look wet
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
        return m;
    }

    private static void PlaceRing(GameObject[] prefabs, Transform root, System.Random rng, int count,
                                  float radius, float radiusJitter, float minH, float maxH,
                                  bool faceCenter, bool collider)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + (float)(rng.NextDouble() * 10.0 - 5.0);
            float r = radius + (float)(rng.NextDouble() * 2.0 - 1.0) * radiusJitter;
            Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * r;
            float yRot = faceCenter ? angle + 180f : angle;
            GameObject prefab = prefabs[rng.Next(prefabs.Length)];
            PlaceOne(prefab, root, pos, yRot, minH, maxH, collider);
        }
    }

    private static void Scatter(GameObject prefab, Transform root, System.Random rng, int count,
                                float minR, float maxR, float minH, float maxH, bool collider)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(rng.NextDouble() * 360.0);
            float r = minR + (float)rng.NextDouble() * (maxR - minR);
            Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * r;
            PlaceOne(prefab, root, pos, (float)(rng.NextDouble() * 360.0), minH, maxH, collider);
        }
    }

    private static void PlaceOne(GameObject prefab, Transform root, Vector3 pos, float yRot,
                                 float minH, float maxH, bool addCollider)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity; // measure axis-aligned first

        // Normalize wildly-scaled assets to a sensible height for this arena
        Bounds b = CalcBounds(go);
        if (b.size.y > 0.01f && (b.size.y < minH || b.size.y > maxH))
        {
            float target = Mathf.Clamp(b.size.y, minH, maxH);
            go.transform.localScale *= target / b.size.y;
            b = CalcBounds(go);
        }

        // Sit exactly on the ground
        go.transform.position += Vector3.up * (0f - b.min.y);
        b = CalcBounds(go);

        // Collider while still axis-aligned, so the box hugs the model. Building
        // colliders extend all the way up to the ceiling: rooftops are not a place
        // you can stand in this game - you slide along the wall instead.
        if (addCollider && go.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider bc = go.AddComponent<BoxCollider>();
            Vector3 ls = go.transform.lossyScale;
            bool isBuilding = b.size.y > 4f; // tall thing = building; cars/props keep snug boxes
            float worldTop = isBuilding ? CeilingHeight + 5f : b.size.y;
            Vector3 worldCenter = new Vector3(b.center.x, worldTop * 0.5f, b.center.z);
            bc.center = go.transform.InverseTransformPoint(worldCenter);
            bc.size = new Vector3(
                b.size.x / Mathf.Max(0.0001f, ls.x),
                worldTop / Mathf.Max(0.0001f, ls.y),
                b.size.z / Mathf.Max(0.0001f, ls.z));
        }

        // Final facing
        go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
    }

    private static void BuildBoundaryWalls(Transform root)
    {
        GameObject bounds = new GameObject("Arena Bounds");
        bounds.transform.SetParent(root, false);

        const int segments = 12;
        float segLength = 2f * Mathf.PI * WallRadius / segments + 2f; // slight overlap
        for (int i = 0; i < segments; i++)
        {
            float angle = (360f / segments) * (i + 0.5f);
            GameObject seg = new GameObject("Wall");
            seg.transform.SetParent(bounds.transform, false);
            seg.transform.position = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * WallRadius
                                     + Vector3.up * (WallHeight * 0.5f);
            seg.transform.rotation = Quaternion.Euler(0f, angle, 0f);
            BoxCollider bc = seg.AddComponent<BoxCollider>();
            bc.size = new Vector3(segLength, WallHeight, 1f);
        }

        // Invisible ceiling: the hard altitude limit. Rising just presses against it,
        // so neither mech (nor a knockdown launch) can ever leave over the walls.
        GameObject ceiling = new GameObject("Ceiling");
        ceiling.transform.SetParent(bounds.transform, false);
        ceiling.transform.position = new Vector3(0f, CeilingHeight + 2.5f, 0f);
        BoxCollider top = ceiling.AddComponent<BoxCollider>();
        top.size = new Vector3(WallRadius * 2f + 10f, 5f, WallRadius * 2f + 10f);
    }

    private static Bounds CalcBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    private static GameObject LoadPrefab(string relative)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(PackPath + "/" + relative);
    }

    // The Lite pack ships with built-in Standard materials, which render MAGENTA in
    // URP. This converts them in place (same fix Unity's own URP converter applies).
    private static void FixPackMaterialsForURP()
    {
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return; // not a URP project after all - nothing to do

        int fixedCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { PackPath }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;
            if (m.shader.name != "Standard") continue; // already URP (or something custom)

            Texture mainTex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;

            m.shader = urpLit;
            if (mainTex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", mainTex);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            fixedCount++;
        }

        if (fixedCount > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[Battlefield] Converted " + fixedCount + " city pack material(s) from Standard to URP Lit (they would have rendered magenta).");
        }
    }
}
