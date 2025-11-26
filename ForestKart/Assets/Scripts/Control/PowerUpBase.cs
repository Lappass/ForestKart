using UnityEngine;

/// <summary>
/// Power Up type enum
/// </summary>
public enum PowerUpType
{
    SpeedBoost,
    Shield,
    Missile,
    Shell,
    RedShell,
    OilSlick,
    Turbo,
}

/// <summary>
/// Power Up base class - defines common behavior for all power ups
/// </summary>
public abstract class PowerUp : ScriptableObject
{
    [Header("Power Up Info")]
    public PowerUpType type;
    public string powerUpName;
    public GameObject visualPrefab;
    public Vector3 attachOffset = new Vector3(0, 0.5f, -1f);
    
    /// <summary>
    /// Use power up effect
    /// </summary>
    public abstract void Use(KartController kartController, PowerUpSystem powerUpSystem);
    
    /// <summary>
    /// Power up effect duration (seconds), 0 means instant effect
    /// </summary>
    public abstract float GetDuration();
}
