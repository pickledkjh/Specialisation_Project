using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Builds the slash1..slash4 melee states in the player animator controller as
/// exact structural copies of the existing punch1..punch4 states.
///
/// Why a tool instead of hand-wiring: the punch states carry the exit transitions
/// that return the mech to Locomotion. A slash state added by hand with no exit
/// transition leaves the mech frozen in the last slash pose - the single most
/// common way melee "stops working" after swapping clips. This copies every
/// outgoing transition (conditions, exit time, durations, interruption source)
/// and remaps punch->slash destinations so the string chains identically.
///
/// INCOMING transitions are deliberately NOT copied: MechCombat drives the string
/// with CrossFadeInFixedTime, so the code is authoritative over which state plays.
/// Duplicating the incoming trigger transitions would only create two states
/// competing for the same Melee triggers.
///
/// Safe and idempotent: the punch states are left untouched, so switching back is
/// just setting MechCombat's "Melee Anim Set" field to "punch".
/// </summary>
public static class SlashSetup
{
    private const string ControllerPath = "Assets/Animation/Player.controller";
    private const string ClipDir = "Assets/Animation/";

    [MenuItem("Tools/Gundam/9. Build SLASH 1-4 States (from the punches)")]
    public static void BuildSlashStates()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null)
        {
            Debug.LogError("[Slash] Controller not found at " + ControllerPath);
            return;
        }
        if (ctrl.layers.Length == 0) { Debug.LogError("[Slash] Controller has no layers."); return; }

        AnimatorStateMachine sm = ctrl.layers[0].stateMachine;
        var report = new System.Text.StringBuilder("[Slash] SETUP REPORT\n");
        int built = 0;

        // Pass 1: make sure every slash state exists with the right clip.
        AnimatorState[] slash = new AnimatorState[5];
        AnimatorState[] punch = new AnimatorState[5];
        for (int i = 1; i <= 4; i++)
        {
            punch[i] = FindState(sm, "punch" + i);
            if (punch[i] == null)
            {
                Debug.LogError("[Slash] No state named 'punch" + i + "' on layer 0 - nothing to copy from. Aborting.");
                return;
            }

            AnimationClip clip = LoadHumanoidClip(ClipDir + "slash" + i + ".fbx", report);
            if (clip == null)
            {
                Debug.LogError("[Slash] No usable AnimationClip inside " + ClipDir + "slash" + i + ".fbx - aborting.");
                return;
            }

            AnimatorState s = FindState(sm, "slash" + i);
            if (s == null)
            {
                Vector3 pos = FindStatePosition(sm, punch[i]) + new Vector3(280f, 0f, 0f);
                s = sm.AddState("slash" + i, pos);
                built++;
            }
            else
            {
                // Re-run: drop the old outgoing transitions so they get rebuilt clean
                for (int t = s.transitions.Length - 1; t >= 0; t--) s.RemoveTransition(s.transitions[t]);
            }

            s.motion = clip;
            s.speed = punch[i].speed;
            s.cycleOffset = punch[i].cycleOffset;
            s.mirror = punch[i].mirror;
            s.iKOnFeet = punch[i].iKOnFeet;
            s.writeDefaultValues = punch[i].writeDefaultValues;
            s.tag = punch[i].tag;
            s.speedParameterActive = punch[i].speedParameterActive;
            if (punch[i].speedParameterActive) s.speedParameter = punch[i].speedParameter;
            slash[i] = s;
            report.Append("  slash").Append(i).Append("  clip='").Append(clip.name)
                  .Append("'  len=").Append(clip.length.ToString("0.00")).Append("s  humanoid=")
                  .Append(clip.isHumanMotion).Append('\n');
        }

        // Pass 2: copy the punch states' OUTGOING transitions, remapping any
        // punchN destination to slashN so the chain stays inside the slash set.
        for (int i = 1; i <= 4; i++)
        {
            foreach (AnimatorStateTransition src in punch[i].transitions)
            {
                AnimatorState dest = src.destinationState;
                for (int k = 1; k <= 4; k++)
                    if (dest == punch[k]) { dest = slash[k]; break; }

                AnimatorStateTransition copy;
                if (dest != null) copy = slash[i].AddTransition(dest);
                else if (src.destinationStateMachine != null) copy = slash[i].AddTransition(src.destinationStateMachine);
                else if (src.isExit) copy = slash[i].AddExitTransition();
                else continue;

                copy.hasExitTime = src.hasExitTime;
                copy.exitTime = src.exitTime;
                copy.hasFixedDuration = src.hasFixedDuration;
                copy.duration = src.duration;
                copy.offset = src.offset;
                copy.interruptionSource = src.interruptionSource;
                copy.orderedInterruption = src.orderedInterruption;
                copy.canTransitionToSelf = src.canTransitionToSelf;
                copy.solo = false;
                copy.mute = false;
                foreach (AnimatorCondition c in src.conditions)
                    copy.AddCondition(c.mode, c.threshold, c.parameter);

                report.Append("    slash").Append(i).Append(" -> ")
                      .Append(dest != null ? dest.name : (src.isExit ? "(Exit)" : "(sub-machine)"))
                      .Append("  exitTime=").Append(src.hasExitTime ? src.exitTime.ToString("0.00") : "off")
                      .Append("  conds=").Append(src.conditions.Length).Append('\n');
            }
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        report.Append("  ").Append(built).Append(" state(s) created, ")
              .Append(4 - built).Append(" refreshed in place.\n")
              .Append("  MechCombat drives these by name - its 'Melee Anim Set' field must read 'slash'.\n")
              .Append("  Set it back to 'punch' to revert; the punch states were not touched.");
        Debug.Log(report.ToString());
    }

    private const string MaskPath = "Assets/Animation/UpperBodyMelee.mask";
    private const string LayerName = "Melee Upper";

    /// <summary>THE fix for "the slash has too much leg motion, it looks messy".
    /// Builds a masked upper-body layer holding slash1-4, so the clips drive only
    /// the torso, arms and head while the LEGS keep their locomotion pose. This is
    /// how every boost-action game does melee: the saber arm swings, the legs stay
    /// planted in the boost stance instead of doing a mocap actor's footwork.</summary>
    [MenuItem("Tools/Gundam/9c. Move SLASH To Upper-Body Layer (clean legs)")]
    public static void BuildUpperBodyMeleeLayer()
    {
        AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ctrl == null) { Debug.LogError("[Slash] Controller not found at " + ControllerPath); return; }

        // ---- 1. the mask: everything except the legs and the root motion ----
        AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        if (mask == null)
        {
            mask = new AvatarMask();
            AssetDatabase.CreateAsset(mask, MaskPath);
        }
        // Body parts the slash clips MAY drive
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Body, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Head, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFingers, true);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFingers, true);
        // ...and the ones it may NOT: this is what kills the messy footwork
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightLeg, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightFootIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.RightHandIK, false);
        mask.SetHumanoidBodyPartActive(AvatarMaskBodyPart.Root, false); // no clip-driven sliding
        EditorUtility.SetDirty(mask);

        // ---- 2. the layer ----
        int layerIndex = -1;
        for (int i = 0; i < ctrl.layers.Length; i++)
            if (ctrl.layers[i].name == LayerName) { layerIndex = i; break; }

        if (layerIndex < 0)
        {
            ctrl.AddLayer(LayerName);
            layerIndex = ctrl.layers.Length - 1;
        }
        AnimatorControllerLayer[] layers = ctrl.layers;
        layers[layerIndex].avatarMask = mask;
        layers[layerIndex].blendingMode = AnimatorLayerBlendingMode.Override;
        layers[layerIndex].defaultWeight = 0f; // MechCombat fades this in per swing
        layers[layerIndex].iKPass = false;
        ctrl.layers = layers;

        AnimatorStateMachine sm = ctrl.layers[layerIndex].stateMachine;

        // ---- 3. Empty rest state (the layer's default) ----
        AnimatorState empty = FindState(sm, "Empty");
        if (empty == null)
        {
            empty = sm.AddState("Empty", new Vector3(260f, 60f, 0f));
            empty.motion = null;
            empty.writeDefaultValues = false;
        }
        sm.defaultState = empty;

        // ---- 4. slash states on this layer, each returning to Empty ----
        int made = 0;
        var report = new System.Text.StringBuilder("[Slash] UPPER-BODY LAYER '" + LayerName + "' (index " + layerIndex + ")\n");
        for (int i = 1; i <= 4; i++)
        {
            AnimationClip clip = LoadHumanoidClip(ClipDir + "slash" + i + ".fbx", report);
            if (clip == null) { Debug.LogError("[Slash] Missing clip for slash" + i + " - aborting."); return; }

            AnimatorState s = FindState(sm, "slash" + i);
            if (s == null)
            {
                s = sm.AddState("slash" + i, new Vector3(260f, 140f + 70f * i, 0f));
                made++;
            }
            else
            {
                for (int t = s.transitions.Length - 1; t >= 0; t--) s.RemoveTransition(s.transitions[t]);
            }
            s.motion = clip;
            s.writeDefaultValues = false;

            // Always drain back to Empty so a finished swing can never leave the
            // torso frozen mid-slash (the "motion hangs there" symptom).
            AnimatorStateTransition back = s.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.92f;
            back.hasFixedDuration = true;
            back.duration = 0.12f;
            report.Append("  slash").Append(i).Append(" -> Empty   clip='").Append(clip.name)
                  .Append("' len=").Append(clip.length.ToString("0.00")).Append("s\n");
        }

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();

        report.Append("  ").Append(made).Append(" state(s) created. Mask: legs + root OFF, torso/arms/head ON.\n")
              .Append("  Set MechCombat's 'Melee Anim Layer' to ").Append(layerIndex)
              .Append(" (it defaults to 1). Layer weight is driven from code - 1 while swinging, 0 otherwise.\n")
              .Append("  Legs now keep their locomotion pose through the whole string.");
        Debug.Log(report.ToString());
    }

    /// <summary>Forces slash1-4.fbx to import as Humanoid so the clips retarget onto
    /// the Gundam avatar. A Generic import is the usual reason a swapped-in clip
    /// plays as a T-pose or not at all.</summary>
    [MenuItem("Tools/Gundam/9b. Fix SLASH Clip Import (make humanoid)")]
    public static void FixSlashImport()
    {
        int changed = 0;
        for (int i = 1; i <= 4; i++)
        {
            string path = ClipDir + "slash" + i + ".fbx";
            ModelImporter imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) { Debug.LogWarning("[Slash] Not found: " + path); continue; }
            if (imp.animationType != ModelImporterAnimationType.Human)
            {
                imp.animationType = ModelImporterAnimationType.Human;
                imp.SaveAndReimport();
                changed++;
                Debug.Log("[Slash] " + path + " reimported as Humanoid.");
            }
        }
        Debug.Log("[Slash] Import check done - " + changed + " clip(s) converted to Humanoid. " +
                  "Now run 9. Build SLASH 1-4 States.");
    }

    // ---------- helpers ----------

    private static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state != null && cs.state.name == name) return cs.state;
        // one level of sub-state-machines, just in case the melee lives in a group
        foreach (ChildAnimatorStateMachine csm in sm.stateMachines)
            foreach (ChildAnimatorState cs in csm.stateMachine.states)
                if (cs.state != null && cs.state.name == name) return cs.state;
        return null;
    }

    private static Vector3 FindStatePosition(AnimatorStateMachine sm, AnimatorState state)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state == state) return cs.position;
        return new Vector3(300f, 300f, 0f);
    }

    private static AnimationClip LoadHumanoidClip(string fbxPath, System.Text.StringBuilder report)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
        if (all == null || all.Length == 0)
        {
            report.Append("  MISSING FILE: ").Append(fbxPath).Append('\n');
            return null;
        }
        AnimationClip best = null;
        foreach (Object o in all)
        {
            AnimationClip c = o as AnimationClip;
            if (c == null || c.name.StartsWith("__preview__")) continue;
            if (best == null || c.length > best.length) best = c;
        }
        if (best != null && !best.isHumanMotion)
            report.Append("  WARNING: '").Append(best.name)
                  .Append("' is NOT humanoid - run 9b first or it will not retarget onto the Gundam.\n");
        return best;
    }
}
