using UnityEngine;

/// <summary>
/// Shield Power Up - protective shield
/// </summary>
[CreateAssetMenu(fileName = "Shield", menuName = "PowerUp/Shield")]
public class ShieldPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.Shield;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Shield";
        }
    }
    
    [Header("Shield Settings")]
    public float duration = 8f;
    public GameObject shieldEffectPrefab;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.StartCoroutine(powerUpSystem.ApplyShield(kartController, shieldEffectPrefab, duration));
    }
    
    public override float GetDuration()
    {
        return duration;
    }
}
