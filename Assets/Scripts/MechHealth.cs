using UnityEngine;
using UnityEngine.InputSystem; // NEW: Required for New Input System

public class MechHealth : MonoBehaviour
{
    [Header("Unit Data")]
    public Team myTeam;               // Assign Team1 for player, Team2 for enemy in Inspector
    public int unitCost = 3000;       // The cost deducted when this mech dies

    [Header("Health Stats")]
    public float maxHealth = 500f;
    public float currentHealth;

    private bool isDead = false;

    private void Start()
    {
        currentHealth = maxHealth;
    }

    // Call this method when a projectile hits the mech
    public void TakeDamage(float damageAmount)
    {
        if (isDead) return; // Prevent taking damage while already dead

        currentHealth -= damageAmount;
        Debug.Log($"{gameObject.name} took {damageAmount} damage! HP: {currentHealth}");

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        isDead = true;
        Debug.Log($"{gameObject.name} was DESTROYED!");

        // Tell the global CostManager to subtract this unit's cost from its team's pool
        if (CostManager.Instance != null)
        {
            CostManager.Instance.DeductCost(myTeam, unitCost);
        }

        // Simulate a respawn for testing purposes
        StartCoroutine(RespawnRoutine());
    }

    private System.Collections.IEnumerator RespawnRoutine()
    {
        // Hide the mech visually
        GetComponent<CharacterController>().enabled = false;

        yield return new WaitForSeconds(3f); // Wait 3 seconds to respawn

        // Reset stats and reactivate
        currentHealth = maxHealth;
        isDead = false;
        GetComponent<CharacterController>().enabled = true;

        Debug.Log($"{gameObject.name} has Respawned!");
    }

//Press 'K' to simulate taking 500 damage
    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame && myTeam == Team.Team1)
        {
            TakeDamage(500f);
        }
    }
}