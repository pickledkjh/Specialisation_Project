using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click swap of the player's Y Bot model for the Origin Gundam.
///
///   Tools > Gundam > 1. Swap Player Model To Gundam
///   Tools > Gundam > Rotate Gundam 180 (if it faces backwards)
///   Tools > Gundam > Scale Gundam +10% / -10%
///
/// Prerequisite: the Gundam FBX must import as HUMANOID - its .meta ships with a
/// full bone mapping (MMD-style Japanese bones -> Unity human bones), so this is
/// automatic after the meta update. If the avatar failed to build, this tool says
/// so instead of half-swapping.
///
/// What the swap does:
///  - instantiates the Gundam under the player root, scaled to the Y Bot's height
///  - moves the Animator's job to the Gundam (Player.controller, humanoid retarget)
///  - adds an AnimationEventRelay so punch clip events still reach MechCombat
///  - re-parents the fist/foot melee hitboxes onto the Gundam's hands and foot
///  - re-aims MechShooter at the Gundam's chest/arm bones, muzzle on the rifle bone
///  - deactivates the old Y Bot visuals
/// </summary>
public static class GundamSetup
{
    private const string FbxPath =
        "Assets/Model/the-origingundamthe-origin-ver/source/オリジンガンダム(2023-03-20)可動版.fbx";
    private const string GundamName = "Gundam Model";

    /// <summary>
    /// Everything, in the one correct order, safe to re-run any time:
    /// bake T-pose -> reimport -> fresh swap -> unpack -> mount weapons ->
    /// move hitboxes -> retire old model -> apply URP materials.
    /// </summary>
    [MenuItem("Tools/Gundam/0. FULL SETUP (run this one)")]
    public static void FullSetup()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        if (player == null) { Debug.LogError("[Gundam] No MechController in the scene."); return; }

        // 1. Reference T-pose baked into the rig, reimport happens inside
        BakeTPose();

        // 2. Fresh instance every time - stale half-configured ones cause the weird states
        Transform existing = player.transform.Find(GundamName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        Swap(player.gameObject);

        Transform g = player.transform.Find(GundamName);
        if (g == null) { Debug.LogError("[Gundam] Swap failed - see errors above."); return; }

        // 3. Unpack NOW so every later restructuring step is legal
        if (PrefabUtility.IsPartOfPrefabInstance(g.gameObject))
            PrefabUtility.UnpackPrefabInstance(g.gameObject, PrefabUnpackMode.Completely, InteractionMode.UserAction);

        // 4. Weapons onto real mounts, hitboxes onto hands, old model retired, paint
        AttachWeapons();
        FixHitboxes();
        FixMaterials();

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[Gundam] FULL SETUP DONE. Press Play and test: idle, run, melee string, shoot. " +
                  "Weapon orientation fine-tune: select ライフル / シールド under the hand/forearm bones " +
                  "and rotate in the Scene view - it saves with the scene. If the Gundam faces backwards: " +
                  "Tools > Gundam > Rotate Gundam 180.");
    }

    [MenuItem("Tools/Gundam/1. Swap Player Model To Gundam")]
    public static void SwapPlayer()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        if (player == null) { Debug.LogError("[Gundam] No MechController found in the scene."); return; }
        Swap(player.gameObject);
    }

    private static void Swap(GameObject root)
    {
        if (root.transform.Find(GundamName) != null)
        {
            Debug.LogWarning("[Gundam] Player already has a Gundam model - nothing to do.");
            return;
        }

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError("[Gundam] FBX not found at " + FbxPath); return; }

        // The avatar must be a valid humanoid for the Mixamo clips to retarget
        Animator fbxAnim = fbx.GetComponent<Animator>();
        if (fbxAnim == null || fbxAnim.avatar == null || !fbxAnim.avatar.isValid || !fbxAnim.avatar.isHuman)
        {
            Debug.LogError("[Gundam] The Gundam did not import as a valid HUMANOID. " +
                           "Select the FBX -> Inspector -> Rig -> Animation Type: Humanoid -> Apply, then rerun. " +
                           "(The bone mapping is already in the .meta - a Reimport usually fixes this.)");
            return;
        }

        // ---- old setup ----
        Animator oldAnim = root.GetComponentInChildren<Animator>();
        RuntimeAnimatorController controller = oldAnim != null ? oldAnim.runtimeAnimatorController : null;
        if (controller == null)
            controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Animation/Player.controller");

        float targetHeight = 1.9f;
        GameObject oldModelGo = null;
        if (oldAnim != null)
        {
            Bounds ob = RenderBounds(oldAnim.gameObject);
            if (ob.size.y > 0.1f) targetHeight = ob.size.y;
            if (oldAnim.gameObject != root) oldModelGo = oldAnim.gameObject;
        }

        // ---- instantiate + scale ----
        GameObject gundam = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        Undo.RegisterCreatedObjectUndo(gundam, "Gundam swap");
        gundam.name = GundamName;
        gundam.transform.SetParent(root.transform, false);
        gundam.transform.localPosition = Vector3.zero;
        gundam.transform.localRotation = Quaternion.identity;

        Bounds nb = RenderBounds(gundam);
        if (nb.size.y > 0.01f)
        {
            float k = targetHeight / nb.size.y;
            gundam.transform.localScale = Vector3.one * k;
            // Feet on the floor: lift so the renderer bottom sits at the root's y
            Bounds sb = RenderBounds(gundam);
            float lift = root.transform.position.y - sb.min.y;
            gundam.transform.localPosition = new Vector3(0f, gundam.transform.localPosition.y + lift, 0f);
        }

        // ---- animator ----
        Animator newAnim = gundam.GetComponent<Animator>();
        if (newAnim == null) newAnim = gundam.AddComponent<Animator>();
        newAnim.runtimeAnimatorController = controller;
        newAnim.applyRootMotion = false; // locomotion is fully code-driven
        newAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (gundam.GetComponent<AnimationEventRelay>() == null)
            gundam.AddComponent<AnimationEventRelay>();

        // ---- retire the old model BEFORE rewiring (so GetComponentInChildren finds the Gundam) ----
        if (oldModelGo != null)
        {
            oldModelGo.SetActive(false);
            oldModelGo.name += " (replaced by Gundam)";
        }
        else if (oldAnim != null && oldAnim.gameObject == root)
        {
            // Animator lived on the root itself: remove it so nothing double-animates
            Object.DestroyImmediate(oldAnim);
        }
        HideOldRenderers(root, gundam.transform);

        // ---- rewire references ----
        MechController mc = root.GetComponent<MechController>();
        if (mc != null) mc.animator = newAnim;

        MechCombat combat = root.GetComponent<MechCombat>();
        Transform rHand = newAnim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lHand = newAnim.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform lFoot = newAnim.GetBoneTransform(HumanBodyBones.LeftFoot);
        if (combat != null)
        {
            MoveHitbox(combat.rightFistCollider, rHand);
            MoveHitbox(combat.leftFistCollider, lHand);
            MoveHitbox(combat.leftFootCollider, lFoot);
        }

        MechShooter shooter = root.GetComponent<MechShooter>();
        if (shooter != null)
        {
            Transform chest = newAnim.GetBoneTransform(HumanBodyBones.UpperChest);
            if (chest == null) chest = newAnim.GetBoneTransform(HumanBodyBones.Chest);
            shooter.spineBone = chest;
            shooter.armBone = newAnim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            shooter.forearmBone = newAnim.GetBoneTransform(HumanBodyBones.RightLowerArm);

            // Muzzle on the rifle bone if the model has one, else the right hand
            Transform rifle = FindDeep(gundam.transform, "ライフル"); // ライフル
            Transform muzzleParent = rifle != null ? rifle : rHand;
            if (muzzleParent != null)
            {
                Transform muzzle = muzzleParent.Find("Muzzle");
                if (muzzle == null)
                {
                    muzzle = new GameObject("Muzzle").transform;
                    muzzle.SetParent(muzzleParent, false);
                    muzzle.localPosition = Vector3.zero;
                }
                shooter.muzzle = muzzle;
            }
        }

        EditorUtility.SetDirty(root);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);

        Debug.Log("[Gundam] Swap complete. CHECK: 1) Play - if the Gundam faces BACKWARDS use " +
                  "Tools > Gundam > Rotate Gundam 180. 2) If limbs look twisted, select the FBX -> Rig -> " +
                  "Configure -> Pose dropdown -> Enforce T-Pose -> Apply. 3) Size wrong? Use the Scale menu items. " +
                  "Rifle bone " + (FindDeep(gundam.transform, "ライフル") != null ? "FOUND - muzzle mounted on it." : "not found - muzzle on right hand."));
    }

    private static void MoveHitbox(Collider col, Transform newParent)
    {
        if (col == null || newParent == null) return;
        Undo.SetTransformParent(col.transform, newParent, "Gundam hitbox move");
        col.transform.localPosition = Vector3.zero;
        col.transform.localRotation = Quaternion.identity;
    }

    private static Bounds RenderBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(go.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform f = FindDeep(t.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }

    [MenuItem("Tools/Gundam/2. Fix Gundam Materials (URP + texture)")]
    public static void FixMaterials()
    {
        const string texDir = "Assets/Model/the-origingundamthe-origin-ver/textures/";
        Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "変換_～_GUNDAM.png");
        Texture2D normal  = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "変換_～_GUNDAM-N.png");
        Texture2D ao      = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "変換_～_GUNDAM-AO.png");
        if (baseMap == null)
        {
            Debug.LogError("[Gundam] Texture atlas not found under " + texDir);
            return;
        }

        // The normal map must be imported AS a normal map
        if (normal != null)
        {
            string nPath = AssetDatabase.GetAssetPath(normal);
            TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(nPath);
            if (ti != null && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.SaveAndReimport();
            }
        }

        const string matPath = "Assets/Model/the-origingundamthe-origin-ver/GundamURP.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetTexture("_BaseMap", baseMap);
        mat.color = Color.white;
        if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
        if (ao != null) { mat.SetTexture("_OcclusionMap", ao); mat.EnableKeyword("_OCCLUSIONMAP"); }
        mat.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        Transform g = FindGundam();
        if (g == null) return;
        int count = 0;
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            count++;
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
        Debug.Log("[Gundam] Applied the URP atlas material to " + count + " renderer(s). " +
                  "If the colours look flat, the atlas texture may need sRGB on (default).");
    }

    [MenuItem("Tools/Gundam/3. Attach Weapons To Body (fix floating shield-rifle)")]
    public static void AttachWeapons()
    {
        Transform g = FindGundam();
        if (g == null) return;
        Animator anim = g.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Debug.LogError("[Gundam] No humanoid Animator on the Gundam instance."); return; }

        // The weapon bones are parented to the ARMATURE ROOT in the FBX (MMD
        // constraint rigs lose their attachments on export) - that is why the
        // shield/rifle/sabers float in place while the body animates.
        // Re-parent them onto real body bones. Prefab instances forbid
        // restructuring, so unpack first.
        if (PrefabUtility.IsPartOfPrefabInstance(g.gameObject))
            PrefabUtility.UnpackPrefabInstance(g.gameObject, PrefabUnpackMode.Completely, InteractionMode.UserAction);

        Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lForearm = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = anim.GetBoneTransform(HumanBodyBones.Spine);
        Transform root = g.transform.root != null ? FindPlayerRoot(g) : g;

        // Snap each weapon BONE to a concrete world-space mount point, computed
        // from the body bones and the mech's facing. The skinned mesh follows its
        // bone, so this drags the geometry to the mount too.
        Vector3 fwd = root.forward, up = Vector3.up, right = root.right;
        SnapBone(g, "ライフル", rHand, rHand.position);                                   // in the right hand
        SnapBone(g, "シールド", lForearm, lForearm.position - right * 0.09f);             // just off the left forearm

        // If you saved a hand-tuned placement (menu 6), it overrides the guesses above.
        ApplySavedWeaponPlacement(g);
        SnapBone(g, "サーベル左", chest, chest.position - fwd * 0.28f - right * 0.13f + up * 0.05f); // back-left
        SnapBone(g, "サーベル右", chest, chest.position - fwd * 0.28f + right * 0.13f + up * 0.05f); // back-right

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(g.gameObject.scene);
        Debug.Log("[Gundam] Weapons attached: rifle -> right hand, shield -> left forearm, sabers -> back. " +
                  "Fine-tune in the Scene view by selecting the ライフル / シールド bones under the new parents " +
                  "and nudging their position/rotation (changes save with the scene).");
    }

    // ------------------------------------------------------------------
    // Hand-tuned weapon placement: position/rotate the four weapon bones in the
    // Scene view ONCE, save, and every future FULL SETUP restores it exactly.
    // ------------------------------------------------------------------
    public const string WeaponJsonPath = "Assets/Model/the-origingundamthe-origin-ver/gundam_weapons.json";
    private static readonly string[] WeaponBones = { "ライフル", "シールド", "サーベル左", "サーベル右" };

    [System.Serializable] public class WeaponPose { public string bone; public string parent; public Vector3 lp; public Quaternion lr; public Vector3 ls; }
    [System.Serializable] public class WeaponPoseList { public List<WeaponPose> items = new List<WeaponPose>(); }

    [MenuItem("Tools/Gundam/6. Save Weapon Placement (after hand-tuning)")]
    public static void SaveWeaponPlacement()
    {
        Transform g = FindGundam();
        if (g == null) return;
        WeaponPoseList list = new WeaponPoseList();
        foreach (string name in WeaponBones)
        {
            Transform b = FindDeep(g, name);
            if (b == null || b.parent == null) continue;
            list.items.Add(new WeaponPose
            {
                bone = name,
                parent = b.parent.name,
                lp = b.localPosition,
                lr = b.localRotation,
                ls = b.localScale,
            });
        }
        File.WriteAllText(WeaponJsonPath, JsonUtility.ToJson(list, true));
        AssetDatabase.Refresh();
        Debug.Log("[Gundam] Saved placement for " + list.items.Count + " weapon(s). FULL SETUP will restore this exact arrangement from now on.");
    }

    private static void ApplySavedWeaponPlacement(Transform g)
    {
        if (!File.Exists(WeaponJsonPath)) return;
        WeaponPoseList list = JsonUtility.FromJson<WeaponPoseList>(File.ReadAllText(WeaponJsonPath));
        if (list == null || list.items == null) return;
        int applied = 0;
        foreach (WeaponPose wp in list.items)
        {
            Transform b = FindDeep(g, wp.bone);
            Transform parent = FindDeep(g, wp.parent);
            if (b == null || parent == null) continue;
            b.SetParent(parent, false);
            b.localPosition = wp.lp;
            b.localRotation = wp.lr;
            b.localScale = wp.ls;
            applied++;
        }
        if (applied > 0) Debug.Log("[Gundam] Restored your saved weapon placement (" + applied + " weapon(s)).");
    }

    // Quick flip helpers: click until the shield is the right way up, then SAVE (menu 6)
    [MenuItem("Tools/Gundam/Flip Shield 180 - X axis")] public static void FlipShieldX() { FlipWeapon("シールド", Vector3.right); }
    [MenuItem("Tools/Gundam/Flip Shield 180 - Y axis")] public static void FlipShieldY() { FlipWeapon("シールド", Vector3.up); }
    [MenuItem("Tools/Gundam/Flip Shield 180 - Z axis")] public static void FlipShieldZ() { FlipWeapon("シールド", Vector3.forward); }

    private static void FlipWeapon(string name, Vector3 localAxis)
    {
        Transform g = FindGundam();
        if (g == null) return;
        Transform b = FindDeep(g, name);
        if (b == null) { Debug.LogWarning("[Gundam] Bone not found: " + name); return; }
        Undo.RecordObject(b, "Flip weapon");
        b.Rotate(localAxis * 180f, Space.Self);
        EditorUtility.SetDirty(b);
        Debug.Log("[Gundam] Flipped " + name + " 180 around local " + localAxis + ". Looks right? Run '6. Save Weapon Placement'.");
    }

    private static Transform FindPlayerRoot(Transform g)
    {
        MechController mc = g.GetComponentInParent<MechController>();
        return mc != null ? mc.transform : g;
    }

    // Parent the named bone to newParent and place it at an exact world position.
    // Rotation is preserved (weapon meshes keep their current orientation - rotate
    // by hand in the Scene view once if the angle looks wrong; it saves with the scene).
    private static void SnapBone(Transform gundam, string boneName, Transform newParent, Vector3 worldPos)
    {
        if (newParent == null) return;
        Transform bone = FindDeep(gundam, boneName);
        if (bone == null) { Debug.LogWarning("[Gundam] Bone not found: " + boneName); return; }
        UnpackFor(bone.gameObject);
        UnpackFor(newParent.gameObject);
        bone.SetParent(newParent, true); // keep world orientation
        bone.position = worldPos;
    }

    private static void AttachBone(Transform gundam, string boneName, Transform newParent, bool snapToParent)
    {
        if (newParent == null) return;
        Transform bone = FindDeep(gundam, boneName);
        if (bone == null) { Debug.LogWarning("[Gundam] Bone not found: " + boneName); return; }
        Undo.SetTransformParent(bone, newParent, "Attach weapon");
        if (snapToParent)
        {
            bone.localPosition = Vector3.zero;
            bone.localRotation = Quaternion.identity;
        }
    }

    public const string TPoseJsonPath = "Assets/Model/the-origingundamthe-origin-ver/gundam_tpose.json";

    [System.Serializable] public class BonePose { public string n; public Vector3 p; public Quaternion r; public Vector3 s; }
    [System.Serializable] public class BonePoseList { public List<BonePose> bones = new List<BonePose>(); }

    /// <summary>
    /// The arms bind STRAIGHT DOWN with zero elbow bend, so Unity's humanoid solver
    /// cannot infer the elbow axis - that is why the arms swing behind the body.
    /// This tool rotates the arm chain into a proper T-pose (horizontal, small
    /// forward elbow hint), records EVERY bone's local pose, saves it to a json,
    /// and reimports - the rig postprocessor feeds it in as the reference skeleton.
    /// </summary>
    [MenuItem("Tools/Gundam/4. Bake Arm T-Pose Into Rig")]
    public static void BakeTPose()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError("[Gundam] FBX not found."); return; }

        GameObject temp = Object.Instantiate(fbx);
        try
        {
            Transform root = temp.transform;

            // Which way does the model face? The FRONT skirt bone tells us.
            Transform frontSkirt = FindDeep(root, "前スカート.L");
            if (frontSkirt == null) frontSkirt = FindDeep(root, "前スカート.R");
            float facing = (frontSkirt != null && frontSkirt.position.z < 0f) ? -1f : 1f;

            foreach (string side in new[] { "L", "R" })
            {
                float sx = side == "L" ? -1f : 1f;
                Transform arm = FindDeep(root, "腕." + side);
                Transform elbow = FindDeep(root, "ひじ." + side);
                Transform wrist = FindDeep(root, "手首." + side);
                if (arm == null || elbow == null || wrist == null)
                {
                    Debug.LogWarning("[Gundam] Missing arm chain on side " + side);
                    continue;
                }
                // Upper arm: straight out to the side (true T-pose)
                AimSegment(arm, elbow, new Vector3(sx, 0f, 0f));
                // Forearm: out to the side with a small bend toward the model's FRONT -
                // this is the elbow-axis hint the humanoid solver needs
                AimSegment(elbow, wrist, new Vector3(sx, 0f, 0.15f * facing).normalized);
            }

            // Record the full skeleton in this corrected pose
            BonePoseList store = new BonePoseList();
            store.bones.Add(new BonePose { n = fbx.name, p = Vector3.zero, r = Quaternion.identity, s = Vector3.one });
            foreach (Transform t in temp.GetComponentsInChildren<Transform>(true))
            {
                if (t == temp.transform) continue;
                store.bones.Add(new BonePose { n = t.name, p = t.localPosition, r = t.localRotation, s = t.localScale });
            }
            File.WriteAllText(TPoseJsonPath, JsonUtility.ToJson(store, true));
            Debug.Log("[Gundam] Baked T-pose for " + store.bones.Count + " bones -> " + TPoseJsonPath + ". Reimporting...");
        }
        finally
        {
            Object.DestroyImmediate(temp);
        }

        AssetDatabase.Refresh();
        AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("[Gundam] Reimported with baked T-pose. Check Rig > Configure: the arms should now be " +
                  "horizontal and green. Then re-run the model swap if the scene instance predates this fix.");
    }

    private static void AimSegment(Transform bone, Transform child, Vector3 worldDir)
    {
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-8f) return;
        Quaternion delta = Quaternion.FromToRotation(cur.normalized, worldDir.normalized);
        bone.rotation = delta * bone.rotation;
    }

    /// <summary>
    /// The swap could not move the melee hitboxes if they lived inside the Y Bot
    /// PREFAB INSTANCE (Unity forbids restructuring those). This unpacks whatever
    /// prefab holds them, moves them onto the Gundam's hands/foot, and then
    /// deactivates the old frozen Y Bot skeleton so nothing stale hangs around.
    /// </summary>
    [MenuItem("Tools/Gundam/5. Fix Hitboxes + Retire Old Model")]
    public static void FixHitboxes()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        Transform g = player != null ? player.transform.Find(GundamName) : null;
        if (player == null || g == null) { Debug.LogError("[Gundam] Need a player with a swapped Gundam first."); return; }
        Animator anim = g.GetComponent<Animator>();
        if (anim == null || !anim.isHuman) { Debug.LogError("[Gundam] Gundam has no humanoid Animator."); return; }

        MechCombat combat = player.GetComponent<MechCombat>();
        if (combat == null) { Debug.LogError("[Gundam] Player has no MechCombat."); return; }

        Transform rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        Transform lFoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);

        // Move existing hitboxes - or REBUILD them if a previous setup run destroyed
        // them along with an old Gundam instance (fresh SphereCollider + MeleeHitbox
        // wired to this MechCombat; all combat numbers live in code defaults).
        combat.rightFistCollider = EnsureHitbox(combat.rightFistCollider, rHand, "Right Fist Hitbox", combat);
        combat.leftFistCollider  = EnsureHitbox(combat.leftFistCollider,  lHand, "Left Fist Hitbox",  combat);
        combat.leftFootCollider  = EnsureHitbox(combat.leftFootCollider,  lFoot, "Left Foot Hitbox",  combat);

        // The Gundam instance is uniformly SCALED to match the old model's height,
        // and colliders inherit that scale - the fist spheres were connecting from
        // a metre away. Normalise every hitbox to a fixed WORLD-space size.
        NormalizeHitboxSize(combat.rightFistCollider, 1.5f);
        NormalizeHitboxSize(combat.leftFistCollider, 1.5f);
        NormalizeHitboxSize(combat.leftFootCollider, 1.5f);
        int moved = (combat.rightFistCollider != null ? 1 : 0) +
                    (combat.leftFistCollider != null ? 1 : 0) +
                    (combat.leftFootCollider != null ? 1 : 0);
        EditorUtility.SetDirty(combat);

        // Retire the old model's frozen skeleton/meshes. We only deactivate
        // subtrees that clearly belong to the OLD model: not the Gundam, not
        // anything gameplay still points at.
        MechShooter shooter = player.GetComponent<MechShooter>();
        int retired = 0;
        foreach (Transform child in player.transform)
        {
            if (child == g) continue;
            if (child.name == GundamName) continue;
            bool isOldModel = child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ||
                              child.name.StartsWith("mixamorig") ||
                              child.name.Contains("(replaced by Gundam)");
            if (!isOldModel) continue;
            if (child.GetComponentInChildren<SimpleMechAI>(true) != null) continue; // never the enemy
            // Safety: never deactivate a subtree gameplay still references
            if (Contains(child, shooter != null ? shooter.muzzle : null)) continue;
            if (Contains(child, player.groundCheckPoint)) continue;
            if (Contains(child, combat.rightFistCollider) || Contains(child, combat.leftFistCollider) || Contains(child, combat.leftFootCollider)) continue;
            if (child.gameObject.activeSelf) { child.gameObject.SetActive(false); retired++; }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log("[Gundam] Hitboxes moved: " + moved + "/3. Old-model subtrees deactivated: " + retired + ". " +
                  "The fists now swing with the Gundam - test a melee string in Play.");
    }

    /// <summary>Sets a collider's dimensions so its WORLD-space radius equals
    /// worldRadius, whatever the accumulated hierarchy scale is.</summary>
    private static void NormalizeHitboxSize(Collider col, float worldRadius)
    {
        if (col == null) return;
        Vector3 ls = col.transform.lossyScale;
        float s = Mathf.Max(Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y)), Mathf.Max(Mathf.Abs(ls.z), 0.0001f));
        SphereCollider sc = col as SphereCollider;
        if (sc != null)
        {
            sc.radius = worldRadius / s;
            sc.center = Vector3.zero;
            return;
        }
        BoxCollider bc = col as BoxCollider;
        if (bc != null)
        {
            bc.size = Vector3.one * (worldRadius * 2f / s);
            bc.center = Vector3.zero;
        }
    }

    private static Collider EnsureHitbox(Collider col, Transform bone, string name, MechCombat combat)
    {
        if (bone == null) return col;

        if (col == null) // never assigned OR destroyed with an old instance
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(bone, false);
            SphereCollider sc = go.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            // Born scale-aware: MMD bones can carry ~93x accumulated scale, so a raw
            // local radius would become an arena-sized sphere ("hits from any distance").
            Vector3 bls = bone.lossyScale;
            float bs = Mathf.Max(Mathf.Max(Mathf.Abs(bls.x), Mathf.Abs(bls.y)), Mathf.Max(Mathf.Abs(bls.z), 0.0001f));
            sc.radius = 1.5f / bs;
            sc.enabled = false; // MechCombat enables it per swing
            MeleeHitbox mh = go.AddComponent<MeleeHitbox>();
            mh.targetTag = "Enemy";
            mh.playerCombatScript = combat;
            Debug.Log("[Gundam] Rebuilt missing hitbox: " + name);
            return sc;
        }

        if (col.transform.IsChildOf(bone)) return col; // already in place

        // Unpack every prefab instance that imprisons the hitbox OR the target bone
        UnpackFor(col.gameObject);
        UnpackFor(bone.gameObject);
        col.transform.SetParent(bone, false);
        col.transform.localPosition = Vector3.zero;
        col.transform.localRotation = Quaternion.identity;
        return col;
    }

    private static void UnpackFor(GameObject go)
    {
        GameObject inst = PrefabUtility.IsPartOfPrefabInstance(go)
            ? PrefabUtility.GetOutermostPrefabInstanceRoot(go)
            : null;
        if (inst != null)
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.UserAction);
    }

    private static bool Contains(Transform tree, Object member)
    {
        if (tree == null || member == null) return false; // == catches destroyed objects
        Transform t = member as Transform;
        if (t == null)
        {
            Component c = member as Component;
            if (c != null) t = c.transform; // Unity-null check above makes this safe
        }
        if (t == null) return false;
        return t == tree || t.IsChildOf(tree);
    }

    /// <summary>Prints where both mechs' pieces ACTUALLY are - finds enemy parts
    /// that got re-parented onto the player (or vice versa) and stray meshes.</summary>
    /// <summary>Shows exactly where every melee hitbox reference points - the
    /// cross-wiring detector. A PLAYER hitbox living under the ENEMY (or vice
    /// versa) = melee that "hits from any distance".</summary>
    /// <summary>THE fix for "melee hits from any distance": the MMD wrist bones
    /// carry a ~93x accumulated scale, so a raw local-radius hitbox becomes an
    /// arena-sized world sphere. This resizes every MeleeHitbox in the scene to
    /// its meleeReachRadius (1.5u world - EXVS-style generous reach).</summary>
    [MenuItem("Tools/Gundam/Fix - Shrink Melee Hitboxes Now")]
    public static void ShrinkMeleeHitboxesNow()
    {
        int n = 0;
        foreach (MeleeHitbox mh in Object.FindObjectsByType<MeleeHitbox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            SphereCollider sc = mh.GetComponent<SphereCollider>();
            if (sc == null) continue;
            Vector3 ls = mh.transform.lossyScale;
            float s = Mathf.Max(Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y)), Mathf.Max(Mathf.Abs(ls.z), 0.0001f));
            float worldBefore = sc.radius * s;
            Undo.RecordObject(sc, "Resize Melee Hitbox");
            mh.ResizeToWorldRadius();
            EditorUtility.SetDirty(sc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mh.gameObject.scene);
            Debug.Log("[Gundam] '" + mh.name + "' world radius " + worldBefore.ToString("0.00") + " -> " + mh.meleeReachRadius);
            n++;
        }
        Debug.Log("[Gundam] Resized " + n + " melee hitboxes. Run Debug - X-Ray Hitboxes to verify.");
    }

    [MenuItem("Tools/Gundam/Debug - X-Ray Hitboxes")]
    public static void XRayHitboxes()
    {
        var sb = new System.Text.StringBuilder("=== HITBOX X-RAY ===\n");
        MechCombat combat = Object.FindFirstObjectByType<MechCombat>();
        SimpleMechAI ai = Object.FindFirstObjectByType<SimpleMechAI>();

        if (combat != null)
        {
            sb.Append("PLAYER MechCombat on '" + combat.name + "' at " + combat.transform.position.ToString("F1") + "\n");
            Describe(sb, "  rightFistCollider", combat.rightFistCollider, combat.transform, ai != null ? ai.transform : null);
            Describe(sb, "  leftFistCollider ", combat.leftFistCollider, combat.transform, ai != null ? ai.transform : null);
            Describe(sb, "  leftFootCollider ", combat.leftFootCollider, combat.transform, ai != null ? ai.transform : null);
        }
        if (ai != null)
        {
            sb.Append("ENEMY SimpleMechAI on '" + ai.name + "' at " + ai.transform.position.ToString("F1") + "\n");
            Describe(sb, "  rightFistCollider", ai.rightFistCollider, ai.transform, combat != null ? combat.transform : null);
            Describe(sb, "  leftFistCollider ", ai.leftFistCollider, ai.transform, combat != null ? combat.transform : null);
            Describe(sb, "  leftFootCollider ", ai.leftFootCollider, ai.transform, combat != null ? combat.transform : null);
        }

        // Every MeleeHitbox in the scene, wherever it hides
        sb.Append("ALL MeleeHitbox components in the scene:\n");
        foreach (MeleeHitbox mh in Object.FindObjectsByType<MeleeHitbox>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Collider c = mh.GetComponent<Collider>();
            sb.Append("  '" + mh.name + "' targetTag=" + mh.targetTag +
                      " at " + mh.transform.position.ToString("F1") +
                      " enabled=" + (c != null && c.enabled) +
                      " parentChain=" + Chain(mh.transform) + "\n");
        }
        Debug.Log(sb.ToString());
    }

    private static void Describe(System.Text.StringBuilder sb, string label, Collider col, Transform owner, Transform other)
    {
        if (col == null) { sb.Append(label + " = NULL / destroyed\n"); return; }
        bool underOwner = col.transform.IsChildOf(owner);
        bool underOther = other != null && col.transform.IsChildOf(other);
        float r = col is SphereCollider sc2 ? sc2.radius * MaxScale(col.transform) : -1f;
        sb.Append(label + " = '" + col.name + "' at " + col.transform.position.ToString("F1") +
                  (r > 0 ? " worldRadius=" + r.ToString("F2") : "") +
                  " enabled=" + col.enabled +
                  (underOwner ? " [under OWNER - ok]" : underOther ? " [!!! UNDER THE OTHER MECH !!!]" : " [!!! NOT UNDER EITHER MECH !!!]") + "\n");
    }

    private static float MaxScale(Transform t)
    {
        Vector3 ls = t.lossyScale;
        return Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z));
    }

    private static string Chain(Transform t)
    {
        string s2 = t.name;
        int guard = 0;
        while (t.parent != null && guard++ < 8) { t = t.parent; s2 = t.name + "/" + s2; }
        return s2;
    }

    [MenuItem("Tools/Gundam/Debug - Print Mech Hierarchies")]
    public static void DebugHierarchies()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        var sb = new System.Text.StringBuilder();

        if (player != null)
        {
            sb.Append("=== PLAYER root '" + player.name + "' at " + player.transform.position.ToString("F1") + " ===\n");
            Dump(player.transform, 0, 3, sb);
            // Enemy parts smuggled under the player?
            foreach (Transform t in player.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith("mixamorig") && t.parent != null && t.parent.GetComponentInParent<SimpleMechAI>() == null)
                {
                    Transform p = t.parent;
                    bool underGundam = false;
                    for (Transform a = t; a != null; a = a.parent) if (a.name == GundamName) { underGundam = true; break; }
                    if (underGundam) sb.Append("SUSPICIOUS: mixamorig bone '" + t.name + "' under the GUNDAM at " + t.position.ToString("F1") + " (parent " + p.name + ")\n");
                }
            }
        }
        if (enemy != null)
        {
            sb.Append("\n=== ENEMY root '" + enemy.name + "' at " + enemy.transform.position.ToString("F1") + " ===\n");
            Dump(enemy.transform, 0, 3, sb);
            SkinnedMeshRenderer[] smrs = enemy.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            sb.Append("Enemy SkinnedMeshRenderers: " + smrs.Length + "\n");
            foreach (var smr in smrs)
            {
                Transform rb = smr.rootBone;
                sb.Append("  '" + smr.name + "' enabled=" + smr.enabled +
                          " boundsCenter=" + smr.bounds.center.ToString("F1") +
                          " rootBone=" + (rb == null ? "NULL" : rb.name + " at " + rb.position.ToString("F1") +
                          (rb.GetComponentInParent<SimpleMechAI>() == null ? "  << ROOTBONE IS OUTSIDE THE ENEMY!" : "")) + "\n");
            }
        }
        else sb.Append("\nNO SimpleMechAI FOUND\n");

        Debug.Log(sb.ToString());
    }

    private static void Dump(Transform t, int depth, int maxDepth, System.Text.StringBuilder sb)
    {
        sb.Append(new string(' ', depth * 2))
          .Append(t.name)
          .Append(t.gameObject.activeSelf ? "" : "  [INACTIVE]")
          .Append(t.GetComponent<Renderer>() != null ? "  [Renderer " + (t.GetComponent<Renderer>().enabled ? "on" : "OFF") + "]" : "")
          .Append("  ").Append(t.position.ToString("F1")).Append('\n');
        if (depth >= maxDepth) return;
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, maxDepth, sb);
    }

    [MenuItem("Tools/Gundam/Debug - Print Avatar Arm Mapping")]
    public static void DebugMapping()
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError("[Gundam] FBX not found."); return; }
        Animator a = fbx.GetComponent<Animator>();
        if (a == null || a.avatar == null) { Debug.LogError("[Gundam] No avatar on FBX."); return; }

        var sb = new System.Text.StringBuilder("[Gundam] AVATAR MAPPING (asset):\n");
        HumanBodyBones[] check = {
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
        };
        foreach (var hb in check)
        {
            Transform t = a.GetBoneTransform(hb);
            sb.Append(hb).Append("  ->  ").Append(t == null ? "(none)" : Path(t, fbx.transform))
              .Append(t == null ? "" : "   worldPos " + t.position.ToString("F2")).Append('\n');
        }

        sb.Append("\n[Gundam] ALL transforms with arm-ish names (duplicates show here):\n");
        string[] names = { "腕", "ひじ", "手首", "肩", "センター" };
        foreach (Transform t in fbx.GetComponentsInChildren<Transform>(true))
        {
            foreach (string n in names)
            {
                if (t.name.Contains(n)) { sb.Append(Path(t, fbx.transform)).Append("   pos ").Append(t.position.ToString("F2")).Append('\n'); break; }
            }
        }
        Debug.Log(sb.ToString());
    }

    private static string Path(Transform t, Transform stopAt)
    {
        string p = t.name;
        while (t.parent != null && t.parent != stopAt) { t = t.parent; p = t.name + "/" + p; }
        return p;
    }

    [MenuItem("Tools/Gundam/Fix - Hide Old Y Bot Renderers")]
    public static void HideOldMenu()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        Transform g = player != null ? player.transform.Find(GundamName) : null;
        if (player == null || g == null) { Debug.LogWarning("[Gundam] Need a player with a swapped Gundam first."); return; }
        HideOldRenderers(player.gameObject, g);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
    }

    // Any renderer on the player that is NOT part of the Gundam is old Y Bot visuals.
    // CRITICAL GUARD: the ENEMY is also a Y Bot - if it lives anywhere under the
    // same hierarchy, never touch anything in a SimpleMechAI subtree (this exact
    // bug made the enemy invisible: punches "hit thin air" that was really the
    // unrendered enemy).
    /// <summary>Gives the ENEMY the Gundam model too, tinted Char-red so the two
    /// mechs stay instantly distinguishable. Mirrors the player swap: animator
    /// rewired, melee hitboxes rebuilt on the new hands/foot, weapons mounted
    /// (reusing your saved hand-tuned placement), old Y Bot hidden. Idempotent -
    /// re-run any time. Run AFTER the player's FULL SETUP.</summary>
    [MenuItem("Tools/Gundam/7. Swap ENEMY To Red Gundam")]
    public static void SwapEnemyToRedGundam()
    {
        SimpleMechAI ai = Object.FindFirstObjectByType<SimpleMechAI>();
        if (ai == null) { Debug.LogError("[Gundam] No SimpleMechAI in the scene."); return; }

        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null) { Debug.LogError("[Gundam] FBX not found at " + FbxPath); return; }
        Animator fbxAnim = fbx.GetComponent<Animator>();
        if (fbxAnim == null || fbxAnim.avatar == null || !fbxAnim.avatar.isValid || !fbxAnim.avatar.isHuman)
        { Debug.LogError("[Gundam] FBX has no valid humanoid avatar - run the player FULL SETUP first."); return; }

        // Fresh instance every run
        Transform existing = ai.transform.Find(GundamName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // Old animator (controller reuse) + old model measurements
        Animator oldAnim = ai.animator != null ? ai.animator : ai.GetComponentInChildren<Animator>();
        RuntimeAnimatorController controller = oldAnim != null ? oldAnim.runtimeAnimatorController : null;
        float targetHeight = 1.9f;
        GameObject oldModelGo = null;
        if (oldAnim != null && oldAnim.gameObject != ai.gameObject)
        {
            Bounds ob = RenderBounds(oldAnim.gameObject);
            if (ob.size.y > 0.1f) targetHeight = ob.size.y;
            oldModelGo = oldAnim.gameObject;
        }

        // Instantiate + scale + ground the feet
        GameObject gundam = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        Undo.RegisterCreatedObjectUndo(gundam, "Enemy Gundam swap");
        gundam.name = GundamName;
        gundam.transform.SetParent(ai.transform, false);
        gundam.transform.localPosition = Vector3.zero;
        gundam.transform.localRotation = Quaternion.identity;
        if (PrefabUtility.IsPartOfPrefabInstance(gundam))
            PrefabUtility.UnpackPrefabInstance(gundam, PrefabUnpackMode.Completely, InteractionMode.UserAction);

        Bounds nb = RenderBounds(gundam);
        if (nb.size.y > 0.01f)
        {
            gundam.transform.localScale = Vector3.one * (targetHeight / nb.size.y);
            Bounds sb = RenderBounds(gundam);
            gundam.transform.localPosition += Vector3.up * (ai.transform.position.y - sb.min.y);
        }

        // Animator + relay
        Animator newAnim = gundam.GetComponent<Animator>();
        if (newAnim == null) newAnim = gundam.AddComponent<Animator>();
        newAnim.runtimeAnimatorController = controller;
        newAnim.applyRootMotion = false;
        newAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (gundam.GetComponent<AnimationEventRelay>() == null)
            gundam.AddComponent<AnimationEventRelay>();

        // Retire the old model, wire the AI to the new one
        if (oldModelGo != null) { oldModelGo.SetActive(false); oldModelGo.name += " (replaced by Gundam)"; }
        foreach (Renderer r in ai.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform == gundam.transform || r.transform.IsChildOf(gundam.transform)) continue;
            if (r.enabled) r.enabled = false;
        }
        ai.animator = newAnim;

        // Melee hitboxes rebuilt on the new hands/foot (MeleeHitbox self-sizes at runtime)
        ai.rightFistCollider = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.RightHand), "Enemy Right Fist Hitbox", ai, ai.rightFistCollider);
        ai.leftFistCollider  = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.LeftHand),  "Enemy Left Fist Hitbox",  ai, ai.leftFistCollider);
        ai.leftFootCollider  = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.LeftFoot),  "Enemy Left Foot Hitbox",  ai, ai.leftFootCollider);

        // Weapons onto the body (same mounts as the player, incl. your saved placement)
        Transform rHand = newAnim.GetBoneTransform(HumanBodyBones.RightHand);
        Transform lForearm = newAnim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        Transform chest = newAnim.GetBoneTransform(HumanBodyBones.Chest);
        if (chest == null) chest = newAnim.GetBoneTransform(HumanBodyBones.Spine);
        Vector3 fwd = ai.transform.forward, up = Vector3.up, right = ai.transform.right;
        if (rHand != null) SnapBone(gundam.transform, "ライフル", rHand, rHand.position);
        if (lForearm != null) SnapBone(gundam.transform, "シールド", lForearm, lForearm.position - right * 0.09f);
        ApplySavedWeaponPlacement(gundam.transform);
        if (chest != null)
        {
            SnapBone(gundam.transform, "サーベル左", chest, chest.position - fwd * 0.28f - right * 0.13f + up * 0.05f);
            SnapBone(gundam.transform, "サーベル右", chest, chest.position - fwd * 0.28f + right * 0.13f + up * 0.05f);
        }

        // CHAR-RED materials: same atlas, red-tinted variant asset
        const string texDir = "Assets/Model/the-origingundamthe-origin-ver/textures/";
        Texture2D baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "変換_～_GUNDAM.png");
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(texDir + "変換_～_GUNDAM-N.png");
        const string redPath = "Assets/Model/the-origingundamthe-origin-ver/GundamURP_Red.mat";
        Material red = AssetDatabase.LoadAssetAtPath<Material>(redPath);
        if (red == null)
        {
            red = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(red, redPath);
        }
        if (baseMap != null) red.SetTexture("_BaseMap", baseMap);
        red.color = new Color(1f, 0.42f, 0.42f); // the Char tint
        if (normal != null) { red.SetTexture("_BumpMap", normal); red.EnableKeyword("_NORMALMAP"); }
        red.SetFloat("_Smoothness", 0.45f);
        EditorUtility.SetDirty(red);
        AssetDatabase.SaveAssets();
        foreach (Renderer r in gundam.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = red;
            r.sharedMaterials = mats;
        }

        EditorUtility.SetDirty(ai);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ai.gameObject.scene);
        Debug.Log("[Gundam] ENEMY is now a red Gundam. Play and check: it moves/attacks, punches connect, " +
                  "and the tint reads clearly. Re-run this menu any time. If it faces backwards, rotate the " +
                  "'" + GundamName + "' child under the enemy 180 on Y.");
    }

    // ==================================================================
    //  8. CLONE THE PLAYER'S MODEL ONTO THE ENEMY
    //  Menu 7 rebuilds the enemy from the raw FBX, so every hand-tuning done on
    //  the PLAYER (scale, height offset, weapon positions and rotations, saber
    //  mounts) was re-derived and never matched exactly. This instead DUPLICATES
    //  the player's live in-scene model, so the enemy is a part-for-part copy of
    //  whatever is currently tuned - only the colour differs.
    // ==================================================================

    /// <summary>Exact copy of the player's hand-tuned model onto the enemy, tinted red.</summary>
    [MenuItem("Tools/Gundam/8. Clone PLAYER Model -> ENEMY (red tint)")]
    public static void ClonePlayerModelToEnemyRed() { ClonePlayerModelToEnemy(true); }

    /// <summary>Exact copy of the player's hand-tuned model onto the enemy, same colours.</summary>
    [MenuItem("Tools/Gundam/8b. Clone PLAYER Model -> ENEMY (identical, no tint)")]
    public static void ClonePlayerModelToEnemyPlain() { ClonePlayerModelToEnemy(false); }

    private static void ClonePlayerModelToEnemy(bool tintRed)
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        SimpleMechAI ai = Object.FindFirstObjectByType<SimpleMechAI>();
        if (player == null) { Debug.LogError("[Gundam] No player (MechController) in the scene."); return; }
        if (ai == null) { Debug.LogError("[Gundam] No enemy (SimpleMechAI) in the scene."); return; }

        Transform src = player.transform.Find(GundamName);
        if (src == null) src = FindDeep(player.transform, GundamName);
        if (src == null)
        {
            Debug.LogError("[Gundam] The player has no '" + GundamName + "' child - run 1. Swap Player Model first.");
            return;
        }

        // Keep the enemy's own animator controller: its AI clips are driven from it.
        Animator oldAnim = ai.animator != null ? ai.animator : ai.GetComponentInChildren<Animator>(true);
        RuntimeAnimatorController enemyController = oldAnim != null ? oldAnim.runtimeAnimatorController : null;
        GameObject oldModelGo = (oldAnim != null && oldAnim.gameObject != ai.gameObject) ? oldAnim.gameObject : null;

        // Fresh copy every run (idempotent)
        Transform existing = ai.transform.Find(GundamName);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        GameObject clone = Object.Instantiate(src.gameObject, ai.transform);
        Undo.RegisterCreatedObjectUndo(clone, "Clone player model to enemy");
        clone.name = GundamName;
        // EXACT local transform - this is the whole point of the tool
        clone.transform.localPosition = src.localPosition;
        clone.transform.localRotation = src.localRotation;
        clone.transform.localScale = src.localScale;
        clone.SetActive(true);

        // The clone carries the PLAYER's melee hitboxes (they point at MechCombat and
        // hunt the "Enemy" tag). Strip them; the enemy versions get rebuilt below.
        foreach (MeleeHitbox mh in clone.GetComponentsInChildren<MeleeHitbox>(true))
            if (mh != null) Object.DestroyImmediate(mh.gameObject);

        // Animator: same rig/avatar as the player (that is what makes it identical),
        // driven by the enemy's controller.
        Animator newAnim = clone.GetComponent<Animator>();
        if (newAnim == null) newAnim = clone.AddComponent<Animator>();
        if (enemyController != null) newAnim.runtimeAnimatorController = enemyController;
        newAnim.applyRootMotion = false;
        newAnim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (clone.GetComponent<AnimationEventRelay>() == null) clone.AddComponent<AnimationEventRelay>();

        // Retire the enemy's previous visuals
        if (oldModelGo != null && oldModelGo != clone)
        {
            oldModelGo.SetActive(false);
            if (!oldModelGo.name.EndsWith("(replaced by Gundam)")) oldModelGo.name += " (replaced by Gundam)";
        }
        int hidden = 0;
        foreach (Renderer r in ai.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform == clone.transform || r.transform.IsChildOf(clone.transform)) continue;
            if (r.enabled) { r.enabled = false; hidden++; }
        }
        ai.animator = newAnim;

        // Melee hitboxes rebuilt on the copied hands/foot, aimed at the PLAYER
        ai.rightFistCollider = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.RightHand), "Enemy Right Fist Hitbox", ai, null);
        ai.leftFistCollider = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.LeftHand), "Enemy Left Fist Hitbox", ai, null);
        ai.leftFootCollider = BuildEnemyHitbox(newAnim.GetBoneTransform(HumanBodyBones.LeftFoot), "Enemy Left Foot Hitbox", ai, null);

        // Colour: per-material RED VARIANTS, so textures / shader / smoothness stay
        // exactly the player's and only the tint differs. Player materials untouched.
        int tinted = 0;
        if (tintRed)
        {
            var cache = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (Renderer r in clone.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    Material redVariant;
                    if (!cache.TryGetValue(mats[i], out redVariant))
                    {
                        redVariant = RedVariantOf(mats[i]);
                        cache[mats[i]] = redVariant;
                    }
                    mats[i] = redVariant;
                    tinted++;
                }
                r.sharedMaterials = mats;
            }
            AssetDatabase.SaveAssets();
        }

        EditorUtility.SetDirty(ai);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(ai.gameObject.scene);
        Debug.Log("[Gundam] ENEMY is now an EXACT copy of the player's model" +
                  (tintRed ? " (red-tinted, " + tinted + " material slot(s))" : " (identical colours)") +
                  ". scale=" + clone.transform.localScale.ToString("F3") +
                  " localPos=" + clone.transform.localPosition.ToString("F3") +
                  ", " + hidden + " old renderer(s) hidden. SAVE THE SCENE.");
    }

    // Duplicates a material as a red-tinted asset next to the original, reusing it
    // on re-runs so the project does not fill up with copies.
    private static Material RedVariantOf(Material source)
    {
        string srcPath = AssetDatabase.GetAssetPath(source);
        string dir = string.IsNullOrEmpty(srcPath)
            ? "Assets/Model/the-origingundamthe-origin-ver"
            : System.IO.Path.GetDirectoryName(srcPath).Replace('\\', '/');
        string outPath = dir + "/" + source.name + "_EnemyRed.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(outPath);
        Material red = existing != null ? existing : new Material(source);
        if (existing != null)
        {
            red.shader = source.shader;
            red.CopyPropertiesFromMaterial(source); // re-sync if the player's material was re-tuned
        }

        Color tint = new Color(1f, 0.42f, 0.42f); // the Char tint
        if (red.HasProperty("_BaseColor")) red.SetColor("_BaseColor", source.GetColor("_BaseColor") * tint);
        else if (red.HasProperty("_Color")) red.SetColor("_Color", source.GetColor("_Color") * tint);
        else red.color = source.color * tint;

        if (existing == null) AssetDatabase.CreateAsset(red, outPath);
        EditorUtility.SetDirty(red);
        return red;
    }

    private static Collider BuildEnemyHitbox(Transform bone, string name, SimpleMechAI ai, Collider old)
    {
        if (bone == null) return old;
        if (old != null && old.transform.IsChildOf(bone)) return old; // already done
        GameObject go = new GameObject(name);
        go.transform.SetParent(bone, false);
        SphereCollider sc = go.AddComponent<SphereCollider>();
        sc.isTrigger = true;
        Vector3 bls = bone.lossyScale;
        float bs = Mathf.Max(Mathf.Max(Mathf.Abs(bls.x), Mathf.Abs(bls.y)), Mathf.Max(Mathf.Abs(bls.z), 0.0001f));
        sc.radius = 1.5f / bs;
        sc.enabled = false; // enabled per swing by the AI's animation events
        MeleeHitbox mh = go.AddComponent<MeleeHitbox>();
        mh.targetTag = "Player";
        mh.aiCombatScript = ai;
        return sc;
    }

    private static void HideOldRenderers(GameObject root, Transform gundam)
    {
        int hidden = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r.transform == gundam || r.transform.IsChildOf(gundam)) continue;
            if (r.GetComponentInParent<SimpleMechAI>() != null) continue; // the enemy!
            if (r.enabled) { r.enabled = false; hidden++; }
        }
        if (hidden > 0) Debug.Log("[Gundam] Hid " + hidden + " old model renderer(s).");
    }

    /// <summary>Undo the accidental enemy-hiding: re-enables every renderer on and
    /// under the SimpleMechAI, and reactivates its deactivated subtrees.</summary>
    [MenuItem("Tools/Gundam/Fix - Restore Enemy Visibility")]
    public static void RestoreEnemyVisibility()
    {
        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        if (enemy == null)
        {
            // It may be on an inactive object - search harder
            foreach (SimpleMechAI ai in Resources.FindObjectsOfTypeAll<SimpleMechAI>())
            {
                if (ai.gameObject.scene.IsValid()) { enemy = ai; break; }
            }
        }
        if (enemy == null) { Debug.LogError("[Gundam] No SimpleMechAI found in the scene."); return; }

        int fixedCount = 0;
        foreach (Transform t in enemy.GetComponentsInChildren<Transform>(true))
        {
            if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); fixedCount++; }
        }
        foreach (Renderer r in enemy.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) { r.enabled = true; fixedCount++; }
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(enemy.gameObject.scene);
        Debug.Log("[Gundam] Enemy visibility restored (" + fixedCount + " renderer(s)/object(s) re-enabled). Save the scene.");
    }

    [MenuItem("Tools/Gundam/Rotate Gundam 180")]
    public static void Rotate180()
    {
        Transform g = FindGundam();
        if (g == null) return;
        Undo.RecordObject(g, "Rotate Gundam");
        g.localRotation = g.localRotation * Quaternion.Euler(0f, 180f, 0f);
        EditorUtility.SetDirty(g);
    }

    [MenuItem("Tools/Gundam/Scale Gundam +10%")]
    public static void ScaleUp() { ScaleBy(1.1f); }

    [MenuItem("Tools/Gundam/Scale Gundam -10%")]
    public static void ScaleDown() { ScaleBy(1f / 1.1f); }

    private static void ScaleBy(float k)
    {
        Transform g = FindGundam();
        if (g == null) return;
        Undo.RecordObject(g, "Scale Gundam");
        g.localScale *= k;
        EditorUtility.SetDirty(g);
    }

    private static Transform FindGundam()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        Transform g = player != null ? player.transform.Find(GundamName) : null;
        if (g == null) Debug.LogWarning("[Gundam] No swapped Gundam found - run the swap first.");
        return g;
    }
}
