using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem; // Require the New Input System

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
    public float stepSpeed = 40f;
    public float stepDuration = 0.2f;
    public float gravity = -20f;

    [Header("Ground Check Settings")]
    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.4f;
    public LayerMask groundLayer;

    [Header("State")]
    public MechState currentState = MechState.Grounded;
    private Vector3 velocity;

    // --- NEW INPUT SYSTEM VARIABLES ---
    private InputAction moveAction;
    private InputAction dashAction;

    // Double-Flick (Step) detection variables
    private Vector2 lastFlickDir;
    private float lastFlickTime = 0f;
    private float doubleTapWindow = 0.3f;
    private bool wasStickNeutral = true;
    private float stickDeadzone = 0.3f; // Prevents stick drift from triggering actions

    private void Awake()
    {
        // Programmatically setup the inputs so you don't need an Action Asset to test
        moveAction = new InputAction("Move", InputActionType.Value, "<Gamepad>/leftStick");
        moveAction.AddCompositeBinding("Dpad")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        dashAction = new InputAction("Dash", InputActionType.Button, "<Gamepad>/buttonSouth"); // Cross on PS, A on Xbox
        dashAction.AddBinding("<Keyboard>/space");
    }

    private void OnEnable()
    {
        moveAction.Enable();
        dashAction.Enable();
    }

    private void OnDisable()
    {
        moveAction.Disable();
        dashAction.Disable();
    }

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
    }

    private void Update()
    {
        // 1. Handle Gravity & State Checking
        bool isGrounded = CheckIfGrounded();

        if (currentState != MechState.BoostStep)
        {
            if (isGrounded && currentState != MechState.BoostDash)
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

        if (currentState != MechState.BoostDash && currentState != MechState.BoostStep)
        {
            velocity.y += gravity * Time.deltaTime;
        }

        // 2. State Machine Logic
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

        // Apply vertical velocity
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

        // Read Vector2 from the New Input System
        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 moveDir = (targetRight * input.x) + (targetForward * input.y);

        if (moveDir.magnitude > 1f) moveDir.Normalize();

        controller.Move(moveDir * walkSpeed * Time.deltaTime);
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
        // Check if the dash button is held down
        if (dashAction.IsPressed() && boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            currentState = MechState.BoostDash;
            velocity.y = 0f;
        }
    }

    private void HandleBoostDash()
    {
        if (!dashAction.IsPressed() || !boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
            return;
        }

        boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);

        Vector2 input = moveAction.ReadValue<Vector2>();

        Vector3 directionToTarget = (enemyTarget.position - transform.position).normalized;
        directionToTarget.y = 0;
        Vector3 targetRight = Vector3.Cross(Vector3.up, directionToTarget);

        // If no input is provided, default to flying forward at the target
        if (input.magnitude < stickDeadzone) input.y = 1;

        Vector3 moveDir = (targetRight * input.x) + (directionToTarget * input.y);
        controller.Move(moveDir.normalized * dashSpeed * Time.deltaTime);

        FaceTarget();
    }

    private void CheckForBoostStepInput()
    {
        Vector2 currentInput = moveAction.ReadValue<Vector2>();
        bool isNeutral = currentInput.magnitude < stickDeadzone;

        // Detect when the stick goes from neutral to pushed
        if (wasStickNeutral && !isNeutral)
        {
            // Quantize the input to pure Up/Down/Left/Right to avoid diagonal step bugs
            Vector2 flickDir = GetPrimaryDirection(currentInput);

            // Check if we double-flicked the SAME direction within the time window
            if (flickDir == lastFlickDir && Time.time - lastFlickTime < doubleTapWindow)
            {
                if (boostManager.CanBoost(boostManager.stepCost))
                {
                    StartCoroutine(ExecuteBoostStep(flickDir));
                }
                lastFlickTime = 0f; // Reset to prevent a triple-tap firing two dodges
            }
            else
            {
                lastFlickDir = flickDir;
                lastFlickTime = Time.time;
            }
        }

        wasStickNeutral = isNeutral;
    }

    // Helper method to turn analog stick circles into a strict 4-way direction
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

        Debug.Log("TRACKING CUT INITIATED: Missiles lose lock-on!");

        Vector3 directionToTarget = (enemyTarget.position - transform.position).normalized;
        directionToTarget.y = 0;
        Vector3 targetRight = Vector3.Cross(Vector3.up, directionToTarget);

        Vector3 stepVector = (targetRight * inputDir.x) + (directionToTarget * inputDir.y);

        float elapsedTime = 0f;

        while (elapsedTime < stepDuration)
        {
            controller.Move(stepVector.normalized * stepSpeed * Time.deltaTime);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }
}