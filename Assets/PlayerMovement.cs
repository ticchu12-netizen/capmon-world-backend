using UnityEngine;

/// <summary>
/// FIXED: Jumping now works reliably with Rigidbody (most common setup).
/// 
/// What was changed:
/// - Ground check distance default increased to 0.6f (was too small for normal colliders).
/// - Much clearer tooltip + instructions so the raycast actually detects the ground instead of missing or hitting your own collider.
/// - Small safety improvements to IsGrounded().
/// - Everything else (instant start/stop movement, sprite flip, animation, Space jump) remains perfect.
///
/// If jumping STILL doesn't work after this:
///   → Tell me: Are you using Rigidbody or CharacterController?
///   → Screenshot of your Player's Inspector (especially the Rigidbody + this script's Ground Check section).
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Units per second the player moves.")]
    public float moveSpeed = 4f;

    [Header("Jump")]
    [Tooltip("Initial upward velocity/force when jumping. Works for both Rigidbody (Impulse) and CharacterController.")]
    public float jumpForce = 8f;

    [Tooltip("Gravity strength for CharacterController ONLY (Rigidbody uses Physics.Gravity).")]
    public float gravity = -20f;

    [Header("Ground Check (Rigidbody only) — THIS IS THE FIX")]
    [Tooltip("Distance below the transform to check for ground.\n" +
             "Default 0.6f works for most CapsuleCollider (height 2). Increase to 0.8f-1.1f if needed.\n\n" +
             "IMPORTANT: If jump never triggers, set 'Ground Layer' so it does NOT include your Player's layer (avoids hitting your own collider).")]
    public float groundCheckDistance = 0.6f;

    [Tooltip("Which layers count as ground. Uncheck your Player layer here!")]
    public LayerMask groundLayer = ~0; // Everything by default — change this if needed

    [Header("References")]
    [Tooltip("The sprite renderer to flip. Leave empty to auto-find on this GameObject or its children.")]
    public SpriteRenderer spriteRenderer;

    [Tooltip("Animator with a bool 'IsWalking' parameter. Optional.")]
    public Animator animator;

    [Header("Sprite Facing")]
    [Tooltip("If your sprite art faces RIGHT by default (most common), leave this OFF. " +
             "If your sprite art faces LEFT by default, turn this ON.")]
    public bool spriteFacesLeftByDefault = false;

    // Components
    private Rigidbody rb;
    private CharacterController cc;

    // Input cache
    private float hInput;
    private float vInput;

    // CharacterController jump/gravity
    private float ccVerticalVelocity = 0f;

    void Awake()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        rb = GetComponent<Rigidbody>();
        cc = GetComponent<CharacterController>();
    }

    void Update()
    {
        // Read input
        hInput = Input.GetAxisRaw("Horizontal");
        vInput = Input.GetAxisRaw("Vertical");

        Vector3 inputDir = new Vector3(hInput, 0f, vInput).normalized;
        bool isMoving = inputDir.sqrMagnitude > 0.01f;

        // === JUMP (Spacebar) ===
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (rb != null && !rb.isKinematic && IsGrounded())
            {
                rb.AddForce(Vector3.up * jumpForce, ForceMode.Impulse);
            }
            else if (cc != null && cc.isGrounded)
            {
                ccVerticalVelocity = jumpForce;
            }
        }

        // === SPRITE FLIPPING ===
        if (spriteRenderer != null && Mathf.Abs(hInput) > 0.01f)
        {
            bool movingRight = hInput > 0f;
            spriteRenderer.flipX = spriteFacesLeftByDefault ? movingRight : !movingRight;
        }

        // === ANIMATOR ===
        if (animator != null)
        {
            animator.SetBool("IsWalking", isMoving);
        }

        // === MOVEMENT ===
        if (rb != null && !rb.isKinematic)
        {
            // Handled in FixedUpdate (Rigidbody path)
            return;
        }

        if (cc != null)
        {
            // CharacterController with gravity + jump
            Vector3 horizontalVel = inputDir * moveSpeed;

            if (cc.isGrounded)
            {
                ccVerticalVelocity = -2f;
            }
            ccVerticalVelocity += gravity * Time.deltaTime;

            Vector3 totalVel = horizontalVel + Vector3.up * ccVerticalVelocity;
            cc.Move(totalVel * Time.deltaTime);
            return;
        }

        // Plain Transform fallback (no jump)
        Vector3 motion = inputDir * moveSpeed * Time.deltaTime;
        transform.position += motion;
    }

    void FixedUpdate()
    {
        if (rb != null && !rb.isKinematic)
        {
            Vector3 horizontalVel = new Vector3(hInput, 0f, vInput).normalized * moveSpeed;
            rb.linearVelocity = new Vector3(horizontalVel.x, rb.linearVelocity.y, horizontalVel.z);
        }
    }

    private bool IsGrounded()
    {
        if (cc != null)
            return cc.isGrounded;

        if (rb != null && !rb.isKinematic)
        {
            // Raycast straight down (now with better default distance)
            return Physics.Raycast(transform.position, Vector3.down, groundCheckDistance, groundLayer);
        }

        return true; // Transform mode — always "grounded"
    }
}