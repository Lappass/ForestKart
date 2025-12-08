using UnityEngine;
using Unity.Netcode;
using UnityEngine.Splines;
using System.Collections;
using System.Collections.Generic;

public class PopcornSpawner : NetworkBehaviour
{
    [Header("Popcorn Prefab")]
    public GameObject popcornPrefab;
    public SplineContainer splinePath;
    [Header("Rain Settings")]
    public float spawnHeight = 25f;
    public float rainSpawnRate = 10f;
    [Range(0.1f, 1f)]
    public float rainCoverage = 1f;
    public float rainLateralWidth = 5f;
    [Header("Initial Velocity")]
    public float initialDownwardVelocity = 2f;
    public float randomHorizontalVelocity = 1f;
    [Header("Game Settings")]
    public bool spawnOnlyAfterGameStart = true;
    [Header("Limits")]
    public int maxPopcornCount = 50;
    private float splineLength = 0f;
    private List<GameObject> spawnedPopcorn = new List<GameObject>();
    private float rainSpawnTimer = 0f;
    private bool isSpawning = false;
    
    void Start()
    {
        if (splinePath == null && GameManager.Instance != null)
        {
            splinePath = GameManager.Instance.raceTrack;
        }
        
        if (splinePath == null)
        {
            splinePath = FindFirstObjectByType<SplineContainer>();
        }
        
        if (splinePath == null)
        {
            Debug.LogError("[PopcornSpawner] No SplineContainer found! Please assign one in the inspector or ensure GameManager has a raceTrack.");
            enabled = false;
            return;
        }
        
        splineLength = splinePath.Spline.GetLength();
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsServer)
        {
            if (!spawnOnlyAfterGameStart || (GameManager.Instance != null && GameManager.Instance.IsGameStarted()))
            {
                StartSpawning();
            }
            else
            {
                StartCoroutine(WaitForGameStart());
            }
        }
    }
    
    private IEnumerator WaitForGameStart()
    {
        while (GameManager.Instance == null || !GameManager.Instance.IsGameStarted())
        {
            yield return new WaitForSeconds(0.5f);
        }
        
        StartSpawning();
    }
    
    private void StartSpawning()
    {
        if (isSpawning) return;
        
        isSpawning = true;
        rainSpawnTimer = Time.time;
        StartCoroutine(SpawnCoroutine());
    }
    
    private IEnumerator SpawnCoroutine()
    {
        while (isSpawning)
        {
            CleanupDestroyedPopcorn();
            
            float spawnInterval = 1f / rainSpawnRate;
            if (Time.time - rainSpawnTimer >= spawnInterval)
            {
                if (maxPopcornCount <= 0 || spawnedPopcorn.Count < maxPopcornCount)
                {
                    SpawnSinglePopcornRain();
                }
                rainSpawnTimer = Time.time;
            }
            
            yield return null;
        }
    }
    
    private void SpawnSinglePopcornRain()
    {
        if (popcornPrefab == null || splinePath == null) return;
        
        float normalizedPosition = Random.Range(0f, rainCoverage);
        
        Vector3 splinePos = SplineUtility.EvaluatePosition(splinePath.Spline, normalizedPosition);
        Vector3 splineTangent = SplineUtility.EvaluateTangent(splinePath.Spline, normalizedPosition);
        Vector3 splineUp = SplineUtility.EvaluateUpVector(splinePath.Spline, normalizedPosition);
        
        splinePos = splinePath.transform.TransformPoint(splinePos);
        splineTangent = splinePath.transform.TransformDirection(splineTangent);
        splineUp = splinePath.transform.TransformDirection(splineUp);
        
        Vector3 right = Vector3.Cross(splineUp, splineTangent).normalized;
        float lateralOffset = Random.Range(-rainLateralWidth, rainLateralWidth);
        
        Vector3 spawnPosition = splinePos + splineUp * spawnHeight + right * lateralOffset;
        
        SpawnPopcornAtPosition(spawnPosition, splineUp, right, splineTangent);
    }
    
    private void SpawnPopcornAtPosition(Vector3 position, Vector3 up, Vector3 right, Vector3 forward)
    {
        GameObject popcorn = Instantiate(popcornPrefab, position, Quaternion.identity);
        
        Rigidbody rb = popcorn.GetComponent<Rigidbody>();
        if (rb != null)
        {
            Vector3 initialVelocity = -up * initialDownwardVelocity;
            initialVelocity += right * Random.Range(-randomHorizontalVelocity * 0.3f, randomHorizontalVelocity * 0.3f);
            initialVelocity += forward * Random.Range(-randomHorizontalVelocity * 0.2f, randomHorizontalVelocity * 0.2f);
            rb.linearVelocity = initialVelocity;
        }
        
        NetworkObject netObj = popcorn.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            netObj = popcorn.AddComponent<NetworkObject>();
        }
        
        var netTransform = popcorn.GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (netTransform == null)
        {
            netTransform = popcorn.AddComponent<Unity.Netcode.Components.NetworkTransform>();
            netTransform.SyncPositionX = true;
            netTransform.SyncPositionY = true;
            netTransform.SyncPositionZ = true;
            netTransform.SyncRotAngleX = true;
            netTransform.SyncRotAngleY = true;
            netTransform.SyncRotAngleZ = true;
            netTransform.UseHalfFloatPrecision = false;
            netTransform.Interpolate = true;
        }
        
        netObj.Spawn();
        spawnedPopcorn.Add(popcorn);
    }
    
    private void CleanupDestroyedPopcorn()
    {
        spawnedPopcorn.RemoveAll(popcorn => popcorn == null);
    }
    
    public void StopSpawning()
    {
        isSpawning = false;
    }
    
    void OnDestroy()
    {
        StopSpawning();
    }
}
