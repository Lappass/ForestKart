using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class IntroCameraPreview : MonoBehaviour
{
    [Header("Camera References")]
    public Camera introCamera1;
    public Camera introCamera2;
    
    [Header("Preview Settings")]
    public float intro1Duration = 2f;
    public float intro2Duration = 2f;
    public float moveDistance = 20f;
    public float arcAngle = 45f;
    public float pivotDistance = 20f;
    
    private Vector3 camera1StartPos;
    private Vector3 camera2StartPos;
    private Quaternion camera2StartRot;
    private float previewTime = 0f;
    private bool isPreviewing = false;
    private int currentPhase = 0; 
    
#if UNITY_EDITOR
    [ContextMenu("Preview Intro Sequence")]
    public void PreviewIntroSequence()
    {
        if (introCamera1 == null || introCamera2 == null)
        {
            Debug.LogError("Please assign both intro cameras!");
            return;
        }
        
        camera1StartPos = introCamera1.transform.position;
        camera2StartPos = introCamera2.transform.position;
        camera2StartRot = introCamera2.transform.rotation;
        
        introCamera1.gameObject.SetActive(true);
        introCamera2.gameObject.SetActive(false);
        
        previewTime = 0f;
        isPreviewing = true;
        currentPhase = 0;
        
        EditorApplication.update += UpdatePreview;
    }
    
    [ContextMenu("Stop Preview")]
    public void StopPreview()
    {
        isPreviewing = false;
        EditorApplication.update -= UpdatePreview;
        
        if (introCamera1 != null)
        {
            introCamera1.transform.position = camera1StartPos;
            introCamera1.gameObject.SetActive(false);
        }
        
        if (introCamera2 != null)
        {
            introCamera2.transform.position = camera2StartPos;
            introCamera2.transform.rotation = camera2StartRot;
            introCamera2.gameObject.SetActive(false);
        }
    }
    
    private void UpdatePreview()
    {
        if (!isPreviewing) return;
        
        float deltaTime = 0.016f; // Approximate editor frame time
        
        if (currentPhase == 0)
        {
            previewTime += deltaTime;
            float progress = Mathf.Clamp01(previewTime / intro1Duration);
            introCamera1.transform.position = camera1StartPos - new Vector3(moveDistance * progress, 0, 0);
            
            if (previewTime >= intro1Duration)
            {
                previewTime = 0f;
                currentPhase = 1;
                introCamera1.gameObject.SetActive(false);
                introCamera2.gameObject.SetActive(true);
            }
        }
        else if (currentPhase == 1)
        {
            previewTime += deltaTime;
            float progress = Mathf.Clamp01(previewTime / intro2Duration);
            
            Vector3 pivotPoint = camera2StartPos + (Quaternion.Euler(0, 0, 0) * Vector3.forward) * pivotDistance;
            float currentAngle = arcAngle * progress;
            
            introCamera2.transform.position = camera2StartPos;
            introCamera2.transform.RotateAround(pivotPoint, Vector3.up, arcAngle * deltaTime / intro2Duration);
            introCamera2.transform.LookAt(pivotPoint);
            
            if (previewTime >= intro2Duration)
            {
                StopPreview();
            }
        }
    }
    
    private void OnDisable()
    {
        EditorApplication.update -= UpdatePreview;
    }
#endif
}








