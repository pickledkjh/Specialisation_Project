using UnityEngine;

/// <summary>
/// Persistent player settings, PlayerPrefs-backed. Written by the SETTINGS menu,
/// read live by BattleAudio (volumes), GameFlowManager (difficulty, round time)
/// and LockOnBattleCamera (screen shake). Static so every system reads the same
/// values with zero wiring.
/// </summary>
public static class GameSettings
{
    public static float MasterVolume = 0.5f;   // 0..1
    public static float MusicVolume = 0.55f;   // 0..1, relative to master
    public static int Difficulty = 1;          // 0 easy, 1 normal, 2 hard
    public static float MatchSeconds = 120f;   // round length
    public static bool ScreenShake = true;

    static GameSettings() { Load(); }

    public static void Load()
    {
        MasterVolume = PlayerPrefs.GetFloat("set_master", 0.5f);
        MusicVolume = PlayerPrefs.GetFloat("set_music", 0.55f);
        Difficulty = PlayerPrefs.GetInt("set_diff", 1);
        MatchSeconds = PlayerPrefs.GetFloat("set_time", 120f);
        ScreenShake = PlayerPrefs.GetInt("set_shake", 1) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("set_master", MasterVolume);
        PlayerPrefs.SetFloat("set_music", MusicVolume);
        PlayerPrefs.SetInt("set_diff", Difficulty);
        PlayerPrefs.SetFloat("set_time", MatchSeconds);
        PlayerPrefs.SetInt("set_shake", ScreenShake ? 1 : 0);
        PlayerPrefs.Save();
    }
}
