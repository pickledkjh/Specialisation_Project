using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// DRAFT — lives in CoworkHandoff_Output/Drafts (NOT compiled by Unity).
/// EXVS-style lock-on battle camera built on Cinemachine 3 (Unity.Cinemachine).
///
/// Concept: this script drives an ordinary CinemachineCamera by moving a "camera rig"
/// pair of transforms (follow anchor + look-at anchor) that the vcam tracks. The rig:
///   - positions itself BEHIND the player relative to the enemy, so player is in the
///     lower-middle of frame and the enemy stays framed ahead (the EXVS staple),
///   - follows smoothly with damped horizontal rotation and gentler vertical motion,
///   - pulls back slightly at long range so both mechs stay in frame,
///   - falls back to a normal third-person follow when there is no target.
///
/// See WIRING_GUIDE_CAMERA.md for scene setup and how this coexists with
/// CameraPivot.cs and the punch1/punch4 cinematic cameras.
/// </summary>
public class LockOnBattleCamera : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player mech root.")]
    public Transform player;
    [Tooltip("Optional. If set, overrides TargetManager as the enemy source.")]
    public Transform enemyOverride;
    [Tooltip("Player's TargetManager (for currentTarget). Auto-found from player if left empty.")]
    public TargetManager targetManager;
    [Tooltip("The CinemachineCamera this rig drives. Its Follow should be this transform; LookAt the child aim anchor (auto-created).")]
    public CinemachineCamera vcam;

    [Header("Framing (lock-on)")]
    [Tooltip("Base distance behind the player.")]
    public float baseDistance = 7f;
    [Tooltip("Camera height above the player's pivot.")]
    public float height = 3f;
    [Tooltip("Extra pull-back per unit of player-enemy distance beyond nearRange, so far fights keep both mechs framed.")]
    public float pullbackPerUnit = 0.06f;
    [Tooltip("No pull-back inside this player-enemy range.")]
    public float nearRange = 15f;
    [Tooltip("Cap on the extra pull-back.")]
    public float maxExtraPullback = 5f;

    [Header("Smoothing")]
    [Tooltip("How fast the rig swings around to stay behind the player (deg-ish/s feel; 4-8 = weighty mech, 12+ = twitchy).")]
    public float horizontalDamping = 6f;
    [Tooltip("Vertical follow is deliberately softer so dash hops / skims don't pump the camera.")]
    public float verticalDamping = 3f;
    [Tooltip("How fast the aim anchor tracks the midpoint / enemy.")]
    public float aimDamping = 8f;

    [Header("Aim")]
    [Tooltip("0 = look at the player, 1 = look at the enemy. ~0.35 keeps the player low in frame with the enemy ahead (EXVS framing).")]
    [Range(0f, 1f)] public float aimBias = 0.35f;
    [Tooltip("Aim height offset so mechs are framed at chest height, not feet.")]
    public float aimHeight = 1.5f;

    [Header("No-target fallback")]
    [Tooltip("With no target, the rig sits behind the player's facing at this distance.")]
    public float freeFollowDistance = 6f;
    public float freeFollowHeight = 2.5f;

    private Transform aimAnchor;   // child transform the vcam LookAt points to
    private Vector3 rigVelocity;   // smoothing state

    public Transform AimAnchor => aimAnchor;

    private void Awake()
    {
        // The vcam looks at this child; we move it every LateUpdate.
        aimAnchor = new GameObject("LockOnAimAnchor").transform;
        aimAnchor.SetParent(null); // world-space, so vcam damping stays sane

        if (targetManager == null && player != null)
            targetManager = player.GetComponent<TargetManager>();
    }

    private Transform GetEnemy()
    {
        if (enemyOverride != null) return enemyOverride;
        if (targetManager != null && targetManager.currentTarget != null)
            return targetManager.currentTarget;
        return null;
    }

    // LateUpdate AFTER gameplay movement (CameraPivot does the same); Cinemachine's
    // brain then reads these transforms in its own late update pass.
    private void LateUpdate()
    {
        if (player == null) return;

        Transform enemy = GetEnemy();
        Vector3 desiredPos;
        Vector3 desiredAim;

        if (enemy != null)
        {
            // ---- lock-on framing: behind the player, opposite the enemy ----
            Vector3 toEnemy = enemy.position - player.position;
            float enemyDist = toEnemy.magnitude;
            Vector3 flat = toEnemy; flat.y = 0f;

            // Degenerate case (enemy directly above/at player): keep last heading
            Vector3 back = flat.sqrMagnitude > 0.01f ? -flat.normalized
                                                     : -player.forward;

            // Long-range pull-back so both mechs stay in frame
            float extra = Mathf.Min(maxExtraPullback,
                                    Mathf.Max(0f, enemyDist - nearRange) * pullbackPerUnit);

            desiredPos = player.position + back * (baseDistance + extra) + Vector3.up * height;

            // Aim between the two mechs, biased toward the enemy, at chest height.
            // Vertical: follows the enemy's height partially so launched/flying mechs
            // tilt the camera up instead of leaving frame.
            desiredAim = Vector3.Lerp(player.position, enemy.position, aimBias) + Vector3.up * aimHeight;
        }
        else
        {
            // ---- fallback: plain third-person follow behind the player's facing ----
            desiredPos = player.position - player.forward * freeFollowDistance + Vector3.up * freeFollowHeight;
            desiredAim = player.position + player.forward * 10f + Vector3.up * aimHeight;
        }

        // Split damping: horizontal follows briskly, vertical is softer so ground-skim
        // dashes and the little dash hop don't bounce the frame.
        Vector3 current = transform.position;
        float hLerp = 1f - Mathf.Exp(-horizontalDamping * Time.deltaTime);
        float vLerp = 1f - Mathf.Exp(-verticalDamping * Time.deltaTime);
        Vector3 next;
        next.x = Mathf.Lerp(current.x, desiredPos.x, hLerp);
        next.z = Mathf.Lerp(current.z, desiredPos.z, hLerp);
        next.y = Mathf.Lerp(current.y, desiredPos.y, vLerp);
        transform.position = next;

        float aLerp = 1f - Mathf.Exp(-aimDamping * Time.deltaTime);
        aimAnchor.position = Vector3.Lerp(aimAnchor.position, desiredAim, aLerp);

        // If the vcam tracks this transform directly (Follow = this, LookAt = aimAnchor)
        // it inherits all of this smoothing; keep the vcam's own damping near zero to
        // avoid double-lag. Facing is provided for setups that use rotation directly:
        Vector3 lookDir = aimAnchor.position - transform.position;
        if (lookDir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(lookDir.normalized);
    }

    private void OnDestroy()
    {
        if (aimAnchor != null) Destroy(aimAnchor.gameObject);
    }
}
