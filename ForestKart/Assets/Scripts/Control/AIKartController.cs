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
    
    [Range(1f, 2f)]
    public float aiSpeedBoost = 1.2f;
    
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
    
    [Header("Stability Control")]
    public bool enableStabilityControl = true;
    
    [Range(0f, 10f)]
    public float angularDamping = 3f;
    
    [Range(0f, 20f)]
    public float lateralCorrection = 10f;
    
    [Range(0f, 15f)]
    public float alignmentForce = 8f;
    
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

    [Header("Power Ups")]
    public bool enablePowerUps = true;
    [Range(0f, 1f)]
    public float usePowerUpChance = 0.3f;
    public float minPowerUpHoldTime = 1.0f;
    
    private KartController kartController;
    private Rigidbody rb;
    private PowerUpSystem powerUpSystem;
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
    private NetworkVariable<int> aiCharacterIndex = new NetworkVariable<int>(-1);
    
    private bool isOvertaking = false;
    private float overtakingTimer = 0f;
    private float lateralOffset = 0f;
    private GameObject targetVehicle = null;
    private float originalTargetSpeed = 0f;
    private float randomSpeedOffset = 0f;
    private float randomLateralOffset = 0f;
    private float speedChangeTimer = 0f;
    private float lateralChangeTimer = 0f;
    private bool hasPassedMidpoint = false;
    private float powerUpHoldTimer = 0f;
    
    private float baseTargetSpeed = 0f;
    private bool isAvoidingObstacle = false;
    
    void Start()
    {
        kartController = GetComponent<KartController>();
        rb = GetComponent<Rigidbody>();
        powerUpSystem = GetComponent<PowerUpSystem>();
        
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
        baseTargetSpeed = targetSpeed;
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        aiCharacterIndex.OnValueChanged += OnAICharacterIndexChanged;
        
        if (IsServer)
        {
            StartCoroutine(SpawnAICharacterDelayed());
        }
        else
        {
            if (aiCharacterIndex.Value != -1)
            {
                SpawnAIModel(aiCharacterIndex.Value);
            }
        }
    }
    
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        aiCharacterIndex.OnValueChanged -= OnAICharacterIndexChanged;
    }
    
    private void OnAICharacterIndexChanged(int oldVal, int newVal)
    {
        if (newVal != -1)
        {
            SpawnAIModel(newVal);
        }
    }
    
    private void SpawnAIModel(int index)
    {
        if (kartController != null && CharacterSelectionUI.Instance != null)
        {
            GameObject[] targetArray = CharacterSelectionUI.Instance.aiCharacterPrefabs;
            
            if (targetArray == null || targetArray.Length == 0)
            {
                targetArray = CharacterSelectionUI.Instance.characterPrefabs;
            }
            
            if (targetArray != null && 
                index >= 0 && 
                index < targetArray.Length)
            {
                GameObject aiCharacter = targetArray[index];
                if (aiCharacter != null)
                {
                    kartController.SpawnDriverModel(aiCharacter);
                }
            }
        }
    }

    private System.Collections.IEnumerator SpawnAICharacterDelayed()
    {
        yield return new WaitForSeconds(1.0f);
        
        if (kartController != null && CharacterSelectionUI.Instance != null)
        {
            int index = CharacterSelectionUI.Instance.GetRandomAvailableCharacterIndex();
            if (index != -1)
            {
                aiCharacterIndex.Value = index;
                SpawnAIModel(index); 
                Debug.Log($"[AIKart] Server selected character index: {index}");
            }
        }
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
        
        if (splineLength <= 0f && splinePath != null)
        {
            splineLength = splinePath.Spline.GetLength();
            if (splineLength > 0f && currentSplinePosition == 0f && lapCount == 0)
            {
                SetStartPosition(0f);
            }
        }
        
        UpdateAILogic();
        if (enablePowerUps)
        {
            UpdatePowerUpLogic();
        }
        ApplyControls();
        
        if (enableStabilityControl && rb != null)
        {
            ApplyStabilityForces();
        }
        
        if (rb != null && kartController != null && kartController.controlsEnabled)
        {
            ApplyAISpeedBoost();
        }
    }
    
    private void ApplyAISpeedBoost()
    {
        if (aiSpeedBoost <= 1f) return;
        
        Vector3 forward = transform.forward;
        Vector3 velocity = rb.linearVelocity;
        float currentSpeed = velocity.magnitude;
        
        float desiredSpeed = targetSpeed * aiSpeedBoost;
        desiredSpeed = Mathf.Clamp(desiredSpeed, minSpeed, maxSpeed * aiSpeedBoost);
        
        if (currentSpeed < desiredSpeed)
        {
            float speedDifference = desiredSpeed - currentSpeed;
            float boostForce = speedDifference * 50f * (aiSpeedBoost - 1f);
            rb.AddForce(forward * boostForce * Time.deltaTime, ForceMode.Force);
        }
    }
    
    private void UpdatePowerUpLogic()
    {
        if (powerUpSystem == null) return;
        
        if (powerUpSystem.HasPowerUp())
        {
            powerUpHoldTimer += Time.deltaTime;
            
            if (powerUpHoldTimer >= minPowerUpHoldTime)
            {
                if (UnityEngine.Random.value < usePowerUpChance * Time.deltaTime)
                {
                    powerUpSystem.UsePowerUp();
                    powerUpHoldTimer = 0f;
                }
            }
        }
        else
        {
            powerUpHoldTimer = 0f;
        }
    }
    
    private void UpdateAILogic()
    {
        currentSpeed = rb.linearVelocity.magnitude;
        float distanceTraveled = currentSpeed * Time.deltaTime;
        float normalizedDistance = distanceTraveled / splineLength;
        
        float rawPredictedPos = currentSplinePosition + normalizedDistance;
        float predictedPos = rawPredictedPos % 1f;
        float searchDist = Mathf.Max(10f, currentSpeed * Time.deltaTime * 2f);
        float searchRange = searchDist / splineLength;
        int searchSteps = 10;
        
        float bestT = predictedPos;
        float bestDistSq = float.MaxValue;
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
        }
        
        float positionChange = bestT - (currentSplinePosition % 1f);
        if (Mathf.Abs(positionChange) > 0.5f)
        {
            if (positionChange > 0.5f)
            {
                positionChange -= 1f;
            }
            else if (positionChange < -0.5f)
            {
                positionChange += 1f;
            }
        }
        
        if (Mathf.Abs(positionChange) > 0.05f)
        {
            bestT = (currentSplinePosition % 1f) + Mathf.Sign(positionChange) * 0.05f;
        }
        
        float previousPosition = lastSplinePosition;
        float newSplinePosition = bestT;
        
        if (newSplinePosition > 0.4f && newSplinePosition < 0.6f)
        {
            hasPassedMidpoint = true;
        }
        
        if (previousPosition > 0.9f && newSplinePosition < 0.1f && previousPosition != 0f)
        {
            if (hasPassedMidpoint)
            {
                lapCount++;
                networkLapCount.Value = lapCount;
                hasPassedMidpoint = false;
                Debug.Log($"[AIKart] {gameObject.name} completed lap {lapCount}! Previous: {previousPosition:F3}, New: {newSplinePosition:F3}");
            }
            else
            {
                Debug.LogWarning($"[AIKart] {gameObject.name} crossed finish line but skipped midpoint (Jitter ignored). Pos: {previousPosition:F3}->{newSplinePosition:F3}");
            }
        }
        
        currentSplinePosition = lapCount + newSplinePosition;
        lastSplinePosition = newSplinePosition;
        
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AIKart] {gameObject.name} - lastPos: {previousPosition:F3}, currentPos: {newSplinePosition:F3}, lapCount: {lapCount}, totalProgress: {currentSplinePosition:F3}");
        }
        
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
        
        networkSplinePosition.Value = newSplinePosition;
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
            
            float distanceToObstacle = hit.distance;
            float avoidanceStrength = Mathf.Clamp01(1f - (distanceToObstacle / detectionDistance));
            
            float angleRad = avoidanceAngle * Mathf.Deg2Rad;
            Vector3 leftDir = (forward * Mathf.Cos(angleRad) - right * Mathf.Sin(angleRad)).normalized;
            Vector3 rightDir = (forward * Mathf.Cos(angleRad) + right * Mathf.Sin(angleRad)).normalized;
            
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
            
            if (leftClear && rightClear)
            {
                float rightStrength = Mathf.Lerp(0.2f, 0.5f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else if (leftClear && leftSideClear)
            {
                float leftStrength = Mathf.Lerp(0.3f, 0.6f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, leftDir, leftStrength);
            }
            else if (rightClear && rightSideClear)
            {
                float rightStrength = Mathf.Lerp(0.3f, 0.6f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else if (leftClear)
            {
                float leftStrength = Mathf.Lerp(0.2f, 0.4f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, leftDir, leftStrength);
            }
            else if (rightClear)
            {
                float rightStrength = Mathf.Lerp(0.2f, 0.4f, avoidanceStrength);
                targetDirection = Vector3.Slerp(targetDirection, rightDir, rightStrength);
            }
            else
            {
                float targetSlowSpeed = baseTargetSpeed * Mathf.Lerp(0.3f, 0.6f, distanceToObstacle / detectionDistance);
                targetSpeed = Mathf.Lerp(targetSpeed, targetSlowSpeed, Time.deltaTime * speedSmoothing * 2f);
                targetSpeed = Mathf.Max(targetSpeed, minSpeed);
            }
            
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
        if (!kartController.controlsEnabled)
        {
            kartController.gas = 0f;
            kartController.brake = 0f;
            kartController.steer = Vector2.zero;
            kartController.drift = Vector2.zero;
            return;
        }
        
        Vector3 localTarget = transform.InverseTransformDirection(targetDirection);
        float forwardComponent = Mathf.Max(localTarget.z, 0.01f);
        float signedAngle = Mathf.Atan2(localTarget.x, forwardComponent);
        float normalizedAngle = signedAngle / (Mathf.PI * 0.5f);
        float steerAngle = Mathf.Clamp(normalizedAngle * steerSensitivity, -1f, 1f);
        
        kartController.steer = new Vector2(steerAngle, 0);
        
        float adjustedTargetSpeed = targetSpeed + randomSpeedOffset * maxSpeed;
        adjustedTargetSpeed *= aiSpeedBoost;
        adjustedTargetSpeed = Mathf.Clamp(adjustedTargetSpeed, minSpeed, maxSpeed * aiSpeedBoost);
        
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
    
    private void ApplyStabilityForces()
    {
        if (rb == null) return;
        
        if (angularDamping > 0f)
        {
            Vector3 angularVel = rb.angularVelocity;
            float angularSpeed = angularVel.magnitude;
            
            if (angularSpeed > 0.1f)
            {
                Vector3 dampedAngular = angularVel * (1f - angularDamping * Time.deltaTime);
                dampedAngular.y = angularVel.y * 0.95f;
                rb.angularVelocity = dampedAngular;
            }
        }
        
        if (lateralCorrection > 0f)
        {
            Vector3 velocity = rb.linearVelocity;
            Vector3 forward = transform.forward;
            
            float forwardSpeed = Vector3.Dot(velocity, forward);
            Vector3 lateralVelocity = velocity - forward * forwardSpeed;
            
            if (lateralVelocity.magnitude > 0.5f)
            {
                Vector3 correctionForce = -lateralVelocity * lateralCorrection;
                rb.AddForce(correctionForce, ForceMode.Force);
            }
        }
        
        if (alignmentForce > 0f && targetDirection.magnitude > 0.1f)
        {
            Vector3 currentForward = transform.forward;
            float alignmentDot = Vector3.Dot(currentForward, targetDirection);
            
            if (alignmentDot < 0.95f)
            {
                Vector3 cross = Vector3.Cross(currentForward, targetDirection);
                float alignmentStrength = (1f - alignmentDot) * alignmentForce;
                rb.AddTorque(cross * alignmentStrength, ForceMode.Force);
            }
        }
    }
    
    public void SetStartPosition(float normalizedPosition)
    {
        normalizedPosition = Mathf.Clamp01(normalizedPosition);
        
        if (IsServer)
        {
            lapCount = 0;
            networkLapCount.Value = 0;
            hasPassedMidpoint = false;
        }
        
        currentSplinePosition = normalizedPosition;
        lastSplinePosition = normalizedPosition;
        
        if (splinePath != null)
        {
            if (splineLength <= 0f)
            {
                splineLength = splinePath.Spline.GetLength();
            }
            
            Vector3 startPos = splinePath.transform.TransformPoint(
                SplineUtility.EvaluatePosition(splinePath.Spline, normalizedPosition)
            );
            transform.position = startPos;
            
            Vector3 direction = SplineUtility.EvaluateTangent(splinePath.Spline, normalizedPosition);
            if (direction.magnitude > 0.1f)
            {
                transform.rotation = Quaternion.LookRotation(direction);
            }
            
            Debug.Log($"[AIKart] {gameObject.name} SetStartPosition: {normalizedPosition:F3}, lapCount: {lapCount}, networkLapCount: {networkLapCount.Value}");
        }
    }
    
    public void SetTargetSpeed(float speed)
    {
        targetSpeed = Mathf.Clamp(speed, minSpeed, maxSpeed);
        baseTargetSpeed = targetSpeed;
    }
    
    public void RecalculateSpeed()
    {
        CalculateSpeedParameters();
    }
    
    public float GetTotalProgress()
    {
        if (splinePath == null || splineLength <= 0f)
        {
            return 0f;
        }
        
        if (IsServer)
        {
            return currentSplinePosition;
        }
        else
        {
            return networkLapCount.Value + networkSplinePosition.Value;
        }
    }
    
    public int GetLapCount()
    {
        int count = 0;
        if (IsServer)
        {
            count = lapCount;
        }
        else
        {
            count = networkLapCount.Value;
        }
        
        if (Time.frameCount % 60 == 0)
        {
            Debug.Log($"[AIKart] {gameObject.name} - LapCount: {count}, Progress: {GetTotalProgress():F3}, Server: {IsServer}, lapCount: {lapCount}, networkLapCount: {networkLapCount.Value}");
        }
        
        return count;
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
