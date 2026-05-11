using UnityEngine;

public class AttackMenuPositioner : MonoBehaviour
{
    public Transform backgroundTransform;  // Assign your background object here
    public Canvas canvas;                  // Assign your Canvas here
    public Vector2 offset;                 // Optional offset (e.g., to move the menu up)

    void Start()
    {
        PositionMenu();
    }

    void PositionMenu()
    {
        // Convert background's world position to screen position
        Vector3 screenPos = Camera.main.WorldToScreenPoint(backgroundTransform.position);

        // Convert to local position within the Canvas
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        Vector2 localPos;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screenPos, Camera.main, out localPos);

        // Apply offset (e.g., move 50 units up)
        localPos += offset;

        // Set the menu's position
        RectTransform menuRect = GetComponent<RectTransform>();
        menuRect.anchoredPosition = localPos;
    }
}