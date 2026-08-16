using UnityEditor;
using UnityEngine;

/// <summary>
/// Copies the three combat effect prefabs out of the Gabriel Aguiar
/// "Free Quick Effects Vol 1" pack into Assets/Resources/CombatVfx, where the
/// runtime CombatVfx spawner loads them from (Resources works in builds too).
///
///   vfx_Impact_01     -> hit.prefab    (every landed hit)
///   vfx_Shield_01     -> block.prefab  (shield absorbs a melee/shot)
///   vfx_Electricity_01-> parry.prefab  (stun on a parried attacker)
///
/// Runs by itself the next time scripts reload after the pack is imported.
/// Tools > Combat VFX > Setup Hit-Block-Parry Effects re-runs it manually.
/// Copies (not moves) - the pack folder stays untouched, and existing copies
/// are never overwritten, so you can safely swap a copied prefab for another
/// effect you like better.
/// </summary>
[InitializeOnLoad]
public static class CombatVfxSetup
{
    private const string PackPrefabDir = "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs";
    private const string ResourcesDir = "Assets/Resources";
    private const string TargetDir = "Assets/Resources/CombatVfx";

    private static readonly string[,] Mappings =
    {
        { "vfx_Impact_01.prefab",      "hit.prefab" },
        { "vfx_Shield_01.prefab",      "block.prefab" },
        { "vfx_Electricity_01.prefab", "parry.prefab" },
        { "vfx_Explosion_01.prefab",   "explosion.prefab" },
        { "vfx_MuzzleFlash_01.prefab", "muzzle.prefab" },
    };

    static CombatVfxSetup()
    {
        // delayCall: AssetDatabase is not safe to touch during the reload itself
        EditorApplication.delayCall += () => Run(false);
    }

    [MenuItem("Tools/Combat VFX/Setup Hit-Block-Parry Effects")]
    private static void RunFromMenu()
    {
        Run(true);
    }

    private static void Run(bool verbose)
    {
        // Pack not imported yet? Stay quiet on auto-runs; explain on manual runs.
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabDir + "/" + Mappings[0, 0]) == null)
        {
            if (verbose)
                Debug.LogWarning("[CombatVfxSetup] VFX pack not imported yet. Double-click " +
                                 "Assets/GabrielAguiarProductions/FreeQuickEffectsVol1_2022_URP_v1.0.unitypackage " +
                                 "in the Project window, click Import, then run this menu item again.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(ResourcesDir))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(TargetDir))
            AssetDatabase.CreateFolder(ResourcesDir, "CombatVfx");

        int copied = 0;
        for (int i = 0; i < Mappings.GetLength(0); i++)
        {
            string src = PackPrefabDir + "/" + Mappings[i, 0];
            string dst = TargetDir + "/" + Mappings[i, 1];

            if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null) continue; // already set up
            if (AssetDatabase.LoadAssetAtPath<GameObject>(src) == null)
            {
                Debug.LogWarning("[CombatVfxSetup] Missing in pack: " + src);
                continue;
            }
            if (AssetDatabase.CopyAsset(src, dst)) copied++;
            else Debug.LogWarning("[CombatVfxSetup] Copy failed: " + src + " -> " + dst);
        }

        if (copied > 0)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[CombatVfxSetup] Combat VFX ready - copied " + copied +
                      " effect prefab(s) into " + TargetDir + ". Hit, block and parry " +
                      "effects will now play in game.");
        }
        else if (verbose)
        {
            Debug.Log("[CombatVfxSetup] Already set up - all effect prefabs present in " + TargetDir + ".");
        }
    }
}
