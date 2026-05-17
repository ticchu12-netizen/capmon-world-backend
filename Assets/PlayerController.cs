using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float gravity = 9.81f;

    private CharacterController controller;
    private Transform spriteTransform;
    private SpriteRenderer spriteRenderer;
    private Vector3 moveDirection = Vector3.zero;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        
        // Find the child object holding our sprite renderer
        spriteTransform = transform.Find("Sprite_Visual");
        
        if (spriteTransform != null)
        {
            spriteRenderer = spriteTransform.GetComponent<SpriteRenderer>();
        }
    }

    void Update()
    {
        // 1. Handle 3D Movement
        if (controller.isGrounded)
        {
            float inputX = Input.GetAxisRaw("Horizontal");
            float inputZ = Input.GetAxisRaw("Vertical");

            moveDirection = new Vector3(inputX, 0f, inputZ).normalized;
            moveDirection *= moveSpeed;

            // 2. Handle Sprite Flipping (Left / Right)
            if (spriteRenderer != null)
            {
                if (inputX > 0)
                {
                    spriteRenderer.flipX = false; // Facing Right
                }
                else if (inputX < 0)
                {
                    spriteRenderer.flipX = true;  // Facing Left
                }
            }
        }

        // Apply constant gravity downward
        moveDirection.y -= gravity * Time.deltaTime;

        // Move the Character Controller
        controller.Move(moveDirection * Time.deltaTime);

        // 3. Handle 2D Billboarding (Face the Camera)
        if (spriteTransform != null && Camera.main != null)
        {
            spriteTransform.rotation = Camera.main.transform.rotation;
        }
    }
}
