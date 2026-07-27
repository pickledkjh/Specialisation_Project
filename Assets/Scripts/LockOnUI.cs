using UnityEngine;
using UnityEngine.UI;

public class LockOnUI : MonoBehaviour
{
    [Header("References")]
    public MechCombat playerCombat;
    public Transform enemyTarget;
    public Image lockOnImage;

    [Header("Sprites")]
    public Sprite greenLockSprite;
    public Sprite redLockSprite;
    public Sprite yellowLockSprite;

    [Header("Settings")]
    public Vector3 targetOffset = new Vector3(0, 1.5f, 0);

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (enemyTarget == null || playerCombat == null || lockOnImage == null)
        {
            lockOnImage.enabled = false;
            return;
        }

        Vector3 toTarget = enemyTarget.position - playerCombat.transform.position;
        toTarget.y = 0;
        float distance = toTarget.magnitude;

        // FIX: Search thoroughly for the health script on the exact target, its parents, or its children
        MechHealth targetHealth = enemyTarget.GetComponent<MechHealth>();
        if (targetHealth == null) targetHealth = enemyTarget.GetComponentInParent<MechHealth>();
        if (targetHealth == null) targetHealth = enemyTarget.GetComponentInChildren<MechHealth>();

        if (targetHealth != null && targetHealth.isYellowLocked)
        {
            lockOnImage.sprite = yellowLockSprite;
        }
        else if (distance <= playerCombat.redLockRange)
        {
            lockOnImage.sprite = redLockSprite;
        }
        else
        {
            lockOnImage.sprite = greenLockSprite;
        }

        Vector3 screenPosition = mainCamera.WorldToScreenPoint(enemyTarget.position + targetOffset);

        if (screenPosition.z > 0)
        {
            lockOnImage.enabled = true;
            lockOnImage.transform.position = screenPosition;
        }
        else
        {
            lockOnImage.enabled = false;
        }
    }
}