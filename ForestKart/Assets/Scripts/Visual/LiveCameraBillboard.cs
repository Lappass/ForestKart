using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ForestKart.Visual
{
    [RequireComponent(typeof(Renderer))]
    public class LiveCameraBillboard : MonoBehaviour
    {
        [Header("Camera Settings")]
        public Camera sourceCamera;
        public List<Camera> availableCameras = new List<Camera>();

        [Header("Switching")]
        [Min(0.5f)]
        public float switchInterval = 3.0f;
        public bool randomSwitch = true;

        [Header("Visual")]
        public Vector2Int resolution = new Vector2Int(512, 512);
        public int materialIndex = 0;
        public string texturePropertyName = "";
        [Range(0f, 5f)]
        public float brightnessMultiplier = 1.0f;

        private Renderer meshRenderer;
        private Material targetMaterial;
        private int brightnessPropID;
        private bool hasBrightnessProp = false;

        private List<Camera> workingCameras = new List<Camera>();
        private int currentCameraIndex = -1;
        private float timer = 0f;
        private int lastListSignature = 0;

        private static Dictionary<Camera, RenderTexture> sharedTextures = new Dictionary<Camera, RenderTexture>();
        private static Dictionary<Camera, int> cameraUsageCount = new Dictionary<Camera, int>();

        void Start()
        {
            InitializeBillboard();
        }

        void InitializeBillboard()
        {
            meshRenderer = GetComponent<Renderer>();
            if (meshRenderer == null)
            {
                enabled = false;
                return;
            }

            SetupMaterial();
            RefreshCameraList();
        }

        void RefreshCameraList()
        {
            foreach (var cam in workingCameras)
            {
                if (cam != null) ReleaseCameraUsage(cam);
            }

            workingCameras.Clear();

            if (sourceCamera != null) workingCameras.Add(sourceCamera);
            foreach (var cam in availableCameras)
            {
                if (cam != null && !workingCameras.Contains(cam)) workingCameras.Add(cam);
            }

            if (workingCameras.Count == 0)
            {
                GameObject camObj = new GameObject($"{name}_SourceCamera");
                Camera newCam = camObj.AddComponent<Camera>();
                camObj.transform.position = transform.position + transform.forward * 5f;
                camObj.transform.LookAt(transform.position + transform.forward * 10f);
                
                var brain = camObj.GetComponent("CinemachineBrain");
                if (brain != null) Destroy(brain);
                var listener = camObj.GetComponent<AudioListener>();
                if (listener != null) Destroy(listener);

                sourceCamera = newCam;
                workingCameras.Add(newCam);
            }

            lastListSignature = GetListSignature();
            
            if (workingCameras.Count > 0)
            {
                currentCameraIndex = 0;
                UseCamera(workingCameras[0]);
            }
        }

        int GetListSignature()
        {
            int hash = (sourceCamera != null ? sourceCamera.GetHashCode() : 0);
            foreach(var cam in availableCameras)
            {
                if (cam != null) hash ^= cam.GetHashCode();
            }
            return hash + availableCameras.Count;
        }

        void SetupMaterial()
        {
            Material[] mats = meshRenderer.materials;
            if (materialIndex >= 0 && materialIndex < mats.Length)
                targetMaterial = mats[materialIndex];
            else
                targetMaterial = meshRenderer.material;

            if (string.IsNullOrEmpty(texturePropertyName))
            {
                if (targetMaterial.HasProperty("_BaseMap")) texturePropertyName = "_BaseMap";
                else if (targetMaterial.HasProperty("_MainTex")) texturePropertyName = "_MainTex";
                else texturePropertyName = "_BaseMap";
            }
            
            if (targetMaterial.HasProperty("_BaseColor")) { brightnessPropID = Shader.PropertyToID("_BaseColor"); hasBrightnessProp = true; }
            else if (targetMaterial.HasProperty("_Color")) { brightnessPropID = Shader.PropertyToID("_Color"); hasBrightnessProp = true; }
            else if (targetMaterial.HasProperty("_EmissionColor")) { brightnessPropID = Shader.PropertyToID("_EmissionColor"); hasBrightnessProp = true; targetMaterial.EnableKeyword("_EMISSION"); }
        }
        
        void UseCamera(Camera cam)
        {
            if (cam == null) return;

            if (!sharedTextures.ContainsKey(cam) || sharedTextures[cam] == null)
            {
                RenderTexture rt = new RenderTexture(resolution.x, resolution.y, 24);
                rt.name = $"SharedRT_{cam.name}";
                rt.Create();
                sharedTextures[cam] = rt;
                cameraUsageCount[cam] = 0;
            }

            cam.targetTexture = sharedTextures[cam];
            cam.gameObject.SetActive(true);
            cam.enabled = true;

            targetMaterial.SetTexture(texturePropertyName, sharedTextures[cam]);

            cameraUsageCount[cam]++;
        }

        void ReleaseCameraUsage(Camera cam)
        {
            if (cam == null) return;

            if (cameraUsageCount.ContainsKey(cam))
            {
                cameraUsageCount[cam]--;
                if (cameraUsageCount[cam] <= 0)
                {
                    cam.enabled = false;
                    cam.gameObject.SetActive(false);
                    cam.targetTexture = null;
                }
            }
        }

        void Update()
        {
            if (GetListSignature() != lastListSignature) RefreshCameraList();

            if (workingCameras.Count > 1)
            {
                timer += Time.deltaTime;
                if (timer >= switchInterval)
                {
                    timer = 0f;
                    SwitchToNextCamera();
                }
            }

            if (hasBrightnessProp && brightnessMultiplier != 1.0f && targetMaterial != null)
            {
                 Color baseColor = Color.white * brightnessMultiplier;
                 if (targetMaterial.HasProperty(brightnessPropID))
                 {
                     Color current = targetMaterial.GetColor(brightnessPropID);
                     baseColor.a = current.a;
                 }
                 targetMaterial.SetColor(brightnessPropID, baseColor);
            }
        }

        public void SwitchToNextCamera()
        {
            if (workingCameras.Count <= 1) return;

            if (currentCameraIndex >= 0 && currentCameraIndex < workingCameras.Count)
            {
                ReleaseCameraUsage(workingCameras[currentCameraIndex]);
            }

            if (randomSwitch)
            {
                int newIndex = currentCameraIndex;
                for (int i = 0; i < 5; i++) 
                {
                    newIndex = Random.Range(0, workingCameras.Count);
                    if (newIndex != currentCameraIndex) break;
                }
                currentCameraIndex = newIndex;
            }
            else
            {
                currentCameraIndex = (currentCameraIndex + 1) % workingCameras.Count;
            }

            if (currentCameraIndex >= 0 && currentCameraIndex < workingCameras.Count)
            {
                UseCamera(workingCameras[currentCameraIndex]);
            }
        }

        void OnDestroy()
        {
            foreach (var cam in workingCameras)
            {
                if (cam != null) ReleaseCameraUsage(cam);
            }
        }
    }
}
