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
    private float firedAt;           // for the step-breaks-tracking rule
    private MechController targetMech;   // victim's controller (either may be null)
    private SimpleMechAI targetAI;

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

        Color beam = color ?? new Color(1f, 0.9f, 0.45f); // beam yellow, pushed brighter

        // THIN, BRIGHT BEAM BOLT. The old shot was a fat 0.22-wide lit capsule that
        // read as a flying pill. A beam wants: a narrow core that is pure white at
        // the centre, a soft coloured sheath around it, and a streak behind. All
        // three use an unlit additive material so they glow instead of taking
        // scene lighting, which is what actually sells "energy" over "object".
        GameObject vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(vis.GetComponent<Collider>()); // visual only - the root sphere does the hitting
        vis.transform.SetParent(go.transform, false);
        vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // capsule Y-axis -> flight Z-axis
        vis.transform.localScale = new Vector3(0.075f * scale, 1.25f * scale, 0.075f * scale); // thin + long
        Renderer r = vis.GetComponent<Renderer>();
        if (r != null)
        {
            r.material = BeamMaterial(Color.Lerp(beam, Color.white, 0.75f));
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        GameObject sheath = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(sheath.GetComponent<Collider>());
        sheath.transform.SetParent(go.transform, false);
        sheath.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        sheath.transform.localScale = new Vector3(0.19f * scale, 1.05f * scale, 0.19f * scale);
        Renderer sr = sheath.GetComponent<Renderer>();
        if (sr != null)
        {
            Color glow = beam; glow.a = 0.4f;
            sr.material = BeamMaterial(glow);
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sr.receiveShadows = false;
        }

        // Streak: what makes a fast bolt legible at speed
        TrailRenderer tr = go.AddComponent<TrailRenderer>();
        tr.time = 0.12f;
        tr.startWidth = 0.16f * scale;
        tr.endWidth = 0f;
        tr.numCapVertices = 2;
        tr.material = BeamMaterial(beam);
        tr.startColor = new Color(beam.r, beam.g, beam.b, 0.85f);
        tr.endColor = new Color(beam.r, beam.g, beam.b, 0f);
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return go.AddComponent<HomingProjectile>();
    }

    // Additive unlit: bright regardless of scene lighting, and overlapping layers
    // build up into a hot core - the classic beam read.
    private static Material BeamMaterial(Color c)
    {
        Shader s = Shader.Find("Particles/Standard Unlit");
        if (s == null) s = Shader.Find("Sprites/Default");
        Material m = new Material(s);
        if (m.HasProperty("_Mode")) m.SetFloat("_Mode", 4f); // additive
        m.color = c;
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 2.2f);
        }
        return m;
    }

    /// <summary>
    /// Called by the shooter immediately after spawn. Pass homingTarget = null when
    /// fired OUTSIDE red lock (or with no target) - the shot then flies straight.
    /// </summary>
    public void Init(Transform homingTarget, Transform shooter)
    {
        target = homingTarget;
        shooterRoot = shooter;
        firedAt = Time.time;
        if (target != null)
        {
            targetMech = target.GetComponentInParent<MechController>();
            if (targetMech == null) targetMech = target.GetComponentInChildren<MechController>();
            targetAI = target.GetComponentInParent<SimpleMechAI>();
            if (targetAI == null) targetAI = target.GetComponentInChildren<SimpleMechAI>();
        }

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

        // EXVS RULE: a boost step or dash performed AFTER this shot was fired breaks
        // its tracking permanently - dodging is now a real answer to gunfire.
        // (This is the designed counter promised in the pitch: "dash can make the
        // incoming attack lose the aim assist".)
        if (target != null &&
            ((targetMech != null && targetMech.LastEvadeTime > firedAt) ||
             (targetAI != null && targetAI.LastEvadeTime > firedAt)))
        {
            target = null; // fly straight from here on
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

            if (isMissile) MissileAssets.SpawnExplosion(transform.position, missileFxScale);

            // LONG-RANGE KNOCKDOWNS FLING TOO. Melee finishers sent the victim
            // flying while a bar-filling shot just dropped them on the spot, which
            // made ranged play feel toothless. The launch is carried along the
            // shot's own direction, scaled by how hard the shot hits.
            Vector3 flingDir = transform.forward;
            flingDir.y = 0f;
            if (flingDir.sqrMagnitude < 0.01f) flingDir = Vector3.forward;
            flingDir.Normalize();
            float power = Mathf.Clamp01(knockdownPower / 40f);
            Vector3 launch = flingDir * (shotFlingForward * (0.55f + power))
                           + Vector3.up * (shotFlingUp * (0.55f + power));

            // ---- TEAM RULES: beams and missiles pass through nobody, but a hit on
            // your own partner lands soft. This is the main way friendly fire happens.
            float dmg = damage, bar = knockdownPower, stun = shotStunDuration;
            if (!TeamRules.ResolveHit(shooterRoot, health, ref dmg, ref bar, ref stun))
            {
                Destroy(gameObject);
                return;
            }
            if (TeamRules.WouldBeFriendly(shooterRoot, health)) launch *= 0.3f;

            health.TakeDamage(dmg, bar, launch);

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
                    pc.TakeHit(stun);
                }
                else if (ai != null)
                {
                    ai.TakeHit(stun);
                }
            }

            Destroy(gameObject);
            return;
        }

        // Hit world geometry. Breakable buildings take structural damage
        // (charge shots crack them in two hits) - everything else just eats the shot.
        BreakableBuilding building = other.GetComponentInParent<BreakableBuilding>();
        if (building != null)
        {
            building.TakeHit(damage * 1.5f, transform.position);
        }
        if (isMissile) MissileAssets.SpawnExplosion(transform.position, missileFxScale);
        Destroy(gameObject);
    }

    /// <summary>Set by SpecialMoves on the R barrage: this projectile carries the
    /// missile pack's model and detonates with its explosion instead of vanishing.</summary>
    [HideInInspector] public bool isMissile;
    [HideInInspector] public float missileFxScale = 1f;

    [Header("Knockdown fling")]
    [Tooltip("Forward launch speed applied when THIS shot is the one that fills the knockdown bar. Scaled by the shot's knockdown power, so a charge shot or missile throws them much further than a rifle tap.")]
    public float shotFlingForward = 26f;
    [Tooltip("Upward component of the ranged fling. Enough to get them off the floor and tumbling, lower than the melee finisher's 11.")]
    public float shotFlingUp = 8f;
}
