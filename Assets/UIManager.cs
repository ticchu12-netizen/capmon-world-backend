using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    public RectTransform firePanel;
    public RectTransform waterPanel;
    public RectTransform grassPanel;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void ShowPanel(ElementType type, bool isPlayer)
    {
        if (TrainingMode.IsTraining) return;

        RectTransform panel = GetPanelRect(type);
        if (panel != null)
        {
            // Activate the panel
            panel.gameObject.SetActive(true);

            if (isPlayer)
            {
                // Position at bottom left for player
                panel.anchoredPosition = new Vector2(0, -20);
                panel.anchorMin = new Vector2(0, 0);
                panel.anchorMax = new Vector2(0, 0);
                panel.pivot = new Vector2(0, 0);
                // Hide AI move buttons for player's panel
                SetAIMoveButtonsActive(panel, false);
            }
            else
            {
                // Position at top right for AI
                panel.anchoredPosition = new Vector2(0, 0);
                panel.anchorMin = new Vector2(1, 1);
                panel.anchorMax = new Vector2(1, 1);
                panel.pivot = new Vector2(1, 1);
                // Show AI move buttons for AI's panel
                SetAIMoveButtonsActive(panel, true);
            }
        }
    }

    public void HideAllPanels()
    {
        if (TrainingMode.IsTraining) return;

        if (firePanel != null) firePanel.gameObject.SetActive(false);
        if (waterPanel != null) waterPanel.gameObject.SetActive(false);
        if (grassPanel != null) grassPanel.gameObject.SetActive(false);
    }

    public RectTransform GetPanelRect(ElementType type)
    {
        switch (type)
        {
            case ElementType.Fire:
                return firePanel;
            case ElementType.Water:
                return waterPanel;
            case ElementType.Grass:
                return grassPanel;
            default:
                return null;
        }
    }

    private void SetAIMoveButtonsActive(RectTransform panel, bool active)
    {
        // Search all descendants for buttons named "AIMoveButton"
        Transform[] allChildren = panel.GetComponentsInChildren<Transform>(true);
        foreach (Transform child in allChildren)
        {
            if (child.name.StartsWith("AIMoveButton"))
            {
                child.gameObject.SetActive(active);
            }
        }
    }

    // New method to get the active player panel (for mobile scaling)
    public RectTransform GetActivePlayerPanel()
    {
        if (firePanel != null && firePanel.gameObject.activeSelf && firePanel.anchorMin == new Vector2(0, 0))
            return firePanel;
        if (waterPanel != null && waterPanel.gameObject.activeSelf && waterPanel.anchorMin == new Vector2(0, 0))
            return waterPanel;
        if (grassPanel != null && grassPanel.gameObject.activeSelf && grassPanel.anchorMin == new Vector2(0, 0))
            return grassPanel;
        return null;
    }

    // New method to get the active AI panel (for mobile scaling)
    public RectTransform GetActiveAIPanel()
    {
        if (firePanel != null && firePanel.gameObject.activeSelf && firePanel.anchorMin == new Vector2(1, 1))
            return firePanel;
        if (waterPanel != null && waterPanel.gameObject.activeSelf && waterPanel.anchorMin == new Vector2(1, 1))
            return waterPanel;
        if (grassPanel != null && grassPanel.gameObject.activeSelf && grassPanel.anchorMin == new Vector2(1, 1))
            return grassPanel;
        return null;
    }

    public RectTransform GetPanelForHovered(Transform hovered)
    {
        if (firePanel != null && hovered.IsChildOf(firePanel))
            return firePanel;
        if (waterPanel != null && hovered.IsChildOf(waterPanel))
            return waterPanel;
        if (grassPanel != null && hovered.IsChildOf(grassPanel))
            return grassPanel;
        return null;
    }
}