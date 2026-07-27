using UnityEngine;
using UnityEngine.UI; // Needed for UI Images

public class TargetManager : MonoBehaviour
{
    public Transform currentTarget;
    public Image lockOnReticle; // Drag your crosshair UI image here

    [Header("Reticle Colors")]
    public Color normalLockColor = Color.red;
    public Color yellowLockColor = Color.yellow;

    private MechCombat playerCombat;

    private void Start()
    {
        playerCombat = FindFirstObjectByType<MechCombat>();
    }

    private void Update()
    {
        if (currentTarget != null && lockOnReticle != null)
        {
            // Position the reticle over the enemy
            lockOnReticle.transform.position = Camera.main.WorldToScreenPoint(currentTarget.position);

            // Change color based on Yellow Lock state
            if (playerCombat != null && playerCombat.IsTargetYellowLocked(currentTarget))
            {
                lockOnReticle.color = yellowLockColor;
            }
            else
            {
                lockOnReticle.color = normalLockColor;
            }
        }
    }
}