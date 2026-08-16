using UnityEngine;
using UnityEngine.UI; // Needed for UI Images

/// <summary>
/// Draws the lock-on circle on whatever currentTarget points at, in the three
/// EXVS lock states.
///
///   GREEN  - target is outside red-lock range. Shots fire straight, no homing.
///   RED    - target is inside range. Shots home.
///   YELLOW - target is downed / invulnerable. No lock at all.
///
/// The green/red decision is NOT computed here. It calls MechShooter.IsRedLock -
/// the same method the fire path calls the instant a shot spawns - so the circle
/// and the bullet can never disagree. The old version measured its own distance
/// against its own copy of the range, which is why it sometimes showed green while
/// the shot still tracked.
///
/// Two other things that used to strand the circle are fixed here as well: it now
/// builds its own reticle when none is assigned (an empty Inspector slot meant no
/// visible lock and no error), and it runs after Cinemachine's brain instead of
/// before it, so the circle no longer lags the target during camera moves.
/// </summary>
[DefaultExecutionOrder(200)] // after Cinemachine Brain
public class TargetManager : MonoBehaviour
{
    public Transform currentTarget;
    public Image lockOnReticle; // Drag your crosshair UI image here, or leave empty to auto-build

    // NOTE: these are deliberately NOT called normalLockColor / yellowLockColor any
    // more. Those names carry values baked into the scene file, which override code
    // defaults - renaming them is what makes these three defaults actually apply.
    [Header("Lock Colours")]
    [Tooltip("Inside red-lock range: shots home.")]
    public Color redLockColor = new Color(1f, 0.25f, 0.2f);
    [Tooltip("Outside red-lock range: shots fire straight with no tracking.")]
    public Color greenLockColor = new Color(0.35f, 1f, 0.45f);
    [Tooltip("Target is downed / invulnerable - nothing will connect.")]
    public Color downedLockColor = new Color(1f, 0.85f, 0.2f);

    [Header("Placement")]
    [Tooltip("Height above the target's origin to draw the circle at - 0 puts it at the mech's feet.")]
    public float aimHeight = 1.3f;
    [Tooltip("Build a reticle in code when none is assigned. Without this a missing " +
             "reference means no visible lock at all.")]
    public bool autoBuildReticle = true;
    [Tooltip("Size of the auto-built reticle in reference pixels.")]
    public float reticleSize = 96f;
    [Tooltip("A green lock draws smaller and thinner than a red one, so the state " +
             "reads even at a glance or in a colour-blind-unfriendly moment.")]
    public float greenLockScale = 0.78f;

    private MechCombat playerCombat;
    private MechShooter playerShooter;
    private Transform lastTarget;
    private float snapAt = -99f;
    private bool builtOurOwn;     // only swap sprites on a reticle WE made
    private bool lastWasRed = true;
    private float nextRebindAt = -99f;
    private bool suppressed;      // a LockOnUI is drawing the circle - stay out of its way

    private void Start() { Rebind(); }

    private void Rebind()
    {
        // Throttled: without this, a scene with no MechShooter at all would run a
        // full FindFirstObjectByType sweep every single frame.
        if (Time.unscaledTime < nextRebindAt) return;
        nextRebindAt = Time.unscaledTime + 1f;

        if (playerCombat == null) playerCombat = FindFirstObjectByType<MechCombat>();
        if (playerShooter == null)
        {
            playerShooter = playerCombat != null ? playerCombat.GetComponent<MechShooter>() : null;
            if (playerShooter == null) playerShooter = FindFirstObjectByType<MechShooter>();
        }

        // The project has its own LockOnUI with real green/red/yellow sprites. When
        // that is present it owns the circle outright - drawing a second one here
        // would just be two reticles arguing. This component then does nothing but
        // hold currentTarget for the camera, the homing and the HUD.
        LockOnUI existing = FindFirstObjectByType<LockOnUI>(FindObjectsInactive.Include);
        suppressed = existing != null && existing.lockOnImage != null;
        if (suppressed)
        {
            if (builtOurOwn && lockOnReticle != null) lockOnReticle.enabled = false;
            return;
        }

        if (lockOnReticle == null && autoBuildReticle) BuildReticle();
    }

    /// <summary>Red lock, straight from the shooter that will actually fire the shot.</summary>
    public bool IsRedLock(Transform t)
    {
        if (t == null) return false;
        if (playerShooter != null) return playerShooter.IsRedLock(t);

        // No shooter found (shouldn't happen) - fall back to the melee range value.
        if (playerCombat != null)
            return Vector3.Distance(playerCombat.transform.position, t.position) <= playerCombat.redLockRange &&
                   !playerCombat.IsTargetYellowLocked(t);
        return false;
    }

    private void LateUpdate()
    {
        Rebind(); // throttled internally
        if (suppressed) return;
        if (lockOnReticle == null) return;

        Camera cam = Camera.main;
        bool show = currentTarget != null && cam != null;

        if (show)
        {
            MechHealth th = currentTarget.GetComponentInParent<MechHealth>();
            if (th != null && th.currentHealth <= 0f) show = false;
        }
        if (!show)
        {
            if (lockOnReticle.enabled) lockOnReticle.enabled = false;
            lastTarget = null;
            return;
        }

        Vector3 sp = cam.WorldToScreenPoint(currentTarget.position + Vector3.up * aimHeight);
        if (sp.z <= 0f) { lockOnReticle.enabled = false; return; }

        lockOnReticle.enabled = true;
        lockOnReticle.transform.position = new Vector3(sp.x, sp.y, 0f);

        // A switch should be unmistakable: the circle snaps in from oversized and
        // flashes white for a quarter second. Without it, TAB feels like nothing
        // happened until you fire.
        if (currentTarget != lastTarget)
        {
            lastTarget = currentTarget;
            snapAt = Time.unscaledTime;
        }

        // ---- the three states, in priority order ----
        bool downed = playerCombat != null && playerCombat.IsTargetYellowLocked(currentTarget);
        bool red = !downed && IsRedLock(currentTarget);
        Color c = downed ? downedLockColor : red ? redLockColor : greenLockColor;
        float scale = red || downed ? 1f : greenLockScale;

        // Crossing the range boundary is a real event - flash it like a switch does.
        if (red != lastWasRed)
        {
            lastWasRed = red;
            snapAt = Time.unscaledTime;
        }

        // The auto-built reticle also changes SHAPE: corner ticks only on a lock
        // that will actually track. Never touch a reticle the project supplied.
        if (builtOurOwn)
        {
            Sprite want = ReticleSprite(red || downed);
            if (lockOnReticle.sprite != want) lockOnReticle.sprite = want;
        }

        float since = Time.unscaledTime - snapAt;
        if (since < 0.25f)
        {
            float k = Mathf.Clamp01(since / 0.25f);
            lockOnReticle.transform.localScale = Vector3.one * Mathf.Lerp(scale * 2f, scale, k);
            c = Color.Lerp(Color.white, c, k);
        }
        else
        {
            lockOnReticle.transform.localScale = Vector3.one * scale;
        }
        lockOnReticle.color = c;
    }

    private void BuildReticle()
    {
        GameObject canvasGo = new GameObject("Lock Reticle Canvas");
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 25; // under the flow panels, over the world
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject go = new GameObject("Lock Reticle");
        go.transform.SetParent(canvasGo.transform, false);
        lockOnReticle = go.AddComponent<Image>();
        lockOnReticle.sprite = ReticleSprite(true);
        lockOnReticle.raycastTarget = false;
        lockOnReticle.color = greenLockColor;
        builtOurOwn = true;

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(reticleSize, reticleSize);
    }

    // Two rings drawn in code so no art asset is needed: a plain thin circle for a
    // green lock, and the same circle plus four inward corner ticks for a red one.
    private static Sprite ringPlain, ringTicked;
    private static Sprite ReticleSprite(bool withTicks)
    {
        if (withTicks && ringTicked != null) return ringTicked;
        if (!withTicks && ringPlain != null) return ringPlain;

        const int S = 128;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                Vector2 d = new Vector2(x + 0.5f, y + 0.5f) - c;
                float r = d.magnitude / (S * 0.5f);

                float inner = withTicks ? 0.62f : 0.66f;
                float outer = withTicks ? 0.80f : 0.76f;
                float ring = Mathf.Clamp01(Mathf.InverseLerp(inner, inner + 0.08f, r)) *
                             Mathf.Clamp01(Mathf.InverseLerp(outer + 0.06f, outer, r));

                float tick = 0f;
                if (withTicks)
                {
                    float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
                    bool onDiagonal = Mathf.Abs(Mathf.DeltaAngle(ang, 45f)) < 11f ||
                                      Mathf.Abs(Mathf.DeltaAngle(ang, 135f)) < 11f ||
                                      Mathf.Abs(Mathf.DeltaAngle(ang, -45f)) < 11f ||
                                      Mathf.Abs(Mathf.DeltaAngle(ang, -135f)) < 11f;
                    tick = onDiagonal && r > 0.86f && r < 0.99f ? 1f : 0f;
                }

                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(Mathf.Max(ring, tick))));
            }
        }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
        if (withTicks) ringTicked = sprite; else ringPlain = sprite;
        return sprite;
    }
}
