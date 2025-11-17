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
    
    // Called when the object becomes enabled and active; wires up UI events and subscribes to LobbyManager events.
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
    
    // Called when the object becomes disabled or inactive; removes UI listeners and unsubscribes from events.
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
    
    // Updates the status text UI with the provided status string.
    void OnStatus(string s)
    {
        if (status) status.text = s;
    }
    
    // Displays the received join code in the UI and toggles relevant panels.
    void OnJoinCode(string code)
    {
        if (joinCodeText) joinCodeText.text = code;
        if (joinCodePanel) joinCodePanel.SetActive(true);
        if (controlsGroup) controlsGroup.SetActive(false);
    }
    
    // Hides the UI when a client (not host) has successfully started.
    void OnStarted()
    {
        if (Unity.Netcode.NetworkManager.Singleton.IsClient &&
            !Unity.Netcode.NetworkManager.Singleton.IsHost)
        {
            gameObject.SetActive(false);
        }
    }
}