using UnityEngine;

/// <summary>
/// Turbo Power Up - instant boost
/// </summary>
[CreateAssetMenu(fileName = "Turbo", menuName = "PowerUp/Turbo")]
public class TurboPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.Turbo;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Turbo";
        }
    }
    
    [Header("Turbo Settings")]
    public float boostForce = 5000f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        Rigidbody rb = kartController.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 boostDirection = kartController.transform.forward;
            rb.AddForce(boostDirection * boostForce, ForceMode.Impulse);
        }
    }
    
    public override float GetDuration()
    {
        return 0f;
    }
}
