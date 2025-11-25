using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Cinemachine;
using UnityEngine.UI;

public class GameManager : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject playerPrefab;
    public GameObject aiKartPrefab;
    
    [Header("Intro Sequence")]
    public GameObject introBannerUI;
    public Camera mainCamera;
    public Camera introCamera1;
    public Camera introCamera2;
    public float intro1Duration = 1f;
    public float intro2Duration = 1f;
    [Tooltip("Camera 1 movement direction (normalized). Default: negative Y axis (down)")]
    public Vector3 camera1MoveDirection = new Vector3(0f, -1f, 0f);
    [Tooltip("Camera 1 total movement distance")]
    public float camera1MoveDistance = 20f;
    [Tooltip("Camera 2 arc height (Y offset for the arc midpoint). Positive = upward arc")]
    public float camera2ArcHeight = 5f;
    [Tooltip("Camera 2 arc forward distance (how far forward the arc goes)")]
    public float camera2ArcForwardDistance = 10f;
    
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
    private NetworkVariable<bool> isPlayingIntro = new NetworkVariable<bool>(false);
    private NetworkVariable<float> countdownTime = new NetworkVariable<float>(0f);
    private NetworkVariable<bool> showLeaderboard = new NetworkVariable<bool>(false);
    
    private float rankingUpdateTimer = 0f;
    private HashSet<NetworkObject> finishedPlayers = new HashSet<NetworkObject>();
    private Dictionary<NetworkObject, int> finishOrder = new Dictionary<NetworkObject, int>();
    private int nextFinishOrder = 1;
    
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
            isPlayingIntro.Value = false;
            countdownTime.Value = 0f;
            showLeaderboard.Value = false;
            finishedPlayers.Clear();
            finishOrder.Clear();
            nextFinishOrder = 1;
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
        isPlayingIntro.Value = true;
        PlayIntroSequenceClientRpc();
        
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
        
        if (CharacterSelectionUI.Instance != null)
        {
            CharacterSelectionUI.Instance.HideSelectionUI();
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
            
            player.transform.position = spawnPos;
            player.transform.rotation = spawnRot;
            
            Rigidbody playerRb = player.GetComponentInChildren<Rigidbody>();
            if (playerRb != null)
            {
                playerRb.position = spawnPos;
                playerRb.rotation = spawnRot;
                playerRb.linearVelocity = Vector3.zero;
                playerRb.angularVelocity = Vector3.zero;
            }
            
            NetworkObject playerNetObj = player.GetComponent<NetworkObject>();
            if (playerNetObj != null)
            {
                player.transform.position = spawnPos;
                player.transform.rotation = spawnRot;
                if (playerRb != null)
                {
                    playerRb.position = spawnPos;
                    playerRb.rotation = spawnRot;
                    playerRb.linearVelocity = Vector3.zero;
                    playerRb.angularVelocity = Vector3.zero;
                }
                
                playerNetObj.SpawnAsPlayerObject(clientId);
                playerNetObj.ChangeOwnership(clientId);
                
                player.transform.position = spawnPos;
                player.transform.rotation = spawnRot;
                if (playerRb != null)
                {
                    playerRb.position = spawnPos;
                    playerRb.rotation = spawnRot;
                    playerRb.linearVelocity = Vector3.zero;
                    playerRb.angularVelocity = Vector3.zero;
                }
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
                
                aiController.SetStartPosition(0f);
            }
            
            spawnIndex++;
            yield return new WaitForSeconds(0.2f);
        }
        float startTime = Time.time;   
        float elapsedTime = Time.time - startTime;
        float totalIntroTime = intro1Duration + intro2Duration;
        
        if (elapsedTime < totalIntroTime)
        {
            yield return new WaitForSeconds(totalIntroTime - elapsedTime);
        }
        isPlayingIntro.Value = false;
        ActivatePlayerCamerasClientRpc();
        StartCountdownServerRpc();
    }
    
    [ClientRpc]
    private void ActivatePlayerCamerasClientRpc()
    {
        if (mainCamera != null)
        {
             Debug.Log($"[GameManager] Main Camera verified: {mainCamera.name}");
        }
        KartController localKart = GetLocalPlayerKart();
        CinemachineCamera activeCamera = localKart != null ? localKart.GetActiveDrivingCamera() : null;
        if (localKart != null && activeCamera != null)
        {
            activeCamera.gameObject.SetActive(false);
            activeCamera.enabled = false;
            activeCamera.gameObject.SetActive(true);
            activeCamera.enabled = true;
            Debug.Log($"[GameManager] Activated local player driving camera: {activeCamera.name}");
        }
        else
        {
            Debug.LogWarning("[GameManager] Could not find local player kart or driving camera in ActivatePlayerCamerasClientRpc!");
        }
    }
    
    [ClientRpc]
    private void PlayIntroSequenceClientRpc()
    {
        Debug.Log($"[GameManager] PlayIntroSequenceClientRpc called on client. introCamera1: {introCamera1}, introCamera2: {introCamera2}");
        if (introCamera1 == null)
        {
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera cam in allCameras)
            {
                if (cam.name.Contains("IntroCamera1") || cam.name.Contains("Intro Camera 1") || cam.name.Contains("Camera1"))
                {
                    introCamera1 = cam;
                    Debug.Log($"[GameManager] Found introCamera1 by name: {cam.name}");
                    break;
                }
            }
        }
        
        if (introCamera2 == null)
        {
            Camera[] allCameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera cam in allCameras)
            {
                if (cam.name.Contains("IntroCamera2") || cam.name.Contains("Intro Camera 2") || cam.name.Contains("Camera2"))
                {
                    introCamera2 = cam;
                    Debug.Log($"[GameManager] Found introCamera2 by name: {cam.name}");
                    break;
                }
            }
        }
        
        StartCoroutine(IntroSequenceCoroutine());
    }
    
    private IEnumerator IntroSequenceCoroutine()
    {
        Debug.Log("[GameManager] IntroSequenceCoroutine started on client");
        
        // 1. Show Banner
        if (introBannerUI != null) introBannerUI.SetActive(true);
        else Debug.LogWarning("[GameManager] introBannerUI is null!");
        KartController localKart = null;
        float waitTime = 0f;
        float maxWaitTime = 5f;
        while (localKart == null && waitTime < maxWaitTime)
        {
            localKart = GetLocalPlayerKart();
            if (localKart == null)
            {
                yield return new WaitForSeconds(0.1f);
                waitTime += 0.1f;
            }
        }
        
        if (localKart != null)
        {
            CinemachineCamera localActiveCamera = localKart.GetActiveDrivingCamera();
            if (localActiveCamera != null)
            {
                localActiveCamera.gameObject.SetActive(false);
            }
        }
        else
        {
            KartController[] allKarts = FindObjectsByType<KartController>(FindObjectsSortMode.None);
            foreach (KartController kart in allKarts)
            {
                CinemachineCamera kartCamera = kart.GetActiveDrivingCamera();
                if (kartCamera != null && kartCamera.gameObject.activeInHierarchy)
                {
                    NetworkObject kartNetObj = kart.GetComponentInParent<NetworkObject>();
                    if (kartNetObj != null && kartNetObj.IsOwner)
                    {
                        kartCamera.gameObject.SetActive(false);
                    }
                }
            }
        }
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null)
            {
                GameObject mainCamObj = GameObject.FindGameObjectWithTag("MainCamera");
                if (mainCamObj != null)
                {
                    mainCamera = mainCamObj.GetComponent<Camera>();
                }
            }
        }
        if (introCamera1 == null)
        {
            yield break;
        }
        
        if (introCamera2 == null)
        {
            yield break;
        }
        if (introCamera1 != null)
        {
            introCamera1.gameObject.SetActive(true);
            introCamera1.depth = 100; 
            
            float timer = 0f;
            Vector3 startPos = introCamera1.transform.position;
            Vector3 normalizedDirection = camera1MoveDirection.normalized;
            
            Debug.Log($"[GameManager] Camera1 Start Position: {startPos}, Move Direction: {normalizedDirection}, Distance: {camera1MoveDistance}");
            
            while (timer < intro1Duration)
            {
                float progress = timer / intro1Duration;
                introCamera1.transform.position = startPos + normalizedDirection * (camera1MoveDistance * progress);
                
                timer += Time.deltaTime;
                yield return null;
            }
            introCamera1.transform.position = startPos + normalizedDirection * camera1MoveDistance;
            Debug.Log($"[GameManager] Camera1 Final Position: {introCamera1.transform.position}");
            introCamera1.gameObject.SetActive(false);
            introCamera1.depth = -1; // Reset depth
        }
        if (introCamera2 != null)
        {
            introCamera2.gameObject.SetActive(true);
            introCamera2.depth = 100;
            
            float timer = 0f;
            Vector3 startPos = introCamera2.transform.position;
            Quaternion startRot = introCamera2.transform.rotation;
            Vector3 endPos = startPos + introCamera2.transform.forward * camera2ArcForwardDistance;
            Vector3 midPoint = (startPos + endPos) * 0.5f;
            midPoint.y += camera2ArcHeight;
            Debug.Log($"[GameManager] Camera2 Arc: Start: {startPos}, End: {endPos}, Mid: {midPoint}, Arc Height: {camera2ArcHeight}");
            
            while (timer < intro2Duration)
            {
                float progress = timer / intro2Duration;
                Vector3 currentPos = (1f - progress) * (1f - progress) * startPos +2f * (1f - progress) * progress * midPoint +progress * progress * endPos;
                introCamera2.transform.position = currentPos;
                introCamera2.transform.rotation = startRot;
                
                timer += Time.deltaTime;
                yield return null;
            }
            introCamera2.transform.position = endPos;
            introCamera2.transform.rotation = startRot;
            
            Debug.Log($"[GameManager] Camera2 Final Position: {introCamera2.transform.position}");
            introCamera2.gameObject.SetActive(false);
            introCamera2.depth = -1;
        }
        if (introBannerUI != null) introBannerUI.SetActive(false);
        Debug.Log("[GameManager] Intro Sequence Finished locally.");
        localKart = GetLocalPlayerKart();
        CinemachineCamera activeCamera = localKart != null ? localKart.GetActiveDrivingCamera() : null;
        if (localKart != null && activeCamera != null)
        {
            // Force toggle: disable first, then enable
            activeCamera.gameObject.SetActive(false);
            activeCamera.enabled = false;
            yield return null; // Wait one frame
            activeCamera.gameObject.SetActive(true);
            activeCamera.enabled = true;
            Debug.Log($"[GameManager] Re-enabled local player driving camera: {activeCamera.name}");
        }
        else
        {
            Debug.LogWarning("[GameManager] Could not find local player kart or driving camera!");
        }
    }
    
    private KartController GetLocalPlayerKart()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            NetworkObject localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (localPlayer != null)
            {
                return localPlayer.GetComponentInChildren<KartController>();
            }
        }
        return null;
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
        NetworkObject localPlayerObject = NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObject != null)
        {
            UnityEngine.InputSystem.PlayerInput playerInput = localPlayerObject.GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = true;
                playerInput.ActivateInput();
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
    
    public bool IsPlayingIntro()
    {
        return isPlayingIntro.Value;
    }
    
    public bool IsGameFinished()
    {
        return gameFinished.Value;
    }
    
    void Update()
    {
        if (IsServer && gameStarted.Value && countdownTime.Value <= 0f)
        {
            rankingUpdateTimer += Time.deltaTime;
            if (rankingUpdateTimer >= rankingUpdateInterval)
            {
                rankingUpdateTimer = 0f;
                UpdateRankings();
                
                if (!gameFinished.Value)
                {
                    CheckRaceFinish();
                }
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
        bool foundNewFinisher = false;
        
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
                    NetworkObject playerObj = client.Value.PlayerObject;
                    if (!finishedPlayers.Contains(playerObj))
                    {
                        finishedPlayers.Add(playerObj);
                        finishOrder[playerObj] = nextFinishOrder++;
                        foundNewFinisher = true;
                        if (winnerTransform == null)
                        {
                            winnerTransform = playerObj.transform;
                        }
                    }
                }
            }
        }
        
        AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
        foreach (var aiKart in aiKarts)
        {
            if (aiKart != null && aiKart.GetLapCount() >= totalLaps)
            {
                NetworkObject aiObj = aiKart.GetComponent<NetworkObject>();
                if (aiObj != null && !finishedPlayers.Contains(aiObj))
                {
                    finishedPlayers.Add(aiObj);
                    finishOrder[aiObj] = nextFinishOrder++;
                    foundNewFinisher = true;
                    if (winnerTransform == null)
                    {
                        winnerTransform = aiKart.transform;
                    }
                }
            }
        }
        
        if (foundNewFinisher)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                if (client.Value.PlayerObject != null && finishedPlayers.Contains(client.Value.PlayerObject))
                {
                    ShowLeaderboardToClientClientRpc(new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { client.Key } } });
                }
            }
            
            int totalVehicles = GetTotalVehicleCount();
            if (finishedPlayers.Count >= totalVehicles)
            {
                if (winnerTransform != null)
                {
                    FinishRace(winnerTransform);
                }
            }
        }
    }
    
    private void FinishRace(Transform winner)
    {
        if (gameFinished.Value) return;
        
        gameFinished.Value = true;
        FocusAllCamerasOnWinnerClientRpc(winner.GetComponent<NetworkObject>());
        
        Debug.Log("Race finished! All players completed the race.");
    }
    
    [ClientRpc]
    private void FocusAllCamerasOnWinnerClientRpc(NetworkObjectReference winnerRef)
    {
        if (!winnerRef.TryGet(out NetworkObject winnerObj) || winnerObj == null) return;
        
        Debug.Log($"[GameManager] Player finished race: {winnerObj.name}");
    }
    
    public int GetPlayerRank(NetworkObject playerObject)
    {
        if (playerObject == null) return 1;
        
        List<LeaderboardEntry> entries = GetAllPlayerRankings();
        
        if (entries == null || entries.Count == 0) return 1;
        
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].networkObject != null && entries[i].networkObject == playerObject)
            {
                return entries[i].rank;
            }
        }
        
        return entries.Count;
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
        List<LeaderboardEntry> entries = GetAllPlayerRankings();
        return entries.Count;
    }
    
    public List<LeaderboardEntry> GetAllPlayerRankings()
    {
        List<LeaderboardEntry> entries = new List<LeaderboardEntry>();
        HashSet<NetworkObject> addedObjects = new HashSet<NetworkObject>();
        
        if (NetworkManager.Singleton == null) return entries;
        
        foreach (var client in NetworkManager.Singleton.ConnectedClients)
        {
            if (client.Value.PlayerObject != null)
            {
                NetworkObject playerObj = client.Value.PlayerObject;
                if (playerObj == null || !playerObj.IsSpawned) continue;
                if (addedObjects.Contains(playerObj)) continue;
                
                PlayerProgressTracker tracker = playerObj.GetComponent<PlayerProgressTracker>();
                if (tracker == null)
                {
                    tracker = playerObj.GetComponentInChildren<PlayerProgressTracker>();
                }
                
                if (tracker != null)
                {
                    float progress = tracker.GetTotalProgress();
                    int lapCount = tracker.GetLapCount();
                    
                    if (float.IsNaN(progress) || float.IsInfinity(progress))
                    {
                        progress = 0f;
                    }
                    
                    LeaderboardEntry entry = new LeaderboardEntry
                    {
                        networkObject = playerObj,
                        playerName = GetPlayerName(playerObj),
                        progress = progress,
                        lapCount = lapCount,
                        isFinished = finishedPlayers.Contains(playerObj),
                        isPlayer = true,
                        isAI = false
                    };
                    entries.Add(entry);
                    addedObjects.Add(playerObj);
                }
            }
        }
        
        AIKartController[] aiKarts = FindObjectsByType<AIKartController>(FindObjectsSortMode.None);
        foreach (var aiKart in aiKarts)
        {
            if (aiKart != null)
            {
                NetworkObject aiObj = aiKart.GetComponent<NetworkObject>();
                if (aiObj == null)
                {
                    aiObj = aiKart.GetComponentInParent<NetworkObject>();
                }
                if (aiObj == null)
                {
                    aiObj = aiKart.GetComponentInChildren<NetworkObject>();
                }
                
                if (aiObj != null && aiObj.IsSpawned && !addedObjects.Contains(aiObj))
                {
                    float progress = aiKart.GetTotalProgress();
                    int lapCount = aiKart.GetLapCount();
                    
                    if (float.IsNaN(progress) || float.IsInfinity(progress))
                    {
                        progress = 0f;
                    }
                    
                    LeaderboardEntry entry = new LeaderboardEntry
                    {
                        networkObject = aiObj,
                        playerName = GetAIName(aiKart),
                        progress = progress,
                        lapCount = lapCount,
                        isFinished = finishedPlayers.Contains(aiObj),
                        isPlayer = false,
                        isAI = true
                    };
                    entries.Add(entry);
                    addedObjects.Add(aiObj);
                }
            }
        }
        
        if (entries.Count == 0) return entries;
        
        entries.Sort((a, b) =>
        {
            if (a.isFinished != b.isFinished)
            {
                return b.isFinished.CompareTo(a.isFinished);
            }
            
            if (a.isFinished && b.isFinished)
            {
                int orderA = finishOrder.ContainsKey(a.networkObject) ? finishOrder[a.networkObject] : int.MaxValue;
                int orderB = finishOrder.ContainsKey(b.networkObject) ? finishOrder[b.networkObject] : int.MaxValue;
                return orderA.CompareTo(orderB);
            }
            
            if (a.lapCount != b.lapCount)
            {
                return b.lapCount.CompareTo(a.lapCount);
            }
            
            float progressDiff = b.progress - a.progress;
            if (Mathf.Abs(progressDiff) > 0.0001f)
            {
                return progressDiff > 0 ? 1 : -1;
            }
            
            if (a.networkObject != null && b.networkObject != null)
            {
                return a.networkObject.NetworkObjectId.CompareTo(b.networkObject.NetworkObjectId);
            }
            
            return 0;
        });
        
        for (int i = 0; i < entries.Count; i++)
        {
            entries[i].rank = i + 1;
        }
        
        return entries;
    }
    
    private string GetPlayerName(NetworkObject playerObj)
    {
        string name = playerObj.name;
        
        if (name.Contains("(Clone)"))
        {
            name = name.Replace("(Clone)", "").Trim();
        }
        
        if (playerObj.IsOwner)
        {
            name = "Player " + name;
        }
        
        return name;
    }
    
    private string GetAIName(AIKartController aiKart)
    {
        string name = aiKart.name;
        
        if (name.Contains("(Clone)"))
        {
            name = name.Replace("(Clone)", "").Trim();
        }
        
        return name;
    }
    
    [ClientRpc]
    private void ShowLeaderboardToClientClientRpc(ClientRpcParams rpcParams = default)
    {
        LeaderboardUI leaderboardUI = FindFirstObjectByType<LeaderboardUI>();
        if (leaderboardUI != null)
        {
            leaderboardUI.ShowLeaderboard();
        }
    }
    
    public bool ShouldShowLeaderboard()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsClient) return false;
        
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            PlayerProgressTracker tracker = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerProgressTracker>();
            if (tracker == null)
            {
                tracker = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponentInChildren<PlayerProgressTracker>();
            }
            
            if (tracker != null)
            {
                return tracker.GetLapCount() >= totalLaps;
            }
        }
        
        return false;
    }
    
}
