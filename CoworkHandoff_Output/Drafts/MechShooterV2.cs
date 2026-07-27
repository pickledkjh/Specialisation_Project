using UnityEngine;

/// <summary>
/// DRAFT — lives in CoworkHandoff_Output/Drafts (NOT compiled by Unity).
/// Drop-in replacement for MechShooter that KEEPS the existing aim-pose / bone-IK
/// behavior verbatim and ADDS: ammo, per-shot cooldown, automatic reload over time
/// when empty, a muzzle Transform, and actual projectile spawning aimed at the
/// locked target (HomingProjectile).
///
/// Deliberately still named a NEW class so it can sit next to MechShooter during
/// testing. See WIRING_GUIDE_SHOOTING.md for the safe swap procedure.
///
/// ---- What an AI-usable version needs (for SimpleMechAI) ----
/// 1. FireWeapon() already has no player-input dependency, so the AI can call it
///    directly; the blockers are the target sources below (TargetManager /
///    MechController are player components). Extract GetTarget() behind a serialized
///    Transform or a small ITargetProvider interface, and have SimpleMechAI assign
///    its playerTarget there.
/// 2. The AI should check CanFire before committing to a shoot decision, and vary
///    fire timing (don't shoot every cooldown tick — telegraph like the melee AI
///    waits politely while the player is downed).
/// 3. Mirror MechCombat's gate: don't fire while the target is yellow-locked
///    (SimpleMechAI.ThinkAndMove already skips downed players — keep that).
/// 4. Ammo/reload can be shared as-is; consider a longer AI cooldown for fairness.
/// </summary>
[DefaultExecutionOrder(100)]
public class MechShooterV2 : MonoBehaviour
{
    [Header("Aiming Setup (unchanged from MechShooter)")]
    public Transform spineBone;
    public Transform armBone;

    [Header("Bone Offsets (Adjust if twisted)")]
    public Vector3 spineAngleOffset;
    public Vector3 armAngleOffset;

    [Header("Aiming Weights")]
    [Range(0f, 1f)] public float spineWeight = 0.4f;
    [Range(0f, 1f)] public float armWeight = 1.0f;

    [Header("Shooting Settings")]
    [Tooltip("How long the aim pose is held per shot (same meaning as before).")]
    public float shootDuration = 0.5f;

    [Header("NEW — Projectile")]
    [Tooltip("Prefab with HomingProjectile + collider + kinematic rigidbody. See wiring guide.")]
    public HomingProjectile projectilePrefab;
    [Tooltip("Empty child at the gun barrel tip, +Z pointing out of the barrel.")]
    public Transform muzzle;
    [Tooltip("Shots only home if the target is inside this range at the moment of firing. 40 matches MechCombat.redLockRange — keep them in sync by hand (or read it off MechCombat at Start).")]
    public float redLockRange = 40f;

    [Header("NEW — Ammo")]
    public int maxAmmo = 8;
    public int currentAmmo;
    [Tooltip("Minimum time between shots.")]
    public float shotCooldown = 0.6f;
    [Tooltip("EXVS-style auto reload: when the magazine hits EMPTY it refills after this many seconds (all 8 at once). Swap to per-round regen by reloading 1 round every (reloadTime / maxAmmo) if you prefer.")]
    public float reloadTime = 5f;

    private float shootTimer = 0f;      // aim-pose timer (original behavior)
    private float lastShotTime = -99f;
    private float emptySince = -1f;     // when the mag hit 0; -1 = not reloading

    private TargetManager targetManager;
    private MechController mechController;
    private MechCombat mechCombat;

    private void Start()
    {
        targetManager = GetComponent<TargetManager>();
        mechController = GetComponent<MechController>();
        mechCombat = GetComponent<MechCombat>();
        currentAmmo = maxAmmo;
    }

    private void Update()
    {
        // Auto reload: empty magazine refills after reloadTime
        if (currentAmmo <= 0 && emptySince >= 0f && Time.time - emptySince >= reloadTime)
        {
            currentAmmo = maxAmmo;
            emptySince = -1f;
        }
    }

    public bool CanFire =>
        currentAmmo > 0 &&
        Time.time - lastShotTime >= shotCooldown &&
        projectilePrefab != null;

    /// <summary>
    /// Same entry point name as MechShooter, so MechCombat.HandleInputs needs no
    /// changes beyond the component swap. Now actually fires a projectile.
    /// </summary>
    public void FireWeapon()
    {
        if (!CanFire)
        {
            // Keep the aim-pose feedback even on a dry/cooldown trigger pull?
            // EXVS does a "click" — leaving the pose out reads better. No-op.
            return;
        }

        lastShotTime = Time.time;
        currentAmmo--;
        if (currentAmmo <= 0) emptySince = Time.time;

        shootTimer = shootDuration; // hold the aim pose (original behavior)

        Transform target = GetTarget();

        // Spawn at the muzzle, facing the target's upper body (or straight ahead).
        Vector3 spawnPos = muzzle != null ? muzzle.position
                                          : transform.position + transform.forward * 1.5f + Vector3.up * 1.5f;
        Vector3 aimPoint = target != null ? target.position + Vector3.up * 1.5f
                                          : spawnPos + transform.forward * 100f;
        Quaternion spawnRot = Quaternion.LookRotation((aimPoint - spawnPos).normalized);

        HomingProjectile shot = Instantiate(projectilePrefab, spawnPos, spawnRot);

        // Red lock at the moment of firing => the shot homes. Green lock / no target
        // / yellow-locked target => straight shot with no tracking.
        bool redLock = target != null &&
                       Vector3.Distance(transform.position, target.position) <= redLockRange &&
                       !IsYellowLocked(target);
        shot.Init(redLock ? target : null, transform);
    }

    private bool IsYellowLocked(Transform t)
    {
        if (mechCombat != null) return mechCombat.IsTargetYellowLocked(t);
        MechHealth h = t.GetComponentInParent<MechHealth>();
        return h != null && h.isYellowLocked;
    }

    // ------------------------------------------------------------------
    // Everything below is IDENTICAL to the original MechShooter aim logic
    // ------------------------------------------------------------------

    private void LateUpdate()
    {
        bool isShooting = shootTimer > 0f;

        if (isShooting)
        {
            Transform target = GetTarget();
            if (target != null)
            {
                // Aim slightly up so they shoot at the chest/head, not the feet
                Vector3 targetPos = target.position + (Vector3.up * 1.5f);

                // 1. Rotate the Upper Body (Spine) FIRST
                if (spineBone != null)
                {
                    Vector3 spineDirection = targetPos - spineBone.position;
                    if (spineDirection != Vector3.zero)
                    {
                        Quaternion spineLookRot = Quaternion.LookRotation(spineDirection) * Quaternion.Euler(spineAngleOffset);
                        // Slerp blends the animation with the procedural aim so it doesn't snap unnaturally
                        spineBone.rotation = Quaternion.Slerp(spineBone.rotation, spineLookRot, spineWeight);
                    }
                }

                // 2. Rotate the Arm SECOND (To perfectly align the weapon)
                if (armBone != null)
                {
                    Vector3 armDirection = targetPos - armBone.position;
                    if (armDirection != Vector3.zero)
                    {
                        Quaternion armLookRot = Quaternion.LookRotation(armDirection) * Quaternion.Euler(armAngleOffset);
                        armBone.rotation = Quaternion.Slerp(armBone.rotation, armLookRot, armWeight);
                    }
                }
            }
            shootTimer -= Time.deltaTime;
        }
    }

    private Transform GetTarget()
    {
        if (targetManager != null && targetManager.currentTarget != null)
            return targetManager.currentTarget;
        if (mechController != null && mechController.enemyTarget != null)
            return mechController.enemyTarget;
        return null;
    }
}
