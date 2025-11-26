using UnityEngine;
using System.Collections;
using Unity.Netcode;
using System.Collections.Generic;

/// <summary>
/// Power Up System - manages player power up state
/// </summary>
[RequireComponent(typeof(KartController))]
public class PowerUpSystem : NetworkBehaviour
{
    [Header("Power Up Settings")]
    [Tooltip("Power up model attach point (rear of kart)")]
    public Transform powerUpAttachPoint;
    
    [Tooltip("Auto find attach point if not set")]
    public bool autoFindAttachPoint = true;
    
    [Tooltip("Power up list (random selection) - drag in ScriptableObject assets (.asset files, not prefabs)")]
    public PowerUp[] availablePowerUps;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    private KartController kartController;
    private PowerUp currentPowerUp = null;
    private GameObject currentPowerUpVisual = null;
    private NetworkVariable<int> currentPowerUpTypeIndex = new NetworkVariable<int>(-1);
    private NetworkVariable<bool> hasPowerUp = new NetworkVariable<bool>(false);
    
    private bool hasSpeedBoost = false;
    private float originalMaxSpeed = 0f;
    private float originalDriveTorque = 0f;
    private GameObject shieldEffect = null;
    
    private void Awake()
    {
        kartController = GetComponent<KartController>();
        
        if (autoFindAttachPoint && powerUpAttachPoint == null)
        {
            Transform attachPoint = transform.Find("PowerUpAttachPoint");
            if (attachPoint == null)
            {
                GameObject attachPointObj = new GameObject("PowerUpAttachPoint");
                attachPointObj.transform.SetParent(transform);
                attachPointObj.transform.localPosition = new Vector3(0, 0.5f, -1f);
                attachPointObj.transform.localRotation = Quaternion.identity;
                powerUpAttachPoint = attachPointObj.transform;
                
                if (showDebugInfo)
                {
                    Debug.Log($"[PowerUpSystem] Created default attach point for {gameObject.name}");
                }
            }
            else
            {
                powerUpAttachPoint = attachPoint;
            }
        }
        
        if (availablePowerUps == null || availablePowerUps.Length == 0)
        {
            CreateDefaultPowerUps();
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        currentPowerUpTypeIndex.OnValueChanged += OnPowerUpTypeChanged;
        hasPowerUp.OnValueChanged += OnHasPowerUpChanged;
        
        if (!IsServer && hasPowerUp.Value && currentPowerUpTypeIndex.Value >= 0)
        {
            SyncPowerUpVisual(currentPowerUpTypeIndex.Value);
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        currentPowerUpTypeIndex.OnValueChanged -= OnPowerUpTypeChanged;
        hasPowerUp.OnValueChanged -= OnHasPowerUpChanged;
        
        ClearPowerUp();
    }
    
    private void OnPowerUpTypeChanged(int oldValue, int newValue)
    {
        if (newValue >= 0 && newValue < availablePowerUps.Length)
        {
            SyncPowerUpVisual(newValue);
        }
        else if (newValue < 0)
        {
            ClearPowerUpVisual();
        }
    }
    
    private void OnHasPowerUpChanged(bool oldValue, bool newValue)
    {
        if (!newValue && currentPowerUpVisual != null)
        {
            ClearPowerUpVisual();
        }
    }
    
    /// <summary>
    /// Acquire a random power up
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void AcquireRandomPowerUpServerRpc()
    {
        if (availablePowerUps == null || availablePowerUps.Length == 0)
        {
            Debug.LogWarning("[PowerUpSystem] No available power ups!");
            return;
        }
        
        if (hasPowerUp.Value)
        {
            ClearPowerUp();
        }
        
        int randomIndex = Random.Range(0, availablePowerUps.Length);
        PowerUp selectedPowerUp = availablePowerUps[randomIndex];
        
        if (selectedPowerUp != null)
        {
            AcquirePowerUp(selectedPowerUp, randomIndex);
        }
    }
    
    private void AcquirePowerUp(PowerUp powerUp, int index)
    {
        currentPowerUp = powerUp;
        hasPowerUp.Value = true;
        currentPowerUpTypeIndex.Value = index;
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] {gameObject.name} acquired power up: {powerUp.powerUpName}");
        }
    }
    
    /// <summary>
    /// Use current power up
    /// </summary>
    public void UsePowerUp()
    {
        if (!hasPowerUp.Value || currentPowerUpTypeIndex.Value < 0)
        {
            if (showDebugInfo)
            {
                Debug.Log($"[PowerUpSystem] {gameObject.name} has no power up to use (hasPowerUp: {hasPowerUp.Value}, index: {currentPowerUpTypeIndex.Value})");
            }
            return;
        }
        
        UsePowerUpServerRpc();
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void UsePowerUpServerRpc(ServerRpcParams serverRpcParams = default)
    {
        NetworkObject kartNetObj = kartController?.GetComponent<NetworkObject>();
        if (kartNetObj == null)
        {
            kartNetObj = kartController?.GetComponentInParent<NetworkObject>();
        }
        
        if (kartNetObj == null || kartNetObj.OwnerClientId != serverRpcParams.Receive.SenderClientId)
        {
            Debug.LogWarning($"[PowerUpSystem] UsePowerUp called by non-owner! Sender: {serverRpcParams.Receive.SenderClientId}, KartOwner: {kartNetObj?.OwnerClientId}");
            return;
        }
        
        if (!hasPowerUp.Value || currentPowerUpTypeIndex.Value < 0)
        {
            if (showDebugInfo)
            {
                Debug.Log($"[PowerUpSystem] {gameObject.name} has no power up to use (hasPowerUp: {hasPowerUp.Value}, index: {currentPowerUpTypeIndex.Value})");
            }
            return;
        }
        
        int powerUpIndex = currentPowerUpTypeIndex.Value;
        if (powerUpIndex < 0 || powerUpIndex >= availablePowerUps.Length)
        {
            Debug.LogWarning($"[PowerUpSystem] Invalid power up index: {powerUpIndex}");
            return;
        }
        
        PowerUp powerUpToUse = availablePowerUps[powerUpIndex];
        if (powerUpToUse == null)
        {
            Debug.LogWarning($"[PowerUpSystem] Power up at index {powerUpIndex} is null");
            return;
        }
        
        string powerUpName = powerUpToUse.powerUpName;
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] {gameObject.name} using power up: {powerUpName}");
        }
        
        powerUpToUse.Use(kartController, this);
        
        ClearPowerUp();
    }
    
    private void ClearPowerUp()
    {
        currentPowerUp = null;
        hasPowerUp.Value = false;
        currentPowerUpTypeIndex.Value = -1;
        ClearPowerUpVisual();
    }
    
    private void SyncPowerUpVisual(int powerUpIndex)
    {
        if (availablePowerUps == null || powerUpIndex < 0 || powerUpIndex >= availablePowerUps.Length)
        {
            ClearPowerUpVisual();
            return;
        }
        
        PowerUp powerUp = availablePowerUps[powerUpIndex];
        if (powerUp == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[PowerUpSystem] Power up at index {powerUpIndex} is null");
            }
            return;
        }
        
        if (powerUp.visualPrefab == null)
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[PowerUpSystem] Power up '{powerUp.powerUpName}' has no visual prefab");
            }
            return;
        }
        
        ClearPowerUpVisual();
        
        if (powerUpAttachPoint != null)
        {
            currentPowerUpVisual = Instantiate(powerUp.visualPrefab, powerUpAttachPoint);
            currentPowerUpVisual.transform.localPosition = powerUp.attachOffset;
            currentPowerUpVisual.transform.localRotation = Quaternion.identity;
            
            NetworkObject netObj = currentPowerUpVisual.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);
            
            if (showDebugInfo)
            {
                Debug.Log($"[PowerUpSystem] Spawned power up visual: {powerUp.powerUpName}");
            }
        }
        else
        {
            if (showDebugInfo)
            {
                Debug.LogWarning($"[PowerUpSystem] Power up attach point is null, cannot spawn visual");
            }
        }
    }
    
    private void ClearPowerUpVisual()
    {
        if (currentPowerUpVisual != null)
        {
            Destroy(currentPowerUpVisual);
            currentPowerUpVisual = null;
        }
    }
    
    /// <summary>
    /// Apply speed boost effect
    /// </summary>
    public IEnumerator ApplySpeedBoost(KartController kart, float multiplier, float duration)
    {
        if (hasSpeedBoost || kart == null) yield break;
        
        hasSpeedBoost = true;
        originalMaxSpeed = kart.maxSpeed;
        originalDriveTorque = kart.DriveTorque;
        
        if (originalMaxSpeed <= 0) originalMaxSpeed = 50f;
        if (originalDriveTorque <= 0) originalDriveTorque = 100f;
        
        kart.maxSpeed = originalMaxSpeed * multiplier;
        kart.DriveTorque = originalDriveTorque * multiplier;
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] Speed boost applied: {multiplier}x for {duration}s on {kart.gameObject.name}");
        }
        
        yield return new WaitForSeconds(duration);
        
        if (kart != null)
        {
            kart.maxSpeed = originalMaxSpeed;
            kart.DriveTorque = originalDriveTorque;
        }
        hasSpeedBoost = false;
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] Speed boost ended");
        }
    }
    
    /// <summary>
    /// Apply shield effect
    /// </summary>
    public IEnumerator ApplyShield(KartController kart, GameObject shieldPrefab, float duration)
    {
        if (shieldEffect != null || kart == null) yield break;
        
        if (shieldPrefab != null)
        {
            shieldEffect = Instantiate(shieldPrefab, kart.transform);
            shieldEffect.transform.localPosition = Vector3.zero;
            shieldEffect.transform.localRotation = Quaternion.identity;
            
            NetworkObject netObj = shieldEffect.GetComponent<NetworkObject>();
            if (netObj != null) Destroy(netObj);
        }
        
        // TODO: Implement shield logic (damage prevention etc.)
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] Shield applied for {duration}s on {kart.gameObject.name}");
        }
        
        yield return new WaitForSeconds(duration);
        
        if (kart != null && shieldEffect != null)
        {
            Destroy(shieldEffect);
            shieldEffect = null;
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"[PowerUpSystem] Shield ended");
        }
    }
    
    /// <summary>
    /// Check if has power up
    /// </summary>
    public bool HasPowerUp()
    {
        return hasPowerUp.Value;
    }
    
    /// <summary>
    /// Get current power up type
    /// </summary>
    public PowerUpType GetCurrentPowerUpType()
    {
        if (currentPowerUp == null) return PowerUpType.SpeedBoost;
        return currentPowerUp.type;
    }
    
    /// <summary>
    /// Fire shell (throw forward)
    /// </summary>
    public void FireShell(KartController kart, GameObject shellPrefab, float speed, float hitForce, float stunDuration)
    {
        if (kart == null || shellPrefab == null) return;
        
        int prefabIndex = -1;
        for (int i = 0; i < availablePowerUps.Length; i++)
        {
            if (availablePowerUps[i] is ShellPowerUp shellPowerUp && shellPowerUp.shellPrefab == shellPrefab)
            {
                prefabIndex = i;
                break;
            }
        }
        
        if (prefabIndex < 0)
        {
            Debug.LogWarning("[PowerUpSystem] Shell prefab not found in availablePowerUps!");
            return;
        }
        
        FireShellServerRpc(kart.OwnerClientId, prefabIndex, speed, hitForce, stunDuration);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void FireShellServerRpc(ulong ownerId, int prefabIndex, float speed, float hitForce, float stunDuration)
    {
        KartController kart = NetworkManager.Singleton.ConnectedClients[ownerId].PlayerObject?.GetComponentInChildren<KartController>();
        if (kart == null) return;
        
        if (prefabIndex < 0 || prefabIndex >= availablePowerUps.Length) return;
        
        ShellPowerUp shellPowerUp = availablePowerUps[prefabIndex] as ShellPowerUp;
        if (shellPowerUp == null || shellPowerUp.shellPrefab == null) return;
        
        GameObject shellPrefab = shellPowerUp.shellPrefab;
        
        Vector3 spawnPos = kart.transform.position + kart.transform.forward * 2f + Vector3.up * 0.5f;
        GameObject shell = Instantiate(shellPrefab, spawnPos, kart.transform.rotation);
        
        ShellProjectile shellScript = shell.GetComponent<ShellProjectile>();
        if (shellScript == null)
        {
            shellScript = shell.AddComponent<ShellProjectile>();
        }
        
        NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
        if (kartNetObj == null)
        {
            kartNetObj = kart.GetComponentInParent<NetworkObject>();
        }
        Transform ownerTransform = kartNetObj != null ? kartNetObj.transform : kart.transform;
        
        shellScript.SetParameters(ownerTransform, speed, hitForce, stunDuration);
        
        NetworkObject netObj = shell.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = shell.AddComponent<NetworkObject>();
        }
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
    }
    
    /// <summary>
    /// Fire red shell (auto-track nearest enemy)
    /// </summary>
    public void FireRedShell(KartController kart, GameObject redShellPrefab, float speed, float hitForce, float stunDuration, float trackingRange)
    {
        if (kart == null || redShellPrefab == null) return;
        
        int prefabIndex = -1;
        for (int i = 0; i < availablePowerUps.Length; i++)
        {
            if (availablePowerUps[i] is RedShellPowerUp redShellPowerUp && redShellPowerUp.redShellPrefab == redShellPrefab)
            {
                prefabIndex = i;
                break;
            }
        }
        
        if (prefabIndex < 0)
        {
            Debug.LogWarning("[PowerUpSystem] Red shell prefab not found in availablePowerUps!");
            return;
        }
        
        FireRedShellServerRpc(kart.OwnerClientId, prefabIndex, speed, hitForce, stunDuration, trackingRange);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void FireRedShellServerRpc(ulong ownerId, int prefabIndex, float speed, float hitForce, float stunDuration, float trackingRange)
    {
        KartController kart = NetworkManager.Singleton.ConnectedClients[ownerId].PlayerObject?.GetComponentInChildren<KartController>();
        if (kart == null) return;
        
        if (prefabIndex < 0 || prefabIndex >= availablePowerUps.Length) return;
        
        RedShellPowerUp redShellPowerUp = availablePowerUps[prefabIndex] as RedShellPowerUp;
        if (redShellPowerUp == null || redShellPowerUp.redShellPrefab == null) return;
        
        GameObject redShellPrefab = redShellPowerUp.redShellPrefab;
        
        Transform target = FindNearestEnemy(kart.transform, trackingRange);
        
        Vector3 spawnPos = kart.transform.position + kart.transform.forward * 2f + Vector3.up * 0.5f;
        GameObject redShell = Instantiate(redShellPrefab, spawnPos, kart.transform.rotation);
        
        RedShellProjectile redShellScript = redShell.GetComponent<RedShellProjectile>();
        if (redShellScript == null)
        {
            redShellScript = redShell.AddComponent<RedShellProjectile>();
        }
        
        NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
        if (kartNetObj == null)
        {
            kartNetObj = kart.GetComponentInParent<NetworkObject>();
        }
        Transform ownerTransform = kartNetObj != null ? kartNetObj.transform : kart.transform;
        
        redShellScript.SetParameters(ownerTransform, target, speed, hitForce, stunDuration, trackingRange);
        
        NetworkObject netObj = redShell.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = redShell.AddComponent<NetworkObject>();
        }
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
    }
    
    /// <summary>
    /// Fire missile (track nearest enemy)
    /// </summary>
    public void FireMissile(KartController kart, GameObject missilePrefab, float speed, float trackingRange, float hitForce, float stunDuration)
    {
        if (kart == null || missilePrefab == null) return;
        
        int prefabIndex = -1;
        for (int i = 0; i < availablePowerUps.Length; i++)
        {
            if (availablePowerUps[i] is MissilePowerUp missilePowerUp && missilePowerUp.missilePrefab == missilePrefab)
            {
                prefabIndex = i;
                break;
            }
        }
        
        if (prefabIndex < 0)
        {
            Debug.LogWarning("[PowerUpSystem] Missile prefab not found in availablePowerUps!");
            return;
        }
        
        FireMissileServerRpc(kart.OwnerClientId, prefabIndex, speed, trackingRange, hitForce, stunDuration);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void FireMissileServerRpc(ulong ownerId, int prefabIndex, float speed, float trackingRange, float hitForce, float stunDuration)
    {
        KartController kart = NetworkManager.Singleton.ConnectedClients[ownerId].PlayerObject?.GetComponentInChildren<KartController>();
        if (kart == null) return;
        
        if (prefabIndex < 0 || prefabIndex >= availablePowerUps.Length) return;
        
        MissilePowerUp missilePowerUp = availablePowerUps[prefabIndex] as MissilePowerUp;
        if (missilePowerUp == null || missilePowerUp.missilePrefab == null) return;
        
        GameObject missilePrefab = missilePowerUp.missilePrefab;
        
        Transform target = FindNearestEnemy(kart.transform, trackingRange);
        
        Vector3 spawnPos = kart.transform.position + kart.transform.forward * 2f + Vector3.up * 1f;
        GameObject missile = Instantiate(missilePrefab, spawnPos, kart.transform.rotation);
        
        MissileProjectile missileScript = missile.GetComponent<MissileProjectile>();
        if (missileScript == null)
        {
            missileScript = missile.AddComponent<MissileProjectile>();
        }
        
        NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
        if (kartNetObj == null)
        {
            kartNetObj = kart.GetComponentInParent<NetworkObject>();
        }
        Transform ownerTransform = kartNetObj != null ? kartNetObj.transform : kart.transform;
        
        missileScript.SetParameters(target, speed, hitForce, stunDuration);
        
        NetworkObject netObj = missile.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = missile.AddComponent<NetworkObject>();
        }
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
    }
    
    /// <summary>
    /// Drop oil slick (behind kart)
    /// </summary>
    public void DropOilSlick(KartController kart, GameObject oilSlickPrefab, float duration, float speedReduction)
    {
        if (kart == null || oilSlickPrefab == null) return;
        
        int prefabIndex = -1;
        for (int i = 0; i < availablePowerUps.Length; i++)
        {
            if (availablePowerUps[i] is OilSlickPowerUp oilPowerUp && oilPowerUp.oilSlickPrefab == oilSlickPrefab)
            {
                prefabIndex = i;
                break;
            }
        }
        
        if (prefabIndex < 0)
        {
            Debug.LogWarning("[PowerUpSystem] Oil slick prefab not found in availablePowerUps!");
            return;
        }
        
        DropOilSlickServerRpc(kart.OwnerClientId, prefabIndex, duration, speedReduction);
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void DropOilSlickServerRpc(ulong ownerId, int prefabIndex, float duration, float speedReduction)
    {
        KartController kart = NetworkManager.Singleton.ConnectedClients[ownerId].PlayerObject?.GetComponentInChildren<KartController>();
        if (kart == null) return;
        
        if (prefabIndex < 0 || prefabIndex >= availablePowerUps.Length) return;
        
        OilSlickPowerUp oilPowerUp = availablePowerUps[prefabIndex] as OilSlickPowerUp;
        if (oilPowerUp == null || oilPowerUp.oilSlickPrefab == null) return;
        
        GameObject oilSlickPrefab = oilPowerUp.oilSlickPrefab;
        
        Vector3 dropPos = kart.transform.position - kart.transform.forward * 2f;
        dropPos.y = 0f;
        
        GameObject oilSlick = Instantiate(oilSlickPrefab, dropPos, Quaternion.identity);
        
        NetworkObject netObj = oilSlick.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = oilSlick.AddComponent<NetworkObject>();
        }
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
        
        OilSlick slickScript = oilSlick.GetComponent<OilSlick>();
        if (slickScript == null)
        {
            slickScript = oilSlick.AddComponent<OilSlick>();
        }
        slickScript.Initialize(duration, speedReduction);
    }
    
    /// <summary>
    /// Find nearest enemy
    /// </summary>
    private Transform FindNearestEnemy(Transform self, float range)
    {
        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        
        KartController[] allKarts = FindObjectsByType<KartController>(FindObjectsSortMode.None);
        
        foreach (var kart in allKarts)
        {
            if (kart.transform == self) continue;
            
            float distance = Vector3.Distance(self.position, kart.transform.position);
            if (distance < range && distance < nearestDistance)
            {
                Vector3 direction = (kart.transform.position - self.position).normalized;
                float dot = Vector3.Dot(self.forward, direction);
                if (dot > 0.3f)
                {
                    nearest = kart.transform;
                    nearestDistance = distance;
                }
            }
        }
        
        return nearest;
    }
    
    private void CreateDefaultPowerUps()
    {
        Debug.LogWarning("[PowerUpSystem] No power ups assigned! Please assign power ups in the inspector.");
    }
}
