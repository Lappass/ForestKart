using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class FlameThrower : MonoBehaviour
{
    private ParticleSystem flameParticles;
    private Transform fireTrigger;
    private Collider triggerCollider;
    
    private float stateTimer;
    private bool isFiring;
    private Coroutine colliderCoroutine;

    [Header("Timing Settings")]
    public float minFireDuration = 2f;
    public float maxFireDuration = 8f;
    public float minCooldownDuration = 4f;
    public float maxCooldownDuration = 10f;
    
    [Header("Collider Delays")]
    public float colliderEnableDelay = 1f;
    public float colliderDisableDelay = 1f;

    private void Start()
    {
        // 1. Handle Particle System
        flameParticles = GetComponent<ParticleSystem>();
        // If not on this object, look in children
        if (flameParticles == null)
        {
            flameParticles = GetComponentInChildren<ParticleSystem>();
        }

        if (flameParticles == null)
        {
            Debug.LogWarning("[FlameThrower] No ParticleSystem found on object or children.");
        }

        // 2. Handle Fire Trigger
        // User stated: "It has a fireTrigger child object with a istrigger box collider"
        fireTrigger = transform.Find("FireTrigger");
        if (fireTrigger != null)
        {
            triggerCollider = fireTrigger.GetComponent<Collider>();
            
            var handler = fireTrigger.gameObject.GetComponent<FlameThrowerTriggerHandler>();
            if (handler == null)
            {
                handler = fireTrigger.gameObject.AddComponent<FlameThrowerTriggerHandler>();
            }
            handler.Initialize(this);
        }
        else
        {
            Debug.LogError($"[FlameThrower] Could not find child object named 'FireTrigger' on {gameObject.name}");
        }
        
        // Ensure collider is initially off before starting cycle
        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }

        // Start the cycle
        StartFiring();
    }

    private void Update()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            if (isFiring)
            {
                StartCooldown();
            }
            else
            {
                StartFiring();
            }
        }
    }

    private void StartFiring()
    {
        isFiring = true;
        stateTimer = Random.Range(minFireDuration, maxFireDuration);
        
        if (flameParticles != null) 
        {
            flameParticles.Play();
        }
        
        if (triggerCollider != null) 
        {
            // Stop any pending disable routine if rapid switching happens
            if (colliderCoroutine != null) StopCoroutine(colliderCoroutine);
            colliderCoroutine = StartCoroutine(EnableColliderRoutine());
        }
    }

    private void StartCooldown()
    {
        isFiring = false;
        stateTimer = Random.Range(minCooldownDuration, maxCooldownDuration);
        
        if (flameParticles != null) 
        {
            flameParticles.Stop();
        }
        
        if (triggerCollider != null) 
        {
            // Stop any pending enable routine
            if (colliderCoroutine != null) StopCoroutine(colliderCoroutine);
            colliderCoroutine = StartCoroutine(DisableColliderRoutine());
        }
    }
    
    private IEnumerator EnableColliderRoutine()
    {
        yield return new WaitForSeconds(colliderEnableDelay);
        if (triggerCollider != null)
        {
            triggerCollider.enabled = true;
        }
    }
    
    private IEnumerator DisableColliderRoutine()
    {
        yield return new WaitForSeconds(colliderDisableDelay);
        if (triggerCollider != null)
        {
            triggerCollider.enabled = false;
        }
    }

    public void OnPlayerHitFire(KartController kart)
    {
        if (kart == null) return;

        // "trigger the same logic as the player drives outside the track and respawn"
        // This logic is encapsulated in KartController.RespawnKart()
        
        // We ensure we only trigger this on the client that owns the kart
        // to prevent network fighting and ensure local prediction works.
        if (kart.IsOwner)
        {
            Debug.Log($"[FlameThrower] Player {kart.name} hit the fire! Respawning...");
            kart.RespawnKart();
        }
    }
}

public class FlameThrowerTriggerHandler : MonoBehaviour
{
    private FlameThrower parentFlameThrower;

    public void Initialize(FlameThrower parent)
    {
        parentFlameThrower = parent;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (parentFlameThrower == null) return;

        KartController kart = other.GetComponent<KartController>();
        if (kart == null)
        {
            kart = other.GetComponentInParent<KartController>();
        }

        if (kart != null)
        {
            parentFlameThrower.OnPlayerHitFire(kart);
        }
    }
}
