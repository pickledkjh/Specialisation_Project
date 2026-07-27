using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Makes sure the battle scene is registered in Build Settings, so the rematch
/// scene-reload works in the editor and standalone builds include the scene.
/// </summary>
[InitializeOnLoad]
public static class BuildSceneSetup
{
    static BuildSceneSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorBuildSettings.scenes.Length > 0) return;
            const string path = "Assets/Scenes/SampleScene.unity";
            if (System.IO.File.Exists(path))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(path, true) };
                UnityEngine.Debug.Log("[GameFlow] Added " + path + " to Build Settings (needed for rematch reload and builds).");
            }
        };
    }
}
