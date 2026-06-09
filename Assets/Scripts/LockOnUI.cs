using UnityEngine;
using UnityEngine.UI; // Required for interacting with Canvas Images

public class LockOnUI : MonoBehaviour
{
    [Header("References")]
    public MechCombat playerCombat; // We use this to read your exact Red Lock distance!
    public Transform enemyTarget;
    public Image lockOnImage;

    [Header("Sprites")]
    public Sprite greenLockSprite;
    public Sprite redLockSprite;

    [Header("Settings")]
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0); // Lifts the UI so it hovers over the chest/head, not the feet

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        // If we don't have a target, hide the reticle
        if (enemyTarget == null || playerCombat == null || lockOnImage == null)
        {
            lockOnImage.enabled = false;
            return;
        }

        // 1. Calculate the 3D Distance (ignoring height)
        Vector3 toTarget = enemyTarget.position - playerCombat.transform.position;
        toTarget.y = 0;
        float distance = toTarget.magnitude;

        // 2. Swap the PNG based on the range set in your Combat script
        if (distance <= playerCombat.redLockRange)
        {
            lockOnImage.sprite = redLockSprite;
        }
        else
        {
            lockOnImage.sprite = greenLockSprite;
        }

        // 3. Project the 3D position onto your 2D screen
        Vector3 screenPosition = mainCamera.WorldToScreenPoint(enemyTarget.position + targetOffset);

        // Only display the lock-on if the enemy is in front of the camera (Z > 0)
        if (screenPosition.z > 0)
        {
            lockOnImage.enabled = true;
            lockOnImage.transform.position = screenPosition;
        }
        else
        {
            lockOnImage.enabled = false; // Hide it if they are behind the camera
        }
    }
}