using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;
using Unity.Netcode;

public class LeaderboardUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject leaderboardPanel;
    public Transform leaderboardContent;
    public GameObject leaderboardEntryPrefab;
    
    [Header("Update Settings")]
    public float updateInterval = 0.5f;
    
    private GameManager gameManager;
    private Dictionary<NetworkObject, GameObject> entryObjects = new Dictionary<NetworkObject, GameObject>();
    private bool isShowing = false;
    private float updateTimer = 0f;
    
    void Start()
    {
        gameManager = GameManager.Instance;
        
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
        
        SetupContentLayout();
    }
    
    private void SetupContentLayout()
    {
        if (leaderboardContent == null) return;
        
        VerticalLayoutGroup layoutGroup = leaderboardContent.GetComponent<VerticalLayoutGroup>();
        if (layoutGroup == null)
        {
            layoutGroup = leaderboardContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layoutGroup.spacing = 5f;
            layoutGroup.padding = new RectOffset(5, 5, 5, 5);
            layoutGroup.childAlignment = TextAnchor.UpperCenter;
            layoutGroup.childControlWidth = true;
            layoutGroup.childControlHeight = false;
            layoutGroup.childForceExpandWidth = true;
            layoutGroup.childForceExpandHeight = false;
        }
        
        ContentSizeFitter sizeFitter = leaderboardContent.GetComponent<ContentSizeFitter>();
        if (sizeFitter == null)
        {
            sizeFitter = leaderboardContent.gameObject.AddComponent<ContentSizeFitter>();
            sizeFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
    
    void Update()
    {
        if (gameManager == null)
        {
            gameManager = GameManager.Instance;
        }
        
        if (gameManager == null) return;
        
        updateTimer += Time.deltaTime;
        if (updateTimer >= updateInterval)
        {
            updateTimer = 0f;
            
            UpdateLeaderboard();
            
            if (gameManager.ShouldShowLeaderboard())
            {
                if (!isShowing)
                {
                    ShowLeaderboard();
                }
            }
            else
            {
                if (isShowing)
                {
                    HideLeaderboard();
                }
            }
        }
    }
    
    public void ShowLeaderboard()
    {
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(true);
            isShowing = true;
        }
    }
    
    public void HideLeaderboard()
    {
        if (leaderboardPanel != null)
        {
            leaderboardPanel.SetActive(false);
        }
        
        isShowing = false;
    }
    
    private void UpdateLeaderboard()
    {
        if (gameManager == null || leaderboardContent == null) return;
        
        List<LeaderboardEntry> entries = gameManager.GetAllPlayerRankings();
        
        if (entries == null || entries.Count == 0) return;
        
        int entriesToShow = entries.Count;
        
        while (leaderboardContent.childCount < entriesToShow)
        {
            if (leaderboardEntryPrefab != null)
            {
                GameObject newEntry = Instantiate(leaderboardEntryPrefab, leaderboardContent);
                newEntry.SetActive(true);
            }
        }
        
        for (int i = entriesToShow; i < leaderboardContent.childCount; i++)
        {
            leaderboardContent.GetChild(i).gameObject.SetActive(false);
        }
        
        for (int i = 0; i < entriesToShow; i++)
        {
            if (i >= entries.Count) break;
            
            var entry = entries[i];
            Transform entryTransform = leaderboardContent.GetChild(i);
            entryTransform.gameObject.SetActive(true);
            
            UpdateLeaderboardEntry(entryTransform, entry, i + 1);
        }
        
        LayoutRebuilder.ForceRebuildLayoutImmediate(leaderboardContent.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();
    }
    
    private void UpdateLeaderboardEntry(Transform entryTransform, LeaderboardEntry entry, int rank)
    {
        TextMeshProUGUI rankText = entryTransform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI nameText = entryTransform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI progressText = entryTransform.Find("ProgressText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI statusText = entryTransform.Find("StatusText")?.GetComponent<TextMeshProUGUI>();
        
        if (rankText == null)
        {
            TextMeshProUGUI[] texts = entryTransform.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length >= 1) rankText = texts[0];
            if (texts.Length >= 2) nameText = texts[1];
            if (texts.Length >= 3) progressText = texts[2];
            if (texts.Length >= 4) statusText = texts[3];
        }
        
        if (rankText != null)
        {
            rankText.text = rank.ToString();
        }
        
        if (nameText != null)
        {
            nameText.text = entry.playerName;
        }
        
        if (progressText != null)
        {
            if (entry.isFinished)
            {
                progressText.text = "Finished";
                if (progressText.color != Color.green)
                {
                    progressText.color = Color.green;
                }
            }
            else
            {
                int currentLap = entry.lapCount;
                int totalLaps = gameManager != null ? gameManager.totalLaps : 3;
                float progressPercent = (entry.progress / totalLaps) * 100f;
                progressText.text = $"{currentLap}/{totalLaps} ({progressPercent:F0}%)";
                
                if (progressText.color != Color.white)
                {
                    progressText.color = Color.white;
                }
            }
        }
        
        if (statusText != null)
        {
            if (entry.isFinished)
            {
                statusText.text = "✓";
                if (statusText.color != Color.green)
                {
                    statusText.color = Color.green;
                }
            }
            else
            {
                statusText.text = "...";
                if (statusText.color != Color.yellow)
                {
                    statusText.color = Color.yellow;
                }
            }
        }
    }
}

[System.Serializable]
public class LeaderboardEntry
{
    public NetworkObject networkObject;
    public string playerName;
    public int rank;
    public float progress;
    public int lapCount;
    public bool isFinished;
    public bool isPlayer;
    public bool isAI;
}
