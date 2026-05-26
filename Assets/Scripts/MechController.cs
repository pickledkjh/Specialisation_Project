using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum MechState
{
    Grounded,
    Airborne,
    BoostDash,
    BoostStep,
    Staggered
}

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BoostManager))]
public class MechController : MonoBehaviour
{
    [Header("References")]
    public Transform enemyTarget;
    private CharacterController controller;
    private BoostManager boostManager;

    [Header("Movement Stats")]
    public float walkSpeed = 8f;
    public float dashSpeed = 22f;
    public float stepSpeed = 45f; // Slightly higher to account for the smooth decay
    public float stepDuration = 0.25f;
    public float ascendSpeed = 15f;
    public float gravity = -20f;

    [Header("Inertia & Momentum")]
    public float momentumDrag = 4f;
    private Vector3 currentMomentum = Vector3.zero;

    [Header("Ground Check Settings")]
    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.4f;
    public LayerMask groundLayer;

    [Header("State")]
    public MechState currentState = MechState.Grounded;
    private Vector3 velocity;

    private InputAction moveAction;
    private InputAction dashAction;
    private InputAction jumpAction;

    private Vector2 lastFlickDir;
    private float lastFlickTime = 0f;
    private float doubleTapWindow = 0.3f;
    private bool wasStickNeutral = true;
    private float stickDeadzone = 0.3f;

    private void Awake()
    {
        moveAction = new InputAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
        moveAction.AddCompositeBinding("Dpad")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        jumpAction = new InputAction("Jump", InputActionType.Button, "<Gamepad>/buttonSouth");
        jumpAction.AddBinding("<Keyboard>/space");

        dashAction = new InputAction("Dash", InputActionType.Button, "<Gamepad>/rightTrigger");
        dashAction.AddBinding("<Keyboard>/shift");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        dashAction.Enable();
        jumpAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        dashAction.Disable();
        jumpAction.Disable();
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
    }

    private void Update()
    {
        bool isGrounded = CheckIfGrounded();

        // 1. Ground/Airborne Checks
        if (currentState != MechState.BoostStep)
        {
            if (isGrounded && currentState != MechState.BoostDash && !jumpAction.IsPressed())
            {
                currentState = MechState.Grounded;
                velocity.y = -2f;
                boostManager.Regenerate(true);
            }
            else if (!isGrounded && currentState != MechState.BoostDash)
            {
                currentState = MechState.Airborne;
                boostManager.Regenerate(false);
            }
        }

        // 2. Handle Vertical Movement 
        if (currentState != MechState.BoostStep)
        {
            if (jumpAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
            {
                velocity.y = ascendSpeed;
                boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
                currentState = MechState.Airborne;
            }
            else if (currentState != MechState.BoostDash)
            {
                velocity.y += gravity * Time.deltaTime;
            }
        }

        // 3. State Machine Logic
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
        }

        controller.Move(velocity * Time.deltaTime);
    }

    private bool CheckIfGrounded()
    {
        if (groundCheckPoint == null) return false;
        return Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);
    }

    private void HandleTargetCentricMovement()
    {
        if (enemyTarget == null) return;

        Vector3 directionToTarget = (enemyTarget.position - transform.position).normalized;
        directionToTarget.y = 0;

        Vector3 targetForward = directionToTarget;
        Vector3 targetRight = Vector3.Cross(Vector3.up, targetForward);

        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 moveDir = (targetRight * input.x) + (targetForward * input.y);
        if (moveDir.magnitude > 1f) moveDir.Normalize();

        Vector3 desiredMove = moveDir * walkSpeed;

        currentMomentum = Vector3.Lerp(currentMomentum, Vector3.zero, Time.deltaTime * momentumDrag);

        Vector3 finalMovement = desiredMove + currentMomentum;
        if (finalMovement.magnitude > dashSpeed)
        {
            finalMovement = finalMovement.normalized * dashSpeed;
        }

        controller.Move(finalMovement * Time.deltaTime);
        FaceTarget();
    }

    private void FaceTarget()
    {
        if (enemyTarget == null) return;

        Vector3 lookPos = enemyTarget.position;
        lookPos.y = transform.position.y;
        transform.LookAt(lookPos);
    }

    private void CheckForBoostDash()
    {
        if (jumpAction.IsPressed()) return;

        if (dashAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            currentState = MechState.BoostDash;
            velocity.y = 0f;
        }
    }

    private void HandleBoostDash()
    {
        if (!dashAction.IsPressed() || jumpAction.IsPressed() || !boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
            return;
        }

        boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);

        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 directionToTarget = (enemyTarget.position - transform.position).normalized;
        directionToTarget.y = 0;
        Vector3 targetRight = Vector3.Cross(Vector3.up, directionToTarget);

        if (input.magnitude < stickDeadzone) input.y = 1;

        Vector3 moveDir = (targetRight * input.x) + (directionToTarget * input.y);

        currentMomentum = moveDir.normalized * dashSpeed;

        controller.Move(currentMomentum * Time.deltaTime);
        FaceTarget();
    }

    private void CheckForBoostStepInput()
    {
        Vector2 currentInput = moveAction.ReadValue<Vector2>();
        bool isNeutral = currentInput.magnitude < stickDeadzone;

        if (wasStickNeutral && !isNeutral)
        {
            Vector2 flickDir = GetPrimaryDirection(currentInput);

            if (flickDir == lastFlickDir && Time.time - lastFlickTime < doubleTapWindow)
            {
                if (boostManager.CanBoost(boostManager.stepCost))
                {
                    StartCoroutine(ExecuteBoostStep(flickDir));
                }
                lastFlickTime = 0f;
            }
            else
            {
                lastFlickDir = flickDir;
                lastFlickTime = Time.time;
            }
        }
        wasStickNeutral = isNeutral;
    }

    private Vector2 GetPrimaryDirection(Vector2 input)
    {
        if (Mathf.Abs(input.x) > Mathf.Abs(input.y))
            return input.x > 0 ? Vector2.right : Vector2.left;
        else
            return input.y > 0 ? Vector2.up : Vector2.down;
    }

    private IEnumerator ExecuteBoostStep(Vector2 inputDir)
    {
        currentState = MechState.BoostStep;
        boostManager.ConsumeBoost(boostManager.stepCost);
        velocity.y = 0;

        currentMomentum = Vector3.zero;

        Debug.Log("TRACKING CUT INITIATED: Missiles lose lock-on!");

        Vector3 directionToTarget = (enemyTarget.position - transform.position).normalized;
        directionToTarget.y = 0;
        Vector3 targetRight = Vector3.Cross(Vector3.up, directionToTarget);

        Vector3 stepVector = (targetRight * inputDir.x) + (directionToTarget * inputDir.y);

        float elapsedTime = 0f;

        // NEW: The Dodge Inertia Math
        while (elapsedTime < stepDuration)
        {
            float timeRatio = elapsedTime / stepDuration;

            // Starts at stepSpeed (explosive) and smoothly Lerps down to walkSpeed (friction)
            float currentStepSpeed = Mathf.Lerp(stepSpeed, walkSpeed, timeRatio);

            controller.Move(stepVector.normalized * currentStepSpeed * Time.deltaTime);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // NEW: Seamlessly hand the final speed of the dodge back into the general momentum system!
        currentMomentum = stepVector.normalized * walkSpeed;

        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheckPoint != null)
        {
            Gizmos.color = CheckIfGrounded() ? Color.green : Color.red;
            Gizmos.DrawWireSphere(groundCheckPoint.position, groundCheckRadius);
        }
    }
}