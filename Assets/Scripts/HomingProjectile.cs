using UnityEngine;

/// <summary>
/// Beam projectile. Homes toward the target for the first half second when fired in
/// red lock, otherwise flies straight. Hits stagger and feed the knockdown bar;
/// downed mechs and raised shields are handled here too.
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
    [Tooltip("Homing only steers during this initial window; afterwards the shot flies straight.")]
    public float homingDuration = 0.5f;
    [Tooltip("Aim height above the target pivot - upper body / chest.")]
    public float aimHeight = 1.5f;

    [Header("Damage")]
    public float damage = 10f;
    [Tooltip("Knockdown bar power per shot. With maxKnockdownValue 100 and decay 5, ~4 quick unanswered shots down a mech - EXVS-ish.")]
    public float knockdownPower = 30f;
    [Tooltip("Stagger applied on hit. EXVS rifle shots always flinch the victim.")]
    public float shotStunDuration = 0.6f;

    // ---- runtime, set by the shooter via Init() ----
    private Transform target;        // null = no homing (green lock / no target)
    private Transform shooterRoot;   // to ignore our own colliders
    private float age;

    /// <summary>Who fired this shot (read by the AI to dodge incoming fire).</summary>
    public Transform ShooterRoot => shooterRoot;

    /// <summary>
    /// Builds a complete runtime projectile with no prefab: trigger sphere, kinematic
    /// rigidbody, capsule visual with the render pipeline's default material.
    /// Used by MechShooter's prefab-less fallback, the AI's gun, and charge shots.
    /// </summary>
    public static HomingProjectile SpawnSimple(Vector3 pos, Quaternion rot, float scale = 1f, Color? color = null)
    {
        GameObject go = new GameObject("Beam Shot");
        go.transform.SetPositionAndRotation(pos, rot);

        SphereCollider col = go.AddComponent<SphereCollider>();
        col.isTrigger = true;
        col.radius = 0.3f * scale; // forgiving EXVS-style hit sphere

        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(vis.GetComponent<Collider>()); // visual only - the root sphere does the hitting
        vis.transform.SetParent(go.transform, false);
        vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // capsule Y-axis -> flight Z-axis
        vis.transform.localScale = new Vector3(0.22f * scale, 0.7f * scale, 0.22f * scale);
        Renderer r = vis.GetComponent<Renderer>();
        if (r != null) r.material.color = color ?? new Color(1f, 0.85f, 0.25f); // beam yellow

        return go.AddComponent<HomingProjectile>();
    }

    /// <summary>
    /// Called by the shooter immediately after spawn. Pass homingTarget = null when
    /// fired OUTSIDE red lock (or with no target) - the shot then flies straight.
    /// </summary>
    public void Init(Transform homingTarget, Transform shooter)
    {
        target = homingTarget;
        shooterRoot = shooter;

        // Belt-and-braces: explicitly ignore every collider on the shooter so a shot
        // spawned inside the muzzle can never clip its own mech.
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
        // Stop homing if the target goes down mid-flight - EXVS shots do not chase
        // a mech that is already falling to the floor.
        if (target != null && age <= homingDuration && !IsTargetYellowLocked())
        {
            Vector3 aimPoint = target.position + Vector3.up * aimHeight;
            Vector3 desired = (aimPoint - transform.position).normalized;
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
        // Never hit the shooter (extra guard on top of IgnoreCollision)
        if (shooterRoot != null && other.transform.IsChildOf(shooterRoot)) return;

        MechHealth health = other.GetComponentInParent<MechHealth>();
        if (health != null)
        {
            // Downed / wake-up protected mechs: pass straight through.
            if (health.isYellowLocked) return;

            // A raised frontal shield absorbs the shot completely (no damage, no
            // flinch, and unlike melee no punishment for the shooter - EXVS rules).
            MechCombat vc = other.GetComponentInParent<MechCombat>();
            SimpleMechAI va = other.GetComponentInParent<SimpleMechAI>();
            if ((vc != null && vc.IsBlocking(transform.position)) ||
                (va != null && va.IsBlocking(transform.position)))
            {
                // Shield flash right where the beam died, so absorbing a shot is
                // visibly a BLOCK and not the projectile mysteriously vanishing.
                CombatVfx.SpawnBlock(transform.position);
                Destroy(gameObject);
                return;
            }

            health.TakeDamage(damage, knockdownPower);

            // EXVS shot flinch - mirrors MeleeHitbox: re-check yellow lock LIVE first,
            // because the damage itself may have just downed or killed them, and the
            // GetHit trigger must not fight HitDown/Die.
            bool downedNow = health.isYellowLocked;
            if (!downedNow)
            {
                MechController pc = other.GetComponentInParent<MechController>();
                SimpleMechAI ai = other.GetComponentInParent<SimpleMechAI>();
                if (pc != null)
                {
                    pc.TakeHit(shotStunDuration);
                }
                else if (ai != null)
                {
                    ai.TakeHit(shotStunDuration);
                }
            }

            Destroy(gameObject);
            return;
        }

        // Hit world geometry (arena floor/walls): just die.
        Destroy(gameObject);
    }
}
