using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

/// <summary>
/// TAB switches which enemy you are locked on to.
///
/// Everything downstream - the battle camera, melee homing, the rifle's tracking,
/// the specials, the thrown interactables and the floating enemy plate - already
/// reads TargetManager.currentTarget. Nothing ever WROTE it: it was an Inspector
/// field pointing at the one enemy in the scene. This is the thing that writes it.
///
/// It runs in 1v1 too, where it just quietly locks the single opponent (and now
/// re-locks correctly if that opponent is ever replaced). With four mechs on the
/// field it becomes the mode's core control.
///
/// Cycle order is by screen angle from the camera's forward, not by distance, so
/// TAB moves the lock to "the other one over there" the way you expect rather than
/// flip-flopping every time the two enemies swap which is closer.
/// </summary>
[DefaultExecutionOrder(-40)]
public class TargetSwitcher : MonoBehaviour
{
    public static TargetSwitcher Instance { get; private set; }

    [Tooltip("How long after a manual TAB before auto-acquire is allowed to move the lock again.")]
    public float manualHoldSeconds = 6f;

    private TargetManager targets;              // the one whose reticle we care about
    private TargetManager[] allManagers;        // ...but EVERY one gets written to
    private float nextManagerScanAt = -99f;
    private MechController controller;
    private MechHealth myHealth;
    private InputAction switchAction;
    private float manualUntil = -99f;

    private readonly List<MechHealth> ordered = new List<MechHealth>();

    /// <summary>Set when TAB moves the lock - the HUD flashes on this.</summary>
    public float LastSwitchAt { get; private set; } = -99f;
    public Transform Current { get { return targets != null ? targets.currentTarget : null; } }

    private void Awake()
    {
        Instance = this;
        controller = GetComponent<MechController>();
        myHealth = GetComponent<MechHealth>();

        RefreshManagers();

        switchAction = new InputAction("SwitchTarget", InputActionType.Button);
        switchAction.AddBinding("<Keyboard>/tab");
        switchAction.AddBinding("<Gamepad>/rightShoulder");
        switchAction.Enable();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (switchAction != null) switchAction.Disable();
    }

    private Team MyTeam { get { return myHealth != null ? myHealth.team : Team.Team1; } }

    /// <summary>
    /// Find every TargetManager in the scene, INCLUDING ones on inactive objects.
    ///
    /// This is what left the lock circle behind on the old enemy: the reticle lives
    /// on whichever TargetManager the scene set up, and if this script wrote its
    /// target to a different instance - or created its own because the scene's was
    /// on an inactive object - the circle was reading a currentTarget nobody ever
    /// changed. Writing to all of them removes the entire class of problem: there
    /// is no longer a "wrong one" to pick.
    /// </summary>
    private void RefreshManagers()
    {
        nextManagerScanAt = Time.unscaledTime + 2f;
        allManagers = FindObjectsByType<TargetManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // Prefer whichever one actually owns a reticle, then the player's own.
        TargetManager preferred = null;
        for (int i = 0; i < allManagers.Length; i++)
            if (allManagers[i] != null && allManagers[i].lockOnReticle != null) { preferred = allManagers[i]; break; }
        if (preferred == null) preferred = GetComponent<TargetManager>();
        if (preferred == null && allManagers.Length > 0) preferred = allManagers[0];
        if (preferred == null) preferred = gameObject.AddComponent<TargetManager>();

        targets = preferred;
        if (allManagers == null || allManagers.Length == 0)
            allManagers = new TargetManager[] { preferred };
    }

    /// <summary>The single place that writes the lock, so no manager falls out of step.</summary>
    private void ApplyTarget(Transform t)
    {
        if (targets != null) targets.currentTarget = t;
        if (allManagers == null) return;
        for (int i = 0; i < allManagers.Length; i++)
            if (allManagers[i] != null) allManagers[i].currentTarget = t;
    }

    private void Update()
    {
        if (targets == null || Time.unscaledTime > nextManagerScanAt) RefreshManagers();
        if (targets == null) return;

        // Drop a dead or destroyed lock immediately - shooting a corpse is the
        // single most common way a lock-on system feels broken.
        MechHealth cur = TeamRules.FindHealth(targets.currentTarget);
        if (!BattleRoster.IsAlive(cur) || (cur != null && cur.team == MyTeam))
        {
            ApplyTarget(null);
            manualUntil = -99f;
        }

        if (switchAction != null && switchAction.WasPressedThisFrame())
        {
            Cycle(+1);
        }
        else if (targets.currentTarget == null || Time.time > manualUntil)
        {
            AutoAcquire();
        }

        // Keep the old direct reference in step so anything still reading it agrees.
        // Only ever WRITE a real target: blanking it would knock the camera into
        // free-follow whenever the roster has not been team-assigned yet (the
        // tutorial runs before the fight sets teams up).
        if (controller != null && targets.currentTarget != null)
            controller.enemyTarget = targets.currentTarget;
    }

    private void BuildOrder()
    {
        ordered.Clear();
        List<MechHealth> live = BattleRoster.Opponents(MyTeam, transform.position);
        for (int i = 0; i < live.Count; i++) ordered.Add(live[i]);

        Camera cam = Camera.main;
        if (cam == null || ordered.Count < 2) return;

        // Left-to-right across the screen: a stable order that does not reshuffle
        // just because the two enemies traded places in distance.
        Vector3 camPos = cam.transform.position;
        Vector3 fwd = cam.transform.forward;
        Vector3 right = cam.transform.right;
        ordered.Sort((a, b) =>
        {
            Vector3 da = a.transform.position - camPos;
            Vector3 db = b.transform.position - camPos;
            float ka = Mathf.Atan2(Vector3.Dot(da, right), Mathf.Max(0.01f, Vector3.Dot(da, fwd)));
            float kb = Mathf.Atan2(Vector3.Dot(db, right), Mathf.Max(0.01f, Vector3.Dot(db, fwd)));
            return ka.CompareTo(kb);
        });
    }

    public void Cycle(int step)
    {
        BuildOrder();
        if (ordered.Count == 0) { ApplyTarget(null); return; }

        int index = -1;
        MechHealth cur = TeamRules.FindHealth(targets.currentTarget);
        for (int i = 0; i < ordered.Count; i++)
            if (ordered[i] == cur) { index = i; break; }

        int next = index < 0 ? 0 : ((index + step) % ordered.Count + ordered.Count) % ordered.Count;
        Transform picked = ordered[next].transform;

        if (picked != targets.currentTarget)
        {
            LastSwitchAt = Time.time;
            BattleAudio.Play("alert", 0.35f, 1.7f);
        }
        ApplyTarget(picked);
        manualUntil = Time.time + manualHoldSeconds;
    }

    private void AutoAcquire()
    {
        MechHealth near = BattleRoster.NearestOpponent(transform.position, MyTeam);
        if (near == null) { ApplyTarget(null); return; }
        if (targets.currentTarget == near.transform) return;

        // With a live lock, only steal it for something meaningfully closer -
        // otherwise the lock jitters between two enemies at similar range.
        MechHealth cur = TeamRules.FindHealth(targets.currentTarget);
        if (BattleRoster.IsAlive(cur))
        {
            float dCur = Vector3.Distance(transform.position, cur.transform.position);
            float dNew = Vector3.Distance(transform.position, near.transform.position);
            if (dNew > dCur - 6f) return;
        }
        ApplyTarget(near.transform);
    }

    /// <summary>Bootstrap: the player mech gets one automatically, no scene setup needed.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        MechController pc = Object.FindFirstObjectByType<MechController>();
        if (pc == null) return;
        if (pc.GetComponent<TargetSwitcher>() == null) pc.gameObject.AddComponent<TargetSwitcher>();
    }
}
