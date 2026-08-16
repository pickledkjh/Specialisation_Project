using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// EXVS-style combat feedback that the base game lacked:
///   - COMBO COUNTER: "N HITS  M DMG" tally under the enemy bar while you string
///     hits together (EXVS shows exactly this; it sells combo mastery)
///   - "DOWN!" banner when the enemy gets floored (and DOWNED! when you are)
///   - INCOMING alert: red warning + beep when an enemy shot is closing in
///     (EXVS's iconic incoming-fire arrows, simplified)
///   - FINISH SLOW-MO: the killing blow drops the game to 30% speed for a beat
/// Self-installing, survives rematch reloads, zero scene setup.
/// </summary>
public class BattleJuice : MonoBehaviour
{
    private MechHealth playerHealth, enemyHealth;

    // combo tally
    private int comboHits;
    private float comboDamage;
    private float lastComboHitAt = -99f;
    private const float ComboLinkSeconds = 2.4f; // gap that still counts as "same combo"
    private UiLabel comboLabel;

    // banners
    private UiLabel banner;
    private float bannerUntil = -1f;
    private bool prevEnemyDown, prevPlayerDown;

    // incoming-shot alert
    private UiLabel alertLabel;
    private float alertUntil = -1f;
    private float nextScanAt;
    private readonly Dictionary<HomingProjectile, float> lastShotDistance = new Dictionary<HomingProjectile, float>();
    private readonly List<HomingProjectile> deadShots = new List<HomingProjectile>();

    // finish slow-mo
    private bool prevPlayerDead, prevEnemyDead;
    private bool slowmoRunning;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<BattleJuice>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Battle Juice").AddComponent<BattleJuice>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        BuildUi();
        Rehook();
    }

    private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    private void OnEnable() { MechHealth.AnyMechDamaged += OnMechDamaged; }
    private void OnDisable() { MechHealth.AnyMechDamaged -= OnMechDamaged; }
    private void OnSceneLoaded(Scene s, LoadSceneMode m) { Rehook(); }

    private void Rehook()
    {
        MechController player = Object.FindFirstObjectByType<MechController>();
        playerHealth = player != null ? player.GetComponent<MechHealth>() : null;
        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        enemyHealth = enemy != null ? enemy.GetComponent<MechHealth>() : null;
        prevEnemyDown = prevPlayerDown = prevPlayerDead = prevEnemyDead = false;
        comboHits = 0; comboDamage = 0f;
        lastShotDistance.Clear();
    }

    // ---------- combo tally ----------

    private void OnMechDamaged(MechHealth victim, float amount)
    {
        if (victim == null || enemyHealth == null || victim != enemyHealth) return;
        if (Time.time - lastComboHitAt > ComboLinkSeconds) { comboHits = 0; comboDamage = 0f; }
        comboHits++;
        comboDamage += amount;
        lastComboHitAt = Time.time;
    }

    // ---------- per-frame ----------

    private void Update()
    {
        if (playerHealth == null || enemyHealth == null) { Rehook(); if (playerHealth == null) return; }

        UpdateComboLabel();
        UpdateBanners();
        UpdateIncomingAlert();
        UpdateFinishSlowmo();
    }

    private void UpdateComboLabel()
    {
        if (comboLabel == null) return;
        float sinceHit = Time.time - lastComboHitAt;
        bool show = comboHits >= 2 && sinceHit < ComboLinkSeconds;
        comboLabel.gameObject.SetActive(show);
        if (show)
        {
            comboLabel.text = comboHits + " HITS   " + Mathf.RoundToInt(comboDamage) + " DMG";
            // solid while linking, fades out in the last 40% of the window
            float a = sinceHit < ComboLinkSeconds * 0.6f ? 1f
                : 1f - (sinceHit - ComboLinkSeconds * 0.6f) / (ComboLinkSeconds * 0.4f);
            comboLabel.color = new Color(1f, 0.85f, 0.25f, Mathf.Clamp01(a));
        }
    }

    private void UpdateBanners()
    {
        bool eDown = enemyHealth.isYellowLocked && enemyHealth.currentHealth > 0f;
        if (eDown && !prevEnemyDown) ShowBanner("DOWN!", new Color(1f, 0.85f, 0.25f), 1.1f);
        prevEnemyDown = eDown;

        bool pDown = playerHealth.isYellowLocked && playerHealth.currentHealth > 0f;
        if (pDown && !prevPlayerDown) ShowBanner("DOWNED!  (SPACE = get up faster)", new Color(1f, 0.4f, 0.3f), 1.4f);
        prevPlayerDown = pDown;

        if (banner != null && bannerUntil > 0f && Time.unscaledTime > bannerUntil)
        {
            banner.gameObject.SetActive(false);
            bannerUntil = -1f;
        }
    }

    private void ShowBanner(string text, Color color, float seconds)
    {
        if (banner == null) return;
        banner.text = text;
        banner.color = color;
        banner.gameObject.SetActive(true);
        bannerUntil = Time.unscaledTime + seconds;
    }

    private void UpdateIncomingAlert()
    {
        // Scan a few times a second - projectile counts are tiny
        if (Time.time >= nextScanAt)
        {
            nextScanAt = Time.time + 0.12f;
            Vector3 myPos = playerHealth.transform.position;

            deadShots.Clear();
            foreach (KeyValuePair<HomingProjectile, float> kv in lastShotDistance)
                if (kv.Key == null) deadShots.Add(kv.Key);
            foreach (HomingProjectile dead in deadShots) lastShotDistance.Remove(dead);

            HomingProjectile[] shots = Object.FindObjectsByType<HomingProjectile>(FindObjectsSortMode.None);
            foreach (HomingProjectile shot in shots)
            {
                if (shot == null) continue;
                float dist = Vector3.Distance(shot.transform.position, myPos);
                float prev;
                bool known = lastShotDistance.TryGetValue(shot, out prev);
                lastShotDistance[shot] = dist;
                if (!known) continue;                 // need two samples to know direction
                if (dist > 30f || dist < 3f) continue; // too far to matter / already on top of us
                if (prev - dist > 0.5f)                // closing in
                {
                    if (alertUntil < Time.unscaledTime) BattleAudio.Play("alert", 0.8f);
                    alertUntil = Time.unscaledTime + 0.45f;
                }
            }
        }

        if (alertLabel != null)
        {
            bool show = Time.unscaledTime < alertUntil;
            alertLabel.gameObject.SetActive(show);
            if (show)
                alertLabel.color = new Color(1f, 0.25f, 0.2f, 0.55f + Mathf.PingPong(Time.unscaledTime * 6f, 0.45f));
        }
    }

    private void UpdateFinishSlowmo()
    {
        bool eDead = enemyHealth.currentHealth <= 0f;
        if (eDead && !prevEnemyDead) StartCoroutine(FinishSlowmo());
        prevEnemyDead = eDead;

        bool pDead = playerHealth.currentHealth <= 0f;
        if (pDead && !prevPlayerDead) StartCoroutine(FinishSlowmo());
        prevPlayerDead = pDead;
    }

    private IEnumerator FinishSlowmo()
    {
        if (slowmoRunning || Time.timeScale < 0.9f) yield break; // already slowed / in a menu
        slowmoRunning = true;

        float normalFixed = Time.fixedDeltaTime;
        Time.timeScale = 0.3f;
        Time.fixedDeltaTime = normalFixed * 0.3f;
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.SpecialKick(1.8f, 1.2f);

        yield return new WaitForSecondsRealtime(1.1f);

        // Only restore if nothing else (menu pause) claimed the timescale meanwhile
        if (Mathf.Abs(Time.timeScale - 0.3f) < 0.01f) Time.timeScale = 1f;
        Time.fixedDeltaTime = normalFixed;
        slowmoRunning = false;
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("Juice Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // combo tally under the enemy HP + knockdown bars (they end at y=-94)
        comboLabel = UiLabel.Create(canvasGo.transform, "Combo", new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                                    new Vector2(-560f, -116f), new Vector2(520f, 30f), 24, true, TextAnchor.MiddleRight);
        comboLabel.gameObject.SetActive(false);

        // DOWN banner - upper middle, below the tutorial strip
        banner = UiLabel.Create(canvasGo.transform, "Down Banner", new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                                new Vector2(0f, -170f), new Vector2(900f, 60f), 44, true, TextAnchor.MiddleCenter);
        banner.gameObject.SetActive(false);

        // incoming-fire alert - centered, low, out of the crosshair's way
        alertLabel = UiLabel.Create(canvasGo.transform, "Incoming", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                                    new Vector2(0f, 130f), new Vector2(600f, 40f), 30, true, TextAnchor.MiddleCenter);
        alertLabel.text = "!  INCOMING  !";
        alertLabel.gameObject.SetActive(false);
    }
}
