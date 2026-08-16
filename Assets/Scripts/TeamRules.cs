using UnityEngine;

/// <summary>
/// The single place that decides what a hit between two mechs does.
///
/// In 1v1 this is deliberately INERT: TeamModeActive is false, and ResolveHit
/// passes every hit through completely untouched. Nothing about the existing
/// mission mode changes because this file exists.
///
/// In 2v2 it is what makes the mode a team mode. Friendly fire is ON - two
/// units firing straight through each other is not a team fight, it is two
/// separate duels sharing an arena - but a hit on your own side is heavily
/// softened: a stray beam is a warning, not a way to lose the match for your
/// partner. Damage, down-bar and stagger are all scaled separately, because the
/// really unfair part of friendly fire is not the chip damage, it is knocking
/// your own teammate over in front of an enemy.
/// </summary>
public static class TeamRules
{
    /// <summary>True only while a 2v2 battle is running.</summary>
    public static bool TeamModeActive = false;

    /// <summary>Off = allied hits are ignored entirely instead of softened.</summary>
    public static bool FriendlyFire = true;

    [Tooltip("Fraction of normal damage a teammate takes.")]
    public static float FriendlyDamageScale = 0.30f;

    [Tooltip("Fraction of normal down-bar a teammate takes. Very low on purpose - " +
             "knocking your own partner down is the worst thing friendly fire can do.")]
    public static float FriendlyBarScale = 0.15f;

    [Tooltip("Fraction of normal stagger a teammate takes.")]
    public static float FriendlyStunScale = 0.35f;

    public static void Reset()
    {
        TeamModeActive = false;
        FriendlyFire = true;
    }

    public static MechHealth FindHealth(Transform t)
    {
        if (t == null) return null;
        MechHealth h = t.GetComponentInParent<MechHealth>();
        if (h == null) h = t.GetComponentInChildren<MechHealth>();
        return h;
    }

    public static bool AreAllies(MechHealth a, MechHealth b)
    {
        return a != null && b != null && a != b && a.team == b.team;
    }

    /// <summary>
    /// Decides whether a hit lands at all, and softens it when it is friendly.
    /// Returns FALSE when the damage source should drop the hit completely.
    /// damage / bar / stun are modified in place.
    /// </summary>
    public static bool ResolveHit(MechHealth attacker, MechHealth victim,
                                  ref float damage, ref float bar, ref float stun)
    {
        if (victim == null) return false;
        if (attacker == null) return true;    // world hazards (buildings, hazards) hit everyone at full
        if (attacker == victim) return false; // never your own blast, your own fist, your own beam
        if (!TeamModeActive) return true;     // 1v1: byte-for-byte the old behaviour
        if (attacker.team != victim.team) return true;

        if (!FriendlyFire) return false;
        damage *= FriendlyDamageScale;
        bar *= FriendlyBarScale;
        stun *= FriendlyStunScale;
        return true;
    }

    /// <summary>Overload for damage sources that only know the attacker's transform.</summary>
    public static bool ResolveHit(Transform attackerRoot, MechHealth victim,
                                  ref float damage, ref float bar, ref float stun)
    {
        return ResolveHit(FindHealth(attackerRoot), victim, ref damage, ref bar, ref stun);
    }

    /// <summary>Cheap yes/no for sources that do not scale, only skip.</summary>
    public static bool WouldBeFriendly(Transform attackerRoot, MechHealth victim)
    {
        if (!TeamModeActive) return false;
        return AreAllies(FindHealth(attackerRoot), victim);
    }
}
