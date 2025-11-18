using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameUI : MonoBehaviour
{
    [Header("UI References")]
    public Button startRaceButton;
    public TextMeshProUGUI countdownText;
    public GameObject countdownPanel;
    
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
            }
        }
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

