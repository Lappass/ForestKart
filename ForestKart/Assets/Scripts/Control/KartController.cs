using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Cinemachine;

public class KartController : NetworkBehaviour
{
    public float gas;
    public float brake;
    public Vector2 steer;
    private Rigidbody rb;
    public WheelCollider[] driveWheels;
    public GameObject[] driveWheelMeshes;
    private Quaternion[] wheelMeshInitialRotations;
    private Quaternion[] rearWheelInitialLocalRotations; // Store initial rotation relative to car body for rear wheels
    private float[] rearWheelRollingAngles; // Accumulate rolling angle for rear wheels
    private bool wheelRotationsInitialized = false;
    public float DriveTorque = 100;
    public float BrakeTorque = 500;
    private float forwardTorque;
    public float Downforce = 100f; 
    public float SteerAngle = 30f;
    public bool BrakeAssist = false;
    public bool grounded = false;
    public bool reverse = false;
    public GameObject taillight;
    public Vector2 drift;
    private WheelFrictionCurve curve;
    private bool curveChange = false;
    public bool controlsEnabled = false;
    private bool hasBeenEnabled = false;
    
    [Header("Stability")]
    public Vector3 centerOfMassOffset = new Vector3(0f, -0.5f, 0f);
    
    [Header("Speed Limit")]
    public float maxSpeed = 0f;
    
    [Header("Spline Speed Penalty")]
    [Tooltip("Enable speed penalty when not on spline")]
    public bool enableSplineSpeedPenalty = true;
    
    [Tooltip("Layer mask for detecting spline. If set to Nothing, no speed penalty will be applied")]
    public LayerMask splineLayer = 1 << 0; // Default layer
    
    [Tooltip("Detection distance (downward raycast distance)")]
    public float detectionDistance = 5f;
    
    [Tooltip("Show debug info in Console")]
    public bool showDebugInfo = false;
    
    [Tooltip("Speed multiplier when not on spline (0-1, lower is slower)")]
    [Range(0.1f, 1f)]
    public float offSplineSpeedMultiplier = 0.7f;
    
    [Tooltip("Speed penalty smoothing")]
    [Range(0.1f, 10f)]
    public float speedPenaltySmoothing = 2f;
    
    [Header("Camera Settings")]
    public CinemachineCamera drivingCameraFront;
    public CinemachineCamera drivingCameraBack;
    public CinemachineCamera finishLineCamera;
    
    [Tooltip("Current driving camera view (true = front, false = back)")]
    private bool isFrontCamera = true;
    
    private bool camerasInitialized = false;
    private int savedFrontCameraPriority = 10;
    private int savedBackCameraPriority = 10;
    
    [Header("Character Model")]
    [Tooltip("Transform where the selected driver/character model should be instantiated. Driver model will be spawned here based on player selection.")]
    public Transform driverModelParent;
    
    [Tooltip("Local position offset for the driver model (relative to driverModelParent)")]
    public Vector3 driverModelPositionOffset = Vector3.zero;
    
    [Tooltip("Local rotation offset for the driver model (relative to driverModelParent)")]
    public Vector3 driverModelRotationOffset = Vector3.zero;
    
    [Tooltip("Current driver/character model GameObject (spawned based on player selection)")]
    private GameObject currentDriverModel;
    
    private float currentSpeedMultiplier = 1f;
    private float originalDriveTorque;
    private float originalMaxSpeed;
    
    void Awake()
    {
        // CRITICAL: Disable PlayerInput immediately in Awake
        // Will be enabled later only for local player
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
            playerInput.DeactivateInput();
            Debug.Log("[KartController] Awake: Disabled PlayerInput");
        }
        
        // CRITICAL: Disable cameras immediately in Awake (before any other script runs)
        // This ensures cameras are off during intro sequence in multiplayer
        if (drivingCameraFront != null)
        {
            drivingCameraFront.Priority = 0;
            drivingCameraFront.enabled = false;
            drivingCameraFront.gameObject.SetActive(false);
            Debug.Log("[KartController] Awake: Disabled drivingCameraFront");
        }
        if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = 0;
            drivingCameraBack.enabled = false;
            drivingCameraBack.gameObject.SetActive(false);
            Debug.Log("[KartController] Awake: Disabled drivingCameraBack");
        }
    }
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 7f;
        
        if (maxSpeed <= 0f)
        {
            maxSpeed = 50f;
        }
        
        originalDriveTorque = DriveTorque;
        originalMaxSpeed = maxSpeed;
        
        // Initialize wheel mesh rotation offsets
        if (driveWheels != null && driveWheelMeshes != null)
        {
            if (driveWheels.Length != driveWheelMeshes.Length)
            {
                Debug.LogWarning($"[KartController] Wheel count mismatch! driveWheels: {driveWheels.Length}, driveWheelMeshes: {driveWheelMeshes.Length}");
            }
            
            wheelMeshInitialRotations = new Quaternion[driveWheelMeshes.Length];
            rearWheelInitialLocalRotations = new Quaternion[driveWheelMeshes.Length];
            rearWheelRollingAngles = new float[driveWheelMeshes.Length];

            for (int i = 0; i < driveWheelMeshes.Length; i++)
            {
                if (i < driveWheels.Length && driveWheels[i] != null && driveWheelMeshes[i] != null)
                {
                    // Get initial world rotation of wheel collider
                    Vector3 wheelPos;
                    Quaternion wheelRot;
                    driveWheels[i].GetWorldPose(out wheelPos, out wheelRot);
                    
                    if (i >= 2)
                    {
                        rearWheelInitialLocalRotations[i] = Quaternion.Inverse(transform.rotation) * driveWheelMeshes[i].transform.rotation;
                        rearWheelRollingAngles[i] = 0f;
                        Debug.Log($"[KartController] Initialized rear wheel {i} relative to body.");
                    }
                    else
                    {
                        wheelMeshInitialRotations[i] = driveWheelMeshes[i].transform.rotation * Quaternion.Inverse(wheelRot);
                        Debug.Log($"[KartController] Initialized front wheel {i} rotation offset. Wheel: {driveWheels[i].name}, Mesh: {driveWheelMeshes[i].name}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[KartController] Wheel {i} or its mesh is null! Skipping rotation offset initialization.");
                }
            }
            wheelRotationsInitialized = true;
        }
        else
        {
            Debug.LogWarning("[KartController] driveWheels or driveWheelMeshes is null! Cannot initialize wheel rotation offsets.");
        }
        
        if (taillight != null)
        {
            taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
        }
        
        if (!hasBeenEnabled)
        {
            controlsEnabled = false;
        }
        
        // IMPORTANT: Disable cameras at start - they will be enabled after intro ends
        // This prevents cameras from being active during intro sequence
        if (drivingCameraFront != null)
        {
            drivingCameraFront.Priority = 0;
            drivingCameraFront.enabled = false;
            drivingCameraFront.gameObject.SetActive(false);
        }
        if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = 0;
            drivingCameraBack.enabled = false;
            drivingCameraBack.gameObject.SetActive(false);
        }
    }
    
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (rb != null)
        {
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 7f;
        }
        
        if (hasBeenEnabled && !controlsEnabled)
        {
            controlsEnabled = true;
        }
        
        StartCoroutine(DelayedSpawnDriverModel());
        
        if (IsOwner)
        {
            // Initialize camera: default to front camera, but check if intro is playing first
            StartCoroutine(InitializeCamerasAfterIntroCheck());
            StartCoroutine(WaitForPositionSync());
            StartCoroutine(SetupPlayerInputDelayed());
        }
        else
        {
            // Non-local player: ensure PlayerInput is completely disabled
            UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = false;
                playerInput.DeactivateInput();
                Debug.Log($"[KartController] OnNetworkSpawn: Disabled PlayerInput for non-owner {gameObject.name}");
            }
            
            // Disable cameras
            if (drivingCameraFront != null)
            {
                drivingCameraFront.Priority = 0;
                drivingCameraFront.enabled = false;
                drivingCameraFront.gameObject.SetActive(false);
            }
            if (drivingCameraBack != null)
            {
                drivingCameraBack.Priority = 0;
                drivingCameraBack.enabled = false;
                drivingCameraBack.gameObject.SetActive(false);
            }
        }
    }
    
    private System.Collections.IEnumerator DelayedSpawnDriverModel()
    {
        yield return new WaitForSeconds(0.5f);
        
        Debug.Log("[KartController] Attempting to spawn driver model...");
        
        ReplaceKartModel();
    }
    
    private void ReplaceKartModel()
    {
        Debug.Log("[KartController] ReplaceKartModel called");
        
        if (CharacterSelectionUI.Instance == null)
        {
            Debug.LogWarning("[KartController] CharacterSelectionUI.Instance is null!");
            return;
        }
        
        if (NetworkManager.Singleton == null)
        {
            Debug.LogWarning("[KartController] NetworkManager.Singleton is null!");
            return;
        }
        
        Debug.Log("[KartController] CharacterSelectionUI and NetworkManager found");
        
        ulong clientId = 0;
        NetworkObject playerNetObj = GetComponentInParent<NetworkObject>();
        
        if (playerNetObj != null && playerNetObj.IsSpawned)
        {
            clientId = playerNetObj.OwnerClientId;
            Debug.Log($"[KartController] Got clientId from NetworkObject OwnerClientId: {clientId}");
        }
        
        if (clientId == 0 && IsOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            clientId = NetworkManager.Singleton.LocalClientId;
            Debug.Log($"[KartController] Got clientId from LocalClientId: {clientId}");
        }
        
        if (clientId == 0 && playerNetObj != null && NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                if (client.Value.PlayerObject == playerNetObj)
                {
                    clientId = client.Key;
                    Debug.Log($"[KartController] Got clientId from ConnectedClients: {clientId}");
                    break;
                }
            }
        }
        
        if (clientId == 0 && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                if (client.Value.PlayerObject != null && client.Value.PlayerObject == playerNetObj)
                {
                    clientId = client.Key;
                    Debug.Log($"[KartController] Got clientId on server from ConnectedClients: {clientId}");
                    break;
                }
            }
        }
        
        if (clientId == 0)
        {
            Debug.LogWarning($"[KartController] Could not get clientId! IsOwner: {IsOwner}, IsServer: {NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer}, playerNetObj: {playerNetObj != null}");
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count > 0)
            {
                clientId = NetworkManager.Singleton.ConnectedClientsIds[0];
                Debug.Log($"[KartController] Using fallback clientId: {clientId}");
            }
            else
            {
                return;
            }
        }
        
        GameObject selectedCharacterPrefab = CharacterSelectionUI.Instance.GetPlayerCharacterPrefab(clientId);
        if (selectedCharacterPrefab == null)
        {
            Debug.LogWarning($"[KartController] No character prefab selected for client {clientId}");
            return;
        }
        
        Debug.Log($"[KartController] Selected character prefab: {selectedCharacterPrefab.name} for client {clientId}");
        
        SpawnDriverModel(selectedCharacterPrefab, clientId);
    }
    
    public void SpawnDriverModel(GameObject selectedCharacterPrefab, ulong clientId = 99999)
    {
        if (driverModelParent == null)
        {
            Debug.LogWarning("[KartController] driverModelParent is not set! Cannot spawn driver model.");
            return;
        }
        
        if (currentDriverModel != null)
        {
            Destroy(currentDriverModel);
            currentDriverModel = null;
        }
        
        GameObject driverModelFromPrefab = selectedCharacterPrefab;
        
        if (driverModelFromPrefab != null)
        {
            currentDriverModel = Instantiate(driverModelFromPrefab, driverModelParent);
            
            currentDriverModel.transform.localPosition = Vector3.zero;
            currentDriverModel.transform.localRotation = Quaternion.identity;
            currentDriverModel.transform.localScale = Vector3.one;
            
            currentDriverModel.transform.localPosition = driverModelPositionOffset;
            currentDriverModel.transform.localRotation = Quaternion.Euler(driverModelRotationOffset);
            
            Debug.Log($"[KartController] Driver model spawned - Parent: {driverModelParent.name}, LocalPos: {currentDriverModel.transform.localPosition}, WorldPos: {currentDriverModel.transform.position}, ParentWorldPos: {driverModelParent.position}, ParentLocalPos: {driverModelParent.localPosition}, Offset: {driverModelPositionOffset}");
            
            NetworkObject driverNetObj = currentDriverModel.GetComponent<NetworkObject>();
            if (driverNetObj != null) Destroy(driverNetObj);
            
            var netTransforms = currentDriverModel.GetComponentsInChildren<Unity.Netcode.Components.NetworkTransform>();
            foreach (var nt in netTransforms) Destroy(nt);
            
            var clientNetTransforms = currentDriverModel.GetComponentsInChildren<Component>();
            foreach (var comp in clientNetTransforms)
            {
                if (comp.GetType().Name.Contains("NetworkTransform"))
                {
                    Destroy(comp);
                }
            }

            Animator animator = currentDriverModel.GetComponent<Animator>();
            if (animator == null) animator = currentDriverModel.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.enabled = false;
            }
            
            currentDriverModel.transform.localPosition = driverModelPositionOffset;
            currentDriverModel.transform.localRotation = Quaternion.Euler(driverModelRotationOffset);
            
            Rigidbody driverRb = currentDriverModel.GetComponent<Rigidbody>();
            if (driverRb != null)
            {
                driverRb.isKinematic = true;
                Destroy(driverRb); 
            }
            
            KartController driverKartController = currentDriverModel.GetComponent<KartController>();
            if (driverKartController == null)
            {
                driverKartController = currentDriverModel.GetComponentInChildren<KartController>();
            }
            if (driverKartController != null)
            {
                driverKartController.enabled = false;
                Destroy(driverKartController);
            }
            
            Debug.Log($"[KartController] Successfully spawned driver model '{driverModelFromPrefab.name}' for client {clientId} under {driverModelParent.name} at position {currentDriverModel.transform.localPosition}");
        }
        else
        {
            Debug.LogError($"[KartController] Could not find driver model in prefab '{selectedCharacterPrefab.name}'. Please check the prefab structure.");
        }
    }
    
    private System.Collections.IEnumerator WaitForPositionSync()
    {
        yield return null;
        yield return null;
        
        Vector3 currentPos = transform.position;
        Vector3 prefabDefaultPos = new Vector3(22.42f, 0f, 21.33f);
        
        if (Vector3.Distance(currentPos, prefabDefaultPos) < 1f)
        {
            float waitTime = 0f;
            float maxWaitTime = 1f;
            
            while (waitTime < maxWaitTime && Vector3.Distance(transform.position, prefabDefaultPos) < 1f)
            {
                yield return null;
                waitTime += Time.deltaTime;
            }
        }
    }
    
    public void AdjustCameraForRaceFinish()
    {
        if (!IsOwner)
        {
            Debug.Log("[KartController] Not Owner, skipping camera switch");
            return;
        }
        
        Debug.Log("[KartController] Switching camera: driving -> finish line");
        
        // Disable both driving cameras
        if (drivingCameraFront != null)
        {
            drivingCameraFront.gameObject.SetActive(false);
            Debug.Log($"[KartController] Disabled driving front camera: {drivingCameraFront.name}");
        }
        
        if (drivingCameraBack != null)
        {
            drivingCameraBack.gameObject.SetActive(false);
            Debug.Log($"[KartController] Disabled driving back camera: {drivingCameraBack.name}");
        }
        
        if (finishLineCamera != null)
        {
            finishLineCamera.gameObject.SetActive(true);
            finishLineCamera.enabled = true;
            Debug.Log($"[KartController] Enabled finish line camera: {finishLineCamera.name}");
        }
        else
        {
            Debug.LogWarning("[KartController] Finish line camera not found!");
        }
        
        var localPlayerSetup = GetComponentInParent<LocalPlayerSetup>();
        if (localPlayerSetup != null)
        {
            localPlayerSetup.UpdateCameraReference(finishLineCamera);
            Debug.Log("[KartController] Updated LocalPlayerSetup camera reference");
        }
        
        Debug.Log("[KartController] Camera switch complete");
    }
    
    public void OnSwitchCamera(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (value.isPressed)
        {
            SwitchCamera();
        }
    }
    
    /// <summary>
    /// Initializes the driving cameras after checking if intro is playing
    /// </summary>
    private System.Collections.IEnumerator InitializeCamerasAfterIntroCheck()
    {
        // Wait a frame to ensure GameManager is initialized
        yield return null;
        
        // Save original priorities
        if (drivingCameraFront != null)
        {
            savedFrontCameraPriority = drivingCameraFront.Priority;
        }
        if (drivingCameraBack != null)
        {
            savedBackCameraPriority = drivingCameraBack.Priority;
        }
        
        // Ensure cameras are disabled and have low priority initially
        if (drivingCameraFront != null)
        {
            drivingCameraFront.Priority = 0; // Set to lowest priority
            drivingCameraFront.gameObject.SetActive(false);
            drivingCameraFront.enabled = false;
        }
        if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = 0; // Set to lowest priority
            drivingCameraBack.gameObject.SetActive(false);
            drivingCameraBack.enabled = false;
        }
        
        // Check if intro is playing
        bool isIntroPlaying = false;
        if (GameManager.Instance != null)
        {
            isIntroPlaying = GameManager.Instance.IsPlayingIntro();
        }
        
        // If intro is playing, keep cameras disabled and wait for it to finish
        if (isIntroPlaying)
        {
            while (GameManager.Instance != null && GameManager.Instance.IsPlayingIntro())
            {
                // Keep cameras disabled during intro
                if (drivingCameraFront != null)
                {
                    drivingCameraFront.Priority = 0;
                    drivingCameraFront.gameObject.SetActive(false);
                    drivingCameraFront.enabled = false;
                }
                if (drivingCameraBack != null)
                {
                    drivingCameraBack.Priority = 0;
                    drivingCameraBack.gameObject.SetActive(false);
                    drivingCameraBack.enabled = false;
                }
                yield return new WaitForSeconds(0.1f);
            }
            // Wait a bit more to ensure intro cameras are disabled
            yield return new WaitForSeconds(0.2f);
        }
        
        // Now initialize cameras
        InitializeDrivingCameras();
    }
    
    /// <summary>
    /// Initializes the driving cameras - sets front camera as active by default
    /// </summary>
    private void InitializeDrivingCameras()
    {
        isFrontCamera = true;
        camerasInitialized = true;
        
        // Disable back camera and restore its priority
        if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = savedBackCameraPriority;
            drivingCameraBack.gameObject.SetActive(false);
        }
        
        // Enable front camera and restore its priority
        if (drivingCameraFront != null)
        {
            drivingCameraFront.Priority = savedFrontCameraPriority;
            drivingCameraFront.gameObject.SetActive(true);
            drivingCameraFront.enabled = true;
            Debug.Log($"[KartController] Initialized with front camera (Priority: {savedFrontCameraPriority})");
        }
        else if (drivingCameraBack != null)
        {
            // Fallback: if front camera is not set, use back camera
            drivingCameraBack.Priority = savedBackCameraPriority;
            drivingCameraBack.gameObject.SetActive(true);
            drivingCameraBack.enabled = true;
            isFrontCamera = false;
            Debug.Log($"[KartController] Front camera not set, using back camera as fallback (Priority: {savedBackCameraPriority})");
        }
    }
    
    /// <summary>
    /// Gets the currently active driving camera (front or back)
    /// Returns null if intro is playing or cameras are not initialized
    /// </summary>
    public CinemachineCamera GetActiveDrivingCamera()
    {
        // Don't return camera if intro is playing or cameras are not initialized
        if (!camerasInitialized)
        {
            return null;
        }
        
        if (GameManager.Instance != null && GameManager.Instance.IsPlayingIntro())
        {
            return null;
        }
        
        if (isFrontCamera && drivingCameraFront != null)
        {
            return drivingCameraFront;
        }
        else if (!isFrontCamera && drivingCameraBack != null)
        {
            return drivingCameraBack;
        }
        // Fallback: return front camera if available, otherwise back
        return drivingCameraFront != null ? drivingCameraFront : drivingCameraBack;
    }
    
    private void SwitchCamera()
    {
        if (!IsOwner) return;
        
        isFrontCamera = !isFrontCamera;
        
        if (isFrontCamera)
        {
            // Switch to front camera
            if (drivingCameraBack != null)
            {
                drivingCameraBack.gameObject.SetActive(false);
            }
            if (drivingCameraFront != null)
            {
                drivingCameraFront.gameObject.SetActive(true);
                drivingCameraFront.enabled = true;
                Debug.Log("[KartController] Switched to front camera");
            }
        }
        else
        {
            // Switch to back camera
            if (drivingCameraFront != null)
            {
                drivingCameraFront.gameObject.SetActive(false);
            }
            if (drivingCameraBack != null)
            {
                drivingCameraBack.gameObject.SetActive(true);
                drivingCameraBack.enabled = true;
                Debug.Log("[KartController] Switched to back camera");
            }
        }
        
        // Update LocalPlayerSetup camera reference
        var localPlayerSetup = GetComponentInParent<LocalPlayerSetup>();
        if (localPlayerSetup != null)
        {
            CinemachineCamera activeCamera = GetActiveDrivingCamera();
            if (activeCamera != null)
            {
                localPlayerSetup.UpdateCameraReference(activeCamera);
            }
        }
    }
    
    private System.Collections.IEnumerator SetupPlayerInputDelayed()
    {
        yield return new WaitForSeconds(0.1f);
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = true;
            playerInput.ActivateInput();
        }
    }
    
    private void Update()
    {
        if (hasBeenEnabled && !controlsEnabled)
        {
            controlsEnabled = true;
        }
        
        if (!controlsEnabled) return;
        
        if (enableSplineSpeedPenalty)
        {
            UpdateSplineSpeedPenalty();
        }
        
        Drive(gas, brake,steer,drift);
        AddDownForce();
    }
    
    public void EnableControls()
    {
        hasBeenEnabled = true;
        controlsEnabled = true;
        
        if (IsOwner)
        {
            StartCoroutine(ActivateInputAfterDelay());
        }
    }
    
    /// <summary>
    /// Called when hit by Shell/projectile (called from server)
    /// </summary>
    public void OnHitByProjectile(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        // Check if this is an AI kart
        bool isAI = GetComponent<AIKartController>() != null;
        
        if (isAI)
        {
            // AI: Apply effect directly on server
            ApplyHitEffectOnServer(hitDirection, force, torque, stunDuration);
        }
        else
        {
            // Player: Use ClientRpc to let owner client apply effect
            ApplyProjectileHitClientRpc(hitDirection, force, torque, stunDuration);
        }
    }
    
    /// <summary>
    /// Apply hit effect directly on server (for AI karts)
    /// </summary>
    private void ApplyHitEffectOnServer(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        Debug.Log($"[KartController] AI {gameObject.name} hit by projectile on server! force: {force}");
        
        Rigidbody kartRb = GetComponent<Rigidbody>();
        if (kartRb != null)
        {
            kartRb.linearVelocity = kartRb.linearVelocity * 0.3f;
            kartRb.AddForce(hitDirection * force, ForceMode.Impulse);
            kartRb.AddTorque(Vector3.up * torque, ForceMode.Impulse);
        }
        
        StartCoroutine(StunCoroutine(stunDuration));
    }
    
    [ClientRpc]
    private void ApplyProjectileHitClientRpc(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        // Only owner executes physics effect (for player karts)
        if (!IsOwner) return;
        
        Debug.Log($"[KartController] Player {gameObject.name} hit by projectile! IsOwner: {IsOwner}, force: {force}");
        
        Rigidbody kartRb = GetComponent<Rigidbody>();
        if (kartRb != null)
        {
            kartRb.linearVelocity = kartRb.linearVelocity * 0.3f;
            kartRb.AddForce(hitDirection * force, ForceMode.Impulse);
            kartRb.AddTorque(Vector3.up * torque, ForceMode.Impulse);
        }
        
        StartCoroutine(StunCoroutine(stunDuration));
    }
    
    private System.Collections.IEnumerator StunCoroutine(float duration)
    {
        bool originalEnabled = controlsEnabled;
        controlsEnabled = false;
        gas = 0f;
        brake = 0f;
        steer = Vector2.zero;
        drift = Vector2.zero;
        
        Debug.Log($"[KartController] {gameObject.name} stunned for {duration} seconds");
        
        yield return new WaitForSeconds(duration);
        
        controlsEnabled = originalEnabled;
        Debug.Log($"[KartController] {gameObject.name} stun ended");
    }
    
    private System.Collections.IEnumerator ActivateInputAfterDelay()
    {
        yield return new WaitForSeconds(0.1f);
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            if (!playerInput.enabled)
            {
                playerInput.enabled = true;
            }
            playerInput.ActivateInput();
            
            if (playerInput.currentActionMap == null)
            {
                var actions = playerInput.actions;
                if (actions != null)
                {
                    var driveMap = actions.FindActionMap("Drive");
                    if (driveMap != null)
                    {
                        playerInput.SwitchCurrentActionMap("Drive");
                    }
                }
            }
        }
    }
    
    public void DisableControls()
    {
        hasBeenEnabled = false;
        controlsEnabled = false;
        gas = 0f;
        brake = 0f;
        steer = Vector2.zero;
        drift = Vector2.zero;
    }
    public void OnAccelerate(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (value.isPressed)
        {
            gas = 1;
        }
        if(!value.isPressed)
        {
            gas = 0;
        }
    }
    public void OnBrake(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (value.isPressed)
        {
            brake = 1;
            taillight.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
            if (BrakeAssist)
            {
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            }
        }
        if (!value.isPressed)
        {
            brake = 0;
            taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
            if (BrakeAssist)
            {
                rb.constraints = RigidbodyConstraints.None;
            }
        }
    }
    public void OnSteering(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        steer = value.Get<Vector2>();
    }
    public void OnReverse(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (reverse)
        {
            reverse = false;
        }
        else
        {
            reverse = true; 
        }
    }
    public void OnReset()
    {
        if (!IsOwner || !controlsEnabled) return;
        
        transform.rotation = new Quaternion(0, 0, 0, 0);
    }
    public void OnDrift(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        drift = value.Get<Vector2>().normalized;
    }
    
    public void OnUsePowerUp(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (value.isPressed)
        {
            PowerUpSystem powerUpSystem = GetComponent<PowerUpSystem>();
            if (powerUpSystem != null)
            {
                powerUpSystem.UsePowerUp();
            }
        }
    }
    
    private void Drive(float acceleration, float brake,Vector2 steer, Vector2 drift)
    {
        float effectiveMaxSpeed = maxSpeed * currentSpeedMultiplier;
        float effectiveDriveTorque = DriveTorque * currentSpeedMultiplier;
        
        if (effectiveMaxSpeed > 0f && (IsServer || IsOwner))
        {
            Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            float horizontalSpeed = horizontalVelocity.magnitude;
            
            if (horizontalSpeed >= effectiveMaxSpeed * 0.95f)
            {
                if (acceleration > 0f)
                {
                    acceleration = 0f;
                }
            }
            
            if (horizontalSpeed > effectiveMaxSpeed)
            {
                Vector3 limitedVelocity = horizontalVelocity.normalized * effectiveMaxSpeed;
                limitedVelocity.y = rb.linearVelocity.y;
                rb.linearVelocity = limitedVelocity;
            }
        }
        
        if (!reverse)
        {
            forwardTorque = acceleration * effectiveDriveTorque;
        }
        else if (reverse)
        {
            forwardTorque = -acceleration * effectiveDriveTorque;
        }
            brake *= BrakeTorque;
        steer.x = steer.x * SteerAngle;
        for (int i = 0;i< driveWheels.Length; i++)
        {
            Vector3 wheelposition;
            Quaternion wheelrotation;
            driveWheels[i].GetWorldPose(out wheelposition, out wheelrotation);
            driveWheelMeshes[i].transform.position = wheelposition;
            
            // Apply rotation
            if (wheelRotationsInitialized)
            {
                if (i >= 2 && i < rearWheelInitialLocalRotations.Length)
                {
                    float rpm = driveWheels[i].rpm;
                    float deltaAngle = rpm * 6f * Time.deltaTime; 
                    rearWheelRollingAngles[i] = (rearWheelRollingAngles[i] + deltaAngle) % 360f;
                    Quaternion baseRotation = transform.rotation * rearWheelInitialLocalRotations[i];
                    driveWheelMeshes[i].transform.rotation = baseRotation * Quaternion.AngleAxis(rearWheelRollingAngles[i], Vector3.forward);
                }
                else if (i < wheelMeshInitialRotations.Length)
                {
                    driveWheelMeshes[i].transform.rotation = wheelrotation * wheelMeshInitialRotations[i];
                }
                else
                {
                    driveWheelMeshes[i].transform.rotation = wheelrotation;
                }
            }
            else
            {
                driveWheelMeshes[i].transform.rotation = wheelrotation;
            }
        }
        for(int i = 0; i < driveWheels.Length; i++)
        {
            driveWheels[i].motorTorque = forwardTorque;
            driveWheels[i].brakeTorque = brake;
            if(i < 2)
            {
                driveWheels[i].steerAngle = steer.x;
            }
        }
        if (drift.x > 0)
        {
             rb.angularVelocity = new Vector3(0,0.5f,0);
             if(curveChange == false)
             {
                curveChange = true;
                SteerAngle = 40f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 0.5f;
                    curve.extremumSlip = 8.0f;
                    driveWheels[i].sidewaysFriction = curve;
                }
             }
        }
        if (drift.x < 0)
        {
            rb.angularVelocity = new Vector3(0, -0.5f, 0);
            if(curveChange == false)
            {
                curveChange = true;
                SteerAngle = 40f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 0.5f;
                    curve.extremumSlip = 8.0f;
                    driveWheels[i].sidewaysFriction = curve;
                }
            }
        }
        if(drift.x == 0)
        {
            if(curveChange == true)
            {
                curveChange = false;
                SteerAngle = 30f;
                for(int i = 2; i < driveWheels.Length; i++)
                {
                    curve = driveWheels[i].sidewaysFriction;
                    curve.stiffness = 1.0f;
                    curve.extremumSlip = 0.2f;
                    driveWheels[i].sidewaysFriction = curve;
                }
            }
        }
    }
    private void AddDownForce()
    {
        if (grounded)
        {
            rb.AddForce(-transform.up * Downforce * rb.linearVelocity.magnitude);
        }
        
        if (IsServer || IsOwner)
        {
            if (rb.linearVelocity.y > 2f && !grounded)
            {
                rb.AddForce(Vector3.down * 500f);
            }
            
            if (rb.linearVelocity.y > 8f)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.y = Mathf.Clamp(velocity.y, -10f, 8f);
                rb.linearVelocity = velocity;
            }
            
            float effectiveMaxSpeed = maxSpeed * currentSpeedMultiplier;
            if (effectiveMaxSpeed > 0f)
            {
                Vector3 horizontalVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
                float horizontalSpeed = horizontalVelocity.magnitude;
                
                if (horizontalSpeed > effectiveMaxSpeed)
                {
                    Vector3 limitedVelocity = horizontalVelocity.normalized * effectiveMaxSpeed;
                    limitedVelocity.y = rb.linearVelocity.y;
                    rb.linearVelocity = limitedVelocity;
                }
            }
        }
        
        for (int i = 2; i < driveWheels.Length; i++)
        {
            WheelHit wheelHit;
            driveWheels[i].GetGroundHit(out wheelHit);
            if (wheelHit.normal == Vector3.zero)
            {
                grounded = false;
                StartCoroutine(SetConstraints());
            }
            else
            {
                grounded = true;
            }
        }
    }
    IEnumerator SetConstraints()
    {
        yield return new WaitForSeconds(0.1f);
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        yield return new WaitForSeconds(0.1f);
        rb.constraints = RigidbodyConstraints.None;
    }
    
    private bool CheckIfOnSpline()
    {
        // Raycast downward to detect if on spline layer
        RaycastHit hit;
        Vector3 rayOrigin = transform.position;
        Vector3 rayDirection = Vector3.down;
        
        // Check from kart center
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, detectionDistance, splineLayer))
        {
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[KartController] Detected spline layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            }
            return true;
        }
        
        // Check from wheel positions (more accurate)
        if (driveWheels != null && driveWheels.Length > 0)
        {
            int wheelsOnSpline = 0;
            foreach (var wheel in driveWheels)
            {
                if (wheel != null)
                {
                    Vector3 wheelPos = wheel.transform.position;
                    if (Physics.Raycast(wheelPos, Vector3.down, out hit, detectionDistance, splineLayer))
                    {
                        wheelsOnSpline++;
                    }
                }
            }
            
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[KartController] Wheel detection: {wheelsOnSpline}/{driveWheels.Length} on spline");
            }
            
            // If at least half of the wheels are on spline, consider on spline
            return wheelsOnSpline >= driveWheels.Length / 2;
        }
        
        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[KartController] No spline layer detected, current layer mask: {splineLayer.value}");
        }
        
        return false;
    }
    
    private void UpdateSplineSpeedPenalty()
    {
        // If splineLayer is not set, default to no speed penalty
        if (splineLayer.value == 0)
        {
            currentSpeedMultiplier = 1f;
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.LogWarning("[KartController] splineLayer not set, defaulting to no speed penalty! Please set splineLayer in Inspector.");
            }
            return;
        }
        
        bool isOnSpline = CheckIfOnSpline();
        float targetMultiplier = isOnSpline ? 1f : offSplineSpeedMultiplier;
        
        currentSpeedMultiplier = Mathf.Lerp(currentSpeedMultiplier, targetMultiplier, Time.deltaTime * speedPenaltySmoothing);
        
        if (showDebugInfo && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[KartController] isOnSpline: {isOnSpline}, currentSpeedMultiplier: {currentSpeedMultiplier:F2}, targetMultiplier: {targetMultiplier:F2}");
        }
    }
    
}
