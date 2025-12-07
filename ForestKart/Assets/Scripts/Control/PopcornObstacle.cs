using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class PopcornObstacle : NetworkBehaviour
{
    [Header("Popcorn Settings")]
    public float stunDuration = 1.5f;
    
    public float hitForce = 1500f;
    
    public float hitTorque = 500f;
    
    [Range(0f, 1f)]
    public float bounceFactor = 0.6f;
    
    public float popcornMass = 5f;
    
    public float minVelocityForImpact = 3f;
    
    [Header("Lifetime")]
    public float lifetime = 30f;
    
    public bool destroyOnHit = false;
    
    [Header("Visual Effects")]
    public GameObject hitEffectPrefab;
    
    public AudioClip hitSound;
    
    private Rigidbody rb;
    private Collider col;
    private AudioSource audioSource;
    private bool hasHitPlayer = false;
    private float spawnTime;
    
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        
        if (rb != null)
        {
            rb.mass = popcornMass;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        
        if (col != null)
        {
            PhysicsMaterial bounceMaterial = new PhysicsMaterial("PopcornBounce");
            bounceMaterial.bounciness = bounceFactor;
            bounceMaterial.staticFriction = 0.4f;
            bounceMaterial.dynamicFriction = 0.4f;
            bounceMaterial.bounceCombine = PhysicsMaterialCombine.Maximum;
            bounceMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
            col.material = bounceMaterial;
        }
        
        if (hitSound != null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.clip = hitSound;
            audioSource.volume = 0.5f;
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        spawnTime = Time.time;
        
        if (IsServer && lifetime > 0f)
        {
            StartCoroutine(DestroyAfterLifetime());
        }
    }
    
    private System.Collections.IEnumerator DestroyAfterLifetime()
    {
        yield return new WaitForSeconds(lifetime);
        
        if (IsServer && IsSpawned)
        {
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject);
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        KartController kart = collision.gameObject.GetComponent<KartController>();
        if (kart == null)
        {
            kart = collision.gameObject.GetComponentInParent<KartController>();
        }
        if (kart == null)
        {
            kart = collision.gameObject.GetComponentInChildren<KartController>();
        }
        
        if (kart != null && !hasHitPlayer)
        {
            hasHitPlayer = true;
            HandleKartCollision(kart, collision);
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        KartController kart = other.gameObject.GetComponent<KartController>();
        if (kart == null)
        {
            kart = other.gameObject.GetComponentInParent<KartController>();
        }
        if (kart == null)
        {
            kart = other.gameObject.GetComponentInChildren<KartController>();
        }
        
        if (kart != null && !hasHitPlayer)
        {
            hasHitPlayer = true;
            Vector3 hitDirection = (kart.transform.position - transform.position).normalized;
            HandleKartCollision(kart, null, hitDirection);
        }
    }
    
    private void HandleKartCollision(KartController kart, Collision collision, Vector3? overrideDirection = null)
    {
        if (kart == null) return;
        
        if (GameManager.Instance != null)
        {
            float countdownTime = GameManager.Instance.GetCountdownTime();
            if (countdownTime > 0f || !GameManager.Instance.IsGameStarted())
            {
                return;
            }
        }
        
        Vector3 hitDirection;
        if (overrideDirection.HasValue)
        {
            hitDirection = overrideDirection.Value;
        }
        else if (collision != null && collision.contacts.Length > 0)
        {
            Vector3 contactNormal = collision.contacts[0].normal;
            Vector3 toKart = (kart.transform.position - transform.position).normalized;
            if (Vector3.Dot(contactNormal, toKart) < 0)
            {
                hitDirection = toKart;
            }
            else
            {
                hitDirection = contactNormal;
            }
        }
        else
        {
            hitDirection = (kart.transform.position - transform.position).normalized;
        }
        
        float velocityMultiplier = 1f;
        if (rb != null)
        {
            float speed = rb.linearVelocity.magnitude;
            if (speed > minVelocityForImpact)
            {
                velocityMultiplier = 1f + (speed - minVelocityForImpact) * 0.2f;
            }
        }
        
        if (IsServer)
        {
            ApplyHitEffectServerRpc(kart.NetworkObjectId, hitDirection, velocityMultiplier);
        }
        else
        {
            ApplyHitEffectServerRpc(kart.NetworkObjectId, hitDirection, velocityMultiplier);
        }
        
        PlayHitEffects();
        
        if (destroyOnHit && IsServer)
        {
            StartCoroutine(DestroyAfterHit());
        }
    }
    
    [ServerRpc(RequireOwnership = false)]
    private void ApplyHitEffectServerRpc(ulong kartNetworkId, Vector3 hitDirection, float velocityMultiplier)
    {
        if (NetworkManager.Singleton == null) return;
        
        NetworkObject kartNetObj = null;
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(kartNetworkId, out kartNetObj))
        {
            KartController kart = kartNetObj.GetComponent<KartController>();
            if (kart == null)
            {
                kart = kartNetObj.GetComponentInChildren<KartController>();
            }
            
            if (kart != null)
            {
                float finalHitForce = hitForce * velocityMultiplier;
                float finalHitTorque = hitTorque * velocityMultiplier;
                
                kart.OnHitByPopcorn(hitDirection, finalHitForce, finalHitTorque, stunDuration);
                
                if (rb != null)
                {
                    rb.AddForce(-hitDirection * finalHitForce * 0.1f, ForceMode.Impulse);
                }
            }
        }
    }
    
    private void PlayHitEffects()
    {
        if (audioSource != null && hitSound != null)
        {
            audioSource.Play();
        }
        
        if (hitEffectPrefab != null)
        {
            GameObject effect = Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 3f);
        }
    }
    
    private System.Collections.IEnumerator DestroyAfterHit()
    {
        yield return new WaitForSeconds(0.1f);
        
        if (IsServer && IsSpawned)
        {
            GetComponent<NetworkObject>().Despawn();
            Destroy(gameObject);
        }
    }
    
    void OnDestroy()
    {
        if (col != null && col.material != null)
        {
            Destroy(col.material);
        }
    }
}
