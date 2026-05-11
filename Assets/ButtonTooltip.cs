using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections;

public class ButtonTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [TextArea] public string description = "";

    private Coroutine showCoroutine;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (showCoroutine != null) StopCoroutine(showCoroutine);
        showCoroutine = StartCoroutine(ShowAfterDelay());
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (showCoroutine != null)
        {
            StopCoroutine(showCoroutine);
            showCoroutine = null;
        }
        TooltipManager.Instance.HideTooltip();
    }

    private IEnumerator ShowAfterDelay()
    {
        yield return new WaitForSeconds(0.8f);

        // This is the ONLY reliable way: check the panel's anchorMin
        RectTransform playerPanel = UIManager.Instance.GetActivePlayerPanel();
        RectTransform aiPanel     = UIManager.Instance.GetActiveAIPanel();

        bool isPlayerSide = playerPanel != null && 
                           (transform.IsChildOf(playerPanel) || 
                            (aiPanel != null && !transform.IsChildOf(aiPanel)));

        TooltipManager.Instance.ShowTooltip(description, isPlayerSide);
    }
}