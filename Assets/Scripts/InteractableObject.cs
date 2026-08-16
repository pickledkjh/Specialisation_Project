using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// THE PITCH'S "INTERACTABLE OBJECTS": random things scattered around the map
/// that either mech can weaponize when close enough. Four types, all procedural
/// (no assets):
///   TREE          - heavy arc lob, SMASHES a small area on landing (mini AoE)
///   FUEL BARREL   - EXPLODES on arrival: big fire AoE, hurts anyone near
///   CAR WRECK     - wrecking ball: a direct hit INSTANTLY fills the down bar
///   ANTENNA MAST  - EMP javelin: low damage but a LONG electric stun (sets up melee)
///
/// PLAYER: walk within range - a hint appears - press G to hurl it at your target.
/// (Moved from F: the dash tackle owns F now.)
/// AI: the enemy automatically uses one when it happens to be close (cooldown'd),
/// so the map fights back. Thrown objects home lightly; a boost step still dodges
/// them. The manager keeps the map stocked by respawning new objects over time.
/// Self-installing, survives rematch reloads.
/// </summary>
public class InteractableObject : MonoBehaviour
{
    public enum Kind { Tree, Barrel, Car, Antenna }
    public Kind kind;
    public float damage = 12f;
    public float barPower = 25f;
    public bool explodes;
    public float throwSpeed = 26f;
    public float arcHeight = 4f;

    public static readonly List<InteractableObject> All = new List<InteractableObject>();
    private void OnEnable() { All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    public string DisplayName
    {
        get
        {
            switch (kind)
            {
                case Kind.Tree: return "TREE";
                case Kind.Barrel: return "FUEL BARREL";
                case Kind.Car: return "CAR WRECK";
                default: return "ANTENNA MAST";
            }
        }
    }

    /// <summary>Hurl this object at a target. It becomes a projectile and is gone
    /// from the map (the manager respawns new ones elsewhere).</summary>
    public void Throw(Transform thrower, Transform target)
    {
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
        ThrownInteractable proj = gameObject.AddComponent<ThrownInteractable>();
        proj.Init(this, thrower, target);
        BattleAudio.Play("saber", 0.7f, 1.3f); // whoosh
        ProceduralVfx.DustPuff(transform.position, 10, 0.8f);
        enabled = false; // out of the interactable pool immediately
    }
}

/// <summary>The in-flight state of a thrown interactable: light homing, tumbling,
/// impact = damage + stagger (+ explosion for barrels).</summary>
public class ThrownInteractable : MonoBehaviour
{
    private InteractableObject source;
    private Transform thrower, target;
    private Vector3 velocity;
    private Vector3 spin;
    private float age;
    private const float MaxAge = 4f;

    public void Init(InteractableObject src, Transform who, Transform tgt)
    {
        source = src;
        thrower = who;
        target = tgt;

        Vector3 aim = tgt != null ? (tgt.position + Vector3.up * 1.2f - transform.position) : who.forward * 20f;
        Vector3 flat = aim; flat.y = 0f;
        velocity = aim.normalized * src.throwSpeed + Vector3.up * src.arcHeight;
        spin = Random.insideUnitSphere * 5f;
        transform.position += Vector3.up * 1.5f; // lift off its resting spot

        // Per-type flight identity: each object streaks its own color through the air
        Color trailCol;
        switch (src.kind)
        {
            case InteractableObject.Kind.Barrel: trailCol = new Color(1f, 0.5f, 0.15f, 0.8f); break;  // burning orange
            case InteractableObject.Kind.Car: trailCol = new Color(0.6f, 0.62f, 0.68f, 0.7f); break;  // grey smoke
            case InteractableObject.Kind.Antenna: trailCol = new Color(0.4f, 0.8f, 1f, 0.85f); break; // electric cyan
            default: trailCol = new Color(0.4f, 0.7f, 0.3f, 0.7f); break;                             // leafy green
        }
        TrailRenderer tr = gameObject.AddComponent<TrailRenderer>();
        tr.time = 0.35f;
        tr.startWidth = src.kind == InteractableObject.Kind.Antenna ? 0.25f : 0.6f;
        tr.endWidth = 0.03f;
        tr.numCapVertices = 3;
        tr.material = new Material(Shader.Find("Sprites/Default"));
        tr.startColor = trailCol;
        tr.endColor = new Color(trailCol.r, trailCol.g, trailCol.b, 0f);
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Barrels visibly ignite in flight
        if (src.kind == InteractableObject.Kind.Barrel)
            ProceduralVfx.FlashLight(transform.position, new Color(1f, 0.55f, 0.2f), 1.5f, 4f, 0.5f);
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (age > MaxAge) { Impact(null); return; }

        // Light homing: bends toward the target but a boost step escapes it
        if (target != null)
        {
            Vector3 want = (target.position + Vector3.up * 1.1f - transform.position).normalized * source.throwSpeed;
            velocity = Vector3.RotateTowards(velocity, want, 55f * Mathf.Deg2Rad * Time.deltaTime, 2f * Time.deltaTime);
        }
        velocity += Vector3.down * 6f * Time.deltaTime; // gentle gravity for the arc
        transform.position += velocity * Time.deltaTime;
        transform.Rotate(spin * 60f * Time.deltaTime, Space.World);

        // Hit the target mech?
        if (target != null)
        {
            MechHealth th = target.GetComponentInParent<MechHealth>();
            if (th != null && Vector3.Distance(transform.position, target.position + Vector3.up * 1.1f) < 1.6f)
            {
                Impact(th);
                return;
            }
        }

        // Hit the ground / a building?
        if (transform.position.y < 0.3f) { Impact(null); return; }
    }

    private void Impact(MechHealth directHit)
    {
        Vector3 pos = transform.position;
        InteractableObject.Kind kind = source != null ? source.kind : InteractableObject.Kind.Tree;

        switch (kind)
        {
            case InteractableObject.Kind.Barrel:
                // FIRE AOE: big blast, hurts ANYONE nearby - including the thrower
                ProceduralVfx.Fireball(pos, 1.1f);
                BattleAudio.Play("explosion", 1f);
                if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.2f, 0.3f);
                AreaHit(pos, 6f, source.damage, source.barPower, 8f, 0.8f, excludeThrower: false);
                // ...and leaves them BURNING: the blast is the hook, the burn is the
                // reason the red barrel is the scary one. Ticks after the explosion,
                // so a glancing hit still costs you something.
                BurnEffect.Apply(pos, 6f, thrower);
                break;

            case InteractableObject.Kind.Tree:
                // GROUND SMASH: small area thump where it lands (no fire, just mass)
                ProceduralVfx.DustPuff(pos, 20, 1.6f);
                ProceduralVfx.ShockRing(pos, new Color(0.75f, 0.65f, 0.5f, 0.5f), 4f);
                BattleAudio.Play("hit", 1f, 0.6f);
                AreaHit(pos, 3.2f, source.damage, source.barPower, 6f, 0.8f, excludeThrower: true);
                break;

            case InteractableObject.Kind.Car:
                // WRECKING BALL: a direct hit INSTANTLY fills the down bar
                if (directHit != null && !directHit.isYellowLocked)
                {
                    Vector3 away = directHit.transform.position - (thrower != null ? thrower.position : pos);
                    away.y = 0f;
                    away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                    float carDmg = source.damage, carBar = 100f, carStun = 0f;
                    if (TeamRules.ResolveHit(thrower, directHit, ref carDmg, ref carBar, ref carStun))
                        directHit.TakeDamage(carDmg, carBar, away * 10f + Vector3.up * 6f); // bar-filling = the cinematic fling
                    CombatVfx.SpawnHit(pos);
                }
                else { ProceduralVfx.DustPuff(pos, 14, 1.4f); BattleAudio.Play("hit", 0.8f, 0.5f); }
                break;

            default: // Antenna mast - EMP javelin
                if (directHit != null && !directHit.isYellowLocked)
                {
                    float mastDmg = source.damage, mastBar = source.barPower, mastStun = 1.8f;
                    if (TeamRules.ResolveHit(thrower, directHit, ref mastDmg, ref mastBar, ref mastStun))
                    {
                        directHit.TakeDamage(mastDmg, mastBar, Vector3.zero);
                        Stagger(directHit, mastStun); // LONG electric stun - the melee setup tool
                    }
                    CombatVfx.SpawnParry(directHit.transform); // electric zap visuals + sound
                }
                else { ProceduralVfx.Sparks(pos, new Color(0.55f, 0.75f, 1f), 16, 6f); BattleAudio.Play("parry", 0.6f); }
                break;
        }

        Destroy(gameObject);
    }

    // Area damage helper for the tree smash / barrel blast
    private void AreaHit(Vector3 pos, float radius, float dmg, float bar, float push, float stun, bool excludeThrower)
    {
        foreach (MechHealth mh in Object.FindObjectsByType<MechHealth>(FindObjectsSortMode.None))
        {
            if (mh == null || mh.isYellowLocked) continue;
            if (excludeThrower && thrower != null && mh.transform.root == thrower.root) continue;
            if (Vector3.Distance(mh.transform.position, pos) > radius) continue;
            Vector3 away = mh.transform.position - pos; away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;

            // TEAM RULES: a thrown barrel does not care who it lands on, but in a 2v2
            // catching your own partner in the blast should cost a scratch, not the round.
            float d = dmg, b = bar, st = stun;
            if (!TeamRules.ResolveHit(thrower, mh, ref d, ref b, ref st)) continue;
            mh.TakeDamage(d, b, away * push + Vector3.up * 5f);
            Stagger(mh, st);
        }
    }

    private static void Stagger(MechHealth mh, float seconds)
    {
        if (mh == null || mh.isYellowLocked) return;
        MechController pc = mh.GetComponent<MechController>();
        SimpleMechAI ai = mh.GetComponent<SimpleMechAI>();
        if (pc != null) pc.TakeHit(seconds);
        else if (ai != null) ai.TakeHit(seconds);
    }
}

/// <summary>Burning: the fuel barrel's lingering damage-over-time. Attaches to a
/// victim, ticks damage and knockdown bar for a few seconds with a fire plume, and
/// refreshes rather than stacking if they get caught by a second barrel.</summary>
public class BurnEffect : MonoBehaviour
{
    public float damagePerTick = 3f;
    public float barPerTick = 4f;
    public float tickInterval = 0.5f;
    public float duration = 4f;

    private MechHealth health;
    private float endsAt;
    private float nextTick;
    private ParticleSystem flame;

    /// <summary>Sets everything inside the radius alight (the thrower included -
    /// standing next to your own barrel should hurt).</summary>
    public static void Apply(Vector3 pos, float radius, Transform thrower)
    {
        foreach (MechHealth mh in Object.FindObjectsByType<MechHealth>(FindObjectsSortMode.None))
        {
            if (mh == null || mh.currentHealth <= 0f || mh.isYellowLocked) continue;
            if (Vector3.Distance(mh.transform.position, pos) > radius) continue;

            BurnEffect b = mh.GetComponent<BurnEffect>();
            if (b == null) b = mh.gameObject.AddComponent<BurnEffect>();
            b.Ignite(mh);
        }
    }

    private void Ignite(MechHealth mh)
    {
        health = mh;
        endsAt = Time.time + duration;   // refresh, never stack
        nextTick = Time.time + tickInterval;
        if (flame == null)
        {
            flame = ProceduralVfx.MakeJet(transform, Vector3.up * 1.1f,
                Quaternion.LookRotation(Vector3.up), new Color(1f, 0.5f, 0.12f), 0.3f);
            var em = flame.emission;
            em.rateOverTime = 45f;
        }
        BattleAudio.Play("explosion", 0.25f, 1.7f);
    }

    private void Update()
    {
        if (health == null || health.currentHealth <= 0f) { Extinguish(); return; }
        if (Time.time >= endsAt) { Extinguish(); return; }

        if (Time.time >= nextTick)
        {
            nextTick = Time.time + tickInterval;
            // No launch vector: burning chips you down, it does not juggle you
            health.TakeDamage(damagePerTick, barPerTick);
            ProceduralVfx.Sparks(transform.position + Vector3.up * 1.2f,
                                 new Color(1f, 0.55f, 0.15f), 6, 4f, 0.35f, 0.1f);
        }
    }

    private void Extinguish()
    {
        if (flame != null)
        {
            var em = flame.emission;
            em.rateOverTime = 0f;
            Object.Destroy(flame.gameObject, 0.6f);
            flame = null;
        }
        Destroy(this);
    }
}

/// <summary>Spawns and restocks the interactables, runs the player's F-key pickup
/// and the AI's automatic use, and shows the proximity hint.</summary>
public class InteractableManager : MonoBehaviour
{
    [Tooltip("How many interactables the map tries to hold at once. The pitch calls these a UNIQUE SELLING POINT ('interactable objects will make the battle unpredictable'), so the map should always have several within reach, not a scarce dozen spread over a 160u arena.")]
    public int liveObjectTarget = 26;
    public float useRange = 5f;
    public float aiUseCooldown = 11f;
    public float minSpawnRadius = 10f;
    public float maxSpawnRadius = 140f;

    [Tooltip("Seconds between restock ticks DURING the fight. Objects are consumed fast once both mechs start throwing them, and at the old 6s the map emptied out and never recovered.")]
    public float restockInterval = 1.6f;
    [Tooltip("How many are added per restock tick, so a heavily used map refills at a visible rate.")]
    public int restockBatch = 2;
    [Tooltip("Keep at least this many close to the FIGHT itself - objects spawn near the midpoint between the two mechs, so there is always something to grab where the action is.")]
    public int nearFightTarget = 6;
    [Tooltip("Radius around the fight that counts as 'near'.")]
    public float nearFightRadius = 28f;

    private MechController player;
    private SimpleMechAI enemy;
    private TargetManager targets;
    private InputAction useAction;
    private float nextRestockAt;
    private float aiReadyAt;
    private UiLabel hint;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<InteractableManager>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return;
        new GameObject("Interactables").AddComponent<InteractableManager>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        useAction = new InputAction("UseInteractable", InputActionType.Button);
        useAction.AddBinding("<Keyboard>/g"); // moved off F - F is the dash tackle now
        useAction.Enable();

        BuildUi();
        Rehook();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        useAction?.Disable();
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) { Rehook(); }

    private void Rehook()
    {
        player = Object.FindFirstObjectByType<MechController>();
        enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        targets = player != null ? player.GetComponent<TargetManager>() : null;
        aiReadyAt = Time.time + 6f;

        // fresh population for a fresh scene
        for (int i = InteractableObject.All.Count - 1; i >= 0; i--)
            if (InteractableObject.All[i] != null) Destroy(InteractableObject.All[i].gameObject);
        for (int i = 0; i < liveObjectTarget; i++) SpawnOne();

        // GUARANTEED sampler ring around the player spawn: one of EACH type ~9u
        // out, so all four are testable within seconds of starting the tutorial.
        if (player != null)
        {
            for (int k = 0; k < 4; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, 45f + 90f * k, 0f) * Vector3.forward;
                Vector3 pos = player.transform.position + dir * 9f;
                pos.y = 0f;
                // nudge outward a couple of times if the spot is blocked
                for (int tries = 0; tries < 3; tries++)
                {
                    if (!Physics.CheckSphere(pos + Vector3.up * 1.2f, 1.6f, ~0, QueryTriggerInteraction.Ignore)) break;
                    pos += dir * 4f;
                }
                BuildAt((InteractableObject.Kind)k, pos);
            }
        }

        // Startup diagnostic: proves the system is alive and says how far the
        // nearest object is - "G does nothing" is almost always "they're too far".
        InteractableObject near = player != null ? Nearest(player.transform.position) : null;
        Debug.Log("[Interactables] " + CountAlive() + " object(s) spawned. " +
                  (near != null
                      ? "Nearest ('" + near.DisplayName + "') is " +
                        Mathf.RoundToInt(Vector3.Distance(near.transform.position, player.transform.position)) +
                        "u from the player - walk within " + useRange + "u and the G hint appears."
                      : "NONE near the player!"));
    }

    private void Update()
    {
        if (player == null) { Rehook(); if (player == null) return; }

        // ---- LIVE RESTOCK: keeps running for the whole match ----
        if (Time.time >= nextRestockAt)
        {
            nextRestockAt = Time.time + restockInterval;

            int alive = CountAlive();
            for (int i = 0; i < restockBatch && alive < liveObjectTarget; i++)
            {
                SpawnOne();
                alive++;
            }

            // ...and specifically top up around wherever the fight actually is, so
            // a duel that drifts to a corner of the map still has objects to use.
            if (enemy != null)
            {
                Vector3 fightCentre = (player.transform.position + enemy.transform.position) * 0.5f;
                if (CountAliveNear(fightCentre, nearFightRadius) < nearFightTarget)
                    SpawnNear(fightCentre);
            }
        }

        // ---- player use ----
        InteractableObject near = Nearest(player.transform.position);
        bool canUse = near != null &&
                      Vector3.Distance(near.transform.position, player.transform.position) <= useRange &&
                      Time.timeScale > 0.5f;
        if (hint != null)
        {
            hint.gameObject.SetActive(canUse);
            if (canUse) hint.text = "G  -  THROW THE " + near.DisplayName;
        }
        if (canUse && useAction.WasPressedThisFrame())
        {
            Transform tgt = targets != null && targets.currentTarget != null
                ? targets.currentTarget
                : (enemy != null ? enemy.transform : null);
            near.Throw(player.transform, tgt);
        }

        // ---- AI use: the map fights back ----
        if (enemy != null && Time.time >= aiReadyAt && Time.timeScale > 0.5f)
        {
            MechHealth eh = enemy.GetComponent<MechHealth>();
            if (eh != null && !eh.isYellowLocked && eh.currentHealth > 0f)
            {
                InteractableObject aiNear = Nearest(enemy.transform.position);
                if (aiNear != null &&
                    Vector3.Distance(aiNear.transform.position, enemy.transform.position) <= useRange)
                {
                    aiReadyAt = Time.time + aiUseCooldown;
                    aiNear.Throw(enemy.transform, player.transform);
                }
            }
        }
    }

    private static int CountAlive()
    {
        int n = 0;
        foreach (InteractableObject io in InteractableObject.All)
            if (io != null && io.enabled) n++;
        return n;
    }

    private static InteractableObject Nearest(Vector3 pos)
    {
        InteractableObject best = null;
        float bestDist = float.MaxValue;
        foreach (InteractableObject io in InteractableObject.All)
        {
            if (io == null || !io.enabled) continue;
            float d = Vector3.Distance(io.transform.position, pos);
            if (d < bestDist) { bestDist = d; best = io; }
        }
        return best;
    }

    // ---------- spawning / procedural models ----------

    /// <summary>Objects alive within a radius of a point - used to keep the fight
    /// itself stocked rather than just the map as a whole.</summary>
    private static int CountAliveNear(Vector3 centre, float radius)
    {
        int n = 0;
        foreach (InteractableObject io in InteractableObject.All)
            if (io != null && io.enabled && Vector3.Distance(io.transform.position, centre) <= radius) n++;
        return n;
    }

    /// <summary>Drops a fresh object in a clear spot near a point (the fight), so
    /// restocks arrive where they will actually get used.</summary>
    private void SpawnNear(Vector3 centre)
    {
        for (int tries = 0; tries < 14; tries++)
        {
            Vector2 off = Random.insideUnitCircle * nearFightRadius;
            Vector3 pos = centre + new Vector3(off.x, 0f, off.y);
            pos.y = 0f;

            // inside the arena, not too close to either mech, not inside geometry
            if (new Vector2(pos.x, pos.z).magnitude > ArenaLimits.Radius - 12f) continue;
            if (player != null && Vector3.Distance(pos, player.transform.position) < 6f) continue;
            if (enemy != null && Vector3.Distance(pos, enemy.transform.position) < 6f) continue;
            if (Physics.CheckSphere(pos + Vector3.up * 1.2f, 1.6f, ~0, QueryTriggerInteraction.Ignore)) continue;

            BuildAt((InteractableObject.Kind)Random.Range(0, 4), pos);
            return;
        }
    }

    private void SpawnOne()
    {
        // Find a clear spot (not inside a building). Distances are BIASED CLOSE
        // to the center (quadratic falloff): the fight starts there, and on a
        // not-yet-rebuilt small map a uniform 32-150u roll put most objects
        // beyond the old 95u walls - unreachable, which read as "F not working".
        Vector3 pos = Vector3.zero;
        bool found = false;
        for (int tries = 0; tries < 12 && !found; tries++)
        {
            float ang = Random.Range(0f, 360f);
            float maxR = Mathf.Min(maxSpawnRadius, ArenaLimits.Radius - 12f);
            float r = Mathf.Lerp(minSpawnRadius, maxR, Random.value * Random.value);
            pos = Quaternion.Euler(0f, ang, 0f) * Vector3.forward * r;
            found = !Physics.CheckSphere(pos + Vector3.up * 1.2f, 1.6f, ~0, QueryTriggerInteraction.Ignore);
        }
        if (!found) return; // crowded roll - try again at the next restock

        BuildAt((InteractableObject.Kind)Random.Range(0, 4), pos);
    }

    /// <summary>TUTORIAL: clear the map and lay out one of EACH kind in a neat arc
    /// directly in front of the player, so the interactables lesson is guaranteed to
    /// have something to teach with instead of hoping the random scatter cooperated.
    /// </summary>
    public static void StageTutorialSet(Transform inFrontOf)
    {
        InteractableManager mgr = Object.FindFirstObjectByType<InteractableManager>();
        if (mgr == null || inFrontOf == null) return;

        // wipe whatever is lying around so the arc reads clearly
        for (int i = InteractableObject.All.Count - 1; i >= 0; i--)
        {
            InteractableObject io = InteractableObject.All[i];
            if (io != null) Object.Destroy(io.gameObject);
        }

        Vector3 fwd = inFrontOf.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        // one of each kind, spread across a shallow arc 6u ahead
        float[] offsets = { -4.5f, -1.5f, 1.5f, 4.5f };
        for (int k = 0; k < 4; k++)
        {
            Vector3 pos = inFrontOf.position + fwd * 6f + right * offsets[k];
            pos.y = 0f;
            mgr.BuildAt((InteractableObject.Kind)k, pos);
            ProceduralVfx.DustPuff(pos, 12, 1f);
        }
        Debug.Log("[Interactables] tutorial set staged in front of the player (one of each kind).");
    }

    public void BuildAt(InteractableObject.Kind kind, Vector3 pos)
    {
        GameObject go = new GameObject("Interactable " + kind);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        InteractableObject io = go.AddComponent<InteractableObject>();
        io.kind = kind;

        switch (kind)
        {
            case InteractableObject.Kind.Tree:
                io.damage = 12f; io.barPower = 26f; io.throwSpeed = 22f; io.arcHeight = 6f;
                Part(go, PrimitiveType.Cylinder, new Vector3(0f, 1.1f, 0f), new Vector3(0.35f, 1.1f, 0.35f), new Color(0.45f, 0.3f, 0.18f));
                Part(go, PrimitiveType.Sphere, new Vector3(0f, 2.8f, 0f), new Vector3(2.2f, 1.9f, 2.2f), new Color(0.2f, 0.55f, 0.2f));
                AddBodyCollider(go, new Vector3(0.7f, 3.8f, 0.7f), 1.9f);
                break;

            case InteractableObject.Kind.Barrel:
                io.damage = 15f; io.barPower = 30f; io.explodes = true; io.throwSpeed = 24f; io.arcHeight = 5f;
                Part(go, PrimitiveType.Cylinder, new Vector3(0f, 0.75f, 0f), new Vector3(0.85f, 0.75f, 0.85f), new Color(0.8f, 0.2f, 0.12f));
                Part(go, PrimitiveType.Cylinder, new Vector3(0f, 1.2f, 0f), new Vector3(0.85f, 0.06f, 0.85f), new Color(0.95f, 0.75f, 0.2f));
                AddBodyCollider(go, new Vector3(1.0f, 1.5f, 1.0f), 0.75f);
                break;

            case InteractableObject.Kind.Car:
                io.damage = 16f; io.barPower = 34f; io.throwSpeed = 20f; io.arcHeight = 5.5f;
                Part(go, PrimitiveType.Cube, new Vector3(0f, 0.55f, 0f), new Vector3(1.5f, 0.7f, 3.1f), new Color(0.35f, 0.45f, 0.6f));
                Part(go, PrimitiveType.Cube, new Vector3(0f, 1.25f, -0.2f), new Vector3(1.3f, 0.6f, 1.6f), new Color(0.55f, 0.65f, 0.75f));
                AddBodyCollider(go, new Vector3(1.6f, 1.7f, 3.2f), 0.85f);
                break;

            default: // Antenna mast
                io.damage = 10f; io.barPower = 20f; io.throwSpeed = 40f; io.arcHeight = 1.5f;
                Part(go, PrimitiveType.Cylinder, new Vector3(0f, 2.1f, 0f), new Vector3(0.12f, 2.1f, 0.12f), new Color(0.7f, 0.72f, 0.75f));
                Transform tip = Part(go, PrimitiveType.Sphere, new Vector3(0f, 4.3f, 0f), new Vector3(0.35f, 0.35f, 0.35f), new Color(1f, 0.35f, 0.3f));
                Renderer tr = tip.GetComponent<Renderer>();
                tr.material = new Material(Shader.Find("Sprites/Default"));
                tr.material.color = new Color(1f, 0.35f, 0.3f, 0.9f); // glowing beacon
                AddBodyCollider(go, new Vector3(0.5f, 4.5f, 0.5f), 2.25f);
                break;
        }

        ProceduralVfx.DustPuff(pos, 8, 0.7f); // arrival poof so restocks are visible
    }

    private static Transform Part(GameObject parent, PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        Object.Destroy(part.GetComponent<Collider>()); // one clean body collider instead
        part.transform.SetParent(parent.transform, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        Renderer r = part.GetComponent<Renderer>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit != null)
        {
            Material m = new Material(lit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            r.material = m;
        }
        return part.transform;
    }

    private static void AddBodyCollider(GameObject go, Vector3 size, float centerY)
    {
        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.size = size;
        bc.center = new Vector3(0f, centerY, 0f);
    }

    // ---------- UI ----------

    private void BuildUi()
    {
        GameObject canvasGo = new GameObject("Interactable Canvas");
        canvasGo.transform.SetParent(transform, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 12;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        hint = UiLabel.Create(canvasGo.transform, "Use Hint", new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f),
                              new Vector2(0f, 170f), new Vector2(700f, 34f), 26, true, TextAnchor.MiddleCenter);
        hint.color = new Color(0.6f, 1f, 0.7f);
        hint.gameObject.SetActive(false);
    }
}
