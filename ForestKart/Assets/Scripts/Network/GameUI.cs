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
    }
    
    void Update()
    {
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
            }
        }
        else
        {
            if (rankingText != null)
            {
                rankingText.text = "";
            }
        }
    }
    
    private void UpdateRankingDisplay()
    {
        if (gameManager == null || rankingText == null) return;
        
        Unity.Netcode.NetworkObject localPlayerObject = Unity.Netcode.NetworkManager.Singleton.LocalClient?.PlayerObject;
        if (localPlayerObject == null) return;
        
        int rank = gameManager.GetPlayerRank(localPlayerObject);
        int totalVehicles = gameManager.GetTotalVehicleCount();
        
        rankingText.text = $"{rank}/{totalVehicles}";
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

