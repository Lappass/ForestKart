using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;
public class LocalPlayerSetup : NetworkBehaviour
{
    public CinemachineCamera cinemachineCamera;
    public AudioListener audioListener;
    public GameObject[] localOnlyObjects;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsOwner)
        {
            EnableLocalComponents();
        }
        else
        {
            DisableNonLocalComponents();
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
