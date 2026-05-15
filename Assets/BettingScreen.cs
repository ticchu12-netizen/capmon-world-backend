using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class BettingScreen : MonoBehaviour
{
    [Header("Prefabs - Drag your character prefabs here")]
    public GameObject firePrefab;
    public GameObject waterPrefab;
    public GameObject grassPrefab;

    [Header("UI References")]
    public TMP_Text currentStarterText;
    public TMP_Text playerCoinsText;

    public TMP_Text aiRageblazeText;
    public TMP_Text aiTsunamiText;
    public TMP_Text aiHealspikeText;

    public Slider betSlider;
    public TMP_Text betValueText;
    public Button confirmButton;
    public Button backButton;
    public TMP_Text statusText;
    [SerializeField] private GameObject battleBackground;

    [Header("Tier payout preview (NFT-staked players only)")]
    public TMP_Text payoutPreviewText;

    [Header("Canvas")]
    public Canvas bettingCanvas;

    [Header("Navigation")]
    public GameObject starterSelectionScreen;

    private GameObject playerPreview;
    private GameObject aiRagePreview;
    private GameObject aiTsunamiPreview;
    private GameObject aiHealspikePreview;

    // Captured ONCE in Awake. Same drift bug as LoginScreen used to have:
    // re-capturing in OnEnable meant each revisit added +90 on top of the
    // already-offset position (mobile only).
    private Vector2 originalBetSliderPos;
    private Vector2 originalBetValuePos;
    private Vector2 originalConfirmPos;
    private bool originalsCaptured = false;

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

        CaptureOriginalsIfNeeded();
    }

    private void CaptureOriginalsIfNeeded()
    {
        if (originalsCaptured) return;

        if (betSlider != null)
            originalBetSliderPos = betSlider.GetComponent<RectTransform>().anchoredPosition;
        if (betValueText != null)
            originalBetValuePos = betValueText.GetComponent<RectTransform>().anchoredPosition;
        if (confirmButton != null)
            originalConfirmPos = confirmButton.GetComponent<RectTransform>().anchoredPosition;

        originalsCaptured = true;
    }

    void OnEnable()
    {
        // Defensive: in case Awake didn't fire (screen instantiated late)
        CaptureOriginalsIfNeeded();

        if (battleBackground != null)
        {
            battleBackground.SetActive(false);
        }
        betSlider.minValue = 1000f;
        betSlider.maxValue = 10000f;

        // RemoveAllListeners before AddListener to prevent stacking on revisits
        betSlider.onValueChanged.RemoveAllListeners();
        betSlider.onValueChanged.AddListener(OnBetValueChanged);

        confirmButton.onClick.RemoveAllListeners();
        confirmButton.onClick.AddListener(OnConfirmBet);

        if (backButton != null)
        {
            backButton.onClick.RemoveAllListeners();
            backButton.onClick.AddListener(OnBackButton);
        }

        TMP_Text buttonText = confirmButton.GetComponentInChildren<TMP_Text>();
        if (buttonText != null && string.IsNullOrEmpty(buttonText.text))
            buttonText.text = "Confirm";

        confirmButton.interactable = true;
        betSlider.interactable = true;
        statusText.text = "";

        CreateLivePreviews();
        UpdateUI();

        ApplyChildScaling();

        // Mobile-only offset, applied against the captured-once originals so
        // there's no drift across revisits.
        if (isMobile)
        {
            if (betSlider != null)
                betSlider.GetComponent<RectTransform>().anchoredPosition = originalBetSliderPos + new Vector2(0, 90f);
            if (betValueText != null)
                betValueText.GetComponent<RectTransform>().anchoredPosition = originalBetValuePos + new Vector2(0, 90f);
            if (confirmButton != null)
                confirmButton.GetComponent<RectTransform>().anchoredPosition = originalConfirmPos + new Vector2(0, 90f);

            Debug.Log("=== [BettingScreen] Mobile-specific UI moved up 90px ===");
        }
        else
        {
            // Desktop: ensure positions match the original (in case a previous
            // OnEnable in mobile mode set them; cheap to be defensive).
            if (betSlider != null)
                betSlider.GetComponent<RectTransform>().anchoredPosition = originalBetSliderPos;
            if (betValueText != null)
                betValueText.GetComponent<RectTransform>().anchoredPosition = originalBetValuePos;
            if (confirmButton != null)
                confirmButton.GetComponent<RectTransform>().anchoredPosition = originalConfirmPos;
        }
    }

    private void ApplyChildScaling()
    {
        if (bettingCanvas == null)
        {
            Debug.LogWarning("[BettingScreen] bettingCanvas not assigned - cannot apply scaling.");
            return;
        }
        if (childScalingApplied) return;

        Transform canvasTf = bettingCanvas.transform;
        originalChildScales.Clear();

        for (int i = 0; i < canvasTf.childCount; i++)
        {
            Transform child = canvasTf.GetChild(i);
            originalChildScales[child] = child.localScale;
            child.localScale = child.localScale * 1f;
        }

        childScalingApplied = true;
        Debug.Log($"[BettingScreen] Scaled {originalChildScales.Count} canvas children to 3x.");
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

    private void CreateLivePreviews()
    {
        var player = FirebaseManager.Instance.currentPlayer;
        if (player == null) return;

        CleanupPreviews();

        GameObject playerPrefab = GetPrefabForStarter(player.currentStarter);
        if (playerPrefab != null && currentStarterText != null)
        {
            playerPreview = Instantiate(playerPrefab, currentStarterText.transform);

            Vector3 playerPosition = Vector3.zero;
            Vector3 playerScale = Vector3.one;
            bool flipHorizontal = false;

            switch (player.currentStarter)
            {
                case "Rageblaze":
                    playerPosition = new Vector3(-75f, -375f, 0f);
                    playerScale = new Vector3(80f, 80f, 1f);
                    flipHorizontal = true;
                    break;
                case "Tsunami":
                    playerPosition = new Vector3(-40f, -325f, 0f);
                    playerScale = new Vector3(80f, 80f, 1f);
                    flipHorizontal = false;
                    break;
                case "Healspike":
                    playerPosition = new Vector3(-25f, -380f, 0f);
                    playerScale = new Vector3(32f, 32f, 1f);
                    flipHorizontal = true;
                    break;
            }

            if (flipHorizontal)
                playerScale.x = -playerScale.x;

            playerPreview.transform.localPosition = playerPosition;
            playerPreview.transform.localScale = playerScale;

            Canvas previewCanvas = playerPreview.AddComponent<Canvas>();
            previewCanvas.renderMode = RenderMode.WorldSpace;
            previewCanvas.sortingOrder = 15;

            DisableBehaviours(playerPreview);
        }

        if (firePrefab != null && aiRageblazeText != null)
        {
            aiRagePreview = Instantiate(firePrefab, aiRageblazeText.transform);
            aiRagePreview.transform.localPosition = new Vector3(-185f, -70f, 0f);
            aiRagePreview.transform.localScale = new Vector3(50f, 50f, 1f);

            Canvas c1 = aiRagePreview.AddComponent<Canvas>();
            c1.renderMode = RenderMode.WorldSpace;
            c1.sortingOrder = 12;
            DisableBehaviours(aiRagePreview);
        }

        if (waterPrefab != null && aiTsunamiText != null)
        {
            aiTsunamiPreview = Instantiate(waterPrefab, aiTsunamiText.transform);
            aiTsunamiPreview.transform.localPosition = new Vector3(-200f, -35f, 0f);
            aiTsunamiPreview.transform.localScale = new Vector3(-48f, 48f, 1f);

            Canvas c2 = aiTsunamiPreview.AddComponent<Canvas>();
            c2.renderMode = RenderMode.WorldSpace;
            c2.sortingOrder = 12;
            DisableBehaviours(aiTsunamiPreview);
        }

        if (grassPrefab != null && aiHealspikeText != null)
        {
            aiHealspikePreview = Instantiate(grassPrefab, aiHealspikeText.transform);
            aiHealspikePreview.transform.localPosition = new Vector3(-200f, -90f, 0f);
            aiHealspikePreview.transform.localScale = new Vector3(20f, 20f, 1f);

            Canvas c3 = aiHealspikePreview.AddComponent<Canvas>();
            c3.renderMode = RenderMode.WorldSpace;
            c3.sortingOrder = 12;
            DisableBehaviours(aiHealspikePreview);
        }
    }

    private GameObject GetPrefabForStarter(string starterName)
    {
        return starterName switch
        {
            "Rageblaze" => firePrefab,
            "Tsunami" => waterPrefab,
            "Healspike" => grassPrefab,
            _ => firePrefab
        };
    }

    private void DisableBehaviours(GameObject preview)
    {
        MonoBehaviour[] behaviours = preview.GetComponentsInChildren<MonoBehaviour>();
        foreach (var b in behaviours) b.enabled = false;
    }

    private void CleanupPreviews()
    {
        if (playerPreview != null) { DOTween.Kill(playerPreview, true); Destroy(playerPreview); }
        if (aiRagePreview != null) { DOTween.Kill(aiRagePreview, true); Destroy(aiRagePreview); }
        if (aiTsunamiPreview != null) { DOTween.Kill(aiTsunamiPreview, true); Destroy(aiTsunamiPreview); }
        if (aiHealspikePreview != null) { DOTween.Kill(aiHealspikePreview, true); Destroy(aiHealspikePreview); }
    }

    public void UpdateUI()
    {
        var player = FirebaseManager.Instance.currentPlayer;
        if (player == null)
        {
            statusText.text = "Waiting for player data...";
            return;
        }

        currentStarterText.text = $"Current Starter: {player.currentStarter}";
        playerCoinsText.text = player.GetStarterCoins(player.currentStarter).ToString("N0");

        aiRageblazeText.text = $"Rageblaze AI {player.aiRageblazeCoins:N0}";
        aiTsunamiText.text = $"Tsunami AI {player.aiTsunamiCoins:N0}";
        aiHealspikeText.text = $"Healspike AI {player.aiHealspikeCoins:N0}";

        betValueText.text = ((int)betSlider.value).ToString("N0");

        UpdatePayoutPreview();
    }

    private void OnBetValueChanged(float value)
    {
        betValueText.text = ((int)value).ToString("N0");
        UpdatePayoutPreview();
    }

    private void UpdatePayoutPreview()
    {
        if (payoutPreviewText == null) return;

        var player = FirebaseManager.Instance.currentPlayer;
        if (player == null || player.stakedTier < 0)
        {
            payoutPreviewText.gameObject.SetActive(false);
            return;
        }

        int tier = Mathf.Clamp(player.stakedTier, 0, TIER_NAMES.Length - 1);
        int bet = (int)betSlider.value;
        float mult = TIER_MULTIPLIERS[tier];
        int winnings = Mathf.FloorToInt(bet * mult);

        payoutPreviewText.text = $"Win: {winnings:N0} ({bet:N0} x {mult:0.0}x {TIER_NAMES[tier]})";
        payoutPreviewText.gameObject.SetActive(true);
    }

    private void OnConfirmBet()
    {
        int bet = (int)betSlider.value;

        confirmButton.interactable = false;
        betSlider.interactable = false;
        statusText.text = "Starting match...";

        var sm = ScreenTransitionManager.Instance;
        if (sm != null) sm.GoToWithAction(() => EnterBattle(bet));
        else EnterBattle(bet);
    }

    private void EnterBattle(int bet)
    {
        AttackMenu attackMenu = FindFirstObjectByType<AttackMenu>(FindObjectsInactive.Include);
        if (attackMenu != null)
        {
            attackMenu.StartMatchWithBet(bet);
        }

        GameManager gm = FindFirstObjectByType<GameManager>();
        if (gm != null && FirebaseManager.Instance.currentPlayer != null)
        {
            gm.StartBattleWithSelectedStarter(FirebaseManager.Instance.currentPlayer.currentStarter);
            gameObject.SetActive(false);
            if (battleBackground != null) battleBackground.SetActive(true);
        }
        else
        {
            statusText.text = "Error: Could not start match";
            confirmButton.interactable = true;
            betSlider.interactable = true;
        }
    }

    private void OnBackButton()
    {
        Debug.Log("=== [BettingScreen] Back pressed — returning to StarterSelectionScreen ===");

        if (starterSelectionScreen == null)
        {
            Debug.LogError("[BettingScreen] starterSelectionScreen not assigned!");
            return;
        }

        var sm = ScreenTransitionManager.Instance;
        if (sm != null) sm.GoTo(gameObject, starterSelectionScreen);
        else
        {
            gameObject.SetActive(false);
            starterSelectionScreen.SetActive(true);
        }
    }

    void OnDisable()
    {
        RestoreChildScaling();
        CleanupPreviews();
    }
}