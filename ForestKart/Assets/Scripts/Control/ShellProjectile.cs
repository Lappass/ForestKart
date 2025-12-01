using UnityEngine;
using Unity.Netcode;
using System.Collections;
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
            Debug.Log($"[ShellProjectile] Hit wall/obstacle: {other.gameObject.name}");
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
            
            // 1. Instance check (Prevent hitting self instance)
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
                    if (ownerNetObj.OwnerClientId != NetworkManager.ServerClientId)
                    {
                        isOwnerKart = true;
                    }
                }
                Debug.Log($"[ShellProjectile] Check Hit {kart.name}. OwnerID: {ownerNetObj?.OwnerClientId}, TargetID: {kartNetObj?.OwnerClientId}. ServerID: {NetworkManager.ServerClientId}. IsOwnerKart: {isOwnerKart}");
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
        
        Debug.Log($"[ShellProjectile] CONFIRMED HIT on {kart.gameObject.name}!");
        
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
