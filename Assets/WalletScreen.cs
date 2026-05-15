using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Wallet linking UI screen. Shown after LoginScreen for Phantom auth only.
/// X auth and guest mode skip this entirely and go directly to starter selection.
/// </summary>
public class WalletScreen : MonoBehaviour
{
    [Header("State panels (toggle one at a time)")]
    [SerializeField] private GameObject panelDisconnected;
    [SerializeField] private GameObject panelConnectedNoNft;
    [SerializeField] private GameObject panelConnectedWithNft;

    [Header("Buttons")]
    [SerializeField] private Button connectButton;
    [SerializeField] private TMP_Text connectButtonLabel;
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button continueButton;
    [SerializeField] private Button mintExternalButton;
    [SerializeField] private Button backButton;        // NEW: back to LoginScreen

    [Header("Text fields")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text walletAddressText;
    [SerializeField] private TMP_Text tierBadgeText;
    [SerializeField] private TMP_Text noNftHelpText;

    [Header("Navigation")]
    [SerializeField] private GameObject starterSelectionScreen;
    [SerializeField] private GameObject loginScreen;   // NEW: back-target

    private const string MINT_URL = "https://play.capmon.fun";

    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };

    private static readonly Color CONNECT_LABEL_DEFAULT = Color.white;
    private static readonly Color CONNECT_LABEL_LINKED  = new Color(0.6f, 1.0f, 0.6f);

    private bool wasShowingNoNftPanel = false;
    private Vector2? cachedNoNftPanelHomePos = null;
    private Coroutine clearStatusCoroutine = null;

    void Awake()
    {
        if (connectButton != null)
        {
            connectButton.onClick.RemoveAllListeners();
            connectButton.onClick.AddListener(OnConnectButton);
        }
        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(OnRefreshButton);
        }
        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(OnDisconnectButton);
        }
        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueButton);
        }
        if (mintExternalButton != null)
        {
            mintExternalButton.onClick.RemoveAllListeners();
            mintExternalButton.onClick.AddListener(OnMintExternalButton);
        }
        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButton);
        }
    }

    void OnEnable()
    {
        wasShowingNoNftPanel = false;
        RefreshUIFromCurrentState();
    }

    public void RefreshUIFromCurrentState()
    {
        bool isConnected = WalletManager.Instance != null && WalletManager.Instance.IsConnected;
        var stake = WalletManager.Instance != null ? WalletManager.Instance.CurrentStake : null;
        bool hasNft = stake != null && stake.stakedTier >= 0;

        SetPanelActive(panelDisconnected, !isConnected);
        SetPanelActive(panelConnectedNoNft, isConnected && !hasNft);
        SetPanelActive(panelConnectedWithNft, isConnected && hasNft);

        bool shouldShowNoNft = isConnected && !hasNft;
        bool justEnteredNoNft = shouldShowNoNft && !wasShowingNoNftPanel;
        if (justEnteredNoNft) PlayNoNftSlideIn();
        wasShowingNoNftPanel = shouldShowNoNft;

        if (mintExternalButton != null)
        {
            if (!shouldShowNoNft)
            {
                mintExternalButton.gameObject.SetActive(false);
            }
            else if (!justEnteredNoNft)
            {
                mintExternalButton.gameObject.SetActive(true);
            }
        }

        if (connectButton != null)
        {
            connectButton.gameObject.SetActive(true);
            connectButton.interactable = !isConnected;

            if (connectButtonLabel != null)
            {
                connectButtonLabel.text = isConnected ? "Linked!" : "Link Phantom Wallet";
                connectButtonLabel.color = isConnected ? CONNECT_LABEL_LINKED : CONNECT_LABEL_DEFAULT;
            }
        }

        if (disconnectButton != null) disconnectButton.gameObject.SetActive(isConnected);
        if (refreshButton != null)    refreshButton.gameObject.SetActive(isConnected);

        if (continueButton != null) continueButton.gameObject.SetActive(isConnected && hasNft);

        if (isConnected)
        {
            string pubkey = WalletManager.Instance.ConnectedWallet;
            if (walletAddressText != null) walletAddressText.text = TruncateAddress(pubkey);
        }
        else
        {
            if (walletAddressText != null) walletAddressText.text = "";
        }

        if (hasNft && tierBadgeText != null)
        {
            int tier = Mathf.Clamp(stake.stakedTier, 0, TIER_NAMES.Length - 1);
            float steps = stake.stakedBrainSteps / 1_000_000f;
            tierBadgeText.text = $"{TIER_NAMES[tier]} {steps:0.0}M Brain {TIER_MULTIPLIERS[tier]:0.0}x";
        }

        if (!isConnected) SetStatus("");
    }

    private void PlayNoNftSlideIn()
    {
        if (panelConnectedNoNft == null) return;
        var rt = panelConnectedNoNft.GetComponent<RectTransform>();
        if (rt == null) return;

        if (!cachedNoNftPanelHomePos.HasValue)
        {
            cachedNoNftPanelHomePos = rt.anchoredPosition;
        }
        Vector2 home = cachedNoNftPanelHomePos.Value;

        rt.DOKill();
        if (mintExternalButton != null) mintExternalButton.transform.DOKill();

        if (mintExternalButton != null) mintExternalButton.gameObject.SetActive(false);

        rt.anchoredPosition = new Vector2(-1500f, home.y);
        rt.DOAnchorPos(home, 0.5f).SetEase(Ease.OutCubic).OnComplete(() =>
        {
            if (mintExternalButton == null) return;
            mintExternalButton.gameObject.SetActive(true);
            mintExternalButton.transform.localScale = Vector3.zero;
            mintExternalButton.transform.DOScale(1f, 0.3f).SetEase(Ease.OutBack);
        });
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null && panel.activeSelf != active) panel.SetActive(active);
    }

    private static string TruncateAddress(string address)
    {
        if (string.IsNullOrEmpty(address) || address.Length < 8) return address ?? "";
        return address.Substring(0, 4) + "..." + address.Substring(address.Length - 4);
    }

    private void SetStatus(string msg, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text = msg;
        statusText.color = isError ? new Color(0.95f, 0.4f, 0.4f) : Color.white;
    }

    private IEnumerator ClearStatusAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        SetStatus("");
        clearStatusCoroutine = null;
    }

    private void StartClearStatusTimer(float seconds = 1.5f)
    {
        if (clearStatusCoroutine != null) StopCoroutine(clearStatusCoroutine);
        clearStatusCoroutine = StartCoroutine(ClearStatusAfterDelay(seconds));
    }

    // ====================== BUTTON HANDLERS ======================

    private void OnConnectButton()
    {
        Debug.Log("🌐 [WalletScreen] Connect pressed");

        if (WalletManager.Instance == null)
        {
            SetStatus("Wallet system not initialized", true);
            return;
        }

        if (!WalletManager.Instance.IsPhantomInstalled())
        {
            SetStatus("Phantom wallet not detected. Install at phantom.app", true);
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Connecting to Phantom...");

        WalletManager.Instance.ConnectWallet(
            onSuccess: pubkey =>
            {
                Debug.Log("✅ [WalletScreen] Connected: " + pubkey);
                SetStatus("Verifying ownership...");
                AttemptLink();
            },
            onError: err =>
            {
                Debug.LogError("❌ [WalletScreen] Connect error: " + err);
                SetStatus("Connect failed: " + err, true);
                SetButtonsInteractable(true);
            }
        );
    }

    private void AttemptLink()
    {
        WalletManager.Instance.LinkWallet(
            onSuccess: result =>
            {
                Debug.Log("✅ [WalletScreen] Link success: tier=" + result.stakedTier);

                if (FirebaseManager.Instance != null)
                {
                    FirebaseManager.Instance.ApplyStakeStateFromLink(result);
                }

                if (result.stakedTier >= 0)
                {
                    SetStatus("Linked! " + TIER_NAMES[result.stakedTier] + " active");
                }
                else
                {
                    SetStatus("Wallet linked, no staked Capmon NFT found");
                }

                RefreshUIFromCurrentState();
                SetButtonsInteractable(true);
                StartClearStatusTimer();
            },
            onError: err =>
            {
                Debug.LogError("❌ [WalletScreen] Link error: " + err);
                SetStatus("Link failed: " + err, true);
                RefreshUIFromCurrentState();
                SetButtonsInteractable(true);
            }
        );
    }

    private void OnRefreshButton()
    {
        Debug.Log("🔄 [WalletScreen] Refresh pressed");

        if (WalletManager.Instance == null || !WalletManager.Instance.IsConnected)
        {
            SetStatus("Not connected", true);
            return;
        }

        SetButtonsInteractable(false);
        SetStatus("Refreshing stake state...");

        WalletManager.Instance.RefreshStakeState(
            onSuccess: result =>
            {
                if (FirebaseManager.Instance != null)
                {
                    FirebaseManager.Instance.ApplyStakeStateFromLink(result);
                }

                SetStatus(result.stakedTier >= 0 ? "Updated" : "No staked NFT found");
                RefreshUIFromCurrentState();
                SetButtonsInteractable(true);
                StartClearStatusTimer();
            },
            onError: err =>
            {
                Debug.LogError("❌ [WalletScreen] Refresh error: " + err);
                SetStatus("Refresh failed: " + err, true);
                SetButtonsInteractable(true);
            }
        );
    }

    private void OnDisconnectButton()
    {
        Debug.Log("🌐 [WalletScreen] Disconnect pressed");

        if (WalletManager.Instance == null) return;

        SetButtonsInteractable(false);
        SetStatus("Disconnecting...");

        WalletManager.Instance.DisconnectWallet(() =>
        {
            if (FirebaseManager.Instance != null && FirebaseManager.Instance.currentPlayer != null)
            {
                FirebaseManager.Instance.currentPlayer.solanaWalletAddress = null;
                FirebaseManager.Instance.currentPlayer.stakedTier = -1;
                FirebaseManager.Instance.currentPlayer.stakedBrainSteps = 0;
                FirebaseManager.Instance.currentPlayer.stakedAssetId = null;
            }

            SetStatus("");
            RefreshUIFromCurrentState();
            SetButtonsInteractable(true);
        });
    }

    private void OnMintExternalButton()
    {
        Debug.Log("🌐 [WalletScreen] Open mint URL");
        Application.OpenURL(MINT_URL);
    }

    private void OnContinueButton()
    {
        Debug.Log("=== [WalletScreen] Continue pressed — transitioning to StarterSelection ===");

        if (starterSelectionScreen == null)
        {
            Debug.LogError("=== [WalletScreen] CRITICAL: starterSelectionScreen reference missing! ===");
            return;
        }

        TransitionTo(starterSelectionScreen);
    }

    /// <summary>
    /// Back button — returns to LoginScreen. Wallet stays connected in WalletManager;
    /// disconnect requires the explicit Disconnect button so users don't lose state by accident.
    /// </summary>
    private void OnBackButton()
    {
        Debug.Log("=== [WalletScreen] Back pressed — returning to LoginScreen ===");

        if (loginScreen == null)
        {
            Debug.LogError("=== [WalletScreen] CRITICAL: loginScreen reference missing! ===");
            return;
        }

        TransitionTo(loginScreen);
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

    private void SetButtonsInteractable(bool interactable)
    {
        if (refreshButton != null)    refreshButton.interactable    = interactable;
        if (disconnectButton != null) disconnectButton.interactable = interactable;
    }
}