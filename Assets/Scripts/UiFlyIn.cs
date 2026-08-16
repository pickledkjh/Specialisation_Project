using UnityEngine;

/// <summary>
/// One-shot UI entrance animation: the element starts offset + transparent and
/// flies/fades to its real position with a cubic ease-out. Runs on UNSCALED time,
/// so it works on the frozen main menu, the pause screen and the results screen.
/// Re-plays every time the element (or its panel) is re-activated - that's what
/// makes the menu "fly out one by one" on every visit.
/// </summary>
public class UiFlyIn : MonoBehaviour
{
    public Vector2 fromOffset = new Vector2(-500f, 0f);
    public float delay = 0f;
    public float duration = 0.28f;

    private RectTransform rt;
    private CanvasGroup cg;
    private Vector2 target;
    private bool captured;
    private bool done;
    private float t;

    /// <summary>Attach (or retune) a fly-in on any UI GameObject.</summary>
    public static UiFlyIn Add(GameObject go, Vector2 fromOffset, float delay = 0f, float duration = 0.28f)
    {
        UiFlyIn f = go.GetComponent<UiFlyIn>();
        if (f == null) f = go.AddComponent<UiFlyIn>();
        f.fromOffset = fromOffset;
        f.delay = delay;
        f.duration = duration;
        // Restart with the CONFIGURED values. AddComponent fires OnEnable
        // immediately - BEFORE the fields above are set - so on the very first
        // menu build the animation ran with default offset and zero stagger and
        // looked like "the buttons are not animated at the start".
        f.Replay();
        return f;
    }

    /// <summary>Reset and play the entrance from the beginning.</summary>
    public void Replay()
    {
        if (rt == null) rt = GetComponent<RectTransform>();
        if (rt == null) { done = true; return; }
        if (cg == null)
        {
            cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        }
        if (!captured) { target = rt.anchoredPosition; captured = true; }
        t = 0f;
        done = false;
        rt.anchoredPosition = target + fromOffset;
        cg.alpha = 0f;
    }

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        Replay();
    }

    private void Update()
    {
        if (done || rt == null) return;
        // Clamp the frame delta: the scene-load / menu-build frame can take a
        // second or more of REAL time, which used to complete the whole entrance
        // in one invisible step on the first menu show.
        t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        float k = Mathf.Clamp01((t - delay) / Mathf.Max(0.01f, duration));
        float ease = 1f - (1f - k) * (1f - k) * (1f - k); // cubic out - fast arrival, soft settle
        rt.anchoredPosition = target + fromOffset * (1f - ease);
        if (cg != null) cg.alpha = Mathf.Clamp01(k * 1.5f);
        if (k >= 1f)
        {
            rt.anchoredPosition = target;
            if (cg != null) cg.alpha = 1f;
            done = true;
        }
    }
}
