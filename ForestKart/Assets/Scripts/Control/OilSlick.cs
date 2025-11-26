using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Oil Slick - slows down players passing through
/// </summary>
[RequireComponent(typeof(Collider))]
public class OilSlick : NetworkBehaviour
{
    private Collider slickCollider;
    private float duration = 10f;
    private float speedReduction = 0.5f;
    private HashSet<KartController> affectedKarts = new HashSet<KartController>();
    
    private void Awake()
    {
        slickCollider = GetComponent<Collider>();
        if (slickCollider == null)
        {
            slickCollider = gameObject.AddComponent<BoxCollider>();
        }
        slickCollider.isTrigger = true;
        
        if (slickCollider is BoxCollider boxCollider)
        {
            boxCollider.size = new Vector3(3f, 0.1f, 3f);
        }
    }
    
    public void Initialize(float slickDuration, float reduction)
    {
        if (!IsServer) return;
        
        duration = slickDuration;
        speedReduction = reduction;
        
        StartCoroutine(DestroyAfterDuration());
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        
        KartController kart = other.GetComponentInParent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponent<KartController>();
        }
        
        if (kart != null && !affectedKarts.Contains(kart))
        {
            ApplyOilEffect(kart);
        }
    }
    
    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;
        
        KartController kart = other.GetComponentInParent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponent<KartController>();
        }
        
        if (kart != null && affectedKarts.Contains(kart))
        {
            RemoveOilEffect(kart);
        }
    }
    
    private void ApplyOilEffect(KartController kart)
    {
        affectedKarts.Add(kart);
        
        StartCoroutine(ApplySpeedReduction(kart));
        
        Rigidbody rb = kart.GetComponent<Rigidbody>();
        if (rb != null)
        {
            // TODO: Can adjust tire friction
        }
    }
    
    private void RemoveOilEffect(KartController kart)
    {
        affectedKarts.Remove(kart);
    }
    
    private IEnumerator ApplySpeedReduction(KartController kart)
    {
        if (kart == null) yield break;
        
        float originalMaxSpeed = kart.maxSpeed;
        float originalDriveTorque = kart.DriveTorque;
        
        if (originalMaxSpeed <= 0) originalMaxSpeed = 50f;
        if (originalDriveTorque <= 0) originalDriveTorque = 100f;
        
        kart.maxSpeed = originalMaxSpeed * speedReduction;
        kart.DriveTorque = originalDriveTorque * speedReduction;
        
        while (affectedKarts.Contains(kart))
        {
            yield return null;
        }
        
        if (kart != null)
        {
            kart.maxSpeed = originalMaxSpeed;
            kart.DriveTorque = originalDriveTorque;
        }
    }
    
    private IEnumerator DestroyAfterDuration()
    {
        yield return new WaitForSeconds(duration);
        
        foreach (var kart in affectedKarts)
        {
            RemoveOilEffect(kart);
        }
        affectedKarts.Clear();
        
        if (IsServer)
        {
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject, 0.1f);
        }
    }
}
