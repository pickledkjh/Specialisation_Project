using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Team-aware list of everything alive in the arena.
///
/// Every targeting decision in the game used to be "find the one SimpleMechAI in
/// the scene" or "the Transform someone dragged into the Inspector". That is fine
/// with exactly two mechs and wrong with four. This is the shared answer to
/// "who can I shoot?" for the player's lock-on, the AI's target picking and the
/// HUD alike.
///
/// The list is rebuilt by scanning rather than by registration hooks: MechHealth
/// is the most bug-prone file in the project and does not need two more lifecycle
/// callbacks in it. A scan costs nothing at this scale and the result is cached
/// for a fraction of a second.
/// </summary>
public static class BattleRoster
{
    private static readonly List<MechHealth> all = new List<MechHealth>();
    private static readonly List<MechHealth> scratch = new List<MechHealth>();
    private static float lastScanAt = -99f;
    private const float RescanInterval = 0.5f;

    /// <summary>Force a rescan on the next query - call after spawning or destroying units.</summary>
    public static void Invalidate() { lastScanAt = -99f; }

    private static void EnsureFresh()
    {
        bool stale = Time.unscaledTime - lastScanAt > RescanInterval;
        if (!stale)
        {
            // a destroyed unit shows up as a fake-null entry - that always forces a rescan
            for (int i = 0; i < all.Count; i++)
                if (all[i] == null) { stale = true; break; }
        }
        if (!stale) return;

        lastScanAt = Time.unscaledTime;
        all.Clear();
        MechHealth[] found = Object.FindObjectsByType<MechHealth>(FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
            if (found[i] != null && found[i].isActiveAndEnabled) all.Add(found[i]);
    }

    public static List<MechHealth> All
    {
        get { EnsureFresh(); return all; }
    }

    public static bool IsAlive(MechHealth m)
    {
        return m != null && m.currentHealth > 0f && m.gameObject.activeInHierarchy;
    }

    /// <summary>Living mechs NOT on the given team, nearest first. Reuses one buffer -
    /// copy it if you need to hold on to the result.</summary>
    public static List<MechHealth> Opponents(Team myTeam, Vector3 sortFrom)
    {
        EnsureFresh();
        scratch.Clear();
        for (int i = 0; i < all.Count; i++)
        {
            MechHealth m = all[i];
            if (!IsAlive(m) || m.team == myTeam) continue;
            scratch.Add(m);
        }
        scratch.Sort((a, b) =>
            (a.transform.position - sortFrom).sqrMagnitude
            .CompareTo((b.transform.position - sortFrom).sqrMagnitude));
        return scratch;
    }

    public static List<MechHealth> Allies(Team myTeam, MechHealth except)
    {
        EnsureFresh();
        scratch.Clear();
        for (int i = 0; i < all.Count; i++)
        {
            MechHealth m = all[i];
            if (!IsAlive(m) || m.team != myTeam || m == except) continue;
            scratch.Add(m);
        }
        return scratch;
    }

    public static MechHealth NearestOpponent(Vector3 from, Team myTeam)
    {
        List<MechHealth> list = Opponents(myTeam, from);
        return list.Count > 0 ? list[0] : null;
    }

    public static int LivingCount(Team team)
    {
        EnsureFresh();
        int n = 0;
        for (int i = 0; i < all.Count; i++)
            if (IsAlive(all[i]) && all[i].team == team) n++;
        return n;
    }

    /// <summary>Combined armour fraction of a whole team - the timeout tiebreaker.</summary>
    public static float TeamHealthFraction(Team team)
    {
        EnsureFresh();
        float cur = 0f, max = 0f;
        for (int i = 0; i < all.Count; i++)
        {
            MechHealth m = all[i];
            if (m == null || m.team != team) continue;
            cur += Mathf.Max(0f, m.currentHealth);
            max += Mathf.Max(0.0001f, m.maxHealth);
        }
        return max > 0f ? cur / max : 0f;
    }
}
