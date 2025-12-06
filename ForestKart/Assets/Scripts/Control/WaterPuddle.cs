using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class WaterPuddle : NetworkBehaviour
{
    [Header("Water Puddle Settings")]
    public float blurDuration = 2f;
    
    private Collider waterCollider;
    private HashSet<ulong> triggeredPlayers = new HashSet<ulong>();
    
    void Start()
    {
        waterCollider = GetComponent<Collider>();
        if (waterCollider != null)
        {
            waterCollider.isTrigger = true;
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        KartController kart = other.GetComponent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponentInParent<KartController>();
        }
        
        if (kart != null && kart.IsSpawned)
        {
            AIKartController aiController = kart.GetComponent<AIKartController>();
            if (aiController != null) return;
            
            if (kart.IsOwner)
            {
                ulong networkId = kart.NetworkObjectId;
                
                if (!triggeredPlayers.Contains(networkId))
                {
                    triggeredPlayers.Add(networkId);
                    kart.OnEnterWaterPuddle(blurDuration);
                }
            }
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        KartController kart = other.GetComponent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponentInParent<KartController>();
        }
        
        if (kart != null && kart.IsSpawned && kart.IsOwner)
        {
            ulong networkId = kart.NetworkObjectId;
            triggeredPlayers.Remove(networkId);
        }
    }
}

