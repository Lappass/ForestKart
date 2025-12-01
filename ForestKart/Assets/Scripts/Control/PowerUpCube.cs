using UnityEngine;
using Unity.Netcode;
using System.Collections;
[RequireComponent(typeof(Collider))]
public class PowerUpCube : NetworkBehaviour
{
    [Header("Power Up Cube Settings")]
    [Tooltip("Auto respawn after collection")]
    public bool autoRespawn = true;
    
    [Tooltip("Respawn time (seconds)")]
    public float respawnTime = 5f;
    
    [Tooltip("Rotation speed (degrees/second)")]
    public float rotationSpeed = 90f;
    
    [Tooltip("Float speed")]
    public float floatSpeed = 2f;
    
    [Tooltip("Float amount")]
    public float floatAmount = 0.3f;
    
    [Header("Visual Settings")]
    [Tooltip("Cube model (for hide/show)")]
    public GameObject cubeVisual;
    
    [Tooltip("Collection effect")]
    public GameObject collectEffectPrefab;
    
    private Collider cubeCollider;
    private Vector3 startPosition;
    private float floatTimer = 0f;
    private bool isCollected = false;
    private NetworkVariable<bool> networkIsCollected = new NetworkVariable<bool>(false);
    private MeshRenderer cubeRenderer;
    
    private void Awake()
    {
        cubeCollider = GetComponent<Collider>();
        if (cubeCollider == null)
        {
            cubeCollider = gameObject.AddComponent<BoxCollider>();
        }
        
        cubeCollider.isTrigger = true;
        
        startPosition = transform.position;
        
        if (cubeVisual == null)
        {
            cubeRenderer = GetComponent<MeshRenderer>();
            if (cubeRenderer == null)
            {
                cubeRenderer = GetComponentInChildren<MeshRenderer>();
            }
        }
        else
        {
            cubeRenderer = cubeVisual.GetComponent<MeshRenderer>();
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        networkIsCollected.OnValueChanged += OnCollectedStateChanged;
        
        if (IsServer)
        {
            startPosition = transform.position;
            Debug.Log($"[PowerUpCube] {gameObject.name} spawned at {startPosition}, autoRespawn: {autoRespawn}, respawnTime: {respawnTime}");
        }
        
        SetCollectedState(networkIsCollected.Value);
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        networkIsCollected.OnValueChanged -= OnCollectedStateChanged;
    }
    
    private void OnCollectedStateChanged(bool oldValue, bool newValue)
    {
        SetCollectedState(newValue);
    }
    
    private void Update()
    {
        if (!isCollected)
        {
            transform.Rotate(0, rotationSpeed * Time.deltaTime, 0, Space.World);
            
            if (IsServer)
            {
                floatTimer += Time.deltaTime * floatSpeed;
                float yOffset = Mathf.Sin(floatTimer) * floatAmount;
                transform.position = new Vector3(startPosition.x, startPosition.y + yOffset, startPosition.z);
            }
        }
    }
    
    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (isCollected) return;
        
        KartController kartController = other.GetComponentInParent<KartController>();
        if (kartController == null)
        {
            AIKartController aiController = other.GetComponentInParent<AIKartController>();
            if (aiController != null)
            {
                kartController = aiController.GetComponent<KartController>();
            }
        }
        
        if (kartController != null)
        {
            PowerUpSystem powerUpSystem = kartController.GetComponent<PowerUpSystem>();
            if (powerUpSystem != null)
            {
                powerUpSystem.AcquireRandomPowerUpServerRpc();
                CollectCube();
            }
        }
    }
    
    private void CollectCube()
    {
        if (isCollected) return;
        
        isCollected = true;
        networkIsCollected.Value = true;
        
        // Notify clients
        CollectCubeClientRpc();
        
        Debug.Log($"[PowerUpCube] {gameObject.name} collected! autoRespawn: {autoRespawn}, respawnTime: {respawnTime}");
        
        if (autoRespawn)
        {
            Debug.Log($"[PowerUpCube] {gameObject.name} starting respawn coroutine...");
            StartCoroutine(RespawnAfterDelay());
        }
    }
    
    [ClientRpc]
    private void CollectCubeClientRpc()
    {
        SetCollectedState(true);
        
        if (collectEffectPrefab != null)
        {
            GameObject effect = Instantiate(collectEffectPrefab, transform.position, Quaternion.identity);
            Destroy(effect, 3f);
        }
    }
    
    private void SetCollectedState(bool collected)
    {
        isCollected = collected;
        
        if (cubeRenderer != null)
        {
            cubeRenderer.enabled = !collected;
        }
        else if (cubeVisual != null && cubeVisual != gameObject)
        {
            cubeVisual.SetActive(!collected);
        }
        
        if (cubeCollider != null)
        {
            cubeCollider.enabled = !collected;
        }
    }
    
    private IEnumerator RespawnAfterDelay()
    {
        Debug.Log($"[PowerUpCube] {gameObject.name} waiting {respawnTime} seconds to respawn...");
        
        yield return new WaitForSeconds(respawnTime);
        
        Debug.Log($"[PowerUpCube] {gameObject.name} respawning now!");
        
        // Reset on server directly (already on server, no need for ServerRpc)
        floatTimer = 0f;
        transform.position = startPosition;
        isCollected = false;
        networkIsCollected.Value = false;
        
        // Notify clients
        RespawnCubeClientRpc();
        
        Debug.Log($"[PowerUpCube] {gameObject.name} respawned at {startPosition}");
    }
    
    [ClientRpc]
    private void RespawnCubeClientRpc()
    {
        SetCollectedState(false);
        floatTimer = 0f;
        
        if (!IsServer)
        {
            transform.position = startPosition;
        }
    }
    
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, 0.5f);
    }
}
