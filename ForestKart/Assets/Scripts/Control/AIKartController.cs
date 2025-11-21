using UnityEngine;
using Unity.Netcode;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(KartController))]
public class AIKartController : NetworkBehaviour
{
    [Header("Path Settings")]
    public SplineContainer splinePath;
    
    [Header("AI Behavior")]
    public float targetSpeed = 0f;
    
    [Range(0.5f, 1.5f)]
    public float speedMultiplier = 1.0f;
    
    public float minSpeed = 5f;
    
    public float maxSpeed = 0f;
    
    [Range(0.1f, 2f)]
    public float steerSensitivity = 1f;
    
    public float lookAheadDistance = 10f;
    
    [Range(0.1f, 1f)]
    public float speedSmoothing = 0.5f;
    
    [Header("Handling")]
    public bool adjustRearWheelFriction = true;
    [Range(1f, 2f)]
    public float rearSidewaysFrictionMultiplier = 1.2f;
    [Range(1f, 2f)]
    public float rearForwardFrictionMultiplier = 1.1f;
    
    [Header("Randomness")]
    public float speedVariation = 0.15f;
    
    public float lateralRandomness = 3f;
    
    public float speedChangeInterval = 2f;
    
    [Header("Obstacle Avoidance")]
    public bool enableObstacleAvoidance = true;
    
    public float detectionDistance = 15f;
    
    public float avoidanceAngle = 30f;
    
    public LayerMask obstacleLayer = -1;
    
    [Header("Overtaking")]
    public bool enableOvertaking = true;
    
    public float overtakingDetectionDistance = 20f;
    
    public float overtakingLaneWidth = 6f;
    
    public float minSpeedDifferenceForOvertaking = 0.5f;
    
    public float overtakingAccelerationMultiplier = 1.3f;
    
    public float overtakingDuration = 4f;
    
    public float aggressiveOvertakingDistance = 5f;
    
    private KartController kartController;
    private Rigidbody rb;
    private float currentSplinePosition = 0f;
    private float currentSpeed = 0f;
    private float splineLength = 0f;
    private Vector3 targetPosition;
    private Vector3 targetDirection;
    
    private int lapCount = 0;
    private float lastSplinePosition = 0f;
    
    private NetworkVariable<float> networkSplinePosition = new NetworkVariable<float>(0f);
    private NetworkVariable<float> networkSpeed = new NetworkVariable<float>(0f);
    private NetworkVariable<int> networkLapCount = new NetworkVariable<int>(0);
    
    private bool isOvertaking = false;
    private float overtakingTimer = 0f;
    private float lateralOffset = 0f;
    private GameObject targetVehicle = null;
    private float originalTargetSpeed = 0f;
    private float randomSpeedOffset = 0f;
    private float randomLateralOffset = 0f;
    private float speedChangeTimer = 0f;
    private float lateralChangeTimer = 0f;
    
    // Obstacle avoidance state
    private float baseTargetSpeed = 0f;
    private bool isAvoidingObstacle = false;
    
    void Start()
    {
        kartController = GetComponent<KartController>();
        rb = GetComponent<Rigidbody>();
        
        FindSplinePath();
        CalculateSpeedParameters();
        ConfigureRearWheelFriction();
        
        if (splinePath != null)
        {
            splineLength = splinePath.Spline.GetLength();
            currentSplinePosition = 0f;
            lastSplinePosition = 0f;
            lapCount = 0;
            
            randomSpeedOffset = UnityEngine.Random.Range(-speedVariation, speedVariation);
            randomLateralOffset = UnityEngine.Random.Range(-lateralRandomness, lateralRandomness);
            speedChangeTimer = UnityEngine.Random.Range(0f, speedChangeInterval);
            lateralChangeTimer = UnityEngine.Random.Range(0f, 2f);
        }
        else
        {
            Debug.LogError($"[AIKart] {gameObject.name} No SplineContainer found!");
        }
    }
    
    private void FindSplinePath()
    {
        if (splinePath != null) return;
        
        SplineContainer[] allSplines = FindObjectsByType<SplineContainer>(FindObjectsSortMode.None);
        
        if (allSplines.Length == 0)
        {
            Debug.LogError($"[AIKart] No SplineContainer found in scene!");
            return;
        }
        
        if (allSplines.Length == 1)
        {
            splinePath = allSplines[0];
            return;
        }
        
        float closestDistance = float.MaxValue;
        SplineContainer closestSpline = null;
        
        foreach (var spline in allSplines)
        {
            Vector3 splineCenter = spline.transform.position;
            float distance = Vector3.Distance(transform.position, splineCenter);
            
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSpline = spline;
            }
        }
        
        splinePath = closestSpline;
    }

    private void CalculateSpeedParameters()
    {
        if (kartController == null) return;
        
        // Prefer using KartController's maxSpeed to ensure AI and players use the same speed limit
        if (kartController.maxSpeed > 0f)
        {
            maxSpeed = kartController.maxSpeed * speedMultiplier;
        }
        else
        {
            float calculatedMaxSpeed = kartController.DriveTorque * 0.15f;
            
            if (maxSpeed <= 0f)
            {
                maxSpeed = calculatedMaxSpeed * speedMultiplier;
            }
            
            if (maxSpeed <= 0f)
            {
                maxSpeed = 50f;
            }
            else
            {
                maxSpeed = Mathf.Max(maxSpeed, 50f);
            }
        }
        
        if (targetSpeed <= 0f)
        {
            targetSpeed = maxSpeed * 0.9f;
        }
        
        targetSpeed = Mathf.Clamp(targetSpeed, minSpeed, maxSpeed);
        baseTargetSpeed = targetSpeed; // Store base speed for obstacle avoidance
    }
    
    private void ConfigureRearWheelFriction()
    {
        if (!adjustRearWheelFriction || kartController == null || kartController.driveWheels == null) return;
        
        for (int i = 0; i < kartController.driveWheels.Length; i++)
        {
            if (kartController.driveWheels[i] == null) continue;
            if (i < 2) continue;
            
            WheelCollider wheel = kartController.driveWheels[i];
            
            WheelFrictionCurve sideways = wheel.sidewaysFriction;
            sideways.stiffness *= rearSidewaysFrictionMultiplier;
            wheel.sidewaysFriction = sideways;
            
            WheelFrictionCurve forward = wheel.forwardFriction;
            forward.stiffness *= rearForwardFrictionMultiplier;
            wheel.forwardFriction = forward;
        }
    }
    
    void Update()
    {
        if (!IsServer) return;
        
        if (splinePath == null || kartController == null) return;
        
        UpdateAILogic();
        ApplyControls();
    }
    
    private void UpdateAILogic()
    {
        currentSpeed = rb.linearVelocity.magnitude;
        
        // Improved tracking logic: Estimate next position and refine locally
        float distanceTraveled = currentSpeed * Time.deltaTime;
        float normalizedDistance = distanceTraveled / splineLength;
        float predictedPos = (currentSplinePosition + normalizedDistance) % 1f;
        
        // Search window parameters
        float searchDist = Mathf.Max(10f, currentSpeed * Time.deltaTime * 2f); // Dynamic search window
        float searchRange = searchDist / splineLength;
        int searchSteps = 10;
        
        float bestT = predictedPos;
        float bestDistSq = float.MaxValue;
        
        // Local search to snap to spline
        for (int i = -searchSteps; i <= searchSteps; i++)
        {
            float t = predictedPos + ((float)i / searchSteps) * searchRange;
            float evalT = Mathf.Repeat(t, 1f);
            
            Vector3 testPos = splinePath.transform.TransformPoint(
                SplineUtility.EvaluatePosition(splinePath.Spline, evalT)
            );
            
            float dSq = (testPos - transform.position).sqrMagnitude;
            if (dSq < bestDistSq)
            {
                bestDistSq = dSq;
                bestT = evalT;
            }
            
            float positionChange = bestT - currentSplinePosition;
            if (Mathf.Abs(positionChange) > 0.05f)
            {
                bestT = currentSplinePosition + Mathf.Sign(positionChange) * 0.05f;
        }
        
        currentSplinePosition = bestT;
        }
    
        currentSplinePosition = Mathf.Repeat(currentSplinePosition, 1f);
        
        float lookAheadT = (currentSplinePosition + lookAheadDistance / splineLength) % 1f;
        Vector3 splinePosition = SplineUtility.EvaluatePosition(splinePath.Spline, lookAheadT);
        Vector3 splineTangent = SplineUtility.EvaluateTangent(splinePath.Spline, lookAheadT);
        Vector3 splineUp = SplineUtility.EvaluateUpVector(splinePath.Spline, lookAheadT);
        Vector3 splineRight = Vector3.Cross(splineTangent, splineUp).normalized;
        Vector3 splineNormal = splineRight;
        
        
        if (enableOvertaking)
        {
            CheckAndExecuteOvertaking();
        }
        
            if (isOvertaking)
        {
            UpdateOvertaking();
        }
        else
        {
            lateralOffset = Mathf.Lerp(lateralOffset, 0f, Time.deltaTime * 2f);
            
            speedChangeTimer += Time.deltaTime;
            if (speedChangeTimer >= speedChangeInterval)
            {
                randomSpeedOffset = UnityEngine.Random.Range(-speedVariation, speedVariation);
                speedChangeTimer = 0f;
                speedChangeInterval = UnityEngine.Random.Range(1.5f, 3f);
            }
            
            lateralChangeTimer += Time.deltaTime;
            if (lateralChangeTimer >= 2f)
            {
                randomLateralOffset = UnityEngine.Random.Range(-lateralRandomness, lateralRandomness);
                lateralChangeTimer = 0f;
                Debug.Log($"[AIKart] {gameObject.name} Changed randomLateralOffset to: {randomLateralOffset}");
            }
        }
        
        float totalLateralOffset = lateralOffset + randomLateralOffset;
        Vector3 offsetPosition = splinePosition + splineNormal * totalLateralOffset;
        targetPosition = splinePath.transform.TransformPoint(offsetPosition);
        targetDirection = (targetPosition - transform.position).normalized;
        
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AIKart] {gameObject.name} - lateralOffset: {lateralOffset}, randomLateralOffset: {randomLateralOffset}, total: {totalLateralOffset}, isOvertaking: {isOvertaking}");
        }
        
        if (!isOvertaking && enableObstacleAvoidance)
        {
            ApplyObstacleAvoidance();
        }
        
        if (lastSplinePosition > 0.9f && currentSplinePosition < 0.1f)
        {
            lapCount++;
            networkLapCount.Value = lapCount;
        }
        lastSplinePosition = currentSplinePosition;
        
        networkSplinePosition.Value = currentSplinePosition;
        networkSpeed.Value = currentSpeed;
    }
    
    private void CheckAndExecuteOvertaking()
    {
        if (isOvertaking)
        {
            return;
        }
        
        if (!enableOvertaking) return;
        
        Collider[] nearbyColliders = Physics.OverlapSphere(transform.position, overtakingDetectionDistance);
        
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[AIKart] {gameObject.name} Checking overtaking - Found {nearbyColliders.Length} nearby colliders");
        }
        
        Transform rootTransform = transform.root;
        
        foreach (var col in nearbyColliders)
        {
            if (col.transform.root == rootTransform) continue;
            
            Transform otherRoot = col.transform.root;
            KartController otherKart = otherRoot.GetComponent<KartController>();
            if (otherKart == null)
            {
                otherKart = otherRoot.GetComponentInChildren<KartController>();
            }
            
            if (otherKart == null)
            {
                if (Time.frameCount % 60 == 0)
                {
                    Debug.Log($"[AIKart] {gameObject.name} Found collider but no KartController: {col.gameObject.name}, root: {otherRoot.name}");
                }
                continue;
            }
            
            Vector3 toOther = otherRoot.position - transform.position;
            float distanceToOther = toOther.magnitude;
            float forwardDistance = Vector3.Dot(toOther, transform.forward);
            
            if (Time.frameCount % 30 == 0 && forwardDistance > 0 && forwardDistance < 20f)
            {
                Debug.Log($"[AIKart] {gameObject.name} Found other kart: {otherRoot.name}, forwardDistance: {forwardDistance}, distance: {distanceToOther}");
            }
            
            if (forwardDistance < 1f || forwardDistance > overtakingDetectionDistance) continue;
            
            Rigidbody otherRb = otherRoot.GetComponent<Rigidbody>();
            if (otherRb == null)
            {
                otherRb = otherRoot.GetComponentInChildren<Rigidbody>();
            }
            
                if (otherRb != null)
                {
                    float otherSpeed = otherRb.linearVelocity.magnitude;
                float speedDifference = currentSpeed - otherSpeed;
                
                bool shouldOvertake = false;
                
                if (forwardDistance < aggressiveOvertakingDistance)
                {
                    shouldOvertake = true;
                    Debug.Log($"[AIKart] {gameObject.name} SHOULD OVERTAKE (aggressive)! forwardDistance: {forwardDistance}");
                }
                else if (forwardDistance < 15f)
                {
                    shouldOvertake = true;
                    Debug.Log($"[AIKart] {gameObject.name} SHOULD OVERTAKE (normal)! forwardDistance: {forwardDistance}, speedDiff: {speedDifference}");
                }
                
                if (shouldOvertake)
                {
                    float lookAheadT = (currentSplinePosition + lookAheadDistance / splineLength) % 1f;
                    Vector3 splineUp = SplineUtility.EvaluateUpVector(splinePath.Spline, lookAheadT);
                    Vector3 splineTangent = SplineUtility.EvaluateTangent(splinePath.Spline, lookAheadT);
                    Vector3 splineRight = Vector3.Cross(splineTangent, splineUp).normalized;
                    Vector3 worldNormal = splinePath.transform.TransformDirection(splineRight);
                    
                    Vector3 right = Vector3.Cross(transform.forward, worldNormal).normalized;
                    if (right.magnitude < 0.1f)
                    {
                        right = transform.right;
                    }
                    float sideOffset = Vector3.Dot(toOther, right);
                    
                    float targetOffset = 0f;
                    
                    bool leftClear = CheckLateralClear(-overtakingLaneWidth, worldNormal);
                    bool rightClear = CheckLateralClear(overtakingLaneWidth, worldNormal);
                    
                    Debug.Log($"[AIKart] {gameObject.name} leftClear: {leftClear}, rightClear: {rightClear}, sideOffset: {sideOffset}, worldNormal: {worldNormal}");
                    
                    if (leftClear && rightClear)
                    {
                        targetOffset = sideOffset > 0 ? -overtakingLaneWidth : overtakingLaneWidth;
                    }
                    else if (leftClear)
                    {
                        targetOffset = -overtakingLaneWidth;
                    }
                    else if (rightClear)
                    {
                        targetOffset = overtakingLaneWidth;
                    }
                    else
                    {
                        targetOffset = sideOffset > 0 ? -overtakingLaneWidth * 0.8f : overtakingLaneWidth * 0.8f;
                    }
                    
                    Debug.Log($"[AIKart] {gameObject.name} targetOffset: {targetOffset}, overtakingLaneWidth: {overtakingLaneWidth}");
                    
                    if (Mathf.Abs(targetOffset) > 0.5f)
                    {
                        Debug.Log($"[AIKart] {gameObject.name} STARTING OVERTAKE! lateralOffset set to: {targetOffset}");
                        isOvertaking = true;
                        overtakingTimer = 0f;
                        targetVehicle = otherRoot.gameObject;
                        originalTargetSpeed = targetSpeed;
                        lateralOffset = targetOffset;
                        targetSpeed = Mathf.Min(targetSpeed * overtakingAccelerationMultiplier, maxSpeed);
                        break;
                    }
                    else
                    {
                        Debug.Log($"[AIKart] {gameObject.name} targetOffset too small: {targetOffset}, forcing to minimum");
                        targetOffset = Mathf.Sign(targetOffset) * Mathf.Max(Mathf.Abs(targetOffset), overtakingLaneWidth * 0.5f);
                        isOvertaking = true;
                        overtakingTimer = 0f;
                        targetVehicle = otherRoot.gameObject;
                        originalTargetSpeed = targetSpeed;
                        lateralOffset = targetOffset;
                        targetSpeed = Mathf.Min(targetSpeed * overtakingAccelerationMultiplier, maxSpeed);
                        Debug.Log($"[AIKart] {gameObject.name} FORCED OVERTAKE with offset: {targetOffset}");
                        break;
                    }
                }
            }
            else
            {
                if (Time.frameCount % 60 == 0 && forwardDistance > 0 && forwardDistance < 20f)
                {
                    Debug.Log($"[AIKart] {gameObject.name} Found kart but no Rigidbody: {otherRoot.name}");
                }
            }
        }
    }
    
    private bool CheckLateralClear(float offset, Vector3 normal)
    {
        float lookAheadT = (currentSplinePosition + lookAheadDistance / splineLength) % 1f;
        Vector3 splinePos = SplineUtility.EvaluatePosition(splinePath.Spline, lookAheadT);
        Vector3 worldNormal = splinePath.transform.TransformDirection(normal);
        Vector3 checkPos = splinePath.transform.TransformPoint(splinePos) + worldNormal * offset;
        
        Collider[] colliders = Physics.OverlapSphere(checkPos, 2.5f);
        
        Transform rootTransform = transform.root;
        Transform targetRoot = targetVehicle != null ? targetVehicle.transform.root : null;
        
        foreach (var col in colliders)
        {
            Transform colRoot = col.transform.root;
            if (colRoot == rootTransform) continue;
            if (targetRoot != null && colRoot == targetRoot) continue;
            
            KartController kart = colRoot.GetComponent<KartController>();
            if (kart == null)
            {
                kart = colRoot.GetComponentInChildren<KartController>();
            }
            
            if (kart != null)
            {
                Vector3 toKart = colRoot.position - checkPos;
                float distance = toKart.magnitude;
                if (distance < 2.5f)
                {
                    float forwardDist = Vector3.Dot(toKart, transform.forward);
                    if (forwardDist > -2f && forwardDist < 10f)
                    {
                        return false;
                    }
                }
            }
        }
        
        return true;
    }
    
    
    private void UpdateOvertaking()
    {
        overtakingTimer += Time.deltaTime;
        
        if (targetVehicle != null)
        {
            Vector3 toTarget = targetVehicle.transform.position - transform.position;
            float forwardDistance = Vector3.Dot(toTarget, transform.forward);
            
            if (forwardDistance > 8f)
            {
                lateralOffset = Mathf.Lerp(lateralOffset, 0f, Time.deltaTime * 3f);
                
                if (Mathf.Abs(lateralOffset) < 0.5f || overtakingTimer > overtakingDuration * 1.5f)
                {
                    isOvertaking = false;
                    targetVehicle = null;
                    lateralOffset = 0f;
                    targetSpeed = originalTargetSpeed;
                }
            }
            else if (forwardDistance > 3f)
            {
                lateralOffset = Mathf.Lerp(lateralOffset, 0f, Time.deltaTime * 1.5f);
            }
        }
        else
        {
            lateralOffset = Mathf.Lerp(lateralOffset, 0f, Time.deltaTime * 2f);
            
            if (overtakingTimer > overtakingDuration)
            {
                isOvertaking = false;
                lateralOffset = 0f;
                targetSpeed = originalTargetSpeed;
            }
        }
    }
    
    private void ApplyObstacleAvoidance()
    {
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;
        Vector3 up = Vector3.up;
        
        RaycastHit hit;
        bool obstacleAhead = Physics.Raycast(
            transform.position + up * 0.5f,
            forward,
            out hit,
            detectionDistance,
            obstacleLayer
        );
        
        if (obstacleAhead)
        {
            isAvoidingObstacle = true;
            
            // Calculate distance to obstacle for dynamic response
            float distanceToObstacle = hit.distance;
            float avoidanceStrength = Mathf.Clamp01(1f - (distanceToObstacle / detectionDistance));
            
            // Use avoidanceAngle parameter to calculate directions
            float angleRad = avoidanceAngle * Mathf.Deg2Rad;
            Vector3 leftDir = (forward * Mathf.Cos(angleRad) - right * Mathf.Sin(angleRad)).normalized;
            Vector3 rightDir = (forward * Mathf.Cos(angleRad) + right * Mathf.Sin(angleRad)).normalized;
            
            // Check left and right directions
            RaycastHit leftHit, rightHit;
            bool leftClear = !Physics.Raycast(
                transform.position + up * 0.5f,
                leftDir,
                out leftHit,
                detectionDistance * 0.8f,
                obstacleLayer
            );
            
            bool rightClear = !Physics.Raycast(
                transform.position + up * 0.5f,
                rightDir,
                out rightHit,
                detectionDistance * 0.8f,
                obstacleLayer
            );
            
            // Check side directions for better awareness
            Vector3 leftSide = -right;
            Vector3 rightSide = right;
            RaycastHit leftSideHit, rightSideHit;
            bool leftSideClear = !Physics.Raycast(
                transform.position + up * 0.5f,
                leftSide,
                out leftSideHit,
                detectionDistance * 0.5f,
                obstacleLayer
            );
            
            bool rightSideClear = !Physics.Raycast(
                transform.position + up * 0.5f,
                rightSide,
                out rightSideHit,
                detectionDistance * 0.5f,
                obstacleLayer
            );
            
            // Decision making with priority
            if (leftClear && rightClear)
            {
                // Both clear, prefer right (or check which is closer to spline path)
                float rightStrength = Mathf.Lerp(0.2f, 0.5f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else if (leftClear && leftSideClear)
            {
                // Left is clear, turn left
                float leftStrength = Mathf.Lerp(0.3f, 0.6f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, leftDir, leftStrength);
            }
            else if (rightClear && rightSideClear)
            {
                // Right is clear, turn right
                float rightStrength = Mathf.Lerp(0.3f, 0.6f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else if (leftClear)
            {
                // Left clear but side blocked, gentle turn
                float leftStrength = Mathf.Lerp(0.2f, 0.4f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, leftDir, leftStrength);
            }
            else if (rightClear)
            {
                // Right clear but side blocked, gentle turn
                float rightStrength = Mathf.Lerp(0.2f, 0.4f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else
            {
                // All blocked, slow down smoothly
                float targetSlowSpeed = baseTargetSpeed * Mathf.Lerp(0.3f, 0.6f, distanceToObstacle / detectionDistance);
                targetSpeed = Mathf.Lerp(targetSpeed, targetSlowSpeed, Time.deltaTime * speedSmoothing * 2f);
                targetSpeed = Mathf.Max(targetSpeed, minSpeed);
            }
            
            // Reduce speed based on distance to obstacle
            if (distanceToObstacle < detectionDistance * 0.5f)
            {
                float speedReduction = Mathf.Lerp(0.5f, 0.8f, distanceToObstacle / (detectionDistance * 0.5f));
                float desiredSpeed = baseTargetSpeed * speedReduction;
                targetSpeed = Mathf.Lerp(targetSpeed, desiredSpeed, Time.deltaTime * speedSmoothing * 3f);
                targetSpeed = Mathf.Max(targetSpeed, minSpeed);
            }
        }
        else
        {
            // No obstacle ahead, restore speed smoothly
            if (isAvoidingObstacle)
            {
                targetSpeed = Mathf.Lerp(targetSpeed, baseTargetSpeed, Time.deltaTime * speedSmoothing);
                if (Mathf.Abs(targetSpeed - baseTargetSpeed) < 0.1f)
                {
                    targetSpeed = baseTargetSpeed;
                    isAvoidingObstacle = false;
                }
            }
        }
    }
    
    private void ApplyControls()
    {
        if (kartController == null) return;
        
        Vector3 localTarget = transform.InverseTransformDirection(targetDirection);
        float forwardComponent = Mathf.Max(localTarget.z, 0.01f);
        float signedAngle = Mathf.Atan2(localTarget.x, forwardComponent);
        float normalizedAngle = signedAngle / (Mathf.PI * 0.5f);
        float steerAngle = Mathf.Clamp(normalizedAngle * steerSensitivity, -1f, 1f);
        
        kartController.steer = new Vector2(steerAngle, 0);
        
        float adjustedTargetSpeed = targetSpeed + randomSpeedOffset * maxSpeed;
        adjustedTargetSpeed = Mathf.Clamp(adjustedTargetSpeed, minSpeed, maxSpeed);
        
        float speedDifference = adjustedTargetSpeed - currentSpeed;
        float speedRatio = currentSpeed / adjustedTargetSpeed;
        
        if (speedDifference > 1f)
        {
            kartController.gas = 1.0f;
            kartController.brake = 0f;
        }
        else if (speedDifference > 0.2f)
        {
            float accelerationInput = Mathf.Lerp(0.6f, 1.0f, speedDifference / adjustedTargetSpeed);
            kartController.gas = Mathf.Clamp01(accelerationInput);
            kartController.brake = 0f;
        }
        else if (speedDifference < -2f)
        {
            float brakeInput = Mathf.Clamp01(Mathf.Abs(speedDifference) / maxSpeed);
            kartController.gas = 0f;
            kartController.brake = brakeInput;
        }
        else if (speedDifference < -0.5f)
        {
            kartController.gas = 0f;
            kartController.brake = 0.3f;
        }
        else
        {
            if (speedRatio < 0.95f)
            {
                kartController.gas = 0.8f;
                kartController.brake = 0f;
            }
            else if (speedRatio > 1.05f)
            {
                kartController.gas = 0f;
                kartController.brake = 0.2f;
            }
            else
            {
                kartController.gas = 0.6f;
            kartController.brake = 0f;
            }
        }
        
        kartController.drift = Vector2.zero;
    }
    
    public void SetStartPosition(float normalizedPosition)
    {
        currentSplinePosition = Mathf.Clamp01(normalizedPosition);
        
        if (splinePath != null)
        {
            Vector3 startPos = splinePath.transform.TransformPoint(
                SplineUtility.EvaluatePosition(splinePath.Spline, currentSplinePosition)
            );
            transform.position = startPos;
            
            Vector3 direction = SplineUtility.EvaluateTangent(splinePath.Spline, currentSplinePosition);
            if (direction.magnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
        }
    }
    
    public void SetTargetSpeed(float speed)
    {
        targetSpeed = Mathf.Clamp(speed, minSpeed, maxSpeed);
        baseTargetSpeed = targetSpeed; // Update base speed when externally set
    }
    
    public void RecalculateSpeed()
    {
        CalculateSpeedParameters();
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
    
    void OnDrawGizmosSelected()
    {
        if (splinePath == null) return;
        
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(targetPosition, 1f);
        
        if (enableObstacleAvoidance)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawRay(transform.position + Vector3.up * 0.5f, transform.forward * detectionDistance);
        }
    }
}

