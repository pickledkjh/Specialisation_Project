using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// In-game HUD: HP bars for both mechs, the enemy knockdown bar, boost gauge with
/// overheat warning, and ammo display. Spawns itself at play start, no scene setup.
/// </summary>
public class BattleHUD : MonoBehaviour
{
    private MechController player;
    private BoostManager playerBoost;
    private MechShooter playerShooter;
    private MechHealth playerHealth;
    private MechHealth enemyHealth;

    private RectTransform playerHpFill, enemyHpFill, stunFill, boostFill, reloadFill;
    private Image playerHpImg, enemyHpImg, boostImg;
    private Image[] ammoPips;
    private UiLabel playerLabel, enemyLabel, ammoLabel;

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
    private static readonly Color PipEmpty = new Color(0.25f, 0.25f, 0.25f, 0.8f);
    private static readonly Color BarBg = new Color(0f, 0f, 0f, 0.55f);

    private void Start()
    {
        player = FindFirstObjectByType<MechController>();
        if (player != null)
        {
            playerBoost = player.GetComponent<BoostManager>();
            playerShooter = player.GetComponent<MechShooter>();
            playerHealth = player.GetComponent<MechHealth>();
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
        // Only the PLAYER getting hit flashes the screen; enemy hits already read
        // through the impact VFX and their HP bar.
        if (victim == null || victim != playerHealth) return;
        // Bigger hits flash harder (a charge shot reads ~2x a rifle shot)
        damageFlashAlpha = Mathf.Clamp(0.30f + amount * 0.006f, 0.30f, 0.55f);
    }

    private void BuildCanvas()
    {
        GameObject canvasGo = new GameObject("HUD Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // ---- player HP (top-left) ----
        playerHpFill = MakeBar(canvasGo.transform, new Vector2(0f, 1f), new Vector2(40f, -50f), new Vector2(520f, 26f), out playerHpImg);
        playerLabel = UiLabel.Create(canvasGo.transform, "Player Label", new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                                     new Vector2(40f, -22f), new Vector2(300f, 26f), 20, false, TextAnchor.MiddleLeft);
        playerLabel.text = "PLAYER";

        // ---- enemy HP + knockdown bar (top-right) ----
        enemyHpFill = MakeBar(canvasGo.transform, new Vector2(1f, 1f), new Vector2(-560f, -50f), new Vector2(520f, 26f), out enemyHpImg);
        stunFill = MakeBar(canvasGo.transform, new Vector2(1f, 1f), new Vector2(-560f, -84f), new Vector2(520f, 10f), out Image stunImg);
        stunImg.color = HpDowned;
        enemyLabel = UiLabel.Create(canvasGo.transform, "Enemy Label", new Vector2(1f, 1f), new Vector2(0f, 0.5f),
                                    new Vector2(-560f, -22f), new Vector2(300f, 26f), 20, false, TextAnchor.MiddleLeft);
        enemyLabel.text = "ENEMY";

        // ---- boost gauge (bottom-center) ----
        boostFill = MakeBar(canvasGo.transform, new Vector2(0.5f, 0f), new Vector2(-330f, 46f), new Vector2(660f, 20f), out boostImg);
        boostImg.color = BoostCol;

        // ---- ammo pips + reload bar (bottom-right) ----
        int pipCount = playerShooter != null ? Mathf.Max(1, playerShooter.maxAmmo) : 8;
        ammoPips = new Image[pipCount];
        for (int i = 0; i < pipCount; i++)
        {
            GameObject pip = MakeRect(canvasGo.transform, new Vector2(1f, 0f), new Vector2(-40f - (pipCount - 1 - i) * 30f, 60f), new Vector2(22f, 22f));
            ammoPips[i] = pip.GetComponent<Image>();
            ammoPips[i].color = PipFull;
        }
        reloadFill = MakeBar(canvasGo.transform, new Vector2(1f, 0f), new Vector2(-40f - (pipCount - 1) * 30f - 22f + 22f, 34f), new Vector2(pipCount * 30f - 8f, 8f), out Image reloadImg);
        reloadImg.color = PipFull;
        ammoLabel = UiLabel.Create(canvasGo.transform, "Ammo Label", new Vector2(1f, 0f), new Vector2(0f, 0.5f),
                                   new Vector2(-40f - (pipCount - 1) * 30f, 92f), new Vector2(300f, 26f), 18, false, TextAnchor.MiddleLeft);
        ammoLabel.text = "AMMO";

        // ---- damage flash (full-screen, behind the bars so the HUD stays readable) ----
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

        // HP bars
        UpdateHpBar(playerHpFill, playerHpImg, playerHealth);
        UpdateHpBar(enemyHpFill, enemyHpImg, enemyHealth);

        // Enemy knockdown (stun) bar - the hidden bar, visible
        if (stunFill != null && enemyHealth != null)
        {
            float f = enemyHealth.maxKnockdownValue > 0f
                ? Mathf.Clamp01(enemyHealth.currentKnockdownValue / enemyHealth.maxKnockdownValue)
                : 0f;
            stunFill.localScale = new Vector3(enemyHealth.isYellowLocked ? 1f : f, 1f, 1f);
        }

        // Boost gauge with overheat flash
        if (boostFill != null && playerBoost != null)
        {
            float f = playerBoost.maxBoost > 0f ? Mathf.Clamp01(playerBoost.currentBoost / playerBoost.maxBoost) : 0f;
            boostFill.localScale = new Vector3(f, 1f, 1f);
            boostImg.color = playerBoost.isOverheated
                ? Color.Lerp(OverheatCol, Color.black, Mathf.PingPong(Time.time * 4f, 0.6f))
                : BoostCol;
            if (playerBoost.isOverheated) boostFill.localScale = new Vector3(1f, 1f, 1f); // full red bar reads clearer than a sliver
        }

        // Ammo pips + reload progress
        if (ammoPips != null && playerShooter != null)
        {
            for (int i = 0; i < ammoPips.Length; i++)
                if (ammoPips[i] != null) ammoPips[i].color = i < playerShooter.currentAmmo ? PipFull : PipEmpty;

            if (reloadFill != null)
            {
                bool reloading = playerShooter.IsReloading;
                reloadFill.parent.gameObject.SetActive(reloading);
                if (reloading) reloadFill.localScale = new Vector3(playerShooter.ReloadProgress01, 1f, 1f);
            }
        }
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
        Image img = go.AddComponent<Image>(); // no sprite = plain tintable rect
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }

    // A bar = background rect + left-pivoted fill rect (scaled 0..1 on X)
    private RectTransform MakeBar(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, out Image fillImg)
    {
        GameObject bg = MakeRect(parent, anchor, pos, size);
        bg.GetComponent<Image>().color = BarBg;

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(bg.transform, false);
        fillImg = fill.AddComponent<Image>();
        fillImg.color = HpHigh;
        RectTransform rt = fill.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = new Vector2(2f, 2f);
        rt.offsetMax = new Vector2(-2f, -2f);
        rt.pivot = new Vector2(0f, 0.5f);
        return rt;
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
