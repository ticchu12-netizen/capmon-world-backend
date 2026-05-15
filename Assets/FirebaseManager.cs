using System;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

public class FirebaseManager : MonoBehaviour
{
    public static FirebaseManager Instance;

    public PlayerData currentPlayer;

    // Pending callbacks
    private Action pendingSignInSuccess;
    private Action<string> pendingSignInError;
    private Action<PlayerData> pendingMatchSuccess;
    private Action<string> pendingMatchError;
    private Action<PlayerData> pendingReviveSuccess;
    private Action<string> pendingReviveError;
    private Action pendingSaveSuccess;
    private Action<string> pendingSaveError;
    private Action<CapbotData> pendingCapbotSuccess;
    private Action<string> pendingCapbotError;
    private Action<string> pendingWalletSignInSuccess;
    private Action<string> pendingWalletSignInError;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void InitializeFirebase()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.Log("🌐 [FirebaseManager] WebGL - initializing via JS bridge");
        FirebaseInitJS();
#else
        Debug.Log("🧪 [FirebaseManager] Editor - Firebase calls will be stubbed");
#endif
    }

    // ====================== X / TWITTER SIGN-IN ======================
    public void SignInWithTwitter(Action onSuccess, Action<string> onError)
    {
        Debug.Log("=== [FirebaseManager] SignInWithTwitter ===");

#if UNITY_WEBGL && !UNITY_EDITOR
        pendingSignInSuccess = onSuccess;
        pendingSignInError = onError;
        SignInWithTwitterJS();
#else
        Debug.Log("🧪 EDITOR MODE: Simulating successful X login...");
        currentPlayer = new PlayerData
        {
            isGuest = false,
            displayName = "Editor Test User",
            playerId = "editor_test_" + System.DateTime.UtcNow.Ticks,
            rageblazeCoins = 50000,
            tsunamiCoins = 50000,
            healspikeCoins = 50000,
            aiRageblazeCoins = 10000000,
            aiTsunamiCoins = 10000000,
            aiHealspikeCoins = 10000000,
            currentStarter = "Rageblaze"
        };
        onSuccess?.Invoke();
#endif
    }

public void SignInWithWallet(Action<string> onSuccess, Action<string> onError)
{
    Debug.Log("=== [FirebaseManager] SignInWithWallet ===");
#if UNITY_WEBGL && !UNITY_EDITOR
    pendingWalletSignInSuccess = onSuccess;
    pendingWalletSignInError = onError;
    SignInWithWalletJS();
#else
    var fakeUid = "EditorWallet1234567890123456789012345678";
    currentPlayer = new PlayerData
    {
        playerId = fakeUid,
        displayName = "Editor Wallet User",
        isGuest = false,
        currentStarter = "Rageblaze",
        rageblazeCoins = 50000,
        tsunamiCoins = 50000,
        healspikeCoins = 50000,
        aiRageblazeCoins = 10000000,
        aiTsunamiCoins = 10000000,
        aiHealspikeCoins = 10000000,
    };
    onSuccess?.Invoke(fakeUid);
#endif
}

public void OnWalletSignInSuccess(string json)
{
    Debug.Log("✅ [FirebaseManager] OnWalletSignInSuccess: " + json);
    try
    {
        var info = JsonUtility.FromJson<WalletSignInResult>(json);
        if (currentPlayer == null) currentPlayer = new PlayerData();
        currentPlayer.isGuest = false;
        currentPlayer.playerId = info.userId;
        currentPlayer.displayName = info.displayName;
        // Pre-populate stake state so any UI that reads currentPlayer before
        // LoadPlayerData completes still sees the correct tier. LoadPlayerData
        // will overwrite with the authoritative values from Firestore shortly.
        currentPlayer.solanaWalletAddress = info.walletAddress;
        currentPlayer.stakedTier = info.stakedTier;
        currentPlayer.stakedBrainSteps = info.stakedBrainSteps;
        currentPlayer.stakedAssetId = info.stakedAssetId;

#if UNITY_WEBGL && !UNITY_EDITOR
        // Bridge wallet callbacks into the X-flow's pendingSignInSuccess slot
        // so OnPlayerDataLoaded fires the wallet onSuccess after the player
        // doc loads (mirrors the X auth pattern).
        var walletSuccess = pendingWalletSignInSuccess;
        var walletError = pendingWalletSignInError;
        pendingWalletSignInSuccess = null;
        pendingWalletSignInError = null;
        var uid = info.userId;
        pendingSignInSuccess = () => walletSuccess?.Invoke(uid);
        pendingSignInError = err => walletError?.Invoke(err);

        LoadPlayerDataJS(currentPlayer.playerId);
#else
        pendingWalletSignInSuccess?.Invoke(info.userId);
        pendingWalletSignInSuccess = null;
        pendingWalletSignInError = null;
#endif
    }
    catch (Exception ex)
    {
        Debug.LogError("[FirebaseManager] Failed to parse wallet sign-in: " + ex.Message);
        pendingWalletSignInError?.Invoke("Failed to parse server response");
        pendingWalletSignInSuccess = null;
        pendingWalletSignInError = null;
    }
}

public void OnWalletSignInError(string err)
{
    Debug.LogError("❌ [FirebaseManager] OnWalletSignInError: " + err);
    pendingWalletSignInError?.Invoke(err);
    pendingWalletSignInSuccess = null;
    pendingWalletSignInError = null;
}
    public void OnTwitterSignInSuccess(string userJson)
    {
        Debug.Log("✅ [FirebaseManager] OnTwitterSignInSuccess: " + userJson);

        var parsed = JsonUtility.FromJson<TwitterSignInResult>(userJson);

        if (currentPlayer == null) currentPlayer = new PlayerData();
        currentPlayer.isGuest = false;
        currentPlayer.playerId = parsed.userId;
        currentPlayer.displayName = parsed.displayName;

#if UNITY_WEBGL && !UNITY_EDITOR
        LoadPlayerDataJS(currentPlayer.playerId);
#else
        pendingSignInSuccess?.Invoke();
        pendingSignInSuccess = null;
        pendingSignInError = null;
#endif
    }

    public void OnTwitterSignInError(string errorMessage)
    {
        Debug.LogError("❌ [FirebaseManager] OnTwitterSignInError: " + errorMessage);
        pendingSignInError?.Invoke(errorMessage);
        pendingSignInSuccess = null;
        pendingSignInError = null;
    }

    public void OnPlayerDataLoaded(string playerJson)
    {
        Debug.Log("✅ [FirebaseManager] OnPlayerDataLoaded: " + playerJson);
        if (!string.IsNullOrEmpty(playerJson) && playerJson != "null")
        {
            var loaded = JsonUtility.FromJson<PlayerData>(playerJson);
            loaded.playerId = currentPlayer.playerId;
            if (string.IsNullOrEmpty(loaded.displayName)) loaded.displayName = currentPlayer.displayName;
            loaded.isGuest = false;

            // Re-pin Solana stake fields if in-memory has them but server doesn't yet
            // (handles race where loadPlayerData fires during a linkWallet flow)
            if (currentPlayer.stakedTier >= 0 && loaded.stakedTier < 0)
            {
                loaded.solanaWalletAddress = currentPlayer.solanaWalletAddress;
                loaded.stakedTier = currentPlayer.stakedTier;
                loaded.stakedBrainSteps = currentPlayer.stakedBrainSteps;
                loaded.stakedAssetId = currentPlayer.stakedAssetId;
            }

            currentPlayer = loaded;
        }

        pendingSignInSuccess?.Invoke();
        pendingSignInSuccess = null;
        pendingSignInError = null;
    }

    /// <summary>
    /// Called by WalletManager.OnLinkWalletSuccess to push the freshly-mirrored
    /// stake state onto currentPlayer so battle UI + resolveMatch see the new tier.
    /// </summary>
    public void ApplyStakeStateFromLink(LinkWalletResult result)
    {
        if (currentPlayer == null) return;
        currentPlayer.solanaWalletAddress = result.walletAddress;
        currentPlayer.stakedTier = result.stakedTier;
        currentPlayer.stakedBrainSteps = result.stakedBrainSteps;
        currentPlayer.stakedAssetId = result.stakedAssetId;
        Debug.Log($"[FirebaseManager] Stake state applied: tier={result.stakedTier} steps={result.stakedBrainSteps}");
    }

    // ====================== SAVE PLAYER DATA (DEPRECATED in Phase 1) ======================
    public void SavePlayerData(Action onSuccess = null, Action<string> onError = null)
    {
        Debug.LogWarning("[FirebaseManager] SavePlayerData is deprecated in Phase 1. Use ResolveMatch instead.");
        onSuccess?.Invoke();
    }

    public void OnSavePlayerDataSuccess(string _unused)
    {
        Debug.Log("✅ [FirebaseManager] (legacy) Player data saved to Firestore");
        pendingSaveSuccess?.Invoke();
        pendingSaveSuccess = null;
        pendingSaveError = null;
    }

    public void OnSavePlayerDataError(string errorMessage)
    {
        Debug.LogError("❌ [FirebaseManager] (legacy) SavePlayerData failed: " + errorMessage);
        pendingSaveError?.Invoke(errorMessage);
        pendingSaveSuccess = null;
        pendingSaveError = null;
    }

    // ====================== RESOLVE MATCH (PHASE 1 PRIMARY PATH) ======================
    public void ResolveMatch(int betAmount, bool playerWon, string usedStarter, string defeatedAi,
                             Action<PlayerData> onSuccess, Action<string> onError)
    {
        Debug.Log($"=== [FirebaseManager] ResolveMatch | bet={betAmount} | won={playerWon} | starter={usedStarter} | ai={defeatedAi} ===");

        if (currentPlayer == null)
        {
            onError?.Invoke("No player data");
            return;
        }

        if (currentPlayer.isGuest)
        {
            Debug.Log("[FirebaseManager] Guest mode — applying match locally");
            ApplyMatchResultLocally(betAmount, playerWon, usedStarter, defeatedAi);
            onSuccess?.Invoke(currentPlayer);
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        pendingMatchSuccess = onSuccess;
        pendingMatchError = onError;

        string idempotencyKey = Guid.NewGuid().ToString("N");

        var req = new MatchResolveRequest
        {
            idempotencyKey = idempotencyKey,
            betAmount = betAmount,
            playerWon = playerWon,
            usedStarter = usedStarter,
            defeatedAi = defeatedAi,
        };
        string json = JsonUtility.ToJson(req);
        Debug.Log($"[FirebaseManager] ResolveMatchJS payload: {json}");
        ResolveMatchJS(json);
#else
        Debug.Log("🧪 [FirebaseManager] Editor: applying match locally");
        ApplyMatchResultLocally(betAmount, playerWon, usedStarter, defeatedAi);
        onSuccess?.Invoke(currentPlayer);
#endif
    }

    public void OnMatchResolveSuccess(string resultJson)
    {
        Debug.Log("✅ [FirebaseManager] OnMatchResolveSuccess: " + resultJson);

        try
        {
            var result = JsonUtility.FromJson<MatchResolveResult>(resultJson);

            int oldRank = currentPlayer.rank;

            switch (result.usedStarter.ToLower())
            {
                case "rageblaze": currentPlayer.rageblazeCoins = result.newStarterBalance; break;
                case "tsunami":   currentPlayer.tsunamiCoins   = result.newStarterBalance; break;
                case "healspike": currentPlayer.healspikeCoins = result.newStarterBalance; break;
            }

            switch (result.defeatedAi.ToLower())
            {
                case "rageblaze": currentPlayer.aiRageblazeCoins = result.newAiPool; break;
                case "tsunami":   currentPlayer.aiTsunamiCoins   = result.newAiPool; break;
                case "healspike": currentPlayer.aiHealspikeCoins = result.newAiPool; break;
            }

            currentPlayer.totalWon = result.newTotalWon;
            currentPlayer.rank = result.newRank;

            if (result.newRank > oldRank)
            {
                string newRankName = GetRankName(result.newRank);
                Debug.Log($"🏆 [FirebaseManager] RANK UP! {GetRankName(oldRank)} → {newRankName}");
            }

            pendingMatchSuccess?.Invoke(currentPlayer);
        }
        catch (Exception ex)
        {
            Debug.LogError("[FirebaseManager] Failed to parse ResolveMatch result: " + ex.Message);
            pendingMatchError?.Invoke("Failed to parse server response");
        }
        finally
        {
            pendingMatchSuccess = null;
            pendingMatchError = null;
        }
    }

    public void OnMatchResolveError(string errorMessage)
    {
        Debug.LogError("❌ [FirebaseManager] OnMatchResolveError: " + errorMessage);
        pendingMatchError?.Invoke(errorMessage);
        pendingMatchSuccess = null;
        pendingMatchError = null;
    }
public void GetCapbotData(Action<CapbotData> onSuccess, Action<string> onError)
    {
        Debug.Log("=== [FirebaseManager] GetCapbotData ===");
        if (currentPlayer == null || currentPlayer.isGuest)
        {
            onError?.Invoke("No staked NFT (sign in with X to use Capbot)");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        pendingCapbotSuccess = onSuccess;
        pendingCapbotError = onError;
        GetCapbotDataJS();
#else
        // Editor stub: build a single-stake CapbotData from currentPlayer flat fields
        CapbotStakeEntry[] stubStakes;
        if (currentPlayer.stakedTier >= 0 && !string.IsNullOrEmpty(currentPlayer.stakedAssetId))
        {
            stubStakes = new CapbotStakeEntry[]
            {
                new CapbotStakeEntry
                {
                    assetId = currentPlayer.stakedAssetId,
                    tier = currentPlayer.stakedTier,
                    brainSteps = currentPlayer.stakedBrainSteps,
                    lastBattleAt = 0L,
                }
            };
        }
        else
        {
            stubStakes = new CapbotStakeEntry[0];
        }

        var fake = new CapbotData
        {
            stakedTier = currentPlayer.stakedTier,
            stakedBrainSteps = currentPlayer.stakedBrainSteps,
            stakedAssetId = currentPlayer.stakedAssetId,
            walletAddress = currentPlayer.solanaWalletAddress,
            stakes = stubStakes,
            recentBattles = new CapbotBattleEntry[0],
            recentUpgrades = new BrainUpgradeEntry[0],
        };
        onSuccess?.Invoke(fake);
#endif
    }

    public void OnCapbotDataLoaded(string json)
    {
        Debug.Log("✅ [FirebaseManager] OnCapbotDataLoaded: " + json);
        try
        {
            var data = JsonUtility.FromJson<CapbotData>(json);
            pendingCapbotSuccess?.Invoke(data);
        }
        catch (Exception ex)
        {
            Debug.LogError("[FirebaseManager] Failed to parse Capbot data: " + ex.Message);
            pendingCapbotError?.Invoke("Failed to parse server response");
        }
        finally
        {
            pendingCapbotSuccess = null;
            pendingCapbotError = null;
        }
    }

    public void OnCapbotDataError(string err)
    {
        Debug.LogError("❌ [FirebaseManager] OnCapbotDataError: " + err);
        pendingCapbotError?.Invoke(err);
        pendingCapbotSuccess = null;
        pendingCapbotError = null;
    }
    public static string GetRankName(int rank)
    {
        switch (rank)
        {
            case 0: return "Bronze";
            case 1: return "Silver";
            case 2: return "Gold";
            case 3: return "Platinum";
            case 4: return "Diamond";
            default: return "Unknown";
        }
    }

    // ====================== REVIVE STARTER ======================
    public void ReviveStarter(string fromStarter, string toStarter, long amount,
                              Action<PlayerData> onSuccess, Action<string> onError)
    {
        Debug.Log($"=== [FirebaseManager] ReviveStarter | from={fromStarter} → to={toStarter} | amount={amount} ===");

        if (currentPlayer == null)
        {
            onError?.Invoke("No player data");
            return;
        }

        if (currentPlayer.isGuest)
        {
            Debug.Log("[FirebaseManager] Guest mode — applying revive locally");
            ApplyReviveLocally(fromStarter, toStarter, amount);
            onSuccess?.Invoke(currentPlayer);
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        pendingReviveSuccess = onSuccess;
        pendingReviveError = onError;

        var req = new ReviveRequest
        {
            fromStarter = fromStarter,
            toStarter = toStarter,
            amount = (int)amount,
        };
        string json = JsonUtility.ToJson(req);
        Debug.Log($"[FirebaseManager] ReviveStarterJS payload: {json}");
        ReviveStarterJS(json);
#else
        Debug.Log("🧪 [FirebaseManager] Editor: applying revive locally");
        ApplyReviveLocally(fromStarter, toStarter, amount);
        onSuccess?.Invoke(currentPlayer);
#endif
    }

    public void OnReviveSuccess(string resultJson)
    {
        Debug.Log("✅ [FirebaseManager] OnReviveSuccess: " + resultJson);

        try
        {
            var result = JsonUtility.FromJson<ReviveResult>(resultJson);

            switch (result.fromStarter.ToLower())
            {
                case "rageblaze": currentPlayer.rageblazeCoins = result.newFromBalance; break;
                case "tsunami":   currentPlayer.tsunamiCoins   = result.newFromBalance; break;
                case "healspike": currentPlayer.healspikeCoins = result.newFromBalance; break;
            }
            switch (result.toStarter.ToLower())
            {
                case "rageblaze": currentPlayer.rageblazeCoins = result.newToBalance; break;
                case "tsunami":   currentPlayer.tsunamiCoins   = result.newToBalance; break;
                case "healspike": currentPlayer.healspikeCoins = result.newToBalance; break;
            }

            pendingReviveSuccess?.Invoke(currentPlayer);
        }
        catch (Exception ex)
        {
            Debug.LogError("[FirebaseManager] Failed to parse Revive result: " + ex.Message);
            pendingReviveError?.Invoke("Failed to parse server response");
        }
        finally
        {
            pendingReviveSuccess = null;
            pendingReviveError = null;
        }
    }

    public void OnReviveError(string errorMessage)
    {
        Debug.LogError("❌ [FirebaseManager] OnReviveError: " + errorMessage);
        pendingReviveError?.Invoke(errorMessage);
        pendingReviveSuccess = null;
        pendingReviveError = null;
    }

    // ====================== GUEST MODE ======================
    public void StartAsGuest(Action onSuccess)
    {
        currentPlayer = new PlayerData
        {
            isGuest = true,
            displayName = "Guest Player",
            currentStarter = "Rageblaze",
            rageblazeCoins = 50000,
            tsunamiCoins = 50000,
            healspikeCoins = 50000,
            aiRageblazeCoins = 10000000,
            aiTsunamiCoins = 10000000,
            aiHealspikeCoins = 10000000,
        };
        Debug.Log("🎮 Started as Guest");
        onSuccess?.Invoke();
    }

    public void LoadPlayerData(Action onSuccess, Action<string> onError)
    {
        if (currentPlayer == null || currentPlayer.isGuest)
        {
            onSuccess?.Invoke();
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        pendingSignInSuccess = onSuccess;
        pendingSignInError = onError;
        LoadPlayerDataJS(currentPlayer.playerId);
#else
        onSuccess?.Invoke();
#endif
    }

    // ====================== LOCAL APPLY (guest + editor only) ======================
    private void ApplyMatchResultLocally(int betAmount, bool playerWon, string usedStarter, string defeatedAi)
    {
        int delta = playerWon ? betAmount : -betAmount;

        switch (usedStarter.ToLower())
        {
            case "rageblaze": currentPlayer.rageblazeCoins += delta; break;
            case "tsunami":   currentPlayer.tsunamiCoins   += delta; break;
            case "healspike": currentPlayer.healspikeCoins += delta; break;
        }

        switch (defeatedAi.ToLower())
        {
            case "rageblaze": currentPlayer.aiRageblazeCoins -= delta; break;
            case "tsunami":   currentPlayer.aiTsunamiCoins   -= delta; break;
            case "healspike": currentPlayer.aiHealspikeCoins -= delta; break;
        }

        if (playerWon)
        {
            currentPlayer.totalWon += betAmount;

            int oldRank = currentPlayer.rank;
            currentPlayer.rank = ComputeRankLocally(currentPlayer.totalWon);
            if (currentPlayer.rank > oldRank)
            {
                Debug.Log($"🏆 [FirebaseManager] (Guest) RANK UP! {GetRankName(oldRank)} → {GetRankName(currentPlayer.rank)}");
            }
        }
    }

    private static int ComputeRankLocally(long totalWon)
    {
        if (totalWon >= 11_000_000) return 4;
        if (totalWon >= 3_300_000)  return 3;
        if (totalWon >= 825_000)    return 2;
        if (totalWon >= 165_000)    return 1;
        return 0;
    }

    private void ApplyReviveLocally(string fromStarter, string toStarter, long amount)
    {
        switch (fromStarter.ToLower())
        {
            case "rageblaze": currentPlayer.rageblazeCoins -= amount; break;
            case "tsunami":   currentPlayer.tsunamiCoins   -= amount; break;
            case "healspike": currentPlayer.healspikeCoins -= amount; break;
        }
        switch (toStarter.ToLower())
        {
            case "rageblaze": currentPlayer.rageblazeCoins = amount; break;
            case "tsunami":   currentPlayer.tsunamiCoins   = amount; break;
            case "healspike": currentPlayer.healspikeCoins = amount; break;
        }
    }

    // ====================== JS bridge declarations ======================
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void FirebaseInitJS();
    [DllImport("__Internal")] private static extern void SignInWithTwitterJS();
    [DllImport("__Internal")] private static extern void LoadPlayerDataJS(string playerId);
    [DllImport("__Internal")] private static extern void SavePlayerDataJS(string jsonRequest);
    [DllImport("__Internal")] private static extern void ResolveMatchJS(string jsonRequest);
    [DllImport("__Internal")] private static extern void ReviveStarterJS(string jsonRequest);
    [DllImport("__Internal")] private static extern void GetCapbotDataJS();
    [DllImport("__Internal")] private static extern void SignInWithWalletJS();
#endif

    // ====================== JsonUtility helper types ======================
    [Serializable]
    private class TwitterSignInResult
    {
        public string userId;
        public string displayName;
    }

    public class WalletSignInResult
{
    public string userId;
    public string displayName;
    public string walletAddress;
    public int stakedTier;
    public long stakedBrainSteps;
    public string stakedAssetId;
    public bool isNewPlayer;
}

    [Serializable]
    private class MatchResolveRequest
    {
        public string idempotencyKey;
        public int betAmount;
        public bool playerWon;
        public string usedStarter;
        public string defeatedAi;
    }

    [Serializable]
    private class MatchResolveResult
    {
        public bool success;
        public bool playerWon;
        public int playerDelta;
        public long newStarterBalance;
        public long newAiPool;
        public long newTotalWon;
        public int newRank;
        public string usedStarter;
        public string defeatedAi;
        public float appliedMultiplier;   // 1.0/1.4/1.9/2.8 — used for "+X (M× Tier)" UI
    }

    [Serializable]
    public class CapbotData
    {
        public int stakedTier;
        public long stakedBrainSteps;
        public string stakedAssetId;
        public string walletAddress;
        public CapbotStakeEntry[] stakes;          // NEW: multi-stake array (CF returns this)
        public CapbotBattleEntry[] recentBattles;
        public BrainUpgradeEntry[] recentUpgrades;
    }

    [Serializable]
    public class CapbotStakeEntry
    {
        public string assetId;
        public int tier;
        public long brainSteps;     // long matches CapbotData.stakedBrainSteps for type consistency
        public long lastBattleAt;
    }

    [Serializable]
    public class CapbotBattleEntry
    {
        public long timestamp;
        public string capbotType;
        public string defeatedAi;
        public int payout;
        public float multiplier;
        public bool playerWon;
        public string battleId;
        public string stakedAssetId;   // NEW: per-stake filter for pagination
    }

    [Serializable]
    public class BrainUpgradeEntry
    {
        public long timestamp;
        public long oldBrainSteps;
        public long newBrainSteps;
        public string txSignature;
        public long coinsBurned;
        public string burnCurrency;
        public string battleId;
        public string stakedAssetId;   // NEW: per-stake filter for pagination
    }
    
    [Serializable]
    private class ReviveRequest
    {
        public string fromStarter;
        public string toStarter;
        public int amount;
    }

    [Serializable]
    private class ReviveResult
    {
        public bool success;
        public string fromStarter;
        public string toStarter;
        public int amount;
        public long newFromBalance;
        public long newToBalance;
    }
}