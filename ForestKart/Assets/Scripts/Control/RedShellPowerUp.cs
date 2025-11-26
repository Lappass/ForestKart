using UnityEngine;

/// <summary>
/// Red Shell Power Up - auto-tracking shell (like Mario Kart)
/// </summary>
[CreateAssetMenu(fileName = "RedShell", menuName = "PowerUp/Red Shell")]
public class RedShellPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.RedShell;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Red Shell";
        }
    }
    
    [Header("Red Shell Settings")]
    public GameObject redShellPrefab;
    public float shellSpeed = 18f;
    public float hitForce = 3000f;
    public float stunDuration = 2f;
    public float trackingRange = 100f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.FireRedShell(kartController, redShellPrefab, shellSpeed, hitForce, stunDuration, trackingRange);
    }
    
    public override float GetDuration()
    {
        return 0f;
    }
}
