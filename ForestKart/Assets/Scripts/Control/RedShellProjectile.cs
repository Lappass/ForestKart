using UnityEngine;
using Unity.Netcode;
using System.Collections;
[RequireComponent(typeof(Rigidbody))]
public class RedShellProjectile : NetworkBehaviour
{
    private Rigidbody rb;
    private Transform ownerTransform;
    private Transform targetTransform;
    private float speed = 18f;
    private float hitForce = 3000f;
    private float stunDuration = 2f;
    private bool hasHit = false;
    private float trackingRange = 100f;
    private float trackingStrength = 3f;
    private float updateTargetInterval = 0.5f;
    private float lastTargetUpdate = 0f;
    
    private Collider shellCollider;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
        }
        rb.useGravity = true;
        rb.mass = 5f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        
        shellCollider = GetComponent<Collider>();
        if (shellCollider == null)
        {
            SphereCollider sphereCollider = gameObject.AddComponent<SphereCollider>();
            sphereCollider.radius = 0.5f;
            shellCollider = sphereCollider;
        }
        else if (shellCollider is SphereCollider sphere)
        {
            if (sphere.radius < 0.5f)
            {
                sphere.radius = 0.5f;
            }
        }
        shellCollider.isTrigger = true;
    }
    
    public void SetParameters(Transform owner, Transform initialTarget, float shellSpeed, float force, float stun, float range)
    {
        ownerTransform = owner;
        targetTransform = initialTarget;
        speed = shellSpeed;
        hitForce = force;
        stunDuration = stun;
        trackingRange = range;
    }
    
    private void InitializeVelocity()
    {
        if (!IsServer || ownerTransform == null || rb == null) return;
        
        Vector3 direction = ownerTransform.forward;
        if (targetTransform != null)
        {
            direction = (targetTransform.position - transform.position).normalized;
        }
        rb.linearVelocity = direction * speed;
        
        rb.angularVelocity = new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(-10f, 10f),
            Random.Range(-5f, 5f)
        );
        
        Debug.Log($"[RedShellProjectile] Initialized velocity: {rb.linearVelocity}");
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
        
        if (IsServer && ownerTransform != null && rb != null)
        {
            InitializeVelocity();
        }
    }
    
    public void Initialize(Transform owner, Transform initialTarget, float shellSpeed, float force, float stun, float range)
    {
        SetParameters(owner, initialTarget, shellSpeed, force, stun, range);
    }
    
    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        if (Time.time - lastTargetUpdate > updateTargetInterval)
        {
            UpdateTarget();
            lastTargetUpdate = Time.time;
        }
        
        CheckForNearbyKarts();
        
        if (targetTransform != null && targetTransform.gameObject.activeInHierarchy)
        {
            Vector3 directionToTarget = (targetTransform.position - transform.position).normalized;
            directionToTarget.y = 0;
            
            Vector3 currentVelocity = rb.linearVelocity.normalized;
            Vector3 desiredVelocity = Vector3.Slerp(currentVelocity, directionToTarget, Time.fixedDeltaTime * trackingStrength) * speed;
            rb.linearVelocity = desiredVelocity;
            
            if (directionToTarget.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * trackingStrength);
            }
        }
        else
        {
            if (rb.linearVelocity.magnitude < speed * 0.5f)
            {
                Vector3 direction = rb.linearVelocity.normalized;
                if (direction.magnitude < 0.1f)
                {
                    direction = transform.forward;
                }
                rb.linearVelocity = direction * speed;
            }
        }
    }
    
    private void UpdateTarget()
    {
        if (!IsServer) return;
        
        Transform nearestEnemy = FindNearestEnemy(trackingRange);
        if (nearestEnemy != null)
        {
            targetTransform = nearestEnemy;
        }
    }
    
    private Transform FindNearestEnemy(float range)
    {
        Transform nearestPlayer = null;
        Transform nearestAI = null;
        float nearestPlayerDistance = float.MaxValue;
        float nearestAIDistance = float.MaxValue;
        
        ulong ownerClientId = ulong.MaxValue;
        if (ownerTransform != null)
        {
            NetworkObject ownerNetObj = ownerTransform.GetComponent<NetworkObject>();
            if (ownerNetObj == null) ownerNetObj = ownerTransform.GetComponentInParent<NetworkObject>();
            if (ownerNetObj != null)
            {
                ownerClientId = ownerNetObj.OwnerClientId;
            }
        }
        
        KartController[] allKarts = FindObjectsByType<KartController>(FindObjectsSortMode.None);
        
        foreach (var kart in allKarts)
        {
            // Skip self instance
            if (ownerTransform != null && (kart.transform == ownerTransform || kart.transform.IsChildOf(ownerTransform)))
            {
                continue;
            }
            
            NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
            if (kartNetObj == null) kartNetObj = kart.GetComponentInParent<NetworkObject>();
            
            // Skip by ClientId
            if (kartNetObj != null && kartNetObj.OwnerClientId == ownerClientId)
            {
                // Fix: Allow finding targets if owner is Server (Host vs AI or AI vs AI)
                if (ownerClientId != NetworkManager.ServerClientId)
                {
                    continue;
                }
            }
            
            float distance = Vector3.Distance(transform.position, kart.transform.position);
            if (distance > range) continue;
            
            Vector3 direction = (kart.transform.position - transform.position).normalized;
            float dot = Vector3.Dot(transform.forward, direction);
            if (dot <= -0.5f) continue;
            
            bool isAI = kart.GetComponent<AIKartController>() != null;
            
            if (isAI)
            {
                if (distance < nearestAIDistance)
                {
                    nearestAI = kart.transform;
                    nearestAIDistance = distance;
                }
            }
            else
            {
                if (distance < nearestPlayerDistance)
                {
                    nearestPlayer = kart.transform;
                    nearestPlayerDistance = distance;
                }
            }
        }
        
        if (nearestPlayer != null)
        {
            return nearestPlayer;
        }
        return nearestAI;
    }
    
    private void CheckForNearbyKarts()
    {
        if (hasHit) return;
        if (ownerTransform == null) return;
        
        float detectionRadius = 2f;
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, detectionRadius);
        
        foreach (Collider col in nearbyColliders)
        {
            if (col.transform == transform || col.transform.IsChildOf(transform))
            {
                continue;
            }
            
            KartController kart = FindKartController(col);
            if (kart != null)
            {
                ProcessHit(kart);
            }
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        HandleCollision(other);
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        HandleCollision(collision.collider);
    }
    
    private void HandleCollision(Collider other)
    {
        if (hasHit) return;
        
        if (other.transform == transform || other.transform.IsChildOf(transform))
        {
            return;
        }
        
        KartController kart = FindKartController(other);
        
        if (kart != null)
        {
            ProcessHit(kart);
            return;
        }
        
        if (other.gameObject.layer == LayerMask.NameToLayer("Default") || 
            other.tag == "Wall" || other.tag == "Obstacle")
        {
            Debug.Log($"[RedShellProjectile] Hit wall/obstacle: {other.gameObject.name}");
            DestroyShell();
        }
    }
    
    private void ProcessHit(KartController kart)
    {
        if (hasHit) return;

        bool isOwnerKart = false;
        
        if (ownerTransform != null)
        {
            Transform kartRoot = kart.transform.root;
            Transform ownerRoot = ownerTransform.root;
            
            // 1. Instance check
            if (kartRoot == ownerRoot || kart.transform == ownerTransform || 
                kart.transform.IsChildOf(ownerTransform) || ownerTransform.IsChildOf(kart.transform))
            {
                isOwnerKart = true;
            }
            
            // 2. OwnerClientId check
            if (!isOwnerKart)
            {
                NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
                if (kartNetObj == null) kartNetObj = kart.GetComponentInParent<NetworkObject>();
                NetworkObject ownerNetObj = ownerTransform.GetComponent<NetworkObject>();
                if (ownerNetObj == null) ownerNetObj = ownerTransform.GetComponentInParent<NetworkObject>();
                
                if (kartNetObj != null && ownerNetObj != null && kartNetObj.OwnerClientId == ownerNetObj.OwnerClientId)
                {
                    // If owner is Server, allow hitting (Host vs AI, AI vs Host, AI vs AI)
                    if (ownerNetObj.OwnerClientId != NetworkManager.ServerClientId)
                    {
                        isOwnerKart = true;
                    }
                }
                
                Debug.Log($"[RedShellProjectile] Check Hit {kart.name}. OwnerID: {ownerNetObj?.OwnerClientId}, TargetID: {kartNetObj?.OwnerClientId}. ServerID: {NetworkManager.ServerClientId}. IsOwnerKart: {isOwnerKart}");
            }
        }
        
        if (!isOwnerKart)
        {
            HitKart(kart);
        }
    }
    
    private KartController FindKartController(Collider collider)
    {
        KartController kart = collider.GetComponent<KartController>();
        if (kart != null) return kart;
        
        kart = collider.GetComponentInParent<KartController>();
        if (kart != null) return kart;
        
        Transform root = collider.transform.root;
        kart = root.GetComponent<KartController>();
        if (kart != null) return kart;
        
        Rigidbody otherRb = collider.attachedRigidbody;
        if (otherRb == null) otherRb = collider.GetComponentInParent<Rigidbody>();
        if (otherRb == null) otherRb = collider.transform.root.GetComponent<Rigidbody>();
        
        if (otherRb != null)
        {
            kart = otherRb.GetComponent<KartController>();
            if (kart != null) return kart;
            kart = otherRb.GetComponentInChildren<KartController>();
            if (kart != null) return kart;
            if (otherRb.transform.parent != null) kart = otherRb.transform.parent.GetComponentInParent<KartController>();
            if (kart != null) return kart;
        }
        
        AIKartController aiController = collider.GetComponentInParent<AIKartController>();
        if (aiController == null) aiController = collider.transform.root.GetComponent<AIKartController>();
        if (aiController != null) kart = aiController.GetComponent<KartController>();
        
        return kart;
    }
    
    private void HitKart(KartController kart)
    {
        if (hasHit) return;
        hasHit = true;
        
        Debug.Log($"[RedShellProjectile] CONFIRMED HIT on {kart.gameObject.name}!");
        
        Vector3 hitDirection = (kart.transform.position - transform.position);
        float distance = hitDirection.magnitude;
        if (distance < 0.1f) hitDirection = transform.forward;
        hitDirection.Normalize();
        hitDirection.y = Mathf.Max(0.2f, hitDirection.y);
        hitDirection.Normalize();
        
        float actualForce = hitForce * 1.5f;
        float torqueAmount = Random.Range(500f, 1500f);
        
        kart.OnHitByProjectile(hitDirection, actualForce, torqueAmount, stunDuration);
        
        HitKartClientRpc();
        
        DestroyShell();
    }
    
    [ClientRpc]
    private void HitKartClientRpc()
    {
        // Effect
    }
    
    private void DestroyShell()
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
