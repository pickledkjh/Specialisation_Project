using UnityEngine;

// Defines the two teams in the game
public enum Team
{
    Team1, // Player Team
    Team2  // Enemy Team
}

public class CostManager : MonoBehaviour
{
    // Singleton allows any script to access this without a reference
    public static CostManager Instance { get; private set; }

    [Header("Team Cost Pools")]
    public int maxTeamCost = 6000;

    [SerializeField] private int team1CurrentCost;
    [SerializeField] private int team2CurrentCost;

    private void Awake()
    {
        // Enforce the Singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Initialize pools at the start of the match
        team1CurrentCost = maxTeamCost;
        team2CurrentCost = maxTeamCost;

        Debug.Log($"Match Started! Both teams have {maxTeamCost} Cost.");
    }

    // Called by a mech when its HP hits 0
    public void DeductCost(Team team, int unitCost)
    {
        if (team == Team.Team1)
        {
            team1CurrentCost -= unitCost;
            Debug.Log($"Team 1 unit destroyed! Team 1 Remaining Cost: {team1CurrentCost}");
        }
        else if (team == Team.Team2)
        {
            team2CurrentCost -= unitCost;
            Debug.Log($"Team 2 unit destroyed! Team 2 Remaining Cost: {team2CurrentCost}");
        }

        CheckWinCondition();
    }

    private void CheckWinCondition()
    {
        if (team1CurrentCost <= 0)
        {
            Debug.Log("GAME OVER: Team 2 Wins! (Team 1 Cost Depleted)");
            // TODO: Trigger End Match UI / Slow motion effect here
        }
        else if (team2CurrentCost <= 0)
        {
            Debug.Log("GAME OVER: Team 1 Wins! (Team 2 Cost Depleted)");
            // TODO: Trigger End Match UI / Slow motion effect here
        }
    }
}