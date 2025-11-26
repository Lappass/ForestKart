using UnityEngine;

/// <summary>
/// Speed Boost Power Up - increases speed
/// </summary>
[CreateAssetMenu(fileName = "SpeedBoost", menuName = "PowerUp/Speed Boost")]
public class SpeedBoostPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.SpeedBoost;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Speed Boost";
        }
    }
    
    [Header("Speed Boost Settings")]
    public float speedMultiplier = 1.5f;
    public float duration = 5f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.StartCoroutine(powerUpSystem.ApplySpeedBoost(kartController, speedMultiplier, duration));
    }
    
    public override float GetDuration()
    {
        return duration;
    }
}
