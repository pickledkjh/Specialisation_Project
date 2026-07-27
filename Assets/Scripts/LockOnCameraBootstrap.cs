using UnityEngine;
using Unity.Cinemachine;

/// <summary>
/// Spawns the battle camera at play start so no scene setup is needed. Delete this
/// file (or set Enabled to false) to go back to the old camera. Place a
/// LockOnBattleCamera in the scene by hand and this steps aside.
/// </summary>
public static class LockOnCameraBootstrap
{
    private static readonly bool Enabled = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (!Enabled) return;

        // A hand-placed (or already spawned) battle camera wins — do nothing.
        if (Object.FindFirstObjectByType<LockOnBattleCamera>() != null) return;

        MechController playerController = Object.FindFirstObjectByType<MechController>();
        if (playerController == null) return; // not a gameplay scene

        GameObject go = new GameObject("Battle Camera");

        // Spawn where the current camera is so the first blend is gentle
        Camera main = Camera.main;
        if (main != null)
            go.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);

        CinemachineCamera vcam = go.AddComponent<CinemachineCamera>();
        vcam.Priority = 15;

        // Match the scene camera's field of view. A fresh CinemachineCamera defaults
        // to a narrow ~40° lens, which reads as zoomed-in and claustrophobic — the
        // EXVS/Starward look needs the wider view the scene camera already uses (~60°).
        LensSettings lens = vcam.Lens;
        lens.FieldOfView = main != null ? main.fieldOfView : 60f;
        vcam.Lens = lens;

        LockOnBattleCamera rig = go.AddComponent<LockOnBattleCamera>();
        rig.player = playerController.transform;
        rig.targetManager = playerController.GetComponent<TargetManager>();
    }
}
