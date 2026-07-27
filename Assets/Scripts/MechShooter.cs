using UnityEngine;

/// <summary>
/// Rifle + aim pose. Handles ammo, cooldown, auto reload, normal and charge shots.
/// If no projectile prefab is set it builds a simple beam in code so shooting works
/// without any setup.
/// </summary>
[DefaultExecutionOrder(100)]
public class MechShooter : MonoBehaviour
{
    [Header("Aiming Setup")]
    public Transform spineBone;
    public Transform armBone;

    [Header("Bone Offsets (Adjust if twisted)")]
    public Vector3 spineAngleOffset;
    public Vector3 armAngleOffset;

    [Header("Aiming Weights")]
    [Range(0f, 1f)] public float spineWeight = 0.4f;
    [Range(0f, 1f)] public float armWeight = 1.0f;

    [Header("Shooting Settings")]
    public float shootDuration = 0.5f;
    private float shootTimer = 0f;

    [Header("Projectile (optional - runtime-built if empty)")]
    [Tooltip("Prefab with a HomingProjectile + trigger collider + kinematic rigidbody. Leave EMPTY to auto-build a simple capsule shot in code.")]
    public HomingProjectile projectilePrefab;
    [Tooltip("Barrel tip transform, +Z out of the barrel. Leave EMPTY to spawn at chest height in front of the mech.")]
    public Transform muzzle;
    [Tooltip("Shots only home if the target is inside this range when fired. Keep in sync with MechCombat.redLockRange (40).")]
    public float redLockRange = 40f;

    [Header("Ammo")]
    public int maxAmmo = 8;
    public int currentAmmo;
    [Tooltip("Minimum time between shots (~EXVS rifle rhythm).")]
    public float shotCooldown = 0.6f;
    [Tooltip("EXVS-style auto reload: an EMPTY magazine refills completely after this many seconds.")]
    public float reloadTime = 5f;

    [Header("Charge Shot (special move - hold the shoot button, release when charged)")]
    public float chargeDamage = 30f;
    [Tooltip("60 = a charge shot alone fills over half the knockdown bar; landing one on a softened enemy downs them instantly.")]
    public float chargeKnockdownPower = 60f;
    public float chargeSpeed = 80f;
    public int chargeAmmoCost = 2;

    private float lastShotTime = -99f;
    private float emptySince = -1f; // -1 = not reloading

    // For the HUD
    public bool IsReloading => currentAmmo <= 0 && emptySince >= 0f;
    public float ReloadProgress01 => IsReloading ? Mathf.Clamp01((Time.time - emptySince) / reloadTime) : 1f;

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
        Time.time - lastShotTime >= shotCooldown;

    /// <summary>Same entry point as always - called by MechCombat on the shoot input.</summary>
    public void FireWeapon()
    {
        if (!CanFire) return;

        lastShotTime = Time.time;
        currentAmmo--;
        if (currentAmmo <= 0) emptySince = Time.time;

        shootTimer = shootDuration; // hold the aim pose (original behavior)
        SpawnAimedShot(false);
    }

    /// <summary>
    /// Special move: released after holding the shoot button. Bigger, faster, red,
    /// heavy knockdown power - lands like a truck but costs 2 ammo.
    /// </summary>
    public void FireChargeShot()
    {
        if (currentAmmo < chargeAmmoCost || Time.time - lastShotTime < shotCooldown) return;

        lastShotTime = Time.time;
        currentAmmo -= chargeAmmoCost;
        if (currentAmmo <= 0) emptySince = Time.time;

        shootTimer = shootDuration * 1.5f; // hold the aim pose a touch longer
        SpawnAimedShot(true);
    }

    private void SpawnAimedShot(bool charged)
    {
        Transform target = GetTarget();

        // Spawn at the muzzle (or chest height fallback), aimed at the target's upper body
        Vector3 spawnPos = muzzle != null
            ? muzzle.position
            : transform.position + transform.forward * 1.2f + Vector3.up * 1.5f;
        Vector3 aimPoint = target != null
            ? target.position + Vector3.up * 1.5f
            : spawnPos + transform.forward * 100f;
        Vector3 aimDir = (aimPoint - spawnPos);
        Quaternion spawnRot = aimDir.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(aimDir.normalized)
            : transform.rotation;

        HomingProjectile shot;
        if (projectilePrefab != null)
        {
            shot = Instantiate(projectilePrefab, spawnPos, spawnRot);
            if (charged) shot.transform.localScale *= 1.8f;
        }
        else
        {
            shot = charged
                ? HomingProjectile.SpawnSimple(spawnPos, spawnRot, 2f, new Color(1f, 0.35f, 0.2f)) // big red
                : HomingProjectile.SpawnSimple(spawnPos, spawnRot);
        }

        if (charged)
        {
            shot.damage = chargeDamage;
            shot.knockdownPower = chargeKnockdownPower;
            shot.speed = chargeSpeed;
            shot.turnRateDegPerSec = 60f; // heavier shot, lazier curve
        }

        // Red lock at the moment of firing => the shot homes. Green lock / no target /
        // downed target => straight shot with no tracking (EXVS rule).
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
    // Original aim-pose logic - unchanged
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
