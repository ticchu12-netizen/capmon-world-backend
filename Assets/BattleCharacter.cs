using UnityEngine;
using UnityEngine.UI;
using Unity.InferenceEngine;
using TMPro;
using System;
using System.Collections;
using DG.Tweening;
using System.Runtime.InteropServices;

public enum ElementType { Fire, Water, Grass, Normal }
[System.Serializable]
public class Move
{
    public string name;
    public ElementType type;
    public int power;
    public bool isStatus;
    public int pp;
    public string description;
}
public class BattleCharacter : MonoBehaviour
{
    public ElementType type;
    public int maxHP;
    public int currentHP;
    public int attack;
    public int defense;
    public int baseDefense;
    public int speed;
    public int baseSpeed;
    public int defenseStage = 0;
    public Move[] moves;
    public Animator animator;
    public float speedDebuffPercentage = 0f;
    public float defenseDebuffPercentage = 0f;
    public Action OnAttackHit;
    public event Action<int, string, bool> OnTakeDamage;
    [HideInInspector] public bool isDefeated = false;
    private Image healthBarImage;
    private TMP_Text hpText;
    private TMP_Text speedText;
    private TMP_Text defenseText;
    public ParticleSystem burstAI;
    public ParticleSystem burstPlayer;
    public ParticleSystem neutralburstAI;
    public ParticleSystem neutralburstPlayer;
    public ParticleSystem defenceburstai;
    public ParticleSystem defenceburstplayer;
    public Vector3 neutralBurstOffsetAI = Vector3.zero;
    public Vector3 neutralBurstOffsetPlayer = Vector3.zero;
    public Vector3 defenceBurstOffsetAI = Vector3.zero;
    public Vector3 defenceBurstOffsetPlayer = Vector3.zero;

    // ============================================================
    // MOBILE-ONLY DAMAGE ICON ENDPOINT OFFSETS
    // ------------------------------------------------------------
    // The damage icon (sprite + number) lands at a position computed
    // by AttackMenu / DamageEffect, but on mobile it doesn't quite
    // line up with the elemental particle burst. These offsets are
    // ADDED to the icon's end position ONLY when on mobile.
    // Per-type (Fire/Water/Grass) and per-side (AI/Player) so each
    // character can be tuned independently.
    // X/Y are typically (0, -20) per the requested 20px lower drop;
    // exposed as full Vector3 in case you need fine X tuning later.
    // ============================================================
    [Header("Mobile-only Damage Icon Endpoint Offsets")]
    public Vector3 fireDamageIconEndOffsetAI_Mobile     = new Vector3(0f, -500f, 0f);
    public Vector3 fireDamageIconEndOffsetPlayer_Mobile = new Vector3(0f, -500f, 0f);
    public Vector3 waterDamageIconEndOffsetAI_Mobile     = new Vector3(0f, -500f, 0f);
    public Vector3 waterDamageIconEndOffsetPlayer_Mobile = new Vector3(0f, -500f, 0f);
    public Vector3 grassDamageIconEndOffsetAI_Mobile     = new Vector3(0f, -500f, 0f);
    public Vector3 grassDamageIconEndOffsetPlayer_Mobile = new Vector3(0f, -500f, 0f);

    private Tween healthBarTween; // To manage the health bar animation
    public float animationStartDelay = 0f;
    public float forwardMoveTime = 0.10f;
    public float hitDelayAfterForward = 0.25f;
    public float backDelayAfterHit = 0.25f;
    public float backMoveTime = 0.10f;
    public float forwardMoveDelay = 0f;
    public AudioClip attackedsound;
    public AudioClip defensesound;
    public AudioSource audioSource;
    public float attackedSoundVolume = 1f;
    public float defenseSoundVolume = 1f;
    public int intimidateCount = 0;
    private Tween colorTweenHp;
    private Tween colorTweenSpeed;
    private Tween colorTweenDefense;
    public GameObject greenArrowPrefab;
    public GameObject redArrowPrefab;
    public Vector2 greenHPOffset;
    public Vector2 redHPOffset;
    public Vector2 greenSpeedOffset;
    public Vector2 redSpeedOffset;
    public Vector2 greenDefenseOffset;
    public Vector2 redDefenseOffset;
    private ParticleSystem greenArrowHP;
    private ParticleSystem redArrowHP;
    private ParticleSystem greenArrowSpeed;
    private ParticleSystem redArrowSpeed;
    private ParticleSystem greenArrowDefense;
    private ParticleSystem redArrowDefense;
    private RectTransform greenHP_RT;
    private RectTransform redHP_RT;
    private RectTransform greenSpeed_RT;
    private RectTransform redSpeed_RT;
    private RectTransform greenDefense_RT;
    private RectTransform redDefense_RT;
    private float greenHP_relX;
    private float redHP_relX;
    private float greenSpeed_relX;
    private float redSpeed_relX;
    private float greenDefense_relX;
    private float redDefense_relX;

    [HideInInspector] public bool isPlayer;

    [SerializeField] public ModelAsset[] aiBrainModels; // Drag and drop .onnx/.sentis model assets here for AI characters

    private bool isMobile = false;  // Runtime mobile detection

    /// <summary>
    /// Public read-only accessor for the mobile detection result.
    /// DamageEffect uses this to decide whether to apply the mobile
    /// damage-icon endpoint offsets above.
    /// </summary>
    public bool IsMobile => isMobile;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal", EntryPoint = "IsMobile")]
    private static extern bool IsMobileExtern();
#endif

    // For AIBrain opponent modeling (tracks last move for prediction)
    [HideInInspector] public Move lastUsedMove;

    void Awake()
    {
        animator = GetComponent<Animator>();
        SetTypeSpecificAttributes();
        InitializeMoves();

        // Detect if mobile
#if UNITY_WEBGL && !UNITY_EDITOR
        isMobile = IsMobileExtern();
#endif
#if UNITY_EDITOR
        isMobile = false;  // Or true for testing in Editor
#endif

        SetOffsets();
        SetBurstOffsets();

        // Assign brain models to AIBrain if this is an AI character
        if (!isPlayer)
        {
            AIBrain aiBrain = GetComponent<AIBrain>();
            if (aiBrain != null && aiBrainModels != null && aiBrainModels.Length > 0)
            {
                aiBrain.SetBrainModels(aiBrainModels);
            }
        }
    }

    private void SetBurstOffsets()
    {
        switch (type)
        {
            case ElementType.Fire:
                defenceBurstOffsetAI = new Vector3(-50f, 140f, 0f); // Example: Start on top for AI side
                defenceBurstOffsetPlayer = new Vector3(10f, 160f, 0f); // Example: Start on top for player side
                break;
            case ElementType.Water:
                defenceBurstOffsetAI = new Vector3(-50f, 140f, 0f); // Adjusted for water type
                defenceBurstOffsetPlayer = new Vector3(10f, 160f, 0f);
                break;
            case ElementType.Grass:
                defenceBurstOffsetAI = new Vector3(-50f, 140f, 0f); // Adjusted for grass type
                defenceBurstOffsetPlayer = new Vector3(10f, 160f, 0f);
                break;
        }
    }

    private void SetOffsets()
    {
        if (isPlayer)
        {
            switch (type)
            {
                case ElementType.Fire:
                    greenHPOffset = new Vector2(60, -10);
                    redHPOffset = new Vector2(75, 0);
                    greenSpeedOffset = new Vector2(40, -10);
                    redSpeedOffset = new Vector2(70, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-20, 0);
                    break;
                case ElementType.Water:
                    greenHPOffset = new Vector2(60, -10);
                    redHPOffset = new Vector2(70, 0);
                    greenSpeedOffset = new Vector2(-10, -10);
                    redSpeedOffset = new Vector2(0, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-30, 0);
                    break;
                case ElementType.Grass:
                    greenHPOffset = new Vector2(35, -10);
                    redHPOffset = new Vector2(70, 0);
                    greenSpeedOffset = new Vector2(40, -10);
                    redSpeedOffset = new Vector2(70, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-45, 0);
                    break;
            }
        }
        else
        {
            switch (type)
            {
                case ElementType.Fire:
                    greenHPOffset = new Vector2(60, -10);
                    redHPOffset = new Vector2(75, 0);
                    greenSpeedOffset = new Vector2(40, -10);
                    redSpeedOffset = new Vector2(70, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-20, 0);
                    break;
                case ElementType.Water:
                    greenHPOffset = new Vector2(60, -10);
                    redHPOffset = new Vector2(70, 0);
                    greenSpeedOffset = new Vector2(-10, -10);
                    redSpeedOffset = new Vector2(0, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-30, 0);
                    break;
                case ElementType.Grass:
                    greenHPOffset = new Vector2(35, -10);
                    redHPOffset = new Vector2(70, 0);
                    greenSpeedOffset = new Vector2(40, -10);
                    redSpeedOffset = new Vector2(70, 0);
                    greenDefenseOffset = new Vector2(-40, -10);
                    redDefenseOffset = new Vector2(-45, 0);
                    break;
            }
        }
    }

    void Start()
    {
        // SAFETY: Skip UI setup if we're in training mode or UIManager isn't available yet.
        // The authoritative setup path is AttackMenu.BindToCharacters() -> SetupUIPanel(), which
        // runs AFTER GameManager has instantiated us and activated the correct UI panels.
        if (TrainingMode.IsTraining)
        {
            return;
        }

        if (UIManager.Instance == null)
        {
            Debug.LogWarning($"[BattleCharacter] Start() skipped for {type} - UIManager not ready. BindToCharacters will run SetupUIPanel later.");
            return;
        }

        // Note: we do NOT call SetupUIPanel() here anymore. GameManager.ResetBattleWithPlayerStarter
        // calls AttackMenu.BindToCharacters() which calls SetupUIPanel() explicitly, AFTER panels
        // are shown via UIManager.ShowPanel. That sequencing is what avoids the stale-child bug.
        // We still update texts and relative offsets here so any BattleCharacter instantiated
        // outside the normal flow (e.g. training) still gets reasonable values.
        UpdateStatTexts();
        CalculateInitialRelativeOffsets();
    }

    /// <summary>
    /// Builds the UI particle arrows and grabs references to the panel's HP/Speed/Defense texts.
    /// PUBLIC so AttackMenu.BindToCharacters can call it after fresh characters are spawned.
    /// Tolerant of reruns: explicitly destroys stale arrow children before rebuilding.
    /// </summary>
    public void SetupUIPanel()
    {
        if (TrainingMode.IsTraining) return;

        GameObject panel = GetPanel();
        if (panel == null)
        {
            Debug.LogError($"[BattleCharacter] No panel found for type: {type}");
            return;
        }

        healthBarImage = panel.transform.Find("HealthBar")?.GetComponent<Image>();
        hpText = panel.transform.Find("HPText")?.GetComponent<TMP_Text>();
        speedText = panel.transform.Find("SpeedText")?.GetComponent<TMP_Text>();
        defenseText = panel.transform.Find("DefenseText")?.GetComponent<TMP_Text>();
        if (healthBarImage == null || hpText == null || speedText == null || defenseText == null)
        {
            Debug.LogError($"Missing UI elements in panel for type: {type}");
        }

        if (greenArrowPrefab == null || redArrowPrefab == null)
        {
            Debug.LogWarning($"[BattleCharacter] greenArrowPrefab/redArrowPrefab not assigned for {type} - skipping arrow setup.");
            return;
        }

        // --- FIX: nuke any leftover arrow containers from prior runs BEFORE building fresh ones.
        // We use DestroyImmediate because Destroy is deferred to end-of-frame, and a deferred
        // destroy is exactly what caused the stale-reference bug you were hitting.
        string[] leftover = { "GreenArrowHP", "RedArrowHP", "GreenArrowSpeed", "RedArrowSpeed", "GreenArrowDefense", "RedArrowDefense" };
        foreach (string childName in leftover)
        {
            Transform existing = panel.transform.Find(childName);
            if (existing != null)
            {
                DestroyImmediate(existing.gameObject);
            }
        }

        // Clear cached references so we don't hold pointers to just-destroyed systems
        greenArrowHP = null;
        redArrowHP = null;
        greenArrowSpeed = null;
        redArrowSpeed = null;
        greenArrowDefense = null;
        redArrowDefense = null;
        greenHP_RT = null;
        redHP_RT = null;
        greenSpeed_RT = null;
        redSpeed_RT = null;
        greenDefense_RT = null;
        redDefense_RT = null;

        // Setup for HP Green
        GameObject greenHPObj = new GameObject("GreenArrowHP");
        greenHPObj.transform.SetParent(panel.transform, false);
        greenHP_RT = greenHPObj.AddComponent<RectTransform>();
        if (hpText != null)
            greenHP_RT.anchoredPosition = hpText.GetComponent<RectTransform>().anchoredPosition + greenHPOffset;
        greenHP_RT.sizeDelta = Vector2.zero;
        greenHPObj.AddComponent<CanvasRenderer>();
        GameObject greenHPInst = Instantiate(greenArrowPrefab, greenHPObj.transform);
        greenHPInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            greenHPInst.transform.localScale = new Vector3(20, 20, 20);
        }
        greenArrowHP = greenHPInst.GetComponent<ParticleSystem>();
        if (greenArrowHP != null)
        {
            var mainModuleHPGreen = greenArrowHP.main;
            mainModuleHPGreen.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        // Setup for HP Red
        GameObject redHPObj = new GameObject("RedArrowHP");
        redHPObj.transform.SetParent(panel.transform, false);
        redHP_RT = redHPObj.AddComponent<RectTransform>();
        if (hpText != null)
            redHP_RT.anchoredPosition = hpText.GetComponent<RectTransform>().anchoredPosition + redHPOffset;
        redHP_RT.sizeDelta = Vector2.zero;
        redHPObj.AddComponent<CanvasRenderer>();
        GameObject redHPInst = Instantiate(redArrowPrefab, redHPObj.transform);
        redHPInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            redHPInst.transform.localScale = new Vector3(20, 20, 20);
        }
        redArrowHP = redHPInst.GetComponent<ParticleSystem>();
        if (redArrowHP != null)
        {
            var mainModuleHPRed = redArrowHP.main;
            mainModuleHPRed.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        // Setup for Speed Green
        GameObject greenSpeedObj = new GameObject("GreenArrowSpeed");
        greenSpeedObj.transform.SetParent(panel.transform, false);
        greenSpeed_RT = greenSpeedObj.AddComponent<RectTransform>();
        if (speedText != null)
            greenSpeed_RT.anchoredPosition = speedText.GetComponent<RectTransform>().anchoredPosition + greenSpeedOffset;
        greenSpeed_RT.sizeDelta = Vector2.zero;
        greenSpeedObj.AddComponent<CanvasRenderer>();
        GameObject greenSpeedInst = Instantiate(greenArrowPrefab, greenSpeedObj.transform);
        greenSpeedInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            greenSpeedInst.transform.localScale = new Vector3(20, 20, 20);
        }
        greenArrowSpeed = greenSpeedInst.GetComponent<ParticleSystem>();
        if (greenArrowSpeed != null)
        {
            var mainModuleSpeedGreen = greenArrowSpeed.main;
            mainModuleSpeedGreen.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        // Setup for Speed Red
        GameObject redSpeedObj = new GameObject("RedArrowSpeed");
        redSpeedObj.transform.SetParent(panel.transform, false);
        redSpeed_RT = redSpeedObj.AddComponent<RectTransform>();
        if (speedText != null)
            redSpeed_RT.anchoredPosition = speedText.GetComponent<RectTransform>().anchoredPosition + redSpeedOffset;
        redSpeed_RT.sizeDelta = Vector2.zero;
        redSpeedObj.AddComponent<CanvasRenderer>();
        GameObject redSpeedInst = Instantiate(redArrowPrefab, redSpeedObj.transform);
        redSpeedInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            redSpeedInst.transform.localScale = new Vector3(20, 20, 20);
        }
        redArrowSpeed = redSpeedInst.GetComponent<ParticleSystem>();
        if (redArrowSpeed != null)
        {
            var mainModuleSpeedRed = redArrowSpeed.main;
            mainModuleSpeedRed.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        // Setup for Defense Green
        GameObject greenDefObj = new GameObject("GreenArrowDefense");
        greenDefObj.transform.SetParent(panel.transform, false);
        greenDefense_RT = greenDefObj.AddComponent<RectTransform>();
        if (defenseText != null)
            greenDefense_RT.anchoredPosition = defenseText.GetComponent<RectTransform>().anchoredPosition + greenDefenseOffset;
        greenDefense_RT.sizeDelta = Vector2.zero;
        greenDefObj.AddComponent<CanvasRenderer>();
        GameObject greenDefInst = Instantiate(greenArrowPrefab, greenDefObj.transform);
        greenDefInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            greenDefInst.transform.localScale = new Vector3(20, 20, 20);
        }
        greenArrowDefense = greenDefInst.GetComponent<ParticleSystem>();
        if (greenArrowDefense != null)
        {
            var mainModuleDefGreen = greenArrowDefense.main;
            mainModuleDefGreen.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        // Setup for Defense Red
        GameObject redDefObj = new GameObject("RedArrowDefense");
        redDefObj.transform.SetParent(panel.transform, false);
        redDefense_RT = redDefObj.AddComponent<RectTransform>();
        if (defenseText != null)
            redDefense_RT.anchoredPosition = defenseText.GetComponent<RectTransform>().anchoredPosition + redDefenseOffset;
        redDefense_RT.sizeDelta = Vector2.zero;
        redDefObj.AddComponent<CanvasRenderer>();
        GameObject redDefInst = Instantiate(redArrowPrefab, redDefObj.transform);
        redDefInst.transform.localPosition = Vector3.zero;
        if (isMobile)
        {
            redDefInst.transform.localScale = new Vector3(20, 20, 20);
        }
        redArrowDefense = redDefInst.GetComponent<ParticleSystem>();
        if (redArrowDefense != null)
        {
            var mainModuleDefRed = redArrowDefense.main;
            mainModuleDefRed.simulationSpace = ParticleSystemSimulationSpace.Local;
        }

        Debug.Log($"[BattleCharacter] SetupUIPanel complete for {type}. Arrows built: greenHP={greenArrowHP != null}, redHP={redArrowHP != null}, greenSpd={greenArrowSpeed != null}, redSpd={redArrowSpeed != null}, greenDef={greenArrowDefense != null}, redDef={redArrowDefense != null}");

        // Refresh stat text and relative offsets now that references are bound.
        UpdateStatTexts();
        CalculateInitialRelativeOffsets();
    }

    void CalculateInitialRelativeOffsets()
    {
        if (TrainingMode.IsTraining) return;

        if (hpText != null)
        {
            hpText.ForceMeshUpdate();
            var info = hpText.textInfo;
            int count = info.characterCount;
            if (count > 0)
            {
                float scale_x = hpText.GetComponent<RectTransform>().localScale.x;
                float initialLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                greenHP_relX = greenHPOffset.x - initialLastX;
                redHP_relX = redHPOffset.x - initialLastX;
            }
        }
        if (speedText != null)
        {
            speedText.ForceMeshUpdate();
            var info = speedText.textInfo;
            int count = info.characterCount;
            if (count > 0)
            {
                float scale_x = speedText.GetComponent<RectTransform>().localScale.x;
                float initialLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                greenSpeed_relX = greenSpeedOffset.x - initialLastX;
                redSpeed_relX = redSpeedOffset.x - initialLastX;
            }
        }
        if (defenseText != null)
        {
            defenseText.ForceMeshUpdate();
            var info = defenseText.textInfo;
            int count = info.characterCount;
            if (count > 0)
            {
                float scale_x = defenseText.GetComponent<RectTransform>().localScale.x;
                float initialLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                greenDefense_relX = greenDefenseOffset.x - initialLastX;
                redDefense_relX = redDefenseOffset.x - initialLastX;
            }
        }
    }

    void OnValidate()
    {
        if (Application.isPlaying && hpText != null && speedText != null && defenseText != null)
        {
            UpdateParticlePositions();
        }
    }

    void UpdateParticlePositions()
    {
        if (TrainingMode.IsTraining) return;

        RectTransform hpRT = hpText ? hpText.GetComponent<RectTransform>() : null;
        RectTransform speedRT = speedText ? speedText.GetComponent<RectTransform>() : null;
        RectTransform defenseRT = defenseText ? defenseText.GetComponent<RectTransform>() : null;
        if (Application.isPlaying)
        {
            // Dynamic positioning during runtime based on last digit
            if (greenHP_RT != null && redHP_RT != null && hpRT != null && hpText != null)
            {
                var info = hpText.textInfo;
                int count = info.characterCount;
                if (count > 0)
                {
                    float scale_x = hpText.GetComponent<RectTransform>().localScale.x;
                    float currentLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                    greenHP_RT.anchoredPosition = new Vector2(hpRT.anchoredPosition.x + currentLastX + greenHP_relX, hpRT.anchoredPosition.y + greenHPOffset.y);
                    redHP_RT.anchoredPosition = new Vector2(hpRT.anchoredPosition.x + currentLastX + redHP_relX, hpRT.anchoredPosition.y + redHPOffset.y);
                }
            }
            if (greenSpeed_RT != null && redSpeed_RT != null && speedRT != null && speedText != null)
            {
                var info = speedText.textInfo;
                int count = info.characterCount;
                if (count > 0)
                {
                    float scale_x = speedText.GetComponent<RectTransform>().localScale.x;
                    float currentLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                    greenSpeed_RT.anchoredPosition = new Vector2(speedRT.anchoredPosition.x + currentLastX + greenSpeed_relX, speedRT.anchoredPosition.y + greenSpeedOffset.y);
                    redSpeed_RT.anchoredPosition = new Vector2(speedRT.anchoredPosition.x + currentLastX + redSpeed_relX, speedRT.anchoredPosition.y + redSpeedOffset.y);
                }
            }
            if (greenDefense_RT != null && redDefense_RT != null && defenseRT != null && defenseText != null)
            {
                var info = defenseText.textInfo;
                int count = info.characterCount;
                if (count > 0)
                {
                    float scale_x = defenseText.GetComponent<RectTransform>().localScale.x;
                    float currentLastX = info.characterInfo[count - 1].topRight.x * scale_x;
                    greenDefense_RT.anchoredPosition = new Vector2(defenseRT.anchoredPosition.x + currentLastX + greenDefense_relX, defenseRT.anchoredPosition.y + greenDefenseOffset.y);
                    redDefense_RT.anchoredPosition = new Vector2(defenseRT.anchoredPosition.x + currentLastX + redDefense_relX, defenseRT.anchoredPosition.y + redDefenseOffset.y);
                }
            }
        }
        else
        {
            // Fixed positioning in editor
            if (greenHP_RT != null && hpRT != null) greenHP_RT.anchoredPosition = hpRT.anchoredPosition + greenHPOffset;
            if (redHP_RT != null && hpRT != null) redHP_RT.anchoredPosition = hpRT.anchoredPosition + redHPOffset;
            if (greenSpeed_RT != null && speedRT != null) greenSpeed_RT.anchoredPosition = speedRT.anchoredPosition + greenSpeedOffset;
            if (redSpeed_RT != null && speedRT != null) redSpeed_RT.anchoredPosition = speedRT.anchoredPosition + redSpeedOffset;
            if (greenDefense_RT != null && defenseRT != null) greenDefense_RT.anchoredPosition = defenseRT.anchoredPosition + greenDefenseOffset;
            if (redDefense_RT != null && defenseRT != null) redDefense_RT.anchoredPosition = defenseRT.anchoredPosition + redDefenseOffset;
        }
    }

    void InitializeMoves()
    {
        Debug.Log("InitializeMoves called for " + type);
        moves = new Move[5];
        switch (type)
        {
            case ElementType.Fire:
                moves[0] = new Move { name = "Headbutt", type = ElementType.Normal, power = 30, pp = -1, description = "Deals 30 neutral damage." };
                moves[1] = new Move { name = "Ember", type = ElementType.Fire, power = 30, pp = -1, description = "Deals 35 fire damage." };
                moves[2] = new Move { name = "Intimidate", type = ElementType.Normal, power = 0, isStatus = true, pp = -1, description = "Lowers enemy defense by 10%." };
                moves[3] = new Move { name = "Fire Blaze", type = ElementType.Fire, power = 60, pp = 1, description = "Deals 60 fire damage. Usable once." };
                moves[4] = new Move { name = "Heal", type = ElementType.Normal, power = 0, isStatus = true, pp = 0, description = "Heals 25% of health. Usable once." };
                break;
            case ElementType.Water:
                moves[0] = new Move { name = "Headbutt", type = ElementType.Normal, power = 30, pp = -1, description = "Deals 30 neutral damage." };
                moves[1] = new Move { name = "Watergun", type = ElementType.Water, power = 30, pp = -1, description = "Deals 35 water damage." };
                moves[2] = new Move { name = "Intimidate", type = ElementType.Normal, power = 0, isStatus = true, pp = -1, description = "Lowers enemy defense by 10%." };
                moves[3] = new Move { name = "Hydroblast", type = ElementType.Water, power = 60, pp = 1, description = "Deals 60 water damage. Usable once." };
                moves[4] = new Move { name = "Heal", type = ElementType.Normal, power = 0, isStatus = true, pp = 0, description = "Heals 25% of health. Usable once." };
                break;
            case ElementType.Grass:
                moves[0] = new Move { name = "Headbutt", type = ElementType.Normal, power = 30, pp = -1, description = "Deals 30 neutral damage." };
                moves[1] = new Move { name = "Vine Whip", type = ElementType.Grass, power = 30, pp = -1, description = "Deals 35 grass damage." };
                moves[2] = new Move { name = "Intimidate", type = ElementType.Normal, power = 0, isStatus = true, pp = -1, description = "Lowers enemy defense by 10%." };
                moves[3] = new Move { name = "Leaf Storm", type = ElementType.Grass, power = 60, pp = 1, description = "Deals 60 grass damage. Usable once." };
                moves[4] = new Move { name = "Heal", type = ElementType.Normal, power = 0, isStatus = true, pp = 0, description = "Heals 25% of health. Usable once." };
                break;
        }
    }

    void SetTypeSpecificAttributes()
    {
        switch (type)
        {
            case ElementType.Fire:
                maxHP = 380;
                attack = 105;
                defense = 55;
                speed = 115;
                break;
            case ElementType.Water:
                maxHP = 400;
                attack = 85;
                defense = 80;
                speed = 75;
                break;
            case ElementType.Grass:
                maxHP = 350;
                attack = 90;
                defense = 60;
                speed = 100;
                break;
        }
        currentHP = maxHP;
        baseSpeed = speed;
        baseDefense = defense;
        intimidateCount = 0;
    }

    public void TakeDamage(int damage, string attackedTrigger, bool isCritical = false)
    {
        OnTakeDamage?.Invoke(damage, attackedTrigger, isCritical);
    }

    public void ApplyDamageAndEffects(int damage, string attackedTrigger)
    {
        if (TrainingMode.IsTraining)
        {
            currentHP -= damage;
            if (currentHP < 0) currentHP = 0;
            if (currentHP <= 0 && !isDefeated)
            {
                isDefeated = true;
            }
            return;
        }

        currentHP -= damage;
        if (currentHP < 0) currentHP = 0;
        if (currentHP <= 0 && !isDefeated)
        {
            isDefeated = true;
            StartCoroutine(DefeatCharacter());
        }
        if (animator != null && !string.IsNullOrEmpty(attackedTrigger))
        {
            animator.SetTrigger(attackedTrigger);
            if (audioSource != null)
            {
                AudioClip soundToPlay = (damage > 0) ? attackedsound : defensesound;
                float volumeToUse = (damage > 0) ? attackedSoundVolume : defenseSoundVolume;
                if (soundToPlay != null)
                {
                    audioSource.PlayOneShot(soundToPlay, volumeToUse);
                }
            }
        }
        if (damage > 0)
        {
            StartCoroutine(FlashTextCoroutine(hpText, Color.red, 1f));
            if (redArrowHP != null) redArrowHP.Play();
        }
        UpdateStatTexts();
    }

    public void ApplyDamage(int damage)
    {
        if (TrainingMode.IsTraining)
        {
            currentHP -= damage;
            if (currentHP < 0) currentHP = 0;
            if (currentHP <= 0 && !isDefeated)
            {
                isDefeated = true;
            }
            return;
        }

        currentHP -= damage;
        if (currentHP < 0) currentHP = 0;
        if (currentHP <= 0 && !isDefeated)
        {
            isDefeated = true;
            StartCoroutine(DefeatCharacter());
        }
        if (damage > 0)
        {
            StartCoroutine(FlashTextCoroutine(hpText, Color.red, 1f));
            if (redArrowHP != null) redArrowHP.Play();
        }
        UpdateStatTexts();
    }

    public void ApplyHeal(int amount)
    {
        if (TrainingMode.IsTraining)
        {
            currentHP += amount;
            if (currentHP > maxHP) currentHP = maxHP;
            return;
        }

        currentHP += amount;
        if (currentHP > maxHP) currentHP = maxHP;
        StartCoroutine(FlashTextCoroutine(hpText, Color.green, 1f));
        if (greenArrowHP != null) greenArrowHP.Play();
        UpdateStatTexts();
    }

    public void ApplyDefenseChange(int newDefense)
    {
        if (TrainingMode.IsTraining)
        {
            defense = Mathf.Min(newDefense, baseDefense * 3);  // FIX: Cap at 3x base (~225 for Water) to prevent bias/overflow
            return;
        }

        int oldDefense = defense;
        defense = newDefense;
        if (defense < oldDefense)
        {
            StartCoroutine(FlashTextCoroutine(defenseText, Color.red, 1f));
            if (redArrowDefense != null) redArrowDefense.Play();
        }
        else if (defense > oldDefense)
        {
            StartCoroutine(FlashTextCoroutine(defenseText, Color.green, 1f));
            if (greenArrowDefense != null) greenArrowDefense.Play();
        }
        UpdateStatTexts();
    }

    public void ApplySpeedChange(int newSpeed)
    {
        if (TrainingMode.IsTraining)
        {
            speed = Mathf.Min(newSpeed, baseSpeed * 3);  // FIX: Cap at 3x base (~225 for Water) to prevent bias/overflow
            return;
        }

        int oldSpeed = speed;
        speed = newSpeed;
        if (speed < oldSpeed)
        {
            StartCoroutine(FlashTextCoroutine(speedText, Color.red, 1f));
            if (redArrowSpeed != null) redArrowSpeed.Play();
        }
        else if (speed > oldSpeed)
        {
            StartCoroutine(FlashTextCoroutine(speedText, Color.green, 1f));
            if (greenArrowSpeed != null) greenArrowSpeed.Play();
        }
        UpdateStatTexts();
    }

    private IEnumerator FlashTextCoroutine(TMP_Text text, Color flashColor, float duration)
    {
        if (TrainingMode.IsTraining || text == null) yield break;

        Tween colorTween = GetColorTween(text);
        if (colorTween != null && colorTween.IsPlaying())
        {
            yield return colorTween.WaitForCompletion();
        }
        Color originalColor = text.color;
        text.color = flashColor;
        colorTween = text.DOColor(originalColor, duration).SetEase(Ease.InOutQuad);
        SetColorTween(text, colorTween);
        yield return colorTween.WaitForCompletion();
    }

    private Tween GetColorTween(TMP_Text text)
    {
        if (text == hpText) return colorTweenHp;
        if (text == speedText) return colorTweenSpeed;
        if (text == defenseText) return colorTweenDefense;
        return null;
    }

    private void SetColorTween(TMP_Text text, Tween tween)
    {
        if (text == hpText) colorTweenHp = tween;
        else if (text == speedText) colorTweenSpeed = tween;
        else if (text == defenseText) colorTweenDefense = tween;
    }

    public float GetDefenseMultiplier()
    {
        if (defenseStage >= 0)
            return (2f + defenseStage) / 2f;
        return 2f / (2f + -defenseStage);
    }

    public void UpdateStatTexts()
    {
        if (TrainingMode.IsTraining) return;

        if (healthBarImage != null)
        {
            float targetFillAmount = (float)currentHP / maxHP;
            if (healthBarTween != null)
            {
                healthBarTween.Kill();
            }
            healthBarTween = healthBarImage.DOFillAmount(targetFillAmount, 0.5f).SetEase(Ease.Linear);
        }
        if (hpText != null)
        {
            hpText.text = $"HP: {currentHP}/{maxHP}";
            hpText.ForceMeshUpdate();
        }
        if (speedText != null)
        {
            speedText.text = $"Spd: {speed}";
            speedText.ForceMeshUpdate();
        }
        if (defenseText != null)
        {
            defenseText.text = $"Def: {defense}";
            defenseText.ForceMeshUpdate();
        }
        UpdateParticlePositions();
    }

    public GameObject GetPanel()
    {
        if (TrainingMode.IsTraining) return null; // Suppress error in headless training

        if (UIManager.Instance == null)
        {
            Debug.LogError("UIManager.Instance is null. Ensure UIManager is initialized before BattleCharacter.");
            return null;
        }
        switch (type)
        {
            case ElementType.Fire:
                return UIManager.Instance.firePanel?.gameObject;
            case ElementType.Water:
                return UIManager.Instance.waterPanel?.gameObject;
            case ElementType.Grass:
                return UIManager.Instance.grassPanel?.gameObject;
            default:
                return null;
        }
    }

    public string GetName()
    {
        if (TrainingMode.IsTraining) return type.ToString(); // Default name in training to suppress error

        GameObject panel = GetPanel();
        if (panel != null)
        {
            TMP_Text nameText = panel.transform.Find("name")?.GetComponent<TMP_Text>();
            if (nameText != null)
            {
                return nameText.text;
            }
        }
        Debug.LogWarning($"Name not found for character of type {type}. Using default.");
        return type.ToString();
    }

    public IEnumerator PerformAttack(BattleCharacter defender)
    {
        if (TrainingMode.IsTraining || isDefeated) yield break;

        Vector3 originalPosition = transform.position;
        float direction = Mathf.Sign(defender.transform.position.x - transform.position.x);
        Vector3 forward = new Vector3(50f * direction, 0, 0);
        float elapsedTime = 0f;
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + forward;
        bool animationTriggered = false;
        yield return new WaitForSeconds(forwardMoveDelay);
        while (elapsedTime < forwardMoveTime)
        {
            transform.position = Vector3.Lerp(startPosition, targetPosition, elapsedTime / forwardMoveTime);
            if (!animationTriggered && elapsedTime >= animationStartDelay)
            {
                animator.SetTrigger("AttackTrigger");
                animationTriggered = true;
            }
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        transform.position = targetPosition;
        if (!animationTriggered)
        {
            animator.SetTrigger("AttackTrigger");
        }
        yield return new WaitForSeconds(hitDelayAfterForward);
        OnAttackHit?.Invoke();
        yield return new WaitForSeconds(backDelayAfterHit);
        elapsedTime = 0f;
        startPosition = transform.position;
        targetPosition = originalPosition;
        while (elapsedTime < backMoveTime)
        {
            transform.position = Vector3.Lerp(startPosition, targetPosition, elapsedTime / backMoveTime);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        transform.position = originalPosition;
    }

    public IEnumerator DefeatCharacter()
    {
        if (TrainingMode.IsTraining) yield break;

        // Disable animator to stop all animations
        if (animator != null)
        {
            animator.enabled = false;
        }
        // Get all renderers in the prefab (SpriteRenderer or MeshRenderer)
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning("No Renderer components found on this BattleCharacter or its children.");
        }
        // Add vibration: Shake left-right fast
        transform.DOShakePosition(0.5f, new Vector3(1f, 0, 0), 20, 90f);
        yield return new WaitForSeconds(.5f); // Wait for shake to finish
        // Fade out each renderer
        foreach (Renderer renderer in renderers)
        {
            if (renderer is SpriteRenderer spriteRenderer)
            {
                // Fade out SpriteRenderer using DOTween
                spriteRenderer.DOFade(0, 1f);
            }
            else if (renderer is MeshRenderer meshRenderer)
            {
                Material mat = meshRenderer.material;
                if (mat.HasProperty("_Color"))
                {
                    // Fade out MeshRenderer material color using DOTween
                    Color color = mat.color;
                    mat.DOColor(new Color(color.r, color.g, color.b, 0), 1f);
                }
                else
                {
                    Debug.LogWarning($"Material on {meshRenderer.name} does not have a '_Color' property. Check shader compatibility.");
                }
            }
        }
        // Move the character down
        transform.DOMoveY(transform.position.y - 100f, 1f);
        // Wait for the fade/move duration
        yield return new WaitForSeconds(1f);
    }

    public string ApplyPassive()
    {
        string log = "";
        if (isDefeated) return log;
        switch (type)
        {
            case ElementType.Grass:
                int heal = (int)(maxHP * 0.05f);
                if (heal > 0)
                {
                    ApplyHeal(heal);
                    log = $"{GetName()}'s health increased by {heal}";
                }
                break;
            case ElementType.Water:
                int speedInc = (int)(baseSpeed * 0.05f);
                if (speedInc > 0)
                {
                    ApplySpeedChange(speed + speedInc);
                    log = $"{GetName()}'s speed increased by {speedInc}";
                }
                break;
            case ElementType.Fire:
                int defInc = (int)(baseDefense * 0.05f);
                if (defInc > 0)
                {
                    ApplyDefenseChange(defense + defInc);
                    log = $"{GetName()}'s defense increased by {defInc}";
                }
                break;
        }
        return log;
    }

    public bool IsMoveUsable(int moveIndex)
    {
        if (moveIndex < 0 || moveIndex >= moves.Length || moves[moveIndex] == null) return false;
        return moves[moveIndex].pp == -1 || moves[moveIndex].pp > 0;
    }

    // Aggressive cleanup on destroy
    void OnDestroy()
    {
        DOTween.Kill(gameObject, true); // Kill all tweens associated with this object
        StopAllCoroutines();
    }
}