using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Static wrapper around the WebGL JS bridge for the replay overlay modal.
/// Call ReplayOverlay.Show(battleId) from any UI to pop the /replay page over
/// the Unity canvas. Click × in the modal (or press ESC) to close.
///
/// Editor / non-WebGL builds log a debug line instead of calling extern.
/// </summary>
public static class ReplayOverlay
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void ShowReplayOverlay(string battleId);

    [DllImport("__Internal")]
    private static extern void HideReplayOverlay();
#endif

    /// <summary>
    /// Open the replay modal for the given battle. The iframe loads
    /// https://capmon-hackathon.web.app/replay?id=<battleId>
    /// </summary>
    public static void Show(string battleId)
    {
        if (string.IsNullOrEmpty(battleId))
        {
            Debug.LogWarning("[ReplayOverlay] Show called with null/empty battleId — ignoring");
            return;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        ShowReplayOverlay(battleId);
#else
        Debug.Log("[ReplayOverlay] (Editor stub) Would show overlay for battle: " + battleId);
#endif
    }

    public static void Hide()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        HideReplayOverlay();
#else
        Debug.Log("[ReplayOverlay] (Editor stub) Would hide overlay");
#endif
    }
}