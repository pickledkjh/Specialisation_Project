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
    [Tooltip("The Melee1-4 string repeats this many times before the launcher. 1 = tight 4-hit EXVS string: three normal hits build 54 bar, the finisher's 60 fills it EXACTLY at the last hit - so the big burning fling always happens on the finisher, and step-cancelling earlier is a real choice. (Renamed from comboLoops to beat the stale scene 2.)")]
    public int comboLoopCount = 1;

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
    [Tooltip("Bar per normal melee hit. LOW on purpose: a full 4-hit combo adds ~50 bar (half of 100) - one combo can never bar-down the enemy mid-string (that was the invisible mid-string down that ate the finisher fling). The finisher's down is FORCED in code, not bar-driven. (Renamed from meleeBarPowerPerHit.)")]
    public float meleeBarPerHit = 12f;
    [Tooltip("Bar the finisher ADDS (its knockdown is forced in code regardless). Small: full combo = ~half the bar. (Renamed from finisherBarPower to beat the stale scene 60.)")]
    public float finisherBarBonus = 6f; // renamed from finisherBarAdd: full combo = 3x12 + 6 = 42 bar, safely under half

    [Header("Finisher Launch")]
    [Tooltip("The finisher fling. BIG on purpose - the last hit sends them flying across the arena, burning, through buildings. (Renamed from finisherLaunchForward/Up so these beat the stale scene 16/8.)")]
    public float finisherFlingForward = 34f;
    public float finisherFlingUp = 11f;

    [Header("Tracking Speeds")]
    public float redLockTurnSpeed = 30f;

    [Header("Charge Shot (special move)")]
    [Tooltip("Hold the shoot button this long, then release, to fire the charge shot. The tap shot still fires instantly on press.")]
    public float chargeShotHoldTime = 1f;
    private float shootHeldSince = -1f;

    /// <summary>0..1 charge progress, for the HUD's charge bar. 0 when not holding.</summary>
    public float ChargeProgress01
    {
        get
        {
            if (shootHeldSince <= 0f) return 0f;
            return Mathf.Clamp01((Time.time - shootHeldSince) / Mathf.Max(0.01f, chargeShotHoldTime));
        }
    }
    /// <summary>True the instant releasing the button would fire the charge shot.</summary>
    public bool ChargeReady { get { return ChargeProgress01 >= 1f; } }

    [Header("Shield (hold Q)")]
    [Tooltip("Boost drained per second while guarding - the shield cannot be held forever, and cannot be raised while overheated (EXVS guard rules).")]
    public float shieldBoostDrainPerSec = 8f;
    [Tooltip("Only attacks from inside the frontal arc are blocked. 0.2 = roughly the front 155 degrees; raise toward 1 for a narrower shield.")]
    public float shieldFrontDot = 0.2f;
    [Tooltip("Console logging for shield raise/refuse. Leave ON until blocking is confirmed working.")]
    public bool shieldDebugLogs = false; // renamed from logShieldDebug: new FALSE default beats the stale scene value - clean submission console
    [Tooltip("Seconds after DROPPING the shield before it can be raised again. Without this, tapping Q on reaction beat every melee opener for free.")]
    public float shieldCooldownSeconds = 5f;
    private float shieldReadyAt = -99f;
    /// <summary>1 = shield ready. For the HUD cooldown bar.</summary>
    public float ShieldReady01 => Mathf.Clamp01(1f - (shieldReadyAt - Time.time) / Mathf.Max(0.01f, shieldCooldownSeconds));
    private InputAction shieldAction;
    private InputAction tackleAction;
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
    private int TotalHits => Mathf.Max(1, comboLoopCount) * 4;
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

        // Tackle moved OFF the mouse button: dash + left click kept firing the
        // tackle when the player just wanted a chase melee. Now it is a deliberate
        // press of F during a boost dash. (Interactables moved to G to make room.)
        tackleAction = new InputAction("Tackle", InputActionType.Button);
        tackleAction.AddBinding("<Keyboard>/f");

        boostManager = GetComponent<BoostManager>();
    }

    /// <summary>True while any part of a melee action owns the mech (for SpecialMoves).</summary>
    public bool IsBusyAttacking => isAttacking || isLunging || isInEndLag;

    // The melee hitbox reads this because the Enable/Disable animation events
    // silently stopped arriving on the retargeted Gundam clips. It opens the hit
    // window ONLY during an actual swing: not while rushing in (isLunging), not in
    // end lag, not frozen in hit-stop, and only after a short windup beat so the
    // hit lands when the blade visually swings - without this gate the fist was
    // live for the whole attack and melee machine-gunned ("too op").
    public bool IsSwinging => isAttacking && !isLunging && !isInEndLag && !isFrozen
                              && currentMeleeStep > 0
                              && Time.time - lastStepStartTime >= 0.08f; // opens fast: at buffed enemy speeds, 0.15s was enough to strafe out of reach

    /// <summary>True during the LAST swing of the string. MeleeHitbox extends its
    /// reach on this swing - the punch4 clip's contact point drifts, and a whiffed
    /// finisher (no fling, no knockdown) feels like a robbery.</summary>
    public bool IsFinisherSwing => IsSwinging && IsFinalStep(currentMeleeStep);

    [Header("Melee animation set")]
    [Tooltip("Animator state-name prefix for the melee string: 'punch' plays the original fist clips (punch1..punch4), 'slash' plays the beam-saber clips built by Tools > Gundam > 9. If the named states are missing this falls back to punch automatically. RENAMED FIELD so this punch default beats the 'slash' value already baked into the scene.")]
    public string meleeClipSet = "punch";
    private string MeleeState(int index) { return meleeClipSet + index; }
    private bool IsInMeleeState(AnimatorStateInfo st)
    {
        return st.IsName(MeleeState(1)) || st.IsName(MeleeState(2)) || st.IsName(MeleeState(3)) || st.IsName(MeleeState(4))
            || st.IsName("punch1") || st.IsName("punch2") || st.IsName("punch3") || st.IsName("punch4")
            || st.IsName("Melee1") || st.IsName("Melee2") || st.IsName("Melee3") || st.IsName("Melee4");
    }

    [Tooltip("Animator layer the melee clips play on. 0 = full-body layer 0, where the punch states live (the default, and what the original melee used). 1 = the masked UPPER-BODY layer built by Tools > Gundam > 9c, which keeps the legs on locomotion - only useful with the slash clips. Falls back to 0 automatically if the layer does not exist. RENAMED FIELD so this 0 default beats the 1 baked into the scene.")]
    public int meleeLayerIndex = 0;

    [Tooltip("State the melee layer returns to when the string ends. Must be an EMPTY state on that layer, so the mask stops contributing.")]
    public string meleeIdleState = "Empty";

    [Tooltip("How fast the masked melee layer fades in and out. High = crisp, EXVS-style.")]
    public float meleeLayerFadeSpeed = 14f;

    [Header("Saber grip")]
    [Tooltip("Degrees the blade is rotated OFF the forearm axis. 0 = it continues straight out of the arm like a lance (the 'pointing down and forward' look). 90 = gripped in the fist standing straight UP, which is how a beam saber is held. Try 75-105 to taste.")]
    public float saberGripPitch = 90f;
    [Tooltip("Twist around the blade's own length. Only changes the plane the trail sweeps through - leave at 0 unless the arc reads flat.")]
    public float saberGripRoll = 0f;

    // If the slash states were never built in the controller, CrossFade would
    // silently do nothing and melee would animate not at all. Verify once, and
    // fall back to the punch set rather than shipping a mech that swings air.
    private void VerifyMeleeAnimSet()
    {
        if (animator == null || string.IsNullOrEmpty(meleeClipSet)) return;

        // Layer check first: the masked layer is optional (tool 9c builds it).
        if (meleeLayerIndex >= animator.layerCount)
        {
            Debug.LogWarning("[Melee] Animator has no layer " + meleeLayerIndex +
                             " - melee falls back to the full-body layer 0. Run Tools > Gundam > " +
                             "9c. Move SLASH To Upper-Body Layer to get clean legs.");
            meleeLayerIndex = 0;
        }

        for (int i = 1; i <= 4; i++)
        {
            if (!animator.HasState(meleeLayerIndex, Animator.StringToHash(MeleeState(i))))
            {
                // Maybe the states only exist on the base layer
                if (meleeLayerIndex != 0 && animator.HasState(0, Animator.StringToHash(MeleeState(i))))
                {
                    Debug.LogWarning("[Melee] '" + MeleeState(i) + "' is missing from layer " + meleeLayerIndex +
                                     " but present on layer 0 - dropping back to full-body melee.");
                    meleeLayerIndex = 0;
                    continue;
                }
                Debug.LogWarning("[Melee] Animator state '" + MeleeState(i) + "' not found - " +
                                 "falling back to the punch clips. Run Tools > Gundam > 9. Build SLASH 1-4 States.");
                meleeClipSet = "punch";
                meleeLayerIndex = 0;
                return;
            }
        }
    }

    /// <summary>Send the melee animation back to rest. On the masked upper-body layer
    /// that means its Empty state (the mask then contributes nothing); on the old
    /// full-body layer 0 it means Locomotion, exactly as before.</summary>
    private void ReturnMeleeLayerToIdle()
    {
        if (animator == null) return;
        if (meleeLayerIndex > 0 && meleeLayerIndex < animator.layerCount &&
            animator.HasState(meleeLayerIndex, Animator.StringToHash(meleeIdleState)))
            animator.CrossFadeInFixedTime(meleeIdleState, 0.12f, meleeLayerIndex);
        else
            animator.CrossFadeInFixedTime("Locomotion", 0.15f, 0);
    }

    // Drives the masked layer's weight: full while a melee action owns the mech,
    // faded out otherwise, so the mask never freezes the torso between strings.
    private void UpdateMeleeLayerWeight()
    {
        if (animator == null || meleeLayerIndex <= 0 || meleeLayerIndex >= animator.layerCount) return;
        float want = (isAttacking || isLunging || isInEndLag) ? 1f : 0f;
        float w = Mathf.MoveTowards(animator.GetLayerWeight(meleeLayerIndex), want, meleeLayerFadeSpeed * Time.deltaTime);
        animator.SetLayerWeight(meleeLayerIndex, w);
    }

    // WATCHDOG for "sometimes the motion just hangs there": if a melee state has
    // played past the end of its clip and no transition is running, the animator is
    // stuck (a copied exit transition whose condition never becomes true, or a
    // trigger consumed by an interrupted chain). Force the string to close out
    // instead of leaving the mech frozen mid-swing.
    private void TickAnimatorHangWatchdog()
    {
        if (animator == null || !isAttacking || isLunging || isFrozen) return;
        if (animator.IsInTransition(meleeLayerIndex)) return;
        AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(meleeLayerIndex);
        if (!IsInMeleeState(st)) return;
        if (st.normalizedTime < 1.05f) return;               // clip still playing
        if (Time.time - lastStepStartTime < minSwingSeconds) return; // never cut a swing short

        // The clip is over and nothing moved us on - treat it exactly like the
        // clip-end event that never arrived.
        EndAttack();
    }

    [Tooltip("Minimum time each swing must PLAY before the combo may advance to the next one. The overlap-scan lands hits ~0.15s into a swing - far earlier than the old mid-clip animation events - so chaining straight off the hit rattled the whole string out at double speed ('attack speed much faster than normal'). This restores the visual cadence: hit confirms early, but the next swing waits for the animation beat.")]
    public float minSwingSeconds = 0.45f;

    [Tooltip("Cooldown on the dash tackle (press F during a boost dash). It was spammable and too strong - EXVS-style moves this strong always carry a cooldown or heavy boost cost.")]
    public float tackleCooldownSeconds = 7f;
    private float tackleReadyAt = -99f;
    /// <summary>BURST perk: tackle comes off cooldown instantly.</summary>
    public void RefreshTackleCooldown() { tackleReadyAt = -99f; }
    /// <summary>BURST perk: tackle cooldown ticks faster while bursting.</summary>
    public void AccelerateTackleCooldown(float extraSeconds) { tackleReadyAt -= extraSeconds; }
    /// <summary>1 = tackle ready. For any HUD that wants to show it.</summary>
    public float TackleReady01 => Mathf.Clamp01(1f - (tackleReadyAt - Time.time) / Mathf.Max(0.01f, tackleCooldownSeconds));

    private int stringHitsLanded = 0;

    // EXVS PARTIAL-COMBO DOWN: ending or voluntarily cancelling a string that
    // landed 2+ hits still floors the victim - a SHORTER down (see
    // MechHealth.TriggerSoftKnockdown), less total damage taken. This is what
    // makes step-cancelling your own combo a real choice: safety + fast enemy
    // get-up now, versus the full string's damage and hard knockdown.
    private void TrySoftDownVictim(bool immediate = false)
    {
        int hits = stringHitsLanded;
        stringHitsLanded = 0;
        if (hits < 2) return;
        Transform target = GetTarget();
        if (target == null) return;
        MechHealth vh = target.GetComponentInParent<MechHealth>();
        if (vh == null || vh.isYellowLocked || vh.currentHealth <= 0f) return;
        if (immediate)
        {
            // Natural string end (no rainbow follow-up is coming): drop them NOW -
            // the delayed flop after a finished string read as broken combo logic.
            Vector3 d = vh.transform.position - transform.position;
            d.y = 0f;
            d = d.sqrMagnitude > 0.01f ? d.normalized : transform.forward;
            vh.TriggerSoftKnockdown(d * 6f + Vector3.up * 4f);
            return;
        }
        // RAINBOW-STEP GRACE: don't drop them yet. If the attacker re-engages
        // (step-cancel into a NEW string) within the window, the victim stays
        // standing-staggered and the combo EXTENDS - the EXVS rainbow loop.
        // Only if the attacker walks away does the victim crumple.
        // (Timestamp-driven, not a coroutine: CancelAttack's StopAllCoroutines
        // would kill a coroutine the same frame it was scheduled.)
        pendingSoftDownVictim = vh;
        pendingSoftDownAt = Time.time + softDownGraceSeconds;
    }

    [Tooltip("After a cut string, the victim stays staggered this long before crumpling - the rainbow-step window. Start a new string within it and they stay standing for the extension.")]
    public float softDownGraceSeconds = 0.65f;
    private MechHealth pendingSoftDownVictim;
    private float pendingSoftDownAt = -1f;

    private void TickPendingSoftDown()
    {
        if (pendingSoftDownVictim == null || Time.time < pendingSoftDownAt) return;
        MechHealth vh = pendingSoftDownVictim;
        pendingSoftDownVictim = null;
        if (vh == null || vh.isYellowLocked || vh.currentHealth <= 0f) return;
        if (isAttacking || isLunging) return; // re-engaged: the new string owns them now
        Vector3 dir = vh.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        vh.TriggerSoftKnockdown(dir * 6f + Vector3.up * 4f);
    }

    // ONE hit per swing: each TriggerPunch stamps lastStepStartTime, and a landed
    // hit consumes that stamp. All three hitboxes (both fists + foot) share this,
    // so a single swing can never multi-hit however long it overlaps the enemy.
    private float lastConsumedSwingStamp = -99f;
    public bool CanConsumeMeleeHit => lastStepStartTime != lastConsumedSwingStamp;
    public void ConsumeMeleeHit() { lastConsumedSwingStamp = lastStepStartTime; }

    private void Start()
    {
        // The specials system rides along on every mech that has combat + a player
        // controller - self-installing keeps rematch scene reloads working.
        if (GetComponent<MechController>() != null && GetComponent<SpecialMoves>() == null)
            gameObject.AddComponent<SpecialMoves>();

        if (rightFistCollider != null) rightFistCollider.enabled = false;
        if (leftFistCollider != null) leftFistCollider.enabled = false;
        if (leftFootCollider != null) leftFootCollider.enabled = false;

        // Park the scene's fixed punch cameras well below the lock-on Battle Camera
        // (priority 15) so they can never grab the shot on their own.
        if (punch1Camera != null) punch1Camera.Priority = usePunchCinematicCameras ? 5 : 0;
        if (punch4Camera != null) punch4Camera.Priority = usePunchCinematicCameras ? 5 : 0;

        VerifyMeleeAnimSet(); // slash1..slash4 present? otherwise fall back to punches
    }

    private void OnEnable() { shootAction.Enable(); meleeAction.Enable(); shieldAction.Enable(); tackleAction.Enable(); }
    private void OnDisable() { shootAction.Disable(); meleeAction.Disable(); shieldAction.Disable(); tackleAction.Disable(); }

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
        return IsFinalStep(currentMeleeStep) ? finisherBarBonus : meleeBarPerHit;
    }

    // MeleeHitbox asks this to pick the launch strength: the finisher flings hard,
    // a mid-string bar-fill downs with the hitbox's small default launch (EXVS weak down).
    public bool IsFinisherHit => IsFinalStep(currentMeleeStep);

    // The finisher is a GUARANTEED down - EXVS launchers always down, whatever
    // the bar says. Fired on the final step's hit however it landed (hitbox or snap).
    private void ForceFinisherDown()
    {
        Transform t = GetTarget();
        if (t == null) return;
        MechHealth vh = t.GetComponentInParent<MechHealth>();
        // NOTE: no longer bails on an already-downed victim. The soft partial-combo
        // knockdown (or an earlier hit filling the bar) could put them on the floor
        // a few frames BEFORE the finisher connected, and TriggerKnockdown ignores
        // an already-downed mech - so the big fling was silently swallowed. That
        // race only exists on this side of the fight, which is why it read as
        // "the enemy never gets knocked away".
        if (vh == null || vh.currentHealth <= 0f) return;
        Vector3 dir = vh.transform.position - transform.position;
        dir.y = 0f;
        dir = dir.sqrMagnitude > 0.01f ? dir.normalized : transform.forward;
        Vector3 fling = dir * finisherFlingForward + Vector3.up * finisherFlingUp;

        if (vh.isYellowLocked) vh.RelaunchDownedNow(fling); // upgrade the down in progress
        else vh.TriggerKnockdown(fling);
        pendingSoftDownVictim = null; // the hard down supersedes any pending soft one
    }

    public void RegisterHit()
    {
        hasHitConnected = true;
        stringHitsLanded++;
        lastHitRegisteredTime = Time.time;
        if (IsFinalStep(currentMeleeStep)) ForceFinisherDown();

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

    // TRUE while a punch cinematic camera outranks the normal battle camera.
    // The reset used to be an ANIMATION EVENT - and animation events silently never
    // fire on the retargeted Gundam clips - so once punch 1 raised a cinematic
    // camera to priority 20 it NEVER came back down. Killing the enemy then blended
    // into the parked finisher camera, which swept the view under the floor: the
    // "camera breaks through the ground after I finish the enemy" bug.
    private bool punchCamsLive;

    [Header("Punch cinematic cameras")]
    [Tooltip("OFF by default. These are separate CinemachineCameras parked in the scene at fixed angles - raising their priority mid-combo makes Cinemachine BLEND the whole view to a different position and back, which is what read as 'the camera goes crazy' during the finisher (an extreme close-up, then a hard pull-out). The lock-on Battle Camera is tuned for this fight; let it keep the shot. Turn back ON only if you want the scene's fixed punch angles.")]
    public bool usePunchCinematicCameras = false;

    public void SwitchToPunch1Camera()
    {
        if (!usePunchCinematicCameras) return;
        if (punch1Camera != null) { punch1Camera.Priority = 20; punchCamsLive = true; }
    }
    public void SwitchToPunch4Camera()
    {
        if (!usePunchCinematicCameras) return;
        if (punch4Camera != null) { punch4Camera.Priority = 20; punchCamsLive = true; }
    }
    public void SwitchToNormalCamera()
    {
        if (punch1Camera != null) punch1Camera.Priority = 5;
        if (punch4Camera != null) punch4Camera.Priority = 5;
        punchCamsLive = false;
    }

    private void Update()
    {
        if (Time.timeScale < 0.5f) return; // paused: clicking RESUME must not swing the saber

        // Hand the camera back the moment no melee owns the mech. Covers EVERY exit
        // path - clean end, step cancel, stagger, and the one that actually broke:
        // the enemy dying mid-string, which left the finisher camera live forever.
        if (punchCamsLive && !isAttacking && !isLunging && !isInEndLag) SwitchToNormalCamera();

        UpdateMeleeLayerWeight();    // masked upper-body layer fades in/out with the string
        TickAnimatorHangWatchdog();  // force-close a swing whose exit transition never fired

        TickPendingSoftDown();

        // FINISHER SNAP (launcher magnetism): if the last swing is out, nothing has
        // connected yet, and the target is in plausible range, the hit lands anyway.
        // The punch4 clip's contact point wanders too much to trust the hitbox alone,
        // and a point-blank whiffed finisher (no fling, no down) reads as a robbery.
        if (IsFinisherSwing && !hasHitConnected && Time.time - lastStepStartTime >= 0.18f)
        {
            Transform snapTarget = GetTarget();
            if (snapTarget != null && Vector3.Distance(transform.position, snapTarget.position) <= 6f)
            {
                MechHealth vh = snapTarget.GetComponentInParent<MechHealth>();
                if (vh != null && !vh.isYellowLocked && vh.currentHealth > 0f)
                {
                    vh.TakeDamage(8f, 0f);                       // finisher damage; the down comes from ForceFinisherDown
                    CombatVfx.SpawnHit(vh.transform.position + Vector3.up * 1.2f);
                    ConsumeMeleeHit();                            // the hitbox can't double-dip this swing
                    RegisterHit();                                // camera + combo counter + guaranteed down
                }
            }
        }

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
        // On cooldown: a fresh Q press gets the "not ready" blip so it never
        // reads as "the shield is broken".
        if (!isShielding && shieldAction != null && shieldAction.WasPressedThisFrame() && Time.time < shieldReadyAt)
            BattleAudio.Play("alert", 0.3f, 0.55f);

        bool wantShield = shieldAction != null && shieldAction.IsPressed()
            && (isShielding || Time.time >= shieldReadyAt) // cooldown gates RAISING, never drops a held shield
            && !isAttacking && !isLunging && !isInEndLag
            && (mechController.currentState == MechState.Grounded ||
                mechController.currentState == MechState.Airborne ||
                mechController.currentState == MechState.BoostDash)
            && boostManager != null && !boostManager.isOverheated && boostManager.currentBoost > 0.5f;

        if (wantShield && !isShielding)
        {
            // Guarding out of a boost dash ENDS the dash. The shield roots you by
            // design, and a shield that keeps dash speed would be strictly better
            // than either option on its own.
            if (mechController.currentState == MechState.BoostDash)
                mechController.currentState = mechController.CheckIfGrounded()
                    ? MechState.Grounded : MechState.Airborne;

            if (myHealth != null) myHealth.BreakWakeUpProtection(); // guarding is an action
            isShielding = true;
            if (shieldVisual == null) shieldVisual = ShieldVisual.Create(transform);
            shieldVisual.SetActive(true);
            if (shieldDebugLogs) Debug.Log("[Parry] shield UP");
        }
        else if (!wantShield && isShielding)
        {
            isShielding = false;
            shieldReadyAt = Time.time + shieldCooldownSeconds; // dropping it (for ANY reason) starts the cooldown
            if (shieldVisual != null) shieldVisual.SetActive(false);
            if (shieldDebugLogs) Debug.Log("[Parry] shield DOWN - ready again in " + shieldCooldownSeconds + "s");
        }

        // Q held but the shield refused to raise: say WHY (once per second)
        if (shieldDebugLogs && shieldAction != null && shieldAction.IsPressed() && !isShielding &&
            Time.time - lastRefuseLog > 1f)
        {
            lastRefuseLog = Time.time;
            Debug.Log("[Parry] Q held but shield refused: cdLeft=" + Mathf.Max(0f, shieldReadyAt - Time.time).ToString("0.0") +
                      " attacking=" + isAttacking +
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

            // BRANCH COMBO (EXVS-style rekka): pressing SHOOT mid-string branches
            // into the gun-smash ender - point-blank shots out of the combo.
            // Once per string, needs at least one landed hit of momentum.
            if (isAttacking && !isLunging && !isInEndLag && !branchUsedThisString &&
                currentMeleeStep >= 1 && mechShooter != null && mechShooter.currentAmmo >= 1)
            {
                StartCoroutine(BranchComboRoutine());
                return;
            }

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

        // DASH TACKLE - now on its own key (F, pressed during a boost dash):
        // a shoulder charge that keeps the dash momentum and slams whatever it
        // touches. Moved off dash+click because it kept firing when the player
        // just wanted a chase melee out of a dash.
        if (tackleAction.WasPressedThisFrame() &&
            mechController.currentState == MechState.BoostDash &&
            !isAttacking && !isLunging && !isInEndLag)
        {
            if (Time.time < tackleReadyAt)
            {
                BattleAudio.Play("alert", 0.35f, 0.6f); // "not ready" blip
            }
            else
            {
                if (myHealth != null) myHealth.BreakWakeUpProtection();
                tackleReadyAt = Time.time + tackleCooldownSeconds;
                StartCoroutine(DashTackleRoutine());
            }
            return;
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
        // A stale pre-melee direction tap must NOT pair with a movement re-press
        // mid-string and read as a rainbow step - that silently cancelled the attack.
        mechController.ResetStepFlickBuffer();

        isAttacking = true;
        branchUsedThisString = false;
        stringHitsLanded = 0;
        mechController.currentState = MechState.Attacking;
        lastMeleeTime = Time.time;
        IgniteSaber();
        // Guards the punch states' exit transitions: while this is true the animator
        // may NOT auto-slide from a finished punch clip back into Locomotion - the
        // "holding a movement key flashes the run pose mid-combo" bug.
        if (animator != null) animator.SetBool("IsAttacking", true);

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

        bool hitChain = hasHitConnected && Time.time - lastHitRegisteredTime >= chainDelayAfterHit
                        && Time.time - lastStepStartTime >= minSwingSeconds; // let the swing anim actually play
        // Whiffs chain EVERYWHERE now (at the full swing cadence). Red-lock whiffs
        // used to dead-end the string - one airborne miss (step + jump + melee)
        // and the combo just stopped.
        bool whiffChain = Time.time - lastStepStartTime >= Mathf.Max(whiffChainInterval, minSwingSeconds);

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
            animator.CrossFadeInFixedTime(MeleeState(AnimIndexForStep(step)), 0.08f, meleeLayerIndex);
        }
        ResetHitboxCooldowns();
        if (step > 1 && startedInRedLock) StartCoroutine(MicroLunge(IsFinalStep(step) ? 0.28f : 0.15f, IsFinalStep(step) ? 24f : 15f));
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

        if (animator != null) animator.CrossFadeInFixedTime(MeleeState(1), 0.08f, meleeLayerIndex); // forced, same as TriggerPunch
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
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(meleeLayerIndex);
            if (st.IsName(MeleeState(1)) || st.IsName("punch1") || st.IsName("Melee1")) { reachedWindup = true; break; }
            poseWait += Time.deltaTime;
            yield return null;
        }

        if (animator != null && reachedWindup && isLunging && isAttacking) animator.speed = 0f;

        float elapsedTime = 0f;
        float currentSpeed = meleeLungeSpeed * mechController.bigMapSpeedScale; // chase scales with the speed buff
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
            if (flat.magnitude <= lungeStopDistance && Mathf.Abs(offset.y) <= 1.2f) break; // was 2.5: stopping that high above the enemy made jump-melee whiff

            currentSpeed = Mathf.Lerp(currentSpeed, minLungeSpeed * mechController.bigMapSpeedScale, Time.deltaTime * lungeSpeedDrag);
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

    private IEnumerator MicroLunge(float duration, float speed)
    {
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            Transform target = GetTarget();
            if (target != null && !IsTargetYellowLocked(target))
            {
                Vector3 offset = target.position - transform.position;
                offset.y = 0;

                if (offset.magnitude > lungeStopDistance)
                {
                    characterController.Move(offset.normalized * speed * mechController.bigMapSpeedScale * Time.deltaTime);
                }
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }

    private bool branchUsedThisString = false;
    private SaberBlade saberBlade; // beam saber shown during melee strings

    // BRANCH COMBO: melee string -> SHOOT = the gun-smash ender. The mech throws
    // the big punch-4 swing while unloading three point-blank shots into the
    // target - heavy knockdown bar, cinematic camera, ammo cost 1-3.
    // isLunging is borrowed as the interlock so stray EndAttack events from the
    // crossfaded clip can't cut the branch short (EndAttack ignores lunges).
    private IEnumerator BranchComboRoutine()
    {
        branchUsedThisString = true;
        isLunging = true;
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();

        if (animator != null)
        {
            animator.ResetTrigger("Melee1"); animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3"); animator.ResetTrigger("Melee4");
            animator.CrossFadeInFixedTime(MeleeState(4), 0.07f, meleeLayerIndex);
        }
        SwitchToPunch4Camera();
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.SpecialKick(1.6f, 0.8f);

        yield return new WaitForSeconds(0.22f); // windup beat

        Transform target = GetTarget();
        int shots = Mathf.Min(3, mechShooter != null ? mechShooter.currentAmmo : 0);
        for (int i = 0; i < shots; i++)
        {
            if (mechShooter != null) mechShooter.currentAmmo--;
            Vector3 from = transform.position + transform.forward * 1.3f + Vector3.up * 1.35f;
            Vector3 aim = target != null ? (target.position + Vector3.up * 1.2f - from).normalized : transform.forward;
            HomingProjectile shot = HomingProjectile.SpawnSimple(from, Quaternion.LookRotation(aim), 1.3f, new Color(1f, 0.55f, 0.15f));
            shot.damage = 7f;
            shot.knockdownPower = 22f;
            shot.speed = 70f;
            bool close = target != null && Vector3.Distance(transform.position, target.position) <= redLockRange;
            shot.Init(close && !IsTargetYellowLocked(target) ? target : null, transform);
            CombatVfx.SpawnHit(from);
            yield return new WaitForSeconds(0.13f);
        }

        yield return new WaitForSeconds(0.25f);
        SwitchToNormalCamera();
        isLunging = false;
        StartCoroutine(EndLagRoutine());
    }

    // ---- beam saber: the melee WEAPON, not just a visual ----
    // The blade is what swings now, so the hit scan rides the blade instead of the
    // fist bone: contact happens where the glow is, and the string gains the saber's
    // reach. Slash clips + blade-driven hitbox = the EXVS saber read.
    private void IgniteSaber()
    {
        // Push the inspector-tuned grip down before the blade aligns itself
        SaberBlade.GripPitchDegrees = saberGripPitch;
        SaberBlade.GripRollDegrees = saberGripRoll;
        if (saberBlade != null) { AttachHitboxesToBlade(); return; }
        Transform hand = null, forearm = null;
        if (animator != null && animator.isHuman)
        {
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        }
        if (hand == null && rightFistCollider != null)
        {
            hand = rightFistCollider.transform;
            forearm = hand.parent;
        }
        if (hand != null) saberBlade = SaberBlade.Create(hand, forearm);
        AttachHitboxesToBlade();
    }

    // Point the right-hand hitbox at the blade's mid-point. The left fist / foot
    // stay on their bones - the saber is a one-handed weapon.
    private void AttachHitboxesToBlade()
    {
        if (saberBlade == null || rightFistCollider == null) return;
        MeleeHitbox h = rightFistCollider.GetComponent<MeleeHitbox>();
        if (h != null) h.followPoint = saberBlade.BladePoint;
    }

    private void ExtinguishSaber()
    {
        if (rightFistCollider != null)
        {
            MeleeHitbox h = rightFistCollider.GetComponent<MeleeHitbox>();
            if (h != null) h.followPoint = null; // back to the bone when the blade is out
        }
        if (saberBlade != null) { saberBlade.Dismiss(); saberBlade = null; }
    }

    // NEW MOVE - DASH TACKLE: shoulder charge out of a boost dash. Keeps dash
    // momentum, hits with both fists, launches on contact via the normal
    // knockdown-bar rules. Short, committal, cancels into end lag.
    private IEnumerator DashTackleRoutine()
    {
        isAttacking = true;
        isLunging = true; // interlock: stray EndAttack events can't cut it short
        branchUsedThisString = true;
        stringHitsLanded = 0;
        currentMeleeStep = 1;
        hasHitConnected = false;
        lastStepStartTime = Time.time;
        mechController.ResetStepFlickBuffer(); // same stale-double-tap guard as StartMeleeString
        mechController.currentState = MechState.Attacking;
        IgniteSaber();

        if (animator != null)
        {
            animator.CrossFadeInFixedTime(MeleeState(3), 0.06f, meleeLayerIndex);
        }
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.SpecialKick(1.2f, 0.6f);

        ResetHitboxCooldowns();
        if (rightFistCollider != null) rightFistCollider.enabled = true;
        if (leftFistCollider != null) leftFistCollider.enabled = true;

        // Charge direction: at the enemy if we have one, else straight ahead
        Transform target = GetTarget();
        Vector3 dir = transform.forward;
        if (target != null)
        {
            Vector3 to = target.position - transform.position; to.y = 0f;
            if (to.sqrMagnitude > 0.04f) dir = to.normalized;
        }

        float t = 0f;
        while (t < 0.38f)
        {
            if (!isAttacking) break; // staggered out of the tackle
            characterController.Move(dir * 26f * mechController.bigMapSpeedScale * Time.deltaTime);
            mechController.velocity.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 18f);
            t += Time.deltaTime;
            yield return null;
        }

        DisableRightFist(); DisableLeftFist();
        isLunging = false;
        StartCoroutine(EndLagRoutine());
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
            AnimatorStateInfo st = animator.GetCurrentAnimatorStateInfo(meleeLayerIndex);
            if (!IsInMeleeState(st)) return;
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
        TrySoftDownVictim(immediate: true); // finished string: down them NOW (grace is only for cancels)
        SwitchToNormalCamera();
        isInEndLag = true;
        isAttacking = false;
        isLunging = false;
        startedInRedLock = false;
        currentMeleeStep = 0;
        ExtinguishSaber();
        lastMeleePressTime = -99f;
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();
        if (animator != null)
        {
            animator.SetBool("IsAttacking", false);
            // The string is truly over: leave the punch pose NOW instead of waiting
            // for the (now-guarded) exit transition.
            ReturnMeleeLayerToIdle();
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

    /// <summary>Unconditional version of CancelAttack for knockdowns. CancelAttack
    /// returns early when no string is running, which is fine mid-fight but wrong
    /// on a down: the routines still get stopped by MechHealth, so any flag they
    /// owned (lunging, end lag, hit-stop freeze) would stay stuck with nothing left
    /// to clear it, and the mech wakes up unable to attack.</summary>
    public void ForceResetAfterDown()
    {
        SwitchToNormalCamera();
        StopAllCoroutines();
        ExtinguishSaber();
        isAttacking = false;
        isLunging = false;
        isInEndLag = false;
        isFrozen = false;
        startedInRedLock = false;
        currentMeleeStep = 0;
        stringHitsLanded = 0;
        lastMeleePressTime = -99f;
        pendingSoftDownVictim = null;
        DisableRightFist(); DisableLeftFist(); DisableLeftFoot();
        if (animator != null)
        {
            animator.SetBool("IsAttacking", false);
            animator.speed = 1f;
            animator.ResetTrigger("Melee1");
            animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3");
            animator.ResetTrigger("Melee4");
        }
        ReturnMeleeLayerToIdle();
    }

    public void CancelAttack(bool interruptedByHit = false)
    {
        SwitchToNormalCamera();
        if (!isAttacking && !isInEndLag) return;
        // Voluntary cancel (rainbow step / BD cancel) still drops a combo'd victim.
        // Getting HIT out of your string does NOT - the interruptor earned the save.
        if (!interruptedByHit) TrySoftDownVictim();
        else stringHitsLanded = 0;
        StopAllCoroutines();
        ExtinguishSaber();
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
            animator.SetBool("IsAttacking", false);
            animator.speed = 1f;
            animator.ResetTrigger("Melee1");
            animator.ResetTrigger("Melee2");
            animator.ResetTrigger("Melee3");
            animator.ResetTrigger("Melee4");
        }
    }
}
