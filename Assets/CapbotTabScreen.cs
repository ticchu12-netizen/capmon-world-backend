using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Capbot tab — shows tier badge, brain-steps progress (toward tier ceiling),
/// recent autonomous battles, on-chain brain upgrades, and a live prefab
/// preview of the staked Capbot. Opened from StarterSelectionScreen for
/// users with a staked NFT. Close returns to WalletScreen (the central hub
/// for Phantom users) rather than StarterSelectionScreen.
///
/// Both the battle log and the upgrade log are clickable. Each row is wrapped
/// in a TMP &lt;link&gt; tag carrying its battleId; clicking opens the /replay
/// page for that specific battle in an iframe overlay (see ReplayOverlay).
/// Rows without a battleId (e.g., legacy entries written before server-side
/// battleId field was added) display normally but aren't clickable.
/// </summary>
public class CapbotTabScreen : MonoBehaviour
{
    [Header("Header")]
    public TMP_Text tierLabelText;       // "King · 2.8× multiplier"
    public TMP_Text brainProgressText;   // "60.0M / 60.0M"
    public Image brainProgressBar;       // optional, fillAmount 0..1

    [Header("Identity")]
    public TMP_Text assetIdText;         // truncated cNFT asset id
    public TMP_Text walletText;          // truncated Solana pubkey

    [Header("Logs (multiline TMP_Text)")]
    public TMP_Text battleLogText;
    public TMP_Text upgradeLogText;

    [Header("Buttons")]
    public Button refreshButton;
    public Button closeButton;
    public Button openExplorerButton;    // opens the most recent upgrade tx

    [Header("Navigation")]
    public GameObject walletScreen;             // primary close target — wallet hub
    public GameObject starterSelectionScreen;   // fallback if walletScreen isn't wired

    [Header("Status")]
    public TMP_Text statusText;

    [Header("Live Preview - Drag the same character prefabs StarterSelectionScreen uses")]
    public GameObject firePrefab;        // Rageblaze (Fire)
    public GameObject waterPrefab;       // Tsunami (Water)
    public GameObject grassPrefab;       // Healspike (Grass)
    public Transform previewAnchor;      // Where the preview is parented (e.g., a UI panel/container)
    public Vector3 previewLocalPosition = Vector3.zero;
    public Vector3 previewLocalScale = new Vector3(50f, 50f, 10f);

    private GameObject capbotPreview;

    // Click handlers attached at runtime to the log text GameObjects
    private TMPLinkClickHandler upgradeLogHandler;
    private TMPLinkClickHandler battleLogHandler;

    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };
    private static readonly int[]    TIER_FLOORS    = { 0, 15_000_000, 40_000_000, 55_000_000 };
    private static readonly int[]    TIER_CEILINGS  = { 14_000_000, 39_000_000, 54_000_000, 60_000_000 };

    private const string EXPLORER_BASE = "https://solana.fm/tx/";
    private const string EXPLORER_SUFFIX = "?cluster=devnet-solana";

    // Color for clickable log entries (cyan, on-brand)
    private const string LINK_COLOR_HEX = "#00f0ff";

    private string mostRecentTxSig = null;

    void Awake()
    {
        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveAllListeners();
            refreshButton.onClick.AddListener(LoadData);
        }
        if (closeButton != null)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(OnCloseButton);
        }
        if (openExplorerButton != null)
        {
            openExplorerButton.onClick.RemoveAllListeners();
            openExplorerButton.onClick.AddListener(OnOpenExplorerButton);
        }
        // Note: click handler wiring lives in WireLogClicks() and is called
        // from RenderData() after the log text is populated. TMP needs the
        // text content present before link rect data can be computed.
    }

    void OnEnable()
    {
        ClearLogs();
        SetStatus("Loading...");
        LoadData();
    }

    private void LoadData()
    {
        SetStatus("Loading...");
        FirebaseManager.Instance.GetCapbotData(
            onSuccess: data => RenderData(data),
            onError: err => SetStatus("Error: " + err, true)
        );
    }

    private void RenderData(FirebaseManager.CapbotData data)
    {
        // Header
        if (data.stakedTier < 0 || data.stakedTier >= TIER_NAMES.Length)
        {
            SetStatus("No staked NFT", true);
            return;
        }
        int tier = data.stakedTier;
        if (tierLabelText != null)
            tierLabelText.text = $"{TIER_NAMES[tier]} {TIER_MULTIPLIERS[tier]:0.0}x multiplier";

        // Brain progress (within current tier's floor->ceiling range)
        int floor = TIER_FLOORS[tier];
        int ceiling = TIER_CEILINGS[tier];
        int span = ceiling - floor;
        float pct = span > 0 ? Mathf.Clamp01((data.stakedBrainSteps - floor) / (float)span) : 1f;
        if (brainProgressText != null)
            brainProgressText.text = $"{data.stakedBrainSteps / 1_000_000f:0.00}M / {ceiling / 1_000_000f:0.00}M";
        if (brainProgressBar != null)
            brainProgressBar.fillAmount = pct;

        // Identity
        if (assetIdText != null)
            assetIdText.text = "Asset: " + Truncate(data.stakedAssetId);
        if (walletText != null)
            walletText.text = "Wallet: " + Truncate(data.walletAddress);

        // Live preview of the staked Capbot
        CreateCapbotPreview(tier);

        // Battle log — each row wrapped in <link> tag carrying the battleId
        // so clicks route to ReplayOverlay.Show(battleId) via TMPLinkClickHandler.
        // Requires RecentBattle to include 'battleId' (set by capbot-server).
        // If battleId is missing, the row is shown as plain text (not clickable).
        if (battleLogText != null)
        {
            var sb = new StringBuilder();
            if (data.recentBattles == null || data.recentBattles.Length == 0)
            {
                sb.Append("No battles yet. Capbot starts in the next tick.");
            }
            else
            {
                for (int i = 0; i < data.recentBattles.Length && i < 10; i++)
                {
                    var b = data.recentBattles[i];
                    string sign = b.payout >= 0 ? "+" : "";
                    string outcome = b.playerWon ? "Won" : "Lost";
                    string entry = $"{outcome} {sign}{b.payout:N0} vs {b.defeatedAi}  ({b.multiplier:0.0}×, {RelativeTime(b.timestamp)})";

                    if (!string.IsNullOrEmpty(b.battleId))
                    {
                        sb.Append($"<link=\"{b.battleId}\"><color={LINK_COLOR_HEX}>{entry}</color></link>");
                    }
                    else
                    {
                        sb.Append(entry);
                    }
                    sb.AppendLine();
                }
            }
            battleLogText.text = sb.ToString();
            // TMP requires this after any text change for link rect data to populate
            battleLogText.ForceMeshUpdate();
        }

        // Brain upgrades log — same pattern, wrapped in <link> tag carrying battleId
        mostRecentTxSig = null;
        if (upgradeLogText != null)
        {
            var sb = new StringBuilder();
            if (data.recentUpgrades == null || data.recentUpgrades.Length == 0)
            {
                sb.Append("No on-chain upgrades yet.");
            }
            else
            {
                mostRecentTxSig = data.recentUpgrades[0].txSignature;
                for (int i = 0; i < data.recentUpgrades.Length && i < 5; i++)
                {
                    var u = data.recentUpgrades[i];
                    string entry = $"{u.oldBrainSteps / 1_000_000f:0.0}M → {u.newBrainSteps / 1_000_000f:0.0}M  ({TruncateSig(u.txSignature)}, {RelativeTime(u.timestamp)})";

                    if (!string.IsNullOrEmpty(u.battleId))
                    {
                        sb.Append($"<link=\"{u.battleId}\"><color={LINK_COLOR_HEX}>{entry}</color></link>");
                    }
                    else
                    {
                        sb.Append(entry);
                    }
                    sb.AppendLine();
                }
            }
            upgradeLogText.text = sb.ToString();
            upgradeLogText.ForceMeshUpdate();
        }

        if (openExplorerButton != null)
            openExplorerButton.interactable = !string.IsNullOrEmpty(mostRecentTxSig);

        // Wire click detection on both logs AFTER text is populated and meshes updated.
        // Idempotent — safe to call on every refresh; old subscriptions are cleared first.
        WireLogClicks();

        SetStatus("");
    }

    /// <summary>
    /// Wires click detection on both log scroll views. Idempotent — safe to call
    /// every time the tab activates or the log content is refreshed.
    /// Must be called AFTER the log text has been populated and ForceMeshUpdate
    /// has been called, because TMP needs the link rect data computed before
    /// FindIntersectingLink can detect clicks correctly.
    /// </summary>
    private void WireLogClicks()
    {
        WireOneLog(upgradeLogText, ref upgradeLogHandler, "UpgradeLog");
        WireOneLog(battleLogText,  ref battleLogHandler,  "BattleLog");
    }

    private void WireOneLog(TMP_Text logText, ref TMPLinkClickHandler handler, string debugName)
    {
        if (logText == null)
        {
            Debug.LogWarning($"[CapbotTabScreen] {debugName} text not assigned in Inspector");
            return;
        }

        // Defensive: ensure raycast is on (Inspector value can drift between sessions)
        logText.raycastTarget = true;

        // Get or add the handler on the SAME GameObject as the TMP_Text
        handler = logText.GetComponent<TMPLinkClickHandler>();
        if (handler == null)
        {
            handler = logText.gameObject.AddComponent<TMPLinkClickHandler>();
        }

        // Subscribe with += (events require +=, not =). Unsubscribe first to
        // guard against double-binding when LoadData refreshes multiple times.
        handler.onLinkClicked -= OnAnyLogLinkClicked;
        handler.onLinkClicked += OnAnyLogLinkClicked;
    }

    /// <summary>
    /// Shared callback for both logs. Action is identical regardless of which
    /// log was clicked: open the battle replay overlay for the given battleId.
    /// </summary>
    private void OnAnyLogLinkClicked(string battleId)
    {
        if (string.IsNullOrEmpty(battleId))
        {
            Debug.LogWarning("[CapbotTabScreen] Link clicked but battleId was empty");
            return;
        }
        Debug.Log("[CapbotTabScreen] Opening replay for battleId: " + battleId);
        ReplayOverlay.Show(battleId);
    }

    /// <summary>
    /// Instantiate the staked Capbot's prefab as a frozen live preview.
    /// Mirrors the StarterSelectionScreen.CreateLivePreviews pattern: world-space
    /// canvas + disabled MonoBehaviours so the prefab renders without running
    /// any AI/battle scripts.
    ///
    /// Tier-to-type mapping (must mirror server-side capbot-server + Anchor program):
    ///   Tier 0 (Evergreen)  -> Healspike (Grass)
    ///   Tier 1 (Aquashrine) -> Tsunami   (Water)
    ///   Tier 2 (Magmamine)  -> Rageblaze (Fire)
    ///   Tier 3 (King)       -> Rageblaze (Fire)
    /// </summary>
    private void CreateCapbotPreview(int tier)
    {
        // Tear down any existing preview before creating a new one
        if (capbotPreview != null)
        {
            DOTween.Kill(capbotPreview, true);
            Destroy(capbotPreview);
            capbotPreview = null;
        }

        if (previewAnchor == null) return;

        GameObject prefabToUse = null;
        if (tier == 0) prefabToUse = grassPrefab;
        else if (tier == 1) prefabToUse = waterPrefab;
        else if (tier == 2 || tier == 3) prefabToUse = firePrefab;

        if (prefabToUse == null)
        {
            Debug.LogWarning($"[CapbotTabScreen] No prefab assigned for tier {tier} — preview skipped.");
            return;
        }

        capbotPreview = Instantiate(prefabToUse, previewAnchor);
        capbotPreview.transform.localPosition = previewLocalPosition;
        capbotPreview.transform.localScale = previewLocalScale;

        Canvas previewCanvas = capbotPreview.AddComponent<Canvas>();
        previewCanvas.renderMode = RenderMode.WorldSpace;
        previewCanvas.sortingOrder = 10;

        MonoBehaviour[] behaviours = capbotPreview.GetComponentsInChildren<MonoBehaviour>();
        foreach (var b in behaviours) b.enabled = false;
    }

    private void ClearLogs()
    {
        if (battleLogText != null) battleLogText.text = "";
        if (upgradeLogText != null) upgradeLogText.text = "";
        if (tierLabelText != null) tierLabelText.text = "";
        if (brainProgressText != null) brainProgressText.text = "";
        if (assetIdText != null) assetIdText.text = "";
        if (walletText != null) walletText.text = "";
    }

    private void SetStatus(string msg, bool isError = false)
    {
        if (statusText == null) return;
        statusText.text = msg;
        statusText.color = isError ? new Color(0.95f, 0.4f, 0.4f) : Color.white;
    }

    private static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 10) return s ?? "";
        return s.Substring(0, 4) + "..." + s.Substring(s.Length - 4);
    }

    private static string TruncateSig(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length < 12) return s ?? "";
        return s.Substring(0, 8) + "...";
    }

    private static string RelativeTime(long timestampMs)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long diff = (now - timestampMs) / 1000;
        if (diff < 60) return "just now";
        if (diff < 3600) return $"{diff / 60}m ago";
        if (diff < 86400) return $"{diff / 3600}h ago";
        return $"{diff / 86400}d ago";
    }

    private void OnOpenExplorerButton()
    {
        if (string.IsNullOrEmpty(mostRecentTxSig)) return;
        Application.OpenURL(EXPLORER_BASE + mostRecentTxSig + EXPLORER_SUFFIX);
    }

    /// <summary>
    /// Close goes back to the WalletScreen (the central hub for Phantom users).
    /// Falls back to StarterSelectionScreen only if walletScreen isn't wired in
    /// the inspector — keeps the screen functional during partial setup.
    /// </summary>
    private void OnCloseButton()
    {
        gameObject.SetActive(false);
        if (walletScreen != null)
        {
            walletScreen.SetActive(true);
        }
        else if (starterSelectionScreen != null)
        {
            Debug.LogWarning("[CapbotTabScreen] walletScreen not assigned — falling back to StarterSelectionScreen");
            starterSelectionScreen.SetActive(true);
        }
        else
        {
            Debug.LogError("[CapbotTabScreen] CRITICAL: neither walletScreen nor starterSelectionScreen wired!");
        }
    }

    void OnDisable()
    {
        // Tear down the live preview when leaving the screen so it doesn't
        // linger or duplicate on next entry.
        if (capbotPreview != null) Destroy(capbotPreview);
        capbotPreview = null;
    }
}