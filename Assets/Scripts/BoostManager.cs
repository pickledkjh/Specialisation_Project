using UnityEngine;

public class BoostManager : MonoBehaviour
{
    [Header("Boost Stats")]
    public float maxBoost = 100f;
    public float currentBoost;
    public float regenRate = 35f;

    [Header("Costs")]
    public float dashDepletionRate = 20f; // Cost per second of sustained dashing
    public float stepCost = 25f;          // Flat cost for a quick dodge

    public bool isOverheated { get; private set; }

    private void Start()
    {
        currentBoost = maxBoost;
        isOverheated = false;
    }

    public void Regenerate(bool isGrounded)
    {
        // Only regenerate if on the ground and not full
        if (isGrounded && currentBoost < maxBoost)
        {
            currentBoost += regenRate * Time.deltaTime;
            if (currentBoost >= maxBoost)
            {
                currentBoost = maxBoost;
                isOverheated = false; // Clear overheat when fully charged
            }
        }
    }

    public bool CanBoost(float cost)
    {
        return !isOverheated && currentBoost >= cost;
    }

    public void ConsumeBoost(float amount)
    {
        currentBoost -= amount;
        if (currentBoost <= 0)
        {
            currentBoost = 0;
            isOverheated = true;
        }
    }

    public void ConsumeBoostOverTime(float rate)
    {
        ConsumeBoost(rate * Time.deltaTime);
    }
}