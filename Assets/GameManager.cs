using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

public class GameManager : MonoBehaviour
{
    public GameObject firePrefab;
    public GameObject waterPrefab;
    public GameObject grassPrefab;
    public AttackMenu attackMenu;
    public UIManager uiManager;
    public Canvas mainCanvas;
    public Transform background;

    private GameObject[] prefabs;
    private float[] scales = new float[] { 40f, 40f, 15f };

    void Awake()
    {
        prefabs = new GameObject[] { firePrefab, waterPrefab, grassPrefab };
    }

    void Start()
    {
        Debug.Log("=== [GameManager] Start() called ===");
        FirebaseManager.Instance.InitializeFirebase();

        if (attackMenu != null)
        {
            attackMenu.gameObject.SetActive(false);
            if (attackMenu.battleLogButton != null) attackMenu.battleLogButton.gameObject.SetActive(false);
            if (attackMenu.fullLogPanel != null) attackMenu.fullLogPanel.SetActive(false);
        }

        LoginScreen loginScreen = FindFirstObjectByType<LoginScreen>(FindObjectsInactive.Include);
        if (loginScreen != null) loginScreen.gameObject.SetActive(true);
    }

    public void StartBattleWithSelectedStarter(string starterName)
    {
        Debug.Log("=== BATTLE INITIALIZED AFTER BETTING CONFIRM ===");

        int playerIndex = 0;
        switch (starterName.ToLower())
        {
            case "rageblaze": playerIndex = 0; break;
            case "tsunami": playerIndex = 1; break;
            case "healspike": playerIndex = 2; break;
        }

        ResetBattleWithPlayerStarter(playerIndex);
    }

    private void ResetBattleWithPlayerStarter(int playerIndex)
    {
        if (attackMenu.player != null && attackMenu.player.gameObject != null && attackMenu.player.gameObject.scene.IsValid())
        {
            DOTween.Kill(attackMenu.player.gameObject, true);
            Destroy(attackMenu.player.gameObject);
        }
        if (attackMenu.ai != null && attackMenu.ai.gameObject != null && attackMenu.ai.gameObject.scene.IsValid())
        {
            DOTween.Kill(attackMenu.ai.gameObject, true);
            Destroy(attackMenu.ai.gameObject);
        }

        attackMenu.player = null;
        attackMenu.ai = null;

        int aiIndex = Random.Range(0, 3);
        while (aiIndex == playerIndex)
        {
            aiIndex = Random.Range(0, 3);
        }

        float playerScaleX = (playerIndex == 0 || playerIndex == 2) ? -scales[playerIndex] : scales[playerIndex];
        float aiScaleX = (aiIndex == 1) ? -scales[aiIndex] : scales[aiIndex];

        float playerY = 240f;
        float aiY = 240f;
        float playerX = 315f;
        float aiX = 955f;

        if (playerIndex == 1) playerY += 20f;
        if (aiIndex == 1) aiY += 10f;
        if (playerIndex == 2) playerY += 0f;
        if (aiIndex == 2) aiY += -5f;
        if (playerIndex == 2) playerX += 1f;
        if (aiIndex == 2) aiX += 1f;
        if (playerIndex == 0) playerY += -4f;
        if (aiIndex == 0) aiY += -7f;
        if (playerIndex == 0) playerX += -20f;
        if (aiIndex == 0) aiX += 20f;

        GameObject playerChar = Instantiate(prefabs[playerIndex], new Vector3(playerX, playerY, 0), Quaternion.identity);
        playerChar.transform.localScale = new Vector3(playerScaleX, scales[playerIndex], scales[playerIndex]);
        playerChar.transform.SetParent(background);
        BattleCharacter playerBattleChar = playerChar.GetComponent<BattleCharacter>();

        UIScaleSyncer playerSyncer = playerChar.GetComponent<UIScaleSyncer>();
        if (playerSyncer != null && mainCanvas != null)
        {
            playerSyncer.canvasScaler = mainCanvas.GetComponent<CanvasScaler>();
        }

        GameObject aiChar = Instantiate(prefabs[aiIndex], new Vector3(aiX, aiY, 0), Quaternion.identity);
        aiChar.transform.localScale = new Vector3(aiScaleX, scales[aiIndex], scales[aiIndex]);
        aiChar.transform.SetParent(background);
        BattleCharacter aiBattleChar = aiChar.GetComponent<BattleCharacter>();

        UIScaleSyncer aiSyncer = aiChar.GetComponent<UIScaleSyncer>();
        if (aiSyncer != null && mainCanvas != null)
        {
            aiSyncer.canvasScaler = mainCanvas.GetComponent<CanvasScaler>();
        }

        playerBattleChar.isPlayer = true;
        aiBattleChar.isPlayer = false;

        attackMenu.player = playerBattleChar;
        attackMenu.ai = aiBattleChar;

        if (TrainingMode.IsTraining) return;

        if (uiManager != null)
        {
            uiManager.HideAllPanels();
            uiManager.ShowPanel(playerBattleChar.type, true);
            uiManager.ShowPanel(aiBattleChar.type, false);
        }

        if (attackMenu != null)
        {
            attackMenu.gameObject.SetActive(true);
            if (attackMenu.battleLogButton != null) attackMenu.battleLogButton.gameObject.SetActive(true);
            if (attackMenu.fullLogPanel != null) attackMenu.fullLogPanel.SetActive(false);
            attackMenu.BindToCharacters();
        }
    }

    public void InitializeBattle()
    {
        ResetBattle();
    }

    public void ResetBattle()
    {
        Debug.Log("=== [GameManager] Play Again → Full cleanup + Starter Selection ===");

        DOTween.KillAll(false);

        if (attackMenu.player != null && attackMenu.player.gameObject != null)
        {
            DOTween.Kill(attackMenu.player.gameObject, true);
            Destroy(attackMenu.player.gameObject);
        }
        if (attackMenu.ai != null && attackMenu.ai.gameObject != null)
        {
            DOTween.Kill(attackMenu.ai.gameObject, true);
            Destroy(attackMenu.ai.gameObject);
        }
        attackMenu.player = null;
        attackMenu.ai = null;

        if (attackMenu != null)
        {
            attackMenu.gameObject.SetActive(false);
            if (attackMenu.battleLogButton != null) attackMenu.battleLogButton.gameObject.SetActive(false);
            if (attackMenu.fullLogPanel != null) attackMenu.fullLogPanel.SetActive(false);
        }

        if (uiManager != null) uiManager.HideAllPanels();

        // === Destroy any lingering DamageEffect INSTANCES ===
        // IMPORTANT: if attackMenu.damageEffectPrefab is a scene object (dragged in from
        // the Hierarchy rather than a Project-window asset), FindObjectsByType will
        // return the template itself alongside any real instances. Destroying the
        // template wipes the inspector reference for the rest of the session, causing
        // damage effects to silently fail on every subsequent battle. We guard against
        // that by skipping any GameObject that IS the prefab reference.
        DamageEffect[] lingeringEffects = FindObjectsByType<DamageEffect>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject prefabTemplate = (attackMenu != null) ? attackMenu.damageEffectPrefab : null;
        int destroyedCount = 0;
        int skippedTemplateCount = 0;
        foreach (DamageEffect effect in lingeringEffects)
        {
            if (effect == null) continue;

            // Skip the prefab template itself if it lives in the scene.
            // Object identity (==) is the right check: for a scene-object template, the
            // GameObject reference matches; for a Project asset, Instantiate copies never
            // match the asset, so this branch simply never fires. Safe either way.
            if (prefabTemplate != null && effect.gameObject == prefabTemplate)
            {
                skippedTemplateCount++;
                continue;
            }

            DOTween.Kill(effect.gameObject, true);
            Destroy(effect.gameObject);
            destroyedCount++;
        }

        Debug.Log($"=== [GameManager] Damage effect cleanup: destroyed {destroyedCount} instances, skipped {skippedTemplateCount} prefab template(s) ===");

        StarterSelectionScreen starterScreen = FindFirstObjectByType<StarterSelectionScreen>(FindObjectsInactive.Include);
        if (starterScreen != null)
        {
            starterScreen.gameObject.SetActive(true);
            StartCoroutine(RefreshCoinsAfterOneFrame(starterScreen));
        }
        else
        {
            Debug.LogError("StarterSelectionScreen not found!");
        }
    }

    private IEnumerator RefreshCoinsAfterOneFrame(StarterSelectionScreen screen)
    {
        yield return null;
        if (screen != null)
        {
            screen.RefreshCoins();
            Debug.Log("=== [GameManager] Coin refresh forced after one frame ===");
        }
    }
}