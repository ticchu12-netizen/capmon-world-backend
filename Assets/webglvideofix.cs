using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Attach to any GameObject with a VideoPlayer. Manages play/stop lifecycle
/// in sync with the GameObject's active state. Plays instantly if the video
/// was already prepared (typically by VideoPreloadOrchestrator at scene start),
/// otherwise prepares first and plays when ready.
///
/// Replaces the older script that called vp.Play() in OnEnable without
/// preparing — which caused the 1-2 sec download delay every time the
/// screen activated.
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class WebGLVideoLoader : MonoBehaviour
{
    [Tooltip("Filename in StreamingAssets, e.g. menu-bg.mp4 or wallet-bg.webm")]
    public string videoFileName;

    [Tooltip("Loop the video when it plays")]
    public bool loop = true;

    private VideoPlayer vp;

    void Awake()
    {
        vp = GetComponent<VideoPlayer>();
        vp.playOnAwake = false;
        vp.isLooping = loop;
        vp.skipOnDrop = true;          // smoother playback in WebGL when frames lag
        vp.waitForFirstFrame = true;   // ensures Play() doesn't show a black frame

        // Set URL once. If VideoPreloadOrchestrator already set it externally,
        // this is a no-op (same URL — no re-download).
        if (string.IsNullOrEmpty(vp.url) && !string.IsNullOrEmpty(videoFileName))
        {
            vp.source = VideoSource.Url;
            vp.url = Path.Combine(Application.streamingAssetsPath, videoFileName);
        }

        vp.prepareCompleted += OnPrepareCompleted;
    }

    void OnEnable()
    {
        if (vp == null) return;
        if (vp.isPrepared)
        {
            vp.Play();
        }
        else
        {
            vp.Prepare(); // OnPrepareCompleted will fire when ready
        }
    }

    private void OnPrepareCompleted(VideoPlayer source)
    {
        // Only play if the GameObject is currently visible.
        if (gameObject.activeInHierarchy && !vp.isPlaying)
        {
            vp.Play();
        }
    }

    void OnDisable()
    {
        if (vp != null && vp.isPlaying) vp.Stop();
    }

    void OnDestroy()
    {
        if (vp != null) vp.prepareCompleted -= OnPrepareCompleted;
    }
}