using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Complete battle SFX with ZERO audio assets - every clip is synthesized in code
/// at startup (a few ms). EXVS/Starward reads owe half their impact to sound;
/// the game was fully silent, which made every hit feel weightless.
///
/// Spawns itself at play start (no scene setup), survives rematch reloads.
/// Hooks: CombatVfx calls Play() for shots/hits/blocks/parries/explosions,
/// SaberBlade for ignition, AwakeningSystem for burst; this script itself polls
/// the boost state for the thruster loop and the downed/death edges.
/// Volume: tweak masterVolume on the "Battle Audio" object while playing.
/// </summary>
public class BattleAudio : MonoBehaviour
{
    public static BattleAudio Instance { get; private set; }

    [Range(0f, 1f)] public float masterVolume = 0.5f;
    [Range(0f, 1f)] public float bgmVolume = 0.55f; // relative to master; 0 = music off
    private AudioSource bgm;

    private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private AudioSource[] pool;
    private int poolIdx;
    private AudioSource boostLoop;

    // polled scene refs (re-found after every scene load)
    private MechController player;
    private BoostManager playerBoost;
    private MechHealth playerHealth, enemyHealth;
    private bool prevEnemyDown, prevPlayerDown, prevEnemyDead, prevPlayerDead;
    private float boostLoopTarget;

    // ---------- lifetime ----------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (Object.FindFirstObjectByType<BattleAudio>() != null) return;
        if (Object.FindFirstObjectByType<MechController>() == null) return; // not a gameplay scene
        new GameObject("Battle Audio").AddComponent<BattleAudio>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        BuildClips();

        // Small round-robin pool so overlapping one-shots each keep their own pitch
        pool = new AudioSource[6];
        for (int i = 0; i < pool.Length; i++)
        {
            pool[i] = gameObject.AddComponent<AudioSource>();
            pool[i].playOnAwake = false;
            pool[i].spatialBlend = 0f; // 2D - arena camera is far, 3D panning just made things quiet
        }

        boostLoop = gameObject.AddComponent<AudioSource>();
        boostLoop.playOnAwake = false;
        boostLoop.spatialBlend = 0f;
        boostLoop.loop = true;
        boostLoop.clip = clips["boostloop"];
        boostLoop.volume = 0f;
        boostLoop.Play();

        // Battle BGM - an 8-second synthesized loop (Am-F-G-Am, driving kick,
        // bass eighths, arpeggio). Fades in whenever the game is actually running
        // (timeScale > 0.5) and out in menus/pauses.
        bgm = gameObject.AddComponent<AudioSource>();
        bgm.playOnAwake = false;
        bgm.spatialBlend = 0f;
        bgm.loop = true;
        bgm.clip = BuildBgm();
        bgm.volume = 0f;
        bgm.Play();

        Rehook();
        EnsureListener();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene s, LoadSceneMode m) { Rehook(); EnsureListener(); }

    private void Rehook()
    {
        player = Object.FindFirstObjectByType<MechController>();
        playerBoost = player != null ? player.GetComponent<BoostManager>() : null;
        playerHealth = player != null ? player.GetComponent<MechHealth>() : null;
        SimpleMechAI enemy = Object.FindFirstObjectByType<SimpleMechAI>();
        enemyHealth = enemy != null ? enemy.GetComponent<MechHealth>() : null;
        prevEnemyDown = prevPlayerDown = prevEnemyDead = prevPlayerDead = false;
    }

    private void EnsureListener()
    {
        if (Object.FindFirstObjectByType<AudioListener>() != null) return;
        Camera cam = Camera.main;
        if (cam != null) cam.gameObject.AddComponent<AudioListener>();
        else gameObject.AddComponent<AudioListener>();
    }

    // ---------- public API ----------

    /// <summary>Fire-and-forget one-shot. Safe to call any time (no-ops without an instance).</summary>
    public static void Play(string key, float volume = 1f, float pitch = 1f)
    {
        BattleAudio a = Instance;
        if (a == null || a.pool == null) return;
        AudioClip clip;
        if (!a.clips.TryGetValue(key, out clip) || clip == null) return;
        AudioSource src = a.pool[a.poolIdx = (a.poolIdx + 1) % a.pool.Length];
        src.pitch = pitch * Random.Range(0.96f, 1.05f);
        src.PlayOneShot(clip, Mathf.Clamp01(volume * a.masterVolume));
    }

    // ---------- per-frame cues ----------

    private void Update()
    {
        // The SETTINGS menu owns the volumes
        masterVolume = GameSettings.MasterVolume;
        bgmVolume = GameSettings.MusicVolume;

        // BGM ducks out in menus (timeScale 0) and during the finish slow-mo
        if (bgm != null)
        {
            float target = Time.timeScale > 0.5f ? bgmVolume * masterVolume * 0.5f : 0f;
            bgm.volume = Mathf.MoveTowards(bgm.volume, target, Time.unscaledDeltaTime * 0.6f);
        }

        if (player == null) { Rehook(); if (player == null) return; }

        // Thruster loop: audible while dashing, quieter while rising
        bool dashing = player.currentState == MechState.BoostDash || player.currentState == MechState.BoostStep;
        bool rising = playerBoost != null && !playerBoost.isOverheated &&
                      player.currentState == MechState.Airborne && player.velocity.y > 0.5f;
        boostLoopTarget = dashing ? 0.4f : rising ? 0.22f : 0f;
        boostLoop.volume = Mathf.MoveTowards(boostLoop.volume, boostLoopTarget * masterVolume, Time.unscaledDeltaTime * 2.5f);

        // Downed / death edges (poll: works for every damage source with no hooks)
        bool eDown = enemyHealth != null && enemyHealth.isYellowLocked && enemyHealth.currentHealth > 0f;
        if (eDown && !prevEnemyDown) Play("down", 0.9f);
        prevEnemyDown = eDown;

        bool pDown = playerHealth != null && playerHealth.isYellowLocked && playerHealth.currentHealth > 0f;
        if (pDown && !prevPlayerDown) Play("down", 0.9f, 0.85f);
        prevPlayerDown = pDown;

        bool eDead = enemyHealth != null && enemyHealth.currentHealth <= 0f;
        if (eDead && !prevEnemyDead) { Play("explosion", 1f, 0.8f); Play("explosion", 0.8f, 1.2f); }
        prevEnemyDead = eDead;

        bool pDead = playerHealth != null && playerHealth.currentHealth <= 0f;
        if (pDead && !prevPlayerDead) { Play("explosion", 1f, 0.8f); }
        prevPlayerDead = pDead;
    }

    // ---------- synthesis ----------

    private const int SampleRate = 22050;

    private void BuildClips()
    {
        clips["shot"] = Synth("shot", 0.20f, (t, d) =>
        {
            float k = t / d;
            float freq = Mathf.Lerp(1600f, 260f, Mathf.Pow(k, 0.55f)); // pew sweep
            float env = Mathf.Pow(1f - k, 2.2f);
            float w = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freq * t);
            return w * env * 0.55f;
        });

        clips["hit"] = Synth("hit", 0.16f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Pow(1f - k, 3f);
            float thud = Mathf.Sin(2f * Mathf.PI * 210f * t) * 0.8f;
            float clank = Mathf.Sin(2f * Mathf.PI * 1730f * t) * 0.35f * Mathf.Pow(1f - k, 6f);
            return (thud + clank + NoiseAt(t, 1) * 0.5f) * env * 0.8f;
        });

        clips["block"] = Synth("block", 0.30f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Pow(1f - k, 2f);
            return (Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.6f +
                    Mathf.Sin(2f * Mathf.PI * 1320f * t) * 0.4f * Mathf.Pow(1f - k, 4f)) * env * 0.7f;
        });

        clips["parry"] = Synth("parry", 0.40f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Pow(1f - k, 1.8f);
            float zap = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(2300f, 1150f, k) * t);
            return (zap * 0.45f + NoiseAt(t, 2) * 0.45f * Mathf.PingPong(t * 90f, 1f)) * env * 0.75f;
        });

        clips["explosion"] = Synth("explosion", 0.85f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Pow(1f - k, 1.7f);
            float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.6f + Mathf.Sin(2f * Mathf.PI * 38f * t) * 0.5f;
            return (SmoothNoiseAt(t, 3) * 0.9f + rumble) * env * 0.9f;
        });

        clips["saber"] = Synth("saber", 0.38f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Sin(Mathf.PI * Mathf.Min(1f, k * 1.25f)); // swell in-out
            float hum = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(85f, 175f, k) * t);
            float buzz = Mathf.Sin(2f * Mathf.PI * 47f * t) * Mathf.Sin(2f * Mathf.PI * 620f * t);
            return (hum * 0.6f + buzz * 0.3f) * env * 0.7f;
        });

        clips["down"] = Synth("down", 0.5f, (t, d) =>
        {
            float k = t / d;
            float freq = k < 0.45f ? 620f : 390f; // two falling tones
            float env = Mathf.Pow(1f - k, 1.2f) * (Mathf.Abs(((k * 4f) % 1f) - 0.5f) < 0.42f ? 1f : 0.2f);
            return Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.55f;
        });

        clips["burst"] = Synth("burst", 0.85f, (t, d) =>
        {
            float k = t / d;
            float env = Mathf.Sin(Mathf.PI * k);
            float rise = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(180f, 880f, Mathf.Pow(k, 1.4f)) * t);
            float shimmer = Mathf.Sin(2f * Mathf.PI * 2200f * t) * 0.25f * Mathf.Pow(k, 2f);
            return (rise * 0.6f + shimmer) * env * 0.8f;
        });

        clips["alert"] = Synth("alert", 0.22f, (t, d) =>
        {
            float k = t / d;
            float on = ((k * 2f) % 1f) < 0.6f ? 1f : 0f; // beep-beep
            return Mathf.Sin(2f * Mathf.PI * 1180f * t) * on * (1f - k * 0.4f) * 0.5f;
        });

        // Seamless-ish thruster loop: fixed-frequency components only, all with an
        // integer number of cycles over the loop, so there is no click at the seam.
        clips["boostloop"] = Synth("boostloop", 0.6f, (t, d) =>
        {
            float w = 0f;
            w += Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.35f;   // 36 cycles
            w += Mathf.Sin(2f * Mathf.PI * 95f * t) * 0.28f;   // 57 cycles
            w += Mathf.Sin(2f * Mathf.PI * 150f * t) * 0.20f;  // 90 cycles
            w += Mathf.Sin(2f * Mathf.PI * 235f * t) * 0.12f;  // 141 cycles
            // pseudo-noise from incommensurate sine products (also loop-safe: both integer cycle counts)
            w += Mathf.Sin(2f * Mathf.PI * 730f * t) * Mathf.Sin(2f * Mathf.PI * 415f * t) * 0.22f;
            return w * 0.8f;
        });
    }

    // 8-second battle loop: Am - F - G - Am, 120 BPM. Kick on every beat, hats on
    // the offbeats, bass eighths on the chord root, a 16th-note arpeggio, and a
    // soft pad. Every note has its own attack/decay, and bar 1 starts on a kick,
    // so the loop seam is masked. Composed in code - zero assets, like the SFX.
    private static AudioClip BuildBgm()
    {
        const float dur = 8f;
        int n = Mathf.CeilToInt(dur * SampleRate);
        float[] data = new float[n];

        float[] bassRoots = { 110f, 87.31f, 98f, 110f }; // A2 F2 G2 A2
        float[][] chords =
        {
            new[] { 220f, 261.63f, 329.63f },   // Am
            new[] { 174.61f, 220f, 261.63f },   // F
            new[] { 196f, 246.94f, 293.66f },   // G
            new[] { 220f, 329.63f, 440f },      // Am (spread)
        };

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            int bar = (int)(t / 2f) % 4;
            float w = 0f;

            // kick: every beat (0.5s), punchy decaying sine
            float beatT = t % 0.5f;
            w += Mathf.Sin(2f * Mathf.PI * 55f * beatT) * Mathf.Exp(-beatT * 28f) * 0.85f;

            // hats: offbeats, tiny noise ticks
            float hatT = (t + 0.25f) % 0.5f;
            w += NoiseAt(t, 9) * Mathf.Exp(-hatT * 70f) * 0.14f;

            // bass: driving eighth notes on the bar's root
            float eighthT = t % 0.25f;
            w += Mathf.Sin(2f * Mathf.PI * bassRoots[bar] * t) * Mathf.Exp(-eighthT * 5f) * 0.30f;

            // arpeggio: 16th notes cycling the chord tones, one octave up
            int step = (int)(t / 0.125f);
            float noteT = t % 0.125f;
            float freq = chords[bar][step % 3] * 2f;
            w += Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-noteT * 12f) * 0.11f;

            // pad: soft sustained chord underneath
            float[] ch = chords[bar];
            for (int c = 0; c < ch.Length; c++)
                w += Mathf.Sin(2f * Mathf.PI * ch[c] * t) * 0.032f;

            data[i] = Mathf.Clamp(w * 0.8f, -1f, 1f);
        }

        AudioClip clip = AudioClip.Create("bgm", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip Synth(string name, float seconds, System.Func<float, float, float> gen)
    {
        int n = Mathf.CeilToInt(seconds * SampleRate);
        float[] data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / SampleRate;
            data[i] = Mathf.Clamp(gen(t, seconds), -1f, 1f);
        }
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Deterministic per-sample noise (hash of the sample index) - no allocation
    private static float NoiseAt(float t, int seed)
    {
        int i = (int)(t * SampleRate) * 374761393 + seed * 668265263;
        i = (i ^ (i >> 13)) * 1274126177;
        return (((i ^ (i >> 16)) & 0xFFFF) / 32768f) - 1f;
    }

    // Noise with a crude low-pass (average of neighbours) for boomier textures
    private static float SmoothNoiseAt(float t, int seed)
    {
        float dt = 1f / SampleRate;
        return (NoiseAt(t, seed) + NoiseAt(t - dt, seed) + NoiseAt(t - 2f * dt, seed) +
                NoiseAt(t - 3f * dt, seed)) * 0.25f;
    }
}
