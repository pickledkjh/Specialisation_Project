using UnityEngine;

/// <summary>
/// Runtime beam-saber blade. MechCombat creates one on the right hand when a melee
/// string starts and dismisses it when the string ends - every punch swing becomes
/// a glowing saber slash with a motion trail (EXVS melee reads).
///
/// The blade aligns itself each frame along the forearm->hand direction, so it
/// works on ANY rig (Gundam or Y Bot) with no bone-axis assumptions, and the trail
/// at the tip draws the slash arcs through the air.
/// </summary>
public class SaberBlade : MonoBehaviour
{
    public float bladeLength = 1.15f;

    /// <summary>Degrees to rotate the blade OFF the forearm axis, around the arm's
    /// side axis. 0 = the blade continues straight out of the arm like a lance
    /// (wrong for a saber - it read as "pointing down and forward"). 90 = gripped
    /// in the fist and standing straight UP out of the hand, which is how every
    /// beam saber is held. Tune from MechCombat's Saber Grip fields.</summary>
    public static float GripPitchDegrees = 90f;

    /// <summary>Twist around the blade's own length. Only matters for the trail's
    /// sweep plane; leave at 0 unless the arc looks flat-on to the camera.</summary>
    public static float GripRollDegrees = 0f;

    private Transform hand;
    private Transform forearm;
    private Transform core, glow;
    private TrailRenderer trail;

    /// <summary>Mid-blade point. MechCombat parks the melee hit scan here so the
    /// saber - not the fist - is what actually connects.</summary>
    public Transform BladePoint { get { return core; } }

    public static SaberBlade Create(Transform hand, Transform forearm)
    {
        if (hand == null) return null;
        GameObject go = new GameObject("Beam Saber");
        SaberBlade blade = go.AddComponent<SaberBlade>();
        blade.hand = hand;
        blade.forearm = forearm != null ? forearm : hand.parent;

        blade.core = Cylinder(go.transform, 0.055f, new Color(1f, 0.95f, 1f, 0.95f));
        blade.glow = Cylinder(go.transform, 0.16f, new Color(1f, 0.35f, 0.85f, 0.45f));

        GameObject tip = new GameObject("Saber Trail");
        tip.transform.SetParent(go.transform, false);
        blade.trail = tip.AddComponent<TrailRenderer>();
        blade.trail.time = 0.22f;
        blade.trail.startWidth = 0.26f;
        blade.trail.endWidth = 0.02f;
        blade.trail.numCapVertices = 4;
        blade.trail.material = new Material(Shader.Find("Sprites/Default"));
        blade.trail.startColor = new Color(1f, 0.4f, 0.9f, 0.75f);
        blade.trail.endColor = new Color(1f, 0.4f, 0.9f, 0f);
        blade.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        blade.Align();
        blade.trail.Clear();
        BattleAudio.Play("saber", 0.8f);
        return blade;
    }

    private static Transform Cylinder(Transform parent, float width, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        Renderer r = go.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void LateUpdate() { Align(); }

    private void Align()
    {
        if (hand == null || forearm == null) return;
        Vector3 armDir = hand.position - forearm.position;
        if (armDir.sqrMagnitude < 1e-6f) return;
        armDir.Normalize();

        // A saber is GRIPPED, not an extension of the forearm. Rotate the blade off
        // the arm axis so it stands up out of the fist. The pivot axis is the arm's
        // "side" (perpendicular to both the arm and world up); when the arm happens
        // to point straight up that degenerates, so fall back to the hand's right.
        Vector3 side = Vector3.Cross(armDir, Vector3.up);
        if (side.sqrMagnitude < 1e-4f) side = hand.right;
        side.Normalize();

        Vector3 dir = Quaternion.AngleAxis(GripPitchDegrees, side) * armDir;
        if (Mathf.Abs(GripRollDegrees) > 0.01f)
            dir = Quaternion.AngleAxis(GripRollDegrees, armDir) * dir;
        dir.Normalize();

        // The hilt still sits in the hand - only the blade direction changed.
        Vector3 basePos = hand.position + dir * 0.06f;
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        core.position = basePos + dir * (bladeLength * 0.5f);
        core.rotation = rot;
        core.localScale = new Vector3(0.055f, bladeLength * 0.5f, 0.055f);
        glow.position = core.position;
        glow.rotation = rot;
        glow.localScale = new Vector3(0.16f, bladeLength * 0.5f, 0.16f);
        trail.transform.position = basePos + dir * bladeLength;
    }

    /// <summary>Blade off - the trail detaches and fades out on its own.</summary>
    public void Dismiss()
    {
        if (trail != null)
        {
            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Object.Destroy(trail.gameObject, 0.5f);
        }
        Object.Destroy(gameObject);
    }
}
