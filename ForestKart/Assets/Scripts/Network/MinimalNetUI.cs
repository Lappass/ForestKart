using UnityEngine;
using UnityEngine.UI;
using TMPro;
public class MinimalNetUI : MonoBehaviour
{
    [Header("Groups")]
    public GameObject controlsGroup;
    public GameObject joinCodePanel;
    public LobbyManager mgr;
    public Button hostBtn;
    public TMP_InputField joinInput;
    public Button joinBtn;
    public TMP_Text status;
    public TMP_Text joinCodeText;
    bool subscribed;
    public Button quickJoinBtn;
    public Button startRaceButton;
    
    private GameUI gameUI;
    
    void Start()
    {
        gameUI = FindFirstObjectByType<GameUI>();
    }
    
    void OnEnable()
    {
        if (mgr == null) return;
        if (!subscribed)
        {
            hostBtn.onClick.AddListener(() => mgr.HostAsync());
            joinBtn.onClick.AddListener(() => mgr.JoinAsync(joinInput.text));
            mgr.OnStatus += OnStatus;
            mgr.OnJoinCode += OnJoinCode;
            mgr.OnStarted += OnStarted;
            subscribed = true;
        }
    }
    
    void OnDisable()
    {
        if (mgr == null || !subscribed) return;
        hostBtn.onClick.RemoveAllListeners();
        joinBtn.onClick.RemoveAllListeners();
        mgr.OnStatus -= OnStatus;
        mgr.OnJoinCode -= OnJoinCode;
        mgr.OnStarted -= OnStarted;
        subscribed = false;
    }
    
    void OnStatus(string s)
    {
        if (status) status.text = s;
    }
    
    void OnJoinCode(string code)
    {
        if (joinCodeText) joinCodeText.text = code;
        if (joinCodePanel) joinCodePanel.SetActive(true);
        if (controlsGroup) controlsGroup.SetActive(false);
    }
    
    void OnStarted()
    {
        if (Unity.Netcode.NetworkManager.Singleton.IsHost)
        {
            if (gameUI != null && startRaceButton != null)
            {
                gameUI.ShowStartButton(true);
            }
        }
    }
}