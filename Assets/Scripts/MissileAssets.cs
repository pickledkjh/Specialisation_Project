using UnityEngine;

/// <summary>
/// Runtime access to the imported Homing Missile pack (model, exhaust smoke,
/// explosion). The pack lives under "Assets/homing missile", which runtime code
/// cannot reach, so Tools > Gundam > 10. Hook Up Missile Pack copies the three
/// prefabs into Assets/Resources/MissilePack and this loads them by name.
///
/// Everything degrades gracefully: if the pack is missing, every accessor returns
/// null and the callers fall back to the procedural VFX that shipped before, so a
/// missing folder can never break a build.
/// </summary>
public static class MissileAssets
{
    public const string ResourceDir = "MissilePack/";

    private static bool loaded;
    private static GameObject missileModel, smokeTrail, explosion;

    // ---------------- AUDIO ----------------
    // The pack's prefabs ship with their own AudioSources, authored to be heard on
    // their own in a demo scene: full volume, usually 2D (so distance does nothing),
    // and completely outside this game's master volume slider. Instantiating them
    // whole meant a six-missile salvo detonating together played six of those on top
    // of each other - which is exactly what "the missile sound is too loud" is.
    //
    // Everything the pack spawns now goes through TameAudio.

    [Tooltip("Pack explosion volume, before the master slider.")]
    public static float ExplosionVolume = 0.40f;
    [Tooltip("Pack thruster / launch loop volume, before the master slider. Lower, " +
             "because there is one per missile in the air.")]
    public static float ThrusterVolume = 0.18f;
    [Tooltip("Distance at which pack sounds fall silent.")]
    public static float MaxAudioDistance = 90f;

    // Salvo limiter: N explosions inside this window are NOT N times as loud.
    private const float SalvoWindow = 0.18f;
    private static readonly float[] recentExplosions = new float[8];
    private static int recentIdx;

    private static float SalvoDuck()
    {
        int recent = 0;
        for (int i = 0; i < recentExplosions.Length; i++)
            if (Time.unscaledTime - recentExplosions[i] < SalvoWindow) recent++;

        recentExplosions[recentIdx = (recentIdx + 1) % recentExplosions.Length] = Time.unscaledTime;

        // 1st blast full, then each simultaneous one adds much less. Without this a
        // barrage is literally six times the amplitude of a single hit.
        if (recent <= 0) return 1f;
        if (recent == 1) return 0.55f;
        if (recent == 2) return 0.38f;
        return 0.25f;
    }

    /// <summary>
    /// Bring an instantiated pack object's audio into this game's mix: scaled to the
    /// master volume slider, made properly 3D so distance matters, and rolled off so
    /// a detonation across the arena is not the same volume as one at your feet.
    /// </summary>
    public static void TameAudio(GameObject go, float volumeScale)
    {
        if (go == null) return;
        float master = Mathf.Clamp01(GameSettings.MasterVolume);
        foreach (AudioSource src in go.GetComponentsInChildren<AudioSource>(true))
        {
            if (src == null) continue;
            src.volume = Mathf.Clamp01(src.volume * volumeScale * master);
            src.spatialBlend = 1f;                       // 3D: the pack usually ships these as 2D
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 6f;
            src.maxDistance = MaxAudioDistance;
            src.dopplerLevel = 0f;                       // no pitch smear on fast missiles
            src.priority = 200;                          // yields to combat cues under voice pressure
        }
    }

    /// <summary>Drop the cache so the next access re-reads Resources. Called after the
    /// editor copies the pack in, so the first play session after install works.</summary>
    public static void Reload() { loaded = false; }

    private static void EnsureLoaded()
    {
        if (loaded) return;
        loaded = true;
        missileModel = Resources.Load<GameObject>(ResourceDir + "Missil_05");
        smokeTrail = Resources.Load<GameObject>(ResourceDir + "rocket_smoke");
        explosion = Resources.Load<GameObject>(ResourceDir + "rocket_destroy_effect");
        Debug.Log("[Missile] pack " + (missileModel != null ? "FOUND" : "not found") +
                  " (model=" + (missileModel != null) +
                  " smoke=" + (smokeTrail != null) +
                  " explosion=" + (explosion != null) + ")" +
                  (missileModel == null ? " - run Tools > Gundam > 10. Hook Up Missile Pack" : ""));
    }

    public static GameObject MissileModel { get { EnsureLoaded(); return missileModel; } }
    public static GameObject SmokeTrail { get { EnsureLoaded(); return smokeTrail; } }
    public static GameObject Explosion { get { EnsureLoaded(); return explosion; } }

    /// <summary>Dresses a projectile as a real missile: the pack's model nose-forward
    /// plus its exhaust smoke. Returns false if the pack is not installed, so the
    /// caller can keep its procedural look.</summary>
    public static bool DressAsMissile(GameObject projectile, float scale = 1f)
    {
        EnsureLoaded();
        if (projectile == null || missileModel == null) return false;

        GameObject body = Object.Instantiate(missileModel, projectile.transform);
        body.name = "Missile Body";
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        body.transform.localScale = Vector3.one * scale;
        // The pack's colliders would fight the projectile's own trigger sphere
        foreach (Collider c in body.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        foreach (Rigidbody rb in body.GetComponentsInChildren<Rigidbody>(true)) Object.Destroy(rb);

        TameAudio(body, ThrusterVolume);

        if (smokeTrail != null)
        {
            GameObject smoke = Object.Instantiate(smokeTrail, projectile.transform);
            smoke.name = "Exhaust";
            smoke.transform.localPosition = new Vector3(0f, 0f, -0.35f * scale); // out the back
            smoke.transform.localRotation = Quaternion.identity;
            TameAudio(smoke, ThrusterVolume);
        }
        return true;
    }

    /// <summary>The pack's explosion at a world point. Falls back to the procedural
    /// fireball when the pack is absent. Self-destructs after a few seconds.</summary>
    public static void SpawnExplosion(Vector3 pos, float scale = 1f)
    {
        EnsureLoaded();
        if (explosion == null)
        {
            ProceduralVfx.Fireball(pos, 1.6f * scale);
            return;
        }
        GameObject fx = Object.Instantiate(explosion, pos, Quaternion.identity);
        fx.transform.localScale = Vector3.one * scale;
        foreach (Collider c in fx.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        TameAudio(fx, ExplosionVolume * SalvoDuck());
        Object.Destroy(fx, 5f);
    }
}
