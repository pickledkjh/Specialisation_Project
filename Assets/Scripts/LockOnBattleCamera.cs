using UnityEngine;

/// <summary>
/// Lock-on battle camera. Sits behind the player on the player-enemy axis and keeps
/// the locked enemy centered on screen, with soft vertical follow and a slight
/// pull-back at long range. Falls back to a normal follow cam with no target.
/// Drives a CinemachineCamera on the same GameObject (priority 15, under the punch
/// cinematics at 20).
/// </summary>
public class LockOnBattleCamera : MonoBehaviour
{
    [Header("References (auto-found if empty)")]
    [Tooltip("The player mech root. Auto-found via MechController if left empty.")]
    public Transform player;
    [Tooltip("Optional. If set, overrides TargetManager as the enemy source.")]
    public Transform enemyOverride;
    [Tooltip("Player's TargetManager (for currentTarget). Auto-found from the player.")]
    public TargetManager targetManager;

    [Header("EXVS Framing")]
    [Tooltip("Distance behind the player. EXVS is close — your mech should fill the lower-center of the frame.")]
    public float baseDistance = 5.5f;
    [Tooltip("Camera height above the player's pivot. Modest — EXVS looks mostly level at the enemy, not down at the arena.")]
    public float height = 2.3f;
    [Tooltip("Chest-height aim on the enemy, so they're centered on their body, not their feet.")]
    public float aimHeight = 1.3f;
    [Tooltip("1 = look straight AT the enemy (EXVS: enemy locked to screen center). Lower values slide the aim back toward the midpoint — NOT how EXVS frames, kept only as a tuning escape hatch.")]
    [Range(0f, 1f)] public float enemyCenterBias = 1f;

    [Header("Long-range pull-back")]
    [Tooltip("Extra pull-back per unit of player-enemy distance beyond nearRange.")]
    public float pullbackPerUnit = 0.05f;
    [Tooltip("No pull-back inside this range.")]
    public float nearRange = 20f;
    [Tooltip("Cap on the extra pull-back.")]
    public float maxExtraPullback = 4f;

    [Header("Vertical framing")]
    [Tooltip("0 = aim at the enemy's height (a rising player can leave the frame), 1 = the player's height. 0.5 keeps BOTH mechs framed when one flies high above the other.")]
    [Range(0f, 1f)] public float verticalAimBalance = 0.5f;
    [Tooltip("Extra camera pull-back per unit of HEIGHT difference between the mechs, so vertical fights stay readable.")]
    public float verticalPullback = 0.35f;

    [Header("Snappiness (EXVS is stiff, not floaty)")]
    [Tooltip("How fast the camera swings around behind the player. EXVS-tight at 10-14; below ~6 it turns floaty and stops feeling like the reference.")]
    public float positionDamping = 12f;
    [Tooltip("Vertical position follow, slightly softer so the dash ground-skim doesn't pump the frame.")]
    public float verticalDamping = 6f;
    [Tooltip("How hard the look-at sticks to the enemy. High = enemy glued to screen center like EXVS.")]
    public float aimDamping = 18f;
    [Tooltip("Hard cap on how fast the camera may swing AROUND the player, in degrees per second. During close-range crossovers or when the enemy passes overhead the player-enemy axis can flip almost instantly - uncapped, that was the 'camera goes crazy' whip from the playtest.")]
    public float maxOrbitDegPerSec = 300f;

    [Header("No-target fallback")]
    public float freeFollowDistance = 6f;
    public float freeFollowHeight = 2.5f;

    private Vector3 currentAim;
    private bool initialized;
    private MechController playerController;

    // Rate-limited orbit direction (the smoothed "behind the player" vector)
    private Vector3 smoothedBack;
    private bool backInitialized;

    private void Start()
    {
        if (player == null)
        {
            MechController pc = FindFirstObjectByType<MechController>();
            if (pc != null) player = pc.transform;
        }
        if (player != null)
            playerController = player.GetComponent<MechController>();

        // TargetManager may live on the player or elsewhere — look everywhere before
        // giving up, otherwise we'd silently drop into the no-target fallback.
        if (targetManager == null && player != null)
            targetManager = player.GetComponent<TargetManager>();
        if (targetManager == null)
            targetManager = FindFirstObjectByType<TargetManager>();
    }

    // Mirrors MechCombat.GetTarget: TargetManager first, then MechController.enemyTarget.
    // The enemyTarget fallback matters: without it, a missing/unassigned TargetManager
    // dropped the camera into free-follow, which tracks your FACING — so walking
    // backwards turned the camera away and the enemy vanished off screen.
    private Transform GetEnemy()
    {
        if (enemyOverride != null) return enemyOverride;
        if (targetManager != null && targetManager.currentTarget != null)
            return targetManager.currentTarget;
        if (playerController != null && playerController.enemyTarget != null)
            return playerController.enemyTarget;
        return null;
    }

    // LateUpdate AFTER gameplay movement; the CinemachineBrain reads this transform
    // in its own later pass.
    private void LateUpdate()
    {
        if (player == null) return;

        Transform enemy = GetEnemy();
        Vector3 desiredPos;
        Vector3 desiredAim;

        if (enemy != null)
        {
            // ---- EXVS framing: camera on the you→enemy axis, behind you ----
            Vector3 toEnemy = enemy.position - player.position;
            float enemyDist = toEnemy.magnitude;
            Vector3 flat = toEnemy; flat.y = 0f;

            // Which way is "behind the player"? Three rules fix the playtest
            // "camera goes crazy" whip:
            //  1. Enemy (nearly) overhead: HOLD the current direction instead of
            //     guessing from facing - the old fallback caused instant 180 flips.
            //  2. The orbit direction may never swing faster than maxOrbitDegPerSec,
            //     so close-range crossovers pan quickly but never teleport-whip.
            Vector3 desiredBack;
            if (flat.sqrMagnitude > 1f) desiredBack = -flat.normalized;
            else if (backInitialized) desiredBack = smoothedBack; // overhead: hold
            else desiredBack = -player.forward;

            if (!backInitialized)
            {
                smoothedBack = desiredBack;
                backInitialized = true;
            }
            smoothedBack = Vector3.RotateTowards(
                smoothedBack, desiredBack,
                maxOrbitDegPerSec * Mathf.Deg2Rad * Time.deltaTime, 0f);
            smoothedBack.y = 0f;
            if (smoothedBack.sqrMagnitude > 0.0001f) smoothedBack = smoothedBack.normalized;
            else smoothedBack = -player.forward;

            Vector3 back = smoothedBack;

            float vSep = Mathf.Abs(toEnemy.y);
            float extra = Mathf.Min(maxExtraPullback,
                                    Mathf.Max(0f, enemyDist - nearRange) * pullbackPerUnit)
                        + Mathf.Min(5f, vSep * verticalPullback);

            desiredPos = player.position + back * (baseDistance + extra) + Vector3.up * height;

            // Horizontally the aim stays glued to the enemy (the EXVS trait), but
            // the VERTICAL aim splits the difference between the two mechs - so
            // rising straight up with SPACE never pushes your own mech out of frame.
            Vector3 playerChest = player.position + Vector3.up * aimHeight;
            Vector3 enemyChest = enemy.position + Vector3.up * aimHeight;
            desiredAim = Vector3.Lerp(playerChest, enemyChest, enemyCenterBias);
            desiredAim.y = Mathf.Lerp(enemyChest.y, playerChest.y, verticalAimBalance);
        }
        else
        {
            // ---- fallback: plain third-person follow behind the player's facing ----
            desiredPos = player.position - player.forward * freeFollowDistance + Vector3.up * freeFollowHeight;
            desiredAim = player.position + player.forward * 10f + Vector3.up * aimHeight;
        }

        if (!initialized)
        {
            // First frame: snap, so we don't lerp in from wherever we spawned
            transform.position = desiredPos;
            currentAim = desiredAim;
            initialized = true;
        }
        else
        {
            // Tight horizontal swing, slightly softer vertical
            float hLerp = 1f - Mathf.Exp(-positionDamping * Time.deltaTime);
            float vLerp = 1f - Mathf.Exp(-verticalDamping * Time.deltaTime);
            Vector3 cur = transform.position;
            transform.position = new Vector3(
                Mathf.Lerp(cur.x, desiredPos.x, hLerp),
                Mathf.Lerp(cur.y, desiredPos.y, vLerp),
                Mathf.Lerp(cur.z, desiredPos.z, hLerp));

            float aLerp = 1f - Mathf.Exp(-aimDamping * Time.deltaTime);
            currentAim = Vector3.Lerp(currentAim, desiredAim, aLerp);
        }

        Vector3 lookDir = currentAim - transform.position;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized);
    }
}
