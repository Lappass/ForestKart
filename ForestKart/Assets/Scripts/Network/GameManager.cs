using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Cinemachine;

public class GameManager : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject playerPrefab;
    public GameObject aiKartPrefab;
    
    [Header("Spawn Settings")]
    public SplineContainer raceTrack;
    public int aiKartCount = 3;
    public float spawnSpacing = 3f;
    
    [Tooltip("Spawn points list (optional). If set, will use these points instead of spacing")]
    public Transform[] spawnPoints;
    
    [Header("Countdown")]
    public float countdownDuration = 3f;
    
    [Header("Ranking")]
    public float rankingUpdateInterval = 0.1f;
    
    [Header("Race Settings")]
    public int totalLaps = 3;
    
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> gameFinished = new NetworkVariable<bool>(false);
    private NetworkVariable<float> countdownTime = new NetworkVariable<float>(0f);
    
    private float rankingUpdateTimer = 0f;
    
    public static GameManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            gameStarted.Value = false;
            gameFinished.Value = false;
            countdownTime.Value = 0f;
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void StartRaceServerRpc()
    {
        if (gameStarted.Value) return;
        
        StartCoroutine(SpawnAndStartRace());
    }
    
    private IEnumerator SpawnAndStartRace()
    {
        if (raceTrack == null)
        {
            raceTrack = FindFirstObjectByType<SplineContainer>();
            if (raceTrack == null)
            {
                Debug.LogError("No SplineContainer found in scene!");
                yield break;
            }
        }
        
        gameStarted.Value = true;
        
        bool useSpawnPoints = spawnPoints != null && spawnPoints.Length > 0;
        
        float splineLength = raceTrack.Spline.GetLength();
        Vector3 startPosition = raceTrack.transform.TransformPoint(
            SplineUtility.EvaluatePosition(raceTrack.Spline, 0f)
        );
        Vector3 startDirection = SplineUtility.EvaluateTangent(raceTrack.Spline, 0f);
        Quaternion startRotation = Quaternion.LookRotation(startDirection);
        
        int totalSpawnCount = NetworkManager.Singleton.ConnectedClientsIds.Count + aiKartCount;
        
        if (useSpawnPoints)
        {
            if (spawnPoints.Length < totalSpawnCount)
            {
                Debug.LogWarning($"Spawn points count ({spawnPoints.Length}) is less than required ({totalSpawnCount}), will reuse spawn points");
            }
        }
        
        int spawnIndex = 0;
        
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            Vector3 spawnPos;
            Quaternion spawnRot;
            
            if (useSpawnPoints && spawnIndex < spawnPoints.Length)
            {
                Transform spawnPoint = spawnPoints[spawnIndex];
                spawnPos = spawnPoint.position;
                spawnRot = spawnPoint.rotation;
            }
            else if (useSpawnPoints && spawnPoints.Length > 0)
            {
                Transform spawnPoint = spawnPoints[spawnPoints.Length - 1];
                spawnPos = spawnPoint.position + Vector3.right * (spawnIndex - spawnPoints.Length + 1) * spawnSpacing;
                spawnRot = spawnPoint.rotation;
            }
            else
            {
                spawnPos = startPosition + Vector3.right * spawnIndex * spawnSpacing;
                spawnRot = startRotation;
            }
            
            GameObject player = Instantiate(playerPrefab, spawnPos, spawnRot);
            NetworkObject playerNetObj = player.GetComponent<NetworkObject>();
            if (playerNetObj != null)
            {
                playerNetObj.SpawnAsPlayerObject(clientId);
                playerNetObj.ChangeOwnership(clientId);
            }
            
            KartController kart = player.GetComponentInChildren<KartController>();
            if (kart != null)
            {
                kart.DisableControls();
            }
            
            spawnIndex++;
        }
        
        yield return new WaitForSeconds(0.1f);
        
        ActivatePlayerInputsClientRpc();
        
        yield return new WaitForSeconds(0.4f);
        
        for (int i = 0; i < aiKartCount; i++)
        {
            Vector3 spawnPos;
            Quaternion spawnRot;
            
            if (useSpawnPoints && spawnIndex < spawnPoints.Length)
            {
                Transform spawnPoint = spawnPoints[spawnIndex];
                spawnPos = spawnPoint.position;
                spawnRot = spawnPoint.rotation;
            }
            else if (useSpawnPoints && spawnPoints.Length > 0)
            {
                Transform spawnPoint = spawnPoints[spawnPoints.Length - 1];
                spawnPos = spawnPoint.position + Vector3.right * (spawnIndex - spawnPoints.Length + 1) * spawnSpacing;
                spawnRot = spawnPoint.rotation;
            }
            else
            {
                spawnPos = startPosition + Vector3.right * spawnIndex * spawnSpacing;
                spawnRot = startRotation;
            }
            
            GameObject aiKart = Instantiate(aiKartPrefab, spawnPos, spawnRot);
            NetworkObject aiNetObj = aiKart.GetComponent<NetworkObject>();
            if (aiNetObj != null)
            {
                aiNetObj.Spawn();
            }
            
            KartController aiKartController = aiKart.GetComponent<KartController>();
            if (aiKartController != null)
            {
                aiKartController.DisableControls();
            }
            
            AIKartController aiController = aiKart.GetComponent<AIKartController>();
            if (aiController != null && raceTrack != null)
            {
                aiController.splinePath = raceTrack;
                
                float speedVariation = UnityEngine.Random.Range(0.9f, 1.2f);
                aiController.speedMultiplier *= speedVariation;
                aiController.speedVariation = UnityEngine.Random.Range(0.1f, 0.25f);
                aiController.RecalculateSpeed();
            }
            
            spawnIndex++;
            yield return new WaitForSeconds(0.2f);
        }
        
        yield return new WaitForSeconds(1f);
        
        StartCountdownServerRpc();
    }
    
    [ServerRpc]
    private void StartCountdownServerRpc()
    {
        StartCoroutine(CountdownCoroutine());
    }
    
    private IEnumerator CountdownCoroutine()
    {
        countdownTime.Value = countdownDuration;
        
        while (countdownTime.Value > 0f)
        {
            countdownTime.Value -= Time.deltaTime;
            yield return null;
        }
        
        countdownTime.Value = 0f;
        EnablePlayerControlsServerRpc();
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void EnablePlayerControlsServerRpc()
    {
        EnablePlayerControlsClientRpc();
    }
    
    [ClientRpc]
    private void ActivatePlayerInputsClientRpc()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject != null)
            {
                UnityEngine.InputSystem.PlayerInput playerInput = client.Value.PlayerObject.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                if (playerInput != null)
                {
                    playerInput.enabled = true;
                    playerInput.ActivateInput();
                }
            }
        }
    }
    
    [ClientRpc]
    private void EnablePlayerControlsClientRpc()
    {
        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObject != null)
        {
            KartController kart = localPlayerObject.GetComponentInChildren<KartController>();
            if (kart != null)
            {
                kart.EnableControls();
            }

            UnityEngine.InputSystem.PlayerInput playerInput = localPlayerObject.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = true;
                playerInput.ActivateInput();
            }
        }

        if (IsServer)
        {
            AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
            foreach (var ai in aiKarts)
            {
                KartController aiKart = ai.GetComponent<KartController>();
                if (aiKart != null)
                {
                    aiKart.EnableControls();
                }
            }
        }
    }
    
    [ClientRpc]
    private void DisableAllControlsClientRpc()
    {
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject != null)
            {
                KartController kart = client.Value.PlayerObject.GetComponentInChildren<KartController>();
                if (kart != null)
                {
                    kart.DisableControls();
                }

                UnityEngine.InputSystem.PlayerInput playerInput = client.Value.PlayerObject.GetComponent<UnityEngine.InputSystem.PlayerInput>();
                if (playerInput != null)
                {
                    playerInput.enabled = false;
                }
            }
        }

        if (IsServer)
        {
            AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
            foreach (var ai in aiKarts)
            {
                KartController aiKart = ai.GetComponent<KartController>();
                if (aiKart != null)
                {
                    aiKart.DisableControls();
                }
            }
        }
    }
    
    public float GetCountdownTime()
    {
        return countdownTime.Value;
    }
    
    public bool IsGameStarted()
    {
        return gameStarted.Value;
    }
    
    public bool IsGameFinished()
    {
        return gameFinished.Value;
    }
    
    void Update()
    {
        if (IsServer && gameStarted.Value && !gameFinished.Value && countdownTime.Value <= 0f)
        {
            rankingUpdateTimer += Time.deltaTime;
            if (rankingUpdateTimer >= rankingUpdateInterval)
            {
                rankingUpdateTimer = 0f;
                UpdateRankings();
                CheckRaceFinish();
            }
        }
    }
    
    private void UpdateRankings()
    {
    }
    
    private void CheckRaceFinish()
    {
        if (gameFinished.Value) return;
        
        Transform winnerTransform = null;
        
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject != null)
            {
                PlayerProgressTracker tracker = client.Value.PlayerObject.GetComponent<PlayerProgressTracker>();
                if (tracker == null)
                {
                    tracker = client.Value.PlayerObject.GetComponentInChildren<PlayerProgressTracker>();
                }
                
                if (tracker != null && tracker.GetLapCount() >= totalLaps)
                {
                    winnerTransform = client.Value.PlayerObject.transform;
                    FinishRace(winnerTransform);
                    return;
                }
            }
        }
        
        AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
        foreach (var aiKart in aiKarts)
        {
            if (aiKart != null && aiKart.GetLapCount() >= totalLaps)
            {
                winnerTransform = aiKart.transform;
                FinishRace(winnerTransform);
                return;
            }
        }
    }
    
    private void FinishRace(Transform winner)
    {
        if (gameFinished.Value) return;
        
        gameFinished.Value = true;
        FocusAllCamerasOnWinnerClientRpc(winner.GetComponent<NetworkObject>());
        
        Debug.Log("Race finished! A player completed 3 laps.");
    }
    
    [ClientRpc]
    private void FocusAllCamerasOnWinnerClientRpc(NetworkObjectReference winnerRef)
    {
        if (!winnerRef.TryGet(out NetworkObject winnerObj) || winnerObj == null) return;
        
        Transform winnerParent = winnerObj.transform.parent;
        if (winnerParent == null)
        {
            winnerParent = winnerObj.transform;
        }
        
        CinemachineCamera winnerCamera = winnerParent.GetComponentInChildren<CinemachineCamera>();
        if (winnerCamera == null)
        {
            Debug.LogWarning("[GameManager] Could not find CinemachineCamera in winner's parent hierarchy");
            return;
        }
        
        var cinemachineFollow = winnerCamera.GetComponent<CinemachineFollow>();
        if (cinemachineFollow != null)
        {
            System.Reflection.FieldInfo followOffsetField = null;
            object targetObject = null;
            
            var targetTrackerField = typeof(CinemachineFollow).GetField("m_TargetTracker", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (targetTrackerField != null)
            {
                var targetTracker = targetTrackerField.GetValue(cinemachineFollow);
                if (targetTracker != null)
                {
                    var trackerType = targetTracker.GetType();
                    var settingsProp = trackerType.GetProperty("Settings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (settingsProp != null)
                    {
                        var settings = settingsProp.GetValue(targetTracker);
                        if (settings != null)
                        {
                            followOffsetField = settings.GetType().GetField("FollowOffset", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            targetObject = settings;
                        }
                    }
                    
                    if (followOffsetField == null)
                    {
                        var settingsField = trackerType.GetField("Settings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (settingsField != null)
                        {
                            var settings = settingsField.GetValue(targetTracker);
                            if (settings != null)
                            {
                                followOffsetField = settings.GetType().GetField("FollowOffset", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                                if (followOffsetField != null)
                                {
                                    Vector3 newOffset = new Vector3(5f, 4f, 10f);
                                    followOffsetField.SetValue(settings, newOffset);
                                    settingsField.SetValue(targetTracker, settings);
                                    targetTrackerField.SetValue(cinemachineFollow, targetTracker);
                                }
                            }
                        }
                    }
                    else if (followOffsetField != null && targetObject != null)
                    {
                        Vector3 newOffset = new Vector3(5f, 4f, 10f);
                        followOffsetField.SetValue(targetObject, newOffset);
                        if (settingsProp != null && settingsProp.CanWrite)
                        {
                            settingsProp.SetValue(targetTracker, targetObject);
                        }
                        targetTrackerField.SetValue(cinemachineFollow, targetTracker);
                    }
                }
            }
        }
    }
    
    public int GetPlayerRank(NetworkObject playerObject)
    {
        if (playerObject == null || raceTrack == null) return 1;
        
        float playerProgress = GetVehicleProgress(playerObject);
        
        int rank = 1;
        
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject != null && client.Value.PlayerObject != playerObject)
            {
                float otherProgress = GetVehicleProgress(client.Value.PlayerObject);
                if (otherProgress > playerProgress)
                {
                    rank++;
                }
            }
        }
        
        AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
        foreach (var aiKart in aiKarts)
        {
            if (aiKart != null && aiKart.splinePath != null)
            {
                float aiProgress = aiKart.GetTotalProgress();
                if (aiProgress > playerProgress)
                {
                    rank++;
                }
            }
        }
        
        return rank;
    }
    
    private float GetVehicleProgress(NetworkObject vehicle)
    {
        if (vehicle == null) return 0f;
        
        PlayerProgressTracker playerTracker = vehicle.GetComponent<PlayerProgressTracker>();
        if (playerTracker == null)
        {
            playerTracker = vehicle.GetComponentInChildren<PlayerProgressTracker>();
        }
        
        if (playerTracker != null)
        {
            return playerTracker.GetTotalProgress();
        }
        
        AIKartController aiController = vehicle.GetComponent<AIKartController>();
        if (aiController == null)
        {
            aiController = vehicle.GetComponentInChildren<AIKartController>();
        }
        
        if (aiController != null && aiController.splinePath != null)
        {
            return aiController.GetTotalProgress();
        }
        
        return 0f;
    }
    
    public int GetTotalVehicleCount()
    {
        int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
        int aiCount = FindObjectsByType<AIKartController>(FindObjectsSortMode.None).Length;
        return playerCount + aiCount;
    }
    
}

