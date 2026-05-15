using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;

/// <summary>
/// Black-overlay fade transitions between screens. Singleton with DontDestroyOnLoad.
///
/// Fade-in (covers screens) → swap active state → fade-out (reveals new screen).
/// Total duration is split 50/50. While transitioning, the overlay also blocks
/// input (raycast target on the image).
///
/// Scene setup:
///   1. Create empty GameObject "_ScreenTransitionManager" at scene root.
///   2. Attach this script.
///   3. Create a child Canvas (Screen Space - Overlay, Sort Order 9999).
///   4. Inside that canvas, create an Image stretched to full screen, color = black.
///      Make sure "Raycast Target" is ON so it blocks input during fade.
///   5. Add a CanvasGroup to the Image, set alpha = 0.
///   6. Drag the Image GameObject into "Black Overlay" slot.
///   7. Drag its CanvasGroup into "Black Overlay Group" slot.
///
/// Two usage patterns:
///   - Simple screen swap:
///       ScreenTransitionManager.Instance.GoTo(gameObject, nextScreen);
///   - Custom action during blackout (multiple objects to flip, battle setup, etc):
///       ScreenTransitionManager.Instance.GoToWithAction(() => {
///           myScreen.SetActive(false);
///           battleBackground.SetActive(true);
///           attackMenu.gameObject.SetActive(true);
///       });
/// </summary>
public class ScreenTransitionManager : MonoBehaviour
{
    public static ScreenTransitionManager Instance { get; private set; }

    [Header("Inspector setup (see class comment)")]
    [SerializeField] private GameObject blackOverlay;
    [SerializeField] private CanvasGroup blackOverlayGroup;

    [Header("Tuning")]
    [Tooltip("Total transition time in seconds (split 50/50 between fade-in and fade-out)")]
    [SerializeField] private float defaultDuration = 0.4f;

    private bool isTransitioning = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (blackOverlayGroup != null) blackOverlayGroup.alpha = 0f;
        if (blackOverlay != null) blackOverlay.SetActive(false);
    }

    /// <summary>
    /// Fade out → swap → fade in.
    /// Ignored if a transition is already in flight (prevents stacking).
    /// Pass duration=0 for instant swap (no fade) if you need it for testing.
    /// </summary>
    public void GoTo(GameObject from, GameObject to, float? duration = null)
    {
        if (isTransitioning) return;
        float d = duration ?? defaultDuration;

        if (d <= 0f)
        {
            if (from != null) from.SetActive(false);
            if (to != null) to.SetActive(true);
            return;
        }

        GoToWithAction(() =>
        {
            if (from != null) from.SetActive(false);
            if (to != null) to.SetActive(true);
        }, d);
    }

    /// <summary>
    /// Fade out → run callback → fade in. Use this when multiple GameObjects
    /// must change state during the blackout (e.g., entering battle: deactivate
    /// betting screen, activate battle background, kick off character spawn).
    /// </summary>
    public void GoToWithAction(System.Action duringBlackout, float? duration = null)
    {
        if (isTransitioning) return;
        float d = duration ?? defaultDuration;

        if (d <= 0f)
        {
            duringBlackout?.Invoke();
            return;
        }

        isTransitioning = true;
        float half = d * 0.5f;

        if (blackOverlay != null) blackOverlay.SetActive(true);

        blackOverlayGroup.DOFade(1f, half).SetEase(Ease.InCubic).OnComplete(() =>
        {
            try { duringBlackout?.Invoke(); }
            catch (System.Exception ex) { Debug.LogError("[ScreenTransition] action threw: " + ex); }

            blackOverlayGroup.DOFade(0f, half).SetEase(Ease.OutCubic).OnComplete(() =>
            {
                if (blackOverlay != null) blackOverlay.SetActive(false);
                isTransitioning = false;
            });
        });
    }
}