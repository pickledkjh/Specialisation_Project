using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// THE lock-on circle - the one with your green / red / yellow sprites.
///
/// Two bugs, both from this script deciding things on its own:
///
///  1. enemyTarget was an Inspector field that nothing ever wrote. It pointed at
///     whichever mech you dragged in before play, so in a 2v2 the circle stayed
///     nailed to that one unit no matter how many times you pressed TAB. It now
///     reads the live lock (the same Transform the camera, the homing and the HUD
///     use), and only falls back to the Inspector field if there is no lock system
///     in the scene at all.
///
///  2. The colour was worked out here, from a HORIZONTAL distance measured against
///     MechCombat.redLockRange - while the bullet's homing was decided elsewhere,
///     from a 3D distance measured against MechShooter.redLockRange. Two ranges,
///     two different distance metrics, one circle claiming to describe both. When
///     those two range values drifted apart in the scene you got a green circle and
///     a homing shot. It now calls MechShooter.IsRedLock - the exact method the
///     fire path calls - so the colour and the bullet cannot disagree.
///
/// It also no longer throws a NullReferenceException when lockOnImage is unassigned
/// (the old early-out dereferenced the very field it had just found to be null).
/// </summary>
[DefaultExecutionOrder(200)] // after Cinemachine Brain, so the circle never lags the target
public class LockOnUI : MonoBehaviour
{
    [Header("References")]
    public MechCombat playerCombat;
    [Tooltip("Fallback only. The live target now comes from the player's lock-on (TAB), " +
             "so this does not have to be wired by hand any more.")]
    public Transform enemyTarget;
    public Image lockOnImage;

    [Header("Sprites")]
    public Sprite greenLockSprite;
    public Sprite redLockSprite;
    public Sprite yellowLockSprite;

    [Header("Settings")]
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0);
    [Tooltip("Follow whatever the player is locked on to. Untick to go back to the " +
             "fixed Inspector target (single-enemy behaviour).")]
    public bool followLockOn = true;
    [Tooltip("Punch the circle when the lock switches or crosses the red-lock boundary, " +
             "so a TAB press and an entry into homing range are both visible events.")]
    public bool snapFlash = true;
    [Tooltip("Force the Image tint to white so the sprite's own colour is what shows.")]
    public bool forceWhiteTint = true;

    private Camera cam;
    private MechShooter playerShooter;
    private TargetManager targets;
    private Transform lastTarget;
    private bool lastWasRed = true;
    private float snapAt = -99f;
    private float nextRebindAt = -99f;
    private Vector3 baseScale = Vector3.one;

    private void Start()
    {
        if (lockOnImage != null) baseScale = lockOnImage.transform.localScale;
        if (forceWhiteTint && lockOnImage != null) lockOnImage.color = Color.white;
        Rebind();
    }

    private void Rebind()
    {
        // Throttled - a scene missing one of these would otherwise sweep every frame.
        if (Time.unscaledTime < nextRebindAt) return;
        nextRebindAt = Time.unscaledTime + 1f;

        if (playerCombat == null) playerCombat = FindFirstObjectByType<MechCombat>();
        if (playerShooter == null)
        {
            playerShooter = playerCombat != null ? playerCombat.GetComponent<MechShooter>() : null;
            if (playerShooter == null) playerShooter = FindFirstObjectByType<MechShooter>();
        }
        if (targets == null)
        {
            targets = playerCombat != null ? playerCombat.GetComponent<TargetManager>() : null;
            if (targets == null) targets = FindFirstObjectByType<TargetManager>();
        }
        cam = Camera.main; // re-read: a rematch reload replaces the camera
    }

    /// <summary>Whatever the player is actually locked on to right now.</summary>
    private Transform ResolveTarget()
    {
        if (followLockOn)
        {
            if (TargetSwitcher.Instance != null && TargetSwitcher.Instance.Current != null)
                return TargetSwitcher.Instance.Current;
            if (targets != null && targets.currentTarget != null)
                return targets.currentTarget;
        }
        return enemyTarget;
    }

    /// <summary>
    /// Red lock straight from the shooter that fires the shot. This is the whole
    /// point: one definition, used by both the circle and the bullet.
    /// </summary>
    private bool IsRedLock(Transform t)
    {
        if (t == null) return false;
        if (playerShooter != null) return playerShooter.IsRedLock(t);
        if (playerCombat != null)
            return Vector3.Distance(playerCombat.transform.position, t.position) <= playerCombat.redLockRange &&
                   !playerCombat.IsTargetYellowLocked(t);
        return false;
    }

    private void LateUpdate()
    {
        if (lockOnImage == null) return; // nothing to draw on - and NO dereference

        if (playerCombat == null || playerShooter == null || targets == null || cam == null) Rebind();

        Transform target = ResolveTarget();
        if (target == null || cam == null)
        {
            if (lockOnImage.enabled) lockOnImage.enabled = false;
            lastTarget = null;
            return;
        }

        // Search thoroughly for the health script on the target, its parents or children
        MechHealth targetHealth = target.GetComponent<MechHealth>();
        if (targetHealth == null) targetHealth = target.GetComponentInParent<MechHealth>();
        if (targetHealth == null) targetHealth = target.GetComponentInChildren<MechHealth>();

        // A destroyed unit keeps its Transform - do not draw a lock on a corpse.
        if (targetHealth != null && targetHealth.currentHealth <= 0f)
        {
            if (lockOnImage.enabled) lockOnImage.enabled = false;
            lastTarget = null;
            return;
        }

        Vector3 screenPosition = cam.WorldToScreenPoint(target.position + targetOffset);
        if (screenPosition.z <= 0f)
        {
            if (lockOnImage.enabled) lockOnImage.enabled = false;
            return;
        }

        lockOnImage.enabled = true;
        lockOnImage.transform.position = new Vector3(screenPosition.x, screenPosition.y, 0f);

        // ---- the three states, in priority order ----
        bool downed = targetHealth != null && targetHealth.isYellowLocked;
        bool red = !downed && IsRedLock(target);

        Sprite want = downed ? yellowLockSprite : red ? redLockSprite : greenLockSprite;
        if (want != null && lockOnImage.sprite != want) lockOnImage.sprite = want;
        if (forceWhiteTint) lockOnImage.color = Color.white;

        // ---- make switches and range crossings readable ----
        bool changedTarget = target != lastTarget;
        bool changedRange = red != lastWasRed;
        if (changedTarget) lastTarget = target;
        if (changedRange) lastWasRed = red;
        if (snapFlash && (changedTarget || changedRange)) snapAt = Time.unscaledTime;

        float since = Time.unscaledTime - snapAt;
        lockOnImage.transform.localScale = snapFlash && since < 0.22f
            ? baseScale * Mathf.Lerp(1.9f, 1f, Mathf.Clamp01(since / 0.22f))
            : baseScale;
    }
}
