using UnityEngine;
using UnityEditor;

/// <summary>
/// One-click answer to "why does the knockdown work on the player but not the
/// enemy?". Prints, for every MechHealth in the scene, the exact things the
/// knockdown flight depends on - so a difference between the two mechs is visible
/// instead of guessed at. Everything the flight can trip over is on this list:
/// a missing CharacterController, a live Rigidbody, a NavMeshAgent, a stale
/// serialized flight value, or a control script the down state cannot disable.
/// </summary>
public static class KnockdownReport
{
    [MenuItem("Tools/Gundam/Debug - Knockdown Parity Report")]
    public static void Report()
    {
        MechHealth[] all = Object.FindObjectsByType<MechHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (all.Length == 0) { Debug.LogWarning("[Parity] No MechHealth in the scene."); return; }

        var sb = new System.Text.StringBuilder("[Parity] KNOCKDOWN SETUP - " + all.Length + " mech(s)\n");
        foreach (MechHealth h in all)
        {
            GameObject go = h.gameObject;
            CharacterController cc = go.GetComponent<CharacterController>();
            Rigidbody rb = go.GetComponent<Rigidbody>();
            UnityEngine.AI.NavMeshAgent nav = go.GetComponent<UnityEngine.AI.NavMeshAgent>();
            MechController pc = go.GetComponent<MechController>();
            SimpleMechAI ai = go.GetComponent<SimpleMechAI>();
            MechCombat mc = go.GetComponent<MechCombat>();

            sb.Append("\n=== '").Append(go.name).Append("'  active=").Append(go.activeInHierarchy)
              .Append("  pos=").Append(go.transform.position.ToString("F1")).Append('\n');

            sb.Append("   CharacterController : ").Append(cc == null ? "MISSING <-- the flight used to be skipped entirely without one"
                                                                     : "yes, enabled=" + cc.enabled).Append('\n');
            sb.Append("   Rigidbody           : ").Append(rb == null ? "none (good)"
                                                                    : "PRESENT kinematic=" + rb.isKinematic + " <-- a non-kinematic body fights transform writes").Append('\n');
            sb.Append("   NavMeshAgent        : ").Append(nav == null ? "none (good)"
                                                                     : "PRESENT enabled=" + nav.enabled + " <-- warps the transform back every frame").Append('\n');
            sb.Append("   Control scripts     : MechController=").Append(pc == null ? "no" : "yes/enabled=" + pc.enabled)
              .Append("  SimpleMechAI=").Append(ai == null ? "no" : "yes/enabled=" + ai.enabled)
              .Append("  MechCombat=").Append(mc == null ? "no" : "yes/enabled=" + mc.enabled).Append('\n');
            sb.Append("   Flight values       : gravity=").Append(h.flightGravity)
              .Append("  drag=").Append(h.flightDrag)
              .Append("  maxSeconds=").Append(h.flightMaxSeconds).Append('\n');
            sb.Append("   Knockdown bar       : max=").Append(h.maxKnockdownValue)
              .Append("  current=").Append(h.currentKnockdownValue)
              .Append("  yellowLocked=").Append(h.isYellowLocked).Append('\n');
            sb.Append("   Health              : ").Append(h.currentHealth).Append(" / ").Append(h.maxHealth)
              .Append("  team=").Append(h.team).Append('\n');
        }

        sb.Append("\nWhat to compare: the two mechs should differ ONLY in health/team. Any difference in\n")
          .Append("CharacterController / Rigidbody / NavMeshAgent / flight values is the reason one of\n")
          .Append("them flies and the other does not.");
        Debug.Log(sb.ToString());
    }
}
