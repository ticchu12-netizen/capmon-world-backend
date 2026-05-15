using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class StarterSelectionScreen : MonoBehaviour
{
    [Header("Prefabs - Drag your character prefabs here")]
    public GameObject firePrefab;      // Rageblaze
    public GameObject waterPrefab;     // Tsunami
    public GameObject grassPrefab;     // Healspike

    [Header("Buttons")]
    public Button rageblazeButton;
    public Button tsunamiButton;
    public Button healspikeButton;
    public Button backButton;          // NEW: back to wallet (Phantom) or login (X/guest)

    [Header("UI - Name + Coins under each preview")]
    public TMP_Text rageblazeNameText;
    public TMP_Text rageblazeCoinsText;
    public TMP_Text tsunamiNameText;
    public TMP_Text tsunamiCoinsText;
    public TMP_Text healspikeNameText;
    public TMP_Text healspikeCoinsText;

    [Header("UI")]
    public TMP_Text rankText;
    public Button closeButton;

    [Header("Tier badge (NFT-staked players only)")]
    public TMP_Text tierBadgeText;

    [Header("Canvas")]
    public Canvas starterCanvas;

    [Header("Capbot tab")]
    public Button myCapbotsButton;
    public GameObject capbotTabScreen;

    [Header("Navigation - back-button targets")]
    public GameObject loginScreen;      // NEW: fallback for X/guest path
    public GameObject walletScreen;     // NEW: primary for Phantom-authed users

    private GameObject ragePreview;
    private GameObject tsunamiPreview;
    private GameObject healspikePreview;

    private Dictionary<Transform, Vector3> originalChildScales = new Dictionary<Transform, Vector3>();
    private bool childScalingApplied = false;

    private bool isMobile = false;

    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };

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
    }

    void OnEnable()
    {
        Debug.Log("=== [StarterSelectionScreen] OnEnable called ===");

        rageblazeButton.onClick.AddListener(() => SelectStarter("Rageblaze"));
        tsunamiButton.onClick.AddListener(() => SelectStarter("Tsunami"));
        healspikeButton.onClick.AddListener(() => SelectStarter("Healspike"));

        if (closeButton != null)
            closeButton.onClick.AddListener(() => gameObject.SetActive(false));

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButton);
        }

        if (myCapbotsButton != null)
        {
            myCapbotsButton.onClick.RemoveAllListeners();
            myCapbotsButton.onClick.AddListener(OpenCapbotTab);
            var p = FirebaseManager.Instance.currentPlayer;
            myCapbotsButton.gameObject.SetActive(p != null && p.stakedTier >= 0);
        }
        CreateLivePreviews();
        RefreshCoins();
        UpdateUI();
        UpdateTierBadge();

        ApplyChildScaling();
    }

    private void OpenCapbotTab()
    {
        if (capbotTabScreen == null) return;
        TransitionTo(capbotTabScreen);
    }

    /// <summary>
    /// Back button — Phantom-authed users return to WalletScreen, X/guest users
    /// return to LoginScreen. Routing is based on whether the in-memory player
    /// has a Solana wallet address (set by the Phantom path, null otherwise).
    /// </summary>
    private void OnBackButton()
    {
        var p = FirebaseManager.Instance != null ? FirebaseManager.Instance.currentPlayer : null;
        bool wasPhantom = p != null && !string.IsNullOrEmpty(p.solanaWalletAddress);

        GameObject target = (wasPhantom && walletScreen != null) ? walletScreen : loginScreen;
        if (target == null)
        {
            Debug.LogError("[StarterSelectionScreen] Back target missing — neither walletScreen nor loginScreen wired");
            return;
        }

        Debug.Log($"=== [StarterSelectionScreen] Back → {(wasPhantom ? "WalletScreen" : "LoginScreen")} ===");

        CleanupPreviews();
        TransitionTo(target);
    }

    private void TransitionTo(GameObject target)
    {
        var sm = ScreenTransitionManager.Instance;
        if (sm != null) sm.GoTo(gameObject, target);
        else
        {
            gameObject.SetActive(false);
            target.SetActive(true);
        }
    }

    private void ApplyChildScaling()
    {
        if (starterCanvas == null)
        {
            Debug.LogWarning("[StarterSelectionScreen] starterCanvas not assigned - cannot apply scaling.");
            return;
        }
        if (childScalingApplied) return;

        Transform canvasTf = starterCanvas.transform;
        originalChildScales.Clear();

        for (int i = 0; i < canvasTf.childCount; i++)
        {
            Transform child = canvasTf.GetChild(i);
            originalChildScales[child] = child.localScale;
            child.localScale = child.localScale * 1f;
        }

        childScalingApplied = true;
        Debug.Log($"[StarterSelectionScreen] Scaled {originalChildScales.Count} canvas children to 3x.");
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

    public void RefreshCoins()
    {
        Debug.Log("=== [StarterSelectionScreen] RefreshCoins() called ===");
        UpdateStarterDisplay();
    }

    private void UpdateStarterDisplay()
    {
        var player = FirebaseManager.Instance.currentPlayer;
        if (player == null)
        {
            Debug.LogWarning("=== [StarterSelectionScreen] currentPlayer is NULL ===");
            return;
        }

        Debug.Log($"=== [StarterSelectionScreen] Coin values: Rage={player.rageblazeCoins} | Tsunami={player.tsunamiCoins} | Healspike={player.healspikeCoins} ===");

        if (rageblazeNameText != null) rageblazeNameText.text = "Rageblaze";
        if (rageblazeCoinsText != null) rageblazeCoinsText.text = FormatCoins(player.rageblazeCoins) + " coins";

        if (tsunamiNameText != null) tsunamiNameText.text = "Tsunami";
        if (tsunamiCoinsText != null) tsunamiCoinsText.text = FormatCoins(player.tsunamiCoins) + " coins";

        if (healspikeNameText != null) healspikeNameText.text = "Healspike";
        if (healspikeCoinsText != null) healspikeCoinsText.text = FormatCoins(player.healspikeCoins) + " coins";
    }

    private string FormatCoins(long coins)
    {
        return coins.ToString("N0");
    }

    private void CreateLivePreviews()
    {
        if (firePrefab != null && rageblazeButton != null)
        {
            ragePreview = Instantiate(firePrefab, rageblazeButton.transform);
            ragePreview.transform.localPosition = new Vector3(40f, -120f, 0f);
            ragePreview.transform.localScale = new Vector3(55f, 55f, 1f);

            Canvas previewCanvas = ragePreview.AddComponent<Canvas>();
            previewCanvas.renderMode = RenderMode.WorldSpace;
            previewCanvas.sortingOrder = 10;

            MonoBehaviour[] behaviours = ragePreview.GetComponentsInChildren<MonoBehaviour>();
            foreach (var b in behaviours) b.enabled = false;
        }

        if (waterPrefab != null && tsunamiButton != null)
        {
            tsunamiPreview = Instantiate(waterPrefab, tsunamiButton.transform);
            tsunamiPreview.transform.localPosition = new Vector3(20f, -90f, 0f);
            tsunamiPreview.transform.localScale = new Vector3(-55f, 55f, 1f);

            Canvas previewCanvas = tsunamiPreview.AddComponent<Canvas>();
            previewCanvas.renderMode = RenderMode.WorldSpace;
            previewCanvas.sortingOrder = 10;

            MonoBehaviour[] behaviours = tsunamiPreview.GetComponentsInChildren<MonoBehaviour>();
            foreach (var b in behaviours) b.enabled = false;
        }

        if (grassPrefab != null && healspikeButton != null)
        {
            healspikePreview = Instantiate(grassPrefab, healspikeButton.transform);
            healspikePreview.transform.localPosition = new Vector3(8f, -120f, 0f);
            healspikePreview.transform.localScale = new Vector3(22f, 22f, 1f);

            Canvas previewCanvas = healspikePreview.AddComponent<Canvas>();
            previewCanvas.renderMode = RenderMode.WorldSpace;
            previewCanvas.sortingOrder = 10;

            MonoBehaviour[] behaviours = healspikePreview.GetComponentsInChildren<MonoBehaviour>();
            foreach (var b in behaviours) b.enabled = false;
        }
    }

    private void CleanupPreviews()
    {
        if (ragePreview != null) { DOTween.Kill(ragePreview, true); Destroy(ragePreview); }
        if (tsunamiPreview != null) { DOTween.Kill(tsunamiPreview, true); Destroy(tsunamiPreview); }
        if (healspikePreview != null) { DOTween.Kill(healspikePreview, true); Destroy(healspikePreview); }
        ragePreview = null;
        tsunamiPreview = null;
        healspikePreview = null;
    }

    private void SelectStarter(string starterName)
    {
        Debug.Log("=== [StarterSelectionScreen] Starter selected: " + starterName);

        CleanupPreviews();

        if (FirebaseManager.Instance.currentPlayer != null)
        {
            FirebaseManager.Instance.currentPlayer.currentStarter = starterName;
        }

        BettingScreen bettingScreen = FindFirstObjectByType<BettingScreen>(FindObjectsInactive.Include);
        if (bettingScreen == null)
        {
            Debug.LogError("=== [StarterSelectionScreen] BettingScreen not found! ===");
            return;
        }

        TransitionTo(bettingScreen.gameObject);
    }

    private void UpdateUI()
    {
        var player = FirebaseManager.Instance.currentPlayer;
        if (player != null && rankText != null)
        {
            rankText.text = $"Current Rank: {player.rank}";
        }
    }

    private void UpdateTierBadge()
    {
        if (tierBadgeText == null) return;

        var player = FirebaseManager.Instance.currentPlayer;
        if (player == null || player.stakedTier < 0)
        {
            tierBadgeText.gameObject.SetActive(false);
            return;
        }

        int tier = Mathf.Clamp(player.stakedTier, 0, TIER_NAMES.Length - 1);
        float steps = player.stakedBrainSteps / 1_000_000f;
        tierBadgeText.text = $"{TIER_NAMES[tier]} · {steps:0.0}M brain · {TIER_MULTIPLIERS[tier]:0.0}x";
        tierBadgeText.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        RestoreChildScaling();
        CleanupPreviews();
    }
}