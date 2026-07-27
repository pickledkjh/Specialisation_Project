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
    public float damage = 20f;
    public float defaultKnockdownPower = 10f; // Fallback if not driven by MechCombat
    [Tooltip("Stagger applied to the victim per hit. Keep it comfortably longer than the gap between your punches so combos hold the victim in place.")]
    public float hitStunDuration = 0.9f;

    [Header("Launch (applied only when this hit causes a knockdown)")]
    public float launchForwardSpeed = 9f;
    public float launchUpSpeed = 5f;

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

        float finalKnockdown = defaultKnockdownPower;
        if (playerCombatScript != null)
        {
            finalKnockdown = playerCombatScript.GetCurrentKnockdownPower();
        }

        // Launch direction: away from the attacker, horizontally
        Transform attacker = playerCombatScript != null ? playerCombatScript.transform
                           : aiCombatScript != null ? aiCombatScript.transform
                           : transform.root;

        if (health != null)
        {
            Vector3 dir = health.transform.position - attacker.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? attacker.forward : dir.normalized;
            // The launch is only applied by MechHealth if THIS hit fills the knockdown bar
            health.TakeDamage(damage, finalKnockdown, dir * launchForwardSpeed + Vector3.up * launchUpSpeed);
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
                playerTarget.TakeHit(hitStunDuration);        // TakeHit FIRST (it calls StopAllCoroutines)
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
                aiTarget.TakeHit(hitStunDuration);
                aiTarget.StartHitStop(hitStopDuration);
            }
        }
    }
}