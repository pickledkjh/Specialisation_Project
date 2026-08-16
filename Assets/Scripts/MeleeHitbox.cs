using UnityEngine;

public class MeleeHitbox : MonoBehaviour
{
    [Header("Who does this hitbox hurt?")]
    public string targetTag = "Enemy";

    [Header("Script References (Assign ONE)")]
    public MechCombat playerCombatScript;
    public SimpleMechAI aiCombatScript;

    [Header("Hit Settings")]
    public float hitStopDuration = 0.1f;
    [Tooltip("Damage per melee hit. 8 = a full 8-hit string deals ~64 of 100 HP, so fights last a few exchanges instead of one combo. (Renamed from damage - the old scene value of 20 made every combo nearly lethal.)")]
    public float damagePerHit = 8f;
    [Tooltip("Bar power per hit when no combat script drives it (AI fists). Matches the player's 14. (Renamed from defaultKnockdownPower so the stronger default applies.)")]
    public float baseBarPower = 12f; // matches the player's per-hit bar power - a full string is ~half the bar
    [Tooltip("Stagger applied to the victim per hit. Long enough that ending your combo leaves the victim staggered PAST your end lag, so they cannot instantly counter-attack. (Renamed from hitStunDuration so the new default beats the stale 0.9 scene value.)")]
    public float hitStunSeconds = 1.3f;

    [Header("Launch (applied only when this hit causes a knockdown)")]
    public float launchForwardSpeed = 9f;
    public float launchUpSpeed = 5f;

    [Header("Shield / Parry")]
    [Tooltip("Stagger applied to the ATTACKER when this melee is blocked - the parry punish window.")]
    public float shieldStunSeconds = 1.5f;
    [Tooltip("Console logging for the whole parry chain. Leave ON until blocking is confirmed working, then untick.")]
    public bool meleeDebugLogs = false; // renamed from logParryDebug: new FALSE default beats the stale scene value - clean submission console

    [Header("Reach")]
    [Tooltip("The hitbox's true WORLD-space radius, whatever scale the bone chain multiplies in. 1.5 = very generous EXVS melee reach (0.35/0.6/0.85 whiffed too much; the unclamped MMD wrist scale once made a 39-unit sphere that hit from anywhere). Awake resizes the sphere to exactly this every play session. (Renamed from meleeReach so the new 1.5 default beats any stale scene value.)")]
    public float meleeReachRadius = 1.5f;

    public float hitCooldown = 0.4f;
    private float lastHitTime = -99f;

    private SphereCollider sphere;        // cached collider (animation events may still toggle it)
    private MechCombat ownerCombat;       // whose arm this fist is on (hierarchy, not Inspector)
    private SimpleMechAI ownerAI;
    private bool announcedLive = false;   // one-time proof in the console that this system runs

    private void Awake()
    {
        sphere = GetComponent<SphereCollider>();
        ownerCombat = GetComponentInParent<MechCombat>();
        ownerAI = GetComponentInParent<SimpleMechAI>();
        if (ownerCombat == null && ownerAI == null) { ownerCombat = playerCombatScript; ownerAI = aiCombatScript; }
        ResizeToWorldRadius();

        // A trigger only fires events when at least ONE collider in the pair has a
        // Rigidbody. The enemy's bone hurtboxes have none, so a fist that crossed
        // only bones (and missed the CharacterController capsule) hit NOTHING -
        // the "sometimes melee doesn't connect" whiff. A kinematic body on the
        // fist makes every pair valid, with zero effect on movement (bone-driven).
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
    }

    // ACTIVE OVERLAP SCAN - the authoritative hit detection. Unity's trigger
    // callbacks proved unreliable for this setup: CharacterController-vs-trigger
    // events largely fire only when the CC ITSELF moves, so slashing a standing
    // enemy produced nothing (the old 39u sphere hid this - the enemy was inside
    // it the moment it moved at all). Instead of waiting for the physics engine
    // to volunteer a callback, a LIVE fist now asks directly every physics step:
    // "what colliders are inside my sphere right now?" OverlapSphere sees CCs,
    // static colliders, and triggers alike, moving or not - it cannot miss.
    // OnTriggerEnter/Stay stay in as a harmless backup; hitCooldown gates dupes.
    private static readonly Collider[] overlapBuf = new Collider[24];

    /// <summary>While the beam saber is lit, MechCombat parks this on the blade and
    /// the hit scan follows the BLADE instead of the fist bone. Null = bone.</summary>
    [HideInInspector] public Transform followPoint;

    /// <summary>Live = the collider was enabled by an animation event (works fine
    /// on the enemy's original rig, and for the code-enabled dash tackle) OR the
    /// player's combat state says a swing's hit window is open right now - the
    /// fallback for the Gundam, whose retargeted clips stopped delivering the
    /// Enable/Disable events. The AI deliberately has NO state fallback: its
    /// events work, and a state-wide window would machine-gun the player.</summary>
    private bool FistIsLive()
    {
        if (sphere != null && sphere.enabled) return true;
        if (ownerCombat != null && ownerCombat.IsSwinging) return true;
        return false;
    }

    private void FixedUpdate()
    {
        if (!FistIsLive()) return;

        if (!announcedLive && meleeDebugLogs)
        {
            announcedLive = true;
            Debug.Log("[Melee] active-scan LIVE on '" + name + "' (reach " + meleeReachRadius +
                      "u, owner=" + (ownerCombat != null ? ownerCombat.name : ownerAI != null ? ownerAI.name : "NONE") + ")");
        }

        // followPoint set = the BEAM SABER is out, and the blade is the weapon: the
        // scan centres on the blade instead of the fist bone, so contact happens
        // where the glow is and the string gains the saber's reach.
        Vector3 center = followPoint != null
            ? followPoint.position
            : (sphere != null ? transform.TransformPoint(sphere.center) : transform.position);
        // Finisher assist: the last swing reaches further (the punch4 clip's contact
        // drifts) - a whiffed finisher means no fling and no knockdown, so it gets
        // the benefit of the doubt.
        float reach = meleeReachRadius;
        if (ownerCombat != null && ownerCombat.IsFinisherSwing) reach *= 1.7f;
        int n = Physics.OverlapSphereNonAlloc(center, reach, overlapBuf, ~0, QueryTriggerInteraction.Collide);

        // Throttled X-ray of what the fist is touching - remove once melee is confirmed
        if (meleeDebugLogs && Time.frameCount % 90 == 0)
        {
            string names = "";
            for (int i = 0; i < n && i < 6; i++)
                if (overlapBuf[i] != null) names += overlapBuf[i].name + "(tag:" + overlapBuf[i].tag + ") ";
            Debug.Log("[Melee] '" + name + "' scanning at " + center.ToString("F1") +
                      " r=" + meleeReachRadius + " -> " + n + " colliders: " + names);
        }

        for (int i = 0; i < n; i++)
        {
            Collider c = overlapBuf[i];
            if (c == null || c.transform.root == transform.root) continue; // never self-hit
            TryHit(c);
        }
    }

    /// <summary>Sets the SphereCollider so its true world radius equals
    /// meleeReachRadius, whatever scale the parent bone chain multiplies in.
    /// Called on Awake and by the editor resize menu - one source of truth.</summary>
    public void ResizeToWorldRadius()
    {
        SphereCollider sc = GetComponent<SphereCollider>();
        if (sc == null) return;
        Vector3 ls = transform.lossyScale;
        float s = Mathf.Max(Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y)), Mathf.Max(Mathf.Abs(ls.z), 0.0001f));
        float world = sc.radius * s;
        if (Mathf.Abs(world - meleeReachRadius) > 0.02f)
        {
            if (world > meleeReachRadius + 1f)
                Debug.LogWarning("[MeleeHitbox] '" + name + "' world radius was " +
                                 world.ToString("0.00") + "u - resized to " + meleeReachRadius + "u.");
            sc.radius = meleeReachRadius / s;
            sc.center = Vector3.zero;
        }
    }

    // Called by MechCombat.TriggerPunch each swing so the cooldown can never
    // swallow a fast follow-up hit (the old bug that broke mashed chains).
    public void ResetCooldown() { lastHitTime = -99f; }

    // Enter alone missed hits: a fast lunge can tunnel past thin colliders in one
    // physics step, and an Enter that happens the same frame the collider is
    // enabled is occasionally swallowed. Stay fires every physics frame while
    // overlapping, and the hitCooldown gate keeps it from multi-hitting.
    private void OnTriggerEnter(Collider other) { TryHit(other); }
    private void OnTriggerStay(Collider other) { TryHit(other); }

    private void TryHit(Collider other)
    {
        if (Time.time - lastHitTime < hitCooldown) return;

        MechHealth health = other.GetComponentInParent<MechHealth>();

        // FIX: also accept the tag on the mech root, so untagged child colliders
        // (hurtbox bones, etc.) no longer make punches silently whiff.
        bool tagMatches = other.CompareTag(targetTag) || (health != null && health.CompareTag(targetTag));
        if (TeamRules.TeamModeActive)
        {
            // In a 2v2 the tag is meaningless: your partner is a clone of an enemy and
            // carries the enemy tag. Anything with a MechHealth that is not this fist's
            // own owner is a valid contact, and TeamRules decides what it costs.
            if (health == null) return;
            Transform fistOwner = ownerAI != null ? ownerAI.transform
                                : ownerCombat != null ? ownerCombat.transform : transform.root;
            if (fistOwner != null && health.transform == fistOwner) return;
        }
        else if (!tagMatches) return;

        if (health != null && health.isYellowLocked)
        {
            // Diagnostic: if this spams while the victim looks like it's standing,
            // the victim is STUCK in the downed state - a state bug, not a whiff.
            if (meleeDebugLogs && Time.frameCount % 60 == 0)
                Debug.Log("[Melee] contact skipped - victim '" + health.name + "' is yellow-locked (downed/invulnerable)");
            return;
        }

        // ONE hit per swing (player side): this swing's stamp may already be spent -
        // the fist stays inside the enemy for many physics steps, but only the first
        // contact of each swing counts. The AI still uses its event windows + cooldown.
        if (ownerCombat != null && !ownerCombat.CanConsumeMeleeHit) return;

        lastHitTime = Time.time;
        if (ownerCombat != null) ownerCombat.ConsumeMeleeHit();

        // ---- SHIELD CHECK: a raised frontal guard blocks the hit and STUNS the
        // attacker (EXVS guard punish). No damage, no bar, no victim stagger.
        // The attacker is derived from this hitbox's OWN hierarchy (the fist knows
        // whose arm it's on) - never from the Inspector references, which can be
        // cross-assigned and would stun the wrong mech. ----
        MechCombat attackerCombat = GetComponentInParent<MechCombat>();
        SimpleMechAI attackerAI = GetComponentInParent<SimpleMechAI>();
        if (attackerCombat == null && attackerAI == null)
        {
            attackerCombat = playerCombatScript;
            attackerAI = aiCombatScript;
        }
        Transform attackerT = attackerAI != null ? attackerAI.transform
                             : attackerCombat != null ? attackerCombat.transform
                             : transform.root;

        MechCombat victimCombat = other.GetComponentInParent<MechCombat>();
        SimpleMechAI victimAI = other.GetComponentInParent<SimpleMechAI>();
        bool blocked = (victimCombat != null && victimCombat != attackerCombat && victimCombat.IsBlocking(attackerT.position)) ||
                       (victimAI != null && victimAI != attackerAI && victimAI.IsBlocking(attackerT.position));

        if (meleeDebugLogs && (victimCombat != null || victimAI != null))
        {
            Transform victimT = victimCombat != null ? victimCombat.transform : victimAI.transform;
            Vector3 toAtk = attackerT.position - victimT.position; toAtk.y = 0f;
            float dot = toAtk.sqrMagnitude > 0.0001f ? Vector3.Dot(victimT.forward, toAtk.normalized) : 1f;
            bool victimShieldUp = victimCombat != null ? victimCombat.IsShielding : victimAI.IsShieldUp;
            Debug.Log("[Parry] melee contact: victim=" + victimT.name +
                      " shieldUp=" + victimShieldUp +
                      " facingDot=" + dot.ToString("0.00") +
                      " blocked=" + blocked +
                      " attacker=" + attackerT.name +
                      " | hitCollider=" + other.name +
                      " colliderType=" + other.GetType().Name + (other.isTrigger ? "(trigger)" : "") +
                      " fistPos=" + transform.position.ToString("F1") +
                      " colliderPos=" + other.transform.position.ToString("F1") +
                      " mechDist=" + Vector3.Distance(attackerT.position, victimT.position).ToString("F1"));
        }

        if (blocked)
        {
            // PARRY: the attacker eats the stun - the blocker's punish window.
            // TakeHit first (it stops coroutines), then the clang freeze.
            if (attackerAI != null)
            {
                attackerAI.TakeHit(shieldStunSeconds);
                attackerAI.StartHitStop(0.15f);
                if (meleeDebugLogs) Debug.Log("[Parry] BLOCKED! Stunned attacker (AI) for " + shieldStunSeconds + "s");
            }
            else if (attackerCombat != null)
            {
                MechController attackerMech = attackerCombat.GetComponent<MechController>();
                if (attackerMech != null) attackerMech.TakeHit(shieldStunSeconds);
                attackerCombat.StartHitStop(0.15f);
                if (meleeDebugLogs) Debug.Log("[Parry] BLOCKED! Stunned attacker (player) for " + shieldStunSeconds + "s");
            }
            if (victimCombat != null) victimCombat.RegisterBlock(); // tutorial tracking

            // ---- parry feedback ----
            // Shield flash between the two mechs + electricity stuck to the STUNNED
            // attacker for the whole punish window, so a successful parry is
            // unmistakable instead of just "the hit didn't land".
            Transform blockerT = victimCombat != null ? victimCombat.transform : victimAI.transform;
            Vector3 toAttacker = attackerT.position - blockerT.position;
            toAttacker.y = 0f;
            toAttacker = toAttacker.sqrMagnitude > 0.01f ? toAttacker.normalized : blockerT.forward;
            CombatVfx.SpawnBlock(blockerT.position + Vector3.up * 1.3f + toAttacker * 0.8f);
            CombatVfx.SpawnParry(attackerT);
            return;
        }

        // Bar power and launch strength come from whoever owns this hitbox.
        // EXVS single-system rule: EVERY hit feeds the bar; whichever hit fills it
        // causes the down. Finisher hits carry big bar power + the big fling; a
        // mid-string bar-fill uses this hitbox's small launch (weak down).
        float finalKnockdown = baseBarPower;
        bool finisher = false;
        float bigForward = 0f, bigUp = 0f;
        if (playerCombatScript != null)
        {
            finalKnockdown = playerCombatScript.GetCurrentKnockdownPower();
            finisher = playerCombatScript.IsFinisherHit;
            bigForward = playerCombatScript.finisherFlingForward;
            bigUp = playerCombatScript.finisherFlingUp;
        }
        else if (aiCombatScript != null)
        {
            finalKnockdown = aiCombatScript.GetCurrentKnockdownPower(baseBarPower);
            finisher = aiCombatScript.IsFinisherHit;
            bigForward = aiCombatScript.finisherFlingForward;
            bigUp = aiCombatScript.finisherFlingUp;
        }

        // Launch direction: away from the attacker, horizontally
        Transform attacker = attackerT;

        // ---- TEAM RULES: a fist that lands on your own partner is softened, not free ----
        float stunSeconds = hitStunSeconds;
        bool friendly = TeamRules.WouldBeFriendly(attacker, health);

        if (health != null)
        {
            Vector3 dir = health.transform.position - attacker.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? attacker.forward : dir.normalized;
            Vector3 launch = finisher
                ? dir * bigForward + Vector3.up * bigUp
                : dir * launchForwardSpeed + Vector3.up * launchUpSpeed;
            float dmg = damagePerHit;
            if (!TeamRules.ResolveHit(attacker, health, ref dmg, ref finalKnockdown, ref stunSeconds)) return;
            if (friendly) launch *= 0.3f; // never fling your own partner across the map
            // The launch is only applied by MechHealth if THIS hit fills the knockdown bar
            health.TakeDamage(dmg, finalKnockdown, launch);
        }

        if (targetTag == "Player")
        {
            MechController playerTarget = other.GetComponentInParent<MechController>();

            if (aiCombatScript != null)
            {
                aiCombatScript.StartHitStop(hitStopDuration);
                aiCombatScript.RegisterHit();
            }

            // Re-check LIVE: the damage (or a forced finisher) may have just downed/killed them.
            // Skipping TakeHit here stops the "GetHit" trigger from fighting "HitDown"/"Die".
            bool downedNow = health != null && health.isYellowLocked;
            if (playerTarget != null && !downedNow)
            {
                playerTarget.TakeHit(stunSeconds);         // TakeHit FIRST (it calls StopAllCoroutines)
                playerTarget.StartHitStop(hitStopDuration);   // then the victim-side freeze survives
            }
        }
        else if (targetTag == "Enemy")
        {
            SimpleMechAI aiTarget = other.GetComponentInParent<SimpleMechAI>();

            if (playerCombatScript != null)
            {
                playerCombatScript.StartHitStop(hitStopDuration);
                playerCombatScript.RegisterHit();   // punch 4 forces the knockdown inside here
            }

            bool downedNow = health != null && health.isYellowLocked;
            if (aiTarget != null && !downedNow)
            {
                aiTarget.TakeHit(stunSeconds);
                aiTarget.StartHitStop(hitStopDuration);
            }
        }
    }
}
