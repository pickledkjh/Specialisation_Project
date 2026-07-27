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

    private InputAction toggleAction;
    private bool active = false;
    private int step = -1;
    private float stepDoneAt = -1f; // "done" flash timing

    // Read/driven by GameFlowManager
    public bool IsActive => active;
    public bool CompletedOnce { get; private set; }
    public void SetTutorialActive(bool on) { SetActiveState(on); }

    private UiLabel titleText, bodyText, hintText;
    private GameObject panel;

    // detection state
    private Vector3 movedOrigin;
    private float movedDistance;
    private float riseBaseY;
    private float dashHeld, shieldHeld;
    private bool sawLanding, sawStep, sawStepCancel;
    private int ammoBaseline;
    private float enemyHpBaseline;
    private MechState prevPlayerState;
    private int blocksBaseline;
    private float blockAttackAt = -1f; // when the AI's practice attack lands
    // lock-range drill
    private int lastAmmoSeen;
    private bool firedGreenLock, firedRedLock;

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
        new Step { title = "CHARGE SHOT", body = "HOLD right click for 1 second, then RELEASE - a heavy charge shot (costs 2 ammo, huge knockdown power). Fire one." },
        new Step { title = "MELEE",       body = "LEFT CLICK near the enemy to melee - from range your mech rushes in automatically. Keep clicking to chain the combo. Land a few hits." },
        new Step { title = "RAINBOW STEP",body = "Mid-combo, double-tap A or D to boost step CANCEL the string, then melee again for a fresh combo. This is the core EXVS mixup. Do a step-cancel during a combo." },
        new Step { title = "SHIELD",      body = "Hold Q to raise your shield. It blocks frontal melee and shots, drains boost, and a BLOCKED melee stuns the attacker for a free punish. Hold it a moment." },
        new Step { title = "BLOCK",       body = "Now use it for real. Hold Q and FACE the enemy - it is going to attack you!" },
        new Step { title = "KNOCKDOWN",   body = "Every hit fills the enemy's yellow bar (top right). Fill it - finish a full combo or mix shots and strings - and knock the enemy DOWN." },
    };

    private void Start()
    {
        player = FindFirstObjectByType<MechController>();
        if (player != null)
        {
            shooter = player.GetComponent<MechShooter>();
            combat = player.GetComponent<MechCombat>();
        }
        enemyAI = FindFirstObjectByType<SimpleMechAI>();
        if (enemyAI != null) enemyHealth = enemyAI.GetComponent<MechHealth>();

        toggleAction = new InputAction("TutorialToggle", InputActionType.Button);
        toggleAction.AddBinding("<Keyboard>/t");
        toggleAction.Enable();

        BuildUi();
        SetActiveState(false);
    }

    private void OnDestroy() { if (toggleAction != null) toggleAction.Disable(); }

    private void Update()
    {
        if (toggleAction.WasPressedThisFrame())
        {
            SetActiveState(!active);
        }

        if (!active || player == null) return;

        if (step >= Steps.Length) return; // finished, showing the completion panel

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
            case 7: // charge shot (costs 2 ammo in one go)
                if (shooter == null) return true; // can't detect - skip
                if (shooter.currentAmmo <= ammoBaseline - 2) return true;
                if (shooter.currentAmmo > ammoBaseline) ammoBaseline = shooter.currentAmmo; // reloaded
                return false;
            case 8: // melee hits (~2-3 punches of damage)
                return enemyHealth != null && enemyHealth.currentHealth <= enemyHpBaseline - 35f;
            case 9: // rainbow step: Attacking -> BoostStep transition
                if (prevPlayerState == MechState.Attacking && player.currentState == MechState.BoostStep)
                    sawStepCancel = true;
                return sawStepCancel;
            case 10: // shield
                if (combat != null && combat.IsShielding) shieldHeld += Time.deltaTime;
                return shieldHeld > 1.0f;
            case 11: // block a real attack (countdown, then the AI melees for real)
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
            case 12: // knockdown (health guard: a KILL revives the bot instead of passing)
                return enemyHealth != null && enemyHealth.isYellowLocked && enemyHealth.currentHealth > 0f;
        }
        return false;
    }

    private void CompleteStep()
    {
        stepDoneAt = Time.time;
        if (bodyText != null) bodyText.text = "NICE!";
    }

    private void AdvanceStep()
    {
        stepDoneAt = -1f;
        step++;
        if (step >= Steps.Length)
        {
            CompletedOnce = true;
            if (titleText != null) titleText.text = "TUTORIAL COMPLETE";
            if (bodyText != null) bodyText.text = "You know everything. Get ready...";
            return;
        }
        ResetDetection();
        ShowStep();
    }

    private void ResetDetection()
    {
        movedOrigin = player != null ? player.transform.position : Vector3.zero;
        movedDistance = 0f;
        riseBaseY = player != null ? player.transform.position.y : 0f;
        dashHeld = 0f;
        shieldHeld = 0f;
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
    }

    private void ShowStep()
    {
        if (titleText != null) titleText.text = "TUTORIAL " + (step + 1) + "/" + Steps.Length + " - " + Steps[step].title;
        if (bodyText != null) bodyText.text = Steps[step].body;
    }

    private void SetActiveState(bool on)
    {
        active = on;
        if (panel != null) panel.SetActive(on);
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

        // instruction panel (top-center)
        panel = new GameObject("Instruction Panel");
        panel.transform.SetParent(canvasGo.transform, false);
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.6f);
        RectTransform prt = panel.GetComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
        prt.pivot = new Vector2(0.5f, 1f);
        prt.anchoredPosition = new Vector2(0f, -110f);
        prt.sizeDelta = new Vector2(880f, 110f);

        titleText = UiLabel.Create(panel.transform, "Title", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                   new Vector2(0f, -12f), new Vector2(860f, 34f), 26, true, TextAnchor.UpperCenter);
        bodyText = UiLabel.Create(panel.transform, "Body", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                                  new Vector2(0f, -48f), new Vector2(840f, 60f), 19, false, TextAnchor.UpperCenter);

        // idle hint (bottom-left, only while the tutorial is off)
        hintText = UiLabel.Create(canvasGo.transform, "Hint", new Vector2(0f, 0f), new Vector2(0f, 0f),
                                  new Vector2(40f, 16f), new Vector2(300f, 26f), 18, false, TextAnchor.MiddleLeft);
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
