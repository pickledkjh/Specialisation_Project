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
    [Tooltip("Extra height ABOVE the target's pivot to aim the rush at. Keep 0 when both mechs share the same pivot height — positive values make every rush gain altitude, which caused the floating fights.")]
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
    [Tooltip("Bar power per normal melee hit. 14 = a full string (7 normal hits = 98) sits just under the 100 bar, so the finisher tips it over, and partial strings accumulate fast. (Renamed from hitKnockdownPower so this stronger default beats the stale scene value.)")]
    public float meleeBarPower = 14f;
    [Tooltip("Bar power of the finisher - fills whatever the string left over. (Renamed from finisherKnockdownPower for the same reason.)")]
    public float finisherBarPower = 60f;

    [Header("Finisher Launch")]
    public float finisherLaunchForward = 16f;
    public float finisherLaunchUp = 8f;

    [Header("Tracking Speeds")]
    public float redLockTurnSpeed = 30f;

    [Header("Charge Shot (special move)")]
    [Tooltip("Hold the shoot button this long, then release, to fire the charge shot. The tap shot still fires instantly on press.")]
    public float chargeShotHoldTime = 1f;
    private float shootHeldSince = -1f;

    [Header("Shield (hold Q)")]
    [Tooltip("Boost drained per second while guarding - the shield cannot be held forever, and cannot be raised while overheated (EXVS guard rules).")]
    public float shieldBoostDrainPerSec = 8f;
    [Tooltip("Only attacks from inside the frontal arc are blocked. 0.2 = roughly the front 155 degrees; raise toward 1 for a narrower shield.")]
    public float shieldFrontDot = 0.2f;
    [Tooltip("Console logging for shield raise/refuse. Leave ON until blocking is confirmed working.")]
    public bool logShieldDebug = true;
    private InputAction shieldAction;
    private bool isShielding = false;
    private GameObject shieldVisual;
    private BoostManager boostManager;
    private float lastRefuseLog = -99f;

    public bool IsShielding => isShielding;

    // Successful blocks (counted by MeleeHitbox) - the tutorial watches this
    [HideInInspector] public int blocksLanded = 0;
    public void RegisterBlock() { blocksLanded++; }

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

        shieldAction = new InputAction("Shield", InputActionType.Button);
        shieldAction.AddBinding("<Keyboard>/q");

        boostManager = GetComponent<BoostManager>();
    }

    private void Start()
    {
        if (rightFistCollider != null) rightFistCollider.enabled = false;
        if (leftFistCollider != null) leftFistCollider.enabled = false;
        if (leftFootCollider != null) leftFootCollider.enabled = false;

        if (punch1Camera != null) punch1Camera.Priority = 5;
        if (punch4Camera != null) punch4Camera.Priority = 5;
    }

    private void OnEnable() { shootAction.Enable(); meleeAction.Enable(); shieldAction.Enable(); }
    private void OnDisable() { shootAction.Disable(); meleeAction.Disable(); shieldAction.Disable(); }

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

    // Normal hits feed the bar; the true finisher carries huge bar power
    public float GetCurrentKnockdownPower()
    {
        return IsFinalStep(currentMeleeStep) ? finisherBarPower : meleeBarPower;
    }

    // MeleeHitbox asks this to pick the launch strength: the finisher flings hard,
    // a mid-string bar-fill downs with the hitbox's small default launch (EXVS weak down).
    public bool IsFinisherHit => IsFinalStep(currentMeleeStep);

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
        // NOTE: no forced knockdown here anymore. EXVS has ONE down system — the bar.
        // The finisher simply carries huge bar power + the big launch, both passed
        // through MeleeHitbox -> MechHealth.TakeDamage, which downs the target when
        // the bar fills. This keeps partial-string accumulation meaningful: the bar
        // remembers every hit, and whichever hit fills it causes the down.
        if (impulseSource != null)
            impulseSource.GenerateImpulse(2f); // finisher screen shake stays
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

        UpdateShield();

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

    // EXVS-style guard: hold to block frontal melee and shots. Movement is rooted
    // (MechController checks IsShielding), boost drains while held, dashing out
    // drops the shield naturally via the state check.
    private void UpdateShield()
    {
        bool wantShield = shieldAction != null && shieldAction.IsPressed()
            && !isAttacking && !isLunging && !isInEndLag
            && (mechController.currentState == MechState.Grounded ||
                mechController.currentState == MechState.Airborne)
            && boostManager != null && !boostManager.isOverheated && boostManager.currentBoost > 0.5f;

        if (wantShield && !isShielding)
        {
            if (myHealth != null) myHealth.BreakWakeUpProtection(); // guarding is an action
            isShielding = true;
            if (shieldVisual == null) shieldVisual = ShieldVisual.Create(transform);
            shieldVisual.SetActive(true);
            if (logShieldDebug) Debug.Log("[Parry] shield UP");
        }
        else if (!wantShield && isShielding)
        {
            isShielding = false;
            if (shieldVisual != null) shieldVisual.SetActive(false);
            if (logShieldDebug) Debug.Log("[Parry] shield DOWN");
        }

        // Q held but the shield refused to raise: say WHY (once per second)
        if (logShieldDebug && shieldAction != null && shieldAction.IsPressed() && !isShielding &&
            Time.time - lastRefuseLog > 1f)
        {
            lastRefuseLog = Time.time;
            Debug.Log("[Parry] Q held but shield refused: attacking=" + isAttacking +
                      " lunging=" + isLunging + " endlag=" + isInEndLag +
                      " state=" + mechController.currentState +
                      " boostMgr=" + (boostManager != null) +
                      (boostManager != null ? " boost=" + boostManager.currentBoost.ToString("0") + " overheated=" + boostManager.isOverheated : ""));
        }

        if (isShielding) boostManager.ConsumeBoostOverTime(shieldBoostDrainPerSec);
    }

    // Queried by MeleeHitbox / HomingProjectile: is this mech blocking a hit that
    // comes from attackerPosition?
    public bool IsBlocking(Vector3 attackerPosition)
    {
        if (!isShielding) return false;
        Vector3 to = attackerPosition - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Dot(transform.forward, to.normalized) >= shieldFrontDot;
    }

    private void HandleInputs()
    {
        if (isShielding) return; // no attacking out of a raised guard - drop it first

        Transform target = GetTarget();

        if (shootAction.WasPressedThisFrame())
        {
            if (myHealth != null) myHealth.BreakWakeUpProtection();

            // ONE ACTION AT A TIME: no shooting while a melee string or rush is
            // running. (You can still shoot at a downed target - the shot just
            // doesn't home and passes through them.)
            if (mechShooter != null && !isAttacking && !isLunging && !isInEndLag &&
                mechController.currentState != MechState.Landing) // landing lag = helpless, EXVS rule
            {
                mechShooter.FireWeapon();
                shootHeldSince = Time.time; // start charging while the button stays held
            }
        }

        // Special move: keep HOLDING the shoot button to charge; releasing after the
        // hold time fires the big red charge shot (heavy knockdown power).
        if (shootAction.WasReleasedThisFrame())
        {
            if (shootHeldSince > 0f && Time.time - shootHeldSince >= chargeShotHoldTime &&
                mechShooter != null && !isAttacking && !isLunging && !isInEndLag &&
                mechController.currentState != MechState.Landing)
            {
                mechShooter.FireChargeShot();
            }
            shootHeldSince = -1f;
        }

        if (meleeAction.WasPressedThisFrame())
        {
            // NOTE: no longer hard-blocked while the target is yellow-locked (downed).
            // Ignoring the button entirely read as "the combo bugged out" — in EXVS you
            // can always swing; a downed enemy just can't be hit (MeleeHitbox skips
            // yellow-locked victims) and the swing gets no homing (see StartMeleeString).
            if (myHealth != null) myHealth.BreakWakeUpProtection();

            // EXVS landing lag = HELPLESS: no melee out of the landing recovery.
            // Beyond the design rule, starting a string here RACED the landing
            // coroutine - it stomped the state back to Grounded and crossfaded
            // Locomotion over the frozen punch windup, which was the "rushed to
            // the enemy but never attacked, then just hung there" bug.
            if (mechController.currentState == MechState.Landing)
            {
                lastMeleePressTime = Time.time; // buffered - chains the moment you recover
            }
            else if (!isAttacking && !isLunging && currentMeleeStep == 0)
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

        // A downed (yellow-locked) target gets NO homing rush and no red-lock chaining:
        // the string behaves like a green-lock whiff string (free swings, whiff-chainable).
        bool targetDowned = IsTargetYellowLocked(GetTarget());

        if (distanceToEnemy <= redLockRange && !targetDowned)
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
        // With trigger-time (no exit time) chain transitions, an early cancel can skip
        // the outgoing clip's Disable-fist events — clear all hitboxes before each swing
        // so a lingering fist from the previous punch can't double-hit.
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();
        // CROSSFADE, not SetTrigger: triggers could get stranded when a chain landed
        // during an in-progress transition (the animator wandered to Locomotion with
        // the queued trigger stuck, and the combo died with no animation playing).
        // CrossFadeInFixedTime FORCES the correct punch state from ANY situation —
        // the code is now authoritative over the combo animation, always in sync.
        if (animator != null)
        {
            animator.ResetTrigger("Melee1"); animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3"); animator.ResetTrigger("Melee4");
            animator.CrossFadeInFixedTime("punch" + AnimIndexForStep(step), 0.08f, 0);
        }
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

        if (animator != null) animator.CrossFadeInFixedTime("punch1", 0.08f, 0); // forced, same as TriggerPunch
        yield return new WaitForSeconds(lungePoseDelay);

        // Only freeze once the animator has actually reached the Melee1 windup.
        // After a step-cancel the step clip may still be finishing — freezing too early
        // locked the step pose and looked broken. This project's state is named
        // "punch1" ("Melee1" also accepted in case it is renamed later). If the windup
        // is never reached within 0.3s, DON'T freeze at all — freezing whatever else
        // was playing (the run/dash pose) caused the frozen-pose rushes.
        float poseWait = 0f;
        bool reachedWindup = false;
        while (poseWait < 0.3f && animator != null && isLunging && isAttacking)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("punch1") || st.IsName("Melee1")) { reachedWindup = true; break; }
            poseWait += Time.deltaTime;
            yield return null;
        }

        if (animator != null && reachedWindup && isLunging && isAttacking) animator.speed = 0f;

        float elapsedTime = 0f;
        float currentSpeed = meleeLungeSpeed;
        Transform target = GetTarget();

        while (elapsedTime < maxLungeTime)
        {
            // Belt-and-braces cancellation guard: if ANY path cleared the attack
            // flags without stopping this coroutine, stop rushing immediately -
            // a cancelled melee must never keep flying the mech into the enemy.
            if (!isLunging || !isAttacking) break;
            if (target == null || IsTargetYellowLocked(target)) break;

            // Pivot-to-pivot 3D homing: level with the target instead of climbing above them.
            // (Aiming at chest height was lifting the attacker ~1m every rush — the floating bug.)
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
        if (!isAttacking || isInEndLag || isLunging) return;

        // A new step just started, or its trigger is still queued in the animator —
        // this event belongs to the outgoing clip, so ignore it.
        if (Time.time - lastStepStartTime < 0.1f) return;
        if (currentMeleeStep >= 2 && animator != null && animator.GetBool("Melee" + AnimIndexForStep(currentMeleeStep))) return;

        // Stray-event guard (the step-cancel combo killer): a punch clip cancelled
        // into a boost step keeps playing through the crossfade, and its EndAttack
        // event still fires DURING that blend — up to a quarter second after the
        // cancel. If a rainbow-step string has started by then, that stale event
        // would end the fresh combo at hit 1. A REAL clip end always happens while
        // the animator's current state is one of the punch states, so only accept
        // the event then.
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            bool inPunchState =
                st.IsName("punch1") || st.IsName("punch2") || st.IsName("punch3") || st.IsName("punch4") ||
                st.IsName("Melee1") || st.IsName("Melee2") || st.IsName("Melee3") || st.IsName("Melee4");
            if (!inPunchState) return;
        }

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
