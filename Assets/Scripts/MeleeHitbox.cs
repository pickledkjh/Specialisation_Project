using UnityEngine;

public class MeleeHitbox : MonoBehaviour
{
    [Header("Who does this hitbox hurt?")]
    public string targetTag = "Enemy"; // "Enemy" for player fists, "Player" for AI fists

    [Header("Script References (Assign ONE)")]
    public MechCombat playerCombatScript;
    public SimpleMechAI aiCombatScript;

    public float hitStopDuration = 0.1f;

    private void OnTriggerEnter(Collider other)
    {
        // Check if we hit the object with the correct tag
        if (other.CompareTag(targetTag))
        {
            if (targetTag == "Player")
            {
                // The collider and the script are now on the same root object!
                MechController playerTarget = other.GetComponent<MechController>();
                if (playerTarget != null)
                {
                    if (aiCombatScript != null) aiCombatScript.StartHitStop(hitStopDuration);
                    playerTarget.TakeHit(0.8f);
                    Debug.Log("AI smacked the Player!");
                }
            }
            else if (targetTag == "Enemy")
            {
                // The collider and the script are now on the same root object!
                SimpleMechAI aiTarget = other.GetComponent<SimpleMechAI>();
                if (aiTarget != null)
                {
                    if (playerCombatScript != null) playerCombatScript.StartHitStop(hitStopDuration);
                    aiTarget.TakeHit(0.8f);
                    Debug.Log("Player smacked the AI!");
                }
            }
        }
    }
}