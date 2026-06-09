using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine; // Use the newer namespace

[RequireComponent(typeof(MechController))]
[RequireComponent(typeof(CharacterController))]
public class MechCombat : MonoBehaviour
{
    private MechController mechController;
    private CharacterController characterController;
    private Animator animator;

    [Header("Cameras")]
    public CinemachineCamera punch1Camera;
    public CinemachineCamera punch4Camera;
    public CinemachineImpulseSource impulseSource; // DRAG YOUR NEW COMPONENT HERE

    [Header("Lock-On Ranges")]
    public float redLockRange = 40f;
    public float meleeHitRange = 3.5f;
    public float meleeLungeSpeed = 50f;

    [Header("Combat Hitboxes")]
    public Collider rightFistCollider;
    public Collider leftFistCollider;
    public Collider leftFootCollider;

    [Header("Combo Settings")]
    public float comboResetTime = 1.0f;
    public float endLagDuration = 0.6f;

    private int currentMeleeStep = 0;
    private float lastMeleeTime = 0f;

    private bool isAttacking = false;
    private bool isLunging = false;
    private bool isInEndLag = false;
    private bool startedInRedLock = false;
    private bool isFrozen = false;

    private InputAction shootAction;
    private InputAction meleeAction;

    private void Awake()
    {
        mechController = GetComponent<MechController>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();

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

    // --- HIT STOP & SHAKE LOGIC ---
    public void StartHitStop(float duration)
    {
        if (isFrozen) return;

        // Trigger Screen Shake!
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

    // --- CAMERA EVENT METHODS ---
    public void SwitchToPunch1Camera() { if (punch1Camera != null) punch1Camera.Priority = 20; }
    public void SwitchToPunch4Camera() { if (punch4Camera != null) punch4Camera.Priority = 20; }
    public void SwitchToNormalCamera()
    {
        if (punch1Camera != null) punch1Camera.Priority = 5;
        if (punch4Camera != null) punch4Camera.Priority = 5;
    }

    private void OnEnable() { shootAction.Enable(); meleeAction.Enable(); }
    private void OnDisable() { shootAction.Disable(); meleeAction.Disable(); }

    private void Update()
    {
        if (currentMeleeStep > 0 && Time.time - lastMeleeTime > comboResetTime && !isLunging && !isInEndLag)
            StartCoroutine(EndLagRoutine());

        if (isAttacking && mechController.enemyTarget != null && !isInEndLag) SmoothFaceEnemy();

        if (mechController.currentState != MechState.Landing &&
            mechController.currentState != MechState.Staggered && !isInEndLag)
            HandleInputs();
    }

    private void SmoothFaceEnemy()
    {
        Vector3 toTarget = mechController.enemyTarget.position - transform.position;
        toTarget.y = 0;
        if (toTarget.magnitude > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(toTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 15f);
        }
    }

    private void HandleInputs()
    {
        if (shootAction.WasPressedThisFrame()) animator.SetTrigger("Shoot");
        if (meleeAction.WasPressedThisFrame() && !isLunging) PerformMeleeCombo();
    }

    private void PerformMeleeCombo()
    {
        isAttacking = true;
        mechController.currentState = MechState.Attacking;
        lastMeleeTime = Time.time;

        if (currentMeleeStep == 0)
        {
            float distanceToEnemy = GetDistanceToTarget();
            if (distanceToEnemy <= redLockRange)
            {
                startedInRedLock = true;
                if (distanceToEnemy > meleeHitRange) { StartCoroutine(MeleeLungeRoutine()); return; }
                else TriggerPunch(1);
            }
            else { startedInRedLock = false; TriggerPunch(1); }
        }
        else if (currentMeleeStep == 1) TriggerPunch(2);
        else if (currentMeleeStep == 2) TriggerPunch(3);
        else if (currentMeleeStep >= 3) TriggerPunch(4);
    }

    private float GetDistanceToTarget()
    {
        if (mechController.enemyTarget == null) return 999f;
        Vector3 toTarget = mechController.enemyTarget.position - transform.position;
        toTarget.y = 0;
        return toTarget.magnitude;
    }

    private void TriggerPunch(int step)
    {
        currentMeleeStep = step;
        animator.SetTrigger("Melee" + step);
        if (step > 1 && startedInRedLock) StartCoroutine(MicroLunge());
    }

    private IEnumerator MeleeLungeRoutine()
    {
        isLunging = true;
        currentMeleeStep = 1;
        animator.SetTrigger("Melee1");
        animator.speed = 0f;

        float elapsedTime = 0f;
        while (elapsedTime < 0.8f)
        {
            if (mechController.enemyTarget == null) break;
            Vector3 toTarget = mechController.enemyTarget.position - transform.position;
            toTarget.y = 0;
            if (toTarget.magnitude <= meleeHitRange) break;
            characterController.Move(toTarget.normalized * meleeLungeSpeed * Time.deltaTime);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        animator.speed = 1f;
        isLunging = false;
        lastMeleeTime = Time.time;
    }

    private IEnumerator MicroLunge()
    {
        float elapsedTime = 0f;
        while (elapsedTime < 0.15f)
        {
            if (mechController.enemyTarget != null)
            {
                Vector3 toTarget = (mechController.enemyTarget.position - transform.position).normalized;
                toTarget.y = 0;
                characterController.Move(toTarget * 12f * Time.deltaTime);
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }

    public void EnableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = true; }
    public void DisableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = false; }
    public void EnableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = true; }
    public void DisableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = false; }
    public void EnableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = true; }
    public void DisableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = false; }

    public void EndAttack()
    {
        if (!isAttacking || isInEndLag) return;
        StartCoroutine(EndLagRoutine());
    }

    private IEnumerator EndLagRoutine()
    {
        SwitchToNormalCamera();
        isInEndLag = true;
        isAttacking = false;
        isLunging = false;
        currentMeleeStep = 0;
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
        currentMeleeStep = 0;
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