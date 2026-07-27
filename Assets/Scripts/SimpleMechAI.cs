using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(BoostManager))]
public class SimpleMechAI : MonoBehaviour
{
    [Header("Health & Status")]
    public float maxHealth = 100f;
    public float currentHealth;
    public bool isDead = false;

    [Header("Targeting")]
    public Transform playerTarget;
    public Animator animator;
    private CharacterController controller;
    private BoostManager boostManager;
    private MechHealth playerHealth;

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
    public float meleeLungeSpeed = 65f;
    public float attackCooldown = 1.2f;
    public float lungePoseDelay = 0.15f;
    [Tooltip("The melee rush closes to this distance before swinging. The old value (meleeHitRange 3.5) stopped outside fist reach and every tutorial attack whiffed.")]
    public float lungeStopDistance = 1.9f;

    [Header("AI Combo")]
    [Tooltip("The Melee1-4 string repeats this many times before the launcher. Needs the same Melee4 -> Melee1 animator transition as the player when > 1.")]
    public int comboLoops = 2;
    public float finisherLaunchForward = 16f;
    public float finisherLaunchUp = 8f;
    [Tooltip("Bar power of the AI's final hit - fills whatever the string left over. (Renamed from finisherKnockdownPower so the stronger default applies.)")]
    public float finisherBarPower = 60f;

    [Header("Hitboxes")]
    public Collider rightFistCollider;
    public Collider leftFistCollider;
    public Collider leftFootCollider;

    [Header("AI Brain - ranged")]
    [Tooltip("The AI shoots inside this range (shots home inside 40, matching red lock).")]
    public float shootRange = 45f;
    [Tooltip("Gap between AI shots. The player fires every 0.6s - keep the AI a bit slower so it reads as fair.")]
    public float aiShotCooldown = 1.4f;
    public int aiMaxAmmo = 8;
    public float aiReloadTime = 5f;
    [Tooltip("Chance the AI spends 2 ammo on a heavy charge shot when it catches you landing or getting up.")]
    public float chargeShotChance = 0.3f;

    [Header("AI Brain - movement & reactions")]
    [Tooltip("Inside this range the AI prefers to commit to melee strings.")]
    public float meleeCommitRange = 9f;
    [Tooltip("The strafing band the AI tries to hold in neutral (it orbits you at roughly this distance).")]
    public float preferredRange = 12f;
    [Tooltip("Chance to sidestep when you rush it with melee or a shot is inbound. 1 = dodges everything (unfair), 0 = the old punching bag.")]
    [Range(0f, 1f)] public float dodgeReactionChance = 0.6f;
    [Tooltip("How often the AI re-checks for threats (seconds). Lower = faster reactions.")]
    public float dodgeCheckInterval = 0.35f;
    [Tooltip("The AI won't start boost dashes below this much boost - it saves gauge for landings like a player would.")]
    public float minBoostToDash = 30f;

    [Header("AI Shield")]
    [Tooltip("When the AI reacts to a threat, this portion of reactions raise the shield instead of sidestepping. Blocked melee stuns the ATTACKER - so respect the guard.")]
    [Range(0f, 1f)] public float shieldChance = 0.4f;
    public float shieldDuration = 0.9f;
    [Tooltip("Frontal blocking arc, same meaning as the player's shieldFrontDot.")]
    public float shieldFrontDot = 0.2f;

    [Header("Tutorial")]
    [Tooltip("Set by the tutorial: the AI only strafes - no attacks, no shots - so the player can practice safely.")]
    public bool passiveMode = false;

    private bool isShielding = false;
    private GameObject shieldVisual;
    public bool IsShieldUp => isShielding;

    private MechController playerMech;   // to read the player's state (landing/stagger punishes)
    private MechHealth myHealth;         // to break our own wake-up protection when we act
    private int aiAmmo;
    private float aiEmptySince = -1f;
    private float lastAiShotTime = -99f;
    private float nextDodgeCheck = 0f;
    private float strafeDir = 1f;
    private float nextStrafeFlip = 0f;

    private MechState currentState = MechState.Grounded;
    private Vector3 velocity;
    private Vector3 currentMomentum = Vector3.zero;
    private Vector3 currentStepVelocity = Vector3.zero;

    private float lastAttackTime = 0f;
    private float dodgeTimer = 0f;
    private int currentComboHit = 0;
    public bool hasHitConnected = false;

    // True once PerformComboSequence has stopped queuing further swings (finished or
    // aborted). EndAttack only ends the attack state when the chain is really done,
    // so the EndAttack animation event on each clip can't cut a running combo short.
    private bool comboChainDone = true;

    private int TotalHits => Mathf.Max(1, comboLoops) * 4;
    private int AnimIndexForHit(int hit) => ((hit - 1) % 4) + 1;

    private void Start()
    {
        currentHealth = maxHealth;

        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        if (playerTarget != null)
        {
            playerHealth = playerTarget.GetComponentInParent<MechHealth>();
            if (playerHealth == null) playerHealth = playerTarget.GetComponentInChildren<MechHealth>();
            playerMech = playerTarget.GetComponentInParent<MechController>();
            if (playerMech == null) playerMech = playerTarget.GetComponentInChildren<MechController>();
        }

        myHealth = GetComponent<MechHealth>();

        aiAmmo = aiMaxAmmo;
        DisableAllHitboxes();
    }

    private void Update()
    {
        if (playerTarget == null) return;

        // Ammo reload mirrors the player's rifle
        if (aiAmmo <= 0 && aiEmptySince >= 0f && Time.time - aiEmptySince >= aiReloadTime)
        {
            aiAmmo = aiMaxAmmo;
            aiEmptySince = -1f;
        }

        bool isGrounded = CheckIfGrounded();

        if (currentState == MechState.Attacking && Time.time > lastAttackTime + 3f + TotalHits * 0.5f)
        {
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

        // Hard arena limits (same as the player)
        Vector3 boundsFix = ArenaLimits.Correction(transform.position);
        if (boundsFix != Vector3.zero)
        {
            controller.Move(boundsFix);
            if (boundsFix.y < 0f && velocity.y > 0f) velocity.y = 0f;
        }

        UpdateAnimations(isGrounded);
    }

    public void RegisterHit()
    {
        hasHitConnected = true;
        // No forced knockdown — the down comes from the ONE bar system inside
        // MechHealth.TakeDamage. MeleeHitbox passes this AI's finisher bar power and
        // big launch on the final hit (see IsFinisherHit / GetCurrentKnockdownPower).
    }

    // Queried by MeleeHitbox, mirroring MechCombat's API.
    public bool IsFinisherHit => currentComboHit >= TotalHits;
    public float GetCurrentKnockdownPower(float normalHitPower)
    {
        return IsFinisherHit ? finisherBarPower : normalHitPower;
    }

    public bool CheckIfGrounded()
    {
        if (groundCheckPoint != null) return Physics.CheckSphere(groundCheckPoint.position, groundCheckRadius, groundLayer);
        return controller.isGrounded;
    }

    // ------------------------------------------------------------------
    // THE BRAIN. Priorities each frame, closest to how a human plays EXVS:
    //   1. React  - sidestep incoming melee rushes and projectiles
    //   2. Punish - rush landings and staggers, snipe getting-up windows
    //   3. Commit - melee string when close and off cooldown
    //   4. Neutral - hold the strafe band, orbit, pepper with shots
    //   5. Approach - dash in with boost awareness, walk + shoot otherwise
    // ------------------------------------------------------------------
    private void ThinkAndMove()
    {
        float distance = Vector3.Distance(transform.position, playerTarget.position);

        // Player downed: don't stand like a statue. Back off toward the strafe band,
        // keep moving sideways, let the rifle reload - be repositioned and loaded the
        // moment they stand up (what any decent human does with a free moment).
        if (playerHealth != null && playerHealth.isYellowLocked)
        {
            StrafeAround(distance, retreatBias: distance < preferredRange ? 1f : 0f);
            return;
        }

        // Tutorial: strafe only - no attacking, no shooting, no reactions
        if (passiveMode)
        {
            StrafeAround(distance);
            return;
        }

        // Holding the guard: face the player and stand firm until it drops
        if (isShielding)
        {
            SmoothFaceTarget();
            return;
        }

        // 1. REACT: dodge OR guard against melee rushes and incoming shots
        if (Time.time >= nextDodgeCheck)
        {
            nextDodgeCheck = Time.time + dodgeCheckInterval;
            if (ThreatIncoming(distance) &&
                boostManager.CanBoost(boostManager.stepCost) &&
                Random.value < dodgeReactionChance)
            {
                if (Random.value < shieldChance)
                {
                    StartCoroutine(ShieldFor(shieldDuration));
                }
                else
                {
                    strafeDir = Random.value > 0.5f ? 1f : -1f;
                    StartCoroutine(ExecuteBoostStep(strafeDir > 0f ? Vector2.right : Vector2.left));
                }
                return;
            }
        }

        // 2. PUNISH: the player is stuck in landing lag or stagger
        MechState playerState = playerMech != null ? playerMech.currentState : MechState.Grounded;
        bool punishWindow = playerState == MechState.Landing || playerState == MechState.Staggered;
        if (punishWindow)
        {
            if (distance <= attackInitiationRange * 1.5f &&
                Time.time >= lastAttackTime + attackCooldown * 0.5f) // punishes come out faster
            {
                StartCoroutine(PerformComboSequence());
                return;
            }
            TryShoot(distance, allowCharge: true); // too far to reach in time: heavy shot
        }

        // 3. COMMIT: close-range melee
        if (distance <= meleeCommitRange && Time.time >= lastAttackTime + attackCooldown)
        {
            StartCoroutine(PerformComboSequence());
            return;
        }

        // 4. NEUTRAL: hold the band, orbit, shoot on cooldown
        if (distance <= shootRange)
        {
            TryShoot(distance, allowCharge: false);
            StrafeAround(distance);
            return;
        }

        // 5. APPROACH: far away - dash in if the gauge can afford it, otherwise
        // walk in while shooting (never dash into overheat like the old AI)
        if (distance < dashRange * 2f && boostManager.CanBoost(minBoostToDash))
        {
            currentState = MechState.BoostDash;
            return;
        }
        TryShoot(distance, allowCharge: false);
        ChasePlayer();
    }

    // The AI's own rifle - runtime projectiles, same bar-feeding rules as the player's
    private void TryShoot(float distance, bool allowCharge)
    {
        if (Time.time - lastAiShotTime < aiShotCooldown) return;
        if (aiAmmo <= 0) return;
        if (distance > shootRange) return;

        bool charge = allowCharge && aiAmmo >= 2 && Random.value < chargeShotChance;

        if (myHealth != null) myHealth.BreakWakeUpProtection(); // shooting forfeits wake-up protection too

        lastAiShotTime = Time.time;
        aiAmmo -= charge ? 2 : 1;
        if (aiAmmo <= 0) aiEmptySince = Time.time;

        Vector3 spawnPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.5f;
        Vector3 aimPoint = playerTarget.position + Vector3.up * 1.5f;
        Vector3 aimDir = aimPoint - spawnPos;
        Quaternion rot = aimDir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(aimDir.normalized) : transform.rotation;

        HomingProjectile shot = charge
            ? HomingProjectile.SpawnSimple(spawnPos, rot, 2f, new Color(1f, 0.35f, 0.2f))
            : HomingProjectile.SpawnSimple(spawnPos, rot, 1f, new Color(0.4f, 0.9f, 1f)); // AI shots cyan so you can read them
        if (charge)
        {
            shot.damage = 30f;
            shot.knockdownPower = 60f;
            shot.speed = 80f;
            shot.turnRateDegPerSec = 60f;
        }

        bool redLock = distance <= 40f && (playerHealth == null || !playerHealth.isYellowLocked);
        shot.Init(redLock ? playerTarget : null, transform);
    }

    // EXVS guard: blocks frontal melee and shots; a blocked melee stuns the attacker
    // (resolved in MeleeHitbox). Dropped early if the AI gets staggered (CancelAttack).
    private IEnumerator ShieldFor(float duration)
    {
        if (myHealth != null) myHealth.BreakWakeUpProtection();
        isShielding = true;
        if (shieldVisual == null) shieldVisual = ShieldVisual.Create(transform);
        shieldVisual.SetActive(true);

        float t = 0f;
        while (t < duration && currentState == MechState.Grounded)
        {
            t += Time.deltaTime;
            yield return null;
        }

        isShielding = false;
        if (shieldVisual != null) shieldVisual.SetActive(false);
    }

    // Queried by MeleeHitbox / HomingProjectile
    public bool IsBlocking(Vector3 attackerPosition)
    {
        if (!isShielding) return false;
        Vector3 to = attackerPosition - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return true;
        return Vector3.Dot(transform.forward, to.normalized) >= shieldFrontDot;
    }

    // True when the player is actively swinging nearby, or one of their shots is
    // flying at us - the triggers a human would sidestep on reaction
    private bool ThreatIncoming(float distance)
    {
        if (playerMech != null && playerMech.currentState == MechState.Attacking && distance < 14f)
            return true;

        HomingProjectile[] shots = FindObjectsByType<HomingProjectile>(FindObjectsSortMode.None);
        foreach (HomingProjectile s in shots)
        {
            if (s == null || s.ShooterRoot == transform) continue;
            Vector3 to = (transform.position + Vector3.up * 1.5f) - s.transform.position;
            float dist = to.magnitude;
            if (dist < 18f && dist > 0.5f && Vector3.Dot(s.transform.forward, to / dist) > 0.8f)
                return true;
        }
        return false;
    }

    // Neutral movement: face the player, orbit sideways in the preferred band,
    // drifting in/out to hold it. retreatBias > 0 forces backing off.
    private void StrafeAround(float distance, float retreatBias = 0f)
    {
        SmoothFaceTarget();

        if (Time.time >= nextStrafeFlip)
        {
            strafeDir = Random.value > 0.5f ? 1f : -1f;
            nextStrafeFlip = Time.time + Random.Range(1.5f, 3.5f);
        }

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.01f) return;
        toPlayer.Normalize();

        // radial: + = toward player, - = away; hold the band around preferredRange
        float radial = retreatBias > 0f ? -retreatBias
                     : distance > preferredRange + 4f ? 0.6f
                     : distance < preferredRange - 4f ? -0.6f
                     : 0f;

        Vector3 tangent = Vector3.Cross(Vector3.up, toPlayer) * strafeDir;
        Vector3 move = (tangent + toPlayer * radial).normalized;
        controller.Move(move * walkSpeed * Time.deltaTime);

        // Drive the strafe blend tree (mirrors the player's face-the-enemy movement)
        animator.SetFloat("InputX", strafeDir, 0.1f, Time.deltaTime);
        animator.SetFloat("InputY", radial, 0.1f, Time.deltaTime);
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
        if (myHealth != null) myHealth.BreakWakeUpProtection(); // stepping forfeits protection

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

        // Only hand the state back if the step wasn't taken over by something else meanwhile
        if (currentState == MechState.BoostStep)
            currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    private IEnumerator ExecuteLandingRecovery()
    {
        currentState = MechState.Landing;
        velocity.y = -2f;
        boostManager.Regenerate(true);

        // Locked until the lag AND the landing animation are both done (same as player)
        float t = 0f;
        bool animDone = false;
        while ((t < landingRecoveryTime || !animDone) && t < 2.5f)
        {
            t += Time.deltaTime;
            if (animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Hard Landing")) animDone = st.normalizedTime >= 0.95f;
                else if (t > 0.4f) animDone = true;
            }
            else animDone = true;
            yield return null;
        }

        currentState = MechState.Grounded;
        if (animator != null)
        {
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
            if (st.IsName("Hard Landing")) animator.CrossFadeInFixedTime("Locomotion", 0.15f, 0);
        }
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
        // Acting forfeits wake-up invincibility IMMEDIATELY — the AI cannot attack
        // you while still yellow-locked from its own knockdown (mirrors the player).
        if (myHealth != null) myHealth.BreakWakeUpProtection();

        currentState = MechState.Attacking;
        lastAttackTime = Time.time;
        animator.SetFloat("InputY", 0f);
        hasHitConnected = false;
        currentComboHit = 1;
        comboChainDone = false;
        DisableAllHitboxes();

        float distance = Vector3.Distance(transform.position, playerTarget.position);
        // CrossFade, not SetTrigger — the code forces the punch state deterministically
        // (stranded triggers were one of the combo-breaking animation bugs)
        if (animator != null) animator.CrossFadeInFixedTime("punch1", 0.08f, 0);

        if (distance > meleeHitRange)
        {
            yield return new WaitForSeconds(lungePoseDelay);

            // Same fix as the player: only freeze the animator if the punch windup was
            // actually reached — freezing the run/dash pose looked broken.
            float poseWait = 0f;
            bool reachedWindup = false;
            while (poseWait < 0.3f && animator != null)
            {
                AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("punch1") || st.IsName("Melee1")) { reachedWindup = true; break; }
                poseWait += Time.deltaTime;
                yield return null;
            }

            if (animator != null && reachedWindup) animator.speed = 0f;

            float elapsed = 0f;
            float currentSpeed = meleeLungeSpeed;

            float speedDrag = 1.5f;
            float maxLungeTime = 0.8f;

            while (elapsed < maxLungeTime)
            {
                if (playerTarget == null || currentState != MechState.Attacking) break;

                // Pivot-to-pivot 3D homing: rush to the player's level instead of climbing
                // above them (the chest-height aim was lifting the AI into the air)
                Vector3 toTarget = playerTarget.position - transform.position;
                Vector3 flat = toTarget; flat.y = 0f;

                if (flat.magnitude <= lungeStopDistance && Mathf.Abs(toTarget.y) <= 2.5f) break;

                currentSpeed = Mathf.Lerp(currentSpeed, 10f, Time.deltaTime * speedDrag);
                controller.Move(toTarget.normalized * currentSpeed * Time.deltaTime);
                velocity.y = 0f; // boost-powered rush: don't sink out of reach while homing

                elapsed += Time.deltaTime;
                yield return null;
            }
            if (animator != null) animator.speed = 1f;
        }

        // Chain the rest of the string as long as hits keep landing (cycles Melee1..Melee4)
        for (int hit = 2; hit <= TotalHits; hit++)
        {
            yield return new WaitForSeconds(0.5f);

            if (!hasHitConnected || currentState != MechState.Attacking) { comboChainDone = true; yield break; }
            if (playerTarget == null || Vector3.Distance(transform.position, playerTarget.position) >= meleeHitRange + 2f) { comboChainDone = true; yield break; }
            if (playerHealth != null && playerHealth.isYellowLocked) { comboChainDone = true; yield break; }

            hasHitConnected = false;
            currentComboHit = hit;
            DisableAllHitboxes(); // clear any fist left on by an early-cancelled clip
            if (animator != null) animator.CrossFadeInFixedTime("punch" + AnimIndexForHit(hit), 0.08f, 0);
            StartCoroutine(MicroLunge());
        }

        comboChainDone = true; // full string queued; the final clip's EndAttack ends the state
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

    // Guarded with the Attacking state: after a cancel, the interrupted melee clip keeps
    // playing and would otherwise re-enable hitboxes via its animation events.
    public void EnableRightFist() { if (currentState == MechState.Attacking && rightFistCollider != null) rightFistCollider.enabled = true; }
    public void DisableRightFist() { if (rightFistCollider != null) rightFistCollider.enabled = false; }
    public void EnableLeftFist() { if (currentState == MechState.Attacking && leftFistCollider != null) leftFistCollider.enabled = true; }
    public void DisableLeftFist() { if (leftFistCollider != null) leftFistCollider.enabled = false; }
    public void EnableLeftFoot() { if (currentState == MechState.Attacking && leftFootCollider != null) leftFootCollider.enabled = true; }
    public void DisableLeftFoot() { if (leftFootCollider != null) leftFootCollider.enabled = false; }

    private void DisableAllHitboxes() { DisableRightFist(); DisableLeftFist(); DisableLeftFoot(); }

    // Animation event: only valid while actually attacking, so a stray event from an
    // interrupted clip can no longer cut a stagger or knockdown short. Also ignored
    // while the combo coroutine is still queuing swings — otherwise the EndAttack
    // event at the end of EVERY punch clip would end the string after one hit.
    public void EndAttack()
    {
        if (currentState != MechState.Attacking) return;
        if (!comboChainDone && currentComboHit < TotalHits) return;
        FinishAttackState();
    }

    private void FinishAttackState()
    {
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
        lastAttackTime = Time.time;
        currentComboHit = 0;
        comboChainDone = true;
        DisableAllHitboxes();
        animator.ResetTrigger("Melee1");
        animator.ResetTrigger("Melee2");
        animator.ResetTrigger("Melee3");
        animator.ResetTrigger("Melee4");
    }

    public void CancelAttack()
    {
        StopAllCoroutines();
        // StopAllCoroutines kills ShieldFor / HitStop coroutines mid-flight -
        // clean their state up here so nothing sticks
        isShielding = false;
        if (shieldVisual != null) shieldVisual.SetActive(false);
        inHitStop = false;
        if (animator != null) animator.speed = 1f;
        FinishAttackState();
    }

    // Called by the game flow when the real fight starts
    public void RestockForBattle()
    {
        aiAmmo = aiMaxAmmo;
        aiEmptySince = -1f;
    }

    // Tutorial: force one real melee string even while passiveMode is on
    public void TutorialAttackNow()
    {
        if (currentState != MechState.Grounded && currentState != MechState.Airborne) return;
        StartCoroutine(PerformComboSequence());
    }

    public void SwitchToPunch1Camera() { }
    public void SwitchToPunch4Camera() { }
    public void SwitchToNormalCamera() { }

    public void TakeHit(float staggerDuration = 0.8f, float damage = 0)
    {
        CancelAttack();
        currentMomentum = Vector3.zero;
        velocity.x = 0;
        velocity.z = 0;

        currentState = MechState.Staggered;

        if (animator != null) animator.SetTrigger("GetHit");

        StartCoroutine(StaggerRecovery(staggerDuration));
    }

    private IEnumerator StaggerRecovery(float duration)
    {
        yield return new WaitForSeconds(duration);
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
        // Restart the attack cooldown from the moment of RECOVERY — otherwise the
        // cooldown expired during the stagger and the AI counter-attacked the instant
        // your combo's end lag finished.
        lastAttackTime = Time.time;
    }

    private bool inHitStop = false;
    public void StartHitStop(float duration)
    {
        // Re-entry guard: two hits landing within one hit-stop window used to nest the
        // coroutines — the second captured speed 0 as "original" and the animator froze
        // at speed 0 PERMANENTLY (mech stuck in a static pose). Restore to 1 explicitly.
        if (inHitStop) return;
        StartCoroutine(HitStopCoroutine(duration));
    }
    private IEnumerator HitStopCoroutine(float duration)
    {
        inHitStop = true;
        animator.speed = 0f;
        yield return new WaitForSecondsRealtime(duration);
        animator.speed = 1f;
        inHitStop = false;
    }
}
