using UnityEngine;
using Unity.Netcode;
using System.Collections;

/// <summary>
/// Shell projectile - flies forward and hits players
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class ShellProjectile : NetworkBehaviour
{
    private Rigidbody rb;
    private Transform ownerTransform;
    private float speed = 15f;
    private float hitForce = 3000f;
    private float stunDuration = 2f;
    private bool hasHit = false;
    
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
        
        Debug.Log($"[ShellProjectile] Initialized collider: {shellCollider.GetType().Name}, isTrigger: {shellCollider.isTrigger}, radius: {(shellCollider is SphereCollider sc ? sc.radius : 0f)}");
    }
    
    public void SetParameters(Transform owner, float shellSpeed, float force, float stun)
    {
        ownerTransform = owner;
        speed = shellSpeed;
        hitForce = force;
        stunDuration = stun;
    }
    
    private void InitializeVelocity()
    {
        if (!IsServer || ownerTransform == null || rb == null) return;
        
        Vector3 direction = ownerTransform.forward;
        rb.linearVelocity = direction * speed;
        
        rb.angularVelocity = new Vector3(
            Random.Range(-5f, 5f),
            Random.Range(-10f, 10f),
            Random.Range(-5f, 5f)
        );
        
        Debug.Log($"[ShellProjectile] Initialized velocity: {rb.linearVelocity}, speed: {speed}, direction: {direction}");
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
        else
        {
            Debug.LogError($"[ShellProjectile] Failed to initialize - IsServer: {IsServer}, owner: {ownerTransform != null}, rb: {rb != null}");
        }
    }
    
    public void Initialize(Transform owner, float shellSpeed, float force, float stun)
    {
        SetParameters(owner, shellSpeed, force, stun);
    }
    
    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        if (rb.linearVelocity.magnitude < speed * 0.5f)
        {
            Vector3 direction = rb.linearVelocity.normalized;
            if (direction.magnitude < 0.1f)
            {
                direction = transform.forward;
            }
            rb.linearVelocity = direction * speed;
        }
        
        CheckForNearbyKarts();
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
                bool isOwnerKart = false;
                if (ownerTransform != null)
                {
                    Transform kartRoot = kart.transform.root;
                    Transform ownerRoot = ownerTransform.root;
                    
                    if (kartRoot == ownerRoot || kart.transform == ownerTransform || 
                        kart.transform.IsChildOf(ownerTransform) || ownerTransform.IsChildOf(kart.transform))
                    {
                        isOwnerKart = true;
                    }
                    
                    if (!isOwnerKart)
                    {
                        NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
                        if (kartNetObj == null) kartNetObj = kart.GetComponentInParent<NetworkObject>();
                        NetworkObject ownerNetObj = ownerTransform.GetComponent<NetworkObject>();
                        if (ownerNetObj == null) ownerNetObj = ownerTransform.GetComponentInParent<NetworkObject>();
                        
                        if (kartNetObj != null && ownerNetObj != null && kartNetObj.OwnerClientId == ownerNetObj.OwnerClientId)
                        {
                            isOwnerKart = true;
                        }
                    }
                }
                
                if (!isOwnerKart)
                {
                    float distance = Vector3.Distance(transform.position, kart.transform.position);
                    if (distance < detectionRadius)
                    {
                        Debug.Log($"[ShellProjectile] Detected nearby kart via OverlapSphere: {kart.gameObject.name}, distance: {distance}");
                        HitKart(kart);
                        return;
                    }
                }
            }
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        Debug.Log($"[ShellProjectile] Trigger entered with: {other.gameObject.name}, layer: {other.gameObject.layer}");
        
        HandleCollision(other);
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;
        if (hasHit) return;
        
        Collider other = collision.collider;
        Debug.Log($"[ShellProjectile] Collision with: {other.gameObject.name}");
        
        HandleCollision(other);
    }
    
    private void HandleCollision(Collider other)
    {
        if (hasHit) return;
        
        if (other.transform == transform || other.transform.IsChildOf(transform))
        {
            return;
        }
        
        if (other.CompareTag("Player"))
        {
            Debug.Log($"[ShellProjectile] Detected Player tag collider: {other.gameObject.name}");
        }
        
        KartController kart = FindKartController(other);
        
        if (kart != null)
        {
            bool isOwnerKart = false;
            
            if (ownerTransform != null)
            {
                Transform kartRoot = kart.transform.root;
                Transform ownerRoot = ownerTransform.root;
                if (kartRoot == ownerRoot)
                {
                    isOwnerKart = true;
                }
                
                if (!isOwnerKart && (kart.transform == ownerTransform || kart.transform.IsChildOf(ownerTransform) || ownerTransform.IsChildOf(kart.transform)))
                {
                    isOwnerKart = true;
                }
                
                NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
                if (kartNetObj == null) kartNetObj = kart.GetComponentInParent<NetworkObject>();
                NetworkObject ownerNetObj = ownerTransform.GetComponent<NetworkObject>();
                if (ownerNetObj == null) ownerNetObj = ownerTransform.GetComponentInParent<NetworkObject>();
                
                if (!isOwnerKart && kartNetObj != null && ownerNetObj != null && kartNetObj.OwnerClientId == ownerNetObj.OwnerClientId)
                {
                    isOwnerKart = true;
                }
            }
            
            if (!isOwnerKart)
            {
                Debug.Log($"[ShellProjectile] Hit kart: {kart.gameObject.name}, ownerTransform: {(ownerTransform != null ? ownerTransform.name : "null")}");
                HitKart(kart);
                return;
            }
            else
            {
                Debug.Log($"[ShellProjectile] Ignored own kart: {kart.gameObject.name}");
                return;
            }
        }
        
        if (other.gameObject.layer == LayerMask.NameToLayer("Default") || 
            other.tag == "Wall" || other.tag == "Obstacle")
        {
            Debug.Log($"[ShellProjectile] Hit wall/obstacle: {other.gameObject.name}");
            DestroyShell();
        }
    }
    
    private KartController FindKartController(Collider collider)
    {
        KartController kart = collider.GetComponent<KartController>();
        if (kart != null) 
        {
            Debug.Log($"[ShellProjectile] Found KartController on collider: {collider.gameObject.name}");
            return kart;
        }
        
        kart = collider.GetComponentInParent<KartController>();
        if (kart != null)
        {
            Debug.Log($"[ShellProjectile] Found KartController in parent of: {collider.gameObject.name}");
            return kart;
        }
        
        Transform root = collider.transform.root;
        kart = root.GetComponent<KartController>();
        if (kart != null)
        {
            Debug.Log($"[ShellProjectile] Found KartController in root: {root.name}");
            return kart;
        }
        
        Rigidbody otherRb = collider.attachedRigidbody;
        if (otherRb == null)
        {
            otherRb = collider.GetComponentInParent<Rigidbody>();
        }
        if (otherRb == null)
        {
            root = collider.transform.root;
            otherRb = root.GetComponent<Rigidbody>();
        }
        
        if (otherRb != null)
        {
            Debug.Log($"[ShellProjectile] Found Rigidbody: {otherRb.gameObject.name}");
            
            kart = otherRb.GetComponent<KartController>();
            if (kart != null)
            {
                Debug.Log($"[ShellProjectile] Found KartController on Rigidbody object: {otherRb.gameObject.name}");
                return kart;
            }
            
            kart = otherRb.GetComponentInChildren<KartController>();
            if (kart != null)
            {
                Debug.Log($"[ShellProjectile] Found KartController in children of Rigidbody: {otherRb.gameObject.name}");
                return kart;
            }
            
            if (otherRb.transform.parent != null)
            {
                kart = otherRb.transform.parent.GetComponentInParent<KartController>();
                if (kart != null)
                {
                    Debug.Log($"[ShellProjectile] Found KartController in parent of Rigidbody: {otherRb.gameObject.name}");
                    return kart;
                }
            }
        }
        
        AIKartController aiController = collider.GetComponentInParent<AIKartController>();
        if (aiController == null)
        {
            root = collider.transform.root;
            aiController = root.GetComponent<AIKartController>();
        }
        if (aiController != null)
        {
            Debug.Log($"[ShellProjectile] Found AIKartController: {aiController.gameObject.name}");
            kart = aiController.GetComponent<KartController>();
            if (kart != null)
            {
                Debug.Log($"[ShellProjectile] Found KartController from AIKartController: {aiController.gameObject.name}");
                return kart;
            }
        }
        
        Debug.LogWarning($"[ShellProjectile] Could not find KartController for collider: {collider.gameObject.name}");
        return null;
    }
    
    private void HitKart(KartController kart)
    {
        if (hasHit) return;
        hasHit = true;
        
        // Check if this is an AI kart before hitting
        bool isAI = kart.GetComponent<AIKartController>() != null || 
                    kart.GetComponentInParent<AIKartController>() != null ||
                    kart.transform.root.GetComponent<AIKartController>() != null;
        
        Debug.Log($"[ShellProjectile] Hitting kart: {kart.gameObject.name}, isAI: {isAI}, IsServer: {IsServer}");
        
        Vector3 hitDirection = (kart.transform.position - transform.position);
        float distance = hitDirection.magnitude;
        if (distance < 0.1f)
        {
            hitDirection = transform.forward;
        }
        hitDirection.Normalize();
        hitDirection.y = Mathf.Max(0.2f, hitDirection.y);
        hitDirection.Normalize();
        
        float actualForce = hitForce * 1.5f;
        float torqueAmount = Random.Range(500f, 1500f);
        
        Debug.Log($"[ShellProjectile] Calling OnHitByProjectile on {kart.gameObject.name} with force: {actualForce}");
        kart.OnHitByProjectile(hitDirection, actualForce, torqueAmount, stunDuration);
        
        HitKartClientRpc();
        
        DestroyShell();
    }
    
    
    [ClientRpc]
    private void HitKartClientRpc()
    {
        // Play explosion effect if available
        // TODO: Add effects
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
            Destroy(gameObject, 10f);
        }
    }
}
