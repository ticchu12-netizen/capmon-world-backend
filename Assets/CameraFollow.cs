using UnityEngine;

/// <summary>
/// Locked-angle 2.5D camera that follows the player at a fixed offset.
/// Matches the Graytail / Capmon adventure mode camera style.
///
/// Setup:
///   1. Attach this script to your Main Camera
///   2. Drag your player GameObject into the 'target' field in the inspector
///   3. The default offset and rotation give the Graytail 45° look
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("The player or any GameObject the camera should follow.")]
    public Transform target;

    [Header("Offset")]
    [Tooltip("Camera position offset from the target. (0, 8, -7) gives a south-facing 45° view.")]
    public Vector3 offset = new Vector3(0f, 8f, -7f);

    [Tooltip("Fixed camera rotation. (50, 0, 0) looks 50° down — Graytail-style.")]
    public Vector3 fixedRotation = new Vector3(50f, 0f, 0f);

    [Header("Smoothing")]
    [Tooltip("How quickly the camera catches up to the target. Higher = snappier.")]
    [Range(0f, 30f)]
    public float smoothSpeed = 8f;

    [Tooltip("If true, camera position lerps smoothly. If false, snaps instantly.")]
    public bool smoothFollow = true;

    void Start()
    {
        // Snap immediately so the camera doesn't fly in from origin at scene start
        if (target != null)
        {
            transform.position = target.position + offset;
        }
        transform.rotation = Quaternion.Euler(fixedRotation);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desired = target.position + offset;
        if (smoothFollow)
        {
            transform.position = Vector3.Lerp(transform.position, desired,
                                              smoothSpeed * Time.deltaTime);
        }
        else
        {
            transform.position = desired;
        }
        // Keep rotation locked — that's what makes it feel like Graytail
        transform.rotation = Quaternion.Euler(fixedRotation);
    }
}