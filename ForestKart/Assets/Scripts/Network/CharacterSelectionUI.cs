using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectionUI : NetworkBehaviour
{
    [Header("UI References")]
    public GameObject characterSelectionPanel;
    public Button leftButton;
    public Button rightButton;
    public Button selectButton;
    public Transform characterDisplayParent;
    
    [Header("Character Prefabs")]
    public GameObject[] characterPrefabs;
    public GameObject[] aiCharacterPrefabs;
    
    [Header("Display Settings")]
    public float rotationSpeed = 30f;
    
    private int currentCharacterIndex = 0;
    private GameObject currentDisplayCharacter;
    
    public struct PlayerSelection : INetworkSerializable, IEquatable<PlayerSelection>
    {
        public ulong clientId;
        public int characterIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref clientId);
            serializer.SerializeValue(ref characterIndex);
        }

        public bool Equals(PlayerSelection other)
        {
            return clientId == other.clientId && characterIndex == other.characterIndex;
        }
    }

    private NetworkList<PlayerSelection> playerSelections;
    
    private NetworkList<ulong> readyPlayers;
    
    private NetworkVariable<bool> selectionActive = new NetworkVariable<bool>(false);
    
    private bool isLocalPlayerReady = false;
    
    public static CharacterSelectionUI Instance { get; private set; }
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        
        playerSelections = new NetworkList<PlayerSelection>();
        readyPlayers = new NetworkList<ulong>();
    }
    
    void Start()
    {
        if (leftButton != null)
        {
            leftButton.onClick.AddListener(OnLeftButtonClicked);
        }
        
        if (rightButton != null)
        {
            rightButton.onClick.AddListener(OnRightButtonClicked);
        }
        
        if (selectButton != null)
        {
            selectButton.onClick.AddListener(OnSelectButtonClicked);
        }
        
        if (characterSelectionPanel != null)
        {
            characterSelectionPanel.SetActive(false);
        }
        
        if (characterDisplayParent != null)
        {
            characterDisplayParent.gameObject.SetActive(false);
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            selectionActive.Value = true;
            
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
        
        readyPlayers.OnListChanged += OnReadyPlayersChanged;
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        
        readyPlayers.OnListChanged -= OnReadyPlayersChanged;
    }
    
    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = readyPlayers.Count - 1; i >= 0; i--)
        {
            if (readyPlayers[i] == clientId)
            {
                readyPlayers.RemoveAt(i);
                Debug.Log($"[CharacterSelectionUI] Removed disconnected player {clientId} from ready list");
                break;
            }
        }
        
        for (int i = playerSelections.Count - 1; i >= 0; i--)
        {
            if (playerSelections[i].clientId == clientId)
            {
                playerSelections.RemoveAt(i);
                Debug.Log($"[CharacterSelectionUI] Removed disconnected player {clientId} from selections");
                break;
            }
        }
        
        UpdateStartButtonVisibility();
    }
    
    private void OnReadyPlayersChanged(NetworkListEvent<ulong> changeEvent)
    {
        UpdateStartButtonVisibility();
    }
    
    
    void OnEnable()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            StartCoroutine(DelayedShowSelection());
        }
    }
    
    private System.Collections.IEnumerator DelayedShowSelection()
    {
        yield return new WaitForSeconds(0.5f);
        
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient && 
            GameManager.Instance != null && !GameManager.Instance.IsGameStarted())
        {
            ShowSelectionUI();
        }
    }
    
    
    public void ShowSelectionUI()
    {
        if (characterSelectionPanel != null)
        {
            characterSelectionPanel.SetActive(true);
        }
        
        bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        
        if (characterDisplayParent != null)
        {
            characterDisplayParent.gameObject.SetActive(isConnected);
        }
        
        isLocalPlayerReady = false;
        currentCharacterIndex = 0;
        
        if (isConnected && characterPrefabs != null && characterPrefabs.Length > 0)
        {
            ShowCharacter(currentCharacterIndex);
        }
        
        if (leftButton != null) leftButton.interactable = true;
        if (rightButton != null) rightButton.interactable = true;
        if (selectButton != null) selectButton.interactable = true;
    }
    
    public void HideSelectionUI()
    {
        if (characterSelectionPanel != null)
        {
            characterSelectionPanel.SetActive(false);
        }
        
        if (currentDisplayCharacter != null)
        {
            Destroy(currentDisplayCharacter);
            currentDisplayCharacter = null;
        }
    }
    
    private void OnLeftButtonClicked()
    {
        if (isLocalPlayerReady) return;
        
        if (characterPrefabs == null || characterPrefabs.Length == 0) return;
        
        currentCharacterIndex--;
        if (currentCharacterIndex < 0)
        {
            currentCharacterIndex = characterPrefabs.Length - 1;
        }
        
        ShowCharacter(currentCharacterIndex);
    }
    
    private void OnRightButtonClicked()
    {
        if (isLocalPlayerReady) return;
        
        if (characterPrefabs == null || characterPrefabs.Length == 0) return;
        
        currentCharacterIndex++;
        if (currentCharacterIndex >= characterPrefabs.Length)
        {
            currentCharacterIndex = 0;
        }
        
        ShowCharacter(currentCharacterIndex);
    }
    
    private void OnSelectButtonClicked()
    {
        if (characterPrefabs == null || characterPrefabs.Length == 0) return;
        if (isLocalPlayerReady) return;
        
        SubmitSelectionServerRpc(currentCharacterIndex);
        
        MarkPlayerReadyServerRpc();
        
        isLocalPlayerReady = true;
        
        if (leftButton != null) leftButton.interactable = false;
        if (rightButton != null) rightButton.interactable = false;
        if (selectButton != null) selectButton.interactable = false;
    }
    
    private void ShowCharacter(int index)
    {
        if (characterPrefabs == null || index < 0 || index >= characterPrefabs.Length) return;
        if (characterDisplayParent == null) return;
        
        bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
        if (!isConnected)
        {
            if (characterDisplayParent != null)
            {
                characterDisplayParent.gameObject.SetActive(false);
            }
            return;
        }
        
        if (characterDisplayParent != null)
        {
            characterDisplayParent.gameObject.SetActive(true);
        }
        
        if (currentDisplayCharacter != null)
        {
            Destroy(currentDisplayCharacter);
        }
        
        GameObject prefab = characterPrefabs[index];
        if (prefab != null)
        {
            currentDisplayCharacter = Instantiate(prefab, characterDisplayParent);
            currentDisplayCharacter.transform.localPosition = Vector3.zero;
            currentDisplayCharacter.transform.localRotation = Quaternion.identity;
            currentDisplayCharacter.transform.localScale = Vector3.one;
            
            NetworkObject netObj = currentDisplayCharacter.GetComponent<NetworkObject>();
            if (netObj != null)
            {
                Destroy(netObj);
            }
            
            KartController kartController = currentDisplayCharacter.GetComponentInChildren<KartController>();
            if (kartController != null)
            {
                kartController.enabled = false;
            }
            
            Rigidbody rb = currentDisplayCharacter.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
            }
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void SubmitSelectionServerRpc(int characterIndex, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        int existingIndex = -1;
        for (int i = 0; i < playerSelections.Count; i++)
        {
            if (playerSelections[i].clientId == clientId)
            {
                existingIndex = i;
                break;
            }
        }
        
        PlayerSelection newSelection = new PlayerSelection
        {
            clientId = clientId,
            characterIndex = characterIndex
        };
        
        if (existingIndex >= 0)
        {
            playerSelections[existingIndex] = newSelection;
        }
        else
        {
            playerSelections.Add(newSelection);
        }
        
        Debug.Log($"[CharacterSelectionUI] Player {clientId} selected character index {characterIndex}");
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void MarkPlayerReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        
        bool alreadyReady = false;
        foreach (var readyId in readyPlayers)
        {
            if (readyId == clientId)
            {
                alreadyReady = true;
                break;
            }
        }
        
        if (!alreadyReady)
        {
            readyPlayers.Add(clientId);
            Debug.Log($"[CharacterSelectionUI] Player {clientId} is now ready");
        }
    }
    
    public bool IsAllPlayersReady()
    {
        if (!IsSpawned) return false;
        
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer) return false;
        
        var connectedClientIds = networkManager.ConnectedClientsIds;
        if (connectedClientIds.Count == 0) return false;
        
        foreach (var clientId in connectedClientIds)
        {
            bool isReady = false;
            foreach (var readyId in readyPlayers)
            {
                if (readyId == clientId)
                {
                    isReady = true;
                    break;
                }
            }
            
            if (!isReady)
            {
                return false;
            }
        }
        
        return true;
    }
    
    private void UpdateStartButtonVisibility()
    {
        if (GameUI.Instance != null)
        {
            bool allReady = IsAllPlayersReady();
            bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            bool gameNotStarted = GameManager.Instance == null || !GameManager.Instance.IsGameStarted();
            
            GameUI.Instance.UpdateStartButtonVisibility(isHost && allReady && gameNotStarted);
        }
    }
    
    public int GetPlayerCharacterIndex(ulong clientId)
    {
        foreach (var selection in playerSelections)
        {
            if (selection.clientId == clientId)
            {
                return selection.characterIndex;
            }
        }
        return 0;
    }
    
    public GameObject GetPlayerCharacterPrefab(ulong clientId)
    {
        int index = GetPlayerCharacterIndex(clientId);
        if (characterPrefabs != null && index >= 0 && index < characterPrefabs.Length)
        {
            return characterPrefabs[index];
        }
        return characterPrefabs != null && characterPrefabs.Length > 0 ? characterPrefabs[0] : null;
    }

    public GameObject GetRandomAvailableCharacterPrefab()
    {
        int index = GetRandomAvailableCharacterIndex();
        if (characterPrefabs != null && index >= 0 && index < characterPrefabs.Length)
        {
            return characterPrefabs[index];
        }
        return null;
    }

    public int GetRandomAvailableCharacterIndex()
    {
        if (aiCharacterPrefabs == null || aiCharacterPrefabs.Length == 0)
        {
            if (characterPrefabs == null || characterPrefabs.Length == 0) return -1;

            List<int> takenIndices = new List<int>();
            foreach (var selection in playerSelections)
            {
                takenIndices.Add(selection.characterIndex);
            }

            List<int> availableIndices = new List<int>();
            for (int i = 0; i < characterPrefabs.Length; i++)
            {
                if (!takenIndices.Contains(i))
                {
                    availableIndices.Add(i);
                }
            }

            if (availableIndices.Count == 0)
            {
                return UnityEngine.Random.Range(0, characterPrefabs.Length);
            }

            return availableIndices[UnityEngine.Random.Range(0, availableIndices.Count)];
        }

        List<GameObject> takenPrefabs = new List<GameObject>();
        foreach (var selection in playerSelections)
        {
            if (selection.characterIndex >= 0 && selection.characterIndex < characterPrefabs.Length)
            {
                takenPrefabs.Add(characterPrefabs[selection.characterIndex]);
            }
        }
        
        List<int> aiAvailableIndices = new List<int>();
        for (int i = 0; i < aiCharacterPrefabs.Length; i++)
        {
            GameObject aiPrefab = aiCharacterPrefabs[i];
            bool isTaken = false;
            
            foreach (var taken in takenPrefabs)
            {
                if (taken == aiPrefab || (taken != null && aiPrefab != null && taken.name == aiPrefab.name))
                {
                    isTaken = true;
                    break;
                }
            }
            
            if (!isTaken)
            {
                aiAvailableIndices.Add(i);
            }
        }
        
        if (aiAvailableIndices.Count == 0)
        {
            return UnityEngine.Random.Range(0, aiCharacterPrefabs.Length);
        }
        
        return aiAvailableIndices[UnityEngine.Random.Range(0, aiAvailableIndices.Count)];
    }
    
    void Update()
    {
        if (currentDisplayCharacter != null)
        {
            currentDisplayCharacter.transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        }
        
        if (characterSelectionPanel != null && characterSelectionPanel.activeSelf)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsGameStarted())
            {
                HideSelectionUI();
            }
            else
            {
                bool isConnected = NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient;
                if (isConnected && characterDisplayParent != null && !characterDisplayParent.gameObject.activeSelf)
                {
                    characterDisplayParent.gameObject.SetActive(true);
                    if (characterPrefabs != null && characterPrefabs.Length > 0 && currentDisplayCharacter == null)
                    {
                        ShowCharacter(currentCharacterIndex);
                    }
                }
                else if (!isConnected && characterDisplayParent != null && characterDisplayParent.gameObject.activeSelf)
                {
                    characterDisplayParent.gameObject.SetActive(false);
                    if (currentDisplayCharacter != null)
                    {
                        Destroy(currentDisplayCharacter);
                        currentDisplayCharacter = null;
                    }
                }
            }
        }
        
        if (IsSpawned && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            UpdateStartButtonVisibility();
        }
    }
    
    void OnDestroy()
    {
        if (currentDisplayCharacter != null)
        {
            Destroy(currentDisplayCharacter);
        }
        
        if (playerSelections != null)
        {
            playerSelections.Dispose();
        }
        
        if (readyPlayers != null)
        {
            readyPlayers.Dispose();
        }
    }
}