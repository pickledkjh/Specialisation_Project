using UnityEngine;
using UnityEditor;

/// <summary>
/// Copies the imported Homing Missile pack's prefabs into Assets/Resources so
/// runtime code can load them. The pack itself stays where it was imported - these
/// are copies, so re-importing or moving the original folder cannot break the game.
///
/// Runs AUTOMATICALLY on editor load when the copies are missing, because a manual
/// menu step that nobody remembers to click is the same as a feature that does not
/// work: the R barrage silently fell back to its old orbs the whole time.
/// Still available from the menu to force a refresh.
/// </summary>
public static class MissilePackSetup
{
    private const string OutDir = "Assets/Resources/MissilePack";

    // Where the pack normally lands, plus a project-wide search by name as a
    // fallback, so renaming or moving the pack folder cannot break this.
    private const string PackRoot = "Assets/homing missile";
    private const string PreferredDir = "Assets/homing missile/prefabs/";

    private static readonly string[] Wanted =
    {
        "Missil_05",              // the missile model
        "rocket_smoke",           // exhaust trail
        "rocket_destroy_effect",  // impact explosion
    };

    [InitializeOnLoadMethod]
    private static void AutoHookOnLoad()
    {
        // Only act when something is actually missing - never fight the user on
        // every domain reload.
        foreach (string name in Wanted)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(OutDir + "/" + name + ".prefab") == null)
            {
                // Delay: AssetDatabase is not reliably ready during InitializeOnLoad
                EditorApplication.delayCall += () => HookUp(silentIfMissing: true);
                return;
            }
        }

        // Copies already exist - but a REIMPORT of the pack resets its materials back
        // to the built-in shader, so always re-check those.
        EditorApplication.delayCall += () =>
        {
            int n = FixPackMaterialsForURP();
            if (n > 0) Debug.Log("[Missile] re-converted " + n + " pack material(s) to URP after a reimport.");
        };
    }

    [MenuItem("Tools/Gundam/10. Hook Up Missile Pack")]
    public static void HookUpMenu() { HookUp(silentIfMissing: false); }

    /// <summary>The pack ships with BUILT-IN (Standard) shader materials. This project
    /// is URP, where those render untextured - which is why the missile had no texture
    /// even after a clean reimport. Converts them in place, carrying the albedo across
    /// from _MainTex to _BaseMap. Particle/additive materials go to the URP particle
    /// shader instead so smoke and explosions keep blending correctly.</summary>
    private static int FixPackMaterialsForURP()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Shader particle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (lit == null) return 0;
        if (!AssetDatabase.IsValidFolder(PackRoot)) return 0;

        int fixedCount = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { PackRoot }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;

            string sn = m.shader.name;
            if (sn.StartsWith("Universal Render Pipeline")) continue; // already converted

            Texture main = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
            Color col = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;

            bool isParticle = sn.Contains("Particle") || sn.Contains("Additive") ||
                              sn.Contains("Transparent") || sn.Contains("Sprites");
            m.shader = (isParticle && particle != null) ? particle : lit;

            if (main != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", main);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
            if (bump != null && m.HasProperty("_BumpMap"))
            {
                m.SetTexture("_BumpMap", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            EditorUtility.SetDirty(m);
            fixedCount++;
        }
        if (fixedCount > 0) AssetDatabase.SaveAssets();
        return fixedCount;
    }

    private static void HookUp(bool silentIfMissing)
    {
        int urpFixed = FixPackMaterialsForURP();
        if (urpFixed > 0)
            Debug.Log("[Missile] converted " + urpFixed + " pack material(s) from the built-in Standard " +
                      "shader to URP - that is why the missile had no texture.");

        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(OutDir))
            AssetDatabase.CreateFolder("Assets/Resources", "MissilePack");

        int copied = 0, missing = 0;
        var log = new System.Text.StringBuilder("[Missile] pack hook-up\n");

        foreach (string name in Wanted)
        {
            string src = FindPrefab(name);
            string dst = OutDir + "/" + name + ".prefab";

            if (src == null)
            {
                log.Append("  MISSING: no prefab named '").Append(name).Append("' anywhere in the project\n");
                missing++;
                continue;
            }
            if (AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null)
                AssetDatabase.DeleteAsset(dst); // refresh on re-run

            if (AssetDatabase.CopyAsset(src, dst))
            {
                copied++;
                log.Append("  ").Append(src).Append("  ->  ").Append(dst).Append('\n');
            }
            else
            {
                log.Append("  COPY FAILED: ").Append(src).Append('\n');
                missing++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        MissileAssets.Reload(); // drop the runtime cache so this session sees the new copies

        if (missing > 0 && silentIfMissing && copied == 0) return; // no pack installed: stay quiet

        log.Append("  ").Append(copied).Append(" copied, ").Append(missing).Append(" missing.");
        if (copied > 0)
            log.Append("\n  The R barrage now fires the pack's missile model with exhaust smoke and its explosion on impact. ENTER PLAY MODE AGAIN to pick them up.");
        Debug.Log(log.ToString());
    }

    /// <summary>Preferred path first, then a project-wide search by exact name.</summary>
    private static string FindPrefab(string name)
    {
        string preferred = PreferredDir + name + ".prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(preferred) != null) return preferred;

        foreach (string guid in AssetDatabase.FindAssets(name + " t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (path.StartsWith(OutDir)) continue; // don't copy our own copy
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return path;
        }
        return null;
    }
}
