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
    public float baseBarPower = 14f;
    [Tooltip("Stagger applied to the victim per hit. Long enough that ending your combo leaves the victim staggered PAST your end lag, so they cannot instantly counter-attack. (Renamed from hitStunDuration so the new default beats the stale 0.9 scene value.)")]
    public float hitStunSeconds = 1.3f;

    [Header("Launch (applied only when this hit causes a knockdown)")]
    public float launchForwardSpeed = 9f;
    public float launchUpSpeed = 5f;

    [Header("Shield / Parry")]
    [Tooltip("Stagger applied to the ATTACKER when this melee is blocked - the parry punish window.")]
    public float shieldStunSeconds = 1.5f;
    [Tooltip("Console logging for the whole parry chain. Leave ON until blocking is confirmed working, then untick.")]
    public bool logParryDebug = true;

    public float hitCooldown = 0.4f;
    private float lastHitTime = -99f;

    // Called by MechCombat.TriggerPunch each swing so the cooldown can never
    // swallow a fast follow-up hit (the old bug that broke mashed chains).
    public void ResetCooldown() { lastHitTime = -99f; }

    private void OnTriggerEnter(Collider other)
    {
        if (Time.time - lastHitTime < hitCooldown) return;

        MechHealth health = other.GetComponentInParent<MechHealth>();

        // FIX: also accept the tag on the mech root, so untagged child colliders
        // (hurtbox bones, etc.) no longer make punches silently whiff.
        bool tagMatches = other.CompareTag(targetTag) || (health != null && health.CompareTag(targetTag));
        if (!tagMatches) return;

        if (health != null && health.isYellowLocked) return;

        lastHitTime = Time.time;

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

        if (logParryDebug && (victimCombat != null || victimAI != null))
        {
            Transform victimT = victimCombat != null ? victimCombat.transform : victimAI.transform;
            Vector3 toAtk = attackerT.position - victimT.position; toAtk.y = 0f;
            float dot = toAtk.sqrMagnitude > 0.0001f ? Vector3.Dot(victimT.forward, toAtk.normalized) : 1f;
            bool victimShieldUp = victimCombat != null ? victimCombat.IsShielding : victimAI.IsShieldUp;
            Debug.Log("[Parry] melee contact: victim=" + victimT.name +
                      " shieldUp=" + victimShieldUp +
                      " facingDot=" + dot.ToString("0.00") +
                      " blocked=" + blocked +
                      " attacker=" + attackerT.name);
        }

        if (blocked)
        {
            // PARRY: the attacker eats the stun - the blocker's punish window.
            // TakeHit first (it stops coroutines), then the clang freeze.
            if (attackerAI != null)
            {
                attackerAI.TakeHit(shieldStunSeconds);
                attackerAI.StartHitStop(0.15f);
                if (logParryDebug) Debug.Log("[Parry] BLOCKED! Stunned attacker (AI) for " + shieldStunSeconds + "s");
            }
            else if (attackerCombat != null)
            {
                MechController attackerMech = attackerCombat.GetComponent<MechController>();
                if (attackerMech != null) attackerMech.TakeHit(shieldStunSeconds);
                attackerCombat.StartHitStop(0.15f);
                if (logParryDebug) Debug.Log("[Parry] BLOCKED! Stunned attacker (player) for " + shieldStunSeconds + "s");
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
            bigForward = playerCombatScript.finisherLaunchForward;
            bigUp = playerCombatScript.finisherLaunchUp;
        }
        else if (aiCombatScript != null)
        {
            finalKnockdown = aiCombatScript.GetCurrentKnockdownPower(baseBarPower);
            finisher = aiCombatScript.IsFinisherHit;
            bigForward = aiCombatScript.finisherLaunchForward;
            bigUp = aiCombatScript.finisherLaunchUp;
        }

        // Launch direction: away from the attacker, horizontally
        Transform attacker = attackerT;

        if (health != null)
        {
            Vector3 dir = health.transform.position - attacker.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? attacker.forward : dir.normalized;
            Vector3 launch = finisher
                ? dir * bigForward + Vector3.up * bigUp
                : dir * launchForwardSpeed + Vector3.up * launchUpSpeed;
            // The launch is only applied by MechHealth if THIS hit fills the knockdown bar
            health.TakeDamage(damagePerHit, finalKnockdown, launch);
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
                playerTarget.TakeHit(hitStunSeconds);         // TakeHit FIRST (it calls StopAllCoroutines)
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
                aiTarget.TakeHit(hitStunSeconds);
                aiTarget.StartHitStop(hitStopDuration);
            }
        }
    }
}
