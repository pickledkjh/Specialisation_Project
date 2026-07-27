using System.Collections;
using UnityEngine;

public class MechHealth : MonoBehaviour
{
    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Team / Cost (used on death)")]
    public Team team = Team.Team2;
    public int unitCost = 2000;

    [Header("Hidden Knockdown Bar")]
    public float maxKnockdownValue = 100f;
    public float currentKnockdownValue = 0f;
    public float knockdownDecayRate = 15f;

    [Header("Down State / Yellow Lock")]
    public bool isYellowLocked = false;
    public float downedDuration = 3.0f;
    public float wakeUpProtectionDuration = 3.0f;

    [Header("Knockdown Flight Physics")]
    [Tooltip("Gravity during the launch flight. Less negative = longer, more dramatic arc.")]
    public float knockdownLaunchGravity = -22f;
    [Tooltip("How fast horizontal launch speed decays mid-flight. Lower = flies farther.")]
    public float knockdownLaunchDrag = 1.1f;
    [Tooltip("Safety cap only ¡ª the flight normally ends when the mech touches the ground.")]
    public float knockdownMaxAirTime = 1.6f;

    private Animator animator;
    private SimpleMechAI aiController;
    private MechController playerController;
    private MechCombat playerCombat;
    private CharacterController cc;

    private Coroutine downedCoroutine;
    private bool inWakeUpProtection = false;

    private void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponentInChildren<Animator>();
        aiController = GetComponent<SimpleMechAI>();
        playerController = GetComponent<MechController>();
        playerCombat = GetComponent<MechCombat>();
        cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        if (!isYellowLocked && currentKnockdownValue > 0)
        {
            currentKnockdownValue -= knockdownDecayRate * Time.deltaTime;
            if (currentKnockdownValue < 0) currentKnockdownValue = 0;
        }
    }

    // launchVelocity is optional so all existing 2-argument call sites still compile.
    public void TakeDamage(float amount, float knockdownPower, Vector3 launchVelocity = default)
    {
        if (isYellowLocked) return;

        currentHealth -= amount;
        currentKnockdownValue += knockdownPower;

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (currentKnockdownValue >= maxKnockdownValue)
        {
            TriggerKnockdown(launchVelocity);
        }
    }

    public void TriggerKnockdown(Vector3 launchVelocity = default)
    {
        if (isYellowLocked) return;

        if (downedCoroutine != null) StopCoroutine(downedCoroutine);
        downedCoroutine = StartCoroutine(DownedStateRoutine(launchVelocity));
    }

    private IEnumerator DownedStateRoutine(Vector3 launchVelocity)
    {
        isYellowLocked = true;
        inWakeUpProtection = false;
        currentKnockdownValue = 0f;

        if (animator != null)
        {
            animator.speed = 1f;              // in case a hit-stop froze the animator
            animator.ResetTrigger("GetHit");  // stagger trigger can no longer eat the knockdown animation
            animator.SetTrigger("HitDown");
        }

        SetControlScripts(false);

        // FIX (floating-while-downed): this always runs, launch or not, and keeps applying
        // gravity until the mech ACTUALLY touches the ground. Previously the flight loop
        // could time out mid-air and the mech lay frozen floating for the whole down state.
        if (cc != null)
        {
            Vector3 vel = launchVelocity;
            float t = 0f;
            float hardCap = knockdownMaxAirTime + 3f; // safety only; normally we land first
            bool landed = false;

            while (t < hardCap && !landed)
            {
                vel.y += knockdownLaunchGravity * Time.deltaTime;
                if (vel.y < -35f) vel.y = -35f;
                vel.x = Mathf.Lerp(vel.x, 0f, Time.deltaTime * knockdownLaunchDrag);
                vel.z = Mathf.Lerp(vel.z, 0f, Time.deltaTime * knockdownLaunchDrag);
                cc.Move(vel * Time.deltaTime);

                if (t > 0.1f && cc.isGrounded) landed = true;

                t += Time.deltaTime;
                yield return null;
            }
        }

        // The down timer starts AFTER landing, so the lie-down reads correctly on the floor
        yield return new WaitForSeconds(downedDuration);

        if (animator != null) animator.SetTrigger("Recover");
        SetControlScripts(true);

        inWakeUpProtection = true;
        float protectionTimer = wakeUpProtectionDuration;
        while (protectionTimer > 0f)
        {
            protectionTimer -= Time.deltaTime;
            yield return null;
        }

        inWakeUpProtection = false;
        isYellowLocked = false;
        downedCoroutine = null;
    }

    private void SetControlScripts(bool enabled)
    {
        if (aiController != null) aiController.enabled = enabled;
        if (playerController != null) playerController.enabled = enabled;
        if (playerCombat != null) playerCombat.enabled = enabled;
    }

    // Acting during wake-up invincibility forfeits it ¡ª but only during the protection
    // window after standing up, never while still lying on the floor.
    public void BreakWakeUpProtection()
    {
        if (inWakeUpProtection && isYellowLocked && currentHealth > 0)
        {
            isYellowLocked = false;
            inWakeUpProtection = false;
            if (downedCoroutine != null)
            {
                StopCoroutine(downedCoroutine);
                downedCoroutine = null;
            }
        }
    }

    private void Die()
    {
        if (downedCoroutine != null) StopCoroutine(downedCoroutine);

        if (animator != null)
        {
            animator.speed = 1f;
            animator.ResetTrigger("GetHit");
            animator.SetTrigger("Die");
        }

        SetControlScripts(false);
        isYellowLocked = true;

        // Wire the destruction into the team cost pool (EXVS win condition)
        if (CostManager.Instance != null)
        {
            CostManager.Instance.DeductCost(team, unitCost);
        }
    }
}