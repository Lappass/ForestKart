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
    private PlayerAvatarRenderer avatarRenderer;
    private Dictionary<NetworkObject, GameObject> entryObjects = new Dictionary<NetworkObject, GameObject>();
    private bool isShowing = false;
    private float updateTimer = 0f;
    
    void Start()
    {
        gameManager = GameManager.Instance;
        avatarRenderer = PlayerAvatarRenderer.Instance;
        
        if (avatarRenderer == null)
        {
            if (gameManager != null)
            {
                avatarRenderer = gameManager.gameObject.AddComponent<PlayerAvatarRenderer>();
            }
            else
            {
                GameObject avatarRendererObj = new GameObject("PlayerAvatarRenderer");
                avatarRenderer = avatarRendererObj.AddComponent<PlayerAvatarRenderer>();
            }
        }
        
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
        RawImage avatarImage = entryTransform.Find("AvatarImage")?.GetComponent<RawImage>();
        TextMeshProUGUI nameText = entryTransform.Find("NameText")?.GetComponent<TextMeshProUGUI>();
        TextMeshProUGUI rankText = entryTransform.Find("RankText")?.GetComponent<TextMeshProUGUI>();
        
        if (avatarImage == null)
        {
            avatarImage = entryTransform.GetComponentInChildren<RawImage>();
        }
        
        if (nameText == null || rankText == null)
        {
            TextMeshProUGUI[] texts = entryTransform.GetComponentsInChildren<TextMeshProUGUI>();
            
            if (texts.Length == 1)
            {
                if (nameText == null)
                {
                    nameText = texts[0];
                }
            }
            else if (texts.Length >= 2)
            {
                if (nameText == null && rankText == null)
                {
                    TextMeshProUGUI rightmostText = texts[0];
                    float rightmostX = texts[0].rectTransform.anchoredPosition.x;
                    
                    for (int i = 1; i < texts.Length; i++)
                    {
                        float x = texts[i].rectTransform.anchoredPosition.x;
                        if (x > rightmostX)
                        {
                            rightmostX = x;
                            rightmostText = texts[i];
                        }
                    }
                    
                    rankText = rightmostText;
                    
                    TextMeshProUGUI leftmostText = null;
                    float leftmostX = float.MaxValue;
                    
                    for (int i = 0; i < texts.Length; i++)
                    {
                        if (texts[i] != rightmostText)
                        {
                            float x = texts[i].rectTransform.anchoredPosition.x;
                            if (x < leftmostX)
                            {
                                leftmostX = x;
                                leftmostText = texts[i];
                            }
                        }
                    }
                    
                    nameText = leftmostText;
                }
                else if (nameText == null)
                {
                    foreach (var text in texts)
                    {
                        if (text != rankText)
                        {
                            nameText = text;
                            break;
                        }
                    }
                }
                else if (rankText == null)
                {
                    foreach (var text in texts)
                    {
                        if (text != nameText)
                        {
                            rankText = text;
                            break;
                        }
                    }
                }
            }
        }
        
        if (avatarImage != null && avatarRenderer != null && entry.networkObject != null)
        {
            RenderTexture avatarRenderTexture = avatarRenderer.GetPlayerAvatar(entry.networkObject, addToUpdateList: true);
            if (avatarRenderTexture != null)
            {
                avatarImage.texture = avatarRenderTexture;
            }
        }
        
        if (nameText != null)
        {
            nameText.text = entry.playerName;
        }
        
        if (rankText != null)
        {
            rankText.text = rank.ToString();
        }
        else
        {
            TextMeshProUGUI[] allTexts = entryTransform.GetComponentsInChildren<TextMeshProUGUI>();
            if (allTexts.Length > 0)
            {
                allTexts[allTexts.Length - 1].text = rank.ToString();
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
