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
    [Tooltip("Optional: the forearm. Aiming this too straightens the whole arm at the target while firing - it makes the mech visibly point the gun.")]
    public Transform forearmBone;

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
        CombatVfx.SpawnMuzzleFlash(muzzle != null ? muzzle : transform, MuzzleWorldPos());
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
        CombatVfx.SpawnMuzzleFlash(muzzle != null ? muzzle : transform, MuzzleWorldPos());
        SpawnAimedShot(true);
    }

    private Vector3 MuzzleWorldPos()
    {
        return muzzle != null
            ? muzzle.position
            : transform.position + transform.forward * 1.2f + Vector3.up * 1.5f;
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
        shot.Init(IsRedLock(target) ? target : null, transform);
    }

    /// <summary>
    /// THE definition of red lock. The lock-on reticle calls this exact method, so
    /// what the circle shows and what the bullet actually does cannot disagree -
    /// which is precisely how the old display drifted out of sync (it measured its
    /// own distance, against its own range value, on its own schedule).
    /// </summary>
    public bool IsRedLock(Transform t)
    {
        return t != null &&
               Vector3.Distance(transform.position, t.position) <= redLockRange &&
               !IsYellowLocked(t);
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
                Vector3 targetPos = target.position + (Vector3.up * 1.5f);

                // AXIS-FREE aiming. The old LookRotation + per-rig Euler offsets
                // twisted the Gundam's arm into weird poses when firing (MMD bone
                // axes differ from Mixamo's). Instead, rotate each bone by the
                // small WORLD-SPACE delta that swings the actual limb direction
                // onto the target line - works on any skeleton, any bone axes.

                // 1. Torso: lean the chest toward the target (damped vertical)
                if (spineBone != null)
                {
                    Vector3 want = targetPos - spineBone.position;
                    want.y *= 0.4f;
                    if (want.sqrMagnitude > 0.01f)
                    {
                        Quaternion delta = Quaternion.FromToRotation(transform.forward, want.normalized);
                        spineBone.rotation = Quaternion.Slerp(Quaternion.identity, delta, spineWeight) * spineBone.rotation;
                    }
                }

                // 2. Upper arm: swing the shoulder->elbow segment at the target
                if (armBone != null)
                {
                    Vector3 cur = forearmBone != null ? (forearmBone.position - armBone.position) : transform.forward;
                    Vector3 want = targetPos - armBone.position;
                    if (cur.sqrMagnitude > 0.0005f && want.sqrMagnitude > 0.01f)
                    {
                        Quaternion delta = Quaternion.FromToRotation(cur.normalized, want.normalized);
                        armBone.rotation = Quaternion.Slerp(Quaternion.identity, delta, armWeight) * armBone.rotation;
                    }
                }

                // 3. Forearm fine-correct: point the gun line (forearm->muzzle) dead on
                if (forearmBone != null && muzzle != null)
                {
                    Vector3 cur = muzzle.position - forearmBone.position;
                    Vector3 want = targetPos - forearmBone.position;
                    if (cur.sqrMagnitude > 0.0005f && want.sqrMagnitude > 0.01f)
                    {
                        Quaternion delta = Quaternion.FromToRotation(cur.normalized, want.normalized);
                        forearmBone.rotation = Quaternion.Slerp(Quaternion.identity, delta, 0.65f) * forearmBone.rotation;
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
