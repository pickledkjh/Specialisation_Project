using UnityEngine;

/// <summary>
/// Procedural at-rest arm pose for the ENEMY Gundam. The MMD rig's raw humanoid
/// retarget folds the arms up at the chest (the player's Gundam has the same
/// quirk, but MechShooter re-poses the player's arms every frame, masking it -
/// the enemy has no shooter, so its arms crumpled).
///
/// Every LateUpdate this pulls each arm toward a natural mech stance - upper arm
/// hanging down and slightly out, forearm bent a touch forward - using the same
/// axis-free FromToRotation deltas that fixed the player's firing arm, so it
/// works on any rig. It stands down whenever the AI is meleeing, guarding, downed
/// or dead so real action poses read through.
///
/// Installed automatically by MechVfx.Rehook on the enemy's humanoid model
/// (skipped on the player, whose MechShooter owns the arms).
/// </summary>
public class GundamArmPose : MonoBehaviour
{
    [Range(0f, 1f)] public float weight = 0.75f;

    private Animator anim;
    private SimpleMechAI ai;
    private MechHealth health;
    private Transform rUp, rLow, rHand, lUp, lLow, lHand;

    /// <summary>Attach to the enemy AI's humanoid model if not already present.
    /// NOTE: the enemy is a duplicate of the player object, so it can carry a
    /// leftover MechShooter component - only an ENABLED shooter actually poses
    /// arms, so only that skips the install (the old check skipped on the mere
    /// presence of one, which silently blocked the whole fix).</summary>
    public static void TryInstall()
    {
        SimpleMechAI ai = Object.FindFirstObjectByType<SimpleMechAI>();
        if (ai == null) return;
        MechShooter shooter = ai.GetComponent<MechShooter>();
        if (shooter != null && shooter.enabled)
        {
            Debug.Log("[ArmPose] Skipped: enemy has an ENABLED MechShooter posing its arms.");
            return;
        }
        Animator a = ai.animator != null ? ai.animator : ai.GetComponentInChildren<Animator>();
        if (a == null || !a.isHuman)
        {
            Debug.LogWarning("[ArmPose] Enemy animator missing or not humanoid - arm pose not installed.");
            return;
        }
        if (a.GetComponent<GundamArmPose>() != null) return;
        a.gameObject.AddComponent<GundamArmPose>();
        Debug.Log("[ArmPose] Installed on '" + a.gameObject.name + "' (enemy arms now procedurally posed).");
    }

    private void Start()
    {
        anim = GetComponent<Animator>();
        ai = GetComponentInParent<SimpleMechAI>();
        health = GetComponentInParent<MechHealth>();
        if (anim == null || !anim.isHuman) { enabled = false; return; }
        rUp = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rLow = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        lUp = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        lLow = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        lHand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        if (rUp == null || rLow == null || lUp == null || lLow == null) enabled = false;
    }

    private void LateUpdate()
    {
        // Let real action poses through untouched
        if (ai != null && (ai.IsSwinging || ai.IsShieldUp)) return;
        if (health != null && (health.isYellowLocked || health.currentHealth <= 0f)) return;

        Transform root = ai != null ? ai.transform : transform.root;
        PoseArm(rUp, rLow, rHand, root, +1f);
        PoseArm(lUp, lLow, lHand, root, -1f);
    }

    private void PoseArm(Transform up, Transform low, Transform hand, Transform root, float side)
    {
        // Upper arm: the shoulder->elbow line should hang DOWN, a bit out and forward
        Vector3 cur = low.position - up.position;
        if (cur.sqrMagnitude > 1e-6f)
        {
            Vector3 want = (-root.up + root.right * side * 0.28f + root.forward * 0.10f).normalized;
            Quaternion delta = Quaternion.FromToRotation(cur.normalized, want);
            up.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * up.rotation;
        }

        // Forearm: elbow->hand bends slightly forward, mech-holding-a-weapon style
        if (hand != null)
        {
            Vector3 cur2 = hand.position - low.position;
            if (cur2.sqrMagnitude > 1e-6f)
            {
                Vector3 want2 = (-root.up * 0.75f + root.forward * 0.6f + root.right * side * 0.12f).normalized;
                Quaternion delta2 = Quaternion.FromToRotation(cur2.normalized, want2);
                low.rotation = Quaternion.Slerp(Quaternion.identity, delta2, weight * 0.85f) * low.rotation;
            }
        }
    }
}
