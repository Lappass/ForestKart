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
            // Check if intro is playing
            bool isIntroPlaying = false;
            if (GameManager.Instance != null)
            {
                isIntroPlaying = GameManager.Instance.IsPlayingIntro();
            }
            
            if (!isIntroPlaying)
            {
                EnableLocalComponents();
            }
            else
            {
                // Disable components initially if intro is playing
                DisableNonLocalComponents(); // Effectively disables camera
                // But we might want to keep audio listener? Let's just disable camera specifically
                if (cinemachineCamera != null) 
                {
                    cinemachineCamera.enabled = false;
                    cinemachineCamera.gameObject.SetActive(false);
                }
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
        if (cinemachineCamera != null)
        {
            cinemachineCamera.enabled = true;
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
