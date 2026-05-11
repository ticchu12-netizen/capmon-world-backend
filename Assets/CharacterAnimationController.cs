using UnityEngine;

public class CharacterAttackHandler : MonoBehaviour
{
    public Animator otherCharacterAnimator; // Assign this in the Inspector

    // Called by the Animation Event at the end of attack2
    public void OnAttack2End()
    {
        if (otherCharacterAnimator != null)
        {
            otherCharacterAnimator.SetTrigger("Attacked"); // Triggers the attacked animation
        }
        else
        {
            Debug.LogWarning("Other character's Animator is not assigned!");
        }
    }
}