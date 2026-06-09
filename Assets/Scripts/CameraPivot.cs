using UnityEngine;

public class CameraPivot : MonoBehaviour
{
    [Header("Targets")]
    public Transform player;
    public Transform enemy;

    // We use LateUpdate so the camera moves strictly AFTER the player has finished moving this frame
    private void LateUpdate()
    {
        if (player == null || enemy == null) return;

        // 1. Snap exactly to the player's location
        transform.position = player.position;

        // 2. Look exactly at the enemy
        Vector3 directionToEnemy = enemy.position - transform.position;

        // Keep the rotation perfectly horizontal so the camera doesn't tilt into the floor
        directionToEnemy.y = 0;

        if (directionToEnemy != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(directionToEnemy);
        }
    }
}