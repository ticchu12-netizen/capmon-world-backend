using System;
using System.Runtime.InteropServices;
using UnityEngine;

[Serializable]
public class LinkWalletResult
{
    public string walletAddress;
    public int stakedTier = -1;
    public long stakedBrainSteps = 0;
    public string stakedAssetId;
}

public class WalletManager : MonoBehaviour
{
    public static WalletManager Instance;

    public string ConnectedWallet { get; private set; }
    public LinkWalletResult CurrentStake { get; private set; }
    public bool IsConnected => !string.IsNullOrEmpty(ConnectedWallet);

    private Action<string> pendingConnectSuccess;
    private Action<string> pendingConnectError;
    private Action<LinkWalletResult> pendingLinkSuccess;
    private Action<string> pendingLinkError;
    private Action pendingDisconnectSuccess;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void SolanaInitJS();
    [DllImport("__Internal")] private static extern int IsPhantomInstalledJS();
    [DllImport("__Internal")] private static extern void ConnectWalletJS();
    [DllImport("__Internal")] private static extern void DisconnectWalletJS();
    [DllImport("__Internal")] private static extern void LinkWalletJS(string walletAddress);
    [DllImport("__Internal")] private static extern void RefreshStakeStateJS();
#endif

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        SolanaInitJS();
#else
        Debug.Log("[WalletManager] Editor stub — JS bridge skipped");
#endif
    }

    public bool IsPhantomInstalled()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        return IsPhantomInstalledJS() == 1;
#else
        return true;
#endif
    }

    public void ConnectWallet(Action<string> onSuccess, Action<string> onError)
    {
        pendingConnectSuccess = onSuccess;
        pendingConnectError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        ConnectWalletJS();
#else
        ConnectedWallet = "EditorStub11111111111111111111111111111111";
        onSuccess?.Invoke(ConnectedWallet);
        pendingConnectSuccess = null;
#endif
    }

    public void DisconnectWallet(Action onSuccess)
    {
        pendingDisconnectSuccess = onSuccess;
#if UNITY_WEBGL && !UNITY_EDITOR
        DisconnectWalletJS();
#else
        ConnectedWallet = null;
        CurrentStake = null;
        onSuccess?.Invoke();
#endif
    }

    public void LinkWallet(Action<LinkWalletResult> onSuccess, Action<string> onError)
    {
        if (!IsConnected)
        {
            onError?.Invoke("No wallet connected");
            return;
        }
        pendingLinkSuccess = onSuccess;
        pendingLinkError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        LinkWalletJS(ConnectedWallet);
#else
        var stub = new LinkWalletResult { walletAddress = ConnectedWallet, stakedTier = 3, stakedBrainSteps = 60000000, stakedAssetId = "EditorStubAsset" };
        CurrentStake = stub;
        onSuccess?.Invoke(stub);
        pendingLinkSuccess = null;
#endif
    }

    public void RefreshStakeState(Action<LinkWalletResult> onSuccess, Action<string> onError)
    {
        pendingLinkSuccess = onSuccess;
        pendingLinkError = onError;
#if UNITY_WEBGL && !UNITY_EDITOR
        RefreshStakeStateJS();
#else
        onSuccess?.Invoke(CurrentStake);
        pendingLinkSuccess = null;
#endif
    }

    // ---------- JS callbacks (called via SendMessage) ----------

    public void OnWalletConnected(string pubkey)
    {
        Debug.Log("[WalletManager] OnWalletConnected: " + pubkey);
        ConnectedWallet = pubkey;
        pendingConnectSuccess?.Invoke(pubkey);
        pendingConnectSuccess = null;
        pendingConnectError = null;
    }

    public void OnWalletDisconnected(string _)
    {
        Debug.Log("[WalletManager] OnWalletDisconnected");
        ConnectedWallet = null;
        CurrentStake = null;
        pendingDisconnectSuccess?.Invoke();
        pendingDisconnectSuccess = null;
    }

    public void OnWalletError(string err)
    {
        Debug.LogError("[WalletManager] OnWalletError: " + err);
        pendingConnectError?.Invoke(err);
        pendingConnectSuccess = null;
        pendingConnectError = null;
    }

    public void OnLinkWalletSuccess(string json)
    {
        Debug.Log("[WalletManager] OnLinkWalletSuccess: " + json);
        try
        {
            var result = JsonUtility.FromJson<LinkWalletResult>(json);
            CurrentStake = result;
            pendingLinkSuccess?.Invoke(result);
        }
        catch (Exception e)
        {
            Debug.LogError("[WalletManager] Failed to parse link result: " + e);
            pendingLinkError?.Invoke("Parse error: " + e.Message);
        }
        finally
        {
            pendingLinkSuccess = null;
            pendingLinkError = null;
        }
    }

    public void OnLinkWalletError(string err)
    {
        Debug.LogError("[WalletManager] OnLinkWalletError: " + err);
        pendingLinkError?.Invoke(err);
        pendingLinkSuccess = null;
        pendingLinkError = null;
    }

    public void OnSolanaError(string err)
    {
        Debug.LogError("[WalletManager] OnSolanaError: " + err);
    }
}