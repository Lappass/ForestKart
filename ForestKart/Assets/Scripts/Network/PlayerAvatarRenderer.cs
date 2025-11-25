using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

public class PlayerAvatarRenderer : MonoBehaviour
{
    [Header("Render Settings")]
    public int textureWidth = 128;
    public int textureHeight = 128;
    public LayerMask renderLayer = 1 << 7;
    public float cameraDistance = 1.5f;
    public Vector3 cameraOffset = new Vector3(0, 1f, 0);
    
    [Header("Update Settings")]
    public bool enableRealTimeUpdate = true;
    public float updateInterval = 0.2f;
    
    public Camera renderCamera;
    private RenderTexture renderTexture;
    private Dictionary<NetworkObject, RenderTexture> avatarRenderTextures = new Dictionary<NetworkObject, RenderTexture>();
    private Dictionary<NetworkObject, Transform> targetTransforms = new Dictionary<NetworkObject, Transform>();
    private Dictionary<int, int> originalLayers = new Dictionary<int, int>();
    private HashSet<NetworkObject> activePlayers = new HashSet<NetworkObject>();
    private float updateTimer = 0f;
    
    public static PlayerAvatarRenderer Instance { get; private set; }
    
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }
    
    void Start()
    {
        SetupRenderCamera();
    }
    
    void Update()
    {
        if (enableRealTimeUpdate && activePlayers.Count > 0)
        {
            updateTimer += Time.deltaTime;
            if (updateTimer >= updateInterval)
            {
                updateTimer = 0f;
                UpdateAllActiveAvatars();
            }
        }
    }
    
    private void SetupRenderCamera()
    {
        GameObject cameraObj = new GameObject("AvatarRenderCamera");
        cameraObj.transform.SetParent(transform);
        cameraObj.transform.localPosition = Vector3.zero;
        
        renderCamera = cameraObj.AddComponent<Camera>();
        renderCamera.enabled = false;
        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        renderCamera.orthographic = false;
        renderCamera.fieldOfView = 30f;
        renderCamera.cullingMask = renderLayer;
        renderCamera.nearClipPlane = 0.1f;
        renderCamera.farClipPlane = 10f;
        
        renderTexture = new RenderTexture(textureWidth, textureHeight, 24);
    }
    
    public RenderTexture GetPlayerAvatar(NetworkObject playerObject, bool addToUpdateList = true)
    {
        if (playerObject == null || !playerObject.IsSpawned)
        {
            return null;
        }
        
        if (avatarRenderTextures.ContainsKey(playerObject))
        {
            if (addToUpdateList && enableRealTimeUpdate)
            {
                activePlayers.Add(playerObject);
            }
            return avatarRenderTextures[playerObject];
        }
        
        Transform targetTransform = GetKartTransform(playerObject);
        if (targetTransform == null)
        {
            return null;
        }
        
        targetTransforms[playerObject] = targetTransform;
        
        if (addToUpdateList && enableRealTimeUpdate)
        {
            activePlayers.Add(playerObject);
        }
        
        RenderTexture playerRenderTexture = new RenderTexture(textureWidth, textureHeight, 24);
        avatarRenderTextures[playerObject] = playerRenderTexture;
        
        RenderAvatarToTexture(targetTransform, playerRenderTexture, playerObject);
        
        return playerRenderTexture;
    }
    
    private Transform GetKartTransform(NetworkObject playerObject)
    {
        if (playerObject == null || !playerObject.IsSpawned) return null;
        
        KartController kart = playerObject.GetComponentInChildren<KartController>();
        if (kart != null)
        {
            if (kart.driverModelParent != null)
            {
                return kart.driverModelParent;
            }
            
            return kart.transform;
        }
        
        AIKartController aiKart = playerObject.GetComponentInChildren<AIKartController>();
        if (aiKart != null)
        {
            KartController aiKartController = aiKart.GetComponent<KartController>();
            if (aiKartController != null)
            {
                if (aiKartController.driverModelParent != null)
                {
                    return aiKartController.driverModelParent;
                }
                return aiKartController.transform;
            }
            
            return aiKart.transform;
        }
        
        return playerObject.transform;
    }
    
    private Transform GetKartRootTransform(NetworkObject playerObject)
    {
        if (playerObject == null || !playerObject.IsSpawned) return null;
        
        KartController kart = playerObject.GetComponentInChildren<KartController>();
        if (kart != null)
        {
            return kart.transform;
        }
        
        AIKartController aiKart = playerObject.GetComponentInChildren<AIKartController>();
        if (aiKart != null)
        {
            KartController aiKartController = aiKart.GetComponent<KartController>();
            if (aiKartController != null)
            {
                return aiKartController.transform;
            }
            
            return aiKart.transform;
        }
        
        return playerObject.transform;
    }
    
    private void RenderAvatarToTexture(Transform targetTransform, RenderTexture targetRenderTexture, NetworkObject playerObject = null)
    {
        if (renderCamera == null || targetTransform == null || targetRenderTexture == null)
        {
            return;
        }
        
        List<GameObject> affectedObjects = new List<GameObject>();
        int targetLayer = GetLayerFromMask(renderLayer);
        
        Transform kartRoot = null;
        if (playerObject != null)
        {
            kartRoot = GetKartRootTransform(playerObject);
        }
        
        if (kartRoot != null && kartRoot != targetTransform)
        {
            SetLayerRecursive(kartRoot.gameObject, targetLayer, affectedObjects);
        }
        
        SetLayerRecursive(targetTransform.gameObject, targetLayer, affectedObjects);
        
        Bounds bounds = CalculateBounds(targetTransform.gameObject);
        if (kartRoot != null && kartRoot != targetTransform)
        {
            Bounds kartBounds = CalculateBounds(kartRoot.gameObject);
            bounds.Encapsulate(kartBounds);
        }
        
        Vector3 center = bounds.center;
        float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        
        float adjustedDistance = cameraDistance;
        if (size > 0.1f)
        {
            float standardSize = 2.0f;
            adjustedDistance = cameraDistance * (standardSize / size);
        }
        
        Vector3 targetPosition = targetTransform.position;
        Vector3 forward = targetTransform.forward;
        
        Vector3 cameraPos = targetPosition + forward * adjustedDistance + cameraOffset;
        
        Vector3 lookAtPos = targetPosition + cameraOffset;
        renderCamera.transform.position = cameraPos;
        renderCamera.transform.LookAt(lookAtPos);
        
        renderCamera.cullingMask = renderLayer;
        
        RenderTexture previousTarget = renderCamera.targetTexture;
        renderCamera.targetTexture = targetRenderTexture;
        
        renderCamera.Render();
        
        renderCamera.targetTexture = previousTarget;
        
        RestoreLayers(affectedObjects);
    }
    
    private Bounds CalculateBounds(GameObject obj)
    {
        Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
        
        if (renderers.Length == 0)
        {
            return new Bounds(obj.transform.position, Vector3.one * 2f);
        }
        
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.enabled)
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        
        return bounds;
    }
    
    private void SetLayerRecursive(GameObject obj, int layer, List<GameObject> affectedObjects)
    {
        if (obj == null) return;
        
        if (!originalLayers.ContainsKey(obj.GetInstanceID()))
        {
            originalLayers[obj.GetInstanceID()] = obj.layer;
        }
        
        obj.layer = layer;
        affectedObjects.Add(obj);
        
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursive(child.gameObject, layer, affectedObjects);
        }
    }
    
    private void RestoreLayers(List<GameObject> affectedObjects)
    {
        foreach (GameObject obj in affectedObjects)
        {
            if (obj != null && originalLayers.ContainsKey(obj.GetInstanceID()))
            {
                obj.layer = originalLayers[obj.GetInstanceID()];
            }
        }
        affectedObjects.Clear();
    }
    
    private int GetLayerFromMask(LayerMask mask)
    {
        int layerNumber = 0;
        int layer = mask.value;
        while (layer > 1)
        {
            layer = layer >> 1;
            layerNumber++;
        }
        return layerNumber;
    }
    
    public void UpdateAvatar(NetworkObject playerObject)
    {
        if (playerObject == null || !playerObject.IsSpawned)
        {
            activePlayers.Remove(playerObject);
            return;
        }
        
        if (!avatarRenderTextures.ContainsKey(playerObject))
        {
            RenderTexture playerRenderTexture = new RenderTexture(textureWidth, textureHeight, 24);
            avatarRenderTextures[playerObject] = playerRenderTexture;
        }
        
        if (!targetTransforms.ContainsKey(playerObject) || targetTransforms[playerObject] == null)
        {
            Transform targetTransform = GetKartTransform(playerObject);
            if (targetTransform == null)
            {
                activePlayers.Remove(playerObject);
                return;
            }
            targetTransforms[playerObject] = targetTransform;
        }
        
        Transform targetTransformToRender = targetTransforms[playerObject];
        RenderTexture targetRenderTexture = avatarRenderTextures[playerObject];
        
        if (targetTransformToRender != null && targetRenderTexture != null)
        {
            RenderAvatarToTexture(targetTransformToRender, targetRenderTexture, playerObject);
        }
    }
    
    private void UpdateAllActiveAvatars()
    {
        List<NetworkObject> playersToUpdate = new List<NetworkObject>(activePlayers);
        
        foreach (NetworkObject playerObject in playersToUpdate)
        {
            if (playerObject != null && playerObject.IsSpawned)
            {
                UpdateAvatar(playerObject);
            }
            else
            {
                activePlayers.Remove(playerObject);
            }
        }
    }
    
    public void RemoveFromUpdateList(NetworkObject playerObject)
    {
        activePlayers.Remove(playerObject);
    }
    
    public void ClearCache(NetworkObject playerObject)
    {
        if (playerObject != null)
        {
            if (avatarRenderTextures.ContainsKey(playerObject))
            {
                if (avatarRenderTextures[playerObject] != null)
                {
                    avatarRenderTextures[playerObject].Release();
                    Destroy(avatarRenderTextures[playerObject]);
                }
                avatarRenderTextures.Remove(playerObject);
            }
            targetTransforms.Remove(playerObject);
            activePlayers.Remove(playerObject);
        }
    }
    
    public void ClearAllCache()
    {
        foreach (var renderTexture in avatarRenderTextures.Values)
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }
        }
        avatarRenderTextures.Clear();
        targetTransforms.Clear();
        activePlayers.Clear();
    }
    
    void OnDestroy()
    {
        ClearAllCache();
        
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }
}
