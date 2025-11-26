using UnityEngine;

/// <summary>
/// Oil Slick Power Up - drops behind kart
/// </summary>
[CreateAssetMenu(fileName = "OilSlick", menuName = "PowerUp/Oil Slick")]
public class OilSlickPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.OilSlick;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Oil Slick";
        }
    }
    
    [Header("Oil Slick Settings")]
    public GameObject oilSlickPrefab;
    public float slickDuration = 10f;
    public float speedReduction = 0.5f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.DropOilSlick(kartController, oilSlickPrefab, slickDuration, speedReduction);
    }
    
    public override float GetDuration()
    {
        return 0f;
    }
}
