using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;

public class VoicePosition : NetworkBehaviour
{
    private float timerMax = 0.2f;
    private float timer = 0;

    void Update()
    {
        if (!IsLocalPlayer || !VoiceManager.Instance.IsIn3DChannel) return;

        if (timer > 0) timer -= Time.deltaTime;
        else
        {
            timer = timerMax;
            VivoxService.Instance.Set3DPosition(gameObject, VoiceManager.Instance.ChannelName);
        }
    }
}
