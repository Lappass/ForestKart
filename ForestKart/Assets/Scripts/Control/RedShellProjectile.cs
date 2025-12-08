using UnityEngine;
using Unity.Netcode;
using Unity.Mathematics;
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
    }
    
    public void SetParameters(Transform owner, Transform initialTarget, float shellSpeed, float force, float stun, float range)
    {
        ownerTransform = owner;
        targetTransform = initialTarget;
        speed = shellSpeed;
        hitForce = force;
        stunDuration = stun;
        trackingRange = range;
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
        if (targetTransform != null)
        {
            direction = (targetTransform.position - transform.position).normalized;
        }
        rb.linearVelocity = direction * speed;
        
        rb.angularVelocity = new Vector3(
            UnityEngine.Random.Range(-5f, 5f),
            UnityEngine.Random.Range(-10f, 10f),
            UnityEngine.Random.Range(-5f, 5f)
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
    
    private int maxBounces = 2;
    private int bounceCount = 0;
    private float lastBounceTime = 0f;

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
            float distanceToTarget = Vector3.Distance(transform.position, targetTransform.position);
            Vector3 directionToTarget = (targetTransform.position - transform.position).normalized;
            directionToTarget.y = 0; 
            
            Vector3 desiredDir = directionToTarget;
            bool usingSpline = false;
            bool terminalGuidance = distanceToTarget < 25f;

            if (!terminalGuidance && GameManager.Instance != null && GameManager.Instance.raceTrack != null)
            {
                var spline = GameManager.Instance.raceTrack;
                using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
                {
                    float t;
                    float3 nearest;
                    float dSq = UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, transform.position, out nearest, out t);
                    
                    if (dSq < 225f)
                    {
                        usingSpline = true;
                        Vector3 splineTangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(nativeSpline, t);

                        Vector3 currentForward = transform.forward;
                        currentForward.y = 0;
                        if (Vector3.Dot(splineTangent, currentForward) < 0) splineTangent = -splineTangent;
                        splineTangent.y = 0;
                        splineTangent.Normalize();
                        float targetWeight = Mathf.Lerp(0.3f, 0.8f, 1f - (distanceToTarget / 100f));
                        desiredDir = (splineTangent * (1f - targetWeight) + directionToTarget * targetWeight).normalized;
                    }
                }
            }

            if (Physics.Raycast(transform.position, transform.forward, out RaycastHit wallHit, 5f, LayerMask.GetMask("Default", "Ground")))
            {
                if (wallHit.collider.tag == "Wall" || wallHit.collider.gameObject.layer == LayerMask.NameToLayer("Default"))
                {
                     Vector3 avoidDir = Vector3.ProjectOnPlane(transform.forward, wallHit.normal).normalized;
                     desiredDir = Vector3.Slerp(desiredDir, avoidDir, 0.8f); 
                }
            }

            Vector3 currentVelocity = rb.linearVelocity;
            currentVelocity.y = 0;
            float currentTrackingStrength = terminalGuidance ? trackingStrength * 3f : trackingStrength;
            Vector3 desiredVelocity = Vector3.Slerp(currentVelocity.normalized, desiredDir, Time.fixedDeltaTime * currentTrackingStrength) * speed;
            desiredVelocity.y = rb.linearVelocity.y; 
            if (!Physics.Raycast(transform.position, Vector3.down, 1.0f))
            {
                 desiredVelocity.y -= 9.8f * Time.fixedDeltaTime;
            }
            
            rb.linearVelocity = desiredVelocity;
            if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2.0f))
            {
                desiredDir = Vector3.ProjectOnPlane(desiredDir, hit.normal).normalized;
            }
            
            if (desiredDir.magnitude > 0.1f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(desiredDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * currentTrackingStrength);
            }
        }
        else
        {
            bool followingSpline = false;
            if (GameManager.Instance != null && GameManager.Instance.raceTrack != null)
            {
                var spline = GameManager.Instance.raceTrack;
                using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
                {
                    float t;
                    float3 nearest;
                    float dSq = UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, transform.position, out nearest, out t);
                    if (dSq < 100f)
                    {
                        followingSpline = true;
                        Vector3 tangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(nativeSpline, t);
                        if (Vector3.Dot(tangent, transform.forward) < 0) tangent = -tangent;
                        
                        Vector3 desiredDir = tangent.normalized;
                        // Project to ground
                        if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2.0f))
                        {
                            desiredDir = Vector3.ProjectOnPlane(desiredDir, hit.normal).normalized;
                        }
                        Vector3 currentDir = rb.linearVelocity.normalized;
                        currentDir.y = 0;
                        desiredDir = Vector3.Slerp(currentDir, desiredDir, Time.fixedDeltaTime * 10f).normalized;
                        
                        rb.linearVelocity = desiredDir * speed + Vector3.up * rb.linearVelocity.y;
                        
                        if (desiredDir.magnitude > 0.1f)
                        {
                            Quaternion targetRotation = Quaternion.LookRotation(desiredDir);
                            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.fixedDeltaTime * 10f);
                        }
                    }
                }
            }
            if (!followingSpline)
            {
                Vector3 forward = transform.forward;
                if (Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 2.0f))
                {
                    forward = Vector3.ProjectOnPlane(transform.forward, hit.normal).normalized;
                }
                rb.linearVelocity = forward * speed + Vector3.up * rb.linearVelocity.y;
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
            if (ownerTransform != null && (kart.transform == ownerTransform || kart.transform.IsChildOf(ownerTransform)))
            {
                continue;
            }
            
            NetworkObject kartNetObj = kart.GetComponent<NetworkObject>();
            if (kartNetObj == null) kartNetObj = kart.GetComponentInParent<NetworkObject>();
            if (kartNetObj != null && kartNetObj.OwnerClientId == ownerClientId)
            {
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
            if (Time.time - lastBounceTime > 0.1f)
            {
                bounceCount++;
                lastBounceTime = Time.time;
                RaycastHit hit;
                Vector3 normal = (transform.position - other.ClosestPoint(transform.position)).normalized;
                if (Physics.Raycast(transform.position - transform.forward, transform.forward, out hit, 2f))
                {
                    normal = hit.normal;
                }
                
                rb.linearVelocity = Vector3.Reflect(rb.linearVelocity, normal);
                
                if (bounceCount > maxBounces)
                {
                    Debug.Log($"[RedShellProjectile] Hit wall too many times ({bounceCount}), destroying.");
                    DestroyShell();
                }
                else
                {
                     Debug.Log($"[RedShellProjectile] Bounced off wall. Count: {bounceCount}");
                }
            }
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
        float torqueAmount = UnityEngine.Random.Range(500f, 1500f);
        
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
