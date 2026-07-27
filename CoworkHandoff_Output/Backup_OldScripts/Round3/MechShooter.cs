using UnityEngine;

[DefaultExecutionOrder(100)]
public class MechShooter : MonoBehaviour
{
    [Header("Aiming Setup")]
    public Transform spineBone;
    public Transform armBone;

    [Header("Bone Offsets (Adjust if twisted)")]
    public Vector3 spineAngleOffset;
    public Vector3 armAngleOffset;

    [Header("Aiming Weights")]
    [Range(0f, 1f)] public float spineWeight = 0.4f;
    [Range(0f, 1f)] public float armWeight = 1.0f;

    [Header("Shooting Settings")]
    public float shootDuration = 0.5f;
    private float shootTimer = 0f;

    private TargetManager targetManager;
    private MechController mechController;

    private void Start()
    {
        targetManager = GetComponent<TargetManager>();
        mechController = GetComponent<MechController>();
    }

    private void LateUpdate()
    {
        bool isShooting = shootTimer > 0f;

        if (isShooting)
        {
            Transform target = GetTarget();
            if (target != null)
            {
                // Aim slightly up so they shoot at the chest/head, not the feet
                Vector3 targetPos = target.position + (Vector3.up * 1.5f);

                // 1. Rotate the Upper Body (Spine) FIRST
                if (spineBone != null)
                {
                    Vector3 spineDirection = targetPos - spineBone.position;
                    if (spineDirection != Vector3.zero)
                    {
                        Quaternion spineLookRot = Quaternion.LookRotation(spineDirection) * Quaternion.Euler(spineAngleOffset);
                        // Slerp blends the animation with the procedural aim so it doesn't snap unnaturally
                        spineBone.rotation = Quaternion.Slerp(spineBone.rotation, spineLookRot, spineWeight);
                    }
                }

                // 2. Rotate the Arm SECOND (To perfectly align the weapon)
                if (armBone != null)
                {
                    Vector3 armDirection = targetPos - armBone.position;
                    if (armDirection != Vector3.zero)
                    {
                        Quaternion armLookRot = Quaternion.LookRotation(armDirection) * Quaternion.Euler(armAngleOffset);
                        armBone.rotation = Quaternion.Slerp(armBone.rotation, armLookRot, armWeight);
                    }
                }
            }
            shootTimer -= Time.deltaTime;
        }
    }

    public void FireWeapon()
    {
        shootTimer = shootDuration;
    }

    private Transform GetTarget()
    {
        if (targetManager != null && targetManager.currentTarget != null)
            return targetManager.currentTarget;
        if (mechController != null && mechController.enemyTarget != null)
            return mechController.enemyTarget;
        return null;
    }
}