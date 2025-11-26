using UnityEngine;

/// <summary>
/// Missile Power Up - tracks nearest enemy ahead
/// </summary>
[CreateAssetMenu(fileName = "Missile", menuName = "PowerUp/Missile")]
public class MissilePowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.Missile;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Missile";
        }
    }
    
    [Header("Missile Settings")]
    public GameObject missilePrefab;
    public float missileSpeed = 20f;
    public float trackingRange = 50f;
    public float hitForce = 4000f;
    public float stunDuration = 3f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.FireMissile(kartController, missilePrefab, missileSpeed, trackingRange, hitForce, stunDuration);
    }
    
    public override float GetDuration()
    {
        return 0f;
    }
}
