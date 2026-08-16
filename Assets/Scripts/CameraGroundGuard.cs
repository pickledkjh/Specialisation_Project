using UnityEngine;

/// <summary>
/// Last line of defence against the camera clipping under the arena floor.
///
/// The real cause of "the camera breaks through the ground after I finish the
/// enemy" was a punch cinematic camera whose priority never got restored (fixed
/// in MechCombat), but ANY camera blend can dip below the floor for a few frames:
/// a scene cinematic camera parked low, a deoccluder shove, a hard blend during a
/// knockdown. This clamps the live camera above the floor no matter who moved it.
///
/// Runs at execution order 10000 so its LateUpdate lands AFTER CinemachineBrain
/// has written the main camera transform for the frame - clamping any earlier
/// would just get overwritten.
/// </summary>
[DefaultExecutionOrder(10000)]
public class CameraGroundGuard : MonoBehaviour
{
    [Tooltip("The camera may never sit below this world height. The arena floor is at y=0, so a small positive value keeps the lens out of the concrete.")]
    public float minWorldY = 0.9f;

    [Tooltip("Extra clearance kept above whatever solid surface is directly under the camera (rooftops, cars, debris). 0 disables the surface probe and only the flat minimum applies.")]
    public float surfaceClearance = 0.45f;

    [Tooltip("How far down to probe for a surface. Keep short: probing far would lift the camera over street-level geometry it is legitimately flying past.")]
    public float probeDistance = 2.5f;

    private void LateUpdate()
    {
        Vector3 p = transform.position;
        float floor = minWorldY;

        // Short downward probe: standing on a rooftop, the "floor" is the roof, not y=0.
        if (surfaceClearance > 0f)
        {
            RaycastHit hit;
            if (Physics.Raycast(p + Vector3.up * 0.05f, Vector3.down, out hit, probeDistance, ~0, QueryTriggerInteraction.Ignore))
                floor = Mathf.Max(floor, hit.point.y + surfaceClearance);
        }

        if (p.y < floor)
        {
            p.y = floor;
            transform.position = p;
        }
    }
}

/// <summary>Puts the guard on the main camera at play start - no scene setup.</summary>
public static class CameraGroundGuardBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<CameraGroundGuard>() == null)
            cam.gameObject.AddComponent<CameraGroundGuard>();
    }
}
