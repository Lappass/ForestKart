using UnityEngine;
using Unity.Netcode;
using Unity.Mathematics;
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
    private float ignoreOwnerTime = 0.5f; 
    private float spawnTime = 0f;
    
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
        spawnTime = Time.time;
        if (owner != null && shellCollider != null)
        {
            Collider[] ownerColliders = owner.GetComponentsInChildren<Collider>();
            foreach (var ownerCollider in ownerColliders)
            {
                if (ownerCollider != null && ownerCollider != shellCollider)
                {
                    Physics.IgnoreCollision(shellCollider, ownerCollider, true);
                }
            }

            StartCoroutine(ReenableCollisionWithOwner(ownerColliders));
        }
    }
    
    private System.Collections.IEnumerator ReenableCollisionWithOwner(Collider[] ownerColliders)
    {
        yield return new WaitForSeconds(ignoreOwnerTime);
        
        if (shellCollider != null && ownerColliders != null)
        {
            foreach (var ownerCollider in ownerColliders)
            {
                if (ownerCollider != null && ownerCollider != shellCollider)
                {
                    Physics.IgnoreCollision(shellCollider, ownerCollider, false);
                }
            }
        }
    }
    
    private void InitializeVelocity()
    {
        if (!IsServer || ownerTransform == null || rb == null) return;
        
        Vector3 direction = ownerTransform.forward;
        rb.linearVelocity = direction * speed;
        
        rb.angularVelocity = new Vector3(
            UnityEngine.Random.Range(-5f, 5f),
            UnityEngine.Random.Range(-10f, 10f),
            UnityEngine.Random.Range(-5f, 5f)
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
        
        CheckForNearbyKarts();
        bool followingSpline = false;
        Vector3 moveDir = transform.forward;
        
        if (GameManager.Instance != null && GameManager.Instance.raceTrack != null)
        {
            var spline = GameManager.Instance.raceTrack;
            using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
            {
                float t;
                Unity.Mathematics.float3 nearest;
                float dSq = UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, transform.position, out nearest, out t);
                if (dSq < 225f)
                {
                    followingSpline = true;
                    Vector3 tangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(nativeSpline, t);
                    Vector3 currentForward = transform.forward;
                    currentForward.y = 0;
                    if (Vector3.Dot(tangent, currentForward) < 0) tangent = -tangent;
                    moveDir = Vector3.Slerp(currentForward.normalized, tangent.normalized, Time.fixedDeltaTime * 20f).normalized;
                    if (dSq < 25f)
                    {
                        moveDir = tangent.normalized;
                    }
                }
            }
        }
        
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2.0f))
        {
            moveDir = Vector3.ProjectOnPlane(moveDir, hit.normal).normalized;
        }
        else
        {
            rb.AddForce(Vector3.down * 20f, ForceMode.Acceleration);
        }

        Vector3 newVel = moveDir * speed;
        if (!Physics.Raycast(transform.position, Vector3.down, 0.5f))
        {
             newVel.y = rb.linearVelocity.y;
        }
        
        rb.linearVelocity = newVel;
        if (moveDir.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * 15f);
        }
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
            if (Time.time - spawnTime < ignoreOwnerTime)
            {
                if (col.transform == ownerTransform || 
                    col.transform.IsChildOf(ownerTransform) || 
                    ownerTransform.IsChildOf(col.transform) ||
                    col.transform.root == ownerTransform.root)
                {
                    continue;
                }
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
        if (ownerTransform != null && Time.time - spawnTime < ignoreOwnerTime)
        {
            if (other.transform == ownerTransform || 
                other.transform.IsChildOf(ownerTransform) || 
                ownerTransform.IsChildOf(other.transform) ||
                other.transform.root == ownerTransform.root)
            {
                return;
            }
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
                
                if (kartNetObj != null && ownerNetObj != null)
                {
                    // Prevent self-hit (same NetworkObject)
                    if (kartNetObj.NetworkObjectId == ownerNetObj.NetworkObjectId)
                    {
                        isOwnerKart = true;
                    }
                    // Prevent friendly fire for non-server clients (Server owns multiple objects so we allow interaction)
                    else if (kartNetObj.OwnerClientId == ownerNetObj.OwnerClientId)
                    {
                        if (ownerNetObj.OwnerClientId != NetworkManager.ServerClientId)
                        {
                            isOwnerKart = true;
                        }
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
        float torqueAmount = UnityEngine.Random.Range(500f, 1500f);
        
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
