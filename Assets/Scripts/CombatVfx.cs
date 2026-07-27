using UnityEngine;

/// <summary>
/// Central spawner for the three combat effects: getting hit, blocking a shot or
/// melee with the shield, and the parry stun on a blocked attacker.
///
/// The prefabs are loaded from Resources/CombatVfx (hit / block / parry).
/// CombatVfxSetup (an editor script) copies them there automatically from the
/// Gabriel Aguiar "Free Quick Effects Vol 1" pack once its URP .unitypackage has
/// been imported - no manual wiring. Everything degrades gracefully: a missing
/// prefab just skips that effect and prints ONE console hint instead of erroring
/// every hit.
/// </summary>
public static class CombatVfx
{
    // Resources paths (CombatVfxSetup writes the prefabs here)
    private const string HitPath = "CombatVfx/hit";
    private const string BlockPath = "CombatVfx/block";
    private const string ParryPath = "CombatVfx/parry";

    // Tuning - tweak freely
    private const float HitScale = 1.3f;    // impact burst on every landed hit
    private const float BlockScale = 1.4f;  // shield flash when a hit is absorbed
    private const float ParryScale = 1.2f;  // electricity on the STUNNED attacker
    private const float HitLife = 2.5f;
    private const float BlockLife = 2.5f;
    private const float ParryLife = 0.6f;   // short zap - the stun itself lasts longer, but the effect should just punctuate it

    private static GameObject hitPrefab, blockPrefab, parryPrefab;
    private static bool hitLoaded, blockLoaded, parryLoaded; // tried loading yet?

    /// <summary>Impact burst on a mech that just took damage (melee or shot).</summary>
    public static void SpawnHit(Vector3 position)
    {
        Spawn(ref hitPrefab, ref hitLoaded, HitPath, position, null, HitScale, HitLife);
    }

    /// <summary>Shield flash when a raised guard absorbs a melee or a shot.</summary>
    public static void SpawnBlock(Vector3 position)
    {
        Spawn(ref blockPrefab, ref blockLoaded, BlockPath, position, null, BlockScale, BlockLife);
    }

    /// <summary>
    /// Electricity on the ATTACKER who just got parried - attached so it follows
    /// them through the stun, making the punish window obvious.
    /// </summary>
    public static void SpawnParry(Transform stunnedAttacker)
    {
        Vector3 pos = stunnedAttacker != null
            ? stunnedAttacker.position + Vector3.up * 1.2f
            : Vector3.zero;
        Spawn(ref parryPrefab, ref parryLoaded, ParryPath, pos, stunnedAttacker, ParryScale, ParryLife);
    }

    private static void Spawn(ref GameObject prefab, ref bool loaded, string path,
                              Vector3 position, Transform parent, float scale, float life)
    {
        if (!loaded)
        {
            loaded = true;
            prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
                Debug.LogWarning("[CombatVfx] No prefab at Resources/" + path +
                                 ". Import FreeQuickEffectsVol1_2022_URP_v1.0.unitypackage " +
                                 "(double-click it in the Project window), then run " +
                                 "Tools > Combat VFX > Setup Hit-Block-Parry Effects.");
        }
        if (prefab == null) return;

        GameObject fx = Object.Instantiate(prefab, position, Quaternion.identity, parent);
        fx.transform.localScale *= scale;
        Object.Destroy(fx, life); // hard cap - also kills looping effects on time
    }
}
