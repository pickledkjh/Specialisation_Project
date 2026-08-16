using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
// Text goes through UiLabel: TextMeshPro when imported, sharp legacy fallback otherwise

/// <summary>
/// Press T to start the tutorial. Walks through every mechanic step by step and only
/// advances when the player actually performs each one. The enemy AI goes passive
/// while the tutorial is running. Press T again to quit.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    private MechController player;
    private MechShooter shooter;
    private MechCombat combat;
    private SimpleMechAI enemyAI;
    private MechHealth enemyHealth;

    private InputAction toggleAction, skipAction;
    private float skipHeld;
    private UiLabel skipLabel;
    private RectTransform skipFill;
    private bool active = false;
    private int step = -1;
    private float stepDoneAt = -1f; // "done" flash timing

    // Read/driven by GameFlowManager
    public bool IsActive => active;
    public bool CompletedOnce { get; private set; }
    public void SetTutorialActive(bool on) { SetActiveState(on); }

    private UiLabel titleText, bodyText, hintText;
    private GameObject panel;
    private UiLabel stepCounter;
    private RectTransform progressFill;
    private Image progressImg;

    // detection state
    private Vector3 movedOrigin;
    private float movedDistance;
    private float riseBaseY;
    private float dashHeld, shieldHeld;
    private bool interactableStaged, staging, aiWasPassive;
    private Image fadeImg;
    private Transform fadeCanvas;
    private const float SkipHoldSeconds = 0.7f;
    private const string SkipPrompt = "Stuck?  hold BACKSPACE to skip";
    private bool sawTackle, sawLaser, sawFunnels, sawThrow, sawBurst;
    private int interactableBaseline = -1;
    private SpecialMoves specials;
    private bool sawLanding, sawStep, sawStepCancel;
    private int ammoBaseline;
    private float enemyHpBaseline;
    private MechState prevPlayerState;
    private int blocksBaseline;
    private float blockAttackAt = -1f; // when the AI's practice attack lands
    // lock-range drill
    private int lastAmmoSeen;
    private bool firedGreenLock, firedRedLock;
    // target-switch drill
    private GameObject practiceDrone;
    private MechHealth practiceDroneHealth;
    private readonly System.Collections.Generic.HashSet<Transform> lockedSoFar =
        new System.Collections.Generic.HashSet<Transform>();
    private int tabSwitches;
    private float lastSwitchSeen = -99f;

    private struct Step { public string title; public string body; }
    private static readonly Step[] Steps = new Step[]
    {
        new Step { title = "MOVEMENT",    body = "Hold W A S D to move. Your mech always faces the enemy - S backs away, A/D circle around them. Move around a bit." },
        new Step { title = "RISE",        body = "Hold SPACE to boost upward. Rising spends boost gauge (bottom bar). Get some height." },
        new Step { title = "BOOST DASH",  body = "Press and HOLD SHIFT to boost dash. Watch the boost gauge - dashing drains it. Dash for a moment." },
        new Step { title = "LANDING LAG", body = "Release the dash and land. Notice the landing lag - landing with LOW boost means a LONG helpless moment. Manage your gauge!" },
        new Step { title = "BOOST STEP",  body = "Double-tap A or D (or W/S) quickly to boost step - a fast dodge that evades attacks and homing shots. Do one." },
        new Step { title = "SHOOT",       body = "Tap RIGHT CLICK to fire. Inside 40 units (red ring) shots curve toward the enemy. Fire a shot." },
        new Step { title = "LOCK RANGES", body = "Your shots change with distance - watch the live readout below and fire ONE shot in each lock." },
        new Step { title = "TARGET SWITCH",body = "A second contact has been deployed. Press TAB to move your lock between them - the circle jumps to whoever you switch to, and every shot, melee rush and special follows it. This is how you pick your target in a 2v2. Lock BOTH of them." },
        new Step { title = "CHARGE SHOT", body = "HOLD right click for 1 second, then RELEASE - a heavy charge shot (costs 2 ammo, huge knockdown power). Fire one." },
        new Step { title = "MELEE",       body = "LEFT CLICK near the enemy to melee - from range your mech rushes in automatically. Keep clicking to chain the combo. Land a few hits." },
        new Step { title = "RAINBOW STEP",body = "Mid-combo, double-tap A or D to boost step CANCEL the string, then melee again for a fresh combo. This is the core melee mixup - cancelling early trades damage for safety. Do a step-cancel during a combo." },
        new Step { title = "SHIELD",      body = "Hold Q to raise your shield. It blocks frontal melee and shots, drains boost, and a BLOCKED melee stuns the attacker for a free punish. Hold it a moment." },
        new Step { title = "BLOCK",       body = "Now use it for real. Hold Q and FACE the enemy - it is going to attack you!" },
        new Step { title = "KNOCKDOWN",   body = "Every hit fills the enemy's knockdown bar (under their name plate). Fill it - finish a full combo or mix shots and strings - and knock the enemy DOWN. A full combo fills about half, so you have to mix." },
        new Step { title = "DASH TACKLE",  body = "Hold SHIFT to boost dash, then press F: a shoulder charge that keeps your dash speed. It has a long cooldown - watch its slot on the right. Land one." },
        new Step { title = "GIANT LASER",  body = "Press E. Your mech rises into a hover and fires a beam that detonates into an expanding blast sphere - anything caught inside is held and floored. Long cooldown. Fire one." },
        new Step { title = "MISSILES",     body = "Press R for the homing missile barrage. They curve toward the target, but a boost step still breaks their tracking. Fire a salvo." },
        new Step { title = "INTERACTABLES",body = "The city is a weapon. Walk up to a TREE, FUEL BARREL, CAR WRECK or ANTENNA MAST until the green prompt appears, then press G to hurl it. Each does something different - the car alone floors an enemy outright. Throw one." },
        new Step { title = "AWAKENING",    body = "Dealing and taking damage fills the AWAKENING gauge (bottom left). At half full, press B: full boost, every cooldown refreshed, faster cooldowns and bonus damage for a while. Use it." },
    };

    private void Start()
    {
        player = FindFirstObjectByType<MechController>();
        if (player != null)
        {
            shooter = player.GetComponent<MechShooter>();
            combat = player.GetComponent<MechCombat>();
            specials = player.GetComponent<SpecialMoves>();
        }
        enemyAI = FindFirstObjectByType<SimpleMechAI>();
        if (enemyAI != null) enemyHealth = enemyAI.GetComponent<MechHealth>();

        toggleAction = new InputAction("TutorialToggle", InputActionType.Button);
        toggleAction.AddBinding("<Keyboard>/t");
        toggleAction.Enable();

        // SKIP: nobody should be stuck on a lesson they cannot land. Backspace, or
        // the on-screen button, moves on. Held for a moment so it is never a
        // mis-tap - a bar fills on the card while you hold it.
        skipAction = new InputAction("TutorialSkip", InputActionType.Button);
        skipAction.AddBinding("<Keyboard>/backspace");
        skipAction.Enable();

        BuildUi();
        SetActiveState(false);
    }

    private void OnDestroy()
    {
        if (toggleAction != null) toggleAction.Disable();
        if (skipAction != null) skipAction.Disable();
    }

    /// <summary>Give up on this lesson and move to the next one.</summary>
    public void SkipCurrentStep()
    {
        if (!active || step >= Steps.Length) return;
        if (bodyText != null) bodyText.text = "SKIPPED - moving on.";
        if (titleText != null) titleText.color = new Color(1f, 0.8f, 0.35f);
        stepDoneAt = Time.time;
        skipHeld = 0f;
    }

    private void Update()
    {
        if (toggleAction.WasPressedThisFrame())
        {
            SetActiveState(!active);
        }

        if (!active || player == null) return;

        if (step >= Steps.Length) return; // finished, showing the completion panel

        // ---- hold BACKSPACE to skip the current lesson ----
        if (skipAction != null && skipAction.IsPressed() && stepDoneAt < 0f)
        {
            skipHeld += Time.deltaTime;
            if (skipFill != null) skipFill.localScale = new Vector3(Mathf.Clamp01(skipHeld / SkipHoldSeconds), 1f, 1f);
            if (skipLabel != null) skipLabel.text = "KEEP HOLDING TO SKIP...";
            if (skipHeld >= SkipHoldSeconds) { SkipCurrentStep(); return; }
        }
        else if (skipHeld > 0f)
        {
            skipHeld = 0f;
            if (skipFill != null) skipFill.localScale = new Vector3(0f, 1f, 1f);
            if (skipLabel != null) skipLabel.text = SkipPrompt;
        }

        // brief "done" flash between steps
        if (stepDoneAt > 0f)
        {
            if (Time.time - stepDoneAt > 0.9f) AdvanceStep();
            return;
        }

        // Practice-bot watchdog: if the player KILLS the bot mid-lesson, bring it
        // straight back - a dead sparring partner would strand the remaining steps
        // (and the knockdown finale needs a live victim). Runs BEFORE the step
        // check so a kill can never masquerade as a completed knockdown.
        if (enemyHealth != null && enemyHealth.currentHealth <= 0f)
        {
            enemyHealth.Revive();
            enemyHpBaseline = enemyHealth.currentHealth; // re-baseline the melee-damage step
            if (bodyText != null && stepDoneAt < 0f)
                bodyText.text = Steps[step].body + "\n(The practice bot respawned - it can't stay dead during the lesson!)";
        }

        if (CheckCurrentStep()) CompleteStep();

        prevPlayerState = player.currentState;
    }

    private bool CheckCurrentStep()
    {
        switch (step)
        {
            case 0: // move
                movedDistance += new Vector3(player.transform.position.x - movedOrigin.x, 0f,
                                             player.transform.position.z - movedOrigin.z).magnitude;
                movedOrigin = player.transform.position;
                return movedDistance > 8f;
            case 1: // rise
                // Height-GAIN based, not a raw velocity threshold - the old
                // "velocity.y > 5" check silently broke whenever ascendSpeed was
                // tuned below it. Gaining ~1.5u of altitude while moving upward
                // counts, at any rise speed.
                riseBaseY = Mathf.Min(riseBaseY, player.transform.position.y);
                return player.velocity.y > 0.05f &&
                       player.transform.position.y - riseBaseY > 1.5f;
            case 2: // dash
                if (player.currentState == MechState.BoostDash) dashHeld += Time.deltaTime;
                return dashHeld > 0.8f;
            case 3: // landing lag
                if (player.currentState == MechState.Landing) sawLanding = true;
                return sawLanding && player.currentState == MechState.Grounded;
            case 4: // boost step
                if (player.currentState == MechState.BoostStep) sawStep = true;
                return sawStep;
            case 5: // shoot
                return shooter != null && shooter.currentAmmo < ammoBaseline;
            case 6: // lock ranges: one shot fired in GREEN lock, one in RED lock
            {
                if (shooter == null || enemyAI == null) return true; // can't detect - skip

                float dist = Vector3.Distance(player.transform.position, enemyAI.transform.position);
                bool inRedLock = dist <= shooter.redLockRange;

                if (shooter.currentAmmo < lastAmmoSeen)
                {
                    if (inRedLock) firedRedLock = true; else firedGreenLock = true;
                }
                if (shooter.currentAmmo != lastAmmoSeen) lastAmmoSeen = shooter.currentAmmo; // also tracks reloads

                // Live readout: current distance + lock color + EXACTLY what to do next.
                if (bodyText != null && stepDoneAt < 0f)
                {
                    string doNow;
                    if (!firedGreenLock && !firedRedLock)
                        doNow = inRedLock
                            ? "You are CLOSE: shots will CURVE. Fire one shot HERE, then back away past 40 and fire again."
                            : "You are FAR: shots fly STRAIGHT. Fire one shot HERE, then move inside 40 and fire again.";
                    else if (!firedGreenLock)
                        doNow = "Good! Now BACK AWAY (hold S / dash out) until the distance is OVER 40, then fire once.";
                    else
                        doNow = "Good! Now GET CLOSE - distance UNDER 40 - then fire once.";

                    bodyText.text =
                        "Distance: " + Mathf.RoundToInt(dist) + "  ->  " +
                        (inRedLock ? "RED LOCK (shots curve at the enemy)" : "GREEN LOCK (shots fly straight)") + "\n" +
                        doNow + "\n" +
                        "Green-lock shot: " + (firedGreenLock ? "DONE" : "todo") +
                        "     Red-lock shot: " + (firedRedLock ? "DONE" : "todo") +
                        "     (YELLOW lock = downed mech, untargetable)";
                }
                return firedGreenLock && firedRedLock;
            }
            case 7: // TARGET SWITCH: lock both contacts with TAB
            {
                if (TargetSwitcher.Instance == null) return true; // can't detect - skip

                // A drill about choosing between targets needs two of them. The real
                // enemy is alone in the tutorial, so a passive practice drone is
                // parked beside it for the duration of this one lesson.
                if (practiceDrone == null) SpawnPracticeDrone();

                // Unkillable on purpose: shooting the drone down mid-drill would
                // leave one target and make the step impossible to finish.
                if (practiceDroneHealth != null)
                {
                    practiceDroneHealth.currentHealth = practiceDroneHealth.maxHealth;
                    practiceDroneHealth.currentKnockdownValue = 0f;
                }

                Transform locked = TargetSwitcher.Instance.Current;
                if (locked != null) lockedSoFar.Add(locked);

                if (TargetSwitcher.Instance.LastSwitchAt > lastSwitchSeen + 0.01f)
                {
                    lastSwitchSeen = TargetSwitcher.Instance.LastSwitchAt;
                    tabSwitches++;
                }

                if (bodyText != null && stepDoneAt < 0f)
                {
                    string who = locked != null ? locked.name : "nothing";
                    bodyText.text =
                        "Locked on: " + who + "\n" +
                        "Press TAB to switch to the other contact. Your shots, your melee rush " +
                        "and your specials all follow the lock - switching IS how you choose who to fight.\n" +
                        "Contacts locked: " + lockedSoFar.Count + " / 2" +
                        "     TAB presses: " + tabSwitches;
                }
                return lockedSoFar.Count >= 2 && tabSwitches >= 1;
            }

            case 8: // charge shot (costs 2 ammo in one go)
                if (shooter == null) return true; // can't detect - skip
                if (shooter.currentAmmo <= ammoBaseline - 2) return true;
                if (shooter.currentAmmo > ammoBaseline) ammoBaseline = shooter.currentAmmo; // reloaded
                return false;
            case 9: // melee hits (~2-3 punches of damage)
                return enemyHealth != null && enemyHealth.currentHealth <= enemyHpBaseline - 35f;
            case 10: // rainbow step: Attacking -> BoostStep transition
                if (prevPlayerState == MechState.Attacking && player.currentState == MechState.BoostStep)
                    sawStepCancel = true;
                return sawStepCancel;
            case 11: // shield
                if (combat != null && combat.IsShielding) shieldHeld += Time.deltaTime;
                return shieldHeld > 1.0f;
            case 12: // block a real attack (countdown, then the AI melees for real)
                if (combat != null && combat.blocksLanded > blocksBaseline) return true;
                if (blockAttackAt < 0f) blockAttackAt = Time.time + 3.5f;
                float remain = blockAttackAt - Time.time;
                if (remain > 0f)
                {
                    if (bodyText != null)
                        bodyText.text = "Hold Q and FACE the enemy - attack incoming in " + Mathf.CeilToInt(remain) + "...";
                }
                else
                {
                    if (remain > -0.15f && enemyAI != null) enemyAI.TutorialAttackNow();
                    if (bodyText != null) bodyText.text = "BLOCK IT! (hold Q)";
                    if (remain < -6f) blockAttackAt = -1f; // missed - run the countdown again
                }
                return false;
            case 13: // knockdown (health guard: a KILL revives the bot instead of passing)
                return enemyHealth != null && enemyHealth.isYellowLocked && enemyHealth.currentHealth > 0f;

            case 14: // dash tackle: its cooldown bar drops from full = it was spent
                if (combat == null) return true;
                if (combat.TackleReady01 < 0.95f) sawTackle = true;
                return sawTackle;

            case 15: // giant laser
                if (specials == null) return true;
                if (specials.LaserReady01 < 0.95f) sawLaser = true;
                return sawLaser;

            case 16: // missile barrage
                if (specials == null) return true;
                if (specials.FunnelReady01 < 0.95f) sawFunnels = true;
                return sawFunnels;

            case 17: // interactable thrown (the live count drops when one is used)
                // Stage the lesson the first time we reach it: black out, clear the
                // map, lay one of each kind in front of the player, freeze the AI,
                // fade back in. Hoping the random scatter put something nearby was
                // not good enough for a lesson that has to be completable.
                if (!interactableStaged)
                {
                    interactableStaged = true;
                    StartCoroutine(StageInteractableLesson());
                    return false;
                }
                if (staging) return false; // mid-fade: nothing to detect yet
                if (InteractableObject.All == null) return true;
                {
                    int aliveNow = 0;
                    foreach (InteractableObject io in InteractableObject.All)
                        if (io != null && io.enabled) aliveNow++;
                    if (interactableBaseline < 0) interactableBaseline = aliveNow;
                    if (aliveNow < interactableBaseline) sawThrow = true;
                    if (aliveNow > interactableBaseline) interactableBaseline = aliveNow; // restocks
                }
                return sawThrow;

            case 18: // awakening burst
                if (AwakeningSystem.Instance == null) return true;
                if (AwakeningSystem.Instance.IsBurstActive) sawBurst = true;
                return sawBurst;
        }
        return false;
    }

    /// <summary>
    /// A stand-in second contact for the TAB drill: a clone of the enemy, parked and
    /// passive. It is torn down the moment the lesson ends so nothing follows the
    /// player into the real fight.
    /// </summary>
    private void SpawnPracticeDrone()
    {
        if (enemyAI == null || enemyHealth == null || player == null) return;

        Vector3 axis = enemyAI.transform.position - player.transform.position;
        axis.y = 0f;
        axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, axis);
        Vector3 pos = enemyAI.transform.position + side * 14f;

        practiceDrone = Instantiate(enemyAI.gameObject, pos, Quaternion.LookRotation(-axis));
        practiceDrone.name = "PRACTICE DRONE";

        CharacterController cc = practiceDrone.GetComponent<CharacterController>();
        if (cc != null)
        {
            cc.enabled = false;
            practiceDrone.transform.position = pos;
            cc.enabled = true;
        }

        practiceDroneHealth = practiceDrone.GetComponent<MechHealth>();
        if (practiceDroneHealth != null)
        {
            practiceDroneHealth.team = enemyHealth.team;   // an opponent, so TAB can reach it
            practiceDroneHealth.currentHealth = practiceDroneHealth.maxHealth;
            practiceDroneHealth.currentKnockdownValue = 0f;
        }

        SimpleMechAI droneAI = practiceDrone.GetComponent<SimpleMechAI>();
        if (droneAI != null)
        {
            droneAI.passiveMode = true;   // it is a target, not a second opponent
            droneAI.isDead = false;
            droneAI.CancelAttack();
        }

        BattleRoster.Invalidate();
    }

    private void ClearPracticeDrone()
    {
        if (practiceDrone != null) Destroy(practiceDrone);
        practiceDrone = null;
        practiceDroneHealth = null;
        BattleRoster.Invalidate();
    }

    /// <summary>Fade to black, rebuild the lesson area, fade back. The AI is parked
    /// in passive mode for the duration so the player can practise without being
    /// shot at, and released when the step is done.</summary>
    private System.Collections.IEnumerator StageInteractableLesson()
    {
        staging = true;

        // fade OUT
        if (fadeImg == null) BuildFade();
        yield return Fade(0f, 1f, 0.35f);

        // rebuild the area while the screen is black
        if (enemyAI != null)
        {
            aiWasPassive = enemyAI.passiveMode;
            enemyAI.passiveMode = true;          // stop it attacking during the lesson
            enemyAI.CancelAttack();
        }
        if (player != null) InteractableManager.StageTutorialSet(player.transform);
        yield return new WaitForSeconds(0.25f);

        // fade IN
        yield return Fade(1f, 0f, 0.45f);
        staging = false;
    }

    private System.Collections.IEnumerator Fade(float from, float to, float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            if (fadeImg != null)
                fadeImg.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / seconds));
            yield return null;
        }
        if (fadeImg != null) fadeImg.color = new Color(0f, 0f, 0f, to);
    }

    private void BuildFade()
    {
        GameObject go = new GameObject("Tutorial Fade");
        go.transform.SetParent(fadeCanvas != null ? fadeCanvas : transform, false);
        fadeImg = go.AddComponent<Image>();
        fadeImg.color = new Color(0f, 0f, 0f, 0f);
        fadeImg.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        go.transform.SetAsLastSibling();
    }

    private void CompleteStep()
    {
        stepDoneAt = Time.time;
        if (bodyText != null) bodyText.text = "NICE!";
        if (titleText != null) titleText.color = new Color(0.45f, 1f, 0.55f); // flash green
        if (progressImg != null) progressImg.color = new Color(0.45f, 1f, 0.55f, 0.95f);
        if (progressFill != null)
            progressFill.localScale = new Vector3((float)(step + 1) / Mathf.Max(1, Steps.Length), 1f, 1f);
        BattleAudio.Play("block", 0.5f, 1.6f); // small confirmation chime
    }

    private void AdvanceStep()
    {
        stepDoneAt = -1f;
        step++;
        // Leaving the target-switch drill: the practice drone goes with it. This has
        // to happen BEFORE the completion early-return below, or finishing on that
        // step would walk the dummy into the real fight.
        if (step > 7) ClearPracticeDrone();
        if (step >= Steps.Length)
        {
            CompletedOnce = true;
            if (titleText != null) { titleText.text = "TUTORIAL COMPLETE"; titleText.color = new Color(0.45f, 1f, 0.55f); }
            if (bodyText != null) bodyText.text = "That is the whole kit. Get ready...";
            if (stepCounter != null) stepCounter.text = Steps.Length + " / " + Steps.Length;
            if (progressFill != null) progressFill.localScale = Vector3.one;
            return;
        }
        // Leaving the interactables lesson: give the AI its aggression back
        if (step == 18 && enemyAI != null) enemyAI.passiveMode = aiWasPassive;

        ResetDetection();
        if (titleText != null) titleText.color = new Color(0.55f, 0.9f, 1f);   // back to cyan
        if (progressImg != null) progressImg.color = new Color(0.35f, 0.85f, 1f, 0.9f);
        ShowStep();
    }

    private void ResetDetection()
    {
        movedOrigin = player != null ? player.transform.position : Vector3.zero;
        movedDistance = 0f;
        riseBaseY = player != null ? player.transform.position.y : 0f;
        dashHeld = 0f;
        shieldHeld = 0f;
        sawTackle = sawLaser = sawFunnels = sawThrow = sawBurst = false;
        interactableBaseline = -1;
        if (specials == null && player != null) specials = player.GetComponent<SpecialMoves>();
        sawLanding = false;
        sawStep = false;
        sawStepCancel = false;
        ammoBaseline = shooter != null ? shooter.currentAmmo : 0;
        enemyHpBaseline = enemyHealth != null ? enemyHealth.currentHealth : 0f;
        blocksBaseline = combat != null ? combat.blocksLanded : 0;
        blockAttackAt = -1f;
        lastAmmoSeen = shooter != null ? shooter.currentAmmo : 0;
        firedGreenLock = false;
        firedRedLock = false;
        lockedSoFar.Clear();
        tabSwitches = 0;
        lastSwitchSeen = TargetSwitcher.Instance != null ? TargetSwitcher.Instance.LastSwitchAt : -99f;
    }

    private void ShowStep()
    {
        // Title is now just the lesson name - the counter and the bar carry progress
        if (titleText != null) titleText.text = Steps[step].title;
        if (bodyText != null) bodyText.text = Steps[step].body;
        if (stepCounter != null) stepCounter.text = "STEP " + (step + 1) + " / " + Steps.Length;
        if (skipLabel != null) skipLabel.text = SkipPrompt;
        if (skipFill != null) skipFill.localScale = new Vector3(0f, 1f, 1f);
        skipHeld = 0f;
        if (progressFill != null)
            progressFill.localScale = new Vector3((float)step / Mathf.Max(1, Steps.Length), 1f, 1f);
    }

    private void SetActiveState(bool on)
    {
        active = on;
        if (!on) ClearPracticeDrone(); // never let the drill's dummy survive into a real fight
        if (panel != null && panel.transform.parent != null)
            panel.transform.parent.gameObject.SetActive(on); // the frame owns the card
        else if (panel != null) panel.SetActive(on);
        if (hintText != null) hintText.gameObject.SetActive(!on);
        if (enemyAI != null) enemyAI.passiveMode = on; // AI strafes but never attacks during the lesson

        if (on)
        {
            step = 0;
            stepDoneAt = -1f;
            prevPlayerState = player != null ? player.currentState : MechState.Grounded;
            ResetDetection();
            ShowStep();
        }
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("Tutorial Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20; // above the battle HUD
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // ---- instruction card (top-center) ----
        // A framed card instead of a flat black rectangle: outer frame, dark inner
        // fill, cyan accent strip, step counter and a progress bar. This is the
        // first thing anyone sees when they press play, so it should look designed.
        GameObject frame = new GameObject("Instruction Frame");
        frame.transform.SetParent(canvasGo.transform, false);
        Image frameImg = frame.AddComponent<Image>();
        frameImg.color = new Color(0.62f, 0.78f, 1f, 0.5f);
        RectTransform frt2 = frame.GetComponent<RectTransform>();
        frt2.anchorMin = frt2.anchorMax = new Vector2(0.5f, 1f);
        frt2.pivot = new Vector2(0.5f, 1f);
        frt2.anchoredPosition = new Vector2(0f, -34f);
        frt2.sizeDelta = new Vector2(1000f, 132f);

        panel = new GameObject("Instruction Panel");
        panel.transform.SetParent(frame.transform, false);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.02f, 0.04f, 0.08f, 0.9f);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = new Vector2(2f, 2f);
        prt.offsetMax = new Vector2(-2f, -2f);

        GameObject accent = new GameObject("Accent");
        accent.transform.SetParent(panel.transform, false);
        Image accentImg = accent.AddComponent<Image>();
        accentImg.color = new Color(0.35f, 0.85f, 1f, 0.95f);
        RectTransform art = accent.GetComponent<RectTransform>();
        art.anchorMin = new Vector2(0f, 0f);
        art.anchorMax = new Vector2(0f, 1f);
        art.pivot = new Vector2(0f, 0.5f);
        art.anchoredPosition = Vector2.zero;
        art.sizeDelta = new Vector2(5f, 0f);

        titleText = UiLabel.Create(panel.transform, "Title", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                   new Vector2(24f, -12f), new Vector2(700f, 34f), 27, true, TextAnchor.UpperLeft);
        titleText.color = new Color(0.55f, 0.9f, 1f);
        bodyText = UiLabel.Create(panel.transform, "Body", new Vector2(0f, 1f), new Vector2(0f, 1f),
                                  new Vector2(24f, -48f), new Vector2(950f, 66f), 19, false, TextAnchor.UpperLeft);

        stepCounter = UiLabel.Create(panel.transform, "Step", new Vector2(1f, 1f), new Vector2(1f, 1f),
                                     new Vector2(-20f, -12f), new Vector2(220f, 28f), 18, true, TextAnchor.UpperRight);
        stepCounter.color = new Color(0.75f, 0.85f, 1f, 0.85f);

        GameObject barBg = new GameObject("Progress Bg");
        barBg.transform.SetParent(panel.transform, false);
        Image barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(1f, 1f, 1f, 0.12f);
        RectTransform bbrt = barBg.GetComponent<RectTransform>();
        bbrt.anchorMin = new Vector2(0f, 0f);
        bbrt.anchorMax = new Vector2(1f, 0f);
        bbrt.pivot = new Vector2(0f, 0f);
        bbrt.offsetMin = new Vector2(6f, 6f);
        bbrt.offsetMax = new Vector2(-6f, 11f);

        // ---- skip prompt (bottom-right of the card) with its own hold-fill ----
        skipLabel = UiLabel.Create(panel.transform, "Skip", new Vector2(1f, 0f), new Vector2(1f, 0f),
                                   new Vector2(-20f, 16f), new Vector2(420f, 24f), 15, false, TextAnchor.LowerRight);
        skipLabel.text = SkipPrompt;
        skipLabel.color = new Color(1f, 0.85f, 0.45f, 0.8f);

        GameObject skipBg = new GameObject("Skip Hold Bg");
        skipBg.transform.SetParent(panel.transform, false);
        Image skipBgImg = skipBg.AddComponent<Image>();
        skipBgImg.color = new Color(1f, 0.8f, 0.35f, 0.15f);
        RectTransform sbrt = skipBg.GetComponent<RectTransform>();
        sbrt.anchorMin = sbrt.anchorMax = new Vector2(1f, 0f);
        sbrt.pivot = new Vector2(1f, 0f);
        sbrt.anchoredPosition = new Vector2(-20f, 14f);
        sbrt.sizeDelta = new Vector2(220f, 3f);

        GameObject skipFillGo = new GameObject("Skip Hold Fill");
        skipFillGo.transform.SetParent(skipBg.transform, false);
        Image skipFillImg = skipFillGo.AddComponent<Image>();
        skipFillImg.color = new Color(1f, 0.8f, 0.35f, 0.95f);
        skipFill = skipFillGo.GetComponent<RectTransform>();
        skipFill.anchorMin = Vector2.zero;
        skipFill.anchorMax = Vector2.one;
        skipFill.offsetMin = Vector2.zero;
        skipFill.offsetMax = Vector2.zero;
        skipFill.pivot = new Vector2(0f, 0.5f);
        skipFill.localScale = new Vector3(0f, 1f, 1f);

        GameObject barFill = new GameObject("Progress Fill");
        barFill.transform.SetParent(barBg.transform, false);
        progressImg = barFill.AddComponent<Image>();
        progressImg.color = new Color(0.35f, 0.85f, 1f, 0.9f);
        progressFill = barFill.GetComponent<RectTransform>();
        progressFill.anchorMin = Vector2.zero;
        progressFill.anchorMax = Vector2.one;
        progressFill.offsetMin = Vector2.zero;
        progressFill.offsetMax = Vector2.zero;
        progressFill.pivot = new Vector2(0f, 0.5f);
        progressFill.localScale = new Vector3(0f, 1f, 1f);

        // idle hint (bottom-left, only while the tutorial is off)
        hintText = UiLabel.Create(canvasGo.transform, "Hint", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                  new Vector2(40f, 16f), new Vector2(300f, 26f), 18, false, TextAnchor.MiddleLeft);
        fadeCanvas = canvasGo.transform;
        hintText.text = "Press T - Tutorial";
        hintText.color = new Color(1f, 1f, 1f, 0.45f);
    }
}

/// <summary>Spawns the tutorial system at play start. Delete the file to remove.</summary>
public static class TutorialBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<TutorialManager>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Tutorial").AddComponent<TutorialManager>();
    }
}
