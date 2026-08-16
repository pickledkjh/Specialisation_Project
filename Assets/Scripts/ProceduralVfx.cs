using UnityEngine;

/// <summary>
/// Code-built particle effects - zero asset dependencies. Generates one soft
/// radial-dot texture at startup and drives everything off runtime-configured
/// ParticleSystems, so every effect works even if the Gabriel Aguiar pack was
/// never imported (and layers extra juice on top when it was).
///
/// API (all static, fire-and-forget):
///   Sparks(pos, color, ...)        - impact spark burst
///   Fireball(pos, scale)           - explosion: fire + smoke + shock ring
///   ShockRing(pos, color, size)    - expanding ground ring
///   DustPuff(pos, ...)             - landing / knockdown dust
///   MuzzlePop(pos)                 - small flash pop at a gun muzzle
///   MakeJet(parent, localPos, rot) - continuous thruster flame (caller toggles)
/// </summary>
public static class ProceduralVfx
{
    private static Material sharedMat;

    private static Material Mat()
    {
        if (sharedMat != null) return sharedMat;

        // Soft radial dot so particles are round and glowy, not hard squares
        const int S = 64;
        Texture2D tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (1.2f - 0.2f * a); // soft center, feathered edge
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();

        sharedMat = new Material(Shader.Find("Sprites/Default"));
        sharedMat.mainTexture = tex;
        return sharedMat;
    }

    // ---------- one-shots ----------

    public static void Sparks(Vector3 pos, Color color, int count = 20, float speed = 8f,
                              float life = 0.45f, float size = 0.14f, float gravity = 0.6f)
    {
        ParticleSystem ps = NewSystem("FX Sparks", pos, life);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.4f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.35f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.5f);
        main.startColor = color;
        main.gravityModifier = gravity;
        ps.Emit(count);
    }

    public static void Fireball(Vector3 pos, float scale = 1f)
    {
        // Fire core: fast bloom, orange -> deep red
        ParticleSystem fire = NewSystem("FX Fire", pos, 0.55f);
        var fm = fire.main;
        fm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        fm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 5.5f * scale);
        fm.startSize = new ParticleSystem.MinMaxCurve(0.7f * scale, 1.6f * scale);
        fm.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.25f), new Color(1f, 0.35f, 0.1f));
        fm.gravityModifier = -0.15f; // fire climbs a little
        fire.Emit(Mathf.RoundToInt(16 * scale));

        // Smoke: slower, larger, rises after the fire
        ParticleSystem smoke = NewSystem("FX Smoke", pos + Vector3.up * 0.3f, 1.4f);
        var sm = smoke.main;
        sm.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
        sm.startSpeed = new ParticleSystem.MinMaxCurve(0.6f * scale, 2.2f * scale);
        sm.startSize = new ParticleSystem.MinMaxCurve(1.0f * scale, 2.2f * scale);
        sm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.25f, 0.23f, 0.22f, 0.6f), new Color(0.45f, 0.42f, 0.4f, 0.45f));
        sm.gravityModifier = -0.35f;
        smoke.Emit(Mathf.RoundToInt(10 * scale));

        // Hot sparks flying out + the ground shockwave
        Sparks(pos, new Color(1f, 0.8f, 0.35f), Mathf.RoundToInt(14 * scale), 11f * scale, 0.5f, 0.12f, 1.2f);
        ShockRing(pos, new Color(1f, 0.7f, 0.3f, 0.5f), 5.5f * scale);

        // Brief hot light
        FlashLight(pos + Vector3.up * 0.8f, new Color(1f, 0.6f, 0.25f), 4f * scale, 10f * scale, 0.25f);
    }

    public static void ShockRing(Vector3 pos, Color color, float finalSize = 5f, float life = 0.4f)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = "FX Ring";
        go.transform.position = pos + Vector3.up * 0.12f;
        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flat on the ground
        Renderer r = go.GetComponent<Renderer>();
        r.material = new Material(Shader.Find("Sprites/Default"));
        r.material.mainTexture = RingTexture();
        r.material.color = color;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        RingFx fx = go.AddComponent<RingFx>();
        fx.finalScale = finalSize;
        fx.life = life;
        fx.startColor = color;
    }

    public static void DustPuff(Vector3 pos, int count = 14, float scale = 1f)
    {
        ParticleSystem ps = NewSystem("FX Dust", pos + Vector3.up * 0.15f, 0.9f);
        var main = ps.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f * scale, 3.5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f * scale, 0.9f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.5f, 0.45f, 0.5f), new Color(0.7f, 0.66f, 0.6f, 0.4f));
        main.gravityModifier = 0.1f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere; // outward + up, hugging the ground
        shape.radius = 0.4f * scale;
        ps.Emit(count);
    }

    public static void MuzzlePop(Vector3 pos)
    {
        Sparks(pos, new Color(1f, 0.85f, 0.4f), 6, 4f, 0.18f, 0.1f, 0f);
        FlashLight(pos, new Color(1f, 0.8f, 0.35f), 2.2f, 4f, 0.08f);
    }

    /// <summary>Short-lived point light - the flash that sells an explosion.</summary>
    public static void FlashLight(Vector3 pos, Color color, float intensity, float range, float life)
    {
        GameObject go = new GameObject("FX Light");
        go.transform.position = pos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        LightFadeFx fade = go.AddComponent<LightFadeFx>();
        fade.life = life;
        fade.startIntensity = intensity;
    }

    // ---------- continuous (caller owns lifetime) ----------

    /// <summary>Thruster flame jet. Attach under a mech, aim with localRotation,
    /// toggle with the returned system's emission module.</summary>
    public static ParticleSystem MakeJet(Transform parent, Vector3 localPos, Quaternion localRot,
                                         Color color, float size = 0.2f)
    {
        GameObject go = new GameObject("FX Jet");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.25f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 7f);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.7f, size * 1.4f);
        main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.6f));
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = 0f; // off until the mech actually boosts
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 9f;
        shape.radius = 0.04f;
        FadeOverLifetime(ps);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = Mat();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    // ---------- internals ----------

    private static ParticleSystem NewSystem(string name, Vector3 pos, float maxLife)
    {
        GameObject go = new GameObject(name);
        go.transform.position = pos;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 256;
        var emission = ps.emission;
        emission.enabled = false; // Emit() only
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        FadeOverLifetime(ps);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.material = Mat();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Object.Destroy(go, maxLife + 0.6f);
        return ps;
    }

    private static void FadeOverLifetime(ParticleSystem ps)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.4f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    private static Texture2D ringTex;
    private static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int S = 128;
        ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(S / 2f, S / 2f)) / (S / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) * 7f); // thin bright ring at 80% radius
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        }
        ringTex.Apply();
        return ringTex;
    }
}

/// <summary>Expanding, fading shockwave ring (driven by ProceduralVfx.ShockRing).</summary>
public class RingFx : MonoBehaviour
{
    public float finalScale = 5f;
    public float life = 0.4f;
    public Color startColor = Color.white;
    private float t;
    private Renderer rend;

    private void Awake() { rend = GetComponent<Renderer>(); }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        float eased = 1f - (1f - k) * (1f - k); // fast start, soft end
        transform.localScale = Vector3.one * Mathf.Lerp(0.6f, finalScale, eased);
        if (rend != null)
        {
            Color c = startColor;
            c.a = startColor.a * (1f - k);
            rend.material.color = c;
        }
        if (k >= 1f) Destroy(gameObject);
    }
}

/// <summary>Fading flash light (driven by ProceduralVfx.FlashLight).</summary>
public class LightFadeFx : MonoBehaviour
{
    public float life = 0.2f;
    public float startIntensity = 3f;
    private float t;
    private Light l;

    private void Awake() { l = GetComponent<Light>(); }

    private void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / life);
        if (l != null) l.intensity = startIntensity * (1f - k) * (1f - k);
        if (k >= 1f) Destroy(gameObject);
    }
}
