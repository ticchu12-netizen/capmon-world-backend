using UnityEngine;

[ExecuteInEditMode]  // Runs in Editor/Simulator for preview
public class BackgroundScaler : MonoBehaviour
{
    [SerializeField] private float referencePixelWidth = 1280f;  // Your reference Game view width
    [SerializeField] private float referencePixelHeight = 720f;  // Your reference Game view height
    [SerializeField] private float referenceOrthoSize = 281f;  // OrthoSize that looks correct in 1920x1080 (from your formula)
    [SerializeField] private bool useCoverMode = true;  // True: Fill + crop (no bars, no distortion); False: Stretch (possible distortion)
    [SerializeField] private bool adjustCameraSize = false;  // True: Dynamically set orthographicSize to fit
    [SerializeField] private float minOrthoSize = 5f;  // Prevent excessive zoom-in

    private SpriteRenderer spriteRenderer;
    private Camera mainCamera;
    private float fixedWorldWidth;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        mainCamera = Camera.main;
        if (spriteRenderer == null || spriteRenderer.sprite == null)
        {
            Debug.LogError("No SpriteRenderer or sprite on " + gameObject.name);
        }

        // Calculate fixed world width from reference (ensures consistent horizontal view)
        float referenceAspect = referencePixelWidth / referencePixelHeight;
        fixedWorldWidth = 2f * referenceOrthoSize * referenceAspect;  // ~853 units in your case
    }

    void LateUpdate()
    {
        AdjustCameraAndScale();
    }

    void AdjustCameraAndScale()
    {
        if (mainCamera == null || spriteRenderer == null || spriteRenderer.sprite == null) return;

        // Step 1: Adjust camera orthoSize to keep visible world WIDTH constant (zooms out on tall devices)
        if (adjustCameraSize)
        {
            float currentAspect = (float)Screen.width / Screen.height;
            float orthoSize = fixedWorldWidth / (2f * currentAspect);  // Larger ortho on tall (small aspect) devices
            mainCamera.orthographicSize = Mathf.Max(orthoSize, minOrthoSize);
        }

        // Step 2: Scale background to fill adjusted camera view
        float worldScreenHeight = mainCamera.orthographicSize * 2f;
        float worldScreenWidth = worldScreenHeight * ((float)Screen.width / Screen.height);

        Vector2 spriteSize = spriteRenderer.sprite.bounds.size;
        float scaleX = worldScreenWidth / spriteSize.x;
        float scaleY = worldScreenHeight / spriteSize.y;

        Vector3 newScale;
        if (useCoverMode)
        {
            float scale = Mathf.Max(scaleX, scaleY);  // Cover: Scale up to fill, crop edges
            newScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            newScale = new Vector3(scaleX, scaleY, 1f);  // Stretch: Exact fit
        }

        transform.localScale = newScale;

    }
}