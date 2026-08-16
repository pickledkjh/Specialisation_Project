using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game HUD, laid out like Gundam EXVS (see the shadPS4 EXVS reference shot):
///   - BOTTOM-LEFT: huge numeric armour readout + name plate, with the
///     AWAKENING (burst) gauge underneath it (the "EX" bar position).
///   - BOTTOM-RIGHT: the boost gauge, with the weapon/cooldown SLOTS stacked
///     above it - each slot is a dark plate with a big key letter (or the ammo
///     count), a procedural weapon icon, and a thin cooldown strip.
///   - ENEMY HP: not parked in a corner - it FLOATS NEXT TO THE LOCK-ON CIRCLE,
///     following the enemy on screen (name + HP + down bar), like EXVS's
///     target plate.
/// Spawns itself at play start, no scene setup. All art is procedural.
/// </summary>
public class BattleHUD : MonoBehaviour
{
    private MechController player;
    private BoostManager playerBoost;
    private MechShooter playerShooter;
    private MechHealth playerHealth;
    private MechHealth enemyHealth;
    private SpecialMoves playerSpecials;
    private MechCombat playerCombat;

    private Canvas hudCanvas;

    // bottom-left player block
    private RectTransform playerHpFill, playerHpGhost;
    private Image playerHpImg, playerHpGhostImg;
    private UiLabel playerHpNum, playerLabel;

    // floating enemy plate (follows the lock-on circle)
    private RectTransform enemyPlate;
    private RectTransform enemyHpFill, enemyHpGhost, stunFill;
    private Image enemyHpImg, enemyHpGhostImg;
    private UiLabel enemyLabel, enemyHpNum;

    // boost (bottom-right)
    private RectTransform boostFill;
    private Image boostImg;
    private UiLabel overheatLabel;

    // weapon / cooldown slots (right column, above the boost bar)
    private UiLabel ammoNum, chargeNum, laserKey, funnelKey, tackleKey, shieldKey;
    private Image ammoIcon, chargeIcon, laserIcon, funnelIcon, tackleIcon, shieldIcon;
    private RectTransform ammoStrip, chargeStrip, laserStrip, funnelStrip, tackleStrip, shieldStrip;
    private Image ammoStripImg, chargeStripImg, laserStripImg, funnelStripImg, tackleStripImg, shieldStripImg;

    // Red full-screen flash when the PLAYER takes damage ("you got hit" feedback)
    private Image damageFlash;
    private float damageFlashAlpha;
    private const float DamageFlashDecayPerSec = 1.7f;

    private static readonly Color HpHigh = new Color(0.35f, 0.95f, 0.45f);
    private static readonly Color HpLow = new Color(0.95f, 0.3f, 0.25f);
    private static readonly Color HpDowned = new Color(0.95f, 0.85f, 0.25f);
    private static readonly Color BoostCol = new Color(0.35f, 0.85f, 1f);
    private static readonly Color OverheatCol = new Color(1f, 0.25f, 0.2f);
    private static readonly Color PipFull = new Color(1f, 0.85f, 0.25f);
    private static readonly Color BarBg = new Color(0f, 0f, 0f, 0.55f);
    private static readonly Color GhostCol = new Color(1f, 1f, 1f, 0.55f);
    private static readonly Color SlotDim = new Color(0.55f, 0.6f, 0.7f);

    private void Start()
    {
        player = FindFirstObjectByType<MechController>();
        if (player != null)
        {
            playerBoost = player.GetComponent<BoostManager>();
            playerShooter = player.GetComponent<MechShooter>();
            playerHealth = player.GetComponent<MechHealth>();
            playerSpecials = player.GetComponent<SpecialMoves>();
            playerCombat = player.GetComponent<MechCombat>();
        }
        SimpleMechAI enemy = FindFirstObjectByType<SimpleMechAI>();
        if (enemy != null) enemyHealth = enemy.GetComponent<MechHealth>();

        BuildCanvas();
    }

    private void OnEnable()
    {
        MechHealth.AnyMechDamaged += OnMechDamaged;
    }

    private void OnDisable()
    {
        MechHealth.AnyMechDamaged -= OnMechDamaged;
    }

    private void OnMechDamaged(MechHealth victim, float amount)
    {
        if (victim == null || victim != playerHealth) return;
        damageFlashAlpha = Mathf.Clamp(0.30f + amount * 0.006f, 0.30f, 0.55f);
    }

    private void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("HUD Canvas");
        canvasGo.transform.SetParent(transform, false);
        hudCanvas = canvasGo.AddComponent<Canvas>();
        hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        hudCanvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ================= BOTTOM-LEFT: big armour number =================
        // EXVS reads player HP as one huge number in the corner, not a bar.
        playerHpNum = UiLabel.Create(canvasGo.transform, "Player HP Num", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                     new Vector2(44f, 122f), new Vector2(420f, 104f), 92, true, TextAnchor.MiddleLeft);
        playerHpNum.text = "600";
        // slim bar above the number keeps the white damage-trail read
        playerHpFill = MakeBar(canvasGo.transform, new Vector2(0f, 0f), new Vector2(48f, 184f), new Vector2(330f, 12f), out playerHpImg);
        playerHpGhost = AddGhost(playerHpFill, out playerHpGhostImg);
        playerLabel = UiLabel.Create(canvasGo.transform, "Player Label", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                     new Vector2(48f, 210f), new Vector2(460f, 24f), 18, false, TextAnchor.MiddleLeft);
        playerLabel.text = "RX-78 ARMOUR";
        // The AWAKENING gauge renders just below this block (AwakeningSystem
        // draws it at y=40, bottom-left) - the "EX" bar slot of the layout.

        // ================= FLOATING ENEMY PLATE (next to the lock circle) =================
        GameObject plateGo = new GameObject("Enemy Plate");
        plateGo.transform.SetParent(canvasGo.transform, false);
        enemyPlate = plateGo.AddComponent<RectTransform>();
        enemyPlate.anchorMin = enemyPlate.anchorMax = new Vector2(0f, 0f);
        enemyPlate.pivot = new Vector2(0f, 0.5f);
        enemyPlate.sizeDelta = new Vector2(280f, 64f);

        enemyLabel = UiLabel.Create(plateGo.transform, "Enemy Label", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                    new Vector2(0f, 44f), new Vector2(280f, 20f), 14, true, TextAnchor.MiddleLeft);
        enemyLabel.text = "CRIMSON UNIT";
        enemyHpFill = MakeBar(plateGo.transform, new Vector2(0f, 0f), new Vector2(0f, 24f), new Vector2(190f, 13f), out enemyHpImg);
        enemyHpGhost = AddGhost(enemyHpFill, out enemyHpGhostImg);
        enemyHpNum = UiLabel.Create(plateGo.transform, "Enemy HP Num", new Vector2(0f, 0f), new Vector2(0f, 0.5f),
                                    new Vector2(198f, 24f), new Vector2(80f, 24f), 19, true, TextAnchor.MiddleLeft);
        // the knockdown (stun) bar rides directly under the enemy HP - the
        // "how close to a down" read stays glued to the target, EXVS-style
        stunFill = MakeBar(plateGo.transform, new Vector2(0f, 0f), new Vector2(0f, 8f), new Vector2(190f, 7f), out Image stunImg);
        stunImg.color = HpDowned;

        // ================= BOTTOM-RIGHT: boost gauge =================
        boostFill = MakeBar(canvasGo.transform, new Vector2(1f, 0f), new Vector2(-560f, 46f), new Vector2(520f, 18f), out boostImg);
        boostImg.color = BoostCol;
        Transform boostBg = boostFill.parent;
        for (int i = 1; i <= 3; i++)
        {
            GameObject tick = new GameObject("Tick");
            tick.transform.SetParent(boostBg, false);
            Image tickImg = tick.AddComponent<Image>();
            tickImg.color = new Color(0f, 0f, 0f, 0.6f);
            tickImg.raycastTarget = false;
            RectTransform trt = tick.GetComponent<RectTransform>();
            trt.anchorMin = trt.anchorMax = new Vector2(0.25f * i, 0.5f);
            trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = Vector2.zero;
            trt.sizeDelta = new Vector2(2.5f, 14f);
            tick.transform.SetAsLastSibling();
        }
        UiLabel boostLabel = UiLabel.Create(canvasGo.transform, "Boost Label", new Vector2(1f, 0f), new Vector2(0f, 0.5f),
                                            new Vector2(-560f, 70f), new Vector2(200f, 20f), 14, true, TextAnchor.MiddleLeft);
        boostLabel.text = "BOOST";
        overheatLabel = UiLabel.Create(canvasGo.transform, "Overheat", new Vector2(1f, 0f), new Vector2(0.5f, 0.5f),
                                       new Vector2(-300f, 46f), new Vector2(300f, 24f), 19, true, TextAnchor.MiddleCenter);
        overheatLabel.text = "O V E R H E A T";
        overheatLabel.gameObject.SetActive(false);

        // ================= RIGHT COLUMN: weapon / cooldown slots =================
        // Stacked above the boost bar like the EXVS weapon list: big number or
        // key letter on the left, weapon icon on the right, cooldown strip below.
        // Slot order bottom-up: ammo, then CHARGE SHOT directly above it (they are
        // the same weapon - the charge is what the rifle does when you hold the
        // button), then the specials.
        MakeSlot(canvasGo.transform, 96f, IconSprite(DrawRifle), out ammoNum, out ammoIcon, out ammoStrip, out ammoStripImg);
        MakeSlot(canvasGo.transform, 150f, IconSprite(DrawCharge), out chargeNum, out chargeIcon, out chargeStrip, out chargeStripImg);
        MakeSlot(canvasGo.transform, 204f, IconSprite(DrawLaser), out laserKey, out laserIcon, out laserStrip, out laserStripImg);
        MakeSlot(canvasGo.transform, 258f, IconSprite(DrawFunnel), out funnelKey, out funnelIcon, out funnelStrip, out funnelStripImg);
        MakeSlot(canvasGo.transform, 312f, IconSprite(DrawTackle), out tackleKey, out tackleIcon, out tackleStrip, out tackleStripImg);
        MakeSlot(canvasGo.transform, 366f, IconSprite(DrawShield), out shieldKey, out shieldIcon, out shieldStrip, out shieldStripImg);
        laserKey.text = "E";
        funnelKey.text = "R";
        tackleKey.text = "F";
        shieldKey.text = "Q";

        // ---- damage flash (full-screen, behind everything) ----
        GameObject flashGo = new GameObject("Damage Flash");
        flashGo.transform.SetParent(canvasGo.transform, false);
        flashGo.transform.SetAsFirstSibling();
        damageFlash = flashGo.AddComponent<Image>();
        damageFlash.color = new Color(0.75f, 0.05f, 0.05f, 0f);
        damageFlash.raycastTarget = false;
        RectTransform frt = flashGo.GetComponent<RectTransform>();
        frt.anchorMin = Vector2.zero;
        frt.anchorMax = Vector2.one;
        frt.offsetMin = Vector2.zero;
        frt.offsetMax = Vector2.zero;
    }

    private void Update()
    {
        // Damage flash: spikes on hit (OnMechDamaged), fades out here
        if (damageFlash != null)
        {
            damageFlashAlpha = Mathf.MoveTowards(damageFlashAlpha, 0f, DamageFlashDecayPerSec * Time.deltaTime);
            Color c = damageFlash.color;
            if (c.a != damageFlashAlpha)
            {
                c.a = damageFlashAlpha;
                damageFlash.color = c;
            }
        }

        // ---- bottom-left: big armour number + slim trail bar ----
        UpdateHpBar(playerHpFill, playerHpImg, playerHealth);
        UpdateGhost(playerHpGhost, playerHpFill);
        if (playerHpNum != null && playerHealth != null)
        {
            playerHpNum.text = Mathf.Max(0, Mathf.CeilToInt(playerHealth.currentHealth)).ToString();
            playerHpNum.color = playerHpImg != null ? playerHpImg.color : Color.white;
        }
        if (CostManager.Instance != null && playerLabel != null)
            playerLabel.text = "RX-78 ARMOUR    COST " + CostManager.Instance.Team1Cost;

        // ---- floating enemy plate: follows the lock-on circle ----
        UpdateEnemyPlate();

        // ---- boost gauge with overheat flash ----
        if (boostFill != null && playerBoost != null)
        {
            float f = playerBoost.maxBoost > 0f ? Mathf.Clamp01(playerBoost.currentBoost / playerBoost.maxBoost) : 0f;
            boostFill.localScale = new Vector3(f, 1f, 1f);
            boostImg.color = playerBoost.isOverheated
                ? Color.Lerp(OverheatCol, Color.black, Mathf.PingPong(Time.time * 4f, 0.6f))
                : BoostCol;
            if (playerBoost.isOverheated) boostFill.localScale = new Vector3(1f, 1f, 1f);

            if (overheatLabel != null)
            {
                overheatLabel.gameObject.SetActive(playerBoost.isOverheated);
                if (playerBoost.isOverheated)
                    overheatLabel.color = Color.Lerp(Color.white, new Color(1f, 0.3f, 0.2f), Mathf.PingPong(Time.time * 5f, 1f));
            }
        }

        // ---- weapon / cooldown slots ----
        if (playerShooter != null && ammoNum != null)
        {
            bool reloading = playerShooter.IsReloading;
            if (reloading)
            {
                ammoNum.text = "RLD";
                ammoNum.color = Color.Lerp(new Color(1f, 0.4f, 0.3f), Color.white, Mathf.PingPong(Time.time * 4f, 1f));
                SetStrip(ammoStrip, ammoStripImg, playerShooter.ReloadProgress01, PipFull);
                ammoIcon.color = SlotDim;
            }
            else
            {
                ammoNum.text = playerShooter.currentAmmo.ToString();
                ammoNum.color = playerShooter.currentAmmo > 0 ? PipFull : SlotDim;
                SetStrip(ammoStrip, ammoStripImg, playerShooter.maxAmmo > 0 ? (float)playerShooter.currentAmmo / playerShooter.maxAmmo : 0f, PipFull);
                ammoIcon.color = Color.white;
            }
        }

        // ---- charge shot: fills while the fire button is held ----
        if (playerCombat == null && player != null) playerCombat = player.GetComponent<MechCombat>();
        if (playerCombat != null && chargeNum != null)
        {
            float c = playerCombat.ChargeProgress01;
            bool ready = playerCombat.ChargeReady;
            Color hot = new Color(1f, 0.45f, 0.25f);

            if (c <= 0f)
            {
                chargeNum.text = "HOLD";
                chargeNum.color = SlotDim;
                chargeIcon.color = SlotDim;
                SetStrip(chargeStrip, chargeStripImg, 0f, SlotDim);
            }
            else if (ready)
            {
                chargeNum.text = "FIRE!";
                Color flash = Color.Lerp(hot, Color.white, Mathf.PingPong(Time.time * 6f, 1f));
                chargeNum.color = flash;
                chargeIcon.color = flash;
                SetStrip(chargeStrip, chargeStripImg, 1f, flash);
            }
            else
            {
                chargeNum.text = Mathf.RoundToInt(c * 100f) + "%";
                chargeNum.color = Color.Lerp(SlotDim, hot, c);
                chargeIcon.color = Color.Lerp(SlotDim, hot, c);
                SetStrip(chargeStrip, chargeStripImg, c, Color.Lerp(SlotDim, hot, c));
            }
        }

        if (playerSpecials == null && player != null) playerSpecials = player.GetComponent<SpecialMoves>();
        if (playerSpecials != null)
        {
            UpdateCdSlot(laserKey, laserIcon, laserStrip, laserStripImg, playerSpecials.LaserReady01, new Color(0.4f, 1f, 0.95f));
            UpdateCdSlot(funnelKey, funnelIcon, funnelStrip, funnelStripImg, playerSpecials.FunnelReady01, new Color(1f, 0.55f, 0.95f));
        }
        if (playerCombat == null && player != null) playerCombat = player.GetComponent<MechCombat>();
        if (playerCombat != null)
        {
            UpdateCdSlot(tackleKey, tackleIcon, tackleStrip, tackleStripImg, playerCombat.TackleReady01, new Color(0.95f, 0.95f, 1f));
            Color shieldReadyCol = playerCombat.IsShielding ? new Color(0.45f, 0.9f, 1f) : new Color(0.85f, 0.95f, 1f);
            UpdateCdSlot(shieldKey, shieldIcon, shieldStrip, shieldStripImg, playerCombat.ShieldReady01, shieldReadyCol);
        }
    }

    // Position the enemy plate beside the enemy on screen (right of the lock
    // circle), clamped so it never leaves the view entirely.
    private void UpdateEnemyPlate()
    {
        if (enemyPlate == null) return;

        // The plate follows whoever you are LOCKED ON to, not "the first SimpleMechAI
        // the scene happened to return". With one enemy that is the same mech; with
        // four on the field it is the difference between a HUD and a guess.
        if (TargetSwitcher.Instance != null)
        {
            MechHealth locked = TeamRules.FindHealth(TargetSwitcher.Instance.Current);
            if (locked != null) enemyHealth = locked;
        }
        if (enemyHealth == null || enemyHealth.currentHealth <= 0f)
        {
            MechHealth fallback = playerHealth != null
                ? BattleRoster.NearestOpponent(playerHealth.transform.position, playerHealth.team)
                : null;
            if (fallback != null) enemyHealth = fallback;
        }

        Camera cam = Camera.main;
        bool show = cam != null && enemyHealth != null && hudCanvas != null;
        if (show)
        {
            Vector3 sp = cam.WorldToScreenPoint(enemyHealth.transform.position + Vector3.up * 2.4f);
            show = sp.z > 0f; // behind the camera = hide
            if (show)
            {
                float s = Mathf.Max(0.01f, hudCanvas.scaleFactor);
                float x = sp.x / s + 70f;   // sit to the RIGHT of the lock circle
                float y = sp.y / s + 6f;
                float w = Screen.width / s, h = Screen.height / s;
                x = Mathf.Clamp(x, 20f, w - 290f);
                y = Mathf.Clamp(y, 90f, h - 90f);
                enemyPlate.anchoredPosition = new Vector2(x, y);
            }
        }
        if (enemyPlate.gameObject.activeSelf != show) enemyPlate.gameObject.SetActive(show);
        if (!show) return;

        UpdateHpBar(enemyHpFill, enemyHpImg, enemyHealth);
        UpdateGhost(enemyHpGhost, enemyHpFill);
        if (enemyHpNum != null)
        {
            enemyHpNum.text = Mathf.Max(0, Mathf.CeilToInt(enemyHealth.currentHealth)).ToString();
            enemyHpNum.color = enemyHpImg != null ? enemyHpImg.color : Color.white;
        }
        if (enemyLabel != null)
        {
            string who = TeamRules.TeamModeActive ? enemyHealth.name : "CRIMSON UNIT";
            enemyLabel.text = CostManager.Instance != null
                ? who + "    COST " + CostManager.Instance.Team2Cost
                : who;
        }
        if (stunFill != null)
        {
            float f = enemyHealth.maxKnockdownValue > 0f
                ? Mathf.Clamp01(enemyHealth.currentKnockdownValue / enemyHealth.maxKnockdownValue)
                : 0f;
            stunFill.localScale = new Vector3(f, 1f, 1f); // truthful bar: resets to 0 on a down
        }
    }

    // Shared look for a cooldown slot: bright key + icon when ready, dim while
    // recharging, strip shows the recharge progress.
    private static void UpdateCdSlot(UiLabel key, Image icon, RectTransform strip, Image stripImg, float ready01, Color readyCol)
    {
        bool ready = ready01 >= 1f;
        if (key != null) key.color = ready ? readyCol : SlotDim;
        if (icon != null) icon.color = ready ? Color.white : SlotDim;
        SetStrip(strip, stripImg, ready01, ready ? readyCol : SlotDim);
    }

    private static void SetStrip(RectTransform strip, Image stripImg, float fill01, Color col)
    {
        if (strip != null) strip.localScale = new Vector3(Mathf.Clamp01(fill01), 1f, 1f);
        if (stripImg != null) stripImg.color = col;
    }

    // Ghost trail: snaps UP with the real bar instantly, but bleeds DOWN slowly
    private static void UpdateGhost(RectTransform ghost, RectTransform fill)
    {
        if (ghost == null || fill == null) return;
        float g = ghost.localScale.x, f = fill.localScale.x;
        if (f >= g) g = f;
        else g = Mathf.MoveTowards(g, f, 0.22f * Time.deltaTime);
        ghost.localScale = new Vector3(g, 1f, 1f);
    }

    private void UpdateHpBar(RectTransform fill, Image img, MechHealth health)
    {
        if (fill == null || health == null) return;
        float f = health.maxHealth > 0f ? Mathf.Clamp01(health.currentHealth / health.maxHealth) : 0f;
        fill.localScale = new Vector3(f, 1f, 1f);
        if (img != null)
            img.color = health.isYellowLocked ? HpDowned : Color.Lerp(HpLow, HpHigh, f);
    }

    // ---------- tiny runtime-uGUI helpers ----------

    private GameObject MakeRect(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject go = new GameObject("Bar");
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }

    private RectTransform AddGhost(RectTransform fill, out Image ghostImg)
    {
        GameObject ghost = new GameObject("Ghost");
        ghost.transform.SetParent(fill.parent, false);
        ghostImg = ghost.AddComponent<Image>();
        ghostImg.color = GhostCol;
        ghostImg.raycastTarget = false;
        RectTransform rt = ghost.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);
        rt.pivot = new Vector2(0f, 0.5f);
        ghost.transform.SetSiblingIndex(fill.GetSiblingIndex());
        return rt;
    }

    // A bar = crisp EXVS-style outlined frame > dark background > left-pivoted fill.
    private RectTransform MakeBar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, out Image fillImg)
    {
        GameObject frame = MakeRect(parent, anchor, pos + new Vector2(-2f, 0f), size + new Vector2(4f, 4f));
        frame.name = "Bar Frame";
        Image frameImg = frame.GetComponent<Image>();
        frameImg.color = new Color(0.75f, 0.85f, 1f, 0.35f);
        frameImg.raycastTarget = false;

        GameObject bg = new GameObject("Bar Bg");
        bg.transform.SetParent(frame.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = BarBg;
        bgImg.raycastTarget = false;
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(2f, 2f);
        bgRt.offsetMax = new Vector2(-2f, -2f);

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(bg.transform, false);
        fillImg = fill.AddComponent<Image>();
        fillImg.color = HpHigh;
        fillImg.raycastTarget = false;
        RectTransform rt = fill.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);
        rt.pivot = new Vector2(0f, 0.5f);
        return rt;
    }

    // ---------- EXVS weapon slot: dark plate | big number/key | icon | cd strip ----------

    private void MakeSlot(Transform parent, float y, Sprite icon,
                          out UiLabel numLabel, out Image iconImg,
                          out RectTransform stripFill, out Image stripImg)
    {
        GameObject frame = new GameObject("Slot");
        frame.transform.SetParent(parent, false);
        Image frameImg = frame.AddComponent<Image>();
        frameImg.color = new Color(0.75f, 0.85f, 1f, 0.30f);
        frameImg.raycastTarget = false;
        RectTransform rt = frame.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.anchoredPosition = new Vector2(-40f, y);
        rt.sizeDelta = new Vector2(250f, 46f);

        GameObject bg = new GameObject("Bg");
        bg.transform.SetParent(frame.transform, false);
        Image bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.02f, 0.03f, 0.06f, 0.72f);
        bgImg.raycastTarget = false;
        RectTransform bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = new Vector2(2f, 2f);
        bgRt.offsetMax = new Vector2(-2f, -2f);

        // big number / key letter on the left
        numLabel = UiLabel.Create(bg.transform, "Num", new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                                  new Vector2(16f, 3f), new Vector2(130f, 40f), 30, true, TextAnchor.MiddleLeft);
        numLabel.text = "";

        // weapon icon on the right
        GameObject ic = new GameObject("Icon");
        ic.transform.SetParent(bg.transform, false);
        iconImg = ic.AddComponent<Image>();
        iconImg.sprite = icon;
        iconImg.raycastTarget = false;
        RectTransform irt = ic.GetComponent<RectTransform>();
        irt.anchorMin = irt.anchorMax = new Vector2(1f, 0.5f);
        irt.pivot = new Vector2(1f, 0.5f);
        irt.anchoredPosition = new Vector2(-10f, 2f);
        irt.sizeDelta = new Vector2(36f, 36f);

        // thin cooldown strip along the bottom of the plate
        GameObject strip = new GameObject("Strip");
        strip.transform.SetParent(bg.transform, false);
        stripImg = strip.AddComponent<Image>();
        stripImg.raycastTarget = false;
        stripFill = strip.GetComponent<RectTransform>();
        stripFill.anchorMin = new Vector2(0f, 0f);
        stripFill.anchorMax = new Vector2(1f, 0f);
        stripFill.pivot = new Vector2(0f, 0f);
        stripFill.offsetMin = new Vector2(3f, 3f);
        stripFill.offsetMax = new Vector2(-3f, 8f);
    }

    // ---------- procedural weapon icons (48x48, white; tinted via Image.color) ----------

    private static Sprite IconSprite(System.Action<Texture2D> draw)
    {
        Texture2D t = new Texture2D(48, 48, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[48 * 48];
        for (int i = 0; i < px.Length; i++) px[i] = new Color32(0, 0, 0, 0);
        t.SetPixels32(px);
        draw(t);
        t.Apply();
        t.wrapMode = TextureWrapMode.Clamp;
        return Sprite.Create(t, new Rect(0f, 0f, 48f, 48f), new Vector2(0.5f, 0.5f));
    }

    private static void Px(Texture2D t, int x, int y, int w, int h)
    {
        for (int i = x; i < x + w; i++)
            for (int j = y; j < y + h; j++)
                if (i >= 0 && i < 48 && j >= 0 && j < 48) t.SetPixel(i, j, Color.white);
    }

    private static void DrawRifle(Texture2D t)
    {
        Px(t, 4, 22, 38, 7);   // barrel
        Px(t, 4, 16, 12, 6);   // stock
        Px(t, 18, 12, 7, 10);  // grip
        Px(t, 30, 29, 6, 5);   // sight
        Px(t, 40, 20, 4, 11);  // muzzle block
    }

    // Charge shot: a rifle round with charge arcs stacking behind it
    private static void DrawCharge(Texture2D t)
    {
        Px(t, 26, 18, 16, 14);          // the hot round
        Px(t, 20, 22, 4, 6);            // charge rings building behind it
        Px(t, 13, 20, 4, 10);
        Px(t, 6, 17, 4, 16);
        Px(t, 30, 34, 4, 6);            // sparks
        Px(t, 30, 10, 4, 6);
    }

    private static void DrawLaser(Texture2D t)
    {
        Px(t, 2, 13, 10, 22);  // emitter
        Px(t, 12, 19, 34, 10); // beam core
        Px(t, 14, 31, 30, 3);  // glow line top
        Px(t, 14, 14, 30, 3);  // glow line bottom
    }

    private static void DrawFunnel(Texture2D t)
    {
        Px(t, 6, 24, 9, 16);   // pod left
        Px(t, 20, 8, 9, 16);   // pod center (staggered)
        Px(t, 34, 24, 9, 16);  // pod right
        Px(t, 8, 18, 5, 3);    // thruster sparks
        Px(t, 22, 2, 5, 3);
        Px(t, 36, 18, 5, 3);
    }

    private static void DrawTackle(Texture2D t)
    {
        // double chevron ">>" - the charge
        for (int i = 0; i < 12; i++)
        {
            Px(t, 8 + i, 12 + i, 5, 4);
            Px(t, 8 + i, 32 - i, 5, 4);
            Px(t, 24 + i, 12 + i, 5, 4);
            Px(t, 24 + i, 32 - i, 5, 4);
        }
    }

    private static void DrawShield(Texture2D t)
    {
        Px(t, 12, 22, 24, 18); // shield body
        for (int k = 0; k < 11; k++)
            Px(t, 12 + k, 21 - k, 24 - 2 * k, 1); // tapered point
    }
}

/// <summary>Spawns the HUD at play start - no scene object needed. Delete the file to remove.</summary>
public static class BattleHUDBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<BattleHUD>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return; // not a gameplay scene
        new GameObject("Battle HUD").AddComponent<BattleHUD>();
    }
}
