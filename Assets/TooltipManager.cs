using UnityEngine;
using TMPro;
using DG.Tweening;
using UnityEngine.UI;

public class TooltipManager : MonoBehaviour
{
    public static TooltipManager Instance;

    [Header("Tooltip Objects")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipText;

    [Header("=== NON-MOBILE PLAYER SIDE (Bottom-Left Panel) ===")]
    public Vector2 playerTooltipOffset = new Vector2(0, 120);

    [Header("=== NON-MOBILE AI SIDE (Top-Right Panel) ===")]
    public Vector2 aiTooltipOffset = new Vector2(0, -120);

    [Header("=== MOBILE PLAYER SIDE (Bottom-Left Panel) ===")]
    public Vector2 playerMobileTooltipOffset = new Vector2(0, 120);

    [Header("=== MOBILE AI SIDE (Top-Right Panel) ===")]
    public Vector2 aiMobileTooltipOffset = new Vector2(0, -120);

    public bool isMobile;

    private RectTransform tooltipRect;
    private CanvasGroup canvasGroup;
    private Tween currentTween;

    // This stores which side is currently showing the tooltip
    private bool showingPlayerTooltip = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        tooltipRect = tooltipPanel.GetComponent<RectTransform>();
        canvasGroup = tooltipPanel.GetComponent<CanvasGroup>() ?? tooltipPanel.AddComponent<CanvasGroup>();
        tooltipPanel.SetActive(false);
    }

    // Call this version — pass true for player side, false for AI side
    public void ShowTooltip(string description, bool isPlayerSide)
    {
        if (TrainingMode.IsTraining) return;

        if (currentTween != null) currentTween.Kill();

        showingPlayerTooltip = isPlayerSide;

        tooltipText.text = description;
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipRect);

        tooltipPanel.SetActive(true);
        canvasGroup.alpha = 0f;
        UpdateTooltipPosition();
        currentTween = canvasGroup.DOFade(1f, 0.3f);
    }

    public void HideTooltip()
    {
        if (TrainingMode.IsTraining) return;

        if (currentTween != null) currentTween.Kill();
        currentTween = canvasGroup.DOFade(0f, 0.2f).OnComplete(() => {
            tooltipPanel.SetActive(false);
            showingPlayerTooltip = false;
        });
    }

    private void Update()
    {
        if (TrainingMode.IsTraining) return;

        if (tooltipPanel.activeSelf)
            UpdateTooltipPosition();
    }

    private void UpdateTooltipPosition()
    {
        RectTransform referencePanel = showingPlayerTooltip
            ? UIManager.Instance.GetActivePlayerPanel()
            : UIManager.Instance.GetActiveAIPanel();

        if (referencePanel == null)
        {
            tooltipPanel.SetActive(false);
            return;
        }

        // Calculate real world center of the panel (works with any pivot/anchor)
        Vector2 size   = referencePanel.rect.size;
        Vector2 pivot  = referencePanel.pivot;
        Vector3 center = referencePanel.position;
        center.x -= (pivot.x - 0.5f) * size.x;
        center.y -= (pivot.y - 0.5f) * size.y;

        Vector2 offset = showingPlayerTooltip 
            ? (isMobile ? playerMobileTooltipOffset : playerTooltipOffset) 
            : (isMobile ? aiMobileTooltipOffset : aiTooltipOffset);
        tooltipRect.position = center + (Vector3)offset;
    }
}