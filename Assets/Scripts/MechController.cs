using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MechState { Grounded, Airborne, BoostDash, BoostStep, Landing, Attacking, Staggered }

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BoostManager))]
public class MechController : MonoBehaviour
{
    public Transform enemyTarget;
    public Animator animator;
    private CharacterController controller;
    private BoostManager boostManager;
    private MechCombat combatScript;

    public float walkSpeed = 8f, dashSpeed = 22f, stepSpeed = 45f, stepDuration = 0.25f, ascendSpeed = 15f, gravity = -20f, bodyRotationSpeed = 15f, landingRecoveryTime = 0.8f, momentumDrag = 4f;
    [HideInInspector] public Vector3 currentMomentum = Vector3.zero;
    private Vector3 currentStepVelocity = Vector3.zero;

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
        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
        combatScript = GetComponent<MechCombat>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    }

    private void Update()
    {
        bool isGrounded = CheckIfGrounded();

        // 1. Check Grounded/Airborne State Transitions
        if (currentState != MechState.BoostStep && currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (isGrounded && currentState != MechState.BoostDash && !jumpAction.IsPressed())
            {
                if (currentState == MechState.Airborne) StartCoroutine(ExecuteLandingRecovery());
                else { currentState = MechState.Grounded; velocity.y = -2f; boostManager.Regenerate(true); }
            }
            else if (!isGrounded && currentState != MechState.BoostDash) { currentState = MechState.Airborne; boostManager.Regenerate(false); }
        }

        // 2. Handle Jump & Gravity
        if (currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (jumpAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
            {
                if (currentState == MechState.BoostStep) { StopAllCoroutines(); currentMomentum = currentStepVelocity * 1.3f; }
                velocity.y = ascendSpeed;
                boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
                currentState = MechState.Airborne;
            }
            else if (currentState != MechState.BoostDash && currentState != MechState.BoostStep) velocity.y += gravity * Time.deltaTime;
        }
        else if (currentState == MechState.Attacking || currentState == MechState.Landing || currentState == MechState.Staggered)
        {
            // Still apply gravity during these locked states so the mech doesn't float forever
            if (!isGrounded) velocity.y += gravity * Time.deltaTime;
            else velocity.y = -2f;
        }

        // 3. Movement Logic Based on State
        switch (currentState)
        {
            case MechState.Grounded:
            case MechState.Airborne:
                HandleTargetCentricMovement();
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
                // Hard brake on momentum when staggered
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

        // Zero out leg animations when locked in animations
        if (currentState == MechState.Landing || currentState == MechState.Attacking || currentState == MechState.Staggered)
            input = Vector2.zero;

        animator.SetFloat("InputY", Mathf.Clamp01(input.magnitude), 0.1f, Time.deltaTime);
        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsDashing", currentState == MechState.BoostDash);
        animator.SetBool("IsAscending", !isGrounded && velocity.y > 0 && currentState != MechState.BoostDash);
    }

    public bool CheckIfGrounded() => groundCheckPoint != null && Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);

    // --- DAMAGE & STAGGER LOGIC ---
    public void TakeHit(float staggerDuration = 0.8f)
    {
        // 1. Cancel any attacks or booststeps immediately
        StopAllCoroutines();
        if (combatScript != null) combatScript.CancelAttack();

        // 2. Stop movement momentum
        currentMomentum = Vector3.zero;
        velocity.x = 0;
        velocity.z = 0;

        // 3. Set state and trigger animation
        currentState = MechState.Staggered;
        if (animator != null) animator.SetTrigger("GetHit");

        // 4. Start the recovery timer
        StartCoroutine(StaggerRecoveryRoutine(staggerDuration));
    }

    private IEnumerator StaggerRecoveryRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private void HandleTargetCentricMovement()
    {
        if (enemyTarget == null) return;
        Vector3 dirToTarget = (enemyTarget.position - transform.position).normalized; dirToTarget.y = 0;
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDir = (Vector3.Cross(Vector3.up, dirToTarget) * input.x) + (dirToTarget * input.y);
        if (moveDir.magnitude > 0.05f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * bodyRotationSpeed);
        currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * momentumDrag);
        controller.Move(((moveDir * walkSpeed) + currentMomentum) * Time.deltaTime);
    }

    private void FaceTarget()
    {
        if (enemyTarget != null) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(enemyTarget.position - transform.position), Time.deltaTime * 20f);
    }

    private void CheckForBoostDash()
    {
        if (!jumpAction.IsPressed() && dashAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            if (currentState == MechState.Attacking && combatScript != null) combatScript.CancelAttack();
            currentState = MechState.BoostDash; velocity.y = 0f;
        }
    }

    private void HandleBoostDash()
    {
        if (!dashAction.IsPressed() || jumpAction.IsPressed() || !boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            if (CheckIfGrounded()) StartCoroutine(ExecuteLandingRecovery());
            else currentState = MechState.Airborne;
            return;
        }
        boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
        Vector3 dirToTarget = (enemyTarget.position - transform.position).normalized; dirToTarget.y = 0;
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 moveDir = (Vector3.Cross(Vector3.up, dirToTarget) * input.x) + (dirToTarget * (input.magnitude < 0.3f ? 1 : input.y));
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), Time.deltaTime * bodyRotationSpeed);
        currentMomentum = moveDir.normalized * dashSpeed;
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

    private IEnumerator ExecuteBoostStep(Vector2 dir)
    {
        currentState = MechState.BoostStep; boostManager.ConsumeBoost(boostManager.stepCost);
        Vector3 dirToTarget = (enemyTarget.position - transform.position).normalized; dirToTarget.y = 0;
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
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private IEnumerator ExecuteLandingRecovery()
    {
        currentState = MechState.Landing; velocity.y = -2f; boostManager.Regenerate(true);
        yield return new WaitForSeconds(landingRecoveryTime); currentState = MechState.Grounded;
    }
}