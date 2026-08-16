using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The player's two special moves (pitch promise: every mech has 2 specials with
/// cooldowns). References: Gundam EXVS "gerobi" irradiation beams and funnel
/// barrages.
///
///   E  - GEROBI LASER: braced, sustained giant beam for ~1.5s. Shreds mechs,
///        demolishes buildings (explosions on the impact point), sweeps slowly
///        toward the enemy. You are rooted and vulnerable while firing.
///   R  - FUNNEL BARRAGE: four remote orbs deploy around you and each fires a
///        strongly-homing shot at the enemy. A boost step still breaks their
///        tracking - dodgeable, like everything else.
///
/// Self-installs: MechCombat adds this component to the player at Start, so it
/// survives scene reloads without scene setup. The HUD reads the cooldowns.
/// </summary>
public class SpecialMoves : MonoBehaviour
{
    [Header("Gerobi Laser (E)")]
    public float laserCooldown = 12f;
    public float laserDuration = 1.5f;
    [Tooltip("Damage per beam tick. TRIPLED from 6 - the gerobi commits you to a stationary hover with a long telegraph, so landing one has to actually decide the round.")]
    public float laserDamageTick = 18f;
    public float laserBarPowerPerTick = 12f;
    public float laserTickInterval = 0.15f;
    public float laserBuildingDamagePerTick = 30f;
    public float laserSweepDegPerSec = 28f;
    public float laserBoostDrainPerSec = 14f;
    public float laserRange = 130f;
    [Tooltip("Radius of the EXVS-style expanding blast sphere at the beam's impact point. Anyone caught inside is held in the get-hit state with rapid damage + knockdown-bar ticks until it pops them or fades.")]
    public float laserBlastRadius = 8f;

    [Header("Funnel Barrage (R)")]
    public float funnelCooldown = 9f;
    public int funnelCount = 4;
    public float funnelShotDamage = 9f;
    public float funnelShotBarPower = 18f;
    public float funnelShotSpeed = 55f;
    public float funnelShotTurnRate = 220f;

    private MechController mc;
    private MechCombat combat;
    private BoostManager boost;
    private TargetManager targets;
    private MechShooter shooter;

    private InputAction laserAction, funnelAction;
    private float laserReadyAt = -99f, funnelReadyAt = -99f;

    /// <summary>BURST perk: both specials come off cooldown instantly.</summary>
    public void RefreshCooldowns() { laserReadyAt = -99f; funnelReadyAt = -99f; }

    /// <summary>BURST perk: cooldowns tick faster - called with the EXTRA seconds
    /// to advance (0.5 * dt for +50% recharge speed while bursting).</summary>
    public void AccelerateCooldowns(float extraSeconds) { laserReadyAt -= extraSeconds; funnelReadyAt -= extraSeconds; }
    private bool firingLaser;

    // For the HUD: 1 = ready, counts up from 0 while cooling down
    public float LaserReady01 => Mathf.Clamp01(1f - (laserReadyAt - Time.time) / laserCooldown);
    public float FunnelReady01 => Mathf.Clamp01(1f - (funnelReadyAt - Time.time) / funnelCooldown);

    private void Awake()
    {
        mc = GetComponent<MechController>();
        combat = GetComponent<MechCombat>();
        boost = GetComponent<BoostManager>();
        targets = GetComponent<TargetManager>();
        shooter = GetComponent<MechShooter>();

        laserAction = new InputAction("SpecialLaser", InputActionType.Button);
        laserAction.AddBinding("<Keyboard>/e");
        funnelAction = new InputAction("SpecialFunnel", InputActionType.Button);
        funnelAction.AddBinding("<Keyboard>/r");
    }

    private void OnEnable() { laserAction.Enable(); funnelAction.Enable(); }
    private void OnDisable() { laserAction.Disable(); funnelAction.Disable(); KillActiveBeamRoot(); }

    private void Update()
    {
        if (mc == null || firingLaser) return;

        // BoostDash counts as free. Being unable to answer a dash with a special was
        // the one state where the kit went quiet: E braces into its hover anyway (the
        // routine sets Attacking, which ends the dash by itself), and R fires without
        // touching movement state at all - so the missiles leave mid-dash and the dash
        // carries on. That is exactly the dash-into-salvo the mode wants to reward.
        bool free = (mc.currentState == MechState.Grounded ||
                     mc.currentState == MechState.Airborne ||
                     mc.currentState == MechState.BoostDash)
                    && (combat == null || (!combat.IsBusyAttacking && !combat.IsShielding));

        if (free && laserAction.WasPressedThisFrame() && Time.time >= laserReadyAt &&
            boost != null && boost.currentBoost > 15f && !boost.isOverheated)
        {
            StartCoroutine(GerobiRoutine());
        }
        else if (free && funnelAction.WasPressedThisFrame() && Time.time >= funnelReadyAt)
        {
            StartCoroutine(FunnelRoutine());
        }
    }

    private Transform GetTarget()
    {
        if (targets != null && targets.currentTarget != null) return targets.currentTarget;
        return mc != null ? mc.enemyTarget : null;
    }

    // ------------------------------------------------------------------ laser

    private IEnumerator GerobiRoutine()
    {
        firingLaser = true;
        laserReadyAt = Time.time + laserCooldown;

        // Brace: rooted like an attack, frozen mid-windup pose (EXVS gerobi stance)
        mc.currentState = MechState.Attacking;
        CharacterController cc = GetComponent<CharacterController>();
        Animator anim = mc.animator;
        if (anim != null)
        {
            anim.CrossFadeInFixedTime("punch2", 0.1f, 0);
        }
        // PRE-FIRE CINEMATIC: low hero-shot sweeping around the mech during the
        // brace + lift-off, then the camera releases into a pulled-back framing
        // exactly as the beam erupts.
        if (LockOnBattleCamera.Instance != null)
        {
            LockOnBattleCamera.Instance.SpecialCinematic(transform, 0.85f);
            LockOnBattleCamera.Instance.SpecialKick(3.5f, laserDuration + 1.2f);
        }
        BattleAudio.Play("burst", 0.5f, 1.4f); // charging whine under the windup

        // LIFT-OFF: the mech rises into the sky and hovers there for the whole
        // firing (EXVS satellite-cannon stance) - it does not move again until
        // the beam ends. Thrusters visibly push it up.
        float riseTime = 0f;
        while (riseTime < 0.4f)
        {
            if (mc.currentState != MechState.Attacking) break; // staggered out of the windup
            mc.velocity = Vector3.zero;
            mc.currentMomentum = Vector3.zero;
            if (cc != null) cc.Move(Vector3.up * 7.5f * Time.deltaTime);
            riseTime += Time.deltaTime;
            yield return null;
        }
        // Staggered out of the lift-off: abort cleanly, no beam
        if (mc.currentState != MechState.Attacking)
        {
            if (anim != null) anim.speed = 1f;
            firingLaser = false;
            yield break;
        }
        if (anim != null) anim.speed = 0f; // hold the brace pose
        Vector3 anchor = transform.position; // pinned here until the beam ends

        // Aim: LOCKED at the moment of firing - the beam does NOT track. Dodging
        // sideways after the flash beats it, like the reference games.
        Transform target = GetTarget();
        Vector3 origin = MuzzlePos();
        Vector3 dir = target != null
            ? (target.position + Vector3.up * 1.2f - origin).normalized
            : transform.forward;

        // Face the shot once, then hold
        Vector3 faceFlat = dir; faceFlat.y = 0f;
        if (faceFlat.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(faceFlat.normalized);

        // EXVS-scale beam: blinding white core inside two cyan glow layers, a
        // flare ball at the muzzle and another at the impact point. The whole
        // thing swells in over the first tenth of a second.
        // Same hazard as the AI's beam: this is an unparented scene object destroyed
        // by a line at the END of this coroutine. Anything that stops the coroutine
        // early - a knockdown mid-beam, a stage reset - would leave it standing.
        // The delayed Destroy is scheduled on the object itself and cannot be
        // interrupted; the explicit one below is still the normal path.
        GameObject beamRoot = new GameObject("Gerobi Beam");
        activeBeamRoot = beamRoot;
        Object.Destroy(beamRoot, laserDuration + 2f);
        Transform core = BeamCylinder(beamRoot.transform, 0.45f, new Color(1f, 1f, 1f, 0.98f));
        Transform mid  = BeamCylinder(beamRoot.transform, 1.30f, new Color(0.45f, 0.95f, 1f, 0.40f));
        Transform glow = BeamCylinder(beamRoot.transform, 2.40f, new Color(0.30f, 0.85f, 1f, 0.16f));
        Transform muzzleFlare = FlareBall(beamRoot.transform, 2.0f, new Color(0.8f, 1f, 1f, 0.55f));
        Transform impactFlare = FlareBall(beamRoot.transform, 2.6f, new Color(0.7f, 1f, 1f, 0.5f));

        float t = 0f, nextTick = 0f, nextBuildingFx = 0f;
        BlastSphere blast = null;          // ONE expanding blast sphere per firing
        float lastBlastAt = -99f;
        Vector3 lastBlastPos = Vector3.zero;
        while (t < laserDuration)
        {
            // Stagger / knockdown interrupts the beam
            if (mc.currentState != MechState.Attacking) break;

            // Hovering: hard position hold at the anchor (cancels gravity, knockback
            // shoves, everything). The boost bar is FROZEN while firing - it neither
            // drains nor recharges. A free full recharge on a giant beam was pure
            // value with zero risk; now committing to the gerobi costs you the
            // recharge time you would have gotten by landing instead.
            mc.velocity = Vector3.zero;
            mc.currentMomentum = Vector3.zero;
            if (cc != null) cc.Move(anchor - transform.position);

            origin = MuzzlePos();
            // dir stays EXACTLY where it was aimed at the flash - no tracking

            // Where does the beam stop?
            float len = laserRange;
            RaycastHit hit;
            bool hitSomething = Physics.Raycast(origin, dir, out hit, laserRange, ~0, QueryTriggerInteraction.Ignore);
            if (hitSomething && hit.transform.root != transform.root) len = hit.distance;
            else if (hitSomething) { hitSomething = false; }

            // Swell-in over the first 0.12s, slight flicker after
            float grow = Mathf.Clamp01(t / 0.12f);
            float flicker = 0.92f + 0.08f * Mathf.Sin(t * 47f);
            float w = grow * flicker;
            PoseBeam(core, origin, dir, len, 0.45f * w);
            PoseBeam(mid,  origin, dir, len, 1.30f * w);
            PoseBeam(glow, origin, dir, len, 2.40f * w);
            muzzleFlare.position = origin;
            muzzleFlare.localScale = Vector3.one * (2.0f * w);
            impactFlare.position = origin + dir * len;
            impactFlare.localScale = Vector3.one * (hitSomething ? 2.6f * w : 0.001f);

            if (t >= nextTick)
            {
                nextTick = t + laserTickInterval;
                if (hitSomething)
                {
                    // THE EXVS BLAST: wherever the beam lands, an expanding damage
                    // sphere takes over - it staggers, ticks damage + knockdown bar
                    // on anything caught inside, stops at max radius, then fades.
                    // (Direct beam damage to mechs is gone: the sphere IS the damage,
                    // so getting clipped and getting engulfed feel the same.)
                    bool blastGone = blast == null;
                    bool impactMovedFar = Vector3.Distance(hit.point, lastBlastPos) > laserBlastRadius * 0.9f;
                    if ((blastGone || impactMovedFar) && Time.time - lastBlastAt > 0.8f)
                    {
                        blast = BlastSphere.Spawn(hit.point, transform, laserBlastRadius);
                        lastBlastAt = Time.time;
                        lastBlastPos = hit.point;
                    }

                    // The beam itself still cuts buildings down directly
                    BreakableBuilding bb = hit.collider.GetComponentInParent<BreakableBuilding>();
                    if (bb != null)
                    {
                        bb.TakeHit(laserBuildingDamagePerTick, hit.point);
                        if (t >= nextBuildingFx)
                        {
                            nextBuildingFx = t + 0.3f;
                            CombatVfx.SpawnExplosion(hit.point);
                        }
                    }
                }
            }

            t += Time.deltaTime;
            yield return null;
        }

        Object.Destroy(beamRoot);
        if (activeBeamRoot == beamRoot) activeBeamRoot = null;
        if (anim != null) anim.speed = 1f;
        if (mc.currentState == MechState.Attacking)
            mc.currentState = mc.CheckIfGrounded() ? MechState.Grounded : MechState.Airborne;
        firingLaser = false;
    }

    private GameObject activeBeamRoot;

    private void OnDestroy() { KillActiveBeamRoot(); }

    /// <summary>Kill the beam visual now - used when the firing is interrupted.</summary>
    public void KillActiveBeamRoot()
    {
        if (activeBeamRoot != null) Object.Destroy(activeBeamRoot);
        activeBeamRoot = null;
    }

    private Vector3 MuzzlePos()
    {
        if (shooter != null && shooter.muzzle != null) return shooter.muzzle.position;
        return transform.position + transform.forward * 1.1f + Vector3.up * 1.4f;
    }

    private static Transform BeamCylinder(Transform parent, float width, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        Renderer r = go.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        go.name = "beam " + width;
        return go.transform;
    }

    private static Transform FlareBall(Transform parent, float scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * scale;
        Renderer r = go.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private static void PoseBeam(Transform cyl, Vector3 origin, Vector3 dir, float length, float width)
    {
        cyl.position = origin + dir * (length * 0.5f);
        cyl.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        cyl.localScale = new Vector3(width, length * 0.5f, width);
    }

    // ------------------------------------------------------------------ funnels

    private IEnumerator FunnelRoutine()
    {
        funnelReadyAt = Time.time + funnelCooldown;
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.SpecialKick(1.2f, 0.9f);

        Transform target = GetTarget();
        var orbs = new GameObject[funnelCount];
        for (int i = 0; i < funnelCount; i++)
        {
            // Deploy in an arc behind the shoulders
            float side = (i % 2 == 0) ? 1f : -1f;
            float tier = 1f + (i / 2) * 0.7f;
            Vector3 pos = transform.position
                        + transform.right * side * (1.1f + 0.5f * (i / 2))
                        + Vector3.up * (1.6f + tier * 0.5f)
                        - transform.forward * 0.4f;
            orbs[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.Destroy(orbs[i].GetComponent<Collider>());
            Object.Destroy(orbs[i], 3f); // survives the coroutine being stopped mid-salvo
            orbs[i].transform.position = pos;
            orbs[i].transform.localScale = Vector3.one * 0.32f;
            Renderer r = orbs[i].GetComponent<Renderer>();
            r.material = new Material(Shader.Find("Sprites/Default"));
            r.material.color = new Color(1f, 0.45f, 0.9f);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            orbs[i].name = "Funnel";
        }

        yield return new WaitForSeconds(0.35f); // deploy beat - readable for the opponent

        for (int i = 0; i < funnelCount; i++)
        {
            if (orbs[i] == null) continue;
            target = GetTarget();
            Vector3 from = orbs[i].transform.position;
            Vector3 aim = target != null ? (target.position + Vector3.up * 1.3f - from).normalized : transform.forward;
            HomingProjectile shot = HomingProjectile.SpawnSimple(from, Quaternion.LookRotation(aim), 1.1f, new Color(1f, 0.45f, 0.9f));

            // Real MISSILES, not beam orbs: the imported pack's model + exhaust
            // smoke, and its explosion on impact. Strips the beam bolt's own glow
            // layers so the model reads clean. Falls back to the beam look if the
            // pack was never hooked up (Tools > Gundam > 10).
            if (MissileAssets.MissileModel != null)
            {
                foreach (Renderer br in shot.GetComponentsInChildren<Renderer>(true))
                    if (br is MeshRenderer) br.enabled = false;
                TrailRenderer beamTrail = shot.GetComponent<TrailRenderer>();
                if (beamTrail != null) beamTrail.emitting = false;

                MissileAssets.DressAsMissile(shot.gameObject, 1.4f);
                shot.isMissile = true;
                shot.missileFxScale = 1.1f;
            }

            shot.damage = funnelShotDamage;
            shot.knockdownPower = funnelShotBarPower;
            shot.speed = funnelShotSpeed;
            shot.turnRateDegPerSec = funnelShotTurnRate;
            shot.homingDuration = 0.9f; // funnels chase harder than rifle fire...
            shot.Init(target, transform); // ...but a boost step still breaks them
            Object.Destroy(orbs[i]);
            yield return new WaitForSeconds(0.15f);
        }
    }
}
