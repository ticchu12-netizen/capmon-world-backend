using System;

[Serializable]
public class PlayerData
{
    public string playerId;
    public string displayName;
    public bool isGuest = false;
    public string currentStarter = "Rageblaze";

    public int rank = 0;
    public long totalWon = 0;

    public long rageblazeCoins = 50000;
    public long tsunamiCoins = 50000;
    public long healspikeCoins = 50000;

    public long aiRageblazeCoins = 50000;
    public long aiTsunamiCoins = 50000;
    public long aiHealspikeCoins = 50000;

    // Solana stake state (populated by linkWallet Cloud Function)
    public string solanaWalletAddress;
    public int stakedTier = -1;       // -1 = no NFT staked
    public long stakedBrainSteps = 0;
    public string stakedAssetId;

    public long GetStarterCoins(string starterName)
    {
        return starterName.ToLower() switch
        {
            "rageblaze" => rageblazeCoins,
            "tsunami" => tsunamiCoins,
            "healspike" => healspikeCoins,
            _ => 0
        };
    }
}