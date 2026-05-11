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
    public GameObject walletScreen;   // Shown after Phantom login (wallet confirmation + tier badge). X and guest paths skip this entirely.

    [Header("Canvas")]
    public Canvas loginCanvas;     // Drag this screen's Canvas here (Screen Space - Camera)

    private bool isMobile = false;

    private Vector2 originalGuestButtonPos;
    private Vector3 originalXScale;
    private Vector3 originalGuestScale;

    // Track original child scales so OnDisable can restore cleanly
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
        isMobile = false;   // ← Set to true when testing mobile in Editor
#endif
    }

    void OnEnable()
    {
        loginWithXButton.onClick.AddListener(OnXLoginClicked);
        playAsGuestButton.onClick.AddListener(OnGuestClicked);
        if (loginWithPhantomButton != null)
            loginWithPhantomButton.onClick.AddListener(OnPhantomLoginClicked);

        if (statusText != null)
            statusText.text = "";

        // Store originals BEFORE any scaling is applied
        if (playAsGuestButton != null)
            originalGuestButtonPos = playAsGuestButton.GetComponent<RectTransform>().anchoredPosition;

        originalXScale = loginWithXButton.transform.localScale;
        originalGuestScale = playAsGuestButton.transform.localScale;

        // === Scale canvas children 3x on BOTH mobile and desktop ===
        // This grows the background/frames/text etc. The buttons also get scaled
        // by this pass, but we override them right after to land at the exact
        // final size we want on each platform.
        ApplyChildScaling();

        // === Per-platform button overrides (applied AFTER canvas-child scaling so
        //     they replace, not compound) ===
        if (isMobile)
        {
            // Mobile: exactly 2x the original inspector scale (no compounding)
            loginWithXButton.transform.localScale = originalXScale * 1f;
            playAsGuestButton.transform.localScale = originalGuestScale * 1f;

            // Guest button offset down 110px
            if (playAsGuestButton != null)
            {
                RectTransform rt = playAsGuestButton.GetComponent<RectTransform>();
                rt.anchoredPosition = originalGuestButtonPos + new Vector2(0, -110f);
            }

            Debug.Log("=== [LoginScreen] Mobile: buttons at 2x original scale, Guest moved -110y ===");
        }
        else
        {
            // Desktop: restore buttons to their original inspector scale
            // (your inspector values already have whatever scale you want for desktop)
            loginWithXButton.transform.localScale = originalXScale;
            playAsGuestButton.transform.localScale = originalGuestScale;

            // Guest button offset down 200px
            if (playAsGuestButton != null)
            {
                RectTransform rt = playAsGuestButton.GetComponent<RectTransform>();
                rt.anchoredPosition = originalGuestButtonPos + new Vector2(0, -200f);
            }

            Debug.Log("=== [LoginScreen] Desktop: buttons at original scale, Guest moved -200y ===");
        }

        Debug.Log("=== [LoginScreen] OnEnable - Ready ===");
    }

    /// <summary>
    /// Scales every direct child of the login canvas to 3x its original scale.
    /// Does NOT touch the canvas itself: it is Screen Space - Camera so the
    /// canvas transform is camera-managed.
    /// </summary>
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
                // X login skips the wallet screen entirely. X users are legacy /
                // no-wallet path; the wallet flow is reserved for Phantom auth.
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
            ShowStarterSelection();   // Guests skip the wallet screen — guests can't link
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
                // Phantom login routes to WalletScreen so the user sees their
                // tier badge (or gets the "Get Capmon NFT" prompt if they have
                // no stake yet). The Continue button on WalletScreen is gated
                // until they have an active NFT, which keeps the flow committed
                // to "every Phantom user plays with a Capbot."
                ShowWalletScreen();
            },
            onError: (error) => {
                statusText.text = "❌ Wallet sign-in failed: " + error;
                Debug.LogError("=== [LoginScreen] Phantom login ERROR ===");
            }
        );
    }

    /// <summary>
    /// Activates the wallet linking screen. Only reachable from Phantom login.
    /// X login and guest mode skip this and go directly to starter selection.
    /// </summary>
    private void ShowWalletScreen()
    {
        gameObject.SetActive(false);
        Debug.Log("=== [LoginScreen] Hiding LoginScreen, opening WalletScreen ===");

        if (walletScreen != null)
        {
            Debug.Log("=== [LoginScreen] SUCCESS: Activating WalletScreen ===");
            walletScreen.SetActive(true);
        }
        else
        {
            // Fallback: if walletScreen isn't wired up yet, skip straight to starter selection.
            Debug.LogWarning("=== [LoginScreen] walletScreen not assigned, falling through to StarterSelectionScreen ===");
            if (starterSelectionScreen != null) starterSelectionScreen.SetActive(true);
        }
    }

    private void ShowStarterSelection()
    {
        gameObject.SetActive(false);
        Debug.Log("=== [LoginScreen] Hiding LoginScreen ===");

        if (starterSelectionScreen != null)
        {
            Debug.Log("=== [LoginScreen] SUCCESS: Activating StarterSelectionScreen ===");
            starterSelectionScreen.SetActive(true);
        }
        else
        {
            Debug.LogError("=== [LoginScreen] CRITICAL: StarterSelectionScreen reference missing in Inspector! ===");
        }
    }

    void OnDisable()
    {
        RestoreChildScaling();
    }
}