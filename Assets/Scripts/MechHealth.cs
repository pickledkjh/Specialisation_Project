using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class MechHealth : MonoBehaviour
{
    /// <summary>
    /// Fired every time ANY mech takes damage (victim, amount). The HUD listens to
    /// this for the red "you got hit" screen flash on the player.
    /// </summary>
    public static System.Action<MechHealth, float> AnyMechDamaged;

    public float maxHealth = 100f;
    public float currentHealth;

    [Header("Team / Cost (used on death)")]
    public Team team = Team.Team2;
    public int unitCost = 2000;

    [Header("Hidden Knockdown Bar")]
    public float maxKnockdownValue = 100f;
    [Range(0f, 1f)]
    [Tooltip("Pitch-promised knockdown calculator: damage taken SHRINKS as the bar fills. 0.4 = a mech one hit from knockdown takes 40% less damage - the knockdown becomes the payoff, and strings can never melt a full health bar.")]
    public float damageFalloffAtFullBar = 0.4f;
    public float currentKnockdownValue = 0f;
    [Tooltip("After the last hit taken, the bar HOLDS its full value for this many seconds before any draining starts. Stop a combo, reposition, come back — the damage still counts. (Field deliberately renamed from knockdownDecayDelay so the new default applies over any stale scene value.)")]
    public float knockdownHoldSeconds = 4f;
    [Tooltip("Drain per second AFTER the hold window. 8 = a full bar takes ~12.5s to empty, so accumulated hits stay threatening for a long while, like the reference games. (Field deliberately renamed from knockdownDecayRate — the old scene value of 15 was wiping partial combos in ~3 seconds.)")]
    public float knockdownDrainPerSecond = 8f;

    [Header("Down State / Yellow Lock")]
    public bool isYellowLocked = false;
    public float downedDuration = 3.0f;
    public float wakeUpProtectionDuration = 3.0f;

    [Header("Knockdown Flight Physics")]
    // RENAMED FIELDS. The player and the enemy each carry their own MechHealth, and
    // the enemy's copy still held old scene-serialized values - which is why the
    // fling and the surface landing looked "player only". Renaming forces BOTH
    // instances back onto these code defaults.
    [Tooltip("Gravity during the launch flight. Less negative = longer, more dramatic arc.")]
    public float flightGravity = -22f;
    [Tooltip("How fast horizontal launch speed decays mid-flight. Lower = flies farther.")]
    public float flightDrag = 1.1f;
    [Tooltip("Safety cap only - the flight normally ends when the mech touches a surface.")]
    public float flightMaxSeconds = 1.6f;

    [Header("Placeholder Down Pose (used only until real HitDown/Recover states exist)")]
    [Tooltip("While the animator has no 'HitDown' state, the model is tilted flat by code so knockdowns are VISIBLE instead of the mech standing there looking normal while invulnerable. Add real knockdown states/clips to the animator and this deactivates itself.")]
    public bool usePlaceholderDownPose = true;
    [Tooltip("Degrees per second the placeholder tilt animates at.")]
    public float placeholderTiltSpeed = 300f;

    private Animator animator;
    private SimpleMechAI aiController;
    private MechController playerController;
    private MechCombat playerCombat;
    private CharacterController cc;

    private Coroutine downedCoroutine;
    private bool loggedComponents;
    private bool inWakeUpProtection = false;

    /// <summary>True only while the mech is genuinely floored - flying or lying
    /// down - and NOT during the wake-up protection window afterwards. Control
    /// scripts must stay hands-off during this, but they must be free to act once
    /// the mech is back on its feet, because acting is what ends the protection.</summary>
    public bool IsDownLocked { get { return isYellowLocked && !inWakeUpProtection; } }

    // ---- BURST ESCAPE INVULNERABILITY ----
    private float burstInvulnUntil = -99f;
    /// <summary>True during the awakening's escape window: yellow-locked (nothing can
    /// touch you) but still fully in control.</summary>
    public bool InBurstInvulnerability { get { return Time.time < burstInvulnUntil; } }

    /// <summary>Awakening escape: a short window where the mech is untargetable and
    /// takes no damage, but - unlike a knockdown - keeps full control. That is what
    /// lets a burst break a combo: every hitbox and projectile already skips a
    /// yellow-locked victim, so the attacker's string simply stops connecting.
    ///
    /// inWakeUpProtection is set alongside it deliberately: IsDownLocked is
    /// (yellowLocked AND NOT wakeUpProtection), so this keeps IsDownLocked FALSE and
    /// the control scripts keep running. Yellow lock for the enemy, business as
    /// usual for you.</summary>
    public void GrantBurstInvulnerability(float seconds)
    {
        if (currentHealth <= 0f) return;
        burstInvulnUntil = Time.time + seconds;
        isYellowLocked = true;
        inWakeUpProtection = true;
        currentKnockdownValue = 0f;         // the escape clears the accumulated bar
        StartCoroutine(EndBurstInvulnerability());
    }

    private IEnumerator EndBurstInvulnerability()
    {
        while (Time.time < burstInvulnUntil) yield return null;
        // A real knockdown may have started meanwhile - never clear its lock
        if (downedCoroutine == null)
        {
            isYellowLocked = false;
            inWakeUpProtection = false;
        }
    }

    // Placeholder lie-down pose state
    private bool animatorHasDownStates = false;
    private Transform modelTransform;
    private Quaternion tiltBaseLocalRot = Quaternion.identity; // captured when the knockdown starts
    private Vector3 tiltBaseLocalPos = Vector3.zero;           // captured with it (pins root-motion drift)
    private float downTilt = 0f;          // current tilt in degrees
    private float downTiltTarget = 0f;    // 0 = upright, 90 = lying on back
    private bool rootMotionWasOn = false;

    private void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponentInChildren<Animator>();
        aiController = GetComponent<SimpleMechAI>();
        playerController = GetComponent<MechController>();
        playerCombat = GetComponent<MechCombat>();
        cc = GetComponent<CharacterController>();

        if (animator != null)
        {
            modelTransform = animator.transform;

            // THE ENEMY-ONLY KNOCKDOWN BUG.
            // The placeholder down pose PINS modelTransform.localPosition every
            // LateUpdate so the model can't drift while lying down. That is correct
            // for a model on a CHILD object - which is where the player's animator
            // lives. The enemy's Animator sits on its ROOT, so modelTransform WAS
            // this mech's own transform, and the pin rewrote the body's world
            // position back to the captured value every single frame. The launch
            // moved it (the controller's internal position advanced 1x, 2x, 3x) and
            // the pin dragged the transform back before anyone could see it - a
            // perfect, silent "flew 0.0u".
            // The fix is NOT to throw the transform away - doing that cost the enemy
            // its lie-down pose entirely. Only the POSITION pin is harmful; rotating
            // the root to tilt the body is exactly what the down pose should do.
            // See LateUpdate: the position line is guarded, the rotation is not.
            if (modelTransform == transform)
                Debug.Log("[Down] '" + name + "' has its Animator on the ROOT - down pose will tilt the " +
                          "root and skip the position pin (that pin was cancelling knockdown launches).");
            // If the controller gains real knockdown states later, the code placeholder
            // steps aside automatically.
            animatorHasDownStates = animator.HasState(0, Animator.StringToHash("HitDown"));
        }
    }

    private float lastDamageTime = -99f;

    private void Update()
    {
        // The bar holds for knockdownHoldSeconds after every hit, then drains slowly.
        // Melee hits AND projectile hits both land in the same bar via TakeDamage.
        if (!isYellowLocked && currentKnockdownValue > 0 &&
            Time.time - lastDamageTime >= knockdownHoldSeconds)
        {
            currentKnockdownValue -= knockdownDrainPerSecond * Time.deltaTime;
            if (currentKnockdownValue < 0) currentKnockdownValue = 0;
        }
    }

    // Runs AFTER the Animator has posed the model this frame, so the placeholder tilt
    // wins over whatever standing animation is still playing during the down state.
    // IMPORTANT: while fully upright this must not touch the model at all — writing
    // the rotation every frame would fight root motion / other systems and skew the
    // body's facing during normal play.
    private void LateUpdate()
    {
        // FLIGHT AUTHORITY. The launch is written from a coroutine, which runs in the
        // Update phase - anything that moves this transform later in the frame wins,
        // and the body lands back where it started ("flew 0.0u" while the per-frame
        // log clearly showed it moving). Re-asserting the flight position here, after
        // every Update, makes the launch the last word. It also NAMES the culprit:
        // any drift between what the flight wrote and what is here now is somebody
        // else moving this mech, and it gets logged once.
        if (flightInProgress)
        {
            Vector3 now = transform.position;
            if ((now - flightAuthoritativePos).sqrMagnitude > 0.0004f)
            {
                if (!loggedFlightInterference)
                {
                    loggedFlightInterference = true;
                    Debug.LogWarning("[Down] '" + name + "' FLIGHT INTERFERENCE: the flight wrote " +
                                     flightAuthoritativePos.ToString("F2") + " but something moved it to " +
                                     now.ToString("F2") + " (delta " + (now - flightAuthoritativePos).ToString("F2") +
                                     "). Overriding it for the rest of the launch.");
                }
                transform.position = flightAuthoritativePos;
            }
        }

        if (!usePlaceholderDownPose || animatorHasDownStates || modelTransform == null) return;
        if (downTiltTarget <= 0f && downTilt <= 0.01f) return; // upright: hands off

        downTilt = Mathf.MoveTowards(downTilt, downTiltTarget, placeholderTiltSpeed * Time.deltaTime);
        modelTransform.localRotation = tiltBaseLocalRot * Quaternion.Euler(-downTilt, 0f, 0f);
        // PIN the model position too: with the animator frozen in a state whose clip
        // has root motion (e.g. Floating's hover bob), the model child accumulated
        // upward drift every loop while downed — the "constantly rising while yellow
        // locked" bug. While the down pose owns the model, nothing else may move it.
        // Belt and braces: never pin the mech's OWN transform. On a root object this
        // is a world-space position lock that silently cancels knockdown launches.
        if (modelTransform != transform) modelTransform.localPosition = tiltBaseLocalPos;
        // On full recovery the final write above restores the exact captured pose,
        // then the early-out takes over next frame.
    }

    // launchVelocity is optional so all existing 2-argument call sites still compile.
    public void TakeDamage(float amount, float knockdownPower, Vector3 launchVelocity = default)
    {
        if (isYellowLocked) return;

        // Knockdown calculator (from the proposal): the fuller the bar, the less
        // raw damage gets through - long strings trade damage for the knockdown.
        float barFill = maxKnockdownValue > 0f ? Mathf.Clamp01(currentKnockdownValue / maxKnockdownValue) : 0f;
        float scaledDamage = amount * (1f - damageFalloffAtFullBar * barFill);

        // Awakening (burst): while the player's burst is active they deal +15% and
        // take -15% - the EXVS awakening payoff. Neutral (1.0) when no burst.
        scaledDamage *= AwakeningSystem.DamageScaleFor(this);

        currentHealth -= scaledDamage;
        currentKnockdownValue += knockdownPower;
        lastDamageTime = Time.time;

        // ---- hit feedback ----
        // One central place covers EVERY damage source (melee, shots, charge shots):
        // an impact burst on the victim's chest + the global event the HUD uses for
        // the player's red damage flash. Without these, ranged hits especially were
        // landing with zero visible reaction.
        CombatVfx.SpawnHit(transform.position + Vector3.up * 1.3f);
        AnyMechDamaged?.Invoke(this, scaledDamage);

        if (currentHealth <= 0)
        {
            Die();
            return;
        }

        if (currentKnockdownValue >= maxKnockdownValue)
        {
            TriggerKnockdown(launchVelocity);
        }
    }

    /// <summary>Upgrade a down that is ALREADY running into a full launch.
    ///
    /// This is the missing piece behind "the enemy doesn't get knocked away".
    /// TriggerKnockdown refuses to fire on an already-downed mech, and the victim
    /// can easily be downed a few frames BEFORE the finisher connects - by the
    /// partial-combo soft knockdown, or by an earlier hit filling the bar. That
    /// path only exists on the attacking-player side, which is why it looked like
    /// an enemy-only problem. The finisher must always fling, so it overrides
    /// whatever down is in progress instead of being swallowed by it.</summary>
    /// <summary>True while a launch arc is actually in the air. Guards against a
    /// second knockdown restarting the routine mid-flight, which reset the flight's
    /// start point and made a real launch report "flew 0.0u".</summary>
    private bool flightInProgress;
    private Vector3 flightAuthoritativePos;   // what the launch says the position is
    private bool loggedFlightInterference;

    public void RelaunchDownedNow(Vector3 launchVelocity)
    {
        if (currentHealth <= 0f) return;
        if (flightInProgress) return; // already sailing - don't restart the arc
        if (launchVelocity.sqrMagnitude > 1f) StartCoroutine(FinisherImpactFreeze());
        isYellowLocked = true;
        currentKnockdownValue = 0f;
        if (downedCoroutine != null) StopCoroutine(downedCoroutine);
        downedCoroutine = StartCoroutine(DownedStateRoutine(launchVelocity, 1f));
    }

    public void TriggerKnockdown(Vector3 launchVelocity = default)
    {
        // NOTE: deliberately does NOT test flightInProgress. If a flight coroutine
        // is ever stopped mid-air (a second knockdown, a death) the flag leaks as
        // true and every future knockdown is silently swallowed - which is exactly
        // what "the impact is gone this time" was.
        if (isYellowLocked) return;

        // CINEMATIC FINISHER IMPACT: the bar-filling hit lands HEAVY - a beat of
        // near-frozen time + camera shake before the body flies off burning.
        if (launchVelocity.sqrMagnitude > 1f) StartCoroutine(FinisherImpactFreeze());

        if (downedCoroutine != null) StopCoroutine(downedCoroutine);
        downedCoroutine = StartCoroutine(DownedStateRoutine(launchVelocity, 1f));
    }

    // Short and violent: 0.22 REAL seconds at 5% speed, then snap back. Kept brief
    // on purpose - a long pause reads as lag when it happens every knockdown.
    private IEnumerator FinisherImpactFreeze()
    {
        if (Time.timeScale < 0.9f) yield break; // never fight menus / burst cut-in / kill slow-mo
        BattleAudio.Play("hit", 1f, 0.55f);     // deep impact thud
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.35f, 0.45f);
        float prevFixed = Time.fixedDeltaTime;
        Time.timeScale = 0.05f;
        Time.fixedDeltaTime = prevFixed * 0.05f;
        yield return new WaitForSecondsRealtime(0.22f);
        if (Mathf.Abs(Time.timeScale - 0.05f) < 0.02f) Time.timeScale = 1f;
        Time.fixedDeltaTime = prevFixed;
    }

    /// <summary>EXVS partial-combo down: an INTERRUPTED melee string still floors
    /// the victim - they hit the ground yellow-locked like any down (bar resets),
    /// but the down and wake-up protection are SHORTER, scaled by how full their
    /// bar was. Less damage taken + faster recovery = the attacker's price for
    /// cutting the combo early, and the reason step-cancelling matters.</summary>
    public void TriggerSoftKnockdown(Vector3 launchVelocity = default)
    {
        if (isYellowLocked || currentHealth <= 0f) return;
        float fill = maxKnockdownValue > 0f ? Mathf.Clamp01(currentKnockdownValue / maxKnockdownValue) : 0f;
        float scale = Mathf.Lerp(0.4f, 0.85f, fill); // emptier bar = much faster get-up
        if (downedCoroutine != null) StopCoroutine(downedCoroutine);
        downedCoroutine = StartCoroutine(DownedStateRoutine(launchVelocity, scale));
    }

    private IEnumerator DownedStateRoutine(Vector3 launchVelocity, float durationScale)
    {
        isYellowLocked = true;
        inWakeUpProtection = false;
        currentKnockdownValue = 0f;
        flightInProgress = false; // fresh down: never inherit a leaked flag

        // KILL THE VICTIM'S RUNNING COROUTINES FIRST.
        // Disabling a MonoBehaviour does NOT stop its coroutines - only deactivating
        // the GameObject does. So SetControlScripts(false) silenced Update() but left
        // the AI's chase / lunge / combo routines running, and every one of them
        // calls controller.Move() at the body every frame. They fought the launch for
        // its whole duration. The player has the same routines but is far less likely
        // to be mid-lunge at the exact moment a finisher lands, which is precisely
        // why this looked like an enemy-only bug.
        // CancelAttack does the stopping AND cleans up the flags those routines own
        // (shield, hit-stop, shot windup, combo counter, live hitboxes). Calling raw
        // StopAllCoroutines instead left the AI standing there with a half-finished
        // combo state and no coroutine alive to finish it - the "AI hangs after one
        // knockdown" bug.
        if (aiController != null) aiController.CancelAttack();
        if (playerCombat != null) playerCombat.ForceResetAfterDown();
        if (playerController != null) playerController.ForceResetAfterDown();

        if (animator != null)
        {
            animator.speed = 1f;              // in case a hit-stop froze the animator
            animator.ResetTrigger("GetHit");  // stagger trigger can no longer eat the knockdown animation
            animator.SetTrigger("HitDown");
        }

        // Visible down state even without real knockdown animations (see header field).
        // Capture the CURRENT model pose as the tilt base so the restore is exact.
        if (modelTransform != null && downTilt <= 0.01f)
        {
            tiltBaseLocalRot = modelTransform.localRotation;
            tiltBaseLocalPos = modelTransform.localPosition;
        }
        downTiltTarget = 90f;

        // Root motion off while downed — its accumulated deltas were floating the
        // body upward through the whole down state. Restored on recovery.
        if (animator != null)
        {
            rootMotionWasOn = animator.applyRootMotion;
            animator.applyRootMotion = false;
        }

        SetControlScripts(false);

        // FIX (floating-while-downed): this always runs, launch or not, and keeps applying
        // gravity until the mech ACTUALLY touches the ground. Previously the flight loop
        // could time out mid-air and the mech lay frozen floating for the whole down state.
        Vector3 flightStart = transform.position;
        {
            // NOTE: this block used to be gated on `cc != null`, which meant a mech
            // without a CharacterController silently skipped the ENTIRE flight and
            // landing - no fling, no surface landing, nothing. That is exactly the
            // shape of "it works on the player but not on the enemy", so the gate
            // is gone: the flight runs for every mech and the CC is handled where
            // it is actually touched.
            Vector3 vel = launchVelocity;
            float t = 0f;
            float hardCap = flightMaxSeconds + 3f; // safety only; normally we land first
            bool landed = false;

            // FLY THROUGH THE CONTROLLER, NOT AROUND IT.
            // Every previous attempt disabled the CharacterController and wrote the
            // transform directly - and the controller won every time, because it
            // restores its cached position the moment it is re-enabled. Fighting it
            // was the mistake. cc.Move() is the controller's OWN api: nothing can
            // revert it, there is no cache to desync, and it brings real collision
            // for free - the body slides along walls and lands on rooftops properly
            // instead of being teleported through them.
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null && !cc.enabled) cc.enabled = true; // must be ON to Move()
            bool useController = cc != null && cc.enabled;

            // Root motion on a ROOT animator rewrites this transform every frame in
            // the animation phase. The enemy has an Animator on its root (the player's
            // lives on a child) - a real asymmetry worth neutralising for the flight.
            Animator[] rootAnimators = GetComponents<Animator>();
            bool[] rootMotionWas = new bool[rootAnimators.Length];
            for (int i = 0; i < rootAnimators.Length; i++)
            {
                rootMotionWas[i] = rootAnimators[i].applyRootMotion;
                rootAnimators[i].applyRootMotion = false;
            }

            // A NavMeshAgent WARPS its transform back to the agent's internal
            // position every frame - the one common component that silently beats
            // even direct transform writes. If the enemy carries one, it dies for
            // the duration of the flight.
            UnityEngine.AI.NavMeshAgent agent = GetComponent<UnityEngine.AI.NavMeshAgent>();
            bool agentWasEnabled = agent != null && agent.enabled;
            if (agent != null) agent.enabled = false;

            // Same story for a physics Rigidbody: a non-kinematic body owns the
            // transform and stomps direct writes every FixedUpdate. The two mechs
            // are NOT set up identically, so this is checked per-victim instead of
            // assumed - that kind of asymmetry is exactly how a fix ends up
            // working on the player and not on the enemy.
            Rigidbody body = GetComponent<Rigidbody>();
            bool bodyWasKinematic = body == null || body.isKinematic;
            if (body != null && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }


            // One-time forensic PER MECH: name every component on this root so
            // whatever fights the flight is identified in the console, not guessed
            // at. Keyed by name so the player and the enemy each get a line.
            if (!loggedComponents)
            {
                loggedComponents = true;
                Component[] comps = GetComponents<Component>();
                string list = "";
                foreach (Component comp in comps) if (comp != null) list += comp.GetType().Name + " ";
                Debug.Log("[Down] components on '" + name + "': " + list);
            }

            // A REAL fling (bar-filling knockdown) burns: fire trail while airborne,
            // and the tumbling body is a wrecking ball - buildings it crashes
            // through collapse. Soft/partial downs (small launches) stay plain.
            bool bigLaunch = launchVelocity.sqrMagnitude > 90f;
            ParticleSystem fireTrail = null;
            if (bigLaunch)
            {
                fireTrail = ProceduralVfx.MakeJet(transform, Vector3.up * 1.1f,
                    Quaternion.LookRotation(Vector3.up), new Color(1f, 0.55f, 0.15f), 0.34f);
                var em = fireTrail.emission;
                em.rateOverTime = 80f;
            }

            // LIFT OFF THE FLOOR FIRST. This is the fix for "on the ground I don't
            // fly off, in the air I do": launched from a standing position the body
            // starts flush with the street, so the very first landing test could
            // pass before the arc had a chance to lift it, ending the flight on
            // frame one. A launch is a launch - pop clear of the surface, then fly.
            if (launchVelocity.sqrMagnitude > 1f)
            {
                if (useController) cc.Move(Vector3.up * 1.1f);
                else transform.position += Vector3.up * 1.1f;
                if (vel.y < 4f) vel.y = Mathf.Max(vel.y, 4f); // never launch flat into the floor
            }

            // WAIT OUT THE IMPACT FREEZE BEFORE FLYING.
            // The finisher sets timeScale to 0.05 for 0.22 real seconds, so the first
            // frames of the flight ran at dt=0.000 and the body went nowhere while the
            // launch velocity quietly decayed under gravity and drag. Worse, the very
            // next knockdown could restart the routine and reset the measurement -
            // which is exactly what "flew 0.0u" was. The freeze is a deliberate beat:
            // let it finish, THEN rocket away. That is also how it reads in EXVS.
            float freezeGuard = 0f;
            while (Time.timeScale < 0.5f && freezeGuard < 1.5f)
            {
                freezeGuard += Time.unscaledDeltaTime;
                yield return null;
            }

            const float minFlightSeconds = 0.15f; // landing is not even TESTED before this
            int diagFrames = 0;
            Debug.Log("[Down] " + name + " FLIGHT BEGIN  start=" + flightStart.ToString("F2") +
                      "  afterLift=" + transform.position.ToString("F2") +
                      "  launch=" + launchVelocity.ToString("F1") +
                      "  ccEnabled=" + (cc != null && cc.enabled) + "  useController=" + useController);
            flightInProgress = true;
            loggedFlightInterference = false;
            flightAuthoritativePos = transform.position;

            while (t < hardCap && !landed)
            {
                float dt = Time.deltaTime;
                // A stalled or absurd frame (editor hitch, first frame out of the
                // freeze) must not stall the arc or teleport it.
                dt = Mathf.Clamp(dt, 0.005f, 0.05f);

                vel.y += flightGravity * dt;
                if (vel.y < -35f) vel.y = -35f;
                float drag = bigLaunch ? flightDrag * 0.35f : flightDrag; // big flings keep sailing
                vel.x = Mathf.Lerp(vel.x, 0f, dt * drag);
                vel.z = Mathf.Lerp(vel.z, 0f, dt * drag);

                Vector3 before = transform.position;
                // THE MOVE: through the controller, so nothing can revert it and the
                // body collides with the world properly on the way.
                if (useController)
                {
                    CollisionFlags flags = cc.Move(vel * dt);
                    // The controller itself reports the landing - no raycast guessing,
                    // and it works on rooftops, cars and debris identically.
                    if (t > minFlightSeconds && vel.y < 0f && (flags & CollisionFlags.Below) != 0)
                        landed = true;
                    if ((flags & CollisionFlags.Sides) != 0) { vel.x *= 0.5f; vel.z *= 0.5f; } // scraped a wall
                }
                else transform.position += vel * dt;

                // First frames of every fling, in the console: if a launch ever fails
                // again this says whether the velocity, the delta time or something
                // stealing the transform is to blame - no more guessing.
                if (bigLaunch && diagFrames < 3)
                {
                    diagFrames++;
                    Debug.Log("[Down] flight frame " + diagFrames + " dt=" + dt.ToString("F3") +
                              " pos=" + transform.position.ToString("F2") +
                              " (was " + before.ToString("F2") + ")" +
                              " vel=" + vel.ToString("F1") +
                              " moved=" + Vector3.Distance(before, transform.position).ToString("F2") + "u");
                }

                // stay inside the arena walls/ceiling (through the controller too)
                Vector3 boundsFix = ArenaLimits.Correction(transform.position);
                if (boundsFix != Vector3.zero)
                {
                    if (useController) cc.Move(boundsFix);
                    else transform.position += boundsFix;
                }

                // LANDING ON ANY SURFACE, not just y=0. The old test was a hard clamp
                // to the street, so a body flung onto a rooftop was either dragged
                // down through the building or left lying at the wrong height.
                if (!useController && t > minFlightSeconds && vel.y < 0f)
                {
                    float surfaceY;
                    if (SurfaceBelow(transform.position, out surfaceY) && transform.position.y <= surfaceY + 0.08f)
                    {
                        Vector3 p = transform.position; p.y = surfaceY + 0.05f; transform.position = p;
                        landed = true;
                    }
                }

                // Crash through cover: still moving fast + touching a breakable = demolition
                if (bigLaunch && vel.sqrMagnitude > 60f)
                {
                    Collider[] near = Physics.OverlapSphere(transform.position + Vector3.up * 1f, 1.5f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (Collider c in near)
                    {
                        BreakableBuilding bb = c != null ? c.GetComponentInParent<BreakableBuilding>() : null;
                        if (bb != null)
                        {
                            bb.TakeHit(500f, transform.position + Vector3.up * 1f); // flattened outright
                            vel.x *= 0.35f; vel.z *= 0.35f;                          // the crash eats momentum
                            if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.3f, 0.4f);
                            break;
                        }
                    }
                }

                // Hand the frame's final say to LateUpdate: anything that moves this
                // transform after now gets overridden back to here.
                flightAuthoritativePos = transform.position;

                t += dt;
                yield return null;
            }

            flightInProgress = false;
            for (int i = 0; i < rootAnimators.Length; i++)
                rootAnimators[i].applyRootMotion = rootMotionWas[i];
            if (body != null && !bodyWasKinematic) body.isKinematic = false;
            if (agent != null && agentWasEnabled) { agent.Warp(transform.position); agent.enabled = true; }

            // The controller was never disabled this time, so there is no cached
            // position waiting to snap the body back - nothing to undo here.
            if (cc != null && cc.enabled != ccWasEnabled) cc.enabled = ccWasEnabled;

            if (fireTrail != null)
            {
                var em2 = fireTrail.emission;
                em2.rateOverTime = 0f;
                Object.Destroy(fireTrail.gameObject, 0.4f);
            }
            if (bigLaunch) ProceduralVfx.DustPuff(transform.position, 18, 1.3f); // crash-landing

            // Diagnostic: if a fling ever fails again, this one line says why
            float flew = Vector3.Distance(flightStart, transform.position);
            Debug.Log("[Down] " + name + " FLIGHT END  start=" + flightStart.ToString("F2") +
                      "  end=" + transform.position.ToString("F2") +
                      "  frames=" + t.ToString("F2") + "s  landed=" + landed +
                      "  flew " + flew.ToString("F1") + "u");
            if (bigLaunch && flew < 2f)
                Debug.LogWarning("[Down] '" + name + "' was launched but barely moved (" + flew.ToString("F1") +
                                 "u). Read the flight-frame lines above: dt=0 means a time freeze, vel=0 means a bad " +
                                 "launch, moved-then-snapped-back means something still owns this transform.");
        }

        // The down timer starts AFTER landing, so the lie-down reads correctly on the
        // floor. Pitch promise: the PLAYER can hold BOOST (space) to get up faster.
        float requiredDown = downedDuration * durationScale;
        float downT = 0f;
        downedFallSpeed = 0f;
        while (downT < requiredDown)
        {
            if (playerController != null && Keyboard.current != null && Keyboard.current.spaceKey.isPressed)
                requiredDown = downedDuration * durationScale * 0.55f; // fast get-up
            // Keep the body on whatever is under it: if the rooftop it landed on is
            // blown up while it lies there, it drops to the street instead of
            // floating over a hole.
            SettleOntoSurfaceWhileDowned();
            downT += Time.deltaTime;
            yield return null;
        }

        if (animator != null)
        {
            animator.SetTrigger("Recover");
            animator.applyRootMotion = rootMotionWasOn; // restore
        }
        downTiltTarget = 0f; // placeholder pose stands back up
        SetControlScripts(true);

        // CLEAN SLATE ON WAKE-UP. The scripts come back on with whatever state they
        // held when they were knocked down - a stale Attacking/BoostStep state, a
        // combo counter mid-string, hitboxes still live. Reset them here so the mech
        // stands up ready to fight instead of frozen waiting on a routine that was
        // killed at the start of the down.
        if (aiController != null) aiController.CancelAttack();
        if (playerCombat != null) playerCombat.ForceResetAfterDown();
        if (playerController != null) playerController.ForceResetAfterDown();

        inWakeUpProtection = true;
        float protectionTimer = wakeUpProtectionDuration * Mathf.Lerp(0.6f, 1f, durationScale);
        while (protectionTimer > 0f)
        {
            protectionTimer -= Time.deltaTime;
            yield return null;
        }

        inWakeUpProtection = false;
        isYellowLocked = false;
        downedCoroutine = null;
    }

    /// <summary>
    /// Full reset back to a fighting state - used by the tutorial to bring the
    /// practice bot back when the player kills it mid-lesson. Undoes everything
    /// Die()/knockdown set: health, bars, yellow lock, the placeholder tilt pose,
    /// root motion, and the disabled control scripts.
    /// </summary>
    public void Revive()
    {
        if (downedCoroutine != null)
        {
            StopCoroutine(downedCoroutine);
            downedCoroutine = null;
        }

        bool wasTilted = downTilt > 0.01f || downTiltTarget > 0f;
        isYellowLocked = false;
        inWakeUpProtection = false;
        currentHealth = maxHealth;
        currentKnockdownValue = 0f;
        downTiltTarget = 0f;
        downTilt = 0f;

        // Restore the exact pose captured when the tilt started - only if we were
        // actually tilted, so a healthy mech's pose is never touched.
        if (wasTilted && modelTransform != null)
        {
            modelTransform.localRotation = tiltBaseLocalRot;
            modelTransform.localPosition = tiltBaseLocalPos;
        }

        if (animator != null)
        {
            animator.speed = 1f;
            animator.applyRootMotion = rootMotionWasOn;
            animator.ResetTrigger("HitDown");
            animator.ResetTrigger("Die");
            animator.ResetTrigger("GetHit");
            if (wasTilted) animator.SetTrigger("Recover");
        }

        SetControlScripts(true);
    }

    /// <summary>Height of the first solid surface under a position - a rooftop, a
    /// car, or the street. Returns false when there is nothing below at all (then
    /// the caller falls back to the street at y=0).</summary>
    /// <summary>Pins the mech at its landing spot for a few frames after the flight.
    /// Re-enabling a CharacterController restores its cached position over the
    /// following update, so without this the body teleports back to where it was
    /// launched from a frame or two after landing.</summary>
    private IEnumerator HoldPositionAfterFlight(Vector3 pos, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            yield return null;
            if (isYellowLocked && (transform.position - pos).sqrMagnitude > 0.01f)
            {
                if (cc != null && cc.enabled)
                {
                    cc.enabled = false;
                    transform.position = pos;
                    cc.enabled = true;
                }
                else transform.position = pos;
                Physics.SyncTransforms();
            }
        }
    }

    private bool SurfaceBelow(Vector3 pos, out float surfaceY)
    {
        surfaceY = 0.05f;
        // RaycastAll, not Raycast: the ray starts INSIDE this mech's own capsule, so
        // a single Raycast almost always returns the mech itself and the real surface
        // below was never found. Take the highest hit that is not our own body.
        RaycastHit[] hits = Physics.RaycastAll(pos + Vector3.up * 0.6f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore);
        bool found = false;
        float best = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform.root == transform.root) continue; // self
            if (hits[i].point.y > best) { best = hits[i].point.y; found = true; }
        }
        if (found) { surfaceY = best; return true; }
        return pos.y <= 0.06f; // nothing under us: only the street counts as ground
    }

    /// <summary>While downed, keep the body sitting on whatever is actually beneath
    /// it. If the building it landed on gets destroyed mid-down, it falls to the
    /// street instead of lying in mid-air over a hole.</summary>
    private void SettleOntoSurfaceWhileDowned()
    {
        float surfaceY;
        bool hasSurface = SurfaceBelow(transform.position, out surfaceY);
        float target = hasSurface ? surfaceY + 0.05f : 0.05f;
        if (transform.position.y > target + 0.1f)
        {
            // fall - accelerating, capped, so a collapsing rooftop drops the body
            downedFallSpeed = Mathf.Min(downedFallSpeed + 30f * Time.deltaTime, 35f);
            float step = downedFallSpeed * Time.deltaTime;
            Vector3 p = transform.position;
            p.y = Mathf.Max(target, p.y - step);
            MoveDownedTo(p);
        }
        else
        {
            downedFallSpeed = 0f;
            if (transform.position.y < target - 0.1f)
            {
                Vector3 p = transform.position; p.y = target; MoveDownedTo(p);
            }
        }
    }
    private float downedFallSpeed;

    // Moving a downed body means going around the CharacterController: it is back
    // on and would re-assert its cached position the moment it is touched.
    private void MoveDownedTo(Vector3 pos)
    {
        if (cc != null && cc.enabled)
        {
            cc.enabled = false;
            transform.position = pos;
            cc.enabled = true;
        }
        else transform.position = pos;
    }

    private void SetControlScripts(bool enabled)
    {
        if (aiController != null) aiController.enabled = enabled;
        if (playerController != null) playerController.enabled = enabled;
        if (playerCombat != null) playerCombat.enabled = enabled;
    }

    // Acting during wake-up invincibility forfeits it — but only during the protection
    // window after standing up, never while still lying on the floor.
    public void BreakWakeUpProtection()
    {
        // The burst escape is not wake-up protection - attacking out of it is the
        // entire point, so acting must not cancel it.
        if (InBurstInvulnerability) return;

        if (inWakeUpProtection && isYellowLocked && currentHealth > 0)
        {
            isYellowLocked = false;
            inWakeUpProtection = false;
            if (downedCoroutine != null)
            {
                StopCoroutine(downedCoroutine);
                downedCoroutine = null;
            }
        }
    }

    private void Die()
    {
        if (downedCoroutine != null) StopCoroutine(downedCoroutine);

        if (animator != null)
        {
            animator.speed = 1f;
            animator.ResetTrigger("GetHit");
            animator.SetTrigger("Die");
        }

        // No Die state exists yet either — the placeholder tilt shows the death too
        if (modelTransform != null && downTilt <= 0.01f)
        {
            tiltBaseLocalRot = modelTransform.localRotation;
            tiltBaseLocalPos = modelTransform.localPosition;
        }
        if (animator != null) animator.applyRootMotion = false; // stays off — it's dead
        downTiltTarget = 90f;

        SetControlScripts(false);
        isYellowLocked = true;

        // Wire the destruction into the team cost pool (EXVS win condition)
        if (CostManager.Instance != null)
        {
            CostManager.Instance.DeductCost(team, unitCost);
        }
    }
}
