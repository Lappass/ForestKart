using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.Rendering;

public class ScreenBlurEffect : MonoBehaviour
{
    private static ScreenBlurEffect instance;
    public static ScreenBlurEffect Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject go = new GameObject("ScreenBlurEffect");
                instance = go.AddComponent<ScreenBlurEffect>();
                DontDestroyOnLoad(go);
                instance.Initialize();
            }
            return instance;
        }
    }
    
    private Canvas blurCanvas;
    private RawImage blurImage;
    private Camera mainCamera;
    private RenderTexture blurRenderTexture;
    private RenderTexture tempRenderTexture;
    private Material blurMaterial;
    private Coroutine blurCoroutine;
    private float currentBlurIntensity = 0f;
    
    private void Initialize()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindAnyObjectByType<Camera>();
        }
        
        Shader blurShader = null;
        if (GameManager.Instance != null && GameManager.Instance.blurShader != null)
        {
            blurShader = GameManager.Instance.blurShader;
        }
        if (blurShader == null)
        {
            blurShader = Shader.Find("Custom/BlurShader");
        }
        
        if (blurShader == null)
        {
            blurShader = Shader.Find("UI/Default");
        }
        
        if (blurShader != null)
        {
            blurMaterial = new Material(blurShader);
        }
        
        GameObject canvasObj = new GameObject("BlurCanvas");
        canvasObj.transform.SetParent(transform);
        blurCanvas = canvasObj.AddComponent<Canvas>();
        blurCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        blurCanvas.sortingOrder = 9999;
        
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        
        canvasObj.AddComponent<GraphicRaycaster>();
        
        GameObject blurObj = new GameObject("BlurImage");
        blurObj.transform.SetParent(canvasObj.transform, false);
        
        RectTransform blurRect = blurObj.AddComponent<RectTransform>();
        blurRect.anchorMin = Vector2.zero;
        blurRect.anchorMax = Vector2.one;
        blurRect.sizeDelta = Vector2.zero;
        blurRect.anchoredPosition = Vector2.zero;
        
        blurImage = blurObj.AddComponent<RawImage>();
        blurImage.color = new Color(1f, 1f, 1f, 0f);
        
        blurCanvas.gameObject.SetActive(false);
    }
    
    private void UpdateBlurTexture()
    {
        if (mainCamera == null || blurMaterial == null) return;
        
        if (blurRenderTexture == null || blurRenderTexture.width != Screen.width || blurRenderTexture.height != Screen.height)
        {
            if (blurRenderTexture != null)
            {
                blurRenderTexture.Release();
            }
            if (tempRenderTexture != null)
            {
                tempRenderTexture.Release();
            }
            
            int width = Mathf.Max(1, Screen.width / 2);
            int height = Mathf.Max(1, Screen.height / 2);
            
            blurRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            blurRenderTexture.filterMode = FilterMode.Bilinear;
            blurRenderTexture.wrapMode = TextureWrapMode.Clamp;
            
            tempRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            tempRenderTexture.filterMode = FilterMode.Bilinear;
            tempRenderTexture.wrapMode = TextureWrapMode.Clamp;
        }
        
        if (currentBlurIntensity > 0.01f)
        {
            RenderTexture previous = RenderTexture.active;
            
            RenderTexture.active = tempRenderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = previous;
            
            RenderTexture previousTarget = mainCamera.targetTexture;
            mainCamera.targetTexture = tempRenderTexture;
            mainCamera.Render();
            mainCamera.targetTexture = previousTarget;
            
            float blurSize = 10f * currentBlurIntensity;
            if (blurMaterial.HasProperty("_BlurSize"))
            {
                blurMaterial.SetFloat("_BlurSize", blurSize);
            }
            if (blurMaterial.HasProperty("_Intensity"))
            {
                blurMaterial.SetFloat("_Intensity", 1f);
            }
            
            RenderTexture tempBlur = RenderTexture.GetTemporary(blurRenderTexture.width, blurRenderTexture.height, 0);
            Graphics.Blit(tempRenderTexture, tempBlur, blurMaterial);
            Graphics.Blit(tempBlur, blurRenderTexture, blurMaterial);
            RenderTexture.ReleaseTemporary(tempBlur);
            
            if (blurImage != null)
            {
                blurImage.texture = blurRenderTexture;
            }
        }
    }
    
    public void ApplyBlur(float duration)
    {
        if (blurCoroutine != null)
        {
            StopCoroutine(blurCoroutine);
        }
        blurCoroutine = StartCoroutine(BlurCoroutine(duration));
    }
    
    private IEnumerator BlurCoroutine(float duration)
    {
        blurCanvas.gameObject.SetActive(true);
        
        float fadeInTime = 0.2f;
        float fadeOutTime = 0.4f;
        float holdTime = Mathf.Max(0f, duration - fadeInTime - fadeOutTime);
        
        float elapsed = 0f;
        while (elapsed < fadeInTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(elapsed / fadeInTime);
            currentBlurIntensity = alpha;
            
            UpdateBlurTexture();
            blurImage.color = new Color(1f, 1f, 1f, alpha);
            
            yield return null;
        }
        
        currentBlurIntensity = 1f;
        blurImage.color = new Color(1f, 1f, 1f, 1f);
        
        float holdElapsed = 0f;
        while (holdElapsed < holdTime)
        {
            holdElapsed += Time.deltaTime;
            UpdateBlurTexture();
            yield return null;
        }
        
        elapsed = 0f;
        while (elapsed < fadeOutTime)
        {
            elapsed += Time.deltaTime;
            float alpha = Mathf.Clamp01(1f - (elapsed / fadeOutTime));
            currentBlurIntensity = alpha;
            
            UpdateBlurTexture();
            blurImage.color = new Color(1f, 1f, 1f, alpha);
            
            yield return null;
        }
        
        currentBlurIntensity = 0f;
        blurImage.color = new Color(1f, 1f, 1f, 0f);
        blurCanvas.gameObject.SetActive(false);
        blurCoroutine = null;
    }
    
    void LateUpdate()
    {
        if (currentBlurIntensity > 0.01f && blurCanvas != null && blurCanvas.gameObject.activeSelf)
        {
            UpdateBlurTexture();
        }
    }
    
    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    
    void OnDestroy()
    {
        if (blurRenderTexture != null)
        {
            blurRenderTexture.Release();
            Destroy(blurRenderTexture);
        }
        if (tempRenderTexture != null)
        {
            tempRenderTexture.Release();
            Destroy(tempRenderTexture);
        }
        if (blurMaterial != null)
        {
            Destroy(blurMaterial);
        }
    }
}

