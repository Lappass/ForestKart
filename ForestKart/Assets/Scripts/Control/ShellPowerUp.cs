using UnityEngine;
[CreateAssetMenu(fileName = "Shell", menuName = "PowerUp/Shell")]
public class ShellPowerUp : PowerUp
{
    private void OnEnable()
    {
        type = PowerUpType.Shell;
        if (string.IsNullOrEmpty(powerUpName))
        {
            powerUpName = "Shell";
        }
    }
    
    [Header("Shell Settings")]
    public GameObject shellPrefab;
    public float shellSpeed = 15f;
    public float hitForce = 3000f;
    public float stunDuration = 2f;
    
    public override void Use(KartController kartController, PowerUpSystem powerUpSystem)
    {
        if (kartController == null) return;
        
        Debug.Log($"[PowerUp] {powerUpName} used by {kartController.gameObject.name}");
        powerUpSystem.FireShell(kartController, shellPrefab, shellSpeed, hitForce, stunDuration);
    }
    
    public override float GetDuration()
    {
        return 0f;
    }
}
