using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

[RequireComponent(typeof(MechController))]
[RequireComponent(typeof(CharacterController))]
public class MechCombat : MonoBehaviour
{
    private MechController mechController;
    private CharacterController characterController;
    private Animator animator;
    private TargetManager targetManager;
    private MechShooter mechShooter;
    private MechHealth myHealth;

    [Header("Cameras")]
    public CinemachineCamera punch1Camera;
    public CinemachineCamera punch4Camera;
    public CinemachineImpulseSource impulseSource;

    [Header("Lock-On Ranges")]
    public float redLockRange = 40f;
    public float meleeHitRange = 3.5f;

    [Header("Melee Lunge (homing rush)")]
    public float meleeLungeSpeed = 65f;
    public float minLungeSpeed = 10f;
    public float lungeSpeedDrag = 1.5f;
    public float maxLungeTime = 0.8f;
    public float lungeStopDistance = 1.8f;
    [Tooltip("Extra height ABOVE the target's pivot to aim the rush at. Keep 0 when both mechs share the same pivot height ！ positive values make every rush gain altitude, which caused the floating fights.")]
    public float lungeAimHeight = 0f;
    public float lungePoseDelay = 0.15f;

    [Header("Combo Length")]
    [Tooltip("The Melee1-4 string repeats this many times before the launcher. 2 = 8-hit combo. If > 1, your Animator needs ONE new transition: Melee4 -> Melee1 (condition: trigger Melee1).")]
    public int comboLoops = 2;

    [Header("Combo Flow")]
    [Tooltip("How long a melee press is remembered while waiting for the chain window (EXVS-style buffering).")]
    public float inputBufferWindow = 0.5f;
    [Tooltip("Tiny pause after a hit registers before the next swing may cancel in, so the impact reads.")]
    public float chainDelayAfterHit = 0.08f;
    [Tooltip("Minimum time between swings when chaining whiffed punches outside red lock.")]
    public float whiffChainInterval = 0.3f;
    [Tooltip("Failsafe only. Ends the string if no EndAttack animation event ever fires. Keep this LONGER than your longest melee clip.")]
    public float comboSafetyTimeout = 2.5f;
    public float endLagDuration = 0.6f;

    [Header("Combat Hitboxes")]
    public Collider rightFistCollider;
    public Collider leftFistCollider;
    public Collider leftFootCollider;

    [Header("Knockdown Bar Powers")]
    [Tooltip("Bar power per normal hit. Keep low so a full designed combo fits under MechHealth.maxKnockdownValue and doesn't drop the enemy mid-string.")]
    public float hitKnockdownPower = 6f;
    public float finisherKnockdownPower = 30f;

    [Header("Finisher Launch")]
    public float finisherLaunchForward = 16f;
    public float finisherLaunchUp = 8f;

    [Header("Tracking Speeds")]
    public float redLockTurnSpeed = 30f;

    private int currentMeleeStep = 0;
    private float lastMeleeTime = 0f;
    private float lastStepStartTime = -99f;
    private float lastMeleePressTime = -99f;
    private float lastHitRegisteredTime = -99f;

    private bool isAttacking = false;
    private bool isLunging = false;
    private bool isInEndLag = false;
    private bool startedInRedLock = false;
    private bool isFrozen = false;
    public bool hasHitConnected = false;

    private InputAction shootAction;
    private InputAction meleeAction;

    // Total hits in the full string, and which animation each step plays (cycles Melee1..Melee4)
    private int TotalHits => Mathf.Max(1, comboLoops) * 4;
    private bool IsFinalStep(int step) => step >= TotalHits;
    private int AnimIndexForStep(int step) => ((step - 1) % 4) + 1;

    private void Awake()
    {
        mechController = GetComponent<MechController>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
        targetManager = GetComponent<TargetManager>();
        mechShooter = GetComponent<MechShooter>();
        myHealth = GetComponent<MechHealth>();

        shootAction = new InputAction("Shoot", InputActionType.Button);
        shootAction.AddBinding("<Mouse>/rightButton");

        meleeAction = new InputAction("Melee", InputActionType.Button);
        meleeAction.AddBinding("<Mouse>/leftButton");
    }

    private void Start()
    {
        if (rightFistCollider != null) rightFistCollider.enabled = false;
        if (leftFistCollider != null) leftFistCollider.enabled = false;
        if (leftFootCollider != null) leftFootCollider.enabled = false;

        if (punch1Camera != null) punch1Camera.Priority = 5;
        if (punch4Camera != null) punch4Camera.Priority = 5;
    }

    private void OnEnable() { shootAction.Enable(); meleeAction.Enable(); }
    private void OnDisable() { shootAction.Disable(); meleeAction.Disable(); }

    private Transform GetTarget()
    {
        if (targetManager != null && targetManager.currentTarget != null)
            return targetManager.currentTarget;
        return mechController.enemyTarget;
    }

    public bool IsTargetYellowLocked(Transform target)
    {
        if (target == null) return false;

        MechHealth targetHealth = target.GetComponent<MechHealth>();
        if (targetHealth == null) targetHealth = target.GetComponentInParent<MechHealth>();
        if (targetHealth == null) targetHealth = target.GetComponentInChildren<MechHealth>();

        return targetHealth != null && targetHealth.isYellowLocked;
    }

    // Normal hits feed the bar lightly; only the true finisher carries big bar power
    public float GetCurrentKnockdownPower()
    {
        return IsFinalStep(currentMeleeStep) ? finisherKnockdownPower : hitKnockdownPower;
    }

    public void RegisterHit()
    {
        hasHitConnected = true;
        lastHitRegisteredTime = Time.time;

        if (currentMeleeStep == 1 && punch1Camera != null) SwitchToPunch1Camera();
        else if (IsFinalStep(currentMeleeStep) && punch4Camera != null) SwitchToPunch4Camera();

        // Only the LAST hit of the full string knocks down
        if (IsFinalStep(currentMeleeStep)) ApplyHitDownEffect();
    }

    private void ApplyHitDownEffect()
    {
        Transform target = GetTarget();
        if (target == null) return;

        MechHealth targetHealth = target.GetComponentInParent<MechHealth>();
        if (targetHealth == null) targetHealth = target.GetComponentInChildren<MechHealth>();

        if (targetHealth != null)
        {
            // Big EXVS-style launch: fly away and up instead of dropping in place
            Vector3 dir = targetHealth.transform.position - transform.position;
            dir.y = 0f;
            dir = dir.sqrMagnitude < 0.01f ? transform.forward : dir.normalized;
            targetHealth.TriggerKnockdown(dir * finisherLaunchForward + Vector3.up * finisherLaunchUp);
        }

        if (impulseSource != null)
            impulseSource.GenerateImpulse(2f);
    }

    public void StartHitStop(float duration)
    {
        if (isFrozen) return;
        if (impulseSource != null) impulseSource.GenerateImpulse();
        StartCoroutine(HitStopCoroutine(duration));
    }

    private IEnumerator HitStopCoroutine(float duration)
    {
        isFrozen = true;
        float originalSpeed = animator.speed;
        animator.speed = 0f;
        yield return new WaitForSecondsRealtime(duration);
        animator.speed = originalSpeed;
        isFrozen = false;
    }

    public void SwitchToPunch1Camera() { if (punch1Camera != null) punch1Camera.Priority = 20; }
    public void SwitchToPunch4Camera() { if (punch4Camera != null) punch4Camera.Priority = 20; }
    public void SwitchToNormalCamera()
    {
        if (punch1Camera != null) punch1Camera.Priority = 5;
        if (punch4Camera != null) punch4Camera.Priority = 5;
    }

    private void Update()
    {
        // Failsafe ONLY. Real step ends come from the EndAttack animation event.
        if (currentMeleeStep > 0 && !isLunging && !isInEndLag && !isFrozen &&
            Time.time - lastMeleeTime > comboSafetyTimeout)
        {
            StartCoroutine(EndLagRoutine());
        }

        HandleTargetTracking();

        if (mechController.currentState != MechState.Landing &&
            mechController.currentState != MechState.Staggered && !isInEndLag)
        {
            HandleInputs();
        }

        TryChainFromBuffer();
    }

    private void HandleTargetTracking()
    {
        Transform target = GetTarget();
        if (target == null) return;
        if (IsTargetYellowLocked(target)) return;

        if (isAttacking && !isInEndLag && startedInRedLock)
        {
            SmoothFaceEnemy(redLockTurnSpeed);
        }
    }

    private void SmoothFaceEnemy(float turnSpeed)
    {
        Transform target = GetTarget();
        if (target == null) return;

        Vector3 toTarget = target.position - transform.position;
        toTarget.y = 0;

        if (toTarget.magnitude > 0.5f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * turnSpeed);
        }
    }

    private void HandleInputs()
    {
        Transform target = GetTarget();

        if (shootAction.WasPressedThisFrame())
        {
            if (myHealth != null) myHealth.BreakWakeUpProtection();

            if (mechShooter != null && !IsTargetYellowLocked(target))
            {
                mechShooter.FireWeapon();
            }
        }

        if (meleeAction.WasPressedThisFrame())
        {
            if (IsTargetYellowLocked(target)) return;
            if (myHealth != null) myHealth.BreakWakeUpProtection();

            if (!isAttacking && !isLunging && currentMeleeStep == 0)
            {
                StartMeleeString();
            }
            else
            {
                // Never drop a press. Buffer it; TryChainFromBuffer / EndAttack consume it.
                lastMeleePressTime = Time.time;
            }
        }
    }

    private void StartMeleeString()
    {
        // Rainbow-step melee: if a boost step is in progress, cut it cleanly so its
        // coroutine can't keep sliding us or stomp the Attacking state afterwards.
        mechController.CancelBoostStep();

        isAttacking = true;
        mechController.currentState = MechState.Attacking;
        lastMeleeTime = Time.time;

        float distanceToEnemy = GetDistanceToTarget();

        if (distanceToEnemy <= redLockRange)
        {
            startedInRedLock = true;
            if (distanceToEnemy > meleeHitRange)
            {
                StartCoroutine(MeleeLungeRoutine());
                return;
            }
            TriggerPunch(1);
        }
        else
        {
            startedInRedLock = false;
            TriggerPunch(1);
        }
    }

    // Full 3D distance so a target above/below you still counts as in reach for the rush
    private float GetDistanceToTarget()
    {
        Transform target = GetTarget();
        if (target == null) return 999f;
        return Vector3.Distance(transform.position, target.position);
    }

    // Consumes a buffered melee press as soon as the chain conditions are met (cancel-on-hit)
    private void TryChainFromBuffer()
    {
        if (!isAttacking || isLunging || isInEndLag || isFrozen) return;
        if (currentMeleeStep <= 0 || currentMeleeStep >= TotalHits) return;
        if (Time.time - lastMeleePressTime > inputBufferWindow) return;

        Transform target = GetTarget();
        if (startedInRedLock && IsTargetYellowLocked(target)) return;

        bool hitChain = hasHitConnected && Time.time - lastHitRegisteredTime >= chainDelayAfterHit;
        bool whiffChain = !startedInRedLock && Time.time - lastStepStartTime >= whiffChainInterval;

        if (hitChain || whiffChain)
        {
            lastMeleePressTime = -99f;
            AdvanceCombo();
        }
    }

    private void AdvanceCombo()
    {
        lastMeleeTime = Time.time;
        TriggerPunch(Mathf.Min(currentMeleeStep + 1, TotalHits));
    }

    private void TriggerPunch(int step)
    {
        hasHitConnected = false;
        currentMeleeStep = step;
        lastStepStartTime = Time.time;
        animator.SetTrigger("Melee" + AnimIndexForStep(step));
        ResetHitboxCooldowns();
        if (step > 1 && startedInRedLock) StartCoroutine(MicroLunge());
    }

    // Re-arm hitboxes each swing so MeleeHitbox's cooldown can't swallow a fast follow-up hit
    private void ResetHitboxCooldowns()
    {
        if (rightFistCollider != null) { MeleeHitbox h = rightFistCollider.GetComponent<MeleeHitbox>(); if (h != null) h.ResetCooldown(); }
        if (leftFistCollider != null) { MeleeHitbox h = leftFistCollider.GetComponent<MeleeHitbox>(); if (h != null) h.ResetCooldown(); }
        if (leftFootCollider != null) { MeleeHitbox h = leftFootCollider.GetComponent<MeleeHitbox>(); if (h != null) h.ResetCooldown(); }
    }

    private IEnumerator MeleeLungeRoutine()
    {
        isLunging = true;
        currentMeleeStep = 1;
        hasHitConnected = false;
        lastStepStartTime = Time.time;
        ResetHitboxCooldowns();

        animator.SetTrigger("Melee1");
        yield return new WaitForSeconds(lungePoseDelay);

        // Only freeze once the animator has actually reached the Melee1 windup.
        // After a step-cancel the step clip may still be finishing ！ freezing too early
        // locked the step pose and looked broken. Falls back to freezing after 0.3s
        // if the state name doesn't match.
        float poseWait = 0f;
        while (poseWait < 0.3f && animator != null &&
               !animator.GetCurrentAnimatorStateInfo(0).IsName("Melee1"))
        {
            poseWait += Time.deltaTime;
            yield return null;
        }

        if (animator != null) animator.speed = 0f;

        float elapsedTime = 0f;
        float currentSpeed = meleeLungeSpeed;
        Transform target = GetTarget();

        while (elapsedTime < maxLungeTime)
        {
            if (target == null || IsTargetYellowLocked(target)) break;

            // Pivot-to-pivot 3D homing: level with the target instead of climbing above them.
            // (Aiming at chest height was lifting the attacker ~1m every rush ！ the floating bug.)
            Vector3 aimPoint = target.position + Vector3.up * lungeAimHeight;
            Vector3 offset = aimPoint - transform.position;

            Vector3 flat = offset; flat.y = 0f;
            if (flat.magnitude <= lungeStopDistance && Mathf.Abs(offset.y) <= 2.5f) break;

            currentSpeed = Mathf.Lerp(currentSpeed, minLungeSpeed, Time.deltaTime * lungeSpeedDrag);
            characterController.Move(offset.normalized * currentSpeed * Time.deltaTime);

            // Boost-powered rush: cancel gravity so we don't sink under an airborne target
            mechController.velocity.y = 0f;

            if (flat.sqrMagnitude > 0.01f)
            {
                Quaternion look = Quaternion.LookRotation(flat.normalized);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 20f);
            }

            elapsedTime += Time.deltaTime;
            yield return null;
        }

        if (animator != null) animator.speed = 1f;

        isLunging = false;
        lastMeleeTime = Time.time;
        lastStepStartTime = Time.time; // the swing effectively starts now
    }

    private IEnumerator MicroLunge()
    {
        float elapsedTime = 0f;

        while (elapsedTime < 0.15f)
        {
            Transform target = GetTarget();
            if (target != null && !IsTargetYellowLocked(target))
            {
                Vector3 offset = target.position - transform.position;
                offset.y = 0;

                if (offset.magnitude > lungeStopDistance)
                {
                    characterController.Move(offset.normalized * 15f * Time.deltaTime);
                }
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }

    // Guarded with isAttacking: after a step/dash cancel, the interrupted melee clip keeps
    // playing and would otherwise re-enable hitboxes mid-step via its animation events.
    public void EnableRightFist() { if (isAttacking && rightFistCollider != null) rightFistCollider.enabled = true; }
    public void DisableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = false; }
    public void EnableLeftFist() { if (isAttacking && leftFistCollider != null) leftFistCollider.enabled = true; }
    public void DisableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = false; }
    public void EnableLeftFoot() { if (isAttacking && leftFootCollider != null) leftFootCollider.enabled = true; }
    public void DisableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = false; }

    // Called by an Animation Event at the LAST frame of every melee clip (Melee1..Melee4)
    public void EndAttack()
    {
        if (!isAttacking || isInEndLag) return;

        // A new step just started, or its trigger is still queued in the animator ！
        // this event belongs to the outgoing clip, so ignore it.
        if (Time.time - lastStepStartTime < 0.1f) return;
        if (currentMeleeStep >= 2 && animator != null && animator.GetBool("Melee" + AnimIndexForStep(currentMeleeStep))) return;

        // Clip finished: last chance to consume a buffered press before ending the string
        if (currentMeleeStep > 0 && currentMeleeStep < TotalHits &&
            Time.time - lastMeleePressTime <= inputBufferWindow &&
            (hasHitConnected || !startedInRedLock) &&
            !(startedInRedLock && IsTargetYellowLocked(GetTarget())))
        {
            lastMeleePressTime = -99f;
            AdvanceCombo();
            return;
        }

        StartCoroutine(EndLagRoutine());
    }

    private IEnumerator EndLagRoutine()
    {
        SwitchToNormalCamera();
        isInEndLag = true;
        isAttacking = false;
        isLunging = false;
        startedInRedLock = false;
        currentMeleeStep = 0;
        lastMeleePressTime = -99f;
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();
        if (animator != null)
        {
            animator.speed = 1f;
            animator.ResetTrigger("Melee1");
            animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3");
            animator.ResetTrigger("Melee4");
        }
        yield return new WaitForSeconds(endLagDuration);
        isInEndLag = false;
        mechController.currentState = mechController.CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    public void CancelAttack()
    {
        SwitchToNormalCamera();
        if (!isAttacking && !isInEndLag) return;
        StopAllCoroutines();
        isAttacking = false;
        isLunging = false;
        isInEndLag = false;
        isFrozen = false; // BD-cancel during hit-stop can no longer lock hit-stops forever
        startedInRedLock = false;
        currentMeleeStep = 0;
        lastMeleePressTime = -99f;
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();
        if (animator != null)
        {
            animator.speed = 1f;
            animator.ResetTrigger("Melee1");
            animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3");
            animator.ResetTrigger("Melee4");
        }
    }
}