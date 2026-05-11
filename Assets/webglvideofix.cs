using UnityEngine;
using UnityEngine.Video;
using System.IO;

public class WebGLVideoLoader : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("The exact name of the video file in StreamingAssets (e.g., menu.mp4)")]
    public string videoFileName;

    private VideoPlayer vp;

    void Awake()
    {
        vp = GetComponent<VideoPlayer>();
        
        // This ensures the video stops and clears memory when the screen is disabled
        vp.playOnAwake = false; 
    }

    void OnEnable()
    {
        // Construct the URL path for WebGL
        string path = Path.Combine(Application.streamingAssetsPath, videoFileName);
        vp.url = path;
        
        // Start playback whenever this panel/screen becomes active
        vp.Play();
    }

    void OnDisable()
    {
        // Critical for WebGL: Stop the stream when the screen is hidden to save memory
        if(vp != null) vp.Stop();
    }
}
