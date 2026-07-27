using UnityEngine;

/// <summary>
/// Hard arena limits enforced in code every frame, so nobody can leave the map even
/// if the scene walls are missing or outdated. Keep Radius/Ceiling in sync with
/// BattlefieldBuilder (WallRadius / CeilingHeight).
/// </summary>
public static class ArenaLimits
{
    public const float Radius = 70f;
    public const float Ceiling = 34f;

    /// <summary>Corrective offset that pushes a position back inside the arena (zero when inside).</summary>
    public static Vector3 Correction(Vector3 position)
    {
        Vector3 fix = Vector3.zero;
        Vector3 flat = new Vector3(position.x, 0f, position.z);
        float dist = flat.magnitude;
        if (dist > Radius) fix -= flat.normalized * (dist - Radius);
        if (position.y > Ceiling) fix += Vector3.down * (position.y - Ceiling);
        return fix;
    }
}
