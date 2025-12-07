using UnityEngine;
using Unity.Netcode;

public class FlameThrower : MonoBehaviour
{
    private ParticleSystem flameParticles;
    private Transform fireTrigger;

    private void Start()
    {
        // 1. Handle Particle System
        flameParticles = GetComponent<ParticleSystem>();
        // If not on this object, look in children
        if (flameParticles == null)
        {
            flameParticles = GetComponentInChildren<ParticleSystem>();
        }

        if (flameParticles != null)
        {
            if (!flameParticles.isPlaying)
            {
                flameParticles.Play();
            }
        }
        else
        {
            Debug.LogWarning("[FlameThrower] No ParticleSystem found on object or children.");
        }

        // 2. Handle Fire Trigger
        // User stated: "It has a fireTrigger child object with a istrigger box collider"
        fireTrigger = transform.Find("fireTrigger");
        if (fireTrigger != null)
        {
            var handler = fireTrigger.gameObject.GetComponent<FlameThrowerTriggerHandler>();
            if (handler == null)
            {
                handler = fireTrigger.gameObject.AddComponent<FlameThrowerTriggerHandler>();
            }
            handler.Initialize(this);
        }
        else
        {
            Debug.LogError($"[FlameThrower] Could not find child object named 'fireTrigger' on {gameObject.name}");
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

