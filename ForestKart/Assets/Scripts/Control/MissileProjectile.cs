using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Missile projectile - tracks target
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MissileProjectile : NetworkBehaviour
{
    private Rigidbody rb;
    private Transform target;
    private float speed = 20f;
    private float hitForce = 4000f;
    private float stunDuration = 3f;
    private bool hasHit = false;
    private float trackingStrength = 5f;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.useGravity = false;
        rb.mass = 1f;
        rb.linearDamping = 0.5f;
    }
    
    public void SetParameters(Transform targetTransform, float missileSpeed, float force, float stun)
    {
        target = targetTransform;
        speed = missileSpeed;
        hitForce = force;
        stunDuration = stun;
    }
    
    private void InitializeVelocity()
    {
        if (!IsServer || rb == null) return;
        
        if (target != null)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            rb.linearVelocity = direction * speed;
        }
        else
        {
            rb.linearVelocity = transform.forward * speed;
        }
        
        Debug.Log($"[MissileProjectile] Initialized velocity: {rb.linearVelocity}");
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            StartCoroutine(DelayedInitialize());
        }
    }
    
    private System.Collections.IEnumerator DelayedInitialize()
    {
        yield return null;
        yield return null;
        
        if (IsServer && rb != null)
        {
            InitializeVelocity();
        }
    }
    
    public void Initialize(Transform targetTransform, float missileSpeed, float force, float stun)
    {
        SetParameters(targetTransform, missileSpeed, force, stun);
    }
    
    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            if (rb.linearVelocity.magnitude < speed * 0.5f)
            {
                rb.linearVelocity = transform.forward * speed;
            }
            return;
        }
        
        Vector3 directionToTarget = (target.position - transform.position).normalized;
        Vector3 currentVelocity = rb.linearVelocity.normalized;
        
        Vector3 desiredVelocity = Vector3.Slerp(currentVelocity, directionToTarget, Time.fixedDeltaTime * trackingStrength) * speed;
        rb.linearVelocity = desiredVelocity;
        
        if (directionToTarget.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * trackingStrength);
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        KartController kart = other.GetComponentInParent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponent<KartController>();
        }
        
        if (kart != null)
        {
            HitKart(kart);
            return;
        }
        
        if (other.gameObject.layer == LayerMask.NameToLayer("Default") || 
            other.tag == "Wall" || other.tag == "Obstacle")
        {
            DestroyMissile();
        }
    }
    
    private void HitKart(KartController kart)
    {
        hasHit = true;
        
        Rigidbody kartRb = kart.GetComponent<Rigidbody>();
        if (kartRb != null)
        {
            Vector3 hitDirection = (kart.transform.position - transform.position).normalized;
            hitDirection.y = 0.5f;
            kartRb.AddForce(hitDirection * hitForce, ForceMode.Impulse);
            
            kartRb.AddTorque(Vector3.up * Random.Range(-10f, 10f), ForceMode.Impulse);
        }
        
        StartCoroutine(StunKart(kart));
        
        HitKartClientRpc();
        
        DestroyMissile();
    }
    
    private System.Collections.IEnumerator StunKart(KartController kart)
    {
        bool originalEnabled = kart.controlsEnabled;
        kart.controlsEnabled = false;
        
        yield return new WaitForSeconds(stunDuration);
        
        if (kart != null)
        {
            kart.controlsEnabled = originalEnabled;
        }
    }
    
    [ClientRpc]
    private void HitKartClientRpc()
    {
        // Play explosion effect if available
        // TODO: Add effects
    }
    
    private void DestroyMissile()
    {
        if (IsServer)
        {
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject, 0.1f);
        }
    }
    
    private void Start()
    {
        if (IsServer)
        {
            Destroy(gameObject, 15f);
        }
    }
}
