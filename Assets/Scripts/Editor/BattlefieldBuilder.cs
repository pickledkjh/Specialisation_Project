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

    // No global empty circle any more - the city now fills the middle too, and
    // only small pockets around the actual mech spawns are kept clear.
    private const float WallRadius = 160f;    // invisible boundary (matches ArenaLimits.Radius) - between the cramped 95 and the too-hikey 285
    private const float CeilingHeight = 70f;  // hard altitude limit (matches ArenaLimits.Ceiling)
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

        occupied.Clear(); // fresh overlap map for this build
        placedCount = 0;

        BuildGround(root.transform);

        GameObject[] houses = house1 != null && house2 != null
            ? new[] { house1, house2 }
            : new[] { house1 ?? house2 };

        // Every building inside the walls is BREAKABLE - cover you can destroy
        // (and that explodes on whoever stands next to it).

        // Where the mechs actually stand. Only these small pockets stay clear now:
        // the old 30u empty circle in the middle was what made the map read as an
        // "arena" instead of a city, and it kept the best feature - buildings
        // exploding - out of the exact spot where most fighting happens.
        CollectSpawnPoints();

        // Rings first: this is the DESIGNED cover, so it wins any contest for space.
        // The city-block grid then fills in around whatever the rings claimed.

        // Inner cover ring - big enough gaps to dash through
        PlaceRing(houses, root.transform, rng, count: 30, radius: 62f, radiusJitter: 8f,
                  minH: 6f, maxH: 14f, faceCenter: true, collider: true, breakable: true);

        // Middle ring - cover during ranged play
        PlaceRing(houses, root.transform, rng, count: 38, radius: 108f, radiusJitter: 10f,
                  minH: 8f, maxH: 18f, faceCenter: true, collider: true, breakable: true);

        // Wall-line ring just inside the boundary - the arena's "city wall"
        PlaceRing(houses, root.transform, rng, count: 42, radius: 148f, radiusJitter: 6f,
                  minH: 10f, maxH: 22f, faceCenter: true, collider: true, breakable: true);

        // Backdrop rings beyond the walls - pure scenery for depth
        PlaceRing(houses, root.transform, rng, count: 38, radius: 190f, radiusJitter: 14f,
                  minH: 12f, maxH: 24f, faceCenter: true, collider: false);
        PlaceRing(houses, root.transform, rng, count: 32, radius: 235f, radiusJitter: 18f,
                  minH: 18f, maxH: 34f, faceCenter: true, collider: false);

        // Far SKYLINE - tall towers on the horizon so the city has no visible edge
        PlaceRing(houses, root.transform, rng, count: 30, radius: 292f, radiusJitter: 22f,
                  minH: 26f, maxH: 48f, faceCenter: true, collider: false);
        PlaceRing(houses, root.transform, rng, count: 26, radius: 355f, radiusJitter: 26f,
                  minH: 34f, maxH: 60f, faceCenter: true, collider: false);

        // ---- CITY BLOCKS: fills the whole arena floor, CENTRE INCLUDED ----
        // A jittered GRID rather than pure random scatter. Random scatter clumps and
        // leaves bald patches; a grid with street gaps reads as a real city, spreads
        // cover evenly, and guarantees lanes to dash down. Heights ramp outward, so
        // the buildings you fight among at spawn are short enough to see over while
        // the outskirts build a skyline.
        BuildCityBlocks(houses, root.transform, rng);

        // Street dressing
        if (lightPole != null)
        {
            PlaceRing(new[] { lightPole }, root.transform, rng, count: 16, radius: 34f, radiusJitter: 2f,
                      minH: 3.5f, maxH: 6f, faceCenter: false, collider: false);
            PlaceRing(new[] { lightPole }, root.transform, rng, count: 22, radius: 90f, radiusJitter: 6f,
                      minH: 3.5f, maxH: 6f, faceCenter: false, collider: false);
        }
        if (car != null)
            Scatter(car, root.transform, rng, count: 46, minR: 12f, maxR: 150f, minH: 1.2f, maxH: 2.2f, collider: true);
        if (trash != null)
            Scatter(trash, root.transform, rng, count: 44, minR: 12f, maxR: 150f, minH: 0.6f, maxH: 1.5f, collider: false);

        BuildBoundaryWalls(root.transform);
        SetupLightingAndSky();

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Battlefield] Built: " + placedCount + " structures (city fills the centre, " +
                  "only " + SpawnClearRadius + "u spawn pockets left clear), walls at " + WallRadius + "u, skyline out to 355u. Lighting + procedural sky applied. " +
                  "Re-run to regenerate, Tools -> Battlefield -> Remove to delete. SAVE THE SCENE to keep it.");
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

    // ---------------- overlap map ----------------
    // Buildings that intersect each other look like a bug, and they trap the mechs.
    // Every placement registers a footprint circle here and later ones test against
    // it, so the city can be dense without pieces growing through one another.
    private static readonly List<Vector3> occupied = new List<Vector3>(); // x, z, radius
    private static int placedCount;

    // Separate stream so height variation never shifts the layout RNG - the seed
    // still produces the same street plan run to run.
    private static readonly System.Random heightRng = new System.Random(Seed ^ 0x5F3A);

    private static bool Fits(Vector3 pos, float radius)
    {
        for (int i = 0; i < occupied.Count; i++)
        {
            float dx = occupied[i].x - pos.x;
            float dz = occupied[i].y - pos.z;
            float rr = occupied[i].z + radius;
            if (dx * dx + dz * dz < rr * rr) return false;
        }
        return true;
    }

    private static void Occupy(Vector3 pos, float radius)
    {
        occupied.Add(new Vector3(pos.x, pos.z, radius));
    }

    // ---------------- city blocks ----------------

    /// <summary>Jittered grid of buildings across the whole arena floor, with street
    /// gaps and occasional empty plazas. Heights ramp outward: low cover you can see
    /// and dash over near the middle, proper buildings toward the walls.</summary>
    private static void BuildCityBlocks(GameObject[] prefabs, Transform root, System.Random rng)
    {
        const float spacing = 19f;      // block pitch - the "street" grid
        const float jitter = 4.8f;      // how far a building may wander in its cell
        const float fillChance = 0.86f; // the rest become plazas / open lanes
        float limit = WallRadius - 12f;

        for (float x = -limit; x <= limit; x += spacing)
        {
            for (float z = -limit; z <= limit; z += spacing)
            {
                Vector3 pos = new Vector3(
                    x + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter,
                    0f,
                    z + (float)(rng.NextDouble() * 2.0 - 1.0) * jitter);

                float dist = new Vector2(pos.x, pos.z).magnitude;
                if (dist > limit) continue;                  // outside the walls
                if (!ClearOfSpawns(pos)) continue;           // don't bury a mech at round start
                if (rng.NextDouble() > fillChance) continue; // plaza

                // Height ramps outward from the middle of the map: low, smashable
                // blocks where the opening fight happens (you can see and shoot over
                // them), proper buildings out toward the walls.
                float k = Mathf.InverseLerp(0f, limit, dist);
                float minH = Mathf.Lerp(3f, 8f, k);
                float maxH = Mathf.Lerp(7f, 20f, k);

                GameObject prefab = prefabs[rng.Next(prefabs.Length)];
                PlaceOne(prefab, root, pos, (float)(rng.NextDouble() * 360.0), minH, maxH,
                         addCollider: true, breakable: true);
            }
        }
    }

    // ---------------- spawn pockets ----------------
    // The only places kept empty. Read straight off the scene, so moving a mech in
    // the editor and rebuilding just works.
    private static readonly List<Vector2> spawnPoints = new List<Vector2>();
    private const float SpawnClearRadius = 16f;

    private static void CollectSpawnPoints()
    {
        spawnPoints.Clear();
        MechController player = Object.FindFirstObjectByType<MechController>();
        if (player != null) spawnPoints.Add(new Vector2(player.transform.position.x, player.transform.position.z));
        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        if (enemy != null) spawnPoints.Add(new Vector2(enemy.transform.position.x, enemy.transform.position.z));
        if (spawnPoints.Count == 0) spawnPoints.Add(Vector2.zero); // no mechs in scene: keep the origin clear
        Debug.Log("[Battlefield] " + spawnPoints.Count + " spawn pocket(s) kept clear at radius " + SpawnClearRadius + "u.");
    }

    private static bool ClearOfSpawns(Vector3 pos)
    {
        Vector2 p = new Vector2(pos.x, pos.z);
        for (int i = 0; i < spawnPoints.Count; i++)
            if (Vector2.Distance(spawnPoints[i], p) < SpawnClearRadius) return false;
        return true;
    }

    // ---------------- lighting + sky ----------------

    /// <summary>Dusk-city lighting: warm raking key light, a cool sky-lit ambient
    /// ramp, distance fog that dissolves the skyline into the horizon, and a
    /// procedural sky tuned to match. Runs as part of the build, or on its own from
    /// the menu when you only want to re-grade the look.</summary>
    [MenuItem("Tools/Battlefield/Lighting + Skybox Only")]
    public static void SetupLightingAndSky()
    {
        // ---- procedural sky ----
        const string matDir = "Assets/Materials";
        const string skyPath = matDir + "/ArenaSky.mat";
        if (!AssetDatabase.IsValidFolder(matDir)) AssetDatabase.CreateFolder("Assets", "Materials");

        Material sky = AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        Shader skyShader = Shader.Find("Skybox/Procedural");
        if (sky == null && skyShader != null)
        {
            sky = new Material(skyShader);
            AssetDatabase.CreateAsset(sky, skyPath);
        }
        if (sky != null)
        {
            if (sky.HasProperty("_SunDisk")) sky.SetFloat("_SunDisk", 2f);         // high-quality disk
            if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.045f);
            if (sky.HasProperty("_SunSizeConvergence")) sky.SetFloat("_SunSizeConvergence", 6f);
            if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 1.35f); // hazier = more depth
            if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.52f, 0.60f, 0.78f));
            if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.28f, 0.27f, 0.29f));
            if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.25f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
        }

        // ---- key light ----
        Light key = null;
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { key = l; break; }
        if (key == null)
        {
            GameObject go = new GameObject("Directional Light");
            Undo.RegisterCreatedObjectUndo(go, "Arena key light");
            key = go.AddComponent<Light>();
            key.type = LightType.Directional;
        }
        Undo.RecordObject(key, "Arena lighting");
        Undo.RecordObject(key.transform, "Arena lighting");
        // Low raking angle: long shadows across the streets read the arena's depth
        // far better than a noon light, and it makes the mechs pop against the road.
        key.transform.rotation = Quaternion.Euler(38f, -132f, 0f);
        key.color = new Color(1f, 0.94f, 0.84f);
        key.intensity = 1.45f;
        key.shadows = LightShadows.Soft;
        key.shadowStrength = 0.78f;
        key.shadowBias = 0.04f;
        key.shadowNormalBias = 0.35f;
        RenderSettings.sun = key;

        // ---- ambient ----
        // Trilight instead of flat: cool sky bounce on the tops, warm road bounce
        // underneath. Costs nothing and stops the shadow sides going dead black.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.44f, 0.52f, 0.68f);
        RenderSettings.ambientEquatorColor = new Color(0.36f, 0.37f, 0.40f);
        RenderSettings.ambientGroundColor = new Color(0.22f, 0.20f, 0.19f);
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.85f;

        // ---- distance fog ----
        // The single biggest look upgrade: the skyline rings fade into the sky
        // instead of ending in a hard ring of geometry, and the arena gains scale.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.62f, 0.68f, 0.79f);
        RenderSettings.fogDensity = 0.0022f;

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Battlefield] Lighting + sky applied: raking warm key light with soft shadows, " +
                  "trilight ambient, exponential-squared fog, procedural skybox at " + skyPath + ". Save the scene.");
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
        ground.transform.localScale = new Vector3(55f, 1f, 55f); // 550 x 550 - covers the arena and the skyline

        // Stone ground from the imported texture pack; plain gray fallback if the
        // texture is missing. The center Plane gets the same look so it all matches.
        Material plateMat = MakeGroundMaterial(550f, "Stone Ground (wide)");
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
                                  bool faceCenter, bool collider, bool breakable = false)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (360f / count) * i + (float)(rng.NextDouble() * 10.0 - 5.0);
            float r = radius + (float)(rng.NextDouble() * 2.0 - 1.0) * radiusJitter;
            Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * r;
            float yRot = faceCenter ? angle + 180f : angle;
            GameObject prefab = prefabs[rng.Next(prefabs.Length)];
            PlaceOne(prefab, root, pos, yRot, minH, maxH, collider, breakable);
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

    // Random scatter that mixes prefabs and supports breakable - used for the
    // short in-arena buildings.
    private static void Scatter2(GameObject[] prefabs, Transform root, System.Random rng, int count,
                                 float minR, float maxR, float minH, float maxH, bool collider, bool breakable)
    {
        for (int i = 0; i < count; i++)
        {
            float angle = (float)(rng.NextDouble() * 360.0);
            float r = minR + (float)rng.NextDouble() * (maxR - minR);
            Vector3 pos = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * r;
            GameObject prefab = prefabs[rng.Next(prefabs.Length)];
            PlaceOne(prefab, root, pos, (float)(rng.NextDouble() * 360.0), minH, maxH, collider, breakable);
        }
    }

    private static void PlaceOne(GameObject prefab, Transform root, Vector3 pos, float yRot,
                                 float minH, float maxH, bool addCollider, bool breakable = false)
    {
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.transform.SetParent(root, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.identity; // measure axis-aligned first

        // Normalize wildly-scaled assets to a sensible height for this arena.
        // Random height inside the band instead of only clamping - a street where
        // every roof is the same height is the giveaway that it was generated.
        Bounds b = CalcBounds(go);
        if (b.size.y > 0.01f)
        {
            float target = Mathf.Lerp(minH, maxH, (float)heightRng.NextDouble());
            go.transform.localScale *= target / b.size.y;
            b = CalcBounds(go);
        }

        // Sit exactly on the ground
        go.transform.position += Vector3.up * (0f - b.min.y);
        b = CalcBounds(go);

        // Footprint test - anything that would grow through something already placed
        // is discarded rather than left interpenetrating. Applies to EVERYTHING
        // inside the walls (props included, with tighter padding); the backdrop
        // rings outside are free to overlap since nothing ever touches them.
        Vector3 wp = go.transform.position;
        bool insideArena = new Vector2(wp.x, wp.z).magnitude < WallRadius + 4f;
        if (insideArena)
        {
            float footprint = Mathf.Max(b.size.x, b.size.z) * 0.5f + (breakable ? 1.2f : 0.35f);
            if (!Fits(wp, footprint) || !ClearOfSpawns(wp))
            {
                Object.DestroyImmediate(go);
                return;
            }
            Occupy(wp, footprint);
        }
        placedCount++;

        // Collider while still axis-aligned, so the box hugs the model.
        // ROOFTOPS ARE LANDABLE: the box now matches the real building height
        // (the old version extended it to the ceiling as an un-standable wall).
        // Together with the any-surface ground check, landing on a roof is a
        // real landing - recovery plays and boost recharges up there.
        if (addCollider && go.GetComponentInChildren<Collider>() == null)
        {
            BoxCollider bc = go.AddComponent<BoxCollider>();
            Vector3 ls = go.transform.lossyScale;
            Vector3 worldCenter = new Vector3(b.center.x, b.size.y * 0.5f, b.center.z);
            bc.center = go.transform.InverseTransformPoint(worldCenter);
            bc.size = new Vector3(
                b.size.x / Mathf.Max(0.0001f, ls.x),
                b.size.y / Mathf.Max(0.0001f, ls.y),
                b.size.z / Mathf.Max(0.0001f, ls.z));
        }

        // Destructible cover. Threshold lowered from 4u: the short blocks that now
        // fill the middle of the map are exactly the ones players will smash, and
        // at 4u half of them were spawning indestructible.
        if (breakable && b.size.y > 2.5f && go.GetComponent<BreakableBuilding>() == null)
            go.AddComponent<BreakableBuilding>();

        // Final facing
        go.transform.rotation = Quaternion.Euler(0f, yRot, 0f);
    }

    private static void BuildBoundaryWalls(Transform root)
    {
        GameObject bounds = new GameObject("Arena Bounds");
        bounds.transform.SetParent(root, false);

        const int segments = 24;
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
