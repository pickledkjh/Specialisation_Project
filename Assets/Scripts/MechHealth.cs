using System.Collections;
using UnityEngine;

public class MechHealth : MonoBehaviour
{
    /// <summary>
    /// Fired every time ANY mech takes damage (victim, amount). The HUD listens to
    /// this for the red "you got hit" screen flash on the player.
    /// </summary>
    public static System.Action<MechHealth, float> AnyMechDamaged;

    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Team / Cost (used on death)")]
    public Team team = Team.Team2;
    public int unitCost = 2000;

    [Header("Hidden Knockdown Bar")]
    public float maxKnockdownValue = 100f;
    public float currentKnockdownValue = 0f;
    [Tooltip("After the last hit taken, the bar HOLDS its full value for this many seconds before any draining starts. Stop a combo, reposition, come back — the damage still counts. (Field deliberately renamed from knockdownDecayDelay so the new default applies over any stale scene value.)")]
    public float knockdownHoldSeconds = 4f;
    [Tooltip("Drain per second AFTER the hold window. 8 = a full bar takes ~12.5s to empty, so accumulated hits stay threatening for a long while, like the reference games. (Field deliberately renamed from knockdownDecayRate — the old scene value of 15 was wiping partial combos in ~3 seconds.)")]
    public float knockdownDrainPerSecond = 8f;

    [Header("Down State / Yellow Lock")]
    public bool isYellowLocked = false;
    public float downedDuration = 3.0f;
    public float wakeUpProtectionDuration = 3.0f;

    [Header("Knockdown Flight Physics")]
    [Tooltip("Gravity during the launch flight. Less negative = longer, more dramatic arc.")]
    public float knockdownLaunchGravity = -22f;
    [Tooltip("How fast horizontal launch speed decays mid-flight. Lower = flies farther.")]
    public float knockdownLaunchDrag = 1.1f;
    [Tooltip("Safety cap only — the flight normally ends when the mech touches the ground.")]
    public float knockdownMaxAirTime = 1.6f;

    [Header("Placeholder Down Pose (used only until real HitDown/Recover states exist)")]
    [Tooltip("While the animator has no 'HitDown' state, the model is tilted flat by code so knockdowns are VISIBLE instead of the mech standing there looking normal while invulnerable. Add real knockdown states/clips to the animator and this deactivates itself.")]
    public bool usePlaceholderDownPose = true;
    [Tooltip("Degrees per second the placeholder tilt animates at.")]
    public float placeholderTiltSpeed = 300f;

    private Animator animator;
    private SimpleMechAI aiController;
    private MechController playerController;
    private MechCombat playerCombat;
    private CharacterController cc;

    private Coroutine downedCoroutine;
    private bool inWakeUpProtection = false;

    // Placeholder lie-down pose state
    private bool animatorHasDownStates = false;
    private Transform modelTransform;
    private Quaternion tiltBaseLocalRot = Quaternion.identity; // captured when the knockdown starts
    private Vector3 tiltBaseLocalPos = Vector3.zero;           // captured with it (pins root-motion drift)
    private float downTilt = 0f;          // current tilt in degrees
    private float downTiltTarget = 0f;    // 0 = upright, 90 = lying on back
    private bool rootMotionWasOn = false;

    private void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponentInChildren<Animator>();
        aiController = GetComponent<SimpleMechAI>();
        playerController = GetComponent<MechController>();
        playerCombat = GetComponent<MechCombat>();
        cc = GetComponent<CharacterController>();

        if (animator != null)
        {
            modelTransform = animator.transform;
            // If the controller gains real knockdown states later, the code placeholder
            // steps aside automatically.
            animatorHasDownStates = animator.HasState(0, Animator.StringToHash("HitDown"));
        }
    }

    private float lastDamageTime = -99f;

    private void Update()
    {
        // The bar holds for knockdownHoldSeconds after every hit, then drains slowly.
        // Melee hits AND projectile hits both land in the same bar via TakeDamage.
        if (!isYellowLocked && currentKnockdownValue > 0 &&
            Time.time - lastDamageTime >= knockdownHoldSeconds)
        {
            currentKnockdownValue -= knockdownDrainPerSecond * Time.deltaTime;
            if (currentKnockdownValue < 0) currentKnockdownValue = 0;
        }
    }

    // Runs AFTER the Animator has posed the model this frame, so the placeholder tilt
    // wins over whatever standing animation is still playing during the down state.
    // IMPORTANT: while fully upright this must not touch the model at all — writing
    // the rotation every frame would fight root motion / other systems and skew the
    // body's facing during normal play.
    private void LateUpdate()
    {
        if (!usePlaceholderDownPose || animatorHasDownStates || modelTransform == null) return;
        if (downTiltTarget <= 0f && downTilt <= 0.01f) return; // upright: hands off

        downTilt = Mathf.MoveTowards(downTilt, downTiltTarget, placeholderTiltSpeed * Time.deltaTime);
        modelTransform.localRotation = tiltBaseLocalRot * Quaternion.Euler(-downTilt, 0f, 0f);
        // PIN the model position too: with the animator frozen in a state whose clip
        // has root motion (e.g. Floating's hover bob), the model child accumulated
        // upward drift every loop while downed — the "constantly rising while yellow
        // locked" bug. While the down pose owns the model, nothing else may move it.
        modelTransform.localPosition = tiltBaseLocalPos;
        // On full recovery the final write above restores the exact captured pose,
        // then the early-out takes over next frame.
    }

    // launchVelocity is optional so all existing 2-argument call sites still compile.
    public void TakeDamage(float amount, float knockdownPower, Vector3 launchVelocity = default)
    {
        if (isYellowLocked) return;

        currentHealth -= amount;
        currentKnockdownValue += knockdownPower;
        lastDamageTime = Time.time;

        // ---- hit feedback ----
        // One central place covers EVERY damage source (melee, shots, charge shots):
        // an impact burst on the victim's chest + the global event the HUD uses for
        // the player's red damage flash. Without these, ranged hits especially were
        // landing with zero visible reaction.
        CombatVfx.SpawnHit(transform.position + Vector3.up * 1.3f);
        AnyMechDamaged?.Invoke(this, amount);

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

        // Visible down state even without real knockdown animations (see header field).
        // Capture the CURRENT model pose as the tilt base so the restore is exact.
        if (modelTransform != null && downTilt <= 0.01f)
        {
            tiltBaseLocalRot = modelTransform.localRotation;
            tiltBaseLocalPos = modelTransform.localPosition;
        }
        downTiltTarget = 90f;

        // Root motion off while downed — its accumulated deltas were floating the
        // body upward through the whole down state. Restored on recovery.
        if (animator != null)
        {
            rootMotionWasOn = animator.applyRootMotion;
            animator.applyRootMotion = false;
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

        if (animator != null)
        {
            animator.SetTrigger("Recover");
            animator.applyRootMotion = rootMotionWasOn; // restore
        }
        downTiltTarget = 0f; // placeholder pose stands back up
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

    /// <summary>
    /// Full reset back to a fighting state - used by the tutorial to bring the
    /// practice bot back when the player kills it mid-lesson. Undoes everything
    /// Die()/knockdown set: health, bars, yellow lock, the placeholder tilt pose,
    /// root motion, and the disabled control scripts.
    /// </summary>
    public void Revive()
    {
        if (downedCoroutine != null)
        {
            StopCoroutine(downedCoroutine);
            downedCoroutine = null;
        }

        bool wasTilted = downTilt > 0.01f || downTiltTarget > 0f;
        isYellowLocked = false;
        inWakeUpProtection = false;
        currentHealth = maxHealth;
        currentKnockdownValue = 0f;
        downTiltTarget = 0f;
        downTilt = 0f;

        // Restore the exact pose captured when the tilt started - only if we were
        // actually tilted, so a healthy mech's pose is never touched.
        if (wasTilted && modelTransform != null)
        {
            modelTransform.localRotation = tiltBaseLocalRot;
            modelTransform.localPosition = tiltBaseLocalPos;
        }

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = rootMotionWasOn;
            animator.ResetTrigger("HitDown");
            animator.ResetTrigger("Die");
            animator.ResetTrigger("GetHit");
            if (wasTilted) animator.SetTrigger("Recover");
        }

        SetControlScripts(true);
    }

    private void SetControlScripts(bool enabled)
    {
        if (aiController != null) aiController.enabled = enabled;
        if (playerController != null) playerController.enabled = enabled;
        if (playerCombat != null) playerCombat.enabled = enabled;
    }

    // Acting during wake-up invincibility forfeits it — but only during the protection
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

        // No Die state exists yet either — the placeholder tilt shows the death too
        if (modelTransform != null && downTilt <= 0.01f)
        {
            tiltBaseLocalRot = modelTransform.localRotation;
            tiltBaseLocalPos = modelTransform.localPosition;
        }
        if (animator != null) animator.applyRootMotion = false; // stays off — it's dead
        downTiltTarget = 90f;

        SetControlScripts(false);
        isYellowLocked = true;

        // Wire the destruction into the team cost pool (EXVS win condition)
        if (CostManager.Instance != null)
        {
            CostManager.Instance.DeductCost(team, unitCost);
        }
    }
}
