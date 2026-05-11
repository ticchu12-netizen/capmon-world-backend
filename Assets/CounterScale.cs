using UnityEngine;

public class CounterScale : MonoBehaviour
{
    private Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale;
    }

    void LateUpdate()
    {
        if (transform.parent != null)
        {
            Vector3 parentScale = transform.parent.localScale;
            transform.localScale = new Vector3(originalScale.x / parentScale.x, originalScale.y / parentScale.y, originalScale.z / parentScale.z);
        }
    }
}