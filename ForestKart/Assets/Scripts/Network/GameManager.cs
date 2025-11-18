using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Splines;

public class GameManager : NetworkBehaviour
{
    [Header("Prefabs")]
    public GameObject playerPrefab;
    public GameObject aiKartPrefab;
    
    [Header("Spawn Settings")]
    public SplineContainer raceTrack;
    public int aiKartCount = 3;
    public float spawnSpacing = 3f;
    
    [Header("Countdown")]
    public float countdownDuration = 3f;
    
    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(false);
    private NetworkVariable<float> countdownTime = new NetworkVariable<float>(0f);
    
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
        
        float splineLength = raceTrack.Spline.GetLength();
        Vector3 startPosition = raceTrack.transform.TransformPoint(
            SplineUtility.EvaluatePosition(raceTrack.Spline, 0f)
        );
        Vector3 startDirection = SplineUtility.EvaluateTangent(raceTrack.Spline, 0f);
        Quaternion startRotation = Quaternion.LookRotation(startDirection);
        
        int spawnIndex = 0;
        
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            Vector3 spawnPos = startPosition + Vector3.right * spawnIndex * spawnSpacing;
            GameObject player = Instantiate(playerPrefab, spawnPos, startRotation);
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
            Vector3 spawnPos = startPosition + Vector3.right * (spawnIndex + i) * spawnSpacing;
            GameObject aiKart = Instantiate(aiKartPrefab, spawnPos, startRotation);
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
    
    public float GetCountdownTime()
    {
        return countdownTime.Value;
    }
    
    public bool IsGameStarted()
    {
        return gameStarted.Value;
    }
    
}

