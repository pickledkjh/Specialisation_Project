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
    [Tooltip("Movement multiplier for the 3x arena - matches the player's bigMapSpeedScale.")]
    public float bigMapSpeedScale = 1.3f;
    [Tooltip("Extra multiplier on the AI's dash speed - keeps it competitive with the player's doubled dashes without matching them outright.")]
    public float dashSpeedScale = 1.5f;
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
    public int comboLoopCount = 1; // renamed from comboLoops: matches the player's tight 4-hit string
    [Tooltip("The finisher fling - matches the player's big launch. (Renamed from finisherLaunchForward/Up so these beat the stale scene values.)")]
    public float finisherFlingForward = 34f;
    public float finisherFlingUp = 11f;
    [Tooltip("Bar power of the AI's final hit - fills whatever the string left over. (Renamed from finisherKnockdownPower so the stronger default applies.)")]
    public float finisherBarBonus = 6f; // renamed from finisherBarAdd: AI full combo also lands under half bar

    [Header("Hitboxes")]
    public Collider rightFistCollider;
    public Collider leftFistCollider;
    public Collider leftFootCollider;

    [Header("AI Brain - ranged")]
    [Tooltip("The AI shoots inside this range (shots home inside 40, matching red lock).")]
    public float shootRange = 45f;
    [Tooltip("Gap between AI shots. The player fires every 0.6s - the AI is deliberately much slower so its fire reads as dodgeable pressure, not a turret. (Renamed from aiShotCooldown so this default beats the old baked value.)")]
    public float aiShotGapSeconds = 2.2f;
    [Tooltip("The AI 'aims' for this long before its shot actually fires - the reaction window the playtest asked for. Electricity crackles at its gun during the windup.")]
    public float shotWindupSeconds = 0.55f;
    [Tooltip("Speed of the AI's NORMAL shots. Slower than the player's 60 so incoming cyan beams are visibly dodgeable.")]
    public float aiShotSpeed = 46f;
    [Tooltip("Homing turn rate of the AI's normal shots, deg/sec. Player shots turn at 120 - the AI's curve is gentler on purpose, and a boost step breaks it entirely.")]
    public float aiShotTurnRate = 70f;
    public int aiMaxAmmo = 8;
    public float aiReloadTime = 5f;
    [Tooltip("Chance the AI spends 2 ammo on a heavy charge shot when it catches you landing or getting up.")]
    public float chargeShotChance = 0.3f;

    [Header("AI Brain - specials (E laser / R missiles / F tackle)")]
    [Tooltip("Master switch for the AI's special moves. It used to own SpecialMoves but never fired anything, so the player only ever saw its rifle and fists.")]
    public bool useSpecials = true;
    [Tooltip("Seconds between special-move ATTEMPTS. The moves have their own cooldowns on top; this stops the AI chaining them back to back the instant they come up.")]
    public float specialAttemptGap = 5f;
    [Tooltip("Chance a given attempt actually commits. Below 1 the AI stays unpredictable instead of firing on a metronome.")]
    [Range(0f, 1f)] public float specialCommitChance = 0.7f;
    [Tooltip("The AI only fires the E laser when you are further away than this - point blank it would just eat a melee punish.")]
    public float laserMinRange = 22f;
    [Tooltip("Missile barrage range band: the AI wants you at mid range where the missiles have room to curve in.")]
    public float missileMinRange = 14f;
    public float missileMaxRange = 60f;
    [Tooltip("Dash tackle range: close enough to connect out of a dash, far enough that a boost dash is worth it.")]
    public float tackleMinRange = 8f;
    public float tackleMaxRange = 26f;
    private float nextSpecialAttempt = -99f;
    private float tackleReadyAt = -99f, aiLaserReadyAt = -99f, aiMissileReadyAt = -99f;
    [Tooltip("The AI's own cooldown on the dash tackle, mirroring the player's 7s.")]
    public float aiTackleCooldown = 8f;
    [Tooltip("Cooldown on the AI's gerobi beam. Longer than the player's 14s - it is the AI's scariest move.")]
    public float aiLaserCooldown = 18f;
    [Tooltip("Cooldown on the AI's missile barrage.")]
    public float aiMissileCooldown = 12f;
    [Tooltip("How many missiles the AI's barrage fires.")]
    public int aiMissileCount = 4;

    [Header("AI Brain - flight")]
    [Tooltip("Master switch for the AI using its boost to RISE. It never did: nothing in this script ever set a positive vertical velocity, so the enemy was permanently glued to the street while the player fought in three dimensions.")]
    public bool useVerticalBoost = true;
    [Tooltip("Rise speed, matching the player's ascend feel.")]
    public float aiAscendSpeed = 14f;
    [Tooltip("How fast vertical speed ramps toward the ascend speed.")]
    public float aiAscendRamp = 60f;
    [Tooltip("If the player is THIS much higher than the AI, it climbs to meet them. This is the main reason to fly - being shot at from above with no answer is what made the AI look stupid.")]
    public float riseIfPlayerAboveBy = 3.5f;
    [Tooltip("Seconds between rise attempts.")]
    public float riseCheckInterval = 1.2f;
    [Tooltip("Chance the AI takes to the air on its own in neutral, just to mix up its approach.")]
    [Range(0f, 1f)] public float randomRiseChance = 0.25f;
    [Tooltip("How long one climb lasts.")]
    public float riseDuration = 0.9f;
    [Tooltip("The AI will not start a climb below this much boost - it saves gauge for the landing, like a player.")]
    public float minBoostToRise = 35f;
    [Tooltip("Hard ceiling for the AI, so it can never climb out of the fight.")]
    public float maxRiseHeight = 40f;
    [Tooltip("Terminal fall speed for the AI, matching the player's floaty drop.")]
    public float aiMaxFallSpeed = 25f;
    private float riseUntil = -99f;
    private float nextRiseCheck = -99f;

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
    private bool shotWindupRunning = false;

    /// <summary>Same rule as the player: stepping breaks tracking on shots already in the air.</summary>
    public float LastEvadeTime { get; private set; } = -99f;
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

    private int TotalHits => Mathf.Max(1, comboLoopCount) * 4;
    private int AnimIndexForHit(int hit) => ((hit - 1) % 4) + 1;

    private void Start()
    {
        currentHealth = maxHealth;

        controller = GetComponent<CharacterController>();
        boostManager = GetComponent<BoostManager>();
        if (animator == null) animator = GetComponentInChildren<Animator>();

        myHealth = GetComponent<MechHealth>();
        SetTarget(playerTarget);

        aiAmmo = aiMaxAmmo;
        DisableAllHitboxes();
    }

    // ---------------- TARGET ACQUISITION ----------------
    // The AI used to have exactly one target: whatever Transform was dragged into
    // playerTarget in the Inspector. That is correct for 1v1 and wrong the moment
    // there are two mechs on the other side, so the target is now chosen from the
    // roster by team and re-checked periodically.
    [Header("Targeting")]
    [Tooltip("How often the AI re-considers which opponent to fight.")]
    public float retargetInterval = 1.6f;
    [Tooltip("A rival has to be this much closer than the current target before the " +
             "AI will switch. Without it the AI dithers between two enemies at similar range.")]
    public float retargetStickiness = 8f;
    private float nextRetargetAt = -99f;

    /// <summary>Point this AI at a new opponent and refresh every cached reference to it.</summary>
    public void SetTarget(Transform t)
    {
        playerTarget = t;
        playerHealth = null;
        playerMech = null;
        if (t == null) return;
        playerHealth = t.GetComponentInParent<MechHealth>();
        if (playerHealth == null) playerHealth = t.GetComponentInChildren<MechHealth>();
        playerMech = t.GetComponentInParent<MechController>();
        if (playerMech == null) playerMech = t.GetComponentInChildren<MechController>();
    }

    private void RetargetTick()
    {
        bool needNow = playerTarget == null || !playerTarget.gameObject.activeInHierarchy ||
                       playerHealth == null || playerHealth.currentHealth <= 0f;
        if (!needNow && Time.time < nextRetargetAt) return;
        nextRetargetAt = Time.time + Mathf.Max(0.25f, retargetInterval);

        Team myTeam = myHealth != null ? myHealth.team : Team.Team2;
        MechHealth pick = BattleRoster.NearestOpponent(transform.position, myTeam);

        // No opponent by team? Then the scene has not been team-assigned (the old
        // both-mechs-on-Team2 setup) - keep whatever the Inspector gave us.
        if (pick == null) return;

        if (!needNow && playerHealth != null)
        {
            float dCur = Vector3.Distance(transform.position, playerHealth.transform.position);
            float dNew = Vector3.Distance(transform.position, pick.transform.position);
            if (dNew > dCur - retargetStickiness) return;
        }

        if (pick.transform != playerTarget) SetTarget(pick.transform);
    }

    private void Update()
    {
        RetargetTick();
        if (playerTarget == null) return;

        // DOWNED: MechHealth owns this body - it is flying through the air or lying
        // on the floor, and every controller.Move() below would drag it back. The
        // down state disables this component, but anything that re-enables it
        // (scene re-hook, difficulty apply, tutorial setup) would otherwise resume
        // driving a knocked-down mech mid-flight. Belt and braces.
        //
        // IsDownLocked, NOT isYellowLocked: yellow lock stays on through the
        // wake-up protection window, and blocking there deadlocked the AI - it
        // could not act, and acting is what ends the protection.
        if (myHealth != null && myHealth.IsDownLocked) return;

        // Paused / burst cut-in / finisher freeze-frame - same guard the player's
        // controller has. Without it the AI kept walking during the freeze.
        if (Time.timeScale < 0.5f) return;

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
        // ---- GRAVITY WHILE AIRBORNE ----
        // THIS WAS MISSING, and it is why the enemy flew off and never came back.
        // Gravity was only ever applied in the Attacking/Landing/Staggered states,
        // which was harmless while the AI could not leave the ground - it never had
        // a rise. The moment it could climb, its upward velocity had nothing acting
        // against it and the mech sailed away forever. Anything airborne and not
        // actively boosting now falls, exactly like the player.
        else if (!isGrounded && currentState != MechState.BoostDash && riseUntil <= Time.time)
        {
            velocity.y += gravity * Time.deltaTime;
            if (velocity.y < -aiMaxFallSpeed) velocity.y = -aiMaxFallSpeed;
        }

        // ---- CLIMB (the player's SPACE, for the AI) ----
        // Mirrors MechController: while the climb is active, vertical speed ramps up
        // toward ascendSpeed and boost drains, exactly as it does when the player
        // holds the boost button.
        if (riseUntil > Time.time &&
            currentState != MechState.Landing && currentState != MechState.Staggered)
        {
            // The gauge is the hard limit, same as the player's: no boost, no flight.
            bool gaugeLeft = boostManager != null && !boostManager.isOverheated &&
                             boostManager.CanBoost(boostManager.dashDepletionRate * Time.deltaTime);
            if (gaugeLeft && transform.position.y < maxRiseHeight)
            {
                velocity.y = Mathf.MoveTowards(velocity.y, aiAscendSpeed, aiAscendRamp * Time.deltaTime);
                boostManager.ConsumeBoostOverTime(boostManager.dashDepletionRate);
                if (currentState == MechState.Grounded) currentState = MechState.Airborne;
            }
            else
            {
                riseUntil = -99f;            // out of gauge or at the ceiling
                if (velocity.y > 0f) velocity.y = 0f; // cut the climb dead so it drops immediately
            }
        }

        // ALTITUDE LEASH. Belt and braces on top of gravity: if the enemy is somehow
        // still high up with no climb running, force it down. A boss that parks in
        // the sky is unfightable, and there is no situation where the AI should be
        // above the ceiling for more than a moment.
        if (!isGrounded && riseUntil <= Time.time && transform.position.y > maxRiseHeight)
            velocity.y = Mathf.Min(velocity.y, -8f);

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

    private int aiStringHitsLanded = 0;

    public void RegisterHit()
    {
        hasHitConnected = true;
        aiStringHitsLanded++;
        // No forced knockdown — the down comes from the ONE bar system inside
        // MechHealth.TakeDamage. MeleeHitbox passes this AI's finisher bar power and
        // big launch on the final hit (see IsFinisherHit / GetCurrentKnockdownPower).
    }

    // Queried by MeleeHitbox, mirroring MechCombat's API.
    public bool IsFinisherHit => currentComboHit >= TotalHits;
    public float GetCurrentKnockdownPower(float normalHitPower)
    {
        return IsFinisherHit ? finisherBarBonus : normalHitPower;
    }

    // Any solid surface counts as ground (rooftops, cars, props) - matches the
    // player's fix so the AI's landings/boost logic work on top of buildings too.
    private static readonly Collider[] aiGroundBuf = new Collider[8];
    public bool CheckIfGrounded()
    {
        if (groundCheckPoint == null) return controller.isGrounded;
        int n = Physics.OverlapSphereNonAlloc(groundCheckPoint.position, groundCheckRadius, aiGroundBuf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            Collider c = aiGroundBuf[i];
            if (c != null && c.transform.root != transform.root) return true;
        }
        return false;
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

        // 1.4 TAKE TO THE AIR. Checked before anything else commits the AI to a
        // ground action. Two reasons to climb: the player is ABOVE us (the common
        // case - they boost up and rain shots down, and an AI stuck on the floor
        // has no answer), or a coin flip in neutral so its approach is not always
        // a predictable ground walk.
        if (useVerticalBoost && Time.time >= nextRiseCheck && riseUntil < Time.time)
        {
            nextRiseCheck = Time.time + riseCheckInterval;
            float heightGap = playerTarget.position.y - transform.position.y;
            bool playerAbove = heightGap > riseIfPlayerAboveBy;
            bool gaugeOk = boostManager != null && !boostManager.isOverheated &&
                           boostManager.currentBoost >= minBoostToRise;

            if (gaugeOk && transform.position.y < maxRiseHeight &&
                (playerAbove || Random.value < randomRiseChance))
            {
                // Chase altitude properly when they are high up, short hop otherwise
                riseUntil = Time.time + (playerAbove ? riseDuration * Mathf.Clamp(heightGap / 6f, 1f, 2.5f)
                                                     : riseDuration);
            }
        }

        // 1.5 SPECIALS: the E laser, the R missile barrage and the F dash tackle.
        // Slotted ahead of the plain shoot/melee logic so they actually get a look
        // in - underneath, everything below would always fire first and the AI's
        // whole special kit sat unused for the entire match.
        if (TryUseSpecial(distance)) return;

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

    // ---------------------------------------------------------------- specials
    // The AI carries a SpecialMoves component (MechCombat self-installs one on
    // anything with a controller) but nothing ever ASKED it to fire, so the E
    // laser and R barrage never appeared in a match and the dash tackle was a
    // player-only move. This picks a special that suits the current range, honours
    // each move's real cooldown, and commits only sometimes so it stays readable.
    private bool TryUseSpecial(float distance)
    {
        if (!useSpecials) return false;
        if (Time.time < nextSpecialAttempt) return false;
        if (currentState == MechState.Attacking || isShielding || shotWindupRunning) return false;
        if (playerHealth != null && playerHealth.isYellowLocked) return false; // no point vs a downed target

        nextSpecialAttempt = Time.time + specialAttemptGap;
        if (Random.value > specialCommitChance) return false;

        // NOTE: the AI cannot use the player's SpecialMoves component - that class
        // is built around MechController (state, velocity, momentum) and this mech
        // runs its own controller. These are self-contained AI versions that reuse
        // the same BlastSphere / HomingProjectile / missile-pack pieces, so they
        // look and hurt like the player's versions without a risky refactor.

        // --- E: GEROBI. Long range only - it commits the AI to a stationary
        // hover, so it wants you far enough away not to punish it instantly.
        if (Time.time >= aiLaserReadyAt &&
            distance >= laserMinRange && distance <= shootRange * 1.6f &&
            boostManager != null && !boostManager.isOverheated)
        {
            aiLaserReadyAt = Time.time + aiLaserCooldown;
            StartCoroutine(AiGerobiRoutine());
            return true;
        }

        // --- R: MISSILE BARRAGE. Mid-range: room for the missiles to curve in.
        if (Time.time >= aiMissileReadyAt &&
            distance >= missileMinRange && distance <= missileMaxRange)
        {
            aiMissileReadyAt = Time.time + aiMissileCooldown;
            StartCoroutine(AiMissileBarrage());
            return true;
        }

        // --- F: DASH TACKLE. A committed closer from just outside melee.
        if (Time.time >= tackleReadyAt &&
            distance >= tackleMinRange && distance <= tackleMaxRange &&
            boostManager != null && boostManager.CanBoost(minBoostToDash))
        {
            tackleReadyAt = Time.time + aiTackleCooldown;
            StartCoroutine(AiDashTackle());
            return true;
        }

        return false;
    }

    // The beam visual is a plain scene object with no parent and no owner. If the
    // coroutine that made it is stopped before its cleanup line runs - a knockdown,
    // a CancelAttack, a stage reset, this whole mech being destroyed at the end of a
    // 2v2 - the cylinder is orphaned in the arena forever. Tracked here so every one
    // of those paths can kill it.
    private GameObject activeBeam;

    private void KillActiveBeam()
    {
        if (activeBeam != null) Destroy(activeBeam);
        activeBeam = null;
    }

    private void OnDisable() { KillActiveBeam(); }
    private void OnDestroy() { KillActiveBeam(); }

    // --- E equivalent: charge, then a fat beam that detonates into a blast sphere.
    private System.Collections.IEnumerator AiGerobiRoutine()
    {
        currentState = MechState.Attacking;       // locks out the rest of the brain
        BattleAudio.Play("alert", 0.5f, 0.7f);    // the tell - the player gets time to move

        // Windup: face the player, crackle at the muzzle
        float windup = 0.85f;
        for (float t = 0f; t < windup; t += Time.deltaTime)
        {
            SmoothFaceTarget();
            if (t % 0.2f < Time.deltaTime)
                ProceduralVfx.Sparks(transform.position + Vector3.up * 1.6f + transform.forward * 1.2f,
                                     new Color(1f, 0.55f, 0.4f), 8);
            yield return null;
        }

        if (playerTarget == null) { currentState = MechState.Grounded; yield break; }

        Vector3 origin = transform.position + Vector3.up * 1.6f + transform.forward * 1.2f;
        Vector3 dir = (playerTarget.position + Vector3.up * 1.2f - origin).normalized;

        // Where does the beam stop?
        float range = 90f;
        RaycastHit hit;
        float len = Physics.Raycast(origin, dir, out hit, range, ~0, QueryTriggerInteraction.Ignore) &&
                    hit.transform.root != transform.root
                    ? hit.distance : range;

        // Beam visual - same thin-bright treatment as the bolts
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(beam.GetComponent<Collider>());
        Renderer br = beam.GetComponent<Renderer>();
        br.material = new Material(Shader.Find("Sprites/Default"));
        br.material.color = new Color(1f, 0.55f, 0.45f, 0.9f); // enemy red beam
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beam.transform.position = origin + dir * (len * 0.5f);
        beam.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        beam.transform.localScale = new Vector3(0.55f, len * 0.5f, 0.55f);

        // Belt and braces. Destroy-with-delay is scheduled on the BEAM, not on this
        // coroutine, so it still fires if the routine is killed mid-flight. The
        // explicit Destroy below is the normal path; this is the one that cannot be
        // interrupted.
        activeBeam = beam;
        Destroy(beam, 1.5f);

        BattleAudio.Play("explosion", 0.6f, 0.7f);
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.22f, 0.4f);

        // The blast sphere is what actually hurts - same weapon the player gets,
        // but at half the tick damage. The player's gerobi was tripled to make
        // landing one decisive; handing the AI that number outright would delete
        // the player from across the map for one mistake.
        BlastSphere aiBlast = BlastSphere.Spawn(origin + dir * len, transform, 7.5f);
        if (aiBlast != null) aiBlast.blastDamageTick = 6f;

        // Unscaled: a finisher freeze or a kill slow-mo would otherwise stretch this
        // 0.7s into many seconds of beam hanging in the air.
        yield return new WaitForSecondsRealtime(0.7f);
        if (beam != null) Destroy(beam);
        if (activeBeam == beam) activeBeam = null;

        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    // --- R equivalent: a salvo of homing missiles from over the shoulders.
    private System.Collections.IEnumerator AiMissileBarrage()
    {
        BattleAudio.Play("alert", 0.4f, 1.1f);

        for (int i = 0; i < aiMissileCount; i++)
        {
            if (playerTarget == null) yield break;

            float side = (i % 2 == 0) ? 1f : -1f;
            Vector3 from = transform.position
                         + transform.right * side * 1.2f
                         + Vector3.up * (2.1f + 0.35f * (i / 2));
            Vector3 aim = (playerTarget.position + Vector3.up * 1.3f - from).normalized;

            HomingProjectile m = HomingProjectile.SpawnSimple(
                from, Quaternion.LookRotation(aim), 1.1f, new Color(1f, 0.5f, 0.35f));

            if (MissileAssets.MissileModel != null)
            {
                foreach (Renderer rr in m.GetComponentsInChildren<Renderer>(true))
                    if (rr is MeshRenderer) rr.enabled = false;
                TrailRenderer bt = m.GetComponent<TrailRenderer>();
                if (bt != null) bt.emitting = false;
                MissileAssets.DressAsMissile(m.gameObject, 1.4f);
                m.isMissile = true;
                m.missileFxScale = 1.1f;
            }

            m.damage = 8f;
            m.knockdownPower = 16f;
            m.speed = 46f;
            m.turnRateDegPerSec = 150f;   // gentler than the player's 220 - still steppable
            m.homingDuration = 0.9f;
            m.Init(playerTarget, transform);

            yield return new WaitForSeconds(0.18f);
        }
    }

    // Shoulder charge: dash straight at the player with the fists live, exactly
    // like the player's F tackle. Uses the existing combo plumbing for the hit.
    private System.Collections.IEnumerator AiDashTackle()
    {
        currentState = MechState.Attacking;
        if (animator != null) animator.CrossFadeInFixedTime("punch1", 0.06f, 0);

        if (rightFistCollider != null) rightFistCollider.enabled = true;
        if (leftFistCollider != null) leftFistCollider.enabled = true;

        float t = 0f;
        const float duration = 0.45f;
        float speed = 26f * bigMapSpeedScale;
        while (t < duration)
        {
            t += Time.deltaTime;
            if (playerTarget != null)
            {
                Vector3 dir = playerTarget.position - transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    dir.Normalize();
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 12f);
                    controller.Move(dir * speed * Time.deltaTime);
                }
            }
            yield return null;
        }

        if (rightFistCollider != null) rightFistCollider.enabled = false;
        if (leftFistCollider != null) leftFistCollider.enabled = false;
        currentState = CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
    }

    // The AI's own rifle - runtime projectiles, same bar-feeding rules as the player's.
    // Firing is now a two-step: WINDUP (visible electricity at the gun, the player's
    // cue to step) then the actual shot. Combined with step-breaks-tracking and the
    // gentler homing numbers, the old "aimbot you cannot react to" is gone.
    private void TryShoot(float distance, bool allowCharge)
    {
        if (shotWindupRunning) return;
        if (Time.time - lastAiShotTime < aiShotGapSeconds) return;
        if (aiAmmo <= 0) return;
        if (distance > shootRange) return;

        bool charge = allowCharge && aiAmmo >= 2 && Random.value < chargeShotChance;
        StartCoroutine(ShotWindupRoutine(charge));
    }

    private IEnumerator ShotWindupRoutine(bool charge)
    {
        shotWindupRunning = true;
        lastAiShotTime = Time.time; // the gap between shots includes the windup

        // Telegraph: the parry electricity doubles as a charge-up crackle on the AI.
        // Getting staggered or downed during the windup cancels the shot entirely
        // (TakeHit -> CancelAttack -> StopAllCoroutines kills this routine; the
        // flag is reset there).
        CombatVfx.SpawnParry(transform);
        float t = 0f;
        while (t < shotWindupSeconds)
        {
            if (myHealth != null && myHealth.isYellowLocked) { shotWindupRunning = false; yield break; }
            t += Time.deltaTime;
            yield return null;
        }
        shotWindupRunning = false;

        if (aiAmmo <= 0 || playerTarget == null) yield break;
        if (charge && aiAmmo < 2) charge = false;

        if (myHealth != null) myHealth.BreakWakeUpProtection(); // shooting forfeits wake-up protection too

        aiAmmo -= charge ? 2 : 1;
        if (aiAmmo <= 0) aiEmptySince = Time.time;

        Vector3 spawnPos = transform.position + transform.forward * 1.2f + Vector3.up * 1.5f;
        Vector3 aimPoint = playerTarget.position + Vector3.up * 1.5f;
        Vector3 aimDir = aimPoint - spawnPos;
        Quaternion rot = aimDir.sqrMagnitude > 0.001f ? Quaternion.LookRotation(aimDir.normalized) : transform.rotation;

        CombatVfx.SpawnMuzzleFlash(transform, spawnPos);
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
        else
        {
            shot.speed = aiShotSpeed;
            shot.turnRateDegPerSec = aiShotTurnRate;
        }

        float distNow = Vector3.Distance(transform.position, playerTarget.position);
        bool redLock = distNow <= 40f && (playerHealth == null || !playerHealth.isYellowLocked);
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
        controller.Move(move * walkSpeed * bigMapSpeedScale * Time.deltaTime);

        // Drive the strafe blend tree (mirrors the player's face-the-enemy movement)
        animator.SetFloat("InputX", strafeDir, 0.1f, Time.deltaTime);
        animator.SetFloat("InputY", radial, 0.1f, Time.deltaTime);
    }

    private void ChasePlayer()
    {
        SmoothFaceTarget();
        Vector3 moveDir = transform.forward * walkSpeed * bigMapSpeedScale;
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
        currentMomentum = moveDir.normalized * dashSpeed * bigMapSpeedScale * dashSpeedScale;
        controller.Move(currentMomentum * Time.deltaTime);
    }

    private IEnumerator ExecuteBoostStep(Vector2 dir)
    {
        if (myHealth != null) myHealth.BreakWakeUpProtection(); // stepping forfeits protection

        LastEvadeTime = Time.time; // AI steps break player shot tracking too - same rule both ways
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
            currentStepVelocity = stepVec.normalized * Mathf.Lerp(stepSpeed, walkSpeed, elapsed / stepDuration); // matches the player's un-scaled step
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

    // The melee hitbox reads this to stay live for the whole attack - hit detection
    // no longer depends on the Enable/Disable animation events arriving.
    public bool IsSwinging => currentState == MechState.Attacking;

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
        TrySoftDownPlayer(); // EXVS partial-combo down (no-op if the bar already downed them)
        FinishAttackState();
    }

    // Mirror of the player's partial-combo down: an AI string that landed 2+ hits
    // and then ENDED still floors the player briefly. A cancelled (staggered) AI
    // string does not - CancelAttack skips this and FinishAttackState just resets.
    private void TrySoftDownPlayer()
    {
        int hits = aiStringHitsLanded;
        aiStringHitsLanded = 0;
        if (hits < 2) return;
        MechController pc = Object.FindFirstObjectByType<MechController>();
        if (pc == null) return;
        MechHealth ph = pc.GetComponent<MechHealth>();
        if (ph == null || ph.isYellowLocked || ph.currentHealth <= 0f) return;
        Vector3 dir = ph.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        ph.TriggerSoftKnockdown(dir * 6f + Vector3.up * 4f);
    }

    private void FinishAttackState()
    {
        aiStringHitsLanded = 0; // cancelled strings reset without the soft down
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
        KillActiveBeam();   // StopAllCoroutines would otherwise strand the gerobi beam
        // StopAllCoroutines kills ShieldFor / HitStop coroutines mid-flight -
        // clean their state up here so nothing sticks
        isShielding = false;
        if (shieldVisual != null) shieldVisual.SetActive(false);
        inHitStop = false;
        shotWindupRunning = false; // a killed windup coroutine must not leave the flag stuck
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
