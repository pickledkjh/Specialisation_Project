using UnityEngine;

/// <summary>
/// EXVS-style expanding blast sphere - the big spherical explosion that ends a
/// gerobi (Wing Zero / GX satellite-cannon style). Spawned at the beam's impact
/// point: it expands to maxRadius, holds for a beat, then fades out. Any mech
/// caught inside (except the owner) is repeatedly staggered into the get-hit
/// state while taking rapid damage + knockdown-bar ticks - stay in the blast
/// and the bar WILL floor you. Buildings inside take demolition ticks too.
///
/// Built entirely at runtime: layered translucent spheres + light + shockwave.
/// </summary>
public class BlastSphere : MonoBehaviour
{
    [Header("Size / timing")]
    public float maxRadius = 8f;
    public float expandSeconds = 1.0f;
    public float holdSeconds = 0.45f;
    public float fadeSeconds = 0.35f;

    [Header("Damage (per tick while inside)")]
    [Tooltip("Damage per tick inside the blast sphere. Tripled from 4 alongside the beam itself.")]
    public float blastDamageTick = 12f;
    public float barPerTick = 18f;   // heavy: the gerobi is THE knockdown tool - ~4 ticks (0.7s inside) floors you
    public float tickInterval = 0.18f;
    public float staggerSeconds = 0.4f;   // re-applied every tick = held in get-hit
    public float buildingDamagePerTick = 22f;

    private Transform owner;              // the caster - immune to their own blast
    private float age;
    private float nextTickAt;
    private Transform shell, core;
    private Material shellMat, coreMat;
    private Light glow;

    private static readonly Color ShellColor = new Color(0.45f, 0.9f, 1f, 0.30f);
    private static readonly Color CoreColor = new Color(1f, 1f, 1f, 0.75f);

    public static BlastSphere Spawn(Vector3 position, Transform owner, float maxRadius = 8f)
    {
        GameObject go = new GameObject("Blast Sphere");
        go.transform.position = position;
        BlastSphere bs = go.AddComponent<BlastSphere>();
        bs.owner = owner;
        bs.maxRadius = maxRadius;
        return bs;
    }

    private void Start()
    {
        shell = MakeSphere(ShellColor, out shellMat);
        core = MakeSphere(CoreColor, out coreMat);

        GameObject lgo = new GameObject("Blast Light");
        lgo.transform.SetParent(transform, false);
        glow = lgo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.6f, 0.9f, 1f);
        glow.range = maxRadius * 2.2f;
        glow.intensity = 5f;

        // Arrival punctuation
        ProceduralVfx.ShockRing(new Vector3(transform.position.x, transform.position.y - 0.5f, transform.position.z),
                                new Color(0.5f, 0.9f, 1f, 0.5f), maxRadius * 1.5f, 0.5f);
        ProceduralVfx.Sparks(transform.position, new Color(0.7f, 0.95f, 1f), 26, 12f, 0.6f, 0.16f, 0.3f);
        BattleAudio.Play("explosion", 1f, 0.75f);
        if (LockOnBattleCamera.Instance != null) LockOnBattleCamera.Instance.Shake(0.3f, 0.5f);
    }

    private Transform MakeSphere(Color color, out Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(go.GetComponent<Collider>()); // damage is distance-checked, not collider-driven
        go.transform.SetParent(transform, false);
        Renderer r = go.GetComponent<Renderer>();
        mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = color;
        r.material = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go.transform;
    }

    private void Update()
    {
        age += Time.deltaTime;
        float total = expandSeconds + holdSeconds + fadeSeconds;

        // ---- radius over the three phases: ease-out expand -> hold -> hold-while-fading ----
        float radius;
        float alphaScale = 1f;
        if (age < expandSeconds)
        {
            float k = age / expandSeconds;
            radius = maxRadius * (1f - (1f - k) * (1f - k)); // fast start, soft arrival at max
        }
        else if (age < expandSeconds + holdSeconds)
        {
            radius = maxRadius;
        }
        else
        {
            radius = maxRadius;
            alphaScale = 1f - Mathf.Clamp01((age - expandSeconds - holdSeconds) / fadeSeconds);
        }

        float flicker = 0.94f + 0.06f * Mathf.Sin(age * 43f);
        if (shell != null) shell.localScale = Vector3.one * radius * 2f * flicker;
        if (core != null) core.localScale = Vector3.one * radius * 2f * 0.55f * flicker;
        if (shellMat != null) { Color c = ShellColor; c.a *= alphaScale; shellMat.color = c; }
        if (coreMat != null) { Color c = CoreColor; c.a *= alphaScale; coreMat.color = c; }
        if (glow != null) glow.intensity = 5f * alphaScale * flicker;

        // ---- damage ticks (only while the sphere is actually up) ----
        if (alphaScale > 0.25f && age >= nextTickAt)
        {
            nextTickAt = age + tickInterval;
            DamageTick(radius);
        }

        if (age >= total) Destroy(gameObject);
    }

    private void DamageTick(float radius)
    {
        // Mechs: caught = held in the get-hit state + damage + bar, every tick.
        MechHealth[] mechs = Object.FindObjectsByType<MechHealth>(FindObjectsSortMode.None);
        foreach (MechHealth mh in mechs)
        {
            if (mh == null || mh.currentHealth <= 0f) continue;
            if (owner != null && mh.transform.root == owner.root) continue; // own blast never cooks the caster
            Vector3 chest = mh.transform.position + Vector3.up * 1.1f;
            if (Vector3.Distance(chest, transform.position) > radius + 0.6f) continue;
            if (mh.isYellowLocked) continue; // already floored - EXVS down protection

            // Push slightly outward so a knockdown launches away from the blast center
            Vector3 dir = chest - transform.position; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.forward;

            // TEAM RULES: the giant laser's blast is the easiest thing in the game to
            // catch a partner in, so a friendly tick is a scratch rather than a hold.
            float dmg = blastDamageTick, bar = barPerTick, stun = staggerSeconds;
            if (!TeamRules.ResolveHit(owner, mh, ref dmg, ref bar, ref stun)) continue;
            mh.TakeDamage(dmg, bar, dir * 7f + Vector3.up * 5f);

            // Re-stagger every tick = constantly in the get-hit state while inside
            MechController pc = mh.GetComponent<MechController>();
            SimpleMechAI ai = mh.GetComponent<SimpleMechAI>();
            if (!mh.isYellowLocked) // the tick above may have just floored them
            {
                if (pc != null) pc.TakeHit(stun);
                else if (ai != null) ai.TakeHit(stun);
            }
        }

        // Buildings inside the blast crumble
        BreakableBuilding[] buildings = Object.FindObjectsByType<BreakableBuilding>(FindObjectsSortMode.None);
        foreach (BreakableBuilding bb in buildings)
        {
            if (bb == null) continue;
            float dist = Vector3.Distance(bb.transform.position, transform.position);
            if (dist <= radius + 2f)
                bb.TakeHit(buildingDamagePerTick, bb.transform.position + (transform.position - bb.transform.position).normalized);
        }
    }
}
