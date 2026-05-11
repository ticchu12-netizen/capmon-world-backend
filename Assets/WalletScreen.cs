using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Wallet linking UI screen. Shown after LoginScreen for Phantom auth only.
/// X auth and guest mode skip this entirely and go directly to starter selection.
///
/// On Phantom auth the user lands here already connected (the LoginScreen
/// Phantom button drove the sign-in). The connect button reads "Linked!" in
/// light green and is non-interactive. The user then sees one of two states:
///   - Connected, no staked NFT: text panel slides in from left, then the
///     "Get Capmon NFT" button reveals (only visible in this state). They
///     mint at play.capmon.fun and come back to refresh.
///   - Connected, NFT staked: tier badge with emoji ("👑 King 60.0M brain 2.8×")
///     and the Continue button becomes available.
///
/// The disconnected state ("Link Phantom Wallet" button) is preserved for edge
/// cases (e.g., Phantom session dropped mid-flow, future "link from settings"
/// surfaces) but is not the primary path under the current LoginScreen routing.
///
/// Continue button is hidden until wallet is linked AND an NFT is active.
/// This commits the flow to "every Phantom user plays with a Capbot" and
/// nudges users without an NFT toward the mint flow rather than letting them
/// skip into manual mode with no Capbot.
///
/// Flow: Connect button -> WalletManager.ConnectWallet (Phantom popup) ->
///       on success -> WalletManager.LinkWallet (sign-message popup -> Cloud
///       Function -> on-chain query -> Firestore mirror) -> on success ->
///       FirebaseManager.ApplyStakeStateFromLink (push tier into currentPlayer).
///
/// Continue advances to StarterSelectionScreen.
/// </summary>
public class WalletScreen : MonoBehaviour
{
    [Header("State panels (toggle one at a time)")]
    [SerializeField] private GameObject panelDisconnected;
    [SerializeField] private GameObject panelConnectedNoNft;
    [SerializeField] private GameObject panelConnectedWithNft;

    [Header("Buttons")]
    [SerializeField] private Button connectButton;
    [SerializeField] private TMP_Text connectButtonLabel;  // TMP_Text on the connect button (for "Linked!" state)
    [SerializeField] private Button refreshButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button continueButton;        // "Continue" -> StarterSelectionScreen
    [SerializeField] private Button mintExternalButton;    // opens play.capmon.fun in new tab (no-NFT state only)

    [Header("Text fields")]
    [SerializeField] private TMP_Text statusText;          // shared across panels
    [SerializeField] private TMP_Text walletAddressText;   // truncated pubkey
    [SerializeField] private TMP_Text tierBadgeText;       // "👑 King 60.0M brain 2.8×"
    [SerializeField] private TMP_Text noNftHelpText;       // explanation copy on no-NFT panel

    [Header("Next screen")]
    [SerializeField] private GameObject starterSelectionScreen;

    // External URL the "Get Capmon NFT" button opens
    private const string MINT_URL = "https://play.capmon.fun";

    // Tier display tables (must mirror Solana program tier ranges)
    // Locked Day 2: Evergreen 0-14M, Aquashrine 15-39M, Magmamine 40-59M, King 60M
    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };

    // Connect button color states
    private static readonly Color CONNECT_LABEL_DEFAULT = Color.white;
    private static readonly Color CONNECT_LABEL_LINKED  = new Color(0.6f, 1.0f, 0.6f); // light green

    // Animation tracking — only play slide-in when transitioning INTO no-NFT state,
    // not on every refresh while the panel stays visible.
    private bool wasShowingNoNftPanel = false;
    private Vector2? cachedNoNftPanelHomePos = null;
    private Coroutine clearStatusCoroutine = null;

    void Awake()
    {
        // Wire button listeners. Use AddListener (not onClick assignment in Inspector
        // alone) so the bindings survive prefab reloads and don't get duplicated.
        // Guard against double-bind by removing first.
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
    }

    void OnEnable()
    {
        // Reset transition tracker so an animation plays again on screen re-entry.
        wasShowingNoNftPanel = false;

        // Refresh state every time the screen is shown — handles the case where
        // user disconnected externally in Phantom, or stake state changed via
        // play.capmon.fun in another tab.
        RefreshUIFromCurrentState();
    }

    /// <summary>
    /// Recompute which panel is visible based on WalletManager + FirebaseManager state.
    /// Called on enable + after every successful action.
    /// </summary>
    public void RefreshUIFromCurrentState()
    {
        bool isConnected = WalletManager.Instance != null && WalletManager.Instance.IsConnected;
        var stake = WalletManager.Instance != null ? WalletManager.Instance.CurrentStake : null;
        bool hasNft = stake != null && stake.stakedTier >= 0;

        // Content panel toggle (only one visible at a time)
        SetPanelActive(panelDisconnected, !isConnected);
        SetPanelActive(panelConnectedNoNft, isConnected && !hasNft);
        SetPanelActive(panelConnectedWithNft, isConnected && hasNft);

        // Slide-in animation for no-NFT panel — only on transition INTO this state
        bool shouldShowNoNft = isConnected && !hasNft;
        bool justEnteredNoNft = shouldShowNoNft && !wasShowingNoNftPanel;
        if (justEnteredNoNft) PlayNoNftSlideIn();
        wasShowingNoNftPanel = shouldShowNoNft;

        // Mint button visibility — only in no-NFT state. Animation controls
        // its activation when entering; if we're staying in this state from
        // a refresh, keep it visible.
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
            // else: animation OnComplete will activate it
        }

        // Connect button — always visible. Becomes "Linked!" + light green +
        // non-interactive once connected. The "Link Phantom Wallet" CTA only
        // applies in the disconnected state.
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

        // Disconnect / refresh — only when connected
        if (disconnectButton != null) disconnectButton.gameObject.SetActive(isConnected);
        if (refreshButton != null)    refreshButton.gameObject.SetActive(isConnected);

        // Continue button — ONLY when wallet is linked AND an NFT is active.
        // This forces the mint flow for users without a Capmon, rather than
        // letting them skip into manual mode with no Capbot.
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

    /// <summary>
    /// Slide the no-NFT text panel in from the left, then reveal the
    /// "Get Capmon NFT" button after the slide completes.
    /// Called only when transitioning INTO the connected-no-NFT state.
    /// </summary>
    private void PlayNoNftSlideIn()
    {
        if (panelConnectedNoNft == null) return;
        var rt = panelConnectedNoNft.GetComponent<RectTransform>();
        if (rt == null) return;

        // Cache the panel's home position the first time we see it, so subsequent
        // animations always slide back to the correct resting place even if a
        // previous tween was interrupted mid-flight.
        if (!cachedNoNftPanelHomePos.HasValue)
        {
            cachedNoNftPanelHomePos = rt.anchoredPosition;
        }
        Vector2 home = cachedNoNftPanelHomePos.Value;

        // Kill any in-flight tween + button tween to avoid stacked animations
        rt.DOKill();
        if (mintExternalButton != null) mintExternalButton.transform.DOKill();

        // Hide button — animation will reveal it OnComplete
        if (mintExternalButton != null) mintExternalButton.gameObject.SetActive(false);

        // Start off-screen left, slide to home over 0.5s
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

    /// <summary>
    /// Clear status text after a brief delay so transient messages like
    /// "Updated" don't compete with the persistent "Linked!" + tier badge UI.
    /// </summary>
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
                // Immediately link — single user-facing action ("Link Wallet")
                // covers connect + sign-message + CF round-trip.
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

                // Push stake state into PlayerData so resolveMatch + battle UI see it
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

                // Let the transient confirmation fade so the persistent UI
                // ("Linked!" button + tier badge) becomes the visible signal.
                StartClearStatusTimer();
            },
            onError: err =>
            {
                Debug.LogError("❌ [WalletScreen] Link error: " + err);
                SetStatus("Link failed: " + err, true);
                // Even on link failure the wallet is connected; user can retry refresh
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

        // Re-runs sign-message + CF call. Phantom popup will appear again — that's
        // intentional, every linkWallet call is a fresh ownership proof, prevents
        // stale CF responses from being trusted across long-lived sessions.
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

                // After "Updated" briefly displays, clear it so the linked UI
                // (button reads "Linked!" + tier badge) is the persistent signal.
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
            // Clear stake state on the player too — losing the wallet means
            // resolveMatch reverts to base 1.0× multiplier on next battle.
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

    /// <summary>
    /// "Continue" — only reachable when wallet is linked AND an NFT is active
    /// (button is hidden otherwise). Advances to StarterSelectionScreen.
    /// </summary>
    private void OnContinueButton()
    {
        Debug.Log("=== [WalletScreen] Continue pressed — advancing to StarterSelection ===");
        gameObject.SetActive(false);

        if (starterSelectionScreen != null)
        {
            Debug.Log("=== [WalletScreen] SUCCESS: Activating StarterSelectionScreen ===");
            starterSelectionScreen.SetActive(true);
        }
        else
        {
            Debug.LogError("=== [WalletScreen] CRITICAL: starterSelectionScreen reference missing in Inspector! ===");
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        // Connect button interactability is governed by RefreshUIFromCurrentState
        // (locked off when already linked). Skip it here so we don't accidentally
        // re-enable it after a successful link.
        if (refreshButton != null)    refreshButton.interactable    = interactable;
        if (disconnectButton != null) disconnectButton.interactable = interactable;
        // continueButton stays interactable when visible — user must always be able
        // to advance once linked, even if a refresh is mid-flight.
    }
}