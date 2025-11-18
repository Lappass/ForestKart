using Unity.Netcode;
using UnityEngine;
using Unity.Cinemachine;
public class LocalPlayerSetup : NetworkBehaviour
{
    [Header("本地玩家组件")]
    [Tooltip("Cinemachine虚拟相机 - 只在本地玩家上激活")]
    public CinemachineCamera cinemachineCamera;
    
    [Tooltip("音频监听器 - 只在本地玩家上激活")]
    public AudioListener audioListener;
    
    [Tooltip("其他需要只在本地玩家上激活的GameObject")]
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
            Debug.Log($"[LocalPlayer] 启用本地玩家相机: {gameObject.name}");
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
