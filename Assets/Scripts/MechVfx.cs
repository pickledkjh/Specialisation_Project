using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Per-mech visual juice, the EXVS look: boost thruster flames that light up when
/// you dash/rise, cyan motion-streak trails during boost dashes, dust when you
/// land, and a shockwave ring + dust + camera shake when anyone gets floored.
/// Self-installing, survives rematch reloads, zero scene setup. Works on both the
/// player (state-driven) and the AI enemy (speed-driven - its state is private).
/// </summary>
public class MechVfx : MonoBehaviour
{
    private class MechRig
    {
        public Transform root;
        public MechHealth health;
        public MechController controller;   // player only
        public ParticleSystem jetL, jetR;
        public TrailRenderer trail;
        public bool prevDown;
        public bool prevDead;
        public bool prevThrust;
        public MechState prevState = MechState.Grounded;
        public Vector3 lastPos;
    }

    private readonly List<MechRig> rigs = new List<MechRig>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<MechVfx>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Mech VFX").AddComponent<MechVfx>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        Rehook();
    }

    private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; }
    private void OnSceneLoaded(Scene s, LoadSceneMode m) { Rehook(); }

    private void Rehook()
    {
        rigs.Clear();

        MechController player = Object.FindFirstObjectByType<MechController>();
        if (player != null) rigs.Add(BuildRig(player.transform, player));

        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        if (enemy != null) rigs.Add(BuildRig(enemy.transform, null));

        // The enemy Gundam needs its procedural arm pose (see GundamArmPose docs)
        GundamArmPose.TryInstall();
    }

    private MechRig BuildRig(Transform root, MechController controller)
    {
        MechRig rig = new MechRig();
        rig.root = root;
        rig.controller = controller;
        rig.health = root.GetComponent<MechHealth>();
        rig.lastPos = root.position;

        // Back thrusters: two blue-white jets aimed backward with a slight down tilt
        Quaternion jetAim = Quaternion.LookRotation(Vector3.back) * Quaternion.Euler(-12f, 0f, 0f);
        Color jetColor = new Color(0.45f, 0.8f, 1f);
        rig.jetL = ProceduralVfx.MakeJet(root, new Vector3(-0.22f, 1.35f, -0.3f), jetAim, jetColor, 0.22f);
        rig.jetR = ProceduralVfx.MakeJet(root, new Vector3(0.22f, 1.35f, -0.3f), jetAim, jetColor, 0.22f);

        // Boost-dash motion streak
        GameObject trailGo = new GameObject("FX Dash Trail");
        trailGo.transform.SetParent(root, false);
        trailGo.transform.localPosition = new Vector3(0f, 1.1f, -0.2f);
        rig.trail = trailGo.AddComponent<TrailRenderer>();
        rig.trail.time = 0.28f;
        rig.trail.startWidth = 0.85f;
        rig.trail.endWidth = 0.05f;
        rig.trail.numCapVertices = 3;
        rig.trail.material = new Material(Shader.Find("Sprites/Default"));
        rig.trail.startColor = new Color(0.45f, 0.85f, 1f, 0.4f);
        rig.trail.endColor = new Color(0.45f, 0.85f, 1f, 0f);
        rig.trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rig.trail.emitting = false;

        return rig;
    }

    private void Update()
    {
        bool anyAlive = false;
        for (int i = 0; i < rigs.Count; i++)
        {
            MechRig rig = rigs[i];
            if (rig.root == null) continue;
            anyAlive = true;

            // ---- thrust detection ----
            bool thrusting;
            bool dashing;
            if (rig.controller != null)
            {
                MechState st = rig.controller.currentState;
                dashing = st == MechState.BoostDash || st == MechState.BoostStep;
                thrusting = dashing || (st == MechState.Airborne && rig.controller.velocity.y > 0.5f);

                // Landing dust the moment the landing recovery starts
                if (st == MechState.Landing && rig.prevState != MechState.Landing)
                    ProceduralVfx.DustPuff(rig.root.position, 16, 1.1f);
                rig.prevState = st;
            }
            else
            {
                // Enemy AI: state is private - infer from horizontal speed.
                // Thresholds sit ABOVE its buffed walk speed (~10.4 with the big-map
                // scale) - at 9 the jets never turned off, a constant spray while walking.
                Vector3 delta = rig.root.position - rig.lastPos;
                delta.y *= 0.35f;
                float speed = Time.deltaTime > 0.0001f ? delta.magnitude / Time.deltaTime : 0f;
                dashing = speed > 19f;
                thrusting = speed > 14f;
            }
            rig.lastPos = rig.root.position;

            // Jets: full flame while thrusting, off otherwise
            SetJet(rig.jetL, thrusting);
            SetJet(rig.jetR, thrusting);
            if (thrusting && !rig.prevThrust)
                ProceduralVfx.Sparks(rig.root.TransformPoint(0f, 1.3f, -0.35f),
                                     new Color(0.55f, 0.85f, 1f), 8, 3.5f, 0.25f, 0.12f, 0f);
            rig.prevThrust = thrusting;

            if (rig.trail != null) rig.trail.emitting = dashing;

            // ---- knockdown impact: ring + dust + shake (any damage source) ----
            if (rig.health != null)
            {
                bool down = rig.health.isYellowLocked && rig.health.currentHealth > 0f;
                if (down && !rig.prevDown)
                {
                    ProceduralVfx.ShockRing(rig.root.position, new Color(1f, 0.85f, 0.4f, 0.45f), 4.5f);
                    ProceduralVfx.DustPuff(rig.root.position, 18, 1.3f);
                    if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.22f, 0.35f);
                }
                rig.prevDown = down;

                // Death: the big one. BattleJuice slows time, BattleAudio booms -
                // this is the visual: double fireball + hard shake.
                bool dead = rig.health.currentHealth <= 0f;
                if (dead && !rig.prevDead)
                {
                    Vector3 p = rig.root.position + Vector3.up * 1.1f;
                    ProceduralVfx.Fireball(p, 1.6f);
                    ProceduralVfx.Fireball(p + Vector3.up * 0.8f, 1.0f);
                    if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.4f, 0.6f);
                }
                rig.prevDead = dead;
            }
        }

        if (!anyAlive) Rehook();
    }

    private static void SetJet(ParticleSystem ps, bool on)
    {
        if (ps == null) return;
        var emission = ps.emission;
        float target = on ? 85f : 0f;
        if (!Mathf.Approximately(emission.rateOverTime.constant, target))
            emission.rateOverTime = target;
    }
}
