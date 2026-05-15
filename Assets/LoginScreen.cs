using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class LoginScreen : MonoBehaviour
{
    [Header("Buttons")]
    public Button loginWithXButton;
    public Button playAsGuestButton;
    public Button loginWithPhantomButton;

    [Header("UI")]
    public TMP_Text statusText;

    [Header("Screens - Drag these in the Inspector")]
    public GameObject starterSelectionScreen;
    public GameObject walletScreen;

    [Header("Canvas")]
    public Canvas loginCanvas;

    private bool isMobile = false;

    // Captured ONCE in Awake so revisits don't compound offsets.
    // Was previously re-captured in OnEnable, which meant each revisit
    // pushed the Guest button down by another 110 (mobile) / 200 (desktop)
    // because the "original" position it captured was already-offset.
    private Vector2 originalGuestButtonPos;
    private Vector3 originalXScale;
    private Vector3 originalGuestScale;
    private bool originalsCaptured = false;

    private Dictionary<Transform, Vector3> originalChildScales = new Dictionary<Transform, Vector3>();
    private bool childScalingApplied = false;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool IsMobile();
#endif

    void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        isMobile = IsMobile();
#elif UNITY_EDITOR
        isMobile = false;
#endif

        // Capture original positions/scales ONCE — these are the "home" values
        // from the prefab/scene. OnEnable will compute its layout offsets
        // against these and never re-capture, so revisits land in the same place.
        CaptureOriginalsIfNeeded();
    }

    private void CaptureOriginalsIfNeeded()
    {
        if (originalsCaptured) return;

        if (playAsGuestButton != null)
            originalGuestButtonPos = playAsGuestButton.GetComponent<RectTransform>().anchoredPosition;

        if (loginWithXButton != null)
            originalXScale = loginWithXButton.transform.localScale;

        if (playAsGuestButton != null)
            originalGuestScale = playAsGuestButton.transform.localScale;

        originalsCaptured = true;
    }

    void OnEnable()
    {
        // Defensive: if Awake didn't run (e.g. screen instantiated late), still capture.
        CaptureOriginalsIfNeeded();

        // RemoveAllListeners first so revisits don't stack duplicate click handlers.
        // Previously every revisit added another OnXLoginClicked subscription, causing
        // one click to fire the handler N+1 times.
        if (loginWithXButton != null)
        {
            loginWithXButton.onClick.RemoveAllListeners();
            loginWithXButton.onClick.AddListener(OnXLoginClicked);
        }
        if (playAsGuestButton != null)
        {
            playAsGuestButton.onClick.RemoveAllListeners();
            playAsGuestButton.onClick.AddListener(OnGuestClicked);
        }
        if (loginWithPhantomButton != null)
        {
            loginWithPhantomButton.onClick.RemoveAllListeners();
            loginWithPhantomButton.onClick.AddListener(OnPhantomLoginClicked);
        }

        if (statusText != null)
            statusText.text = "";

        ApplyChildScaling();

        // Apply layout from the ORIGINAL captured positions/scales each time.
        // Because originalGuestButtonPos is captured once and never overwritten,
        // the final position is deterministic regardless of how many times the
        // user has bounced into and out of this screen.
        if (isMobile)
        {
            if (loginWithXButton != null) loginWithXButton.transform.localScale = originalXScale * 1f;
            if (playAsGuestButton != null) playAsGuestButton.transform.localScale = originalGuestScale * 1f;

            if (playAsGuestButton != null)
            {
                RectTransform rt = playAsGuestButton.GetComponent<RectTransform>();
                rt.anchoredPosition = originalGuestButtonPos + new Vector2(0, -190f);
            }

            Debug.Log("=== [LoginScreen] Mobile: Guest at original -110y ===");
        }
        else
        {
            if (loginWithXButton != null) loginWithXButton.transform.localScale = originalXScale;
            if (playAsGuestButton != null) playAsGuestButton.transform.localScale = originalGuestScale;

            if (playAsGuestButton != null)
            {
                RectTransform rt = playAsGuestButton.GetComponent<RectTransform>();
                rt.anchoredPosition = originalGuestButtonPos + new Vector2(0, -200f);
            }

            Debug.Log("=== [LoginScreen] Desktop: Guest at original -200y ===");
        }

        Debug.Log("=== [LoginScreen] OnEnable - Ready ===");
    }

    private void ApplyChildScaling()
    {
        if (loginCanvas == null)
        {
            Debug.LogWarning("[LoginScreen] loginCanvas not assigned - cannot apply scaling.");
            return;
        }
        if (childScalingApplied) return;

        Transform canvasTf = loginCanvas.transform;
        originalChildScales.Clear();

        for (int i = 0; i < canvasTf.childCount; i++)
        {
            Transform child = canvasTf.GetChild(i);
            originalChildScales[child] = child.localScale;
            child.localScale = child.localScale * 3f;
        }

        childScalingApplied = true;
        Debug.Log($"[LoginScreen] Scaled {originalChildScales.Count} canvas children to 3x.");
    }

    private void RestoreChildScaling()
    {
        foreach (var kvp in originalChildScales)
        {
            if (kvp.Key != null)
            {
                kvp.Key.localScale = kvp.Value;
            }
        }
        originalChildScales.Clear();
        childScalingApplied = false;
    }

    private void OnXLoginClicked()
    {
        Vector3 restScale = loginWithXButton.transform.localScale;
        loginWithXButton.transform.DOScale(restScale * 0.9f, 0.1f)
            .OnComplete(() => loginWithXButton.transform.DOScale(restScale, 0.1f));
        statusText.text = "Signing in with X...";
        Debug.Log("=== [LoginScreen] X login clicked ===");

        FirebaseManager.Instance.SignInWithTwitter(
            onSuccess: () => {
                statusText.text = "✅ Signed in! Choosing starter...";
                ShowStarterSelection();
            },
            onError: (error) => {
                statusText.text = "❌ Sign-in failed: " + error;
                Debug.LogError("=== [LoginScreen] X login ERROR ===");
            }
        );
    }

    private void OnGuestClicked()
    {
        Vector3 restScale = playAsGuestButton.transform.localScale;
        playAsGuestButton.transform.DOScale(restScale * 0.9f, 0.1f)
            .OnComplete(() => playAsGuestButton.transform.DOScale(restScale, 0.1f));
        statusText.text = "Starting as Guest...";
        Debug.Log("=== [LoginScreen] Guest clicked ===");

        FirebaseManager.Instance.StartAsGuest(() =>
        {
            statusText.text = "✅ Guest mode active. Choosing starter...";
            ShowStarterSelection();
        });
    }

    private void OnPhantomLoginClicked()
    {
        Vector3 restScale = loginWithPhantomButton.transform.localScale;
        loginWithPhantomButton.transform.DOScale(restScale * 0.9f, 0.1f)
            .OnComplete(() => loginWithPhantomButton.transform.DOScale(restScale, 0.1f));
        statusText.text = "Connecting to Phantom...";
        Debug.Log("=== [LoginScreen] Phantom login clicked ===");

        FirebaseManager.Instance.SignInWithWallet(
            onSuccess: (uid) => {
                statusText.text = "✅ Wallet signed in! Verifying stake...";
                ShowWalletScreen();
            },
            onError: (error) => {
                statusText.text = "❌ Wallet sign-in failed: " + error;
                Debug.LogError("=== [LoginScreen] Phantom login ERROR ===");
            }
        );
    }

    private void ShowWalletScreen()
    {
        Debug.Log("=== [LoginScreen] Transitioning to WalletScreen ===");

        if (walletScreen == null)
        {
            Debug.LogWarning("=== [LoginScreen] walletScreen not assigned, falling through to StarterSelectionScreen ===");
            ShowStarterSelection();
            return;
        }

        TransitionTo(walletScreen);
    }

    private void ShowStarterSelection()
    {
        Debug.Log("=== [LoginScreen] Transitioning to StarterSelectionScreen ===");

        if (starterSelectionScreen == null)
        {
            Debug.LogError("=== [LoginScreen] CRITICAL: StarterSelectionScreen reference missing in Inspector! ===");
            return;
        }

        TransitionTo(starterSelectionScreen);
    }

    private void TransitionTo(GameObject target)
    {
        var sm = ScreenTransitionManager.Instance;
        if (sm != null)
        {
            sm.GoTo(gameObject, target);
        }
        else
        {
            Debug.LogWarning("=== [LoginScreen] ScreenTransitionManager not present, doing instant swap ===");
            gameObject.SetActive(false);
            target.SetActive(true);
        }
    }

    void OnDisable()
    {
        RestoreChildScaling();
    }
}