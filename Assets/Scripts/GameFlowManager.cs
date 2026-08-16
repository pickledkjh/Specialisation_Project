using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Complete game loop for playtesting: start menu -> tutorial -> battle ->
/// victory/defeat -> rematch. Spawns itself at play start, survives scene reloads,
/// and rebuilds the camera/HUD/tutorial helpers after a rematch reload.
///
/// Menu: ENTER starts (tutorial first), F skips straight to battle, ESC quits.
/// After a match: R rematch, ESC quit.
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    private enum Phase { Menu, Tutorial, Transition, Fight, StageClear, GameOver }
    private Phase phase = Phase.Menu;

    /// <summary>Mission = the solo three-stage run. TeamBattle = 2v2.</summary>
    public enum BattleMode { Mission, TeamBattle }
    private BattleMode mode = BattleMode.Mission;

    private MechHealth playerHealth, enemyHealth;
    private TutorialManager tutorial;

    private InputAction startAction, skipAction, restartAction, quitAction;
    private GameObject menuPanel, overPanel;
    private UiLabel menuTitle, menuBody, overTitle, overBody, splash;
    private float splashUntil = -1f;
    private float tutorialDoneAt = -1f;

    // ---------------- MISSION STAGES ----------------
    // The game is no longer one round against one enemy: it is a short mission of
    // escalating fights. Beating a stage banks your stats, patches you up a little
    // and drops you into a tougher opponent. Losing any stage ends the run.
    private class Stage
    {
        public string name = "";
        public string brief = "";
        public float enemyHealthScale = 1f;  // multiplies the enemy's SCENE max armour
        public int difficultyBump;           // added on top of the SETTINGS difficulty
        public float aggression = 1f;        // >1 = shoots / specials / attacks more often
        public float healFraction;           // fraction of MISSING armour restored between stages
        public float repairFlat;             // PLUS this fraction of max armour, flat
        public float bonusSeconds;           // added to the round clock
    }

    private static readonly Stage[] Stages =
    {
        new Stage { name = "TRIAL RUN",  brief = "Standard trainer unit. Warm up.",
                    enemyHealthScale = 1f,   difficultyBump = 0, aggression = 1f,
                    healFraction = 0f,    repairFlat = 0f,    bonusSeconds = 0f },
        new Stage { name = "ASSAULT",    brief = "Heavier armour. Faster trigger.",
                    enemyHealthScale = 1.3f, difficultyBump = 1, aggression = 1.25f,
                    healFraction = 0.5f,  repairFlat = 0.15f, bonusSeconds = 15f },
        new Stage { name = "ACE CUSTOM", brief = "Full spec. It will use everything it has.",
                    enemyHealthScale = 1.7f, difficultyBump = 2, aggression = 1.55f,
                    healFraction = 0.4f,  repairFlat = 0.2f,  bonusSeconds = 30f },
    };

    private int stageIndex;
    private int stagesCleared;
    private Stage CurrentStage { get { return Stages[Mathf.Clamp(stageIndex, 0, Stages.Length - 1)]; } }

    // Scene-authored values, captured once so stage multipliers never compound.
    private bool baselineCaptured;
    private float baseEnemyMaxHealth = 100f, baseAttackCooldown = 1.2f, baseSpecialGap = 5f, baseRiseChance = 0.25f;
    private Vector3 playerSpawnPos, enemySpawnPos;
    private Quaternion playerSpawnRot = Quaternion.identity, enemySpawnRot = Quaternion.identity;
    private bool playerSpawnKnown, enemySpawnKnown;

    // run totals (all stages) vs the per-stage counters below
    private float runDamageDealt, runDamageTaken, runSeconds;
    private int runMaxCombo;

    // ---------------- FADE ----------------
    public float fadeSeconds = 0.55f;
    private Image fadeOverlay;
    private UiLabel fadeTitle, fadeSub, stageLabel;
    private GameObject stagePanel;
    private UiLabel stageTitle, stageBody, stageNext, repairLabel;
    private Image repairFill;

    // EXVS/Starward-style timed round: timeout -> whoever has the higher HP
    // fraction wins. Visible countdown top-center, red in the last 30 seconds.
    public float matchSeconds = 120f; // demo-friendly rounds (was 180)
    private float fightEndsAt = -1f;
    private UiLabel timerLabel;

    // ---- "a game, not a practical": identity, difficulty, intro, ranked results ----
    public static int Difficulty
    {
        get { return GameSettings.Difficulty; }
        set { GameSettings.Difficulty = value; GameSettings.Save(); }
    }
    private InputAction controlsAction, diff1Action, diff2Action, diff3Action, teamAction;
    private UiLabel menuSubtitle;
    private GameObject panelMain, panelSettings, panelControls;
    private UiLabel setMasterVal, setMusicVal, setDiffVal, setTimeVal, setShakeVal;
    private UiLabel overRank, overStats;
    private GameObject pausePanel;
    private bool paused;
    private bool settingsFromPause;
    private bool controlsShowing;
    // per-match stats feeding the results screen
    private float statDamageDealt, statDamageTaken;
    private int statMaxCombo, statComboRun;
    private float statLastHitAt = -99f, fightStartedAt;

    // R on the results screen reloads the scene, and the reload used to land back
    // on the main menu - so "rematch" quietly meant "quit to menu". This carries the
    // intent across the load so the same mode starts again straight away.
    private bool rematchQueued;
    private BattleMode rematchMode;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        startAction = new InputAction("Start", InputActionType.Button);
        startAction.AddBinding("<Keyboard>/enter");
        startAction.AddBinding("<Keyboard>/numpadEnter");
        skipAction = new InputAction("Skip", InputActionType.Button);
        skipAction.AddBinding("<Keyboard>/f");
        restartAction = new InputAction("Restart", InputActionType.Button);
        restartAction.AddBinding("<Keyboard>/r");
        quitAction = new InputAction("Quit", InputActionType.Button);
        quitAction.AddBinding("<Keyboard>/escape");
        controlsAction = new InputAction("Controls", InputActionType.Button);
        controlsAction.AddBinding("<Keyboard>/c");
        diff1Action = new InputAction("Diff1", InputActionType.Button);
        diff1Action.AddBinding("<Keyboard>/1");
        diff2Action = new InputAction("Diff2", InputActionType.Button);
        diff2Action.AddBinding("<Keyboard>/2");
        diff3Action = new InputAction("Diff3", InputActionType.Button);
        diff3Action.AddBinding("<Keyboard>/3");
        teamAction = new InputAction("TeamBattle", InputActionType.Button);
        teamAction.AddBinding("<Keyboard>/v");
        teamAction.Enable();
        startAction.Enable(); skipAction.Enable(); restartAction.Enable(); quitAction.Enable();
        controlsAction.Enable(); diff1Action.Enable(); diff2Action.Enable(); diff3Action.Enable();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        startAction?.Disable(); skipAction?.Disable(); restartAction?.Disable(); quitAction?.Disable();
        controlsAction?.Disable(); diff1Action?.Disable(); diff2Action?.Disable(); diff3Action?.Disable();
        teamAction?.Disable();
    }

    private void Start()
    {
        BuildUi();
        HookScene();
        EnterMenu();
    }

    private void OnEnable() { MechHealth.AnyMechDamaged += OnMechDamaged; }
    private void OnDisable() { MechHealth.AnyMechDamaged -= OnMechDamaged; }

    private void OnMechDamaged(MechHealth victim, float amount)
    {
        if (phase != Phase.Fight || victim == null) return;
        if (victim == enemyHealth)
        {
            statDamageDealt += amount;
            if (Time.time - statLastHitAt > 2.4f) statComboRun = 0;
            statComboRun++;
            statLastHitAt = Time.time;
            if (statComboRun > statMaxCombo) statMaxCombo = statComboRun;
        }
        else if (victim == playerHealth)
        {
            statDamageTaken += amount;
        }
    }

    // After a rematch reload the bootstrapped helpers are gone (RuntimeInitialize
    // only fires once per session) - recreate whatever is missing, then re-hook.
    // NOTE: the second parameter is deliberately NOT called 'mode' - that would
    // shadow this class's BattleMode field for the whole method body.
    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (FindFirstObjectByType<MechController>() == null) return;

        if (FindFirstObjectByType<LockOnBattleCamera>() == null)
        {
            GameObject cam = new GameObject("Battle Camera");
            Camera main = Camera.main;
            if (main != null) cam.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
            var vcam = cam.AddComponent<Unity.Cinemachine.CinemachineCamera>();
            vcam.Priority = 15;
            var lens = vcam.Lens;
            lens.FieldOfView = main != null ? main.fieldOfView : 60f;
            vcam.Lens = lens;
            cam.AddComponent<LockOnBattleCamera>();
        }
        if (FindFirstObjectByType<BattleHUD>() == null)
            new GameObject("Battle HUD").AddComponent<BattleHUD>();
        if (FindFirstObjectByType<TutorialManager>() == null)
            new GameObject("Tutorial").AddComponent<TutorialManager>();
        // RuntimeInitializeOnLoadMethod only fires once per session, so the lock-on
        // switcher has to be put back by hand after a rematch reload.
        MechController reloaded = FindFirstObjectByType<MechController>();
        if (reloaded != null && reloaded.GetComponent<TargetSwitcher>() == null)
            reloaded.gameObject.AddComponent<TargetSwitcher>();
        // The floor clamp is bootstrapped once per session; a rematch reload needs it back.
        Camera mainCam = Camera.main;
        if (mainCam != null && mainCam.GetComponent<CameraGroundGuard>() == null)
            mainCam.gameObject.AddComponent<CameraGroundGuard>();

        HookScene();

        if (rematchQueued)
        {
            rematchQueued = false;
            this.mode = rematchMode;
            if (menuPanel != null) menuPanel.SetActive(false);
            if (overPanel != null) overPanel.SetActive(false);
            Time.timeScale = 1f;
            if (this.mode == BattleMode.TeamBattle) BeginTeamBattle();
            else BeginPlay(withTutorial: false);
            return;
        }

        EnterMenu();
    }

    private void HookScene()
    {
        MechController player = FindFirstObjectByType<MechController>();
        if (player != null) playerHealth = player.GetComponent<MechHealth>();
        SimpleMechAI enemy = FindFirstObjectByType<SimpleMechAI>();
        if (enemy != null) enemyHealth = enemy.GetComponent<MechHealth>();
        tutorial = FindFirstObjectByType<TutorialManager>();
        tutorialDoneAt = -1f;

        // TEAMS. The scene shipped with BOTH mechs on Team2, which broke the cost
        // readout and left every team-aware system with nothing to target. Setting
        // them here (not at fight start) means the tutorial is team-correct too.
        if (playerHealth != null) playerHealth.team = Team.Team1;
        if (enemyHealth != null) enemyHealth.team = Team.Team2;
        BattleRoster.Invalidate();

        baselineCaptured = false;
        playerSpawnKnown = enemySpawnKnown = false;
        CaptureBaseline();
    }

    // Read the scene-authored numbers ONCE per scene load. Every stage multiplier is
    // applied to these, never to the already-multiplied live values.
    private void CaptureBaseline()
    {
        if (baselineCaptured) return;
        SimpleMechAI enemy = enemyHealth != null ? enemyHealth.GetComponent<SimpleMechAI>() : null;
        if (enemyHealth == null && enemy == null && playerHealth == null) return;

        if (enemyHealth != null)
        {
            baseEnemyMaxHealth = enemyHealth.maxHealth;
            enemySpawnPos = enemyHealth.transform.position;
            enemySpawnRot = enemyHealth.transform.rotation;
            enemySpawnKnown = true;
        }
        if (enemy != null)
        {
            baseAttackCooldown = enemy.attackCooldown;
            baseSpecialGap = enemy.specialAttemptGap;
            baseRiseChance = enemy.randomRiseChance;
        }
        if (playerHealth != null)
        {
            playerSpawnPos = playerHealth.transform.position;
            playerSpawnRot = playerHealth.transform.rotation;
            playerSpawnKnown = true;
        }
        baselineCaptured = true;
    }

    private void EnterMenu()
    {
        phase = Phase.Menu;
        Time.timeScale = 0f;
        StopAllCoroutines();
        mode = BattleMode.Mission;
        TeamRules.Reset();
        if (TeamBattleSetup.Instance != null) TeamBattleSetup.Instance.Teardown();
        if (TeamHud.Instance != null) TeamHud.Instance.SetVisible(false);
        stageIndex = 0;
        stagesCleared = 0;
        runDamageDealt = runDamageTaken = runSeconds = 0f;
        runMaxCombo = 0;
        if (menuPanel != null) menuPanel.SetActive(true);
        if (overPanel != null) overPanel.SetActive(false);
        if (stagePanel != null) stagePanel.SetActive(false);
        if (stageLabel != null) stageLabel.gameObject.SetActive(false);
        if (fadeOverlay != null) fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        if (fadeTitle != null) fadeTitle.gameObject.SetActive(false);
        if (fadeSub != null) fadeSub.gameObject.SetActive(false);
        if (timerLabel != null) timerLabel.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        ShowMenuPanel(0);
    }

    private void Update()
    {
        // Splash hides on time no matter which phase we're in
        if (splash != null && splashUntil > 0f && Time.unscaledTime > splashUntil)
        {
            splash.gameObject.SetActive(false);
            splashUntil = -1f;
        }

        if (quitAction.WasPressedThisFrame())
        {
            if ((phase == Phase.Fight || phase == Phase.Tutorial))
            {
                if (settingsFromPause && menuPanel != null && menuPanel.activeSelf)
                {
                    CloseSettingsToPause(); // ESC inside pause-settings goes back to the pause panel
                }
                else
                {
                    TogglePause();
                }
            }
            else if (phase == Phase.Menu &&
                     ((panelSettings != null && panelSettings.activeSelf) || (panelControls != null && panelControls.activeSelf)))
            {
                ShowMenuPanel(0); // ESC backs out of settings/controls first
            }
            else if (phase == Phase.GameOver)
            {
                // Finishing a match and pressing ESC should never close the game -
                // that is what the menu's QUIT is for.
                QuitToMenu();
            }
            else if (phase == Phase.Menu)
            {
                QuitGame();
            }
        }

        switch (phase)
        {
            case Phase.Menu:
                if (controlsAction.WasPressedThisFrame())
                    ShowMenuPanel(panelControls != null && panelControls.activeSelf ? 0 : 2);
                if (diff1Action.WasPressedThisFrame()) { Difficulty = 0; RefreshSettingsLabels(); }
                if (diff2Action.WasPressedThisFrame()) { Difficulty = 1; RefreshSettingsLabels(); }
                if (diff3Action.WasPressedThisFrame()) { Difficulty = 2; RefreshSettingsLabels(); }

                if (startAction.WasPressedThisFrame())
                {
                    BeginPlay(withTutorial: true);
                }
                else if (teamAction.WasPressedThisFrame())
                {
                    BeginTeamBattle();
                }
                else if (skipAction.WasPressedThisFrame())
                {
                    BeginPlay(withTutorial: false);
                }
                break;

            case Phase.Tutorial:
                if (tutorial == null || !tutorial.IsActive)
                {
                    BeginMission(); // player quit the tutorial with T / the skip hold
                }
                else if (tutorial.CompletedOnce)
                {
                    if (tutorialDoneAt < 0f) tutorialDoneAt = Time.unscaledTime;
                    if (Time.unscaledTime - tutorialDoneAt > 1.8f) BeginMission();
                }
                break;

            case Phase.Transition:
                break; // the transition coroutine owns this phase end to end

            case Phase.StageClear:
                if (startAction.WasPressedThisFrame() || restartAction.WasPressedThisFrame())
                    BeginNextStage();
                break;

            case Phase.Fight:
                UpdateTimer();
                if (mode == BattleMode.TeamBattle)
                {
                    UpdateTeamFight();
                }
                else if (playerHealth != null && playerHealth.currentHealth <= 0f)
                    ShowResults(false, false, "DEFEAT");
                else if (enemyHealth != null && enemyHealth.currentHealth <= 0f)
                    WinCurrentStage(false);
                else if (fightEndsAt > 0f && Time.time >= fightEndsAt)
                    EndByTimeout();
                break;

            case Phase.GameOver:
                if (restartAction.WasPressedThisFrame()) Rematch();
                break;
        }
    }

    private void BeginPlay(bool withTutorial)
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        Time.timeScale = 1f;
        CaptureBaseline();
        stageIndex = 0;
        stagesCleared = 0;
        runDamageDealt = runDamageTaken = runSeconds = 0f;
        runMaxCombo = 0;

        if (withTutorial && tutorial != null)
        {
            tutorial.SetTutorialActive(true);
            phase = Phase.Tutorial;
            tutorialDoneAt = -1f;
        }
        else
        {
            BeginMission();
        }
    }

    // Tutorial (or menu) -> the real mission. The fade hides every reset that has to
    // happen in between: armour restored, ammo restocked, staged tutorial props
    // cleared, the AI un-paused, both mechs teleported back to their spawns.
    private void BeginMission()
    {
        if (phase == Phase.Transition) return;
        phase = Phase.Transition;
        stageIndex = 0;
        if (tutorial != null) tutorial.SetTutorialActive(false);
        StartCoroutine(TransitionIntoStage("MISSION START"));
    }

    /// <summary>
    /// 2v2. You and an allied unit against two hostiles, one round, no stages.
    /// Losing YOUR unit ends it - your partner cannot carry the match for you -
    /// and the round is won when both hostiles are destroyed.
    /// </summary>
    /// <summary>Replay the mode you just finished, from a clean scene.</summary>
    private void Rematch()
    {
        rematchQueued = true;
        rematchMode = mode;
        Time.timeScale = 1f;
        if (overPanel != null) overPanel.SetActive(false);
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void BeginTeamBattle()
    {
        if (phase == Phase.Transition) return;
        if (menuPanel != null) menuPanel.SetActive(false);
        Time.timeScale = 1f;
        CaptureBaseline();

        mode = BattleMode.TeamBattle;
        stageIndex = 0;
        stagesCleared = 0;
        runDamageDealt = runDamageTaken = runSeconds = 0f;
        runMaxCombo = 0;
        if (tutorial != null) tutorial.SetTutorialActive(false);

        phase = Phase.Transition;
        StartCoroutine(TransitionIntoStage("TEAM BATTLE"));
    }

    private void UpdateTeamFight()
    {
        if (playerHealth != null && playerHealth.currentHealth <= 0f)
        {
            ShowResults(false, false, "DEFEAT - UNIT DESTROYED");
            return;
        }
        if (BattleRoster.LivingCount(Team.Team2) <= 0)
        {
            ShowResults(true, false, "TEAM VICTORY");
            return;
        }
        if (fightEndsAt > 0f && Time.time >= fightEndsAt)
        {
            float mine = BattleRoster.TeamHealthFraction(Team.Team1);
            float theirs = BattleRoster.TeamHealthFraction(Team.Team2);
            if (mine > theirs + 0.01f) ShowResults(true, true, "TIME UP - TEAM VICTORY");
            else if (theirs > mine + 0.01f) ShowResults(false, true, "TIME UP - TEAM DEFEAT");
            else ShowResults(false, true, "TIME UP - DRAW");
        }
    }

    private void BeginNextStage()
    {
        if (phase != Phase.StageClear) return;
        stageIndex = Mathf.Min(stageIndex + 1, Stages.Length - 1);
        if (stagePanel != null) stagePanel.SetActive(false);
        phase = Phase.Transition;
        StartCoroutine(TransitionIntoStage("NEXT TARGET"));
    }

    // ---------- the fade ----------

    private System.Collections.IEnumerator FadeTo(float targetAlpha, float duration)
    {
        if (fadeOverlay == null) yield break;
        float from = fadeOverlay.color.a;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Lerp(from, targetAlpha, Mathf.Clamp01(t / Mathf.Max(0.0001f, duration)));
            fadeOverlay.color = new Color(0f, 0f, 0f, a);
            yield return null;
        }
        fadeOverlay.color = new Color(0f, 0f, 0f, targetAlpha);
    }

    private System.Collections.IEnumerator TransitionIntoStage(string caption)
    {
        phase = Phase.Transition;
        if (stagePanel != null) stagePanel.SetActive(false);
        if (overPanel != null) overPanel.SetActive(false);
        if (splash != null) splash.gameObject.SetActive(false);
        if (timerLabel != null) timerLabel.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // ---- fade OUT to black ----
        yield return FadeTo(1f, fadeSeconds);

        // world frozen behind the black screen while everything is rebuilt
        Time.timeScale = 0f;
        SetUpStage();

        Stage st = CurrentStage;
        if (fadeTitle != null)
        {
            fadeTitle.text = caption;
            fadeTitle.gameObject.SetActive(true);
        }
        if (fadeSub != null)
        {
            fadeSub.text = mode == BattleMode.TeamBattle
                ? "2 V 2   -   YOU + ALLY UNIT   vs   HOSTILE A + HOSTILE B\n" +
                  "FRIENDLY FIRE IS ON - your partner takes reduced damage.\nTAB switches which hostile you are locked on to."
                : "STAGE " + (stageIndex + 1) + " / " + Stages.Length + "   -   " + st.name + "\n" + st.brief;
            fadeSub.gameObject.SetActive(true);
        }
        BattleAudio.Play("alert", 0.5f, 0.85f);
        yield return new WaitForSecondsRealtime(1.6f);

        if (fadeTitle != null) fadeTitle.gameObject.SetActive(false);
        if (fadeSub != null) fadeSub.gameObject.SetActive(false);

        // Stay frozen through the fade-in - StartFight's READY.../FIGHT! ritual is what
        // unfreezes the world, so nobody gets shot while the screen is still dark.
        yield return FadeTo(0f, fadeSeconds);

        StartFight("FIGHT!");
    }

    // Everything that must be true the instant a stage begins. Runs behind the fade.
    private void SetUpStage()
    {
        CaptureBaseline();

        if (mode == BattleMode.TeamBattle) { SetUpTeamBattle(); return; }

        // Solo mission: strictly 1v1, so tear any team battle down and put the
        // teams back the way the mission expects them.
        if (TeamBattleSetup.Instance != null) TeamBattleSetup.Instance.Teardown();
        if (TeamHud.Instance != null) TeamHud.Instance.SetVisible(false);
        TeamRules.TeamModeActive = false;
        if (playerHealth != null) playerHealth.team = Team.Team1;
        if (enemyHealth != null) enemyHealth.team = Team.Team2;
        BattleRoster.Invalidate();

        Stage st = CurrentStage;
        SimpleMechAI enemy = enemyHealth != null ? enemyHealth.GetComponent<SimpleMechAI>() : null;

        if (stageIndex == 0 && CostManager.Instance != null) CostManager.Instance.ResetPools();

        // --- enemy: scaled, revived, awake, back at its spawn ---
        if (enemyHealth != null)
        {
            enemyHealth.maxHealth = baseEnemyMaxHealth * st.enemyHealthScale;
            // Revive() undoes everything Die()/knockdown set - the yellow lock, the
            // 90-degree lying-down tilt, root motion, and the disabled control scripts.
            enemyHealth.Revive();
            if (enemySpawnKnown) TeleportTo(enemyHealth, enemySpawnPos, enemySpawnRot);
        }
        RefreshMech(enemyHealth);
        if (enemy != null)
        {
            enemy.passiveMode = false; // the interactables lesson parks the AI - always release it
            enemy.RestockForBattle();
            enemy.maxHealth = baseEnemyMaxHealth * st.enemyHealthScale;
            enemy.currentHealth = enemy.maxHealth;
            enemy.isDead = false;
            enemy.attackCooldown = baseAttackCooldown / st.aggression;
            enemy.specialAttemptGap = baseSpecialGap / st.aggression;
            enemy.randomRiseChance = Mathf.Clamp01(baseRiseChance * st.aggression);
            ApplyDifficulty(enemy);
            enemy.aiShotGapSeconds /= st.aggression; // difficulty sets it absolutely; stage sharpens it
            enemy.shotWindupSeconds /= st.aggression;
        }

        // --- player: full at stage 1, carried-over armour after that ---
        // The between-stage repair is NOT done here - it is played out on the STAGE
        // CLEAR screen where you can watch the bar refill, so whatever armour you
        // walk out of that screen with is exactly what you start the next fight on.
        if (playerHealth != null)
        {
            float carried = playerHealth.currentHealth;
            playerHealth.Revive();   // clears the tutorial's bumps and any down state
            RefreshMech(playerHealth);
            if (stageIndex > 0)
                playerHealth.currentHealth = Mathf.Clamp(carried, playerHealth.maxHealth * 0.2f, playerHealth.maxHealth);
            if (playerSpawnKnown) TeleportTo(playerHealth, playerSpawnPos, playerSpawnRot);
        }

        // clear any lingering down / stagger / cinematic state on both mechs
        ClearCombatState(playerHealth);
        ClearCombatState(enemyHealth);

        if (stageLabel != null)
        {
            stageLabel.gameObject.SetActive(true);
            stageLabel.text = "STAGE " + (stageIndex + 1) + " / " + Stages.Length + "   " + st.name;
        }
    }

    // 2v2 setup. Both extra units are clones of the hand-tuned scene enemy, so the
    // ally is a real unit of the same class rather than a weaker escort.
    private void SetUpTeamBattle()
    {
        if (CostManager.Instance != null) CostManager.Instance.ResetPools();

        // Everyone back to their marks first, then the ally / second hostile are
        // spawned relative to those positions.
        if (playerHealth != null)
        {
            playerHealth.Revive();
            RefreshMech(playerHealth);
            if (playerSpawnKnown) TeleportTo(playerHealth, playerSpawnPos, playerSpawnRot);
        }
        if (enemyHealth != null)
        {
            enemyHealth.maxHealth = baseEnemyMaxHealth;
            enemyHealth.Revive();
            RefreshMech(enemyHealth);
            if (enemySpawnKnown) TeleportTo(enemyHealth, enemySpawnPos, enemySpawnRot);
        }

        TeamBattleSetup team = TeamBattleSetup.Ensure();
        team.Build(playerHealth, enemyHealth);
        team.ResetUnits();

        // Every AI in a team battle runs at the SETTINGS difficulty - no stage bump.
        foreach (MechHealth m in BattleRoster.All)
        {
            if (m == null) continue;
            SimpleMechAI ai = m.GetComponent<SimpleMechAI>();
            if (ai == null) continue;
            ai.attackCooldown = baseAttackCooldown;
            ai.specialAttemptGap = baseSpecialGap;
            ai.randomRiseChance = baseRiseChance;
            ApplyDifficulty(ai);
        }

        ClearCombatState(playerHealth);
        TeamHud.Ensure().SetVisible(true);

        if (stageLabel != null)
        {
            stageLabel.gameObject.SetActive(true);
            stageLabel.text = "2 V 2   TEAM BATTLE";
        }
    }

    // A CharacterController caches its own position internally - writing the transform
    // while it is enabled gets silently snapped back on the next Move().
    private static void TeleportTo(Component who, Vector3 pos, Quaternion rot)
    {
        if (who == null) return;
        CharacterController cc = who.GetComponent<CharacterController>();
        bool had = cc != null && cc.enabled;
        if (had) cc.enabled = false;
        who.transform.SetPositionAndRotation(pos, rot);
        if (had) cc.enabled = true;
    }

    private static void ClearCombatState(MechHealth health)
    {
        if (health == null) return;
        MechCombat combat = health.GetComponent<MechCombat>();
        if (combat != null) combat.ForceResetAfterDown();
        MechController ctrl = health.GetComponent<MechController>();
        if (ctrl != null) ctrl.ForceResetAfterDown();
        SimpleMechAI ai = health.GetComponent<SimpleMechAI>();
        if (ai != null) ai.CancelAttack();
    }

    // ---------- stage results ----------

    private void BankStageStats()
    {
        runDamageDealt += statDamageDealt;
        runDamageTaken += statDamageTaken;
        runSeconds += Mathf.Max(0f, Time.time - fightStartedAt);
        if (statMaxCombo > runMaxCombo) runMaxCombo = statMaxCombo;
        stagesCleared++;
    }

    private void WinCurrentStage(bool timeout)
    {
        BankStageStats();
        if (stageIndex + 1 < Stages.Length)
            StartCoroutine(StageClearRoutine(timeout));
        else
            ShowResults(true, timeout, timeout ? "TIME UP - MISSION COMPLETE" : "MISSION COMPLETE");
    }

    private System.Collections.IEnumerator StageClearRoutine(bool timeout)
    {
        phase = Phase.StageClear;
        if (timerLabel != null) timerLabel.gameObject.SetActive(false);
        if (splash != null)
        {
            splash.text = "STAGE CLEAR";
            splash.gameObject.SetActive(true);
            splashUntil = -1f;
        }
        BattleAudio.Play("alert", 0.6f, 1.4f);
        yield return new WaitForSecondsRealtime(1.4f); // let the kill play out
        if (splash != null) splash.gameObject.SetActive(false);

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Stage next = Stages[Mathf.Min(stageIndex + 1, Stages.Length - 1)];
        if (stageTitle != null)
            stageTitle.text = timeout ? "STAGE CLEAR - TIME UP" : "STAGE " + (stageIndex + 1) + " CLEAR";
        if (stageBody != null)
            stageBody.text =
                "DAMAGE DEALT  " + Mathf.RoundToInt(statDamageDealt) +
                "        TAKEN  " + Mathf.RoundToInt(statDamageTaken) +
                "        MAX COMBO  " + statMaxCombo + " HITS\n\n" +
                "NEXT  -  STAGE " + (stageIndex + 2) + " / " + Stages.Length + "   " + next.name + "\n" + next.brief;
        if (stageNext != null) stageNext.text = "ENTER - launch next battle";
        if (stagePanel != null) stagePanel.SetActive(true);

        // the repair is a beat you watch, not a number that silently changed
        yield return RepairPlayerArmour(next);
    }

    /// <summary>Between-stage field repair. Restores a slice of what you lost plus a
    /// flat top-up, and plays it out on the STAGE CLEAR screen so the bar visibly
    /// refills instead of the armour quietly changing behind a fade.</summary>
    private System.Collections.IEnumerator RepairPlayerArmour(Stage next)
    {
        if (playerHealth == null || playerHealth.maxHealth <= 0f) yield break;

        float max = playerHealth.maxHealth;
        float before = Mathf.Clamp(playerHealth.currentHealth, 0f, max);
        float repair = (max - before) * next.healFraction + max * next.repairFlat;
        float after = Mathf.Min(max, before + repair);

        if (repairFill != null) SetFill(repairFill, before / max);
        if (repairLabel != null)
            repairLabel.text = "FIELD REPAIR      ARMOUR  " + Mathf.RoundToInt(before / max * 100f) + "%";

        yield return new WaitForSecondsRealtime(0.45f); // let the panel land first

        if (after <= before + 0.01f)
        {
            if (repairLabel != null) repairLabel.text = "ARMOUR  " + Mathf.RoundToInt(before / max * 100f) + "%  -  ALREADY AT FULL";
            yield break;
        }

        BattleAudio.Play("alert", 0.35f, 1.9f);
        const float dur = 1.15f;
        float t = 0f;
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
            float now = Mathf.Lerp(before, after, k);
            playerHealth.currentHealth = now; // written live, so the battle HUD agrees
            if (repairFill != null) SetFill(repairFill, now / max);
            if (repairLabel != null)
                repairLabel.text = "FIELD REPAIR      " + Mathf.RoundToInt(before / max * 100f) +
                                   "%   ->   " + Mathf.RoundToInt(now / max * 100f) + "%" +
                                   "      ( + " + Mathf.RoundToInt(now - before) + " )";
            yield return null;
        }
        playerHealth.currentHealth = after;
        if (repairFill != null) SetFill(repairFill, after / max);
        if (repairLabel != null)
            repairLabel.text = "FIELD REPAIR      " + Mathf.RoundToInt(before / max * 100f) +
                               "%   ->   " + Mathf.RoundToInt(after / max * 100f) + "%" +
                               "      ( + " + Mathf.RoundToInt(after - before) + " )";
        BattleAudio.Play("alert", 0.4f, 1.3f);
    }

    private static void SetFill(Image fill, float frac)
    {
        RectTransform rt = fill.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(Mathf.Clamp01(frac), 1f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        // green while healthy, amber when the repair still leaves you thin
        fill.color = frac >= 0.6f ? new Color(0.35f, 0.95f, 0.5f)
                   : frac >= 0.3f ? new Color(1f, 0.8f, 0.3f)
                                  : new Color(1f, 0.42f, 0.32f);
    }

    private void StartFight(string splashText)
    {
        phase = Phase.Fight;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // round length from SETTINGS, plus the extra time a tougher stage earns you.
        // A 2v2 gets a flat bonus instead - four units take longer to resolve.
        matchSeconds = mode == BattleMode.TeamBattle
            ? GameSettings.MatchSeconds + 60f
            : GameSettings.MatchSeconds + CurrentStage.bonusSeconds;

        statDamageDealt = statDamageTaken = 0f;
        statMaxCombo = statComboRun = 0;
        statLastHitAt = -99f;
        fightStartedAt = Time.time;

        StartCoroutine(FightIntro(splashText));

        fightEndsAt = Time.time + matchSeconds;
        if (timerLabel != null) timerLabel.gameObject.SetActive(true);
    }

    private void UpdateTimer()
    {
        if (timerLabel == null || fightEndsAt < 0f) return;
        float remain = Mathf.Max(0f, fightEndsAt - Time.time);
        int m = Mathf.FloorToInt(remain / 60f);
        int s = Mathf.FloorToInt(remain % 60f);
        timerLabel.text = m + ":" + s.ToString("00");
        timerLabel.color = remain <= 30f
            ? Color.Lerp(new Color(1f, 0.3f, 0.25f), Color.white, Mathf.PingPong(Time.time * 2f, 0.5f))
            : Color.white;
    }

    private void EndByTimeout()
    {
        float pFrac = playerHealth != null && playerHealth.maxHealth > 0f ? playerHealth.currentHealth / playerHealth.maxHealth : 0f;
        float eFrac = enemyHealth != null && enemyHealth.maxHealth > 0f ? enemyHealth.currentHealth / enemyHealth.maxHealth : 0f;
        if (pFrac > eFrac)
            WinCurrentStage(true);
        else if (eFrac > pFrac)
            ShowResults(false, true, "TIME UP - DEFEAT");
        else
            ShowResults(false, true, "TIME UP - DRAW");
    }

    private static void RefreshMech(MechHealth health)
    {
        if (health == null) return;
        health.currentHealth = health.maxHealth;
        health.currentKnockdownValue = 0f;
        BoostManager boost = health.GetComponent<BoostManager>();
        if (boost != null) boost.currentBoost = boost.maxBoost;
        MechShooter shooter = health.GetComponent<MechShooter>();
        if (shooter != null) shooter.currentAmmo = shooter.maxAmmo;
    }

    // ---------- pause ----------

    private void TogglePause()
    {
        // Never fight the burst cut-in / finisher freeze / kill slow-mo for the clock
        if (!paused && Time.timeScale < 0.9f) return;
        paused = !paused;
        Time.timeScale = paused ? 0f : 1f;
        if (pausePanel != null) pausePanel.SetActive(paused);
        if (paused) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        BattleAudio.Play("alert", 0.4f, paused ? 0.8f : 1.2f);
    }

    private void ResumeFromPause()
    {
        if (!paused) return;
        paused = false;
        Time.timeScale = 1f;
        if (pausePanel != null) pausePanel.SetActive(false);
    }

    private void OpenSettingsFromPause()
    {
        settingsFromPause = true;
        if (pausePanel != null) pausePanel.SetActive(false);
        if (menuPanel != null) menuPanel.SetActive(true);
        ShowMenuPanel(1);
    }

    private void CloseSettingsToPause()
    {
        settingsFromPause = false;
        if (menuPanel != null) menuPanel.SetActive(false);
        if (pausePanel != null) pausePanel.SetActive(true);
    }

    private void QuitToMenu()
    {
        paused = false;
        settingsFromPause = false;
        if (pausePanel != null) pausePanel.SetActive(false);
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name); // clean reset -> OnSceneLoaded -> EnterMenu
    }

    // ---------- menu building blocks ----------

    private void QuitGame()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // Designed still background: navy gradient + faint grid + star specks +
    // red diagonal accent stripes. All generated - no art assets.
    private void BuildMenuBackdrop(Transform parent)
    {
        const int S = 512;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        System.Random rng = new System.Random(20260810);
        for (int y = 0; y < S; y++)
        {
            float g = y / (float)(S - 1);
            Color row = Color.Lerp(new Color(0.02f, 0.03f, 0.055f), new Color(0.075f, 0.11f, 0.18f), g);
            for (int x = 0; x < S; x++)
            {
                Color c = row;
                if (x % 64 == 0 || y % 64 == 0) c += new Color(1f, 1f, 1f) * 0.018f; // faint grid
                if (rng.NextDouble() < 0.0012) c += new Color(0.8f, 0.9f, 1f) * (float)rng.NextDouble() * 0.5f; // star speck
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();

        GameObject bgGo = new GameObject("Backdrop");
        bgGo.transform.SetParent(parent, false);
        bgGo.transform.SetAsFirstSibling();
        Image bg = bgGo.AddComponent<Image>();
        bg.sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        bg.color = Color.white;
        bg.raycastTarget = false;
        RectTransform bgRt = bgGo.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;

        // Red diagonal accent stripes down the left side - the ARMOUR CLASH brand mark
        MakeBackdropStripe(parent, new Vector2(-640f, 0f), new Vector2(150f, 2400f), new Color(0.85f, 0.22f, 0.18f, 0.85f));
        MakeBackdropStripe(parent, new Vector2(-500f, 0f), new Vector2(46f, 2400f), new Color(0.95f, 0.85f, 0.8f, 0.55f));
        MakeBackdropStripe(parent, new Vector2(-436f, 0f), new Vector2(16f, 2400f), new Color(0.85f, 0.22f, 0.18f, 0.6f));
        MakeBackdropStripe(parent, new Vector2(660f, -300f), new Vector2(220f, 2400f), new Color(0.06f, 0.10f, 0.18f, 0.9f));
    }

    private void MakeBackdropStripe(Transform parent, Vector2 pos, Vector2 size, Color color)
    {
        GameObject go = new GameObject("Stripe");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, -16f);
    }

    private void EnsureEventSystem()
    {
        if (UnityEngine.EventSystems.EventSystem.current != null) return;
        GameObject es = new GameObject("EventSystem");
        es.transform.SetParent(transform, false); // rides the DontDestroyOnLoad flow object
        es.AddComponent<UnityEngine.EventSystems.EventSystem>();
        es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private GameObject MakeSubPanel(Transform parent, string name)
    {
        GameObject go = new GameObject("Panel " + name);
        go.transform.SetParent(parent, false);
        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return go;
    }

    private void MakeAccent(Transform parent, Vector2 pos, Vector2 size)
    {
        GameObject accent = new GameObject("Accent");
        accent.transform.SetParent(parent, false);
        Image img = accent.AddComponent<Image>();
        img.color = new Color(0.92f, 0.25f, 0.2f, 0.95f);
        img.raycastTarget = false;
        RectTransform rt = accent.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    private GameObject MakeMenuButton(Transform parent, string text, Vector2 pos, UnityEngine.Events.UnityAction onClick, float width = 460f, float height = 52f)
    {
        GameObject go = new GameObject("Btn " + text);
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = new Color(0.10f, 0.16f, 0.26f, 0.92f);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, height);
        Button b = go.AddComponent<Button>();
        b.targetGraphic = img;
        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.4f, 0.9f); // warm glow on hover
        cb.pressedColor = new Color(0.7f, 0.7f, 0.7f);
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        b.onClick.AddListener(onClick);
        b.onClick.AddListener(() => BattleAudio.Play("alert", 0.3f, 1.6f)); // click blip
        UiLabel lbl = UiLabel.Create(go.transform, "Label", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     Vector2.zero, new Vector2(width, height), 23, true, TextAnchor.MiddleCenter);
        lbl.text = text;
        return go;
    }

    private int settingRowIndex;
    private UiLabel MakeSettingRow(Transform parent, string label, float y,
                                   UnityEngine.Events.UnityAction onLeft, UnityEngine.Events.UnityAction onRight)
    {
        float delay = 0.1f + 0.06f * settingRowIndex++;
        UiLabel name = UiLabel.Create(parent, "Set " + label, new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
                                      new Vector2(-40f, y), new Vector2(420f, 34f), 22, false, TextAnchor.MiddleRight);
        name.text = label;
        UiFlyIn.Add(name.gameObject, new Vector2(-600f, 0f), delay);

        GameObject lb = MakeMenuButton(parent, "<", new Vector2(60f, y), () => { onLeft(); GameSettings.Save(); RefreshSettingsLabels(); }, 44f, 40f);
        UiLabel val = UiLabel.Create(parent, "Val " + label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(185f, y), new Vector2(210f, 34f), 22, true, TextAnchor.MiddleCenter);
        val.color = new Color(1f, 0.85f, 0.35f);
        GameObject rb = MakeMenuButton(parent, ">", new Vector2(310f, y), () => { onRight(); GameSettings.Save(); RefreshSettingsLabels(); }, 44f, 40f);
        UiFlyIn.Add(lb, new Vector2(600f, 0f), delay);
        UiFlyIn.Add(val.gameObject, new Vector2(600f, 0f), delay);
        UiFlyIn.Add(rb, new Vector2(600f, 0f), delay);
        return val;
    }

    private void ShowMenuPanel(int which)
    {
        if (panelMain != null) panelMain.SetActive(which == 0);
        if (panelSettings != null) panelSettings.SetActive(which == 1);
        if (panelControls != null) panelControls.SetActive(which == 2);
        if (which == 1) RefreshSettingsLabels();
    }

    private void RefreshSettingsLabels()
    {
        if (setMasterVal != null) setMasterVal.text = Mathf.RoundToInt(GameSettings.MasterVolume * 10f) + " / 10";
        if (setMusicVal != null) setMusicVal.text = Mathf.RoundToInt(GameSettings.MusicVolume * 10f) + " / 10";
        if (setDiffVal != null) setDiffVal.text = GameSettings.Difficulty == 0 ? "EASY" : GameSettings.Difficulty == 2 ? "HARD" : "NORMAL";
        if (setTimeVal != null) setTimeVal.text = Mathf.RoundToInt(GameSettings.MatchSeconds) + "s";
        if (setShakeVal != null) setShakeVal.text = GameSettings.ScreenShake ? "ON" : "OFF";
    }

    // Difficulty tunes the AI around its Normal scene values - reapplied at every
    // fight start so rematch reloads and mid-session changes both take effect.
    private void ApplyDifficulty(SimpleMechAI enemy)
    {
        if (enemy == null) return;
        int level = Mathf.Clamp(Difficulty + CurrentStage.difficultyBump, 0, 2);
        if (level == 0)      { enemy.aiShotGapSeconds = 3.4f; enemy.shotWindupSeconds = 0.85f; enemy.dashSpeedScale = 1.15f; }
        else if (level == 2) { enemy.aiShotGapSeconds = 1.3f; enemy.shotWindupSeconds = 0.35f; enemy.dashSpeedScale = 1.9f; }
        else                 { enemy.aiShotGapSeconds = 2.2f; enemy.shotWindupSeconds = 0.55f; enemy.dashSpeedScale = 1.5f; }
    }

    // READY... FIGHT! - the tiny ritual that makes a match feel like a match.
    private System.Collections.IEnumerator FightIntro(string text)
    {
        if (splash == null) yield break;
        Time.timeScale = 0.2f; // brief dramatic freeze while READY shows
        splash.text = "READY...";
        splash.gameObject.SetActive(true);
        splashUntil = -1f;
        yield return new WaitForSecondsRealtime(0.9f);
        if (phase != Phase.Fight) { yield break; }
        Time.timeScale = 1f;
        splash.text = text;
        splashUntil = Time.unscaledTime + 1.1f;
        BattleAudio.Play("alert", 0.6f, 1.3f);
    }

    // Results screen: stats + an EXVS-style letter rank. A rank makes the game
    // judge the PLAYER - the single cheapest way to feel like a game.
    private void ShowResults(bool won, bool timeout, string title)
    {
        string rank;
        Color rankCol;
        float hpFrac = playerHealth != null && playerHealth.maxHealth > 0f
            ? Mathf.Clamp01(playerHealth.currentHealth / playerHealth.maxHealth) : 0f;
        bool teamMode = mode == BattleMode.TeamBattle;
        bool allyAlive = TeamBattleSetup.Instance != null &&
                         BattleRoster.IsAlive(TeamBattleSetup.Instance.AllyUnit);
        bool cleared = teamMode ? (won && allyAlive) : (won && stagesCleared >= Stages.Length);
        if (cleared && !timeout && hpFrac >= 0.7f && runMaxCombo >= 6) { rank = "S"; rankCol = new Color(1f, 0.85f, 0.25f); }
        else if (cleared && hpFrac >= 0.5f) { rank = "A"; rankCol = new Color(0.4f, 0.9f, 1f); }
        else if (cleared)                   { rank = "B"; rankCol = new Color(0.45f, 0.95f, 0.5f); }
        else if (won || stagesCleared >= 1) { rank = "C"; rankCol = Color.white; }
        else                                { rank = "D"; rankCol = new Color(0.6f, 0.6f, 0.65f); }

        // whatever the last stage scored has not been banked yet on a loss
        float dealt = teamMode ? statDamageDealt : runDamageDealt + (won ? 0f : statDamageDealt);
        float taken = teamMode ? statDamageTaken : runDamageTaken + (won ? 0f : statDamageTaken);
        int combo = teamMode ? statMaxCombo : Mathf.Max(runMaxCombo, won ? 0 : statMaxCombo);
        float secs = teamMode ? Mathf.Max(0f, Time.time - fightStartedAt)
                              : runSeconds + (won ? 0f : Mathf.Max(0f, Time.time - fightStartedAt));
        string stats =
            (teamMode ? "PARTNER  " + (allyAlive ? "SURVIVED" : "DESTROYED")
                      : "STAGES CLEARED  " + stagesCleared + " / " + Stages.Length) +
            "        DAMAGE DEALT  " + Mathf.RoundToInt(dealt) +
            "        TAKEN  " + Mathf.RoundToInt(taken) +
            "        MAX COMBO  " + combo + " HITS" +
            "        TIME  " + Mathf.FloorToInt(secs / 60f) + ":" + Mathf.FloorToInt(secs % 60f).ToString("00");

        EnterGameOver(title, "R - rematch        ESC - main menu");
        if (overStats != null) overStats.text = stats;
        if (overRank != null)
        {
            overRank.text = rank;
            overRank.color = rankCol;
            overRank.gameObject.SetActive(true);
        }
    }

    private void EnterGameOver(string title, string body)
    {
        phase = Phase.GameOver;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        if (timerLabel != null) timerLabel.gameObject.SetActive(false);
        if (stagePanel != null) stagePanel.SetActive(false);
        if (stageLabel != null) stageLabel.gameObject.SetActive(false);
        if (splash != null) splash.gameObject.SetActive(false);
        if (overPanel != null)
        {
            overPanel.SetActive(true);
            if (overTitle != null) overTitle.text = title;
            if (overBody != null) overBody.text = body;
        }
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("Game Flow Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40; // above everything
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasGo.AddComponent<GraphicRaycaster>(); // WITHOUT this, no button on this canvas can ever be clicked

        // ---- MAIN MENU: proper panel-based UI with clickable buttons ----
        EnsureEventSystem();
        menuPanel = MakePanel(canvasGo.transform, "Start Menu", new Color(0.02f, 0.03f, 0.06f, 1f)); // opaque - the raw frozen scene is not menu art
        BuildMenuBackdrop(menuPanel.transform);

        // -- panel 1: MAIN --
        panelMain = MakeSubPanel(menuPanel.transform, "Main");
        menuTitle = UiLabel.Create(panelMain.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 240f), new Vector2(1500f, 110f), 96, true, TextAnchor.MiddleCenter);
        menuTitle.text = "ARMOUR CLASH";
        UiFlyIn.Add(menuTitle.gameObject, new Vector2(0f, 260f), 0f, 0.3f);
        MakeAccent(panelMain.transform, new Vector2(0f, 178f), new Vector2(640f, 6f));
        menuSubtitle = UiLabel.Create(panelMain.transform, "Subtitle", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                      new Vector2(0f, 146f), new Vector2(1000f, 30f), 20, false, TextAnchor.MiddleCenter);
        menuSubtitle.text = "T H R E E - S T A G E   M E C H   A R E N A   M I S S I O N";
        menuSubtitle.color = new Color(0.7f, 0.78f, 0.9f, 0.85f);
        UiFlyIn.Add(menuSubtitle.gameObject, new Vector2(0f, 180f), 0.08f, 0.3f);

        GameObject b1 = MakeMenuButton(panelMain.transform, "LAUNCH MISSION  (3 stages)", new Vector2(0f, 78f), () => BeginPlay(withTutorial: true));
        GameObject b2 = MakeMenuButton(panelMain.transform, "2 V 2 TEAM BATTLE", new Vector2(0f, 22f), BeginTeamBattle);
        GameObject b3 = MakeMenuButton(panelMain.transform, "QUICK BATTLE (skip tutorial)", new Vector2(0f, -34f), () => BeginPlay(withTutorial: false));
        GameObject b4 = MakeMenuButton(panelMain.transform, "SETTINGS", new Vector2(0f, -90f), () => ShowMenuPanel(1));
        GameObject b5 = MakeMenuButton(panelMain.transform, "CONTROLS", new Vector2(0f, -146f), () => ShowMenuPanel(2));
        GameObject b6 = MakeMenuButton(panelMain.transform, "QUIT", new Vector2(0f, -202f), QuitGame);
        // THE fly-out: buttons sweep in one after another from the left
        UiFlyIn.Add(b1, new Vector2(-750f, 0f), 0.16f);
        UiFlyIn.Add(b2, new Vector2(-750f, 0f), 0.22f);
        UiFlyIn.Add(b3, new Vector2(-750f, 0f), 0.28f);
        UiFlyIn.Add(b4, new Vector2(-750f, 0f), 0.34f);
        UiFlyIn.Add(b5, new Vector2(-750f, 0f), 0.40f);
        UiFlyIn.Add(b6, new Vector2(-750f, 0f), 0.46f);

        UiLabel hintLine = UiLabel.Create(panelMain.transform, "Hints", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                          new Vector2(0f, -262f), new Vector2(1100f, 26f), 15, false, TextAnchor.MiddleCenter);
        hintLine.text = "shortcuts:  ENTER mission    V team battle    F quick battle    C controls    1/2/3 difficulty    ESC back / quit";
        hintLine.color = new Color(0.55f, 0.62f, 0.75f, 0.8f);
        UiFlyIn.Add(hintLine.gameObject, new Vector2(0f, -160f), 0.55f, 0.3f);

        // -- panel 2: SETTINGS --
        panelSettings = MakeSubPanel(menuPanel.transform, "Settings");
        UiLabel setTitle = UiLabel.Create(panelSettings.transform, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                          new Vector2(0f, 220f), new Vector2(600f, 70f), 54, true, TextAnchor.MiddleCenter);
        setTitle.text = "SETTINGS";
        UiFlyIn.Add(setTitle.gameObject, new Vector2(0f, 200f), 0f, 0.25f);
        MakeAccent(panelSettings.transform, new Vector2(0f, 178f), new Vector2(360f, 5f));

        setMasterVal = MakeSettingRow(panelSettings.transform, "SOUND VOLUME", 108f,
            () => { GameSettings.MasterVolume = Mathf.Clamp01(GameSettings.MasterVolume - 0.1f); },
            () => { GameSettings.MasterVolume = Mathf.Clamp01(GameSettings.MasterVolume + 0.1f); });
        setMusicVal = MakeSettingRow(panelSettings.transform, "MUSIC VOLUME", 48f,
            () => { GameSettings.MusicVolume = Mathf.Clamp01(GameSettings.MusicVolume - 0.1f); },
            () => { GameSettings.MusicVolume = Mathf.Clamp01(GameSettings.MusicVolume + 0.1f); });
        setDiffVal = MakeSettingRow(panelSettings.transform, "DIFFICULTY", -12f,
            () => { GameSettings.Difficulty = Mathf.Max(0, GameSettings.Difficulty - 1); },
            () => { GameSettings.Difficulty = Mathf.Min(2, GameSettings.Difficulty + 1); });
        setTimeVal = MakeSettingRow(panelSettings.transform, "ROUND TIME", -72f,
            () => { GameSettings.MatchSeconds = GameSettings.MatchSeconds <= 60f ? 60f : GameSettings.MatchSeconds - 30f; },
            () => { GameSettings.MatchSeconds = GameSettings.MatchSeconds >= 300f ? 300f : GameSettings.MatchSeconds + 30f; });
        setShakeVal = MakeSettingRow(panelSettings.transform, "SCREEN SHAKE", -132f,
            () => { GameSettings.ScreenShake = !GameSettings.ScreenShake; },
            () => { GameSettings.ScreenShake = !GameSettings.ScreenShake; });

        MakeMenuButton(panelSettings.transform, "BACK", new Vector2(0f, -220f), () =>
        {
            if (settingsFromPause) CloseSettingsToPause();
            else ShowMenuPanel(0);
        });

        // -- panel 3: CONTROLS --
        panelControls = MakeSubPanel(menuPanel.transform, "Controls");
        UiLabel ctrlTitle = UiLabel.Create(panelControls.transform, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                           new Vector2(0f, 230f), new Vector2(600f, 70f), 54, true, TextAnchor.MiddleCenter);
        ctrlTitle.text = "CONTROLS";
        MakeAccent(panelControls.transform, new Vector2(0f, 190f), new Vector2(360f, 5f));
        menuBody = UiLabel.Create(panelControls.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, -10f), new Vector2(1250f, 330f), 20, false, TextAnchor.UpperCenter);
        menuBody.text =
            "WASD move   SPACE rise   SHIFT boost dash (2x speed!)   double-tap A/D boost step (breaks shot tracking)\n" +
            "LEFT CLICK melee (keep clicking to combo)   RIGHT CLICK shoot (hold 1s = charge shot)\n" +
            "SHOOT during a combo = gun-smash ender   F during a boost dash = TACKLE (see its HUD bar)\n" +
            "E - GIANT LASER + blast sphere (hover stance)   R - FUNNEL BARRAGE   Q shield (roots you, has a cooldown)   T tutorial\n" +
            "Q, E and R all fire straight out of a boost dash - R keeps the dash going\n" +
            "B - BURST at half gauge: full boost + all cooldowns refreshed + bonus damage\n" +
            "G near a tree / barrel / car / antenna = THROW IT   SPACE while downed = get up faster\n" +
            "Rounds are timed - most armour left wins on time up. Buildings break. Rooftops are landable.\n" +
            "TAB - switch which enemy you are locked on to (matters most in a 2v2)\n" +
            "MISSION: three stages, each enemy tougher than the last. Clear all three for an S rank.\n" +
            "2 V 2: you + an ally against two hostiles. FRIENDLY FIRE IS ON - your partner takes reduced damage.";
        MakeMenuButton(panelControls.transform, "BACK", new Vector2(0f, -230f), () => ShowMenuPanel(0));

        RefreshSettingsLabels();

        // ---- PAUSE PANEL (in-fight ESC): translucent - the frozen battle shows through ----
        pausePanel = MakePanel(canvasGo.transform, "Pause Panel", new Color(0.02f, 0.04f, 0.08f, 0.72f));
        UiLabel pauseTitle = UiLabel.Create(pausePanel.transform, "T", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                            new Vector2(0f, 170f), new Vector2(700f, 80f), 58, true, TextAnchor.MiddleCenter);
        pauseTitle.text = "P A U S E D";
        UiFlyIn.Add(pauseTitle.gameObject, new Vector2(0f, 220f), 0f, 0.22f);
        GameObject pbResume = MakeMenuButton(pausePanel.transform, "RESUME", new Vector2(0f, 40f), ResumeFromPause);
        GameObject pbSettings = MakeMenuButton(pausePanel.transform, "SETTINGS", new Vector2(0f, -24f), OpenSettingsFromPause);
        GameObject pbQuit = MakeMenuButton(pausePanel.transform, "QUIT TO MENU", new Vector2(0f, -88f), QuitToMenu);
        UiFlyIn.Add(pbResume, new Vector2(-650f, 0f), 0.06f);
        UiFlyIn.Add(pbSettings, new Vector2(-650f, 0f), 0.12f);
        UiFlyIn.Add(pbQuit, new Vector2(-650f, 0f), 0.18f);
        pausePanel.SetActive(false);

        // ---- game over: rank + stats + title ----
        overPanel = MakePanel(canvasGo.transform, "Result Screen", new Color(0.03f, 0.05f, 0.09f, 0.85f));
        overRank = UiLabel.Create(overPanel.transform, "Rank", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, 205f), new Vector2(260f, 170f), 130, true, TextAnchor.MiddleCenter);
        overRank.gameObject.SetActive(false);
        UiFlyIn.Add(overRank.gameObject, new Vector2(0f, 420f), 0.05f, 0.32f);
        overTitle = UiLabel.Create(overPanel.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 60f), new Vector2(1200f, 90f), 64, true, TextAnchor.MiddleCenter);
        overStats = UiLabel.Create(overPanel.transform, "Stats", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, -30f), new Vector2(1500f, 36f), 22, false, TextAnchor.MiddleCenter);
        overStats.color = new Color(0.85f, 0.9f, 1f);
        UiFlyIn.Add(overTitle.gameObject, new Vector2(-900f, 0f), 0.15f, 0.3f);
        UiFlyIn.Add(overStats.gameObject, new Vector2(900f, 0f), 0.28f, 0.3f);
        overBody = UiLabel.Create(overPanel.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, -88f), new Vector2(1100f, 40f), 20, false, TextAnchor.MiddleCenter);
        overBody.color = new Color(0.6f, 0.68f, 0.8f);

        // Clickable, so the results screen is not a keyboard riddle. QUIT is the only
        // thing here that closes the game, and it says so.
        GameObject ob1 = MakeMenuButton(overPanel.transform, "REMATCH", new Vector2(0f, -148f), Rematch, 460f, 52f);
        GameObject ob2 = MakeMenuButton(overPanel.transform, "MAIN MENU", new Vector2(0f, -206f), QuitToMenu, 460f, 52f);
        GameObject ob3 = MakeMenuButton(overPanel.transform, "QUIT GAME", new Vector2(0f, -264f), QuitGame, 460f, 52f);
        UiFlyIn.Add(ob1, new Vector2(-700f, 0f), 0.38f);
        UiFlyIn.Add(ob2, new Vector2(-700f, 0f), 0.44f);
        UiFlyIn.Add(ob3, new Vector2(-700f, 0f), 0.50f);

        overPanel.SetActive(false);

        // ---- fight splash ----
        splash = UiLabel.Create(canvasGo.transform, "Splash", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                new Vector2(0f, 120f), new Vector2(900f, 100f), 72, true, TextAnchor.MiddleCenter);
        splash.color = new Color(1f, 0.85f, 0.25f);
        splash.gameObject.SetActive(false);

        // ---- match timer (round clock, top-center) ----
        timerLabel = UiLabel.Create(canvasGo.transform, "Match Timer", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                                    new Vector2(0f, -46f), new Vector2(300f, 60f), 46, true, TextAnchor.MiddleCenter);
        timerLabel.gameObject.SetActive(false);

        // ---- which mission stage you are on, under the clock ----
        stageLabel = UiLabel.Create(canvasGo.transform, "Stage Label", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                                    new Vector2(0f, -84f), new Vector2(620f, 30f), 19, true, TextAnchor.MiddleCenter);
        stageLabel.color = new Color(1f, 0.8f, 0.35f, 0.95f);
        stageLabel.gameObject.SetActive(false);

        // ---- STAGE CLEAR intermission ----
        stagePanel = MakePanel(canvasGo.transform, "Stage Clear", new Color(0.02f, 0.05f, 0.10f, 0.88f));
        stageTitle = UiLabel.Create(stagePanel.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    new Vector2(0f, 215f), new Vector2(1300f, 90f), 66, true, TextAnchor.MiddleCenter);
        stageTitle.color = new Color(0.45f, 0.95f, 0.6f);
        MakeAccent(stagePanel.transform, new Vector2(0f, 158f), new Vector2(700f, 5f));
        stageBody = UiLabel.Create(stagePanel.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 40f), new Vector2(1500f, 210f), 22, false, TextAnchor.UpperCenter);
        stageBody.color = new Color(0.85f, 0.9f, 1f);

        // ---- field-repair readout: label above a bar that visibly refills ----
        repairLabel = UiLabel.Create(stagePanel.transform, "Repair", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                     new Vector2(0f, -78f), new Vector2(1000f, 32f), 24, true, TextAnchor.MiddleCenter);
        repairLabel.color = new Color(0.55f, 0.95f, 0.65f);

        GameObject barBg = new GameObject("Repair Bar");
        barBg.transform.SetParent(stagePanel.transform, false);
        Image barBgImg = barBg.AddComponent<Image>();
        barBgImg.color = new Color(0.06f, 0.09f, 0.14f, 0.95f);
        barBgImg.raycastTarget = false;
        RectTransform barRt = barBg.GetComponent<RectTransform>();
        barRt.anchorMin = barRt.anchorMax = new Vector2(0.5f, 0.5f);
        barRt.anchoredPosition = new Vector2(0f, -112f);
        barRt.sizeDelta = new Vector2(760f, 24f);

        GameObject barFill = new GameObject("Fill");
        barFill.transform.SetParent(barBg.transform, false);
        repairFill = barFill.AddComponent<Image>();
        repairFill.raycastTarget = false;
        SetFill(repairFill, 0f);

        MakeMenuButton(stagePanel.transform, "LAUNCH NEXT BATTLE", new Vector2(0f, -182f), BeginNextStage, 520f, 56f);
        stageNext = UiLabel.Create(stagePanel.transform, "Hint", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, -240f), new Vector2(900f, 28f), 17, false, TextAnchor.MiddleCenter);
        stageNext.color = new Color(0.6f, 0.68f, 0.8f);
        stagePanel.SetActive(false);

        // ---- FADE OVERLAY: last sibling so it covers every panel above ----
        GameObject fadeGo = new GameObject("Fade");
        fadeGo.transform.SetParent(canvasGo.transform, false);
        fadeOverlay = fadeGo.AddComponent<Image>();
        fadeOverlay.color = new Color(0f, 0f, 0f, 0f);
        fadeOverlay.raycastTarget = false; // never eat clicks, even at full black
        RectTransform fadeRt = fadeGo.GetComponent<RectTransform>();
        fadeRt.anchorMin = Vector2.zero; fadeRt.anchorMax = Vector2.one;
        fadeRt.offsetMin = fadeRt.offsetMax = Vector2.zero;

        fadeTitle = UiLabel.Create(canvasGo.transform, "Fade Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 60f), new Vector2(1400f, 100f), 72, true, TextAnchor.MiddleCenter);
        fadeTitle.color = new Color(1f, 0.85f, 0.3f);
        fadeTitle.gameObject.SetActive(false);
        fadeSub = UiLabel.Create(canvasGo.transform, "Fade Sub", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                 new Vector2(0f, -40f), new Vector2(1400f, 110f), 26, false, TextAnchor.UpperCenter);
        fadeSub.color = new Color(0.85f, 0.9f, 1f);
        fadeSub.gameObject.SetActive(false);
    }

    private GameObject MakePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        Image img = panel.AddComponent<Image>();
        img.color = color;
        RectTransform rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        return panel;
    }
}

/// <summary>Spawns the game flow at play start. Delete the file to remove the menu loop.</summary>
public static class GameFlowBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<GameFlowManager>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Game Flow").AddComponent<GameFlowManager>();
    }
}
