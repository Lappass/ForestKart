using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine.Splines;
using Unity.Mathematics;

public class KartController : NetworkBehaviour
{
    public float gas;
    public float brake;
    public Vector2 steer;
    private Rigidbody rb;
    public WheelCollider[] driveWheels;
    public GameObject[] driveWheelMeshes;
    private Quaternion[] wheelMeshInitialRotations;
    private Quaternion[] rearWheelInitialLocalRotations;
    private float[] rearWheelRollingAngles;
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
    public bool enableSplineSpeedPenalty = true;

    [Header("Respawn Settings")]
    public bool enableRespawn = true;
    public float respawnDelay = 3f;
    public float fallRespawnY = -10f;
    public float respawnBacktrackTime = 2.0f;
    
    private struct PositionSnapshot
    {
        public Vector3 position;
        public Quaternion rotation;
        public float timestamp;
    }
    
    private List<PositionSnapshot> validPositionHistory = new List<PositionSnapshot>();
    private float snapshotInterval = 0.2f;
    private float lastSnapshotTime = 0f;
    private float offTrackTimer = 0f;
    private float respawnGracePeriod = 0f;

    private float inputGas = 0f;
    private bool inputBrake = false;
    private bool isAI = false;
    
    [Tooltip("Layer mask for detecting spline. If set to Nothing, no speed penalty will be applied")]
    public LayerMask splineLayer = 1 << 0;
    
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
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponentInParent<UnityEngine.InputSystem.PlayerInput>();
        if (playerInput != null)
        {
            playerInput.enabled = false;
            playerInput.DeactivateInput();
        }
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
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += centerOfMassOffset;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.maxAngularVelocity = 7f;
        
        ConfigurePhysics();
        
        if (maxSpeed <= 0f)
        {
            maxSpeed = 100f;
        }
        else
        {
            maxSpeed *= 1.6f;
        }
        
        DriveTorque *= 2.0f;
        Downforce *= 1.8f;
        
        originalDriveTorque = DriveTorque;
        originalMaxSpeed = maxSpeed;
        
        isAI = GetComponent<AIKartController>() != null || GetComponentInParent<AIKartController>() != null;

        validPositionHistory.Add(new PositionSnapshot 
        { 
            position = transform.position, 
            rotation = transform.rotation, 
            timestamp = Time.time 
        });
        
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
                    Vector3 wheelPos;
                    Quaternion wheelRot;
                    driveWheels[i].GetWorldPose(out wheelPos, out wheelRot);
                    
                    if (i >= 2)
                    {
                        rearWheelInitialLocalRotations[i] = Quaternion.Inverse(transform.rotation) * driveWheelMeshes[i].transform.rotation;
                        rearWheelRollingAngles[i] = 0f;
                    }
                    else
                    {
                        wheelMeshInitialRotations[i] = driveWheelMeshes[i].transform.rotation * Quaternion.Inverse(wheelRot);
                    }
                }
                else
                {
                }
            }
            wheelRotationsInitialized = true;
        }
        else
        {
        }
        
        if (taillight != null)
        {
            taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
        }
        
        if (!hasBeenEnabled)
        {
            controlsEnabled = false;
        }
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
    
    private void ConfigurePhysics()
    {
        PhysicsMaterial bouncyMat = new PhysicsMaterial("KartBouncy");
        bouncyMat.bounciness = 0.8f;
        bouncyMat.bounceCombine = PhysicsMaterialCombine.Maximum;
        bouncyMat.dynamicFriction = 0.6f;
        bouncyMat.staticFriction = 0.6f;
        
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.sharedMaterial = bouncyMat;
        }
        
        if (rb != null)
        {
            rb.mass *= 2f;
        }
        
        if (driveWheels != null)
        {
            foreach (var wheel in driveWheels)
            {
                if (wheel != null)
                {
                    WheelFrictionCurve forward = wheel.forwardFriction;
                    forward.stiffness *= 1.5f;
                    wheel.forwardFriction = forward;
                    
                    WheelFrictionCurve sideways = wheel.sidewaysFriction;
                    sideways.stiffness *= 1.3f;
                    wheel.sidewaysFriction = sideways;
                }
            }
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
            StartCoroutine(InitializeCamerasAfterIntroCheck());
            StartCoroutine(WaitForPositionSync());
            StartCoroutine(SetupPlayerInputDelayed());
        }
        else
        {
            UnityEngine.InputSystem.PlayerInput playerInput = GetComponentInParent<UnityEngine.InputSystem.PlayerInput>();
            if (playerInput != null)
            {
                playerInput.enabled = false;
                playerInput.DeactivateInput();
            }
            
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
        
        ReplaceKartModel();
    }
    
    private void ReplaceKartModel()
    {
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
        
        ulong clientId = 0;
        NetworkObject playerNetObj = GetComponentInParent<NetworkObject>();
        
        if (playerNetObj != null && playerNetObj.IsSpawned)
        {
            clientId = playerNetObj.OwnerClientId;
        }
        
        if (clientId == 0 && IsOwner && NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClient != null)
        {
            clientId = NetworkManager.Singleton.LocalClientId;
        }
        
        if (clientId == 0 && playerNetObj != null && NetworkManager.Singleton != null)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClients)
            {
                if (client.Value.PlayerObject == playerNetObj)
                {
                    clientId = client.Key;
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

            if (!IsOwner)
            {
                var playerInputs = currentDriverModel.GetComponentsInChildren<UnityEngine.InputSystem.PlayerInput>(true);
                foreach (var input in playerInputs)
                {
                    input.enabled = false;
                    input.DeactivateInput();
                }
            }
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
            return;
        }
        
        if (drivingCameraFront != null)
        {
            drivingCameraFront.gameObject.SetActive(false);
        }
        
        if (drivingCameraBack != null)
        {
            drivingCameraBack.gameObject.SetActive(false);
        }
        
        if (finishLineCamera != null)
        {
            finishLineCamera.gameObject.SetActive(true);
            finishLineCamera.enabled = true;
        }
        else
        {
            Debug.LogWarning("[KartController] Finish line camera not found!");
        }
        
        var localPlayerSetup = GetComponentInParent<LocalPlayerSetup>();
        if (localPlayerSetup != null)
        {
            localPlayerSetup.UpdateCameraReference(finishLineCamera);
        }
    }
    
    public void OnSwitchCamera(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        if (value.isPressed)
        {
            SwitchCamera();
        }
    }
    private System.Collections.IEnumerator InitializeCamerasAfterIntroCheck()
    {
        yield return null;
        
        if (drivingCameraFront != null)
        {
            savedFrontCameraPriority = drivingCameraFront.Priority;
        }
        if (drivingCameraBack != null)
        {
            savedBackCameraPriority = drivingCameraBack.Priority;
        }
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
        bool isIntroPlaying = false;
        if (GameManager.Instance != null)
        {
            isIntroPlaying = GameManager.Instance.IsPlayingIntro();
        }
        
        if (isIntroPlaying)
        {
            while (GameManager.Instance != null && GameManager.Instance.IsPlayingIntro())
            {
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
            yield return new WaitForSeconds(0.2f);
        }
        InitializeDrivingCameras();
    }
    private void InitializeDrivingCameras()
    {
        isFrontCamera = true;
        camerasInitialized = true;
        if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = savedBackCameraPriority;
            drivingCameraBack.gameObject.SetActive(false);
        }
        if (drivingCameraFront != null)
        {
            drivingCameraFront.Priority = savedFrontCameraPriority;
            drivingCameraFront.gameObject.SetActive(true);
            drivingCameraFront.enabled = true;
        }
        else if (drivingCameraBack != null)
        {
            drivingCameraBack.Priority = savedBackCameraPriority;
            drivingCameraBack.gameObject.SetActive(true);
            drivingCameraBack.enabled = true;
            isFrontCamera = false;
        }
    }
    public CinemachineCamera GetActiveDrivingCamera()
    {
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
        return drivingCameraFront != null ? drivingCameraFront : drivingCameraBack;
    }
    
    private void SwitchCamera()
    {
        if (!IsOwner) return;
        
        isFrontCamera = !isFrontCamera;
        
        if (isFrontCamera)
        {
            if (drivingCameraBack != null)
            {
                drivingCameraBack.gameObject.SetActive(false);
            }
            if (drivingCameraFront != null)
            {
                drivingCameraFront.gameObject.SetActive(true);
                drivingCameraFront.enabled = true;
            }
        }
        else
        {
            if (drivingCameraFront != null)
            {
                drivingCameraFront.gameObject.SetActive(false);
            }
            if (drivingCameraBack != null)
            {
                drivingCameraBack.gameObject.SetActive(true);
                drivingCameraBack.enabled = true;
            }
        }
        
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
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponentInParent<UnityEngine.InputSystem.PlayerInput>();
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
        
        if (enableRespawn)
        {
            UpdateRespawnLogic();
        }
        
        if (!isAI && controlsEnabled)
        {
            UpdatePlayerControls();
        }

        Drive(gas, brake,steer,drift);
    }
    
    private void FixedUpdate()
    {
        if (!controlsEnabled) return;
        AddDownForce();
    }
    
    private void UpdatePlayerControls()
    {
        if (inputBrake)
        {
            float forwardSpeed = transform.InverseTransformDirection(rb.linearVelocity).z;
            
            if (forwardSpeed > 1.0f)
            {
                brake = 1f;
                reverse = false;
                gas = inputGas; 
            }
            else
            {
                brake = 0f;
                reverse = true;
                gas = 1f; 
            }
        }
        else
        {
            brake = 0f;
            reverse = false;
            gas = inputGas;
        }
        
        if (taillight != null)
        {
            if (inputBrake)
                taillight.GetComponent<Renderer>().material.EnableKeyword("_EMISSION");
            else
                taillight.GetComponent<Renderer>().material.DisableKeyword("_EMISSION");
        }
        
        if (BrakeAssist)
        {
             if (brake > 0)
                rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
             else
                rb.constraints = RigidbodyConstraints.None;
        }
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
    
    public void OnHitByProjectile(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        bool isAI = GetComponent<AIKartController>() != null || 
                    GetComponentInParent<AIKartController>() != null ||
                    transform.root.GetComponent<AIKartController>() != null;
        
        if (isAI)
        {
            if (IsServer)
            {
                ApplyHitEffectOnServer(hitDirection, force, torque, stunDuration);
            }
            else
            {
                Debug.LogError($"[KartController] AI kart {gameObject.name} hit but not on server! This should not happen.");
            }
        }
        else
        {
            ApplyProjectileHitClientRpc(hitDirection, force, torque, stunDuration);
        }
    }
    
    public void OnEnterWaterPuddle(float blurDuration)
    {
        if (!IsOwner) return;
        
        AIKartController aiController = GetComponent<AIKartController>();
        if (aiController != null) return;
        
        if (ScreenBlurEffect.Instance != null)
        {
            ScreenBlurEffect.Instance.ApplyBlur(blurDuration);
        }
    }
    
    public void OnHitByPopcorn(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        bool isAI = GetComponent<AIKartController>() != null || 
                    GetComponentInParent<AIKartController>() != null ||
                    transform.root.GetComponent<AIKartController>() != null;
        
        if (isAI)
        {
            if (IsServer)
            {
                ApplyHitEffectOnServer(hitDirection, force, torque, stunDuration);
            }
            else
            {
                Debug.LogError($"[KartController] AI kart {gameObject.name} hit by popcorn but not on server! This should not happen.");
            }
        }
        else
        {
            ApplyProjectileHitClientRpc(hitDirection, force, torque, stunDuration);
        }
    }
   
    private void ApplyHitEffectOnServer(Vector3 hitDirection, float force, float torque, float stunDuration)
    {
        if (!IsServer)
        {
            Debug.LogError($"[KartController] ApplyHitEffectOnServer called on non-server for {gameObject.name}!");
            return;
        }
        
        Rigidbody kartRb = GetComponent<Rigidbody>();
        if (kartRb == null)
        {
            kartRb = GetComponentInParent<Rigidbody>();
        }
        if (kartRb == null)
        {
            kartRb = transform.root.GetComponent<Rigidbody>();
        }
        if (kartRb == null)
        {
            Debug.LogError($"[KartController] Could not find Rigidbody on AI {gameObject.name} (checked self, parent, root)!");
        }
        else
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
        if (!IsOwner) return;
        
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
        
        yield return new WaitForSeconds(duration);
        
        controlsEnabled = originalEnabled;
    }
    
     private System.Collections.IEnumerator ActivateInputAfterDelay()
    {
        yield return new WaitForSeconds(0.1f);
        
        UnityEngine.InputSystem.PlayerInput playerInput = GetComponentInParent<UnityEngine.InputSystem.PlayerInput>();
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
        
        inputGas = value.isPressed ? 1f : 0f;
    }
    public void OnBrake(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        inputBrake = value.isPressed;
    }
    public void OnSteering(InputValue value)
    {
        if (!IsOwner || !controlsEnabled) return;
        
        steer = value.Get<Vector2>();
    }
    public void OnReverse(InputValue value)
    {
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
    
    private void UpdateRespawnLogic()
    {
        if (splineLayer.value == 0) return;

        if (!IsServer && !IsOwner) return;

        if (respawnGracePeriod > 0f)
        {
            respawnGracePeriod -= Time.deltaTime;
            return;
        }

        bool isOnSpline = CheckIfOnSpline();

        if (isOnSpline)
        {
            offTrackTimer = 0f;
            
            if (Time.time - lastSnapshotTime > snapshotInterval)
            {
                lastSnapshotTime = Time.time;
                validPositionHistory.Add(new PositionSnapshot
                {
                    position = transform.position,
                    rotation = transform.rotation,
                    timestamp = Time.time
                });
                float maxHistoryTime = Mathf.Max(10f, respawnBacktrackTime * 2f);
                
                while (validPositionHistory.Count > 0 && Time.time - validPositionHistory[0].timestamp > maxHistoryTime)
                {
                    validPositionHistory.RemoveAt(0);
                }
            }
        }
        else
        {
            offTrackTimer += Time.deltaTime;
        }
        if (offTrackTimer > respawnDelay || transform.position.y < fallRespawnY)
        {
            RespawnKart();
        }
    }

    public void RespawnKart()
    {
        offTrackTimer = 0f;
        respawnGracePeriod = 1.0f;
        Vector3 respawnPos = transform.position;
        Quaternion respawnRot = transform.rotation;
        bool foundValidPos = false;
        if (validPositionHistory.Count > 0)
        {
            float targetTime = Time.time - respawnBacktrackTime;
            PositionSnapshot bestSnapshot = validPositionHistory[0];
            float minTimeDiff = Mathf.Abs(bestSnapshot.timestamp - targetTime);
            
            for (int i = 0; i < validPositionHistory.Count; i++)
            {
                float diff = Mathf.Abs(validPositionHistory[i].timestamp - targetTime);
                if (diff < minTimeDiff)
                {
                    minTimeDiff = diff;
                    bestSnapshot = validPositionHistory[i];
                }
            }
            
            respawnPos = bestSnapshot.position;
            respawnRot = bestSnapshot.rotation;
            foundValidPos = true;
            if (GameManager.Instance != null && GameManager.Instance.raceTrack != null)
            {
                var spline = GameManager.Instance.raceTrack;
                using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
                {
                    float t;
                    Unity.Mathematics.float3 nearest;
                    float dSq = UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, respawnPos, out nearest, out t);
                    if (dSq < 100f)
                    {
                        Vector3 splinePos = nearest;
                        Vector3 splineTangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(nativeSpline, t);
                        respawnPos = new Vector3(splinePos.x, respawnPos.y, splinePos.z);
                        if (splineTangent.magnitude > 0.1f)
                        {
                            splineTangent.y = 0;
                            splineTangent.Normalize();
                            respawnRot = Quaternion.LookRotation(splineTangent);
                        }
                    }
                }
            }
        }
        
        if (!foundValidPos)
        {
            if (GameManager.Instance != null && GameManager.Instance.raceTrack != null)
            {
                var spline = GameManager.Instance.raceTrack;
                using (var nativeSpline = new UnityEngine.Splines.NativeSpline(spline.Spline, spline.transform.localToWorldMatrix, Unity.Collections.Allocator.Temp))
                {
                    float t;
                    Unity.Mathematics.float3 nearest;
                    UnityEngine.Splines.SplineUtility.GetNearestPoint(nativeSpline, transform.position, out nearest, out t);
                    
                    Vector3 splinePos = nearest;
                    Vector3 splineTangent = UnityEngine.Splines.SplineUtility.EvaluateTangent(nativeSpline, t);
                    
                    respawnPos = splinePos;
                    if (splineTangent.magnitude > 0.1f)
                    {
                        splineTangent.y = 0;
                        splineTangent.Normalize();
                        respawnRot = Quaternion.LookRotation(splineTangent);
                    }
                    foundValidPos = true;
                }
            }
            
            if (!foundValidPos)
            {
                respawnPos = transform.position + Vector3.up * 5f;
                Debug.LogWarning("[KartController] No valid history or spline found for respawn!");
            }
        }
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 finalPos = respawnPos + Vector3.up * 2.0f;
            var netTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (netTransform == null)
            {
                netTransform = GetComponentInParent<Unity.Netcode.Components.NetworkTransform>();
            }
            
            bool wasEnabled = true;
            if (netTransform != null)
            {
                wasEnabled = netTransform.enabled;
                netTransform.enabled = false;
            }
            rb.position = finalPos;
            rb.rotation = respawnRot;
            transform.position = finalPos;
            transform.rotation = respawnRot;
            if (netTransform != null && wasEnabled)
            {
                StartCoroutine(ReenableNetworkTransform(netTransform));
            }
        }
        else
        {
            transform.position = respawnPos + Vector3.up * 2.0f;
            transform.rotation = respawnRot;
        }
        
    }
    
    private System.Collections.IEnumerator ReenableNetworkTransform(Unity.Netcode.Components.NetworkTransform netTransform)
    {
        yield return null;
        if (netTransform != null)
        {
            netTransform.enabled = true;
        }
    }

    private bool CheckIfOnSpline()
    {
        RaycastHit hit;
        Vector3 rayOrigin = transform.position;
        Vector3 rayDirection = Vector3.down;
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, detectionDistance, splineLayer))
        {
            if (showDebugInfo && Time.frameCount % 60 == 0)
            {
                Debug.Log($"[KartController] Detected spline layer: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");
            }
            return true;
        }
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
