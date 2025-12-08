using UnityEngine;
using Unity.Netcode;

public class FlameThrower : MonoBehaviour
{
    private ParticleSystem flameParticles;
    private Transform fireTrigger;
    private Collider triggerCollider;
    
    private float stateTimer;
    private bool isFiring;

    [Header("Timing Settings")]
    public float minFireDuration = 2f;
    public float maxFireDuration = 8f;
    public float minCooldownDuration = 4f;
    public float maxCooldownDuration = 10f;

    private void Start()
    {

        flameParticles = GetComponent<ParticleSystem>();        
        if (flameParticles == null)
        {
            flameParticles = GetComponentInChildren<ParticleSystem>();
        }
        if (flameParticles == null)
        {
            Debug.LogWarning("[FlameThrower] No ParticleSystem found on object or children.");
        }
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
            triggerCollider.enabled = true;
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
            triggerCollider.enabled = false;
        }
    }

    public void OnPlayerHitFire(KartController kart)
    {
        if (kart == null) return;
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
