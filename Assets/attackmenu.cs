using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.EventSystems;
using DG.Tweening;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public class AttackMenu : MonoBehaviour
{
    public BattleCharacter player;
    public BattleCharacter ai;
    public GameObject damageEffectPrefab;
    public Canvas canvas;
    public Animator backgroundAnimator;
    public Sprite fireIcon;
    public Sprite waterIcon;
    public Sprite shieldIcon;
    public Sprite fistIcon;
    public Sprite leafIcon;
    public Button NeutralAttackButton;
    public Button UtilityButton;
    public Button HealingButton;
    public Button UltimateButton;
    public Button DefenseDownButton;
    public TMP_Text winLossCounterText;
    public TMP_Text battleResultText;

    [Header("Battle End Screen - New")]
    public TMP_Text coinChangeText;

    [Header("Tier badge (NFT-staked players only)")]
    public TMP_Text tierBadgeText;   // e.g. "Brain: King 60M · 2.8×" — shown during battle

    public Button playAgainButton;
    public GameManager gameManager;
    public Vector3 damageStartOffsetPlayer = new Vector3(0f, 0f, 0f);
    public Vector3 damageStartOffsetAI = new Vector3(0f, 0f, 0f);
    public Vector3 damageEndOffsetPlayer = new Vector3(0f, 0f, 0f);
    public Vector3 damageEndOffsetAI = new Vector3(0f, 0f, 0f);
    public string currentStarter = "Rageblaze";
    private bool isAttacking = false;
    private bool isBattleOver = false;
    private int wins = 0;
    private int losses = 0;
    private Move playerSelectedMove;
    private Move aiSelectedMove;
    private int playerTurnsLeft = 0;
    private int aiTurnsLeft = 0;
    private int[] playerCooldowns = new int[5];
    private int[] aiCooldowns = new int[5];
    private Move lastUsedMove;
    private BattleCharacter lastAttacker;
    public Button battleLogButton;
    public GameObject fullLogPanel;
    public ScrollRect fullLogScrollRect;
    public TMP_Text fullLogText;
    public RectTransform line1Rect;
    public TMP_Text line1Text;
    public CanvasGroup line1Group;
    public RectTransform line2Rect;
    public TMP_Text line2Text;
    public CanvasGroup line2Group;
    public RectTransform line3Rect;
    public TMP_Text line3Text;
    public CanvasGroup line3Group;
    public float lineHeight = 20f;
    public float fadeDuration = 0.5f;
    public float attackFadeDelay = 1f;
    public float passiveFadeDelay = 3f;
    public float turnEndDelay = 1f;
    private string currentLine1 = "";
    private string currentLine2 = "";
    private string currentLine3 = "";
    private string fullBattleLog = "";
    private Coroutine fadeCoroutine;
    private bool turnComplete = false;
    private bool isMobile = false;
    private Camera mainCamera;

    // Track whether we've subscribed to the current player/ai's events so we don't double-subscribe or leak
    private BattleCharacter subscribedPlayer = null;
    private BattleCharacter subscribedAi = null;

    // Guard so we only ever add EventTrigger touch feedback once per button
    private bool touchFeedbackInstalled = false;

    // Original button properties from inspector
    private Vector3[] originalAnchoredPositions = new Vector3[5];
    private Vector2[] originalSizeDeltas = new Vector2[5];
    private Vector3[] originalLocalScales = new Vector3[5];
    private Vector2[] originalPivots = new Vector2[5];
    private Quaternion[] originalLocalRotations = new Quaternion[5];
    private Vector2[] originalAnchorMins = new Vector2[5];
    private Vector2[] originalAnchorMaxs = new Vector2[5];

    // Store the bet amount for the current match
    private int currentBet;

    // Tier display tables — must mirror Cloud Function CONFIG.TIER_MULTIPLIERS
    // and Unity-side WalletScreen/BettingScreen/StarterSelectionScreen tables.
    private static readonly string[] TIER_NAMES = { "Evergreen", "Aquashrine", "Magmamine", "King" };
    private static readonly float[]  TIER_MULTIPLIERS = { 1.0f, 1.4f, 1.9f, 2.8f };

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool IsMobile();
    [DllImport("__Internal")]
    private static extern void Vibrate(float duration);
#endif

    void Awake()
    {
        // Capture pristine inspector button state BEFORE any layout modification can run.
        // Awake fires synchronously on SetActive(true), which is before GameManager calls
        // BindToCharacters, so originals here are always the clean inspector values.
        CaptureOriginalButtonProperties();
    }

    void Start()
    {
        Debug.Log("=== [AttackMenu] Start() - NO battle initialization here ===");

        if (TrainingMode.IsTraining) return;

#if UNITY_WEBGL && !UNITY_EDITOR
        isMobile = IsMobile();
#endif
#if UNITY_EDITOR
        isMobile = false;
#endif

        if (NeutralAttackButton != null) NeutralAttackButton.onClick.AddListener(() => SelectPlayerMove(0, fistIcon));
        if (UtilityButton != null) UtilityButton.onClick.AddListener(() => SelectPlayerMove(1, waterIcon));
        if (HealingButton != null) HealingButton.onClick.AddListener(() => SelectPlayerMove(4, shieldIcon));
        if (UltimateButton != null) UltimateButton.onClick.AddListener(() => SelectPlayerMove(3, fireIcon));
        if (DefenseDownButton != null) DefenseDownButton.onClick.AddListener(() => SelectPlayerMove(2, shieldIcon));
        if (playAgainButton != null) playAgainButton.onClick.AddListener(ResetBattle);

        if (winLossCounterText != null) winLossCounterText.text = "Wins: 0, Losses: 0";
        if (battleResultText != null) battleResultText.gameObject.SetActive(false);
        if (coinChangeText != null) coinChangeText.gameObject.SetActive(false);
        if (playAgainButton != null) playAgainButton.gameObject.SetActive(false);
        if (fullLogPanel != null) fullLogPanel.SetActive(false);
        if (battleLogButton != null) battleLogButton.onClick.AddListener(ToggleFullLog);
        if (line1Group != null) line1Group.alpha = 0;
        if (line2Group != null) line2Group.alpha = 0;
        if (line3Group != null) line3Group.alpha = 0;

        mainCamera = Camera.main;

        // One-time only: install touch feedback. EventTrigger.AddComponent cannot be
        // safely repeated — calling it multiple times stacks triggers and fires vibrations
        // multiple times per tap. This is guarded by touchFeedbackInstalled inside.
        if (isMobile) SetupMobileTouchFeedback();

        // Safety net: if characters are already assigned when Start runs (they usually are,
        // since GameManager.ResetBattleWithPlayerStarter calls BindToCharacters synchronously
        // after SetActive(true), which is before Start fires), bind now. BindToCharacters
        // itself calls ApplyMobileLayout when isMobile, so no separate layout call is needed.
        if (player != null && ai != null)
        {
            BindToCharacters();
        }
    }

    /// <summary>
    /// Called by GameManager AFTER player and ai have been (re)assigned to fresh instances.
    /// Safe to call multiple times. Calls ApplyMobileLayout at the end so EVERY battle
    /// (not just the first) gets correct mobile panel + button layout.
    /// </summary>
    public void BindToCharacters()
    {
        if (TrainingMode.IsTraining) return;
        if (player == null || ai == null)
        {
            Debug.LogWarning("[AttackMenu] BindToCharacters called but player or ai is null. Skipping.");
            return;
        }
        if (player.moves == null || player.moves.Length < 5)
        {
            Debug.LogWarning("[AttackMenu] BindToCharacters: player.moves not ready yet.");
            return;
        }

        Debug.Log($"[AttackMenu] BindToCharacters: player={player.type}, ai={ai.type}");

        UnsubscribeFromEvents();

        player.isPlayer = true;
        ai.isPlayer = false;

        player.SetupUIPanel();
        ai.SetupUIPanel();

        SubscribeToEvents();

        isBattleOver = false;
        isAttacking = false;
        playerTurnsLeft = 0;
        aiTurnsLeft = 0;
        for (int i = 0; i < playerCooldowns.Length; i++) playerCooldowns[i] = 0;
        for (int i = 0; i < aiCooldowns.Length; i++) aiCooldowns[i] = 0;

        UpdateButtonTexts();
        UpdateButtonInteractability();
        SetAttackButtonsTransparency(250f / 255f);
        UpdateStatDisplays();
        UpdateTierBadge();   // NEW: refresh staked tier badge for this battle

        // CRITICAL: reapply mobile layout for THIS battle's specific player/ai panels.
        // The old OptimizeForMobile only ran in Start(), which fires once per instance.
        // ApplyMobileLayout is per-battle idempotent: safe to call every time.
        if (isMobile) ApplyMobileLayout();

        if (battleResultText != null) battleResultText.gameObject.SetActive(false);
        if (coinChangeText != null) coinChangeText.gameObject.SetActive(false);
        if (playAgainButton != null) playAgainButton.gameObject.SetActive(false);
    }

    /// <summary>
    /// Show staked NFT tier as a small badge during battle. Hidden for non-NFT users
    /// (including guests). Identical pattern to BettingScreen / StarterSelectionScreen
    /// — the badge follows the player from screen to screen for visual continuity.
    /// </summary>
    private void UpdateTierBadge()
    {
        if (tierBadgeText == null) return;

        var p = FirebaseManager.Instance != null ? FirebaseManager.Instance.currentPlayer : null;
        if (p == null || p.stakedTier < 0)
        {
            tierBadgeText.gameObject.SetActive(false);
            return;
        }

        int tier = Mathf.Clamp(p.stakedTier, 0, TIER_NAMES.Length - 1);
        float steps = p.stakedBrainSteps / 1_000_000f;
        tierBadgeText.text = $"Brain: {TIER_NAMES[tier]} {steps:0.0}M · {TIER_MULTIPLIERS[tier]:0.0}×";
        tierBadgeText.gameObject.SetActive(true);
    }

    private void CaptureOriginalButtonProperties()
    {
        CaptureButtonProperty(NeutralAttackButton, 0);
        CaptureButtonProperty(UtilityButton, 1);
        CaptureButtonProperty(HealingButton, 2);
        CaptureButtonProperty(UltimateButton, 3);
        CaptureButtonProperty(DefenseDownButton, 4);
    }

    private void CaptureButtonProperty(Button button, int index)
    {
        if (button == null) return;
        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt == null) return;
        originalAnchoredPositions[index] = rt.anchoredPosition3D;
        originalSizeDeltas[index] = rt.sizeDelta;
        originalLocalScales[index] = rt.localScale;
        originalPivots[index] = rt.pivot;
        originalLocalRotations[index] = rt.localRotation;
        originalAnchorMins[index] = rt.anchorMin;
        originalAnchorMaxs[index] = rt.anchorMax;
    }

    /// <summary>
    /// ONE-TIME: installs EventTrigger-based touch feedback on each button.
    /// EventTrigger.AddComponent stacks components if called repeatedly, so this is
    /// guarded by touchFeedbackInstalled. Called once from Start().
    /// </summary>
    private void SetupMobileTouchFeedback()
    {
        if (touchFeedbackInstalled) return;
        touchFeedbackInstalled = true;

        AddTouchFeedback(NeutralAttackButton);
        AddTouchFeedback(UtilityButton);
        AddTouchFeedback(HealingButton);
        AddTouchFeedback(UltimateButton);
        AddTouchFeedback(DefenseDownButton);
        AddTouchFeedback(battleLogButton);
        AddTouchFeedback(playAgainButton);

        Debug.Log("[AttackMenu] Mobile touch feedback installed (one-time).");
    }

    /// <summary>
    /// PER-BATTLE idempotent: reparents the 5 player attack buttons onto THIS battle's
    /// active player panel, applies hardcoded mobile button positions, scales both
    /// the player (1.2, 1.2, 1) and AI (1, 1.2, 1) panels. Safe to call every battle.
    /// </summary>
    private void ApplyMobileLayout()
    {
        if (UIManager.Instance == null)
        {
            Debug.LogWarning("[AttackMenu] ApplyMobileLayout: UIManager.Instance is null.");
            return;
        }

        if (line1Group != null) line1Group.gameObject.SetActive(false);
        if (line2Group != null) line2Group.gameObject.SetActive(false);
        if (line3Group != null) line3Group.gameObject.SetActive(false);

        RectTransform playerPanel = UIManager.Instance.GetActivePlayerPanel();
        RectTransform aiPanel = UIManager.Instance.GetActiveAIPanel();

        if (playerPanel == null)
        {
            Debug.LogWarning("[AttackMenu] ApplyMobileLayout: no active player panel found. Panels may not be shown yet — make sure UIManager.ShowPanel ran before BindToCharacters.");
        }
        else
        {
            Debug.Log($"[AttackMenu] ApplyMobileLayout: parenting player attack buttons to '{playerPanel.name}' and scaling to (1.2, 1.2, 1).");

            NeutralAttackButton.transform.SetParent(playerPanel);
            UtilityButton.transform.SetParent(playerPanel);
            HealingButton.transform.SetParent(playerPanel);
            UltimateButton.transform.SetParent(playerPanel);
            DefenseDownButton.transform.SetParent(playerPanel);
            playerPanel.localScale = new Vector3(1.2f, 1.2f, 1f);

            SetupIgnoreLayout(NeutralAttackButton);
            SetupIgnoreLayout(UtilityButton);
            SetupIgnoreLayout(HealingButton);
            SetupIgnoreLayout(UltimateButton);
            SetupIgnoreLayout(DefenseDownButton);

            SetButtonProperties(NeutralAttackButton, new Vector3(127.1373f, 3.189163f, 2.737047f));
            SetButtonProperties(UtilityButton, new Vector3(359.7289f, 68.6389f, 2.737047f));
            SetButtonProperties(UltimateButton, new Vector3(414.4561f, 3.189209f, 2.737047f));
            SetButtonProperties(DefenseDownButton, new Vector3(74.94363f, 68.63888f, 2.737047f));
            SetButtonProperties(HealingButton, new Vector3(127.1373f, 3.189163f, 2.737047f));
        }

        if (aiPanel == null)
        {
            Debug.LogWarning("[AttackMenu] ApplyMobileLayout: no active AI panel found.");
        }
        else
        {
            Debug.Log($"[AttackMenu] ApplyMobileLayout: scaling AI panel '{aiPanel.name}' to (1, 1.2, 1).");
            aiPanel.localScale = new Vector3(1f, 1.2f, 1f);
        }
    }

    private void SetupIgnoreLayout(Button button)
    {
        if (button == null) return;
        LayoutElement le = button.gameObject.GetComponent<LayoutElement>();
        if (le == null) le = button.gameObject.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    private void SetButtonProperties(Button button, Vector3 anchoredPos)
    {
        if (button == null) return;
        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition3D = anchoredPos;
            rt.localScale = new Vector3(2.948185f, 5.067352f, 0.7370464f);
            rt.sizeDelta = new Vector2(19f, 55f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }
    }

    private void AddTouchFeedback(Button button)
    {
        if (button == null) return;
        EventTrigger trigger = button.gameObject.AddComponent<EventTrigger>();
        EventTrigger.Entry pointerDown = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        pointerDown.callback.AddListener((data) => {
#if UNITY_WEBGL && !UNITY_EDITOR
            Vibrate(50f);
#endif
        });
        trigger.triggers.Add(pointerDown);
    }

    private Vector2 WorldToCanvasLocal(Vector3 worldPos)
    {
        Camera cam = (canvas.renderMode == RenderMode.ScreenSpaceOverlay) ? null : mainCamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(mainCamera, worldPos);
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvas.GetComponent<RectTransform>(), screenPos, cam, out localPos);
        return localPos;
    }

    private void SubscribeToEvents()
    {
        if (TrainingMode.IsTraining) return;

        if (player != null && subscribedPlayer != player)
        {
            player.OnTakeDamage += OnPlayerTakeDamage;
            player.OnTakeDamage += OnPlayerTakeDamageShake;
            subscribedPlayer = player;
        }
        if (ai != null && subscribedAi != ai)
        {
            ai.OnTakeDamage += OnAITakeDamage;
            ai.OnTakeDamage += OnAITakeDamageShake;
            subscribedAi = ai;
        }
    }

    private void UnsubscribeFromEvents()
    {
        if (TrainingMode.IsTraining) return;

        if (subscribedPlayer != null)
        {
            subscribedPlayer.OnTakeDamage -= OnPlayerTakeDamage;
            subscribedPlayer.OnTakeDamage -= OnPlayerTakeDamageShake;
            subscribedPlayer = null;
        }
        if (subscribedAi != null)
        {
            subscribedAi.OnTakeDamage -= OnAITakeDamage;
            subscribedAi.OnTakeDamage -= OnAITakeDamageShake;
            subscribedAi = null;
        }
    }

    private void OnPlayerTakeDamage(int damage, string trigger, bool isCritical)
    {
        ShowDamageEffect(player, damage, trigger, isCritical);
    }

    private void OnAITakeDamage(int damage, string trigger, bool isCritical)
    {
        ShowDamageEffect(ai, damage, trigger, isCritical);
    }

    private void OnPlayerTakeDamageShake(int damage, string trigger, bool isCritical)
    {
    }

    private void OnAITakeDamageShake(int damage, string trigger, bool isCritical)
    {
    }

    private void ShowDamageEffect(BattleCharacter character, int damage, string trigger, bool isCritical)
    {
        if (damageEffectPrefab == null)
        {
            Debug.LogError("[AttackMenu] damageEffectPrefab is NULL - drag it in the Inspector!");
            return;
        }
        if (canvas == null)
        {
            Debug.LogError("[AttackMenu] canvas reference is NULL - drag the main battle Canvas in the Inspector!");
            return;
        }
        if (lastAttacker == null)
        {
            Debug.LogError("[AttackMenu] lastAttacker is NULL when ShowDamageEffect fired. Was ExecuteMove called first?");
            return;
        }

        bool isPlayerDefender = character == player;
        BattleCharacter defender = character;

        Vector3 startWorld = lastAttacker.transform.position + (lastAttacker == player ? damageStartOffsetPlayer : damageStartOffsetAI);
        Vector3 endWorld = defender.transform.position + (isPlayerDefender ? damageEndOffsetPlayer : damageEndOffsetAI);
        Vector2 startPos = WorldToCanvasLocal(startWorld);
        Vector2 endPos = WorldToCanvasLocal(endWorld);

        if (isMobile)
        {
            if (isPlayerDefender) { endPos.x += -20f; endPos.y += -10f; }
            else { endPos.x += 80f; endPos.y += -10f; }
        }

        Sprite moveIcon = GetIconForMove(lastUsedMove);
        GameObject effect = Instantiate(damageEffectPrefab, canvas.transform);

        Debug.Log($"[DamageFX] spawned -> parent='{effect.transform.parent?.name}' canvasActive={canvas.gameObject.activeInHierarchy} canvasRender={canvas.renderMode} startLocal={startPos} endLocal={endPos} startWorld={startWorld} endWorld={endWorld} sprite={(moveIcon != null ? moveIcon.name : "NULL")}");

        DamageEffect damageEffect = effect.GetComponent<DamageEffect>();
        if (damageEffect != null)
        {
            damageEffect.Initialize(character, damage, trigger, lastUsedMove, lastAttacker, this, isCritical);
            StartCoroutine(damageEffect.PlayDamageEffect(moveIcon, startPos, endPos));
        }
        else
        {
            Debug.LogError("[AttackMenu] Instantiated damageEffectPrefab has NO DamageEffect component on its root!");
            Destroy(effect);
        }
    }

    void SelectPlayerMove(int moveIndex, Sprite icon)
    {
        if (isAttacking || isBattleOver) return;
        if (player == null || player.moves == null) return;

        Move selectedMove = player.moves[moveIndex];
        if (selectedMove.pp == 0) return;
        if (playerCooldowns[moveIndex] > 0) return;

        if (fadeCoroutine != null) { StopCoroutine(fadeCoroutine); FastFadeVisibleLog(); }

        playerSelectedMove = selectedMove;
        StartCoroutine(PerformTurn(moveIndex));
        EventSystem.current.SetSelectedGameObject(null);
    }

    public void StartMatchWithBet(int betAmount)
    {
        currentBet = betAmount;
    }

    IEnumerator PerformTurn(int playerMoveIndex)
    {
        if (TrainingMode.IsTraining)
        {
            yield break;
        }

        isAttacking = true;
        SetAttackButtonsTransparency(200f / 255f);

        int aiMoveIndex = SelectAIMove();
        aiSelectedMove = ai.moves[aiMoveIndex];

        BattleCharacter first = player.speed >= ai.speed ? player : ai;
        BattleCharacter second = first == player ? ai : player;
        Move firstMove = first == player ? playerSelectedMove : aiSelectedMove;
        Move secondMove = first == player ? aiSelectedMove : playerSelectedMove;
        int firstMoveIndex = first == player ? playerMoveIndex : aiMoveIndex;
        int secondMoveIndex = first == player ? aiMoveIndex : playerMoveIndex;

        yield return new WaitForSeconds(0.5f);

        Debug.Log($"TURN START | {first.GetName()} goes first (Speed: {first.speed}) vs {second.GetName()} (Speed: {second.speed})");

        yield return StartCoroutine(ExecuteMove(first, second, firstMove, first == player ? playerTurnsLeft : aiTurnsLeft, firstMoveIndex, first == player ? playerCooldowns : aiCooldowns));
        UpdateStatDisplays();

        if (second.isDefeated)
        {
            EndBattle(second == ai ? "Win" : "Lose");
            yield break;
        }

        yield return new WaitForSeconds(1.5f);

        if (!second.isDefeated)
        {
            yield return StartCoroutine(ExecuteMove(second, first, secondMove, second == player ? playerTurnsLeft : aiTurnsLeft, secondMoveIndex, second == player ? playerCooldowns : aiCooldowns));
            UpdateStatDisplays();

            if (first.isDefeated)
            {
                EndBattle(first == ai ? "Win" : "Lose");
                yield break;
            }
        }

        yield return new WaitForSeconds(0.3f);
        turnComplete = true;
        fadeCoroutine = StartCoroutine(FadeOutVisibleLogAfterDelay(attackFadeDelay));
        yield return fadeCoroutine;

        string playerPassiveLog = player.ApplyPassive();
        string aiPassiveLog = ai.ApplyPassive();
        if (!string.IsNullOrEmpty(playerPassiveLog) || !string.IsNullOrEmpty(aiPassiveLog))
        {
            DisplayPassives(playerPassiveLog, aiPassiveLog);
            StartCoroutine(FadeOutPassivesAfterDelay(passiveFadeDelay));
        }

        UpdateButtonTexts();
        DecreaseCooldowns();

        if (!isBattleOver)
        {
            SetAttackButtonsTransparency(250f / 255f);
            UpdateButtonInteractability();
            fullBattleLog += "\n";
        }

        isAttacking = false;
    }

    private IEnumerator FadeOutVisibleLogAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (line1Group != null) line1Group.DOFade(0, fadeDuration);
        if (line2Group != null) line2Group.DOFade(0, fadeDuration);
        yield return new WaitForSeconds(fadeDuration);
        currentLine1 = "";
        currentLine2 = "";
        turnComplete = false;
    }

    private IEnumerator FadeOutPassivesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(1f);
        if (line3Group != null) line3Group.DOFade(0, fadeDuration);
        yield return new WaitForSeconds(fadeDuration);
        currentLine3 = "";
    }

    private void FastFadeVisibleLog()
    {
        if (line1Group != null) line1Group.DOFade(0, 0.1f);
        if (line2Group != null) line2Group.DOFade(0, 0.1f);
        if (line3Group != null) line3Group.DOFade(0, 0.1f);
        currentLine1 = "";
        currentLine2 = "";
        currentLine3 = "";
        turnComplete = false;
    }

    IEnumerator ExecuteMove(BattleCharacter attacker, BattleCharacter defender, Move move, int turnsLeft, int moveIndex, int[] cooldowns)
    {
        if (TrainingMode.IsTraining)
        {
            BattleUtils.DamageResult damageResult = BattleUtils.CalculateDamage(attacker, defender, move);
            int damage = damageResult.Damage;
            bool isCritical = damageResult.IsCritical;

            defender.ApplyDamage(damage);
            int dummyTurns = 0;
            BattleUtils.ApplyMoveEffect(attacker, defender, move, ref dummyTurns);
            if (move.pp > 0) move.pp--;
            yield break;
        }

        lastUsedMove = move;
        lastAttacker = attacker;

        attacker.OnAttackHit = () =>
        {
            BattleUtils.DamageResult damageResult = BattleUtils.CalculateDamage(attacker, defender, move);
            int damage = damageResult.Damage;
            bool isCritical = damageResult.IsCritical;

            Debug.Log($"{attacker.GetName()} uses {move.name} → {damage} damage {(isCritical ? "(CRITICAL!)" : "")} | {defender.GetName()} HP: {defender.currentHP - damage} → ?");

            if (move.power > 0)
            {
                defender.TakeDamage(damage, "Attacked", isCritical);
                string damageMessage = $"{attacker.GetName()} used {move.name} for {damage} damage";
                if (isCritical) damageMessage += " (critical hit!)";
                LogMessage(damageMessage);
            }

            if (move.isStatus)
            {
                string effectLog = BattleUtils.ApplyMoveEffect(attacker, defender, move, ref turnsLeft);
                if (!string.IsNullOrEmpty(effectLog))
                {
                    LogMessage(effectLog);
                }
            }
        };

        yield return attacker.PerformAttack(defender);
        yield return new WaitForSeconds(0.2f);
        attacker.OnAttackHit = null;
        if (move.pp > 0) move.pp--;
    }

    int SelectAIMove()
    {
        var brain = ai.GetComponent<AIBrain>();
        int moveIndex = brain ? brain.ChooseMove(ai, player) : Random.Range(0, 4);
        while (ai.moves[moveIndex].pp == 0 && ai.moves[moveIndex].pp != -1)
        {
            moveIndex = Random.Range(0, 4);
        }
        return moveIndex;
    }

    void UpdateStatusEffects()
    {
        string playerPassiveLog = player.ApplyPassive();
        if (!string.IsNullOrEmpty(playerPassiveLog)) LogMessage(playerPassiveLog);

        string aiPassiveLog = ai.ApplyPassive();
        if (!string.IsNullOrEmpty(aiPassiveLog)) LogMessage(aiPassiveLog);

        UpdateStatDisplays();
    }

    void UpdateStatDisplays()
    {
        if (player != null) player.UpdateStatTexts();
        if (ai != null) ai.UpdateStatTexts();
    }

    void DecreaseCooldowns()
    {
        for (int i = 0; i < playerCooldowns.Length; i++) if (playerCooldowns[i] > 0) playerCooldowns[i]--;
        for (int i = 0; i < aiCooldowns.Length; i++) if (aiCooldowns[i] > 0) aiCooldowns[i]--;
    }

    void UpdateButtonTexts()
    {
        if (player == null || player.moves == null || player.moves.Length < 5)
        {
            Debug.LogWarning("[AttackMenu] UpdateButtonTexts skipped: player/moves not ready.");
            return;
        }

        if (NeutralAttackButton != null)
        {
            var t = NeutralAttackButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = player.moves[0].name;
        }
        if (UtilityButton != null)
        {
            var t = UtilityButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = player.moves[1].name;
        }
        if (HealingButton != null)
        {
            var t = HealingButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = player.moves[4].name;
        }
        if (UltimateButton != null)
        {
            var t = UltimateButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = player.moves[3].name;
        }
        if (DefenseDownButton != null)
        {
            var t = DefenseDownButton.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = player.moves[2].name;
        }

        Debug.Log($"[AttackMenu] UpdateButtonTexts applied for {player.type}: [{player.moves[0].name}, {player.moves[1].name}, {player.moves[2].name}, {player.moves[3].name}, {player.moves[4].name}]");
    }

    void UpdateButtonInteractability()
    {
        if (player == null || player.moves == null || player.moves.Length < 5) return;
        if (NeutralAttackButton != null) NeutralAttackButton.interactable = true;
        if (UtilityButton != null) UtilityButton.interactable = true;
        if (HealingButton != null) HealingButton.interactable = true;
        if (UltimateButton != null) UltimateButton.interactable = player.moves[3].pp != 0;
        if (DefenseDownButton != null) DefenseDownButton.interactable = true;
    }

    void SetAttackButtonsTransparency(float alpha)
    {
        SetButtonTransparency(NeutralAttackButton, alpha);
        SetButtonTransparency(UtilityButton, alpha);
        SetButtonTransparency(HealingButton, alpha);
        SetButtonTransparency(UltimateButton, alpha);
        SetButtonTransparency(DefenseDownButton, alpha);
    }

    private void SetButtonTransparency(Button button, float alpha)
    {
        if (button == null) return;
        Image image = button.GetComponent<Image>();
        if (image != null)
        {
            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }
    }

    void EndBattle(string result)
    {
        if (TrainingMode.IsTraining) return;

        isBattleOver = true;

        bool playerWon = (result == "Win");

        var playerData = FirebaseManager.Instance.currentPlayer;
        if (playerData == null)
        {
            Debug.LogError("[AttackMenu] EndBattle: currentPlayer is null!");
            return;
        }

        // Compute payout including tier multiplier (mirrors server-side resolveMatch math).
        // Client-local computation is safe because both client and server read stakedTier
        // from the same Firestore doc. If they ever differ briefly (mid-link), the next
        // battle resyncs. Server is authoritative — this is just for the optimistic UI.
        float multiplier = 1.0f;
        int tierForDisplay = -1;
        if (playerData.stakedTier >= 0 && playerData.stakedTier < TIER_MULTIPLIERS.Length)
        {
            tierForDisplay = playerData.stakedTier;
            multiplier = TIER_MULTIPLIERS[tierForDisplay];
        }
        int winnings = Mathf.FloorToInt(currentBet * multiplier);
        int coinDelta = playerWon ? winnings : -currentBet;

        // Determine which AI was defeated (used by Cloud Function to apply pool logic)
        string defeatedAiName = "Rageblaze"; // default fallback
        if (ai != null)
        {
            switch (ai.type)
            {
                case ElementType.Fire:  defeatedAiName = "Rageblaze"; break;
                case ElementType.Water: defeatedAiName = "Tsunami";   break;
                case ElementType.Grass: defeatedAiName = "Healspike"; break;
            }
        }

        string usedStarterName = playerData.currentStarter; // "Rageblaze" / "Tsunami" / "Healspike"

        // Update local UI counters
        if (playerWon) wins++;
        else losses++;
        winLossCounterText.text = $"Wins: {wins}, Losses: {losses}";

        // Show end-screen UI immediately (optimistic UI)
        if (battleResultText != null)
        {
            battleResultText.text = playerWon ? "You Win!" : "You Lose!";
            battleResultText.gameObject.SetActive(true);
        }
        if (coinChangeText != null)
        {
            // Format payout: include tier name + multiplier on wins for staked users
            // ("+2,800 (2.8× King)"). Plain "+1,000 coins" / "-1,000 coins" otherwise.
            if (playerWon && tierForDisplay >= 0)
            {
                coinChangeText.text = $"+{coinDelta:N0} ({multiplier:0.0}× {TIER_NAMES[tierForDisplay]})";
            }
            else if (playerWon)
            {
                coinChangeText.text = $"+{coinDelta:N0} coins";
            }
            else
            {
                coinChangeText.text = $"{coinDelta:N0} coins";   // negative sign included naturally
            }
            coinChangeText.color = playerWon ? Color.green : Color.red;
            coinChangeText.gameObject.SetActive(true);
        }
        if (playAgainButton != null) playAgainButton.gameObject.SetActive(true);

        Debug.Log($"=== BATTLE OVER === {result} | Bet: {currentBet} | Mult: {multiplier:0.0}× | Payout: {coinDelta} | Starter: {usedStarterName} | DefeatedAI: {defeatedAiName}");

        // Resolve match server-side. Cloud Function authoritatively updates:
        // - starter coin balance
        // - AI pool balance
        // - totalWon
        // - rank
        // For guest/editor, FirebaseManager applies result locally.
        FirebaseManager.Instance.ResolveMatch(
            betAmount: currentBet,
            playerWon: playerWon,
            usedStarter: usedStarterName,
            defeatedAi: defeatedAiName,
            onSuccess: (updatedPlayer) =>
            {
                Debug.Log("✅ [AttackMenu] Match resolved by Cloud Function");
            },
            onError: (err) =>
            {
                Debug.LogError("❌ [AttackMenu] ResolveMatch failed: " + err);
            }
        );
    }

    void ResetBattle()
    {
        UnsubscribeFromEvents();

        // Move buttons off the panel they were on — the panel is about to be hidden by
        // GameManager.ResetBattle. Keep them on a stable parent until the next battle,
        // when BindToCharacters → ApplyMobileLayout will reparent them to the new player panel.
        NeutralAttackButton.transform.SetParent(transform, false);
        UtilityButton.transform.SetParent(transform, false);
        HealingButton.transform.SetParent(transform, false);
        UltimateButton.transform.SetParent(transform, false);
        DefenseDownButton.transform.SetParent(transform, false);

        RemoveIgnoreLayout(NeutralAttackButton);
        RemoveIgnoreLayout(UtilityButton);
        RemoveIgnoreLayout(HealingButton);
        RemoveIgnoreLayout(UltimateButton);
        RemoveIgnoreLayout(DefenseDownButton);

        ResetButtonProperties(NeutralAttackButton, 0);
        ResetButtonProperties(UtilityButton, 1);
        ResetButtonProperties(HealingButton, 2);
        ResetButtonProperties(UltimateButton, 3);
        ResetButtonProperties(DefenseDownButton, 4);

        // GameManager.InitializeBattle() will clean up and return to the starter selection
        // screen. When the user later confirms a new bet, GameManager.ResetBattleWithPlayerStarter
        // will call BindToCharacters → ApplyMobileLayout, which reparents the buttons to the
        // correct new player panel and applies mobile scaling.
        //
        // We do NOT call OptimizeForMobile / ApplyMobileLayout here. At this point
        // GameManager.ResetBattle has already hidden all panels via uiManager.HideAllPanels(),
        // so GetActivePlayerPanel() and GetActiveAIPanel() would both return null and the
        // call would silently no-op — exactly the bug we're fixing.
        gameManager.InitializeBattle();

        battleResultText.gameObject.SetActive(false);
        if (coinChangeText != null) coinChangeText.gameObject.SetActive(false);
        playAgainButton.gameObject.SetActive(false);
        fullBattleLog = "";
        fullLogText.text = "";
        currentLine1 = "";
        currentLine2 = "";
        currentLine3 = "";
        line1Group.alpha = 0;
        line2Group.alpha = 0;
        line3Group.alpha = 0;
        fullLogPanel.SetActive(false);
    }

    private void RemoveIgnoreLayout(Button button)
    {
        if (button == null) return;
        LayoutElement le = button.gameObject.GetComponent<LayoutElement>();
        if (le != null) Destroy(le);
    }

    private void ResetButtonProperties(Button button, int index)
    {
        if (button == null) return;
        RectTransform rt = button.GetComponent<RectTransform>();
        if (rt == null) return;
        rt.anchorMin = originalAnchorMins[index];
        rt.anchorMax = originalAnchorMaxs[index];
        rt.anchoredPosition3D = originalAnchoredPositions[index];
        rt.sizeDelta = originalSizeDeltas[index];
        rt.localScale = originalLocalScales[index];
        rt.pivot = originalPivots[index];
        rt.localRotation = originalLocalRotations[index];
    }

    public void LogMessage(string message)
    {
        if (TrainingMode.IsTraining) return;

        fullBattleLog += message + "\n";
        fullLogText.text = fullBattleLog;
        ApplyGradientToText(fullLogText, player.GetName(), GetNameGradient(player.type));
        ApplyGradientToText(fullLogText, ai.GetName(), GetNameGradient(ai.type));
        StartCoroutine(ScrollToBottom());

        if (string.IsNullOrEmpty(currentLine1))
        {
            currentLine1 = message;
            line1Text.text = currentLine1;
            line1Group.alpha = 0;
            line1Group.DOFade(1, fadeDuration);
        }
        else if (string.IsNullOrEmpty(currentLine2))
        {
            currentLine2 = message;
            line2Text.text = currentLine2;
            line2Group.alpha = 0;
            line2Group.DOFade(1, fadeDuration);
        }
        else
        {
            StartCoroutine(ShiftAndAdd(message));
        }
    }

    private void DisplayPassives(string playerLog, string aiLog)
    {
        string passiveMessage = "";
        if (!string.IsNullOrEmpty(playerLog)) passiveMessage += playerLog + "\n\n";
        if (!string.IsNullOrEmpty(aiLog)) passiveMessage += aiLog;
        if (passiveMessage.EndsWith("\n\n")) passiveMessage = passiveMessage.Substring(0, passiveMessage.Length - 2);

        currentLine3 = passiveMessage;
        line3Text.text = currentLine3;
        line3Group.alpha = 0;

        Sequence passiveSeq = DOTween.Sequence();
        passiveSeq.Append(line3Group.DOFade(1, fadeDuration));
        passiveSeq.AppendInterval(passiveFadeDelay);
        passiveSeq.Append(line3Group.DOFade(0, fadeDuration));
        passiveSeq.OnComplete(() => currentLine3 = "");

        string fullPassiveMessage = "";
        if (!string.IsNullOrEmpty(playerLog)) fullPassiveMessage += playerLog + "\n";
        if (!string.IsNullOrEmpty(aiLog)) fullPassiveMessage += aiLog;
        if (fullPassiveMessage.EndsWith("\n")) fullPassiveMessage = fullPassiveMessage.Substring(0, fullPassiveMessage.Length - 1);

        fullBattleLog += fullPassiveMessage + "\n";
        fullLogText.text = fullBattleLog;
        ApplyGradientToText(fullLogText, player.GetName(), GetNameGradient(player.type));
        ApplyGradientToText(fullLogText, ai.GetName(), GetNameGradient(ai.type));
        StartCoroutine(ScrollToBottom());
    }

    private VertexGradient GetNameGradient(ElementType type)
    {
        switch (type)
        {
            case ElementType.Fire:
                return new VertexGradient(new Color(1f, 0.5f, 0f), new Color(1f, 0.5f, 0f), new Color(1f, 0.8f, 0f), new Color(1f, 0.8f, 0f));
            case ElementType.Water:
                return new VertexGradient(new Color(0f, 0f, 0.6f), new Color(0f, 0f, 0.6f), new Color(0.2f, 0.6f, 1f), new Color(0.2f, 0.6f, 1f));
            case ElementType.Grass:
                return new VertexGradient(new Color(0f, 0.4f, 0f), new Color(0f, 0.4f, 0f), new Color(0.2f, 0.8f, 0.2f), new Color(0.2f, 0.8f, 0.2f));
            default:
                return new VertexGradient(Color.white);
        }
    }

    private void ApplyGradientToText(TMP_Text textComponent, string name, VertexGradient gradient)
    {
        if (string.IsNullOrEmpty(name)) return;
        textComponent.ForceMeshUpdate();
        TMP_TextInfo textInfo = textComponent.textInfo;
        if (textInfo.characterInfo == null || textInfo.characterInfo.Length == 0) return;

        Color32 topLeft = gradient.topLeft;
        Color32 topRight = gradient.topRight;
        Color32 bottomLeft = gradient.bottomLeft;
        Color32 bottomRight = gradient.bottomRight;

        string text = textComponent.text;
        int index = 0;
        while ((index = text.IndexOf(name, index)) != -1)
        {
            for (int i = 0; i < name.Length; i++)
            {
                int charIndex = index + i;
                if (charIndex >= textInfo.characterCount) break;
                TMP_CharacterInfo charInfo = textInfo.characterInfo[charIndex];
                if (!charInfo.isVisible) continue;
                int materialIndex = charInfo.materialReferenceIndex;
                int vertexIndex = charInfo.vertexIndex;
                Color32[] vertexColors = textInfo.meshInfo[materialIndex].colors32;
                vertexColors[vertexIndex + 0] = bottomLeft;
                vertexColors[vertexIndex + 1] = topLeft;
                vertexColors[vertexIndex + 2] = topRight;
                vertexColors[vertexIndex + 3] = bottomRight;
            }
            index += name.Length;
        }
        textComponent.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private IEnumerator ScrollToBottom()
    {
        Canvas.ForceUpdateCanvases();
        fullLogText.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(fullLogText.rectTransform);
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        if (fullLogScrollRect != null) fullLogScrollRect.verticalNormalizedPosition = 0f;
    }

    private IEnumerator ShiftAndAdd(string newMessage)
    {
        line1Group.DOFade(0, fadeDuration);
        line2Group.DOFade(0, fadeDuration);
        yield return new WaitForSeconds(fadeDuration);
        currentLine1 = currentLine2;
        line1Text.text = currentLine1;
        currentLine2 = newMessage;
        line2Text.text = currentLine2;
        line1Group.DOFade(1, fadeDuration);
        line2Group.DOFade(1, fadeDuration);
    }

    private void ToggleFullLog()
    {
        fullLogPanel.SetActive(!fullLogPanel.activeSelf);
        if (fullLogPanel.activeSelf) StartCoroutine(ScrollToBottom());
    }

    public void TriggerBackgroundShake()
    {
        if (backgroundAnimator != null) backgroundAnimator.SetTrigger("Shake");
    }

    private Sprite GetIconForMove(Move move)
    {
        if (move == null) return fistIcon;
        if (move.isStatus) return shieldIcon;
        switch (move.type)
        {
            case ElementType.Fire: return fireIcon;
            case ElementType.Water: return waterIcon;
            case ElementType.Grass: return leafIcon;
            case ElementType.Normal: return fistIcon;
            default: return fistIcon;
        }
    }

    public Vector3 GetBurstOffset(ElementType type, string direction)
    {
        if (TrainingMode.IsTraining) return Vector3.zero;

        switch (type)
        {
            case ElementType.Fire:
                return direction == "AI" ? new Vector3(-200f, 30f, 0f) : new Vector3(200f, 30f, 0f);
            case ElementType.Water:
                return direction == "AI" ? new Vector3(-200f, 50f, 0f) : new Vector3(200f, 50f, 0f);
            case ElementType.Grass:
                return direction == "AI" ? new Vector3(-185f, 50f, 0f) : new Vector3(200f, 50f, 0f);
        }
        return Vector3.zero;
    }

    void OnDestroy()
    {
        UnsubscribeFromEvents();
    }
}