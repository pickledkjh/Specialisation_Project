using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds and tears down the 2v2 arrangement at runtime.
///
/// There is no second enemy prefab and no ally prefab in the project - and there
/// should not be, because the enemy in the scene is the one mech that has been
/// hand-tuned (model, rig, hitboxes, colliders, Animator). So both extra units
/// are Instantiate() clones of it. The ally is exactly the same machine with its
/// team flipped, which is also the honest thing for a 2v2: your partner is not a
/// weaker escort, it is another unit of the same class.
///
/// Everything this creates is destroyed by Teardown(), so leaving the mode does
/// not leave three mechs standing in the menu.
/// </summary>
public class TeamBattleSetup : MonoBehaviour
{
    public static TeamBattleSetup Instance { get; private set; }

    public MechHealth PlayerUnit { get; private set; }
    public MechHealth AllyUnit { get; private set; }
    public MechHealth EnemyA { get; private set; }
    public MechHealth EnemyB { get; private set; }
    public bool Active { get; private set; }

    public static readonly Color AllyColor = new Color(0.35f, 0.75f, 1f);
    public static readonly Color HostileColor = new Color(1f, 0.35f, 0.3f);

    private readonly List<GameObject> spawned = new List<GameObject>();

    private void Awake() { Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    public static TeamBattleSetup Ensure()
    {
        if (Instance != null) return Instance;
        return new GameObject("Team Battle").AddComponent<TeamBattleSetup>();
    }

    /// <summary>
    /// Spawns the ally and the second hostile, assigns teams, and names everything.
    /// Safe to call twice - it tears the previous set down first.
    /// </summary>
    public void Build(MechHealth player, MechHealth enemyTemplate)
    {
        Teardown();
        if (player == null || enemyTemplate == null)
        {
            Debug.LogWarning("[2v2] Cannot build a team battle: player=" + player + " enemy=" + enemyTemplate);
            return;
        }

        PlayerUnit = player;
        EnemyA = enemyTemplate;

        Vector3 p = player.transform.position;
        Vector3 e = enemyTemplate.transform.position;
        Vector3 axis = e - p; axis.y = 0f;
        axis = axis.sqrMagnitude > 0.01f ? axis.normalized : Vector3.forward;
        Vector3 side = Vector3.Cross(Vector3.up, axis);

        // Partner on your left shoulder, second hostile on their right flank - so the
        // opening frame reads as two lines facing each other, not a scrum.
        AllyUnit = CloneUnit(enemyTemplate, Ground(p - side * 10f), Quaternion.LookRotation(axis), "ALLY UNIT");
        EnemyB = CloneUnit(enemyTemplate, Ground(e + side * 12f), Quaternion.LookRotation(-axis), "HOSTILE B");

        // Teams. The scene had BOTH mechs on Team2, which is also why the cost
        // readout never made sense - this fixes it for the mode outright.
        player.team = Team.Team1;
        if (AllyUnit != null) AllyUnit.team = Team.Team1;
        EnemyA.team = Team.Team2;
        if (EnemyB != null) EnemyB.team = Team.Team2;

        EnemyA.name = "HOSTILE A";

        MarkUnit(AllyUnit, AllyColor);
        MarkUnit(EnemyA, HostileColor);
        MarkUnit(EnemyB, HostileColor);

        TeamRules.TeamModeActive = true;
        BattleRoster.Invalidate();
        Active = true;

        Debug.Log("[2v2] Team battle built - Team1: " + player.name + " + " + NameOf(AllyUnit) +
                  "   Team2: " + NameOf(EnemyA) + " + " + NameOf(EnemyB));
    }

    private static string NameOf(MechHealth m) { return m != null ? m.name : "(none)"; }

    private MechHealth CloneUnit(MechHealth template, Vector3 pos, Quaternion rot, string newName)
    {
        GameObject go = Instantiate(template.gameObject, pos, rot);
        go.name = newName;
        spawned.Add(go);

        // Instantiate copies the template's team ring too, and Destroy() is deferred
        // to the end of the frame - so a rebuild in the same frame would clone a ring
        // of the WRONG colour onto the ally. Detach it first so nothing can find it.
        StripRing(go.transform);

        MechHealth h = go.GetComponent<MechHealth>();
        if (h != null)
        {
            h.currentHealth = h.maxHealth;
            h.currentKnockdownValue = 0f;
        }

        // A CharacterController caches its own position - Instantiate places the
        // transform, but the controller can snap the body back to the template's
        // spot on its first Move(). Cycling it re-seats the internal position.
        CharacterController cc = go.GetComponent<CharacterController>();
        if (cc != null) { cc.enabled = false; go.transform.SetPositionAndRotation(pos, rot); cc.enabled = true; }

        SimpleMechAI ai = go.GetComponent<SimpleMechAI>();
        if (ai != null)
        {
            ai.isDead = false;
            ai.currentHealth = ai.maxHealth;
            ai.passiveMode = false;
            ai.RestockForBattle();
        }
        return h;
    }

    private static Vector3 Ground(Vector3 around)
    {
        RaycastHit[] hits = Physics.RaycastAll(around + Vector3.up * 60f, Vector3.down, 200f);
        float best = float.NegativeInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && hits[i].collider.GetComponentInParent<MechHealth>() != null) continue;
            if (hits[i].point.y > best) best = hits[i].point.y;
        }
        return new Vector3(around.x, best > float.NegativeInfinity ? best + 0.1f : around.y, around.z);
    }

    // ---- team markers: a coloured ring on the deck under each unit ----

    private static void StripRing(Transform unit)
    {
        if (unit == null) return;
        Transform old = unit.Find("Team Ring");
        if (old == null) return;
        old.name = "Old Ring";
        old.SetParent(null);
        Destroy(old.gameObject);
    }

    private void MarkUnit(MechHealth unit, Color color)
    {
        if (unit == null) return;
        StripRing(unit.transform);

        GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Collider col = ring.GetComponent<Collider>();
        if (col != null) Destroy(col); // decoration only - never a hurtbox
        ring.name = "Team Ring";
        ring.transform.SetParent(unit.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        ring.transform.localScale = Vector3.one * 4.2f;

        Renderer r = ring.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            Material mat = new Material(sh);
            mat.mainTexture = RingTexture();
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", RingTexture());
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            // transparent surface so only the ring itself draws
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            r.material = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
        spawned.Add(ring);
    }

    private static Texture2D ringTex;
    private static Texture2D RingTexture()
    {
        if (ringTex != null) return ringTex;
        const int S = 128;
        ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        Vector2 c = new Vector2(S * 0.5f, S * 0.5f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (S * 0.5f);
                // a bright annulus between 0.72 and 0.95 of the radius, soft on both edges
                float a = Mathf.Clamp01(Mathf.InverseLerp(0.70f, 0.80f, d)) *
                          Mathf.Clamp01(Mathf.InverseLerp(0.98f, 0.88f, d));
                ringTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        ringTex.Apply();
        ringTex.wrapMode = TextureWrapMode.Clamp;
        return ringTex;
    }

    // ---- results ----

    public bool TeamWiped(Team team) { return BattleRoster.LivingCount(team) <= 0; }
    public float TeamFraction(Team team) { return BattleRoster.TeamHealthFraction(team); }

    public void ResetUnits()
    {
        ReviveUnit(PlayerUnit);
        ReviveUnit(AllyUnit);
        ReviveUnit(EnemyA);
        ReviveUnit(EnemyB);
        BattleRoster.Invalidate();
    }

    private static void ReviveUnit(MechHealth m)
    {
        if (m == null) return;
        m.Revive();
        SimpleMechAI ai = m.GetComponent<SimpleMechAI>();
        if (ai != null)
        {
            ai.isDead = false;
            ai.currentHealth = ai.maxHealth;
            ai.passiveMode = false;
            ai.RestockForBattle();
        }
        BoostManager boost = m.GetComponent<BoostManager>();
        if (boost != null) boost.currentBoost = boost.maxBoost;
        MechShooter shooter = m.GetComponent<MechShooter>();
        if (shooter != null) shooter.currentAmmo = shooter.maxAmmo;
    }

    public void Teardown()
    {
        for (int i = 0; i < spawned.Count; i++)
            if (spawned[i] != null) Destroy(spawned[i]);
        spawned.Clear();

        if (EnemyA != null) StripRing(EnemyA.transform);
        if (PlayerUnit != null) StripRing(PlayerUnit.transform);

        AllyUnit = null;
        EnemyB = null;
        Active = false;
        TeamRules.TeamModeActive = false;
        BattleRoster.Invalidate();
    }
}
