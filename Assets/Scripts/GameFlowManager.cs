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
    private enum Phase { Menu, Tutorial, Fight, GameOver }
    private Phase phase = Phase.Menu;

    private MechHealth playerHealth, enemyHealth;
    private TutorialManager tutorial;

    private InputAction startAction, skipAction, restartAction, quitAction;
    private GameObject menuPanel, overPanel;
    private UiLabel menuTitle, menuBody, overTitle, overBody, splash;
    private float splashUntil = -1f;
    private float tutorialDoneAt = -1f;

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
        startAction.Enable(); skipAction.Enable(); restartAction.Enable(); quitAction.Enable();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        startAction?.Disable(); skipAction?.Disable(); restartAction?.Disable(); quitAction?.Disable();
    }

    private void Start()
    {
        BuildUi();
        HookScene();
        EnterMenu();
    }

    // After a rematch reload the bootstrapped helpers are gone (RuntimeInitialize
    // only fires once per session) - recreate whatever is missing, then re-hook.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
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

        HookScene();
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
    }

    private void EnterMenu()
    {
        phase = Phase.Menu;
        Time.timeScale = 0f;
        if (menuPanel != null) menuPanel.SetActive(true);
        if (overPanel != null) overPanel.SetActive(false);
    }

    private void Update()
    {
        // Splash hides on time no matter which phase we're in
        if (splash != null && splashUntil > 0f && Time.unscaledTime > splashUntil)
        {
            splash.gameObject.SetActive(false);
            splashUntil = -1f;
        }

        if (quitAction.WasPressedThisFrame() && (phase == Phase.Menu || phase == Phase.GameOver))
        {
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        switch (phase)
        {
            case Phase.Menu:
                if (startAction.WasPressedThisFrame())
                {
                    BeginPlay(withTutorial: true);
                }
                else if (skipAction.WasPressedThisFrame())
                {
                    BeginPlay(withTutorial: false);
                }
                break;

            case Phase.Tutorial:
                if (tutorial == null || !tutorial.IsActive)
                {
                    // player quit the tutorial with T - straight to the fight
                    StartFight("FIGHT!");
                }
                else if (tutorial.CompletedOnce)
                {
                    if (tutorialDoneAt < 0f) tutorialDoneAt = Time.unscaledTime;
                    if (Time.unscaledTime - tutorialDoneAt > 3f)
                    {
                        tutorial.SetTutorialActive(false);
                        StartFight("FIGHT!");
                    }
                }
                break;

            case Phase.Fight:
                if (playerHealth != null && playerHealth.currentHealth <= 0f)
                    EnterGameOver("DEFEAT", "The enemy mech wrecked you. R - rematch    ESC - quit");
                else if (enemyHealth != null && enemyHealth.currentHealth <= 0f)
                    EnterGameOver("VICTORY", "Enemy mech destroyed. R - rematch    ESC - quit");
                break;

            case Phase.GameOver:
                if (restartAction.WasPressedThisFrame())
                {
                    Time.timeScale = 1f;
                    if (overPanel != null) overPanel.SetActive(false);
                    Scene active = SceneManager.GetActiveScene();
                    SceneManager.LoadScene(active.name);
                }
                break;
        }
    }

    private void BeginPlay(bool withTutorial)
    {
        if (menuPanel != null) menuPanel.SetActive(false);
        Time.timeScale = 1f;

        if (withTutorial && tutorial != null)
        {
            tutorial.SetTutorialActive(true);
            phase = Phase.Tutorial;
        }
        else
        {
            StartFight("FIGHT!");
        }
    }

    private void StartFight(string splashText)
    {
        phase = Phase.Fight;
        Time.timeScale = 1f;

        // Fresh start for the real fight: tutorial damage and spent resources are wiped
        RefreshMech(playerHealth);
        RefreshMech(enemyHealth);
        SimpleMechAI enemy = enemyHealth != null ? enemyHealth.GetComponent<SimpleMechAI>() : null;
        if (enemy != null) enemy.RestockForBattle();

        if (splash != null)
        {
            splash.text = splashText;
            splash.gameObject.SetActive(true);
            splashUntil = Time.unscaledTime + 1.1f; // quick flash, not a billboard
        }
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

    private void EnterGameOver(string title, string body)
    {
        phase = Phase.GameOver;
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

        // ---- start menu ----
        menuPanel = MakePanel(canvasGo.transform, "Start Menu", new Color(0.03f, 0.05f, 0.09f, 0.93f));
        menuTitle = UiLabel.Create(menuPanel.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 150f), new Vector2(1200f, 90f), 64, true, TextAnchor.MiddleCenter);
        menuTitle.text = "MECH ARENA";
        menuBody = UiLabel.Create(menuPanel.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, -60f), new Vector2(1100f, 320f), 24, false, TextAnchor.UpperCenter);
        menuBody.text =
            "ENTER  -  start (tutorial first)\n" +
            "F  -  skip tutorial, straight to battle\n" +
            "ESC  -  quit\n\n" +
            "CONTROLS\n" +
            "WASD move   SPACE rise   SHIFT boost dash   double-tap A/D boost step\n" +
            "LEFT CLICK melee (keep clicking to combo)   RIGHT CLICK shoot (hold 1s = charge shot)\n" +
            "Q shield (blocked melee stuns the attacker)   T tutorial";

        // ---- game over ----
        overPanel = MakePanel(canvasGo.transform, "Result Screen", new Color(0.03f, 0.05f, 0.09f, 0.85f));
        overTitle = UiLabel.Create(overPanel.transform, "Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                   new Vector2(0f, 60f), new Vector2(1200f, 90f), 64, true, TextAnchor.MiddleCenter);
        overBody = UiLabel.Create(overPanel.transform, "Body", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(0f, -40f), new Vector2(1100f, 60f), 26, false, TextAnchor.MiddleCenter);
        overPanel.SetActive(false);

        // ---- fight splash ----
        splash = UiLabel.Create(canvasGo.transform, "Splash", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                new Vector2(0f, 120f), new Vector2(900f, 100f), 72, true, TextAnchor.MiddleCenter);
        splash.color = new Color(1f, 0.85f, 0.25f);
        splash.gameObject.SetActive(false);
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
