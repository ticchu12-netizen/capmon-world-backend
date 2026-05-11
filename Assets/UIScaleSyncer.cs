using UnityEngine;
using UnityEngine.UI;  // For CanvasScaler

public class UIScaleSyncer : MonoBehaviour
{
    [SerializeField] public CanvasScaler canvasScaler;  // Assign your Canvas's CanvasScaler in Inspector
    private Vector3 originalScale;  // Stores initial scale from Editor

    void Start()
    {
        if (TrainingMode.IsTraining) return;

        originalScale = transform.localScale;  // Capture your Editor/Game view scale
    }

    void LateUpdate()
    {
        if (TrainingMode.IsTraining) return;

        if (canvasScaler == null) return;

        // Get CanvasScaler's computed scale (matches UI scaling exactly)
        Vector2 refRes = canvasScaler.referenceResolution;
        float scaleFactor = 0f;
        switch (canvasScaler.screenMatchMode)
        {
            case CanvasScaler.ScreenMatchMode.MatchWidthOrHeight:
                float logWidth = Mathf.Log(Screen.width / refRes.x, 2);
                float logHeight = Mathf.Log(Screen.height / refRes.y, 2);
                float logWeightedAvg = Mathf.Lerp(logWidth, logHeight, canvasScaler.matchWidthOrHeight);
                scaleFactor = Mathf.Pow(2, logWeightedAvg);
                break;
            // Add other modes if you change Scaler (e.g., Expand: scaleFactor = Mathf.Min(Screen.width / refRes.x, Screen.height / refRes.y))
        }

        // Apply to character (multiplies original—exact UI match)
        transform.localScale = originalScale * scaleFactor;
    }
}