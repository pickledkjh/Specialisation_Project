using UnityEngine;

/// <summary>
/// DRAFT — lives in CoworkHandoff_Output/Drafts (NOT compiled by Unity).
/// EXVS-style beam/bullet: flies forward fast; if it was fired while the target was
/// inside red lock range, it curves toward the target's upper body with a capped turn
/// rate for the first ~half second, then flies dead straight. Fired outside red lock
/// (green lock), it never homes at all.
///
/// Spawned and configured by MechShooterV2.Fire(). See WIRING_GUIDE_SHOOTING.md for
/// prefab/layer setup so it can never hit its own shooter.
/// </summary>
public class HomingProjectile : MonoBehaviour
{
    [Header("Flight")]
    [Tooltip("Forward flight speed (u/s). EXVS beams read well around 60.")]
    public float speed = 60f;
    [Tooltip("Lifetime in seconds before self-destructing (range = speed * lifetime).")]
    public float lifetime = 4f;

    [Header("Homing (only active when fired in red lock)")]
    [Tooltip("Max turn rate toward the target, degrees per second. 120 = gentle EXVS curve, not a heat-seeker.")]
    public float turnRateDegPerSec = 120f;
    [Tooltip("Homing only steers during this initial window; afterwards the shot flies straight (EXVS behavior — you can dodge the tail end).")]
    public float homingDuration = 0.5f;
    [Tooltip("Aim height above the target pivot — upper body / chest. NOT applied to the shooter's own height, so no floating-fight bug here.")]
    public float aimHeight = 1.5f;

    [Header("Damage")]
    public float damage = 10f;
    [Tooltip("Knockdown bar power per shot. MechHealth.maxKnockdownValue is 100; 30 means ~4 unanswered shots down a mech.")]
    public float knockdownPower = 30f;

    // ---- runtime, set by the shooter via Init() ----
    private Transform target;        // null = no homing (green lock / no target)
    private Transform shooterRoot;   // to ignore our own colliders
    private float age;

    /// <summary>
    /// Called by the shooter immediately after Instantiate.
    /// Pass homingTarget = null when the shot was fired OUTSIDE red lock
    /// (or with no target) — the projectile then just flies straight.
    /// </summary>
    public void Init(Transform homingTarget, Transform shooter)
    {
        target = homingTarget;
        shooterRoot = shooter;

        // Belt-and-braces on top of the layer matrix (see wiring guide):
        // explicitly ignore every collider on the shooter so a shot spawned inside
        // the muzzle can never clip its own mech.
        if (shooterRoot != null)
        {
            Collider myCol = GetComponent<Collider>();
            if (myCol != null)
            {
                foreach (Collider c in shooterRoot.GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(myCol, c, true);
            }
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            Destroy(gameObject);
            return;
        }

        // Curve toward the target's upper body during the homing window only.
        // Stop homing early if the target goes down (yellow lock) mid-flight —
        // EXVS shots do not chase a mech that is already on the floor.
        if (target != null && age <= homingDuration && !IsTargetYellowLocked())
        {
            Vector3 aimPoint = target.position + Vector3.up * aimHeight;
            Vector3 desired = (aimPoint - transform.position).normalized;
            // Capped turn rate: rotate at most turnRateDegPerSec toward the aim point
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(desired),
                turnRateDegPerSec * Time.deltaTime);
        }

        transform.position += transform.forward * speed * Time.deltaTime;
    }

    private bool IsTargetYellowLocked()
    {
        MechHealth h = target.GetComponentInParent<MechHealth>();
        if (h == null) h = target.GetComponentInChildren<MechHealth>();
        return h != null && h.isYellowLocked;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Never hit the shooter (extra guard; IgnoreCollision + layers should already prevent this)
        if (shooterRoot != null && other.transform.IsChildOf(shooterRoot)) return;

        MechHealth health = other.GetComponentInParent<MechHealth>();
        if (health != null)
        {
            // Skip yellow-locked (downed / wake-up protected) mechs entirely:
            // pass through instead of exploding on an invulnerable body.
            if (health.isYellowLocked) return;

            health.TakeDamage(damage, knockdownPower);
            // TODO(owner): also call the victim's TakeHit/StartHitStop for shot
            // flinch, mirroring MeleeHitbox — left out of the draft to keep the
            // projectile self-contained.
            Destroy(gameObject);
            return;
        }

        // Hit world geometry (arena floor/walls): just die.
        // The layer matrix (wiring guide) keeps this from triggering on lock-on
        // UI triggers or other projectiles.
        Destroy(gameObject);
    }
}
