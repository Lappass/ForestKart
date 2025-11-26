using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;
public class LocalPlayerSetup : NetworkBehaviour
{
    public CinemachineCamera cinemachineCamera;
    public AudioListener audioListener;
    public GameObject[] localOnlyObjects;
    
    public void UpdateCameraReference(CinemachineCamera newCamera)
    {
        if (newCamera == null) return;
        
        if (cinemachineCamera != null && cinemachineCamera != newCamera)
        {
            cinemachineCamera.enabled = false;
            cinemachineCamera.gameObject.SetActive(false);
        }
        
        cinemachineCamera = newCamera;
        cinemachineCamera.enabled = true;
        cinemachineCamera.gameObject.SetActive(true);
        
        Debug.Log($"[LocalPlayerSetup] Camera reference updated to: {newCamera.name}");
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsOwner)
        {
            // Check if game has started - if so, intro might be playing
            bool gameStarted = false;
            bool isIntroPlaying = false;
            if (GameManager.Instance != null)
            {
                gameStarted = GameManager.Instance.IsGameStarted();
                isIntroPlaying = GameManager.Instance.IsPlayingIntro();
            }
            
            // If game just started, always assume intro is playing (network sync delay workaround)
            // Camera will be enabled after intro ends
            if (gameStarted || isIntroPlaying)
            {
                Debug.Log($"[LocalPlayerSetup] Game started or intro playing, disabling camera initially. gameStarted={gameStarted}, isIntroPlaying={isIntroPlaying}");
                DisableNonLocalComponents();
                if (cinemachineCamera != null) 
                {
                    cinemachineCamera.Priority = 0;
                    cinemachineCamera.enabled = false;
                    cinemachineCamera.gameObject.SetActive(false);
                }
                // Enable audio listener but not camera
                if (audioListener != null) audioListener.enabled = true;
            }
            else
            {
                EnableLocalComponents();
            }
        }
        else
        {
            DisableNonLocalComponents();
        }
    }
    
    public void SetCameraEnabled(bool isEnabled)
    {
        if (!IsOwner) return;
        
        // Don't enable camera if intro is playing
        bool isIntroPlaying = GameManager.Instance != null && GameManager.Instance.IsPlayingIntro();
        if (isEnabled && isIntroPlaying)
        {
            Debug.Log("[LocalPlayerSetup] SetCameraEnabled(true) blocked - intro is playing");
            return;
        }
        
        Debug.Log($"[LocalPlayerSetup] SetCameraEnabled({isEnabled}) called");
        
        if (cinemachineCamera != null)
        {
            if (isEnabled)
            {
                // Force toggle to ensure CinemachineBrain detects it
                cinemachineCamera.gameObject.SetActive(false);
                cinemachineCamera.enabled = false;
                
                cinemachineCamera.gameObject.SetActive(true);
                cinemachineCamera.enabled = true;
            }
            else
            {
                cinemachineCamera.Priority = 0;
                cinemachineCamera.enabled = false;
                cinemachineCamera.gameObject.SetActive(false);
            }
            
            Debug.Log($"[LocalPlayerSetup] CinemachineCamera {(isEnabled ? "enabled" : "disabled")}: {cinemachineCamera.name}");
        }
        else
        {
            Debug.LogWarning("[LocalPlayerSetup] cinemachineCamera is null!");
        }
        
        if (isEnabled)
        {
            // Also ensure other local components are enabled if they weren't
            if (audioListener != null) audioListener.enabled = true;
            foreach (var obj in localOnlyObjects)
            {
                if (obj != null) obj.SetActive(true);
            }
        }
    }
    private void EnableLocalComponents()
    {
        // Don't enable camera if intro is playing or game just started
        bool isIntroPlaying = GameManager.Instance != null && GameManager.Instance.IsPlayingIntro();
        bool gameStarted = GameManager.Instance != null && GameManager.Instance.IsGameStarted();
        
        if (cinemachineCamera != null && !isIntroPlaying && !gameStarted)
        {
            cinemachineCamera.enabled = true;
        }
        else if (cinemachineCamera != null)
        {
            // Ensure camera stays disabled during intro
            cinemachineCamera.Priority = 0;
            cinemachineCamera.enabled = false;
            cinemachineCamera.gameObject.SetActive(false);
            Debug.Log("[LocalPlayerSetup] EnableLocalComponents: Camera kept disabled because intro/game is active");
        }
        
        if (audioListener != null)
        {
            audioListener.enabled = true;
        }
        foreach (var obj in localOnlyObjects)
        {
            if (obj != null)
            {
                obj.SetActive(true);
            }
        }
    }
    private void DisableNonLocalComponents()
    {
        if (cinemachineCamera != null)
        {
            cinemachineCamera.enabled = false;
        }
        if (audioListener != null)
        {
            audioListener.enabled = false;
        }
        foreach (var obj in localOnlyObjects)
        {
            if (obj != null)
            {
                obj.SetActive(false);
            }
        }
    }
}
