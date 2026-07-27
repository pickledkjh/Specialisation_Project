using UnityEngine;

public class CameraPivot : MonoBehaviour
{
    [Header("Targets")]
    public Transform player;
    public Transform enemy;

    [Header("Camera Smoothing & Deadzone")]
    public float rotationSpeed = 20f;      // How fast the camera tracks the enemy
    public float distanceDeadzone = 2.5f;  // Stops rotating if closer than this distance

    // We use LateUpdate so the camera moves strictly AFTER the player has finished moving this frame
    private void LateUpdate()
    {
        if (player == null || enemy == null) return;

        // 1. Snap exactly to the player's location
        transform.position = player.position;

        // 2. Calculate horizontal direction to the enemy
        Vector3 directionToEnemy = enemy.position - transform.position;
        directionToEnemy.y = 0;

        float distance = directionToEnemy.magnitude;

        // 3. Only update the camera's rotation if we are OUTSIDE the deadzone
        if (distance > distanceDeadzone && distance > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToEnemy.normalized);

            // Smoothly rotate the camera instead of instantly snapping it
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSpeed);
        }
    }
}