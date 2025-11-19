using UnityEngine;
using Unity.Netcode;
using UnityEngine.Splines;

[RequireComponent(typeof(KartController))]
public class PlayerProgressTracker : NetworkBehaviour
{
    [Header("Spline Path")]
    public SplineContainer splinePath;
    
    private float currentSplinePosition = 0f;
    private float splineLength = 0f;
    private Rigidbody rb;
    private int lapCount = 0;
    private float totalProgress = 0f;
    
    private NetworkVariable<float> networkSplinePosition = new NetworkVariable<float>(0f);
    private NetworkVariable<int> networkLapCount = new NetworkVariable<int>(0);
    
    private float checkPointThreshold = 0.98f;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        
        if (splinePath == null)
        {
            splinePath = FindFirstObjectByType<SplineContainer>();
        }
        
        if (splinePath != null)
        {
            splineLength = splinePath.Spline.GetLength();
        }
    }
    
    void Update()
    {
        if (splinePath == null || splineLength <= 0f) return;
        
        UpdateSplinePosition();
        
        if (currentSplinePosition > checkPointThreshold && networkSplinePosition.Value < 0.1f && lapCount == networkLapCount.Value)
        {
            lapCount++;
            networkLapCount.Value = lapCount;
        }
        
        if (IsServer)
        {
            networkSplinePosition.Value = currentSplinePosition;
        }
        
        totalProgress = lapCount + currentSplinePosition;
    }
    
    private void UpdateSplinePosition()
    {
        if (rb == null) return;
        
        float closestT = 0f;
        float closestDistance = float.MaxValue;
        
        float searchRange = 0.2f;
        float searchStartT = Mathf.Max(0f, currentSplinePosition - searchRange);
        float searchEndT = Mathf.Min(1f, currentSplinePosition + searchRange);
        
        for (int i = 0; i <= 50; i++)
        {
            float testT = Mathf.Lerp(searchStartT, searchEndT, i / 50f);
            Vector3 splinePos = splinePath.transform.TransformPoint(
                SplineUtility.EvaluatePosition(splinePath.Spline, testT)
            );
            float distance = Vector3.Distance(transform.position, splinePos);
            
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestT = testT;
            }
        }
        
        currentSplinePosition = Mathf.Lerp(currentSplinePosition, closestT, Time.deltaTime * 5f);
        
        if (closestDistance > 10f)
        {
            for (int i = 0; i <= 100; i++)
            {
                float testT = i / 100f;
                Vector3 splinePos = splinePath.transform.TransformPoint(
                    SplineUtility.EvaluatePosition(splinePath.Spline, testT)
                );
                float distance = Vector3.Distance(transform.position, splinePos);
                
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestT = testT;
                }
            }
            currentSplinePosition = closestT;
        }
    }
    
    public float GetTotalProgress()
    {
        if (IsServer)
        {
            return lapCount + currentSplinePosition;
        }
        else
        {
            return networkLapCount.Value + networkSplinePosition.Value;
        }
    }
    
    public int GetLapCount()
    {
        if (IsServer)
        {
            return lapCount;
        }
        else
        {
            return networkLapCount.Value;
        }
    }
    
    public float GetSplinePosition()
    {
        if (IsServer)
        {
            return currentSplinePosition;
        }
        else
        {
            return networkSplinePosition.Value;
        }
    }
}


