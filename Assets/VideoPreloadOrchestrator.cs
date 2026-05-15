using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Sits on LoginScreen (or any always-active root GameObject). On Awake,
/// triggers browser HTTP prefetch for every video referenced in the inspector
/// list — including videos belonging to inactive screens (Wallet, etc.).
///
/// Why we don't call VideoPlayer.Prepare() anymore: Unity logs
/// "Cannot Prepare a disabled VideoPlayer" if the VideoPlayer's GameObject
/// is inactive. The fix is to skip the Unity-side preload entirely and let
/// the BROWSER preload the file via fetch(). When the screen activates later
/// and Unity's VideoPlayer loads the URL, the browser HTTP cache serves the
/// bytes immediately — no network round-trip needed.
///
/// Caveats (be honest):
/// - This caches HTTP bytes, not decoded frames. Some decode delay remains
///   when Play() runs. Expect "1-2 sec" delay to drop to "0.3-0.6 sec," not zero.
/// - In editor builds the prefetch is a no-op (no DllImport target). The
///   editor doesn't have the delay problem anyway since files load from disk.
/// - The VideoPlayer reference is OPTIONAL. We only use it to set the URL +
///   loop flag now (so the WebGLVideoLoader on the screen has correct config
///   ready). The reference doesn't have to be on an active GameObject.
///
/// Inspector setup unchanged from before:
///   - Add this component to LoginScreen (always active at scene start).
///   - For each video on a "later" screen, add an entry with the VideoPlayer
///     and matching filename in StreamingAssets.
/// </summary>
public class VideoPreloadOrchestrator : MonoBehaviour
{
    [System.Serializable]
    public class PreloadTarget
    {
        [Tooltip("For debug logs only")]
        public string label;

        [Tooltip("Optional — used to pre-configure URL + loop. Safe to leave null if WebGLVideoLoader handles it on the screen.")]
        public VideoPlayer videoPlayer;

        [Tooltip("Filename in StreamingAssets, e.g. wallet-bg.mp4")]
        public string videoFileName;

        [Tooltip("Loop the video when it plays later")]
        public bool loop = true;
    }

    [SerializeField] private PreloadTarget[] targets;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void PrefetchVideoURL(string url);
#endif

    void Awake()
    {
        if (targets == null) return;

        foreach (var t in targets)
        {
            if (string.IsNullOrEmpty(t.videoFileName))
            {
                Debug.LogWarning($"[VideoPreload] {t.label}: videoFileName empty");
                continue;
            }

            string url = Path.Combine(Application.streamingAssetsPath, t.videoFileName);

            // OPTIONAL: pre-configure the VideoPlayer so when its screen activates,
            // WebGLVideoLoader.OnEnable finds the URL already set and skips its own
            // config step. Safe to do on inactive VideoPlayers — we're only setting
            // properties, not calling Prepare().
            if (t.videoPlayer != null)
            {
                t.videoPlayer.source = VideoSource.Url;
                t.videoPlayer.url = url;
                t.videoPlayer.playOnAwake = false;
                t.videoPlayer.isLooping = t.loop;
                t.videoPlayer.skipOnDrop = true;
                t.videoPlayer.waitForFirstFrame = true;
            }

            // Ask the browser to download the URL into its HTTP cache so the
            // VideoPlayer's eventual <video src=URL> load hits cache.
#if UNITY_WEBGL && !UNITY_EDITOR
            PrefetchVideoURL(url);
#endif

            Debug.Log($"[VideoPreload] Prefetching '{t.label}': {url}");
        }
    }
}