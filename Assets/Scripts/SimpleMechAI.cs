using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BoostManager))]
public class SimpleMechAI : MonoBehaviour
{
    [Header("Targeting")]
    public Transform playerTarget;
    public Animator animator;
    private CharacterController controller;
    private BoostManager boostManager;

    [Header("AI Movement Stats")]
    public float walkSpeed = 8f;
    public float dashSpeed = 22f;
    public float stepSpeed = 45f;
    public float stepDuration = 0.25f;
    public float dashRange = 20f;
    public float gravity = -20f;
    public float landingRecoveryTime = 0.8f;

    [Header("Ground Check Settings")]
    public Transform groundCheckPoint;
    public float groundCheckRadius = 0.4f;
    public LayerMask groundLayer;

    [Header("AI Combat Settings")]
    public float attackInitiationRange = 15f;
    public float meleeHitRange = 3.5f;
    public float meleeLungeSpeed = 50f;
    public float attackCooldown = 1.2f;

    [Header("Hitboxes")]
    public Collider rightFistCollider;
    public Collider leftFistCollider;
    public Collider leftFootCollider;

    private MechState currentState = MechState.Grounded;
    private Vector3 velocity;
    private Vector3 currentMomentum = Vector3.zero;
    private Vector3 currentStepVelocity = Vector3.zero;

    private float lastAttackTime = 0f;
    private float dodgeTimer = 0f;

    private void Start()
    {
        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        DisableAllHitboxes();
    }

    private void Update()
    {
        if (playerTarget == null) return;

        bool isGrounded = CheckIfGrounded();

        // FAILSAFE: If the Animator ignores an attack trigger, the AI could freeze forever.
        // This timer detects if we've been "Attacking" for 3 seconds and forces a reset.
        if (currentState == MechState.Attacking && Time.time > lastAttackTime + 3f)
        {
            Debug.LogWarning("AI Combo Failsafe Triggered! Unsticking AI.");
            CancelAttack();
        }

        if (currentState != MechState.BoostStep && currentState != MechState.Landing && currentState != MechState.Attacking && currentState != MechState.Staggered)
        {
            if (isGrounded && currentState != MechState.BoostDash)
            {
                if (currentState == MechState.Airborne) StartCoroutine(ExecuteLandingRecovery());
                else { currentState = MechState.Grounded; velocity.y = -2f; boostManager.Regenerate(true); }
            }
            else if (!isGrounded && currentState != MechState.BoostDash) { currentState = MechState.Airborne; boostManager.Regenerate(false); }
        }

        if (currentState == MechState.Attacking || currentState == MechState.Landing || currentState == MechState.Staggered)
        {
            if (!isGrounded) velocity.y += gravity * Time.deltaTime;
            else velocity.y = -2f;
        }

        switch (currentState)
        {
            case MechState.Grounded:
            case MechState.Airborne:
                ThinkAndMove();
                break;
            case MechState.BoostDash:
                HandleBoostDash();
                break;
            case MechState.BoostStep:
                SmoothFaceTarget();
                break;
            case MechState.Attacking:
                SmoothFaceTarget();
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

    // Bulletproof Ground Check (Falls back to standard controller if you forgot to assign the transform)
    public bool CheckIfGrounded()
    {
        if (groundCheckPoint != null) return Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);
        return controller.isGrounded;
    }

    private void ThinkAndMove()
    {
        float distance = Vector3.Distance(transform.position, playerTarget.position);

        if (distance < 10f && Time.time > dodgeTimer && boostManager.CanBoost(boostManager.stepCost))
        {
            if (Random.Range(0, 100) < 2)
            {
                Vector2 randomDodgeDir = Random.value > 0.5f ? Vector2.right : Vector2.left;
                StartCoroutine(ExecuteBoostStep(randomDodgeDir));
                dodgeTimer = Time.time + Random.Range(2f, 4f);
                return;
            }
        }

        if (distance <= attackInitiationRange && Time.time >= lastAttackTime + attackCooldown)
        {
            StartCoroutine(PerformComboSequence());
        }
        else if (distance > attackInitiationRange && distance < dashRange && boostManager.CanBoost(boostManager.dashDepletionRate))
        {
            currentState = MechState.BoostDash;
        }
        else if (distance > attackInitiationRange)
        {
            ChasePlayer();
        }
        else
        {
            animator.SetFloat("InputY", 0f, 0.1f, Time.deltaTime);
        }
    }

    private void ChasePlayer()
    {
        SmoothFaceTarget();
        Vector3 moveDir = transform.forward * walkSpeed;
        controller.Move(moveDir * Time.deltaTime);
        animator.SetFloat("InputY", 1f, 0.1f, Time.deltaTime);
    }

    private void HandleBoostDash()
    {
        float distance = Vector3.Distance(transform.position, playerTarget.position);

        if (distance <= attackInitiationRange || !boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime))
        {
            currentState = MechState.Grounded;
            return;
        }

        boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
        SmoothFaceTarget();

        Vector3 moveDir = transform.forward;
        currentMomentum = moveDir.normalized * dashSpeed;
        controller.Move(currentMomentum * Time.deltaTime);
    }

    private IEnumerator ExecuteBoostStep(Vector2 dir)
    {
        currentState = MechState.BoostStep;
        boostManager.ConsumeBoost(boostManager.stepCost);

        Vector3 dirToTarget = (playerTarget.position - transform.position).normalized;
        dirToTarget.y = 0;
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
            elapsed += Time.deltaTime;
            yield return null;
        }

        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private IEnumerator ExecuteLandingRecovery()
    {
        currentState = MechState.Landing;
        velocity.y = -2f;
        boostManager.Regenerate(true);
        yield return new WaitForSeconds(landingRecoveryTime);
        currentState = MechState.Grounded;
    }

    private void SmoothFaceTarget()
    {
        Vector3 dirToTarget = (playerTarget.position - transform.position).normalized;
        dirToTarget.y = 0;
        if (dirToTarget.magnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 15f);
        }
    }

    private IEnumerator PerformComboSequence()
    {
        currentState = MechState.Attacking;
        lastAttackTime = Time.time; // Reset the failsafe timer!
        animator.SetFloat("InputY", 0f);

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        animator.SetTrigger("Melee1");

        if (distance > meleeHitRange)
        {
            animator.speed = 0f;
            float elapsed = 0f;
            while (elapsed < 0.8f)
            {
                if (playerTarget == null || currentState != MechState.Attacking) break;
                Vector3 toTarget = playerTarget.position - transform.position;
                toTarget.y = 0;

                if (toTarget.magnitude <= meleeHitRange) break;

                controller.Move(toTarget.normalized * meleeLungeSpeed * Time.deltaTime);
                elapsed += Time.deltaTime;
                yield return null;
            }
            animator.speed = 1f;
        }

        yield return new WaitForSeconds(0.5f);

        if (currentState == MechState.Attacking && Vector3.Distance(transform.position, playerTarget.position) < meleeHitRange + 2f)
        {
            animator.SetTrigger("Melee2");
            StartCoroutine(MicroLunge());
            yield return new WaitForSeconds(0.5f);

            if (currentState == MechState.Attacking && Vector3.Distance(transform.position, playerTarget.position) < meleeHitRange + 2f)
            {
                animator.SetTrigger("Melee3");
                StartCoroutine(MicroLunge());
                yield return new WaitForSeconds(0.5f);

                if (currentState == MechState.Attacking && Vector3.Distance(transform.position, playerTarget.position) < meleeHitRange + 2f)
                {
                    animator.SetTrigger("Melee4");
                    StartCoroutine(MicroLunge());
                }
            }
        }
    }

    private IEnumerator MicroLunge()
    {
        float elapsedTime = 0f;
        while (elapsedTime < 0.15f)
        {
            if (playerTarget != null && currentState == MechState.Attacking)
            {
                Vector3 toTarget = (playerTarget.position - transform.position).normalized;
                toTarget.y = 0;
                controller.Move(toTarget * 12f * Time.deltaTime);
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }

    private void UpdateAnimations(bool isGrounded)
    {
        if (animator == null) return;

        if (currentState == MechState.Attacking || currentState == MechState.Staggered || currentState == MechState.Landing)
        {
            animator.SetFloat("InputX", 0f);
            animator.SetFloat("InputY", 0f);
        }

        animator.SetBool("IsGrounded", isGrounded);
        animator.SetBool("IsDashing", currentState == MechState.BoostDash);
        animator.SetBool("IsAscending", !isGrounded && velocity.y > 0 && currentState != MechState.BoostDash);
    }

    // --- ANIMATION EVENTS & HITBOXES ---
    public void EnableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = true; }
    public void DisableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = false; }
    public void EnableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = true; }
    public void DisableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = false; }
    public void EnableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = true; }
    public void DisableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = false; }

    private void DisableAllHitboxes() { DisableRightFist(); DisableLeftFist(); DisableLeftFoot(); }

    public void EndAttack()
    {
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
        lastAttackTime = Time.time;
        DisableAllHitboxes();
        animator.ResetTrigger("Melee1");
        animator.ResetTrigger("Melee2");
        animator.ResetTrigger("Melee3");
        animator.ResetTrigger("Melee4");
    }

    public void CancelAttack()
    {
        StopAllCoroutines();
        if (animator != null) animator.speed = 1f;
        EndAttack();
    }

    // --- CAMERA EVENT DUMMIES ---
    public void SwitchToPunch1Camera() { }
    public void SwitchToPunch4Camera() { }
    public void SwitchToNormalCamera() { }

    // --- DAMAGE & STAGGER ---
    public void TakeHit(float staggerDuration = 0.8f, float damage = 0)
    {
        CancelAttack();
        currentMomentum = Vector3.zero;
        velocity.x = 0;
        velocity.z = 0;

        currentState = MechState.Staggered;

        // **CRITICAL SPELLING CHECK HERE:**
        // Make sure your Animator parameter is spelled EXACTLY like the string below.
        // If your parameter is all lowercase "gethit", change "GetHit" to "gethit" below!
        if (animator != null) animator.SetTrigger("GetHit");

        StartCoroutine(StaggerRecovery(staggerDuration));
    }

    private IEnumerator StaggerRecovery(float duration)
    {
        yield return new WaitForSeconds(duration);
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    // --- HIT STOP ---
    public void StartHitStop(float duration) { StartCoroutine(HitStopCoroutine(duration)); }
    private IEnumerator HitStopCoroutine(float duration)
    {
        float originalSpeed = animator.speed;
        animator.speed = 0f;
        yield return new WaitForSecondsRealtime(duration);
        animator.speed = originalSpeed;
    }
}