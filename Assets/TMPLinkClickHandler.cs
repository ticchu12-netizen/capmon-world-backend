using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

/// <summary>
/// Click handler for TMP_Text link tags. Reads &lt;link id="..."&gt; markup, fires
/// onLinkClicked(linkId) when a link span is clicked.
///
/// WebGL diagnostic version: logs every step of the click pipeline. If link
/// clicks aren't firing, the logs reveal exactly which check failed:
///   1. Was the click received at all? (raycast target / event system issue)
///   2. Did we find a link at the click point? (no &lt;link&gt; tags / wrong camera)
///   3. Did the callback fire? (callback not subscribed)
///
/// Attach to the GameObject that has the TextMeshProUGUI component.
/// </summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public class TMPLinkClickHandler : MonoBehaviour, IPointerClickHandler
{
    [Tooltip("Auto-set in Awake if left empty.")]
    public TextMeshProUGUI tmpText;

    [Tooltip("Set to true to spam logs in WebGL console. Helpful for debugging click issues.")]
    public bool verboseLogging = true;

    public event Action<string> onLinkClicked;

    private Canvas parentCanvas;
    private Camera eventCamera;
    

    void Awake()
    {
        if (tmpText == null) tmpText = GetComponent<TextMeshProUGUI>();

        // Walk up the hierarchy to find the parent Canvas, so we know which
        // camera (if any) to pass to TMP_TextUtilities.FindIntersectingLink.
        parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas != null)
        {
            // For Screen Space Overlay -> null camera
            // For Screen Space Camera / World Space -> the canvas's worldCamera
            eventCamera = (parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
                ? null
                : parentCanvas.worldCamera;
        }

        if (verboseLogging)
        {
            Debug.Log($"[TMPLinkClickHandler] Awake on {gameObject.name}. " +
                      $"Canvas mode: {(parentCanvas != null ? parentCanvas.renderMode.ToString() : "NO CANVAS FOUND")}, " +
                      $"Event camera: {(eventCamera != null ? eventCamera.name : "null (correct for overlay)")}, " +
                      $"Raycast Target: {tmpText.raycastTarget}");

            if (!tmpText.raycastTarget)
            {
                Debug.LogError($"[TMPLinkClickHandler] Raycast Target is OFF on {gameObject.name}. Clicks will NOT register. Enable it in the Inspector on the TextMeshProUGUI component.");
            }
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (verboseLogging)
        {
            Debug.Log($"[TMPLinkClickHandler] Click received at screen pos {eventData.position} on {gameObject.name}");
        }

        if (tmpText == null)
        {
            Debug.LogError("[TMPLinkClickHandler] tmpText is null. Cannot detect links.");
            return;
        }

        // Try the event camera first (correct for most setups)
        int linkIndex = TMP_TextUtilities.FindIntersectingLink(tmpText, eventData.position, eventCamera);

        // Fallback: if we got -1 and we're in overlay mode, try with eventData's pressEventCamera.
        // Sometimes Unity passes a non-null camera even for overlay canvases in WebGL.
        if (linkIndex == -1 && eventCamera == null && eventData.pressEventCamera != null)
        {
            if (verboseLogging) Debug.Log("[TMPLinkClickHandler] Primary detection returned -1. Retrying with pressEventCamera.");
            linkIndex = TMP_TextUtilities.FindIntersectingLink(tmpText, eventData.position, eventData.pressEventCamera);
        }

        if (linkIndex == -1)
        {
            if (verboseLogging)
            {
                Debug.LogWarning($"[TMPLinkClickHandler] Click at {eventData.position} did NOT hit a <link> tag. " +
                                 $"Either no link tags in text, click was on whitespace between links, or wrong camera passed. " +
                                 $"Current text length: {tmpText.text.Length}, " +
                                 $"contains '<link': {tmpText.text.Contains("<link")}");
            }
            return;
        }

        TMP_LinkInfo linkInfo = tmpText.textInfo.linkInfo[linkIndex];
        string linkId = linkInfo.GetLinkID();

        if (verboseLogging)
        {
            Debug.Log($"[TMPLinkClickHandler] Hit link with id='{linkId}', text='{linkInfo.GetLinkText()}'");
        }

        if (onLinkClicked == null)
        {
            Debug.LogError($"[TMPLinkClickHandler] Link id='{linkId}' was clicked but onLinkClicked has NO subscribers. Did you forget to attach a handler in CapbotTabScreen.Start?");
            return;
        }

        onLinkClicked.Invoke(linkId);
    }
}