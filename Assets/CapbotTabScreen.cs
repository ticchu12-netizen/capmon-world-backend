using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// Capbot tab — paginated multi-stake. Each "page" is the original single-stake
/// UI showing data for one staked NFT. Prev/Next buttons cycle through stakes.
/// Battle log + upgrade log are filtered to the current stake's assetId.
/// </summary>
public class CapbotTabScreen : MonoBehaviour
{
    [Header("Header")]
    public TMP_Text tierLabelText;
    public TMP_Text brainProgressText;
    public Image brainProgressBar;

    [Header("Identity")]
    public TMP_Text assetIdText;
    public TMP_Text walletText;

    [Header("Logs (multiline TMP_Text)")]
    public TMP_Text battleLogText;
    public TMP_Text upgradeLogText;

    [Header("Buttons")]
    public Button refreshButton;
    public Button closeButton;
    public Button openExplorerButton;
    public Button practiceBattleButton;

    [Header("Pagination (NEW)")]
    public Button prevButton;
    public Button nextButton;
    public TMP_Text pageIndicatorText;  // "Capbot 1 of 4"

    [Header("Navigation")]
    public GameObject walletScreen;
    public GameObject starterSelectionScreen;
    public GameObject bettingScreen;

    [Header("Status")]
    public TMP_Text statusText;

    [Header("Live Preview - same as original (single anchor, shared pos/scale)")]
    public GameObject firePrefab;
    public GameObject waterPrefab;
    public GameObject grassPrefab;
    public Transform previewAnchor;
    public Vector3 previewLocalPosition = Vector3.zero;
    public Vector3 previewLocalScale = new Vector3(50f, 50f, 10f);

    private GameObject capbotPreview;

    private TMPLinkClickHandler upgradeLogHandler;
    private TMPLinkClickHandler battleLogHandler;

    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };
    private static readonly int[]    TIER_FLOORS    = { 0, 15_000_000, 40_000_000, 55_000_000 };
    private static readonly int[]    TIER_CEILINGS  = { 14_000_000, 39_000_000, 54_000_000, 60_000_000 };
    private static readonly string[] TIER_TO_STARTER = { "Healspike", "Tsunami", "Rageblaze", "Rageblaze" };

    private const string EXPLORER_BASE = "https://solana.fm/tx/";
    private const string EXPLORER_SUFFIX = "?cluster=devnet-solana";
    private const string LINK_COLOR_HEX = "#00f0ff";

    private string mostRecentTxSig = null;
    private int currentTier = -1;

    // Pagination state
    private FirebaseManager.CapbotData cachedData;
    private List<FirebaseManager.CapbotStakeEntry> sortedStakes = new List<FirebaseManager.CapbotStakeEntry>();
    private int currentStakeIndex = 0;

    void Awake()
    {
        if (refreshButton != null) { refreshButton.onClick.RemoveAllListeners(); refreshButton.onClick.AddListener(LoadData); }
        if (closeButton != null) { closeButton.onClick.RemoveAllListeners(); closeButton.onClick.AddListener(OnCloseButton); }
        if (openExplorerButton != null) { openExplorerButton.onClick.RemoveAllListeners(); openExplorerButton.onClick.AddListener(OnOpenExplorerButton); }
        if (practiceBattleButton != null) { practiceBattleButton.onClick.RemoveAllListeners(); practiceBattleButton.onClick.AddListener(OnPracticeBattleButton); }
        if (prevButton != null) { prevButton.onClick.RemoveAllListeners(); prevButton.onClick.AddListener(OnPrevButton); }
        if (nextButton != null) { nextButton.onClick.RemoveAllListeners(); nextButton.onClick.AddListener(OnNextButton); }
    }

    void OnEnable()
    {
        ClearLogs();
        SetStatus("Loading...");
        if (practiceBattleButton != null) practiceBattleButton.interactable = false;
        if (prevButton != null) prevButton.interactable = false;
        if (nextButton != null) nextButton.interactable = false;
        LoadData();
    }

    private void LoadData()
    {
        SetStatus("Loading...");
        FirebaseManager.Instance.GetCapbotData(
            onSuccess: data => OnDataLoaded(data),
            onError: err => SetStatus("Error: " + err, true)
        );
    }

    private void OnDataLoaded(FirebaseManager.CapbotData data)
    {
        cachedData = data;

        // Build stake list — prefer new stakes[] array, fall back to flat fields
        sortedStakes.Clear();
        if (data.stakes != null && data.stakes.Length > 0)
        {
            sortedStakes.AddRange(data.stakes);
        }
        else if (data.stakedTier >= 0 && !string.IsNullOrEmpty(data.stakedAssetId))
        {
            sortedStakes.Add(new FirebaseManager.CapbotStakeEntry
            {
                assetId = data.stakedAssetId,
                tier = data.stakedTier,
                brainSteps = data.stakedBrainSteps,
                lastBattleAt = 0,
            });
        }

        // Sort: highest tier first, then by assetId for stable ordering
        sortedStakes = sortedStakes.OrderByDescending(s => s.tier).ThenBy(s => s.assetId).ToList();

        if (sortedStakes.Count == 0)
        {
            SetStatus("No staked NFTs", true);
            if (practiceBattleButton != null) practiceBattleButton.interactable = false;
            if (prevButton != null) prevButton.interactable = false;
            if (nextButton != null) nextButton.interactable = false;
            if (pageIndicatorText != null) pageIndicatorText.text = "";
            return;
        }

        // Clamp current page if stakes shrank
        if (currentStakeIndex >= sortedStakes.Count) currentStakeIndex = 0;

        RenderCurrentPage();
    }

    private void RenderCurrentPage()
    {
        if (sortedStakes.Count == 0) return;

        var stake = sortedStakes[currentStakeIndex];
        int tier = stake.tier;
        currentTier = tier;

        if (practiceBattleButton != null) practiceBattleButton.interactable = (tier >= 0 && tier < TIER_TO_STARTER.Length);

        // Page indicator
        if (pageIndicatorText != null)
            pageIndicatorText.text = $"Capbot {currentStakeIndex + 1} of {sortedStakes.Count}";

        // Prev/next enabled only if >1 stake (wrap-around behavior on click)
        bool hasMultiple = sortedStakes.Count > 1;
        if (prevButton != null) prevButton.interactable = hasMultiple;
        if (nextButton != null) nextButton.interactable = hasMultiple;

        // Tier label
        if (tier >= 0 && tier < TIER_NAMES.Length && tierLabelText != null)
            tierLabelText.text = $"{TIER_NAMES[tier]} {TIER_MULTIPLIERS[tier]:0.0}x multiplier";

        // Brain progress bar (for this stake)
        if (tier >= 0 && tier < TIER_FLOORS.Length)
        {
            int floor = TIER_FLOORS[tier];
            int ceiling = TIER_CEILINGS[tier];
            int span = ceiling - floor;
            float pct = span > 0 ? Mathf.Clamp01((stake.brainSteps - floor) / (float)span) : 1f;
            if (brainProgressText != null)
                brainProgressText.text = $"{stake.brainSteps / 1_000_000f:0.00}M / {ceiling / 1_000_000f:0.00}M";
            if (brainProgressBar != null)
                brainProgressBar.fillAmount = pct;
        }

        // Identity
        if (assetIdText != null) assetIdText.text = "Asset: " + Truncate(stake.assetId);
        if (walletText != null) walletText.text = "Wallet: " + Truncate(cachedData.walletAddress);

        // Preview prefab (single, original behavior)
        CreateCapbotPreview(tier);

        // Filtered battle log + upgrade log for this stake
        RenderBattleLog(stake.assetId);
        RenderUpgradeLog(stake.assetId);

        if (openExplorerButton != null)
            openExplorerButton.interactable = !string.IsNullOrEmpty(mostRecentTxSig);

        WireLogClicks();
        SetStatus("");
    }

    private void RenderBattleLog(string filterAssetId)
    {
        if (battleLogText == null) return;
        var sb = new StringBuilder();
        var battles = cachedData?.recentBattles;
        var filtered = new List<FirebaseManager.CapbotBattleEntry>();
        if (battles != null)
        {
            foreach (var b in battles)
            {
                if (b.stakedAssetId == filterAssetId) filtered.Add(b);
            }
        }

        if (filtered.Count == 0)
        {
            sb.Append("No battles yet for this Capbot.");
        }
        else
        {
            for (int i = 0; i < filtered.Count && i < 10; i++)
            {
                var b = filtered[i];
                string sign = b.payout >= 0 ? "+" : "";
                string outcome = b.playerWon ? "Won" : "Lost";
                string entry = $"{outcome} {sign}{b.payout:N0} vs {b.defeatedAi}  ({b.multiplier:0.0}×, {RelativeTime(b.timestamp)})";
                if (!string.IsNullOrEmpty(b.battleId))
                    sb.Append($"<link=\"{b.battleId}\"><color={LINK_COLOR_HEX}>{entry}</color></link>");
                else
                    sb.Append(entry);
                sb.AppendLine();
            }
        }
        battleLogText.text = sb.ToString();
        battleLogText.ForceMeshUpdate();
    }

    private void RenderUpgradeLog(string filterAssetId)
    {
        mostRecentTxSig = null;
        if (upgradeLogText == null) return;
        var sb = new StringBuilder();
        var upgrades = cachedData?.recentUpgrades;
        var filtered = new List<FirebaseManager.BrainUpgradeEntry>();
        if (upgrades != null)
        {
            foreach (var u in upgrades)
            {
                if (u.stakedAssetId == filterAssetId) filtered.Add(u);
            }
        }

        if (filtered.Count == 0)
        {
            sb.Append("No on-chain upgrades yet for this Capbot.");
        }
        else
        {
            mostRecentTxSig = filtered[0].txSignature;
            for (int i = 0; i < filtered.Count && i < 5; i++)
            {
                var u = filtered[i];
                string entry = $"{u.oldBrainSteps / 1_000_000f:0.0}M → {u.newBrainSteps / 1_000_000f:0.0}M  ({TruncateSig(u.txSignature)}, {RelativeTime(u.timestamp)})";
                if (!string.IsNullOrEmpty(u.battleId))
                    sb.Append($"<link=\"{u.battleId}\"><color={LINK_COLOR_HEX}>{entry}</color></link>");
                else
                    sb.Append(entry);
                sb.AppendLine();
            }
        }
        upgradeLogText.text = sb.ToString();
        upgradeLogText.ForceMeshUpdate();
    }

    private void OnPrevButton()
    {
        if (sortedStakes.Count == 0) return;
        currentStakeIndex = (currentStakeIndex - 1 + sortedStakes.Count) % sortedStakes.Count;
        RenderCurrentPage();
    }

    private void OnNextButton()
    {
        if (sortedStakes.Count == 0) return;
        currentStakeIndex = (currentStakeIndex + 1) % sortedStakes.Count;
        RenderCurrentPage();
    }

    private void WireLogClicks()
    {
        WireOneLog(upgradeLogText, ref upgradeLogHandler, "UpgradeLog");
        WireOneLog(battleLogText, ref battleLogHandler, "BattleLog");
    }

    private void WireOneLog(TMP_Text logText, ref TMPLinkClickHandler handler, string debugName)
    {
        if (logText == null) { Debug.LogWarning($"[CapbotTabScreen] {debugName} text not assigned"); return; }
        logText.raycastTarget = true;
        handler = logText.GetComponent<TMPLinkClickHandler>();
        if (handler == null) handler = logText.gameObject.AddComponent<TMPLinkClickHandler>();
        handler.onLinkClicked -= OnAnyLogLinkClicked;
        handler.onLinkClicked += OnAnyLogLinkClicked;
    }

    private void OnAnyLogLinkClicked(string battleId)
    {
        if (string.IsNullOrEmpty(battleId)) return;
        ReplayOverlay.Show(battleId);
    }

    private void CreateCapbotPreview(int tier)
    {
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
        if (prefabToUse == null) return;

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
        if (pageIndicatorText != null) pageIndicatorText.text = "";
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

    private void OnPracticeBattleButton()
    {
        if (currentTier < 0 || currentTier >= TIER_TO_STARTER.Length || bettingScreen == null) return;
        string starter = TIER_TO_STARTER[currentTier];
        if (FirebaseManager.Instance != null && FirebaseManager.Instance.currentPlayer != null)
            FirebaseManager.Instance.currentPlayer.currentStarter = starter;

        var sm = ScreenTransitionManager.Instance;
        if (sm != null) sm.GoTo(gameObject, bettingScreen);
        else { gameObject.SetActive(false); bettingScreen.SetActive(true); }
    }

    private void OnCloseButton()
    {
        GameObject target = walletScreen != null ? walletScreen : starterSelectionScreen;
        if (target == null) return;
        var sm = ScreenTransitionManager.Instance;
        if (sm != null) sm.GoTo(gameObject, target);
        else { gameObject.SetActive(false); target.SetActive(true); }
    }

    void OnDisable()
    {
        if (capbotPreview != null) Destroy(capbotPreview);
        capbotPreview = null;
    }
}