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
    [Tooltip("Speed of the initial dash snap.")]
    public float dashBurstSpeed = 30f;
    [Tooltip("Speed the dash holds while the button stays down. Keep this HIGH ¡ª an EXVS dash does not slow down while held.")]
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

    [HideInInspector] public Vector3 currentMomentum = Vector3.zero;
    private Vector3 currentStepVelocity = Vector3.zero;
    private float dashStartTime = -99f;

    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.4f;
    public LayerMask groundLayer;

    public MechState currentState = MechState.Grounded;
    public Vector3 velocity;
    private float currentIKWeight = 1f;

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
    }

    private void Update()
    {
        bool isGrounded = CheckIfGrounded();

        if (currentState != MechState.BoostStep && currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (isGrounded && currentState != MechState.BoostDash && !jumpAction.IsPressed())
            {
                if (currentState == MechState.Airborne) StartCoroutine(ExecuteLandingRecovery());
                else { currentState = MechState.Grounded; velocity.y = -2f; boostManager.Regenerate(true); }
            }
            else if (!isGrounded && currentState != MechState.BoostDash) { currentState = MechState.Airborne; boostManager.Regenerate(false); }
        }

        if (currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (jumpAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
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

        switch (currentState)
        {
            case MechState.Grounded:
            case MechState.Airborne:
                HandleTargetCentricMovement(isGrounded);
                CheckForBoostStepInput();
                CheckForBoostDash();
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

        animator.SetFloat("InputY", Mathf.Clamp01(input.magnitude), 0.1f, Time.deltaTime);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsDashing", currentState == MechState.BoostDash);
        animator.SetBool("IsAscending", !isGrounded && velocity.y > 0 && currentState != MechState.BoostDash);
    }

    public bool CheckIfGrounded() => groundCheckPoint != null && Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);

    public void TakeHit(float staggerDuration = 0.8f)
    {
        StopAllCoroutines();
        if (combatScript != null) combatScript.CancelAttack();
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
        Vector3 moveDir = (Vector3.Cross(Vector3.up, dirToTarget) * input.x) + (dirToTarget * input.y);
        if (moveDir.magnitude > 0.05f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * bodyRotationSpeed);

        // Momentum bleeds slower in the air (inertia glide after a dash), quickly on the ground
        float drag = isGrounded ? groundMomentumDrag : airInertiaDrag;
        currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * drag);

        float control = isGrounded ? 1f : airControlFactor;
        controller.Move(((moveDir * walkSpeed * control) + currentMomentum) * Time.deltaTime);
    }

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

        // No NullReferenceException when there is no target ¡ª fall back to facing direction
        Vector3 forwardRef = enemyTarget != null ? (enemyTarget.position - transform.position) : transform.forward;
        forwardRef.y = 0f;
        if (forwardRef.sqrMagnitude < 0.001f) forwardRef = transform.forward;
        forwardRef.Normalize();

        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDir = (Vector3.Cross(Vector3.up, forwardRef) * input.x) + (forwardRef * (input.magnitude < 0.3f ? 1f : input.y));
        if (moveDir.sqrMagnitude < 0.001f) moveDir = forwardRef;
        moveDir.Normalize();

        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * bodyRotationSpeed);

        // Quick snap into a fast, sustained cruise ¡ª the dash never bleeds speed while held
        float t = dashBurstSettleTime > 0f ? Mathf.Clamp01((Time.time - dashStartTime) / dashBurstSettleTime) : 1f;
        float speed = Mathf.Lerp(dashBurstSpeed, dashCruiseSpeed, t);

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
                    StartCoroutine(ExecuteBoostStep(dir));
                }
                lastFlickTime = 0f;
            }
            else { lastFlickDir = dir; lastFlickTime = Time.time; }
        }
        wasStickNeutral = input.magnitude < stickDeadzone;
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

    private IEnumerator ExecuteBoostStep(Vector2 dir)
    {
        currentState = MechState.BoostStep; boostManager.ConsumeBoost(boostManager.stepCost);

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
            currentStepVelocity = stepVec.normalized * Mathf.Lerp(stepSpeed, walkSpeed, elapsed / stepDuration);
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

        // Landing lag scales with remaining boost; overheat = long punishment lag.
        // Boost refills during the lag via the Regenerate(true) call in Update's grounded branch.
        float lag = boostManager.isOverheated
            ? overheatLandingLag
            : Mathf.Lerp(landingLagAtEmptyBoost, landingLagAtFullBoost, boostManager.currentBoost / boostManager.maxBoost);

        yield return new WaitForSeconds(lag);
        currentState = MechState.Grounded;
    }
}