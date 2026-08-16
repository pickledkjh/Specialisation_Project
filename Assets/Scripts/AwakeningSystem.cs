using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// EXVS-style AWAKENING (burst). The one headline mechanic both Gundam EXVS and
/// Starward have that this game lacked. The gauge builds from dealing damage and
/// (faster) from taking it; at 50%+ press B to burst:
///   - boost gauge instantly refills (the classic EXVS burst reset)
///   - +15% damage dealt, -15% damage taken while active
///   - boost regenerates faster during the burst
///   - golden aura + rising sound + gauge drains over the burst duration
/// Duration scales with the gauge spent (50% ~ 5s, 100% ~ 9s), so banking the
/// gauge is a real decision - exactly the EXVS mind-game.
///
/// Self-installing (no scene setup), survives rematch reloads, builds its own
/// gauge bar bottom-left under the big armour number (EXVS "EX" bar position).
/// </summary>
public class AwakeningSystem : MonoBehaviour
{
    public static AwakeningSystem Instance { get; private set; }

    [Header("Gauge")]
    public float gauge;                    // 0..100
    public float gainPerDamageDealt = 0.45f;
    public float gainPerDamageTaken = 0.75f;
    public float minToActivate = 50f;

    [Header("Burst")]
    public float damageDealtScale = 1.15f;
    public float damageTakenScale = 0.85f;
    public float boostRegenBonus = 14f;    // extra boost per second while bursting

    private bool burstActive;
    private float burstEndsAt;
    private float burstDuration;

    private MechController player;
    private BoostManager playerBoost;
    private MechHealth playerHealth;
    private SpecialMoves playerSpecials;
    private MechCombat playerCombat;
    private InputAction burstAction;

    /// <summary>True while the player's burst is running (cooldown systems read this).</summary>
    public bool IsBurstActive => burstActive;

    private Transform aura;
    private Light auraLight;

    // ---- EXVS awakening CUT-IN: game freezes, a gold banner slams across the
    // screen with the call-out, then combat resumes with the aura up ----
    private GameObject cutinRoot;
    private RectTransform cutinBanner;
    private Image cutinFlash;
    private UiLabel cutinTitle, cutinSub;
    private RawImage cutinPortrait;
    private RectTransform cutinPortraitRt;
    private Image cutinSlash;
    private RenderTexture portraitRt;
    private Camera portraitCam;

    private RectTransform gaugeFill;
    private Image gaugeImg;
    private UiLabel gaugeLabel;

    private static readonly Color GaugeCharging = new Color(0.85f, 0.6f, 0.2f);
    private static readonly Color GaugeReady = new Color(1f, 0.85f, 0.25f);

    // ---------- lifetime ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<AwakeningSystem>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Awakening System").AddComponent<AwakeningSystem>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        burstAction = new InputAction("Burst", InputActionType.Button);
        burstAction.AddBinding("<Keyboard>/b");
        burstAction.Enable();

        BuildUi();
        Rehook();
    }

    private void OnDestroy()
    {
        if (portraitCam != null) portraitCam.targetTexture = null;
        if (portraitRt != null) { portraitRt.Release(); Object.Destroy(portraitRt); portraitRt = null; }
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        burstAction?.Disable();
    }

    private void OnEnable() { MechHealth.AnyMechDamaged += OnMechDamaged; }
    private void OnDisable() { MechHealth.AnyMechDamaged -= OnMechDamaged; }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) { Rehook(); }

    private void Rehook()
    {
        player = Object.FindFirstObjectByType<MechController>();
        playerBoost = player != null ? player.GetComponent<BoostManager>() : null;
        playerHealth = player != null ? player.GetComponent<MechHealth>() : null;
        playerSpecials = player != null ? player.GetComponent<SpecialMoves>() : null;
        playerCombat = player != null ? player.GetComponent<MechCombat>() : null;
        gauge = 0f;
        EndBurst();
    }

    // ---------- gauge + damage scaling ----------

    private void OnMechDamaged(MechHealth victim, float amount)
    {
        if (playerHealth == null || victim == null) return;
        if (burstActive) return; // no charging while spending
        gauge = Mathf.Clamp(gauge + amount * (victim == playerHealth ? gainPerDamageTaken : gainPerDamageDealt), 0f, 100f);
    }

    /// <summary>Called by MechHealth.TakeDamage - scales damage by the player's
    /// burst state. Victim is the mech ABOUT to take the damage.</summary>
    public static float DamageScaleFor(MechHealth victim)
    {
        AwakeningSystem a = Instance;
        if (a == null || !a.burstActive || victim == null || a.playerHealth == null) return 1f;
        return victim == a.playerHealth ? a.damageTakenScale : a.damageDealtScale;
    }

    // ---------- burst ----------

    private void Update()
    {
        if (player == null) { Rehook(); if (player == null) return; }

        // BURST ESCAPE: deliberately NOT gated on being un-hit. Getting comboed is
        // exactly when you want this - it is the genre's panic button, and the whole
        // reason to bank the gauge. Only a real KNOCKDOWN blocks it (IsDownLocked);
        // being staggered mid-combo does not.
        if (!burstActive && gauge >= minToActivate && burstAction.WasPressedThisFrame()
            && playerHealth != null && playerHealth.currentHealth > 0f
            && !playerHealth.IsDownLocked
            && Time.timeScale > 0.5f) // not in menus
        {
            StartBurst();
        }

        if (burstActive)
        {
            // Gauge drains as the visible burst timer
            float remain = Mathf.Max(0f, burstEndsAt - Time.time);
            gauge = burstDuration > 0f ? (remain / burstDuration) * 100f : 0f;

            if (playerBoost != null)
                playerBoost.currentBoost = Mathf.Min(playerBoost.maxBoost,
                    playerBoost.currentBoost + boostRegenBonus * Time.deltaTime);

            // Burst perk: cooldowns recharge 50% faster while the burst runs
            if (playerSpecials != null) playerSpecials.AccelerateCooldowns(0.5f * Time.deltaTime);
            if (playerCombat != null) playerCombat.AccelerateTackleCooldown(0.5f * Time.deltaTime);

            if (aura != null)
            {
                float pulse = 1f + Mathf.Sin(Time.time * 9f) * 0.08f;
                aura.localScale = Vector3.one * 3.2f * pulse;
                if (auraLight != null) auraLight.intensity = 2.4f + Mathf.Sin(Time.time * 9f) * 0.5f;
            }

            if (remain <= 0f || playerHealth == null || playerHealth.currentHealth <= 0f)
                EndBurst();
        }

        UpdateUi();
    }

    [Header("Burst escape")]
    [Tooltip("Seconds of yellow lock granted the instant the burst fires. Nothing can touch you, but you keep full control - this is what breaks a combo you are trapped in.")]
    public float escapeInvulnSeconds = 1f;

    private void StartBurst()
    {
        // ---- ESCAPE FIRST, everything else after ----
        // Yellow lock for a second: hitboxes and projectiles already skip a
        // yellow-locked victim, so the attacker's combo simply stops connecting.
        if (playerHealth != null && escapeInvulnSeconds > 0.01f)
            playerHealth.GrantBurstInvulnerability(escapeInvulnSeconds);

        // Break out of whatever the enemy had you in. Being staggered or mid-hitstop
        // would otherwise leave you standing there invulnerable but unable to act,
        // which defeats the point of escaping.
        if (player != null && player.currentState == MechState.Staggered)
            player.ForceResetAfterDown();
        if (playerCombat != null) playerCombat.ForceResetAfterDown();

        burstActive = true;
        burstDuration = Mathf.Lerp(5f, 9f, Mathf.InverseLerp(50f, 100f, gauge));
        burstEndsAt = Time.time + burstDuration; // scaled clock freezes during the cut-in, so the duration starts AFTER it

        // The classic EXVS burst reward: instant full boost - an escape or an all-in
        if (playerBoost != null) playerBoost.currentBoost = playerBoost.maxBoost;

        // Burst perk: EVERY cooldown refreshes instantly - laser, funnels, tackle.
        // SpecialMoves may self-install a frame later, so re-find if needed.
        if (playerSpecials == null && player != null) playerSpecials = player.GetComponent<SpecialMoves>();
        if (playerCombat == null && player != null) playerCombat = player.GetComponent<MechCombat>();
        if (playerSpecials != null) playerSpecials.RefreshCooldowns();
        if (playerCombat != null) playerCombat.RefreshTackleCooldown();

        BattleAudio.Play("burst", 1f);
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.SpecialKick(1.2f, 0.6f);

        // THE CUT-IN: pause the world, slam the banner, then fight on with the aura
        StartCoroutine(CutInRoutine());
    }

    // Freeze-frame cut-in, animated entirely on unscaled time.
    /// <summary>Points a throwaway camera at the mech's HEAD and renders it to a
    /// texture for the cut-in portrait. Uses the humanoid Head bone when the rig
    /// exposes one, so it frames the face rather than the chest.</summary>
    private void StartPortraitRender()
    {
        if (player == null || cutinPortrait == null) return;

        Transform head = null;
        Animator anim = player.GetComponentInChildren<Animator>();
        if (anim != null && anim.isHuman) head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) head = player.transform;

        if (portraitRt == null)
            portraitRt = new RenderTexture(720, 900, 16) { name = "Burst Portrait RT" };

        if (portraitCam == null)
        {
            GameObject camGo = new GameObject("Burst Portrait Camera");
            camGo.transform.SetParent(transform, false);
            portraitCam = camGo.AddComponent<Camera>();
            portraitCam.clearFlags = CameraClearFlags.SolidColor;
            portraitCam.backgroundColor = new Color(0.03f, 0.06f, 0.16f, 1f); // deep blue like the reference
            portraitCam.fieldOfView = 26f;      // long lens = a real close-up, no distortion
            portraitCam.nearClipPlane = 0.05f;
            portraitCam.targetTexture = portraitRt;
            portraitCam.depth = -50;            // never competes with the main camera
        }

        // Frame the face from the front-left, slightly above eye line
        Vector3 fwd = player.transform.forward;
        Vector3 right = player.transform.right;
        Vector3 focus = head.position;
        portraitCam.transform.position = focus + fwd * 1.55f + right * 0.55f + Vector3.up * 0.12f;
        portraitCam.transform.rotation = Quaternion.LookRotation(focus - portraitCam.transform.position);
        portraitCam.enabled = true;

        cutinPortrait.texture = portraitRt;
        portraitCamFocus = focus;
        portraitCamBase = portraitCam.transform.position;
    }

    private void StopPortraitRender()
    {
        if (portraitCam != null) portraitCam.enabled = false;
    }

    private Vector3 portraitCamFocus, portraitCamBase;

    private System.Collections.IEnumerator CutInRoutine()
    {
        if (cutinRoot == null) { SpawnAura(); yield break; } // UI missing? skip straight to the aura

        StartPortraitRender();
        cutinRoot.SetActive(true);
        float prevScale = Time.timeScale;
        Time.timeScale = 0f;

        // UiLabel is a plain wrapper (not a Component) - go through its gameObject,
        // and scale RELATIVE to its base (the legacy-text fallback bakes a 0.5x trick)
        Transform titleT = cutinTitle != null ? cutinTitle.gameObject.transform : null;
        Vector3 titleBase = titleT != null ? titleT.localScale : Vector3.one;

        // SLAM IN: banner sweeps from the left, white flash, title punches down to size
        float t = 0f;
        while (t < 0.14f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / 0.14f);
            float ease = 1f - (1f - k) * (1f - k);
            cutinBanner.anchoredPosition = new Vector2(Mathf.Lerp(-2600f, 0f, ease), 0f);
            if (cutinFlash != null) cutinFlash.color = new Color(1f, 1f, 1f, 0.85f * (1f - k));
            if (titleT != null) titleT.localScale = titleBase * Mathf.Lerp(1.6f, 1f, ease);
            // portrait wipes in from the left with the banner
            if (cutinPortrait != null) cutinPortrait.color = new Color(1f, 1f, 1f, ease);
            if (cutinPortraitRt != null)
                cutinPortraitRt.anchoredPosition = new Vector2(Mathf.Lerp(-500f, 0f, ease), 0f);
            if (cutinSlash != null) cutinSlash.color = new Color(1f, 1f, 1f, 0.9f * ease);
            yield return null;
        }
        if (titleT != null) titleT.localScale = titleBase;

        // HOLD: slow drift so the frame stays alive
        t = 0f;
        while (t < 0.8f)
        {
            t += Time.unscaledDeltaTime;
            cutinBanner.anchoredPosition = new Vector2(Mathf.Lerp(0f, -45f, t / 0.8f), 0f);
            // SLOW PUSH IN on the face - the zoom that makes it read as a cut-in
            if (portraitCam != null)
            {
                float push = t / 0.8f;
                portraitCam.transform.position =
                    Vector3.Lerp(portraitCamBase, Vector3.Lerp(portraitCamBase, portraitCamFocus, 0.32f), push);
                portraitCam.transform.rotation =
                    Quaternion.LookRotation(portraitCamFocus - portraitCam.transform.position);
                portraitCam.fieldOfView = Mathf.Lerp(26f, 21f, push);
            }
            yield return null;
        }

        // SLAM OUT to the right
        t = 0f;
        while (t < 0.15f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / 0.15f);
            cutinBanner.anchoredPosition = new Vector2(Mathf.Lerp(-45f, 2600f, k * k), 0f);
            // portrait wipes back out to the left
            if (cutinPortrait != null) cutinPortrait.color = new Color(1f, 1f, 1f, 1f - k);
            if (cutinPortraitRt != null)
                cutinPortraitRt.anchoredPosition = new Vector2(Mathf.Lerp(0f, -500f, k * k), 0f);
            if (cutinSlash != null) cutinSlash.color = new Color(1f, 1f, 1f, 0.9f * (1f - k));
            yield return null;
        }

        StopPortraitRender();
        cutinRoot.SetActive(false);
        // Restore time ONLY if nothing else (menu/pause) claimed it meanwhile
        if (Mathf.Abs(Time.timeScale) < 0.01f) Time.timeScale = prevScale > 0.5f ? prevScale : 1f;

        SpawnAura();
    }

    private void SpawnAura()
    {
        // Golden aura sphere + light on the player
        if (player != null)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(go.GetComponent<Collider>());
            go.name = "Burst Aura";
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = Vector3.up * 1.1f;
            go.transform.localScale = Vector3.one * 3.2f;
            Renderer r = go.GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Sprites/Default"));
            r.material.color = new Color(1f, 0.8f, 0.2f, 0.16f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            aura = go.transform;

            GameObject lgo = new GameObject("Burst Light");
            lgo.transform.SetParent(player.transform, false);
            lgo.transform.localPosition = Vector3.up * 1.4f;
            auraLight = lgo.AddComponent<Light>();
            auraLight.type = LightType.Point;
            auraLight.color = new Color(1f, 0.82f, 0.3f);
            auraLight.range = 9f;
            auraLight.intensity = 2.4f;

            // Golden energy rising off the mech for the whole burst (parented to
            // the aura, so EndBurst cleans it up automatically)
            // (size stays small - the aura parent's 3.2x scale multiplies it up)
            burstJet = ProceduralVfx.MakeJet(aura, Vector3.down * 0.3f,
                                             Quaternion.LookRotation(Vector3.up),
                                             new Color(1f, 0.8f, 0.25f), 0.12f);
            var em = burstJet.emission;
            em.rateOverTime = 40f;
            var shape = burstJet.shape;
            shape.angle = 30f;
            shape.radius = 0.45f;
        }
    }

    private ParticleSystem burstJet;

    private void EndBurst()
    {
        burstActive = false;
        if (burstActive == false && gauge > 0f && burstDuration > 0f) gauge = 0f;
        if (aura != null) { Destroy(aura.gameObject); aura = null; }
        if (auraLight != null) { Destroy(auraLight.gameObject); auraLight = null; }
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("Burst Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // EXVS layout: the awakening ("EX") gauge lives BOTTOM-LEFT, right under
        // the big armour number - the boost gauge owns the bottom-right corner.
        GameObject bg = new GameObject("Burst Bar");
        bg.transform.SetParent(canvasGo.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0f, 0f, 0f, 0.55f);
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = bgRt.anchorMax = new Vector2(0f, 0f);
        bgRt.pivot = new Vector2(0f, 0.5f);
        bgRt.anchoredPosition = new Vector2(44f, 42f);
        bgRt.sizeDelta = new Vector2(440f, 16f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(bg.transform, false);
        gaugeImg = fill.AddComponent<Image>();
        gaugeImg.color = GaugeCharging;
        gaugeFill = fill.GetComponent<RectTransform>();
        gaugeFill.anchorMin = Vector2.zero;
        gaugeFill.anchorMax = Vector2.one;
        gaugeFill.offsetMin = new Vector2(2f, 2f);
        gaugeFill.offsetMax = new Vector2(-2f, -2f);
        gaugeFill.pivot = new Vector2(0f, 0.5f);

        gaugeLabel = UiLabel.Create(canvasGo.transform, "Burst Label", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                    new Vector2(494f, 42f), new Vector2(260f, 22f), 15, true, TextAnchor.MiddleLeft);
        gaugeLabel.text = "";

        BuildCutin();
    }

    // The freeze-frame banner: own canvas above the HUD, tilted EXVS-style,
    // gold plate with white speed-stripes and the call-out text. Hidden until fired.
    private void BuildCutin()
    {
        cutinRoot = new GameObject("Burst Cutin Canvas");
        cutinRoot.transform.SetParent(transform, false);
        Canvas canvas = cutinRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 35; // above HUD/juice, below the menu overlay
        CanvasScaler scaler = cutinRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        // white impact flash behind the banner
        GameObject flash = new GameObject("Flash");
        flash.transform.SetParent(cutinRoot.transform, false);
        cutinFlash = flash.AddComponent<Image>();
        cutinFlash.color = new Color(1f, 1f, 1f, 0f);
        cutinFlash.raycastTarget = false;
        RectTransform frt = flash.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        // tilted pivot so the whole banner assembly rides at the EXVS angle
        // PORTRAIT: a live close-up of the mech's head, filling the left of the
        // frame. The reference cut-in puts a big character portrait here; with no
        // pilot art, the mech's own face is the equivalent - and rendering it live
        // from a second camera means it always matches whatever model is in use.
        GameObject portraitGo = new GameObject("Cutin Portrait");
        portraitGo.transform.SetParent(cutinRoot.transform, false);
        cutinPortrait = portraitGo.AddComponent<RawImage>();
        cutinPortrait.raycastTarget = false;
        cutinPortrait.color = new Color(1f, 1f, 1f, 0f);
        RectTransform port = portraitGo.GetComponent<RectTransform>();
        port.anchorMin = new Vector2(0f, 0f);
        port.anchorMax = new Vector2(0.52f, 1f);   // left half, full height
        port.offsetMin = Vector2.zero;
        port.offsetMax = Vector2.zero;
        cutinPortraitRt = port;

        // diagonal white slash separating portrait from scene, like the reference
        GameObject slash = new GameObject("Slash");
        slash.transform.SetParent(cutinRoot.transform, false);
        cutinSlash = slash.AddComponent<Image>();
        cutinSlash.color = new Color(1f, 1f, 1f, 0f);
        cutinSlash.raycastTarget = false;
        RectTransform slrt = slash.GetComponent<RectTransform>();
        slrt.anchorMin = slrt.anchorMax = new Vector2(0.5f, 0.5f);
        slrt.pivot = new Vector2(0.5f, 0.5f);
        slrt.anchoredPosition = new Vector2(-180f, 0f);
        slrt.sizeDelta = new Vector2(26f, 2400f);
        slash.transform.localRotation = Quaternion.Euler(0f, 0f, 18f);

        // BANNER PIVOT - anchored to the LOWER THIRD, not screen centre. The
        // reference banner is a strip across the bottom; a centred one covered the
        // whole frame and hid the fight.
        GameObject pivot = new GameObject("Banner Pivot");
        pivot.transform.SetParent(cutinRoot.transform, false);
        RectTransform prt = pivot.AddComponent<RectTransform>();
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.anchoredPosition = new Vector2(0f, 190f);   // sits in the lower third
        prt.sizeDelta = Vector2.zero;
        pivot.transform.localRotation = Quaternion.Euler(0f, 0f, -4f);

        GameObject banner = new GameObject("Banner");
        banner.transform.SetParent(pivot.transform, false);
        Image bImg = banner.AddComponent<Image>();
        bImg.color = new Color(0.95f, 0.72f, 0.12f, 0.97f);
        bImg.raycastTarget = false;
        cutinBanner = banner.GetComponent<RectTransform>();
        cutinBanner.sizeDelta = new Vector2(2800f, 150f);
        cutinBanner.anchoredPosition = new Vector2(-2600f, 0f);

        // white speed-stripes along the banner edges
        for (int i = 0; i < 2; i++)
        {
            GameObject stripe = new GameObject("Stripe");
            stripe.transform.SetParent(banner.transform, false);
            Image sImg = stripe.AddComponent<Image>();
            sImg.color = new Color(1f, 1f, 1f, 0.9f);
            sImg.raycastTarget = false;
            RectTransform srt = stripe.GetComponent<RectTransform>();
            srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0.5f);
            srt.anchoredPosition = new Vector2(0f, i == 0 ? 82f : -82f);
            srt.sizeDelta = new Vector2(2800f, 8f);
        }

        cutinTitle = UiLabel.Create(banner.transform, "Cutin Title", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                    new Vector2(120f, 14f), new Vector2(1500f, 90f), 68, true, TextAnchor.MiddleCenter);
        cutinTitle.text = "A W A K E N I N G";
        cutinTitle.color = new Color(0.3f, 0.05f, 0.05f);

        cutinSub = UiLabel.Create(banner.transform, "Cutin Sub", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                                  new Vector2(120f, -44f), new Vector2(1200f, 34f), 22, true, TextAnchor.MiddleCenter);
        cutinSub.text = "A R M O U R   B U R S T   S Y S T E M";
        cutinSub.color = new Color(0.45f, 0.12f, 0.08f);

        cutinRoot.SetActive(false);
    }

    private void UpdateUi()
    {
        if (gaugeFill == null) return;
        gaugeFill.localScale = new Vector3(Mathf.Clamp01(gauge / 100f), 1f, 1f);

        if (burstActive)
        {
            gaugeImg.color = Color.Lerp(GaugeReady, Color.white, Mathf.PingPong(Time.unscaledTime * 3f, 0.5f));
            gaugeLabel.text = "BURST!";
            gaugeLabel.color = GaugeReady;
        }
        else if (gauge >= minToActivate)
        {
            gaugeImg.color = Color.Lerp(GaugeCharging, GaugeReady, Mathf.PingPong(Time.unscaledTime * 2.5f, 1f));
            gaugeLabel.text = "B - BURST READY";
            gaugeLabel.color = Color.Lerp(GaugeReady, Color.white, Mathf.PingPong(Time.unscaledTime * 2.5f, 0.6f));
        }
        else
        {
            gaugeImg.color = GaugeCharging;
            gaugeLabel.text = "";
        }
    }
}
