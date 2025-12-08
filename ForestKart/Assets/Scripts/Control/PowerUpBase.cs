using UnityEngine;

public enum PowerUpType
{
    SpeedBoost,
    Shell,
    RedShell,
}

public abstract class PowerUp : ScriptableObject
{
    [Header("Power Up Info")]
    public PowerUpType type;
    public string powerUpName;
    public GameObject visualPrefab;
    public Vector3 attachOffset = new Vector3(0, 0.5f, -1f);
    
    public abstract void Use(KartController kartController, PowerUpSystem powerUpSystem);
    
    public abstract float GetDuration();
}
