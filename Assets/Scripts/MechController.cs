using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MechState { Grounded, Airborne, BoostDash, BoostStep, Landing, Attacking, Staggered }

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BoostManager))]
public class MechController : MonoBehaviour
{
    [Header("Health & Status")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead = false;

    public Transform enemyTarget;
    public Animator animator;
    private CharacterController controller;
    private BoostManager boostManager;
    private MechCombat combatScript;

    [Header("Base Movement")]
    public float walkSpeed = 8f;
    public float stepSpeed = 45f;
    public float stepDuration = 0.25f;
    public float ascendSpeed = 15f;
    public float gravity = -20f;
    public float bodyRotationSpeed = 15f;

    [Header("EXVS Boost Feel")]
    [Tooltip("Extra multiplier on BOOST DASH speed only (on top of bigMapSpeedScale). 2 = double-speed dashes - crossing the big arena is a dash decision, not a hike.")]
    public float dashSpeedScale = 2f;
    [Tooltip("Speed of the initial dash snap.")]
    public float dashBurstSpeed = 30f;
    [Tooltip("Speed the dash holds while the button stays down. Keep this HIGH — an EXVS dash does not slow down while held.")]
    public float dashCruiseSpeed = 24f;
    [Tooltip("Seconds for the burst to settle into cruise. Keep short so the dash never feels like it is losing power.")]
    public float dashBurstSettleTime = 0.2f;
    [Tooltip("Small upward pop when starting a dash from the ground.")]
    public float dashHopImpulse = 2f;
    [Tooltip("Vertical speed a dash settles toward after the hop. Slightly negative = the mech skims back down to the floor instead of hovering forever.")]
    public float dashVerticalSettle = -2.5f;
    [Tooltip("How fast leftover momentum bleeds off in the AIR after releasing a dash. Lower = longer inertia glide.")]
    public float airInertiaDrag = 2f;
    [Tooltip("How fast leftover momentum bleeds off on the GROUND.")]
    public float groundMomentumDrag = 6f;
    [Range(0f, 1f)]
    [Tooltip("Stick input authority while airborne (1 = same as ground).")]
    public float airControlFactor = 0.85f;
    public float maxFallSpeed = 25f;
    [Tooltip("How quickly vertical speed snaps toward ascendSpeed when rising. High = crisp EXVS rise.")]
    public float ascendRampSpeed = 160f;

    [Header("Landing Lag (scales with remaining boost)")]
    public float landingLagAtFullBoost = 0.12f;
    public float landingLagAtEmptyBoost = 0.7f;
    public float overheatLandingLag = 1.8f;

    [Header("Landing Polish (playtest feedback: 'landing not smooth / not aligned with floor')")]
    [Tooltip("Blend time INTO the Hard Landing pose. The animator's own transition snapped into the pose - the visible hitch testers called 'not smooth'.")]
    public float landingBlendIn = 0.12f;
    [Tooltip("The landing anim counts as finished at this fraction of the clip. The tail of the Mixamo clip is basically a still frame - waiting for 95% just read as frozen.")]
    [Range(0.3f, 1f)] public float landingAnimDonePoint = 0.7f;

    [HideInInspector] public Vector3 currentMomentum = Vector3.zero;
    private Vector3 currentStepVelocity = Vector3.zero;
    private float dashStartTime = -99f;

    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.4f;
    public LayerMask groundLayer;

    public MechState currentState = MechState.Grounded;
    public Vector3 velocity;
    private float currentIKWeight = 1f;

    // Landing model pin (see Start / LateUpdate)
    private Transform modelT;
    private Vector3 modelBaseLocalPos;
    private bool modelBaseCaptured;

    /// <summary>
    /// Time of this mech's last boost step OR boost dash start. Read by
    /// HomingProjectile: any shot fired BEFORE this moment loses its homing —
    /// the EXVS "stepping breaks tracking" rule, the designed counter to the
    /// "aimbot bullets" complaint.
    /// </summary>
    public float LastEvadeTime { get; private set; } = -99f;
    public void MarkEvade() { LastEvadeTime = Time.time; }

    private InputAction moveAction, dashAction, jumpAction;
    private Vector2 lastFlickDir;
    private float lastFlickTime = 0f, doubleTapWindow = 0.3f, stickDeadzone = 0.3f;
    private bool wasStickNeutral = true;

    private void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value);
        moveAction.AddCompositeBinding("Dpad")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        jumpAction = new InputAction("Jump", InputActionType.Button);
        jumpAction.AddBinding("<Keyboard>/space");

        dashAction = new InputAction("Dash", InputActionType.Button);
        dashAction.AddBinding("<Keyboard>/shift");
    }

    private void OnEnable() { moveAction.Enable(); dashAction.Enable(); jumpAction.Enable(); }
    private void OnDisable() { moveAction.Disable(); dashAction.Disable(); jumpAction.Disable(); }

    private void Start()
    {
        currentHealth = maxHealth;

        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
        combatScript = GetComponent<MechCombat>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        // Rest pose of the model under the capsule, captured while everything is
        // still aligned - the landing pin (see LateUpdate) restores to this.
        if (animator != null && animator.transform != transform)
        {
            modelT = animator.transform;
            modelBaseLocalPos = modelT.localPosition;
            modelBaseCaptured = true;
        }
    }

    // While landing, pin the model to its rest position under the capsule. The
    // Mixamo landing clip carries a little root offset that slid the model off the
    // CharacterController - the "not correctly aligned on the floor" feedback.
    // Runs AFTER the Animator poses the model; only active during the landing state
    // so it can never fight the knockdown pose pin in MechHealth (which disables
    // this whole script while a mech is downed).
    private void LateUpdate()
    {
        if (currentState == MechState.Landing && modelBaseCaptured && modelT != null)
            modelT.localPosition = modelBaseLocalPos;
    }

    private void Update()
    {
        if (Time.timeScale < 0.5f) return; // paused / cut-in / finisher freeze: no input, no state changes

        bool isGrounded = CheckIfGrounded();
        // Guard fully roots the mech: no walk (zeroed in the movement handlers),
        // and now no rise, no dash, no step either. Holding SPACE/SHIFT behind a
        // raised shield used to keep you fully mobile, which made guarding free.
        bool shielding = combatScript != null && combatScript.IsShielding;

        if (currentState != MechState.BoostStep && currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (isGrounded && currentState != MechState.BoostDash && (!jumpAction.IsPressed() || shielding))
            {
                if (currentState == MechState.Airborne) StartCoroutine(ExecuteLandingRecovery());
                else { currentState = MechState.Grounded; velocity.y = -2f; boostManager.Regenerate(true); }
            }
            else if (!isGrounded && currentState != MechState.BoostDash) { currentState = MechState.Airborne; boostManager.Regenerate(false); }
        }

        if (currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (jumpAction.IsPressed() && !shielding && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
            {
                if (currentState == MechState.BoostStep) { StopAllCoroutines(); currentMomentum = currentStepVelocity * 1.3f; }
                // Crisp rise: high ramp speed so the reversal out of a fall is near-instant
                velocity.y = Mathf.MoveTowards(velocity.y, ascendSpeed, ascendRampSpeed * Time.deltaTime);
                boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
                currentState = MechState.Airborne;
            }
            else if (currentState != MechState.BoostDash && currentState != MechState.BoostStep)
            {
                velocity.y += gravity * Time.deltaTime;
                if (velocity.y < -maxFallSpeed) velocity.y = -maxFallSpeed; // terminal fall = floatier EXVS drop
            }
        }
        else if (currentState == MechState.Attacking || currentState == MechState.Landing || currentState == MechState.Staggered)
        {
            if (!isGrounded)
            {
                velocity.y += gravity * Time.deltaTime;
                if (velocity.y < -maxFallSpeed) velocity.y = -maxFallSpeed;
            }
            else
            {
                velocity.y = -2f;
                // Boost keeps refilling while grounded during landing lag / attacks / stagger (EXVS behavior)
                boostManager.Regenerate(true);
            }
        }

        // BOOST STEP WATCHDOG. While stepping, the state machine deliberately ignores
        // movement input. If the step coroutine is ever killed mid-way (StopAllCoroutines
        // from a hit, a knockdown, an overlapping step) the state sticks and the mech is
        // unresponsive forever. Nothing should be in BoostStep for more than a step.
        if (currentState == MechState.BoostStep && Time.time - stepStartedAt > stepDuration * 2.5f + 0.2f)
        {
            currentState = isGrounded ? MechState.Grounded : MechState.Airborne;
            currentStepVelocity = Vector3.zero;
        }

        switch (currentState)
        {
            case MechState.Grounded:
            case MechState.Airborne:
                HandleTargetCentricMovement(isGrounded);
                if (!shielding) { CheckForBoostStepInput(); CheckForBoostDash(); }
                break;
            case MechState.BoostDash:
                HandleBoostDash();
                break;
            case MechState.BoostStep:
                FaceTarget();
                break;
            case MechState.Attacking:
                CheckForBoostStepInput();
                CheckForBoostDash();
                currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * 6f);
                controller.Move(currentMomentum * Time.deltaTime);
                break;
            case MechState.Landing:
                currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * 6f);
                controller.Move(currentMomentum * Time.deltaTime);
                break;
            case MechState.Staggered:
                currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * 10f);
                controller.Move(currentMomentum * Time.deltaTime);
                break;
        }

        controller.Move(velocity * Time.deltaTime);

        // Hard arena limits - nobody leaves the map or flies over the ceiling
        Vector3 boundsFix = ArenaLimits.Correction(transform.position);
        if (boundsFix != Vector3.zero)
        {
            controller.Move(boundsFix);
            if (boundsFix.y < 0f && velocity.y > 0f) velocity.y = 0f;
        }

        UpdateAnimations(isGrounded);
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || enemyTarget == null) return;
        float targetWeight = (currentState == MechState.Attacking || currentState == MechState.Staggered) ? 0f : 1f;
        currentIKWeight = Mathf.Lerp(currentIKWeight, targetWeight, Time.deltaTime * 15f);
        animator.SetLookAtWeight(currentIKWeight, currentIKWeight * 0.2f, currentIKWeight, currentIKWeight, 0.6f);
        animator.SetLookAtPosition(enemyTarget.position + (Vector3.up * 1.5f));
    }

    private void UpdateAnimations(bool isGrounded)
    {
        if (animator == null) return;
        Vector2 input = moveAction.ReadValue<Vector2>();

        if (currentState == MechState.Landing || currentState == MechState.Attacking || currentState == MechState.Staggered)
            input = Vector2.zero;
        if (combatScript != null && combatScript.IsShielding) input = Vector2.zero; // guard pose = stand still

        // Signed inputs drive the 2D Locomotion blend tree: forward run, backpedal,
        // and side strafes - the mech faces the enemy while the legs match movement.
        animator.SetFloat("InputX", input.x, 0.1f, Time.deltaTime);
        animator.SetFloat("InputY", input.y, 0.1f, Time.deltaTime);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsDashing", currentState == MechState.BoostDash);
        animator.SetBool("IsAscending", !isGrounded && velocity.y > 0 && currentState != MechState.BoostDash);
    }

    // ANY solid surface counts as ground now - rooftops, cars, props, not just the
    // "ground layer". The old layer-mask check meant landing on a building never
    // registered as landing (no recovery, boost never recharged). Triggers are
    // ignored and the mech's own colliders are excluded.
    private static readonly Collider[] groundBuf = new Collider[8];
    public bool CheckIfGrounded()
    {
        if (groundCheckPoint == null) return false;
        int n = Physics.OverlapSphereNonAlloc(groundCheckPoint.position, groundCheckRadius, groundBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = groundBuf[i];
            if (c != null && c.transform.root != transform.root) return true;
        }
        return false;
    }

    public void TakeHit(float staggerDuration = 0.8f)
    {
        StopAllCoroutines();
        if (combatScript != null) combatScript.CancelAttack(interruptedByHit: true); // counter-hit: the victim earned the escape, no free down
        if (animator != null) animator.speed = 1f; // in case a previous hit-stop was interrupted

        currentMomentum = Vector3.zero;
        velocity.x = 0;
        velocity.z = 0;

        currentState = MechState.Staggered;
        if (animator != null) animator.SetTrigger("GetHit");

        StartCoroutine(StaggerRecoveryRoutine(staggerDuration));
    }

    // Victim-side hit-stop: brief animation freeze when this mech gets punched (EXVS impact feel).
    // MeleeHitbox calls this AFTER TakeHit so TakeHit's StopAllCoroutines can't kill the freeze.
    public void StartHitStop(float duration)
    {
        StartCoroutine(HitStopVictim(duration));
    }

    private IEnumerator HitStopVictim(float duration)
    {
        if (animator == null) yield break;
        animator.speed = 0f;
        yield return new WaitForSecondsRealtime(duration);
        animator.speed = 1f;
    }

    private IEnumerator StaggerRecoveryRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private void HandleTargetCentricMovement(bool isGrounded)
    {
        if (enemyTarget == null) return;
        Vector3 dirToTarget = (enemyTarget.position - transform.position).normalized; dirToTarget.y = 0;
        Vector2 input = moveAction.ReadValue<Vector2>();
        if (combatScript != null && combatScript.IsShielding) input = Vector2.zero; // guard roots you
        Vector3 moveDir = (Vector3.Cross(Vector3.up, dirToTarget) * input.x) + (dirToTarget * input.y);

        // EXVS / Starward lock-on movement: while locked, the body always FACES THE
        // ENEMY; walking strafes and backpedals around them (the Locomotion blend tree
        // is a 2D InputX/InputY tree built exactly for this). Turning the body to the
        // movement direction (the old behavior) turned your back to the enemy when
        // holding S, which is never how the reference games move. Only boost dashes
        // and steps orient differently.
        if (dirToTarget.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dirToTarget), Time.deltaTime * bodyRotationSpeed);

        // Momentum bleeds slower in the air (inertia glide after a dash), quickly on the ground
        float drag = isGrounded ? groundMomentumDrag : airInertiaDrag;
        currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * drag);

        float control = isGrounded ? 1f : airControlFactor;
        controller.Move(((moveDir * walkSpeed * bigMapSpeedScale * control) + currentMomentum) * Time.deltaTime);
    }

    [Tooltip("Movement multiplier for the 3x arena - scales walking, dashing and steps together so the bigger map doesn't feel like hiking. NEW field (not baked into the scene), so tune it freely here.")]
    public float bigMapSpeedScale = 1.3f;

    private void FaceTarget()
    {
        if (enemyTarget != null) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(enemyTarget.position - transform.position), Time.deltaTime * 20f);
    }

    private void CheckForBoostDash()
    {
        // WasPressedThisFrame: you must click Dash again to cancel an attack (prevents the hold bug)
        if (!jumpAction.IsPressed() && dashAction.WasPressedThisFrame() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            if (currentState == MechState.Attacking && combatScript != null) combatScript.CancelAttack();
            MarkEvade(); // dashing breaks incoming shot tracking (EXVS rule)
            dashStartTime = Time.time; // start of the burst curve
            currentState = MechState.BoostDash;
            // Ground dash pops you slightly off the floor; air dash levels you out
            velocity.y = CheckIfGrounded() ? dashHopImpulse : 0f;
        }
    }

    private void HandleBoostDash()
    {
        // IsPressed here because the dash is maintained while held
        if (!dashAction.IsPressed() || jumpAction.IsPressed() || !boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            if (CheckIfGrounded()) StartCoroutine(ExecuteLandingRecovery());
            else currentState = MechState.Airborne;
            return;
        }
        boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);

        // The hop fades into a gentle downward settle, so a ground dash skims back onto the
        // floor instead of hovering at hop height forever (the old floating bug). The
        // CharacterController just slides along the ground once it touches down.
        velocity.y = Mathf.MoveTowards(velocity.y, dashVerticalSettle, 16f * Time.deltaTime);

        // No NullReferenceException when there is no target — fall back to facing direction
        Vector3 forwardRef = enemyTarget != null ? (enemyTarget.position - transform.position) : transform.forward;
        forwardRef.y = 0f;
        if (forwardRef.sqrMagnitude < 0.001f) forwardRef = transform.forward;
        forwardRef.Normalize();

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDir = (Vector3.Cross(Vector3.up, forwardRef) * input.x) + (forwardRef * (input.magnitude < 0.3f ? 1f : input.y));
        if (moveDir.sqrMagnitude < 0.001f) moveDir = forwardRef;
        moveDir.Normalize();

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * bodyRotationSpeed);

        // Quick snap into a fast, sustained cruise — the dash never bleeds speed while held
        float t = dashBurstSettleTime > 0f ? Mathf.Clamp01((Time.time - dashStartTime) / dashBurstSettleTime) : 1f;
        float speed = Mathf.Lerp(dashBurstSpeed, dashCruiseSpeed, t) * bigMapSpeedScale * dashSpeedScale;

        currentMomentum = moveDir * speed;
        controller.Move(currentMomentum * Time.deltaTime);
    }

    private void CheckForBoostStepInput()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        if (wasStickNeutral && input.magnitude > stickDeadzone)
        {
            Vector2 dir = Mathf.Abs(input.x) > Mathf.Abs(input.y) ? (input.x > 0 ? Vector2.right : Vector2.left) : (input.y > 0 ? Vector2.up : Vector2.down);
            if (dir == lastFlickDir && Time.time - lastFlickTime < doubleTapWindow)
            {
                if (boostManager.CanBoost(boostManager.stepCost))
                {
                    if (currentState == MechState.Attacking && combatScript != null) combatScript.CancelAttack();
                    MarkEvade(); // stepping breaks incoming shot tracking
                    // Never let two steps run at once - overlapping coroutines fought
                    // over the state and could leave it stuck in BoostStep.
                    if (stepRoutine != null) StopCoroutine(stepRoutine);
                    stepRoutine = StartCoroutine(ExecuteBoostStep(dir));
                }
                lastFlickTime = 0f;
            }
            else { lastFlickDir = dir; lastFlickTime = Time.time; }
        }
        // FALSE NEUTRAL - the "hold D, then press A and everything hangs" bug.
        // WASD is a COMPOSITE axis: holding right and pressing left sums to x = 0,
        // which reads as "stick released" even though both keys are down. Releasing
        // one then registered as a fresh flick, which paired with the previous one
        // as a double-tap and fired an unwanted boost step - and while the mech is
        // in BoostStep the state machine ignores movement input entirely, so it
        // looked like the player froze. A neutral only counts when NO movement key
        // is actually held.
        bool anyMoveKeyHeld = Keyboard.current != null &&
            (Keyboard.current.wKey.isPressed || Keyboard.current.aKey.isPressed ||
             Keyboard.current.sKey.isPressed || Keyboard.current.dKey.isPressed ||
             Keyboard.current.upArrowKey.isPressed || Keyboard.current.downArrowKey.isPressed ||
             Keyboard.current.leftArrowKey.isPressed || Keyboard.current.rightArrowKey.isPressed);
        wasStickNeutral = input.magnitude < stickDeadzone && !anyMoveKeyHeld;
    }

    /// <summary>Called by MechCombat when a melee string/tackle STARTS. Wipes the
    /// step double-tap memory so a direction tap made just BEFORE the melee press
    /// can't pair with the first movement re-press DURING the string - that stale
    /// pair read as a "double-tap", rainbow-stepped the player out of their own
    /// attack, and the step momentum kept flying them into the enemy swinging at
    /// nothing (the "movement stops my melee" bug). A rainbow step now requires
    /// both taps to happen during the attack - still instant when deliberate.</summary>
    /// <summary>Called by MechHealth when a knockdown starts and again when the mech
    /// stands up. A down stops every coroutine this script owns (landing recovery,
    /// stagger recovery, boost step), so without an explicit reset the state machine
    /// can wake up stuck in Landing or BoostStep with nothing left alive to move it
    /// on - the mech just stands there.</summary>
    public void ForceResetAfterDown()
    {
        StopAllCoroutines();
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
        currentMomentum = Vector3.zero;
        velocity = new Vector3(0f, -2f, 0f);
        ResetStepFlickBuffer();
        if (animator != null)
        {
            animator.speed = 1f;
            animator.SetBool("IsAttacking", false);
        }
    }

    public void ResetStepFlickBuffer()
    {
        lastFlickTime = -99f;
        lastFlickDir = Vector2.zero;
        wasStickNeutral = false;
    }

    // Lets MechCombat cut an in-progress step cleanly when the player does step -> melee
    // (rainbow step). Without this, the step coroutine kept running under the new attack
    // and then stomped currentState back to Grounded mid-swing, breaking the combo.
    public void CancelBoostStep()
    {
        if (currentState != MechState.BoostStep) return;
        StopAllCoroutines();
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private Coroutine stepRoutine;
    private float stepStartedAt = -99f;

    private IEnumerator ExecuteBoostStep(Vector2 dir)
    {
        currentState = MechState.BoostStep; boostManager.ConsumeBoost(boostManager.stepCost);
        stepStartedAt = Time.time;

        Vector3 dirToTarget = enemyTarget != null ? (enemyTarget.position - transform.position).normalized : transform.forward;
        dirToTarget.y = 0;
        if (dirToTarget.sqrMagnitude < 0.001f) dirToTarget = transform.forward;
        Vector3 stepVec = (Vector3.Cross(Vector3.up, dirToTarget) * dir.x) + (dirToTarget * dir.y);

        if (animator != null)
        {
            animator.SetFloat("StepX", dir.x);
            animator.SetFloat("StepY", dir.y);
            animator.SetTrigger("DoStep");
        }

        float elapsed = 0f;
        while (elapsed < stepDuration)
        {
            currentStepVelocity = stepVec.normalized * Mathf.Lerp(stepSpeed, walkSpeed, elapsed / stepDuration); // steps deliberately NOT speed-scaled - a dodge, not a traversal tool
            controller.Move(currentStepVelocity * Time.deltaTime);
            elapsed += Time.deltaTime; yield return null;
        }

        // Only hand the state back if nothing else (like a step-cancel melee) took over meanwhile
        if (currentState == MechState.BoostStep)
            currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private IEnumerator ExecuteLandingRecovery()
    {
        currentState = MechState.Landing;
        velocity.y = -2f;

        // Settle flush with the floor RIGHT NOW - the ground check triggers a few
        // cm above the surface, so the landing pose used to play while visibly
        // hovering. CharacterController.Move stops at the floor, never clips through.
        if (controller != null) controller.Move(Vector3.down * 0.6f);

        // Blend INTO the landing pose instead of letting the animator hard-snap.
        if (animator != null && animator.HasState(0, Animator.StringToHash("Hard Landing")))
            animator.CrossFadeInFixedTime("Hard Landing", landingBlendIn, 0);

        // Landing lag scales with remaining boost; overheat = long punishment lag.
        float lag = boostManager.isOverheated
            ? overheatLandingLag
            : Mathf.Lerp(landingLagAtEmptyBoost, landingLagAtFullBoost, boostManager.currentBoost / boostManager.maxBoost);

        // Locked until BOTH the lag has passed AND the landing animation has finished -
        // no more running around inside the landing pose.
        float t = 0f;
        bool animDone = false;
        while ((t < lag || !animDone) && t < 2.5f) // 2.5s safety cap
        {
            // Someone else took over (a melee string, a stagger): this landing is
            // VOID. Bail without touching the state or the animator - finishing
            // here used to stomp the state back to Grounded and crossfade
            // Locomotion over the melee windup, killing the attack ("rushed to
            // the enemy but never swung" bug).
            if (currentState != MechState.Landing) yield break;

            t += Time.deltaTime;
            if (animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Hard Landing")) animDone = st.normalizedTime >= landingAnimDonePoint;
                else if (t > 0.4f) animDone = true; // clip never played or already left
            }
            else animDone = true;
            yield return null;
        }

        if (currentState != MechState.Landing) yield break; // taken over on the last frame

        currentState = MechState.Grounded;
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("Hard Landing")) animator.CrossFadeInFixedTime("Locomotion", 0.15f, 0);
        }
    }
}
