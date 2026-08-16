using UnityEngine;

/// <summary>
/// Sits on the SAME GameObject as the Animator (the model child) and forwards
/// animation-clip events up to the gameplay scripts on the mech root.
/// Needed because the Gundam model lives one level below the root: Unity only
/// delivers animation events to components on the Animator's own GameObject.
/// Every event name used by the punch/gethit clips is covered - a missing one
/// would spam "has no receiver" errors every swing.
/// </summary>
public class AnimationEventRelay : MonoBehaviour
{
    private MechCombat combat;
    private SimpleMechAI ai;

    private void Awake()
    {
        combat = GetComponentInParent<MechCombat>();
        ai = GetComponentInParent<SimpleMechAI>();
    }

    public void EnableRightFist()  { if (combat != null) combat.EnableRightFist();  else if (ai != null) ai.EnableRightFist(); }
    public void DisableRightFist() { if (combat != null) combat.DisableRightFist(); else if (ai != null) ai.DisableRightFist(); }
    public void EnableLeftFist()   { if (combat != null) combat.EnableLeftFist();   else if (ai != null) ai.EnableLeftFist(); }
    public void DisableLeftFist()  { if (combat != null) combat.DisableLeftFist();  else if (ai != null) ai.DisableLeftFist(); }
    public void EnableLeftFoot()   { if (combat != null) combat.EnableLeftFoot();   else if (ai != null) ai.EnableLeftFoot(); }
    public void DisableLeftFoot()  { if (combat != null) combat.DisableLeftFoot();  else if (ai != null) ai.DisableLeftFoot(); }
    public void EndAttack()        { if (combat != null) combat.EndAttack();        else if (ai != null) ai.EndAttack(); }
    public void SwitchToPunch1Camera() { if (combat != null) combat.SwitchToPunch1Camera(); else if (ai != null) ai.SwitchToPunch1Camera(); }
    public void SwitchToPunch4Camera() { if (combat != null) combat.SwitchToPunch4Camera(); else if (ai != null) ai.SwitchToPunch4Camera(); }
    public void SwitchToNormalCamera() { if (combat != null) combat.SwitchToNormalCamera(); else if (ai != null) ai.SwitchToNormalCamera(); }
}
