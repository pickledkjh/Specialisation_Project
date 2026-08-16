using System.Collections;
using UnityEngine;

/// <summary>
/// Destructible city building - the pitch's "interactable objects" USP.
/// Shots chip it, charge shots crack it, the gerobi laser demolishes it.
/// When it collapses it EXPLODES: both mechs inside the blast radius take damage
/// and knockdown bar - buildings are cover AND a weapon. Fight near them at your
/// own risk, or laser one while your enemy hugs it.
/// BattlefieldBuilder attaches this to every building inside the walls.
/// </summary>
public class BreakableBuilding : MonoBehaviour
{
    [Tooltip("Total damage before collapse. Rifle shot 15, charge shot 45, laser tick 30.")]
    public float health = 90f;
    [Header("Collapse blast")]
    public float splashRadius = 8f;
    public float splashDamage = 15f;
    public float splashBarPower = 30f;
    [Tooltip("Seconds the collapse animation takes (sink + crumble).")]
    public float collapseSeconds = 1.1f;

    private bool collapsing;
    private float maxHealth;

    private void Start() { maxHealth = health; }

    /// <summary>Called by projectiles and the laser. hitPoint is for impact VFX.</summary>
    public void TakeHit(float damage, Vector3 hitPoint)
    {
        if (collapsing) return;
        health -= damage;
        CombatVfx.SpawnHit(hitPoint);
        if (health <= 0f) StartCoroutine(Collapse());
    }

    private IEnumerator Collapse()
    {
        collapsing = true;

        Bounds b = new Bounds(transform.position, Vector3.one);
        Renderer[] rs = GetComponentsInChildren<Renderer>();
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        Vector3 center = b.center;

        // The blast: explosion VFX + area damage to EVERY mech in range.
        CombatVfx.SpawnExplosion(center);
        CombatVfx.SpawnExplosion(center + Vector3.up * (b.size.y * 0.35f));
        foreach (MechHealth mech in FindObjectsByType<MechHealth>(FindObjectsSortMode.None))
        {
            if (mech.isYellowLocked) continue;
            float d = Vector3.Distance(mech.transform.position, center);
            if (d <= splashRadius)
            {
                Vector3 away = (mech.transform.position - center);
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                mech.TakeDamage(splashDamage, splashBarPower, away * 7f + Vector3.up * 4f);
            }
        }

        // No more cover: colliders off immediately so shots fly through the rubble
        foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;

        // THE BUILDING BREAKS INTO PIECES: real physics chunks blasted outward,
        // tinted like the building, bouncing off the ground before crumbling away.
        Color tint = SampleTint(rs);
        SpawnDebris(b, tint);

        // The intact model vanishes the moment the chunks appear
        foreach (Renderer r in rs) if (r != null) r.enabled = false;

        yield return null;
    }

    private static Color SampleTint(Renderer[] rs)
    {
        foreach (Renderer r in rs)
        {
            if (r == null || r.sharedMaterial == null) continue;
            Material m = r.sharedMaterial;
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.color;
        }
        return new Color(0.75f, 0.55f, 0.45f); // brick-ish fallback
    }

    private void SpawnDebris(Bounds b, Color tint)
    {
        // 3x3 grid of chunks through the building volume, each a physical cube
        // with an outward blast impulse. DebrisChunk shrinks and removes them.
        int chunks = 0;
        for (int ix = 0; ix < 3; ix++)
        {
            for (int iy = 0; iy < 3; iy++)
            {
                for (int iz = 0; iz < 3; iz++)
                {
                    if ((ix + iy + iz) % 2 == 1 && chunks >= 10) continue; // cap the count
                    chunks++;

                    Vector3 pos = b.min + new Vector3(
                        b.size.x * (ix + 0.5f) / 3f,
                        b.size.y * (iy + 0.5f) / 3f,
                        b.size.z * (iz + 0.5f) / 3f);

                    GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    chunk.name = "Debris";
                    chunk.transform.position = pos;
                    chunk.transform.rotation = Random.rotation;
                    float s = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) / 3.2f, 0.5f, 2.6f);
                    chunk.transform.localScale = new Vector3(s, s, s) * Random.Range(0.6f, 1.15f);

                    Renderer cr = chunk.GetComponent<Renderer>();
                    Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                    if (lit != null)
                    {
                        Material cm = new Material(lit);
                        Color c = Color.Lerp(tint, new Color(0.5f, 0.47f, 0.44f), Random.Range(0.2f, 0.6f));
                        if (cm.HasProperty("_BaseColor")) cm.SetColor("_BaseColor", c);
                        cr.material = cm;
                    }

                    Rigidbody rb = chunk.AddComponent<Rigidbody>();
                    rb.mass = 3f;
                    Vector3 outward = (pos - b.center);
                    outward.y = Mathf.Abs(outward.y) * 0.4f;
                    outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Random.onUnitSphere;
                    rb.linearVelocity = outward * Random.Range(4f, 11f) + Vector3.up * Random.Range(2f, 7f);
                    rb.angularVelocity = Random.insideUnitSphere * 6f;

                    chunk.AddComponent<DebrisChunk>().life = Random.Range(2.8f, 4.2f);
                }
            }
        }
    }
}

/// <summary>Rubble piece: tumbles physically, then shrinks away and self-destructs
/// so the arena never fills with permanent physics junk.</summary>
public class DebrisChunk : MonoBehaviour
{
    public float life = 3.5f;
    private float t;
    private Vector3 baseScale;
    private bool captured;

    private void Update()
    {
        t += Time.deltaTime;
        if (!captured && t > life * 0.55f) { baseScale = transform.localScale; captured = true; }
        if (captured)
        {
            float k = Mathf.Clamp01((t - life * 0.55f) / (life * 0.45f));
            transform.localScale = baseScale * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }
    }
}
