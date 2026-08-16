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
    private const string ExplosionPath = "CombatVfx/explosion";
    private const string MuzzlePath = "CombatVfx/muzzle";

    // Tuning - tweak freely
    private const float HitScale = 1.3f;    // impact burst on every landed hit
    private const float BlockScale = 1.4f;  // shield flash when a hit is absorbed
    private const float ParryScale = 1.2f;  // electricity on the STUNNED attacker
    private const float HitLife = 2.5f;
    private const float BlockLife = 2.5f;
    private const float ParryLife = 0.6f;   // short zap - the stun itself lasts longer, but the effect should just punctuate it

    private static GameObject hitPrefab, blockPrefab, parryPrefab, explosionPrefab, muzzlePrefab;
    private static bool hitLoaded, blockLoaded, parryLoaded, explosionLoaded, muzzleLoaded; // tried loading yet?

    /// <summary>Impact burst on a mech that just took damage (melee or shot).</summary>
    public static void SpawnHit(Vector3 position)
    {
        BattleAudio.Play("hit", 0.9f);
        // Procedural sparks ALWAYS fire - hits read even if the VFX pack was never set up
        ProceduralVfx.Sparks(position, new Color(1f, 0.75f, 0.3f), 18, 8f);
        ProceduralVfx.FlashLight(position, new Color(1f, 0.7f, 0.3f), 2.2f, 5f, 0.1f);
        Spawn(ref hitPrefab, ref hitLoaded, HitPath, position, null, HitScale, HitLife);
    }

    /// <summary>Shield flash when a raised guard absorbs a melee or a shot.</summary>
    public static void SpawnBlock(Vector3 position)
    {
        BattleAudio.Play("block", 0.85f);
        ProceduralVfx.Sparks(position, new Color(0.5f, 0.85f, 1f), 14, 5f, 0.4f, 0.12f, 0.2f);
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
        BattleAudio.Play("parry", 0.9f);
        ProceduralVfx.Sparks(pos, new Color(0.55f, 0.75f, 1f), 22, 6.5f, 0.5f, 0.1f, 0.1f);
        Spawn(ref parryPrefab, ref parryLoaded, ParryPath, pos, stunnedAttacker, ParryScale, ParryLife);
    }

    /// <summary>Muzzle flash at the gun when a shot fires. Parented so it rides the barrel.</summary>
    public static void SpawnMuzzleFlash(Transform attachTo, Vector3 position)
    {
        BattleAudio.Play("shot", 0.6f);
        ProceduralVfx.MuzzlePop(position);
        Spawn(ref muzzlePrefab, ref muzzleLoaded, MuzzlePath, position, attachTo, 0.7f, 0.7f);
    }

    /// <summary>Big boom - building collapses, gerobi laser impacts.</summary>
    public static void SpawnExplosion(Vector3 position)
    {
        BattleAudio.Play("explosion", 1f);
        // Full procedural explosion (fireball + smoke + sparks + shockwave + light)
        // layered under the pack prefab - together they finally look like a boom.
        ProceduralVfx.Fireball(position, 1.2f);
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.18f, 0.3f);
        Spawn(ref explosionPrefab, ref explosionLoaded, ExplosionPath, position, null, 2.0f, 3.5f);
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
