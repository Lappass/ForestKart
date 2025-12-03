using UnityEngine;
using System.Collections;
using Unity.Netcode;
using System.Collections.Generic;
using UnityEngine.Splines;

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
        bool isAI = GetComponent<AIKartController>() != null || GetComponentInParent<AIKartController>() != null;
        
        if (!isAI)
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
    
    public bool HasPowerUp()
    {
        return hasPowerUp.Value;
    }
    
    public PowerUpType GetCurrentPowerUpType()
    {
        if (currentPowerUp == null) return PowerUpType.SpeedBoost;
        return currentPowerUp.type;
    }
    
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
    private void FireShellServerRpc(ulong ownerId, int prefabIndex, float speed, float hitForce, float stunDuration, ServerRpcParams serverRpcParams = default)
    {
        Debug.Log($"[PowerUpSystem] FireShellServerRpc called by SenderClientId: {serverRpcParams.Receive.SenderClientId}, passed ownerId: {ownerId}");

        KartController kart = null;
        kart = this.kartController;
        if (kart == null && NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerId, out var client))
        {
             kart = client.PlayerObject?.GetComponentInChildren<KartController>();
        }
        
        if (kart == null) 
        {
             Debug.LogWarning($"[PowerUpSystem] FireShellServerRpc: Could not find kart controller! OwnerId: {ownerId}");
             return;
        }

        Debug.Log($"[PowerUpSystem] Shell Owner identified as: {kart.gameObject.name} (NetId: {kart.NetworkObjectId})");
        
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
        var netTransform = shell.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (netTransform == null)
        {
            netTransform = shell.AddComponent<Unity.Netcode.Components.NetworkTransform>();
        }
        netTransform.SyncPositionX = true;
        netTransform.SyncPositionY = true;
        netTransform.SyncPositionZ = true;
        netTransform.SyncRotAngleX = true;
        netTransform.SyncRotAngleY = true;
        netTransform.SyncRotAngleZ = true;
        netTransform.UseHalfFloatPrecision = false;
        netTransform.Interpolate = true;
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
    }
    
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
    private void FireRedShellServerRpc(ulong ownerId, int prefabIndex, float speed, float hitForce, float stunDuration, float trackingRange, ServerRpcParams serverRpcParams = default)
    {
        Debug.Log($"[PowerUpSystem] FireRedShellServerRpc called by SenderClientId: {serverRpcParams.Receive.SenderClientId}, passed ownerId: {ownerId}");
        KartController kart = null;
        kart = this.kartController;
        if (kart == null && NetworkManager.Singleton.ConnectedClients.TryGetValue(ownerId, out var client))
        {
             kart = client.PlayerObject?.GetComponentInChildren<KartController>();
        }

        if (kart == null)
        {
            Debug.LogWarning($"[PowerUpSystem] FireRedShellServerRpc: Could not find kart controller! OwnerId: {ownerId}");
            return;
        }

        Debug.Log($"[PowerUpSystem] RedShell Owner identified as: {kart.gameObject.name} (NetId: {kart.NetworkObjectId})");
        
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
        var netTransform = redShell.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (netTransform == null)
        {
            netTransform = redShell.AddComponent<Unity.Netcode.Components.NetworkTransform>();
        }
        netTransform.SyncPositionX = true;
        netTransform.SyncPositionY = true;
        netTransform.SyncPositionZ = true;
        netTransform.SyncRotAngleX = true;
        netTransform.SyncRotAngleY = true;
        netTransform.SyncRotAngleZ = true;
        netTransform.UseHalfFloatPrecision = false;
        netTransform.Interpolate = true; 
        
        netObj.SpawnWithOwnership(NetworkManager.ServerClientId);
    }
    
    private Transform FindNearestEnemy(Transform self, float range)
    {
        Transform nearest = null;
        float nearestDistance = float.MaxValue;
        float bestScore = float.MinValue;
        KartController[] allKarts = FindObjectsByType<KartController>(FindObjectsSortMode.None);
        SplineContainer spline = null;
        float selfSplinePos = -1f;
        AIKartController myAI = self.GetComponent<AIKartController>();
        if (myAI == null) myAI = self.GetComponentInParent<AIKartController>();
        
        if (myAI != null && myAI.splinePath != null)
        {
             spline = myAI.splinePath;
        }
        else if (GameManager.Instance != null && GameManager.Instance.raceTrack != null)
        {
             spline = GameManager.Instance.raceTrack;
        }
        else
        {
             SplineContainer[] splines = FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
             if (splines.Length > 0) spline = splines[0];
        }

        if (spline != null)
        {
             using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
             {
                 float t;
                 UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, self.position, out _, out t);
                 selfSplinePos = t;
             }
        }
        
        foreach (var kart in allKarts)
        {
            if (kart.transform == self || kart.transform.root == self.root) continue;
            
            float distance = Vector3.Distance(self.position, kart.transform.position);
            if (distance > range) continue;
            if (spline != null && selfSplinePos >= 0f)
            {
                using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
                {
                     float targetT;
                     UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, kart.transform.position, out _, out targetT);
                     float diff = targetT - selfSplinePos;
                     if (diff < -0.5f) diff += 1f; 
                     if (diff > 0.5f) diff -= 1f; 
                     if (diff > 0f)
                     {
                         float score = 1.0f - diff; 
                         if (score > bestScore)
                         {
                             bestScore = score;
                             nearest = kart.transform;
                         }
                     }
                }
            }
            else
            {
                Vector3 direction = (kart.transform.position - self.position).normalized;
                float dot = Vector3.Dot(self.forward, direction);
                if (dot > 0.5f) 
                {
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = kart.transform;
                    }
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
