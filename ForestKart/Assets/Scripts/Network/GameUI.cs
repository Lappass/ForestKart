using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("UI References")]
    public Button startRaceButton;
    public TextMeshProUGUI countdownText;
    public GameObject countdownPanel;
    
    [Header("Ranking UI")]
    public TextMeshProUGUI rankingText;
    
    [Header("Lap UI")]
    public TextMeshProUGUI lapText;
    
    private GameManager gameManager;
    
    void Start()
    {
        gameManager = GameManager.Instance;
        
        if (startRaceButton != null)
        {
            startRaceButton.onClick.AddListener(OnStartRaceClicked);
        }
        
        if (countdownPanel != null)
        {
            countdownPanel.SetActive(false);
        }
        
        if (countdownText != null)
        {
            countdownText.text = "";
        }
        
        if (rankingText != null)
        {
            rankingText.text = "";
        }
        
        if (lapText != null)
        {
            lapText.text = "";
        }
    }
    
    void Update()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
        
        if (gameManager != null && gameManager.IsGameStarted())
        {
            float countdown = gameManager.GetCountdownTime();
            
            if (countdown > 0f)
            {
                if (countdownPanel != null && !countdownPanel.activeSelf)
                {
                    countdownPanel.SetActive(true);
                }
                
                if (startRaceButton != null)
                {
                    startRaceButton.gameObject.SetActive(false);
                }
                
                int countdownInt = Mathf.CeilToInt(countdown);
                if (countdownText != null)
                {
                    countdownText.text = countdownInt.ToString();
                }
                
                if (rankingText != null)
                {
                    rankingText.text = "";
                }
            }
            else
            {
                if (countdownPanel != null)
                {
                    countdownPanel.SetActive(false);
                }
                
                if (countdownText != null)
                {
                    countdownText.text = "";
                }
                
                UpdateRankingDisplay();
                UpdateLapDisplay();
            }
        }
        else
        {
            if (rankingText != null)
            {
                rankingText.text = "-/-";
            }
            
            if (lapText != null)
            {
                lapText.text = "0/3";
            }
        }
    }
    
    private void UpdateRankingDisplay()
    {
        if (gameManager == null || rankingText == null) return;
        
        Unity.Netcode.NetworkObject localPlayerObject = GetLocalPlayerObject();
        if (localPlayerObject == null)
        {
            rankingText.text = "-/-";
            return;
        }
        
        try
        {
            int rank = gameManager.GetPlayerRank(localPlayerObject);
            int totalVehicles = gameManager.GetTotalVehicleCount();
            
            rankingText.text = $"{rank}/{totalVehicles}";
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[GameUI] Error updating ranking display: {e.Message}");
            rankingText.text = "-/-";
        }
    }
    
    private void UpdateLapDisplay()
    {
        if (gameManager == null || lapText == null) return;
        
        Unity.Netcode.NetworkObject localPlayerObject = GetLocalPlayerObject();
        if (localPlayerObject == null)
        {
            lapText.text = "0/3";
            return;
        }
        
        PlayerProgressTracker tracker = localPlayerObject.GetComponent<PlayerProgressTracker>();
        if (tracker == null)
        {
            tracker = localPlayerObject.GetComponentInChildren<PlayerProgressTracker>();
        }
        
        if (tracker != null)
        {
            try
            {
                int currentLap = tracker.GetLapCount();
                int totalLaps = gameManager.totalLaps;
                
                lapText.text = $"{currentLap}/{totalLaps}";
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GameUI] Error updating lap display: {e.Message}");
                lapText.text = "0/3";
            }
        }
        else
        {
            lapText.text = "0/3";
        }
    }
    
    private Unity.Netcode.NetworkObject GetLocalPlayerObject()
    {
        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsClient) return null;
        
        if (networkManager.LocalClient != null && networkManager.LocalClient.PlayerObject != null)
        {
            return networkManager.LocalClient.PlayerObject;
        }
        
        if (networkManager.ConnectedClients.ContainsKey(networkManager.LocalClientId))
        {
            var localClient = networkManager.ConnectedClients[networkManager.LocalClientId];
            if (localClient != null && localClient.PlayerObject != null)
            {
                return localClient.PlayerObject;
            }
        }
        
        foreach (var client in networkManager.ConnectedClients)
        {
            if (client.Value.ClientId == networkManager.LocalClientId && client.Value.PlayerObject != null)
            {
                return client.Value.PlayerObject;
            }
        }
        
        Unity.Netcode.NetworkObject[] allNetworkObjects = FindObjectsByType<Unity.Netcode.NetworkObject>(FindObjectsSortMode.None);
        foreach (var netObj in allNetworkObjects)
        {
            if (netObj != null && netObj.IsSpawned && netObj.IsOwner)
            {
                PlayerProgressTracker tracker = netObj.GetComponent<PlayerProgressTracker>();
                if (tracker == null)
                {
                    tracker = netObj.GetComponentInChildren<PlayerProgressTracker>();
                }
                
                if (tracker != null)
                {
                    return netObj;
                }
            }
        }
        
        return null;
    }
    
    private void OnStartRaceClicked()
    {
        if (gameManager != null)
        {
            gameManager.StartRaceServerRpc();
        }
    }
    
    public void ShowStartButton(bool show)
    {
        if (startRaceButton != null)
        {
            startRaceButton.gameObject.SetActive(show);
        }
    }
}
//why not workling why
