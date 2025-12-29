using UnityEngine;

public class CarrotSnapper : MonoBehaviour
{
    // Assign the Audio Source on the same object (Carrot top half)
    public AudioSource carrotSoundSource; 
    
    // Assign the Rigidbody of the other half in the Inspector
    public Rigidbody carrotBottomHalf;

    private bool isBroken = false; 

    void OnJointBreak(float breakForce)
    {
        // Safety check to ensure the break logic runs only once
        // if (isBroken) return; 
        // isBroken = true; 
        
        // --- 1. Audio and Physics Effects ---

        if (carrotSoundSource != null)
        {
            carrotSoundSource.Play(); 
        }
        
        if (carrotBottomHalf != null)
        {
            // Apply a small force to the bottom half to make the break visible
            carrotBottomHalf.AddRelativeForce(Vector3.forward * 0.1f, ForceMode.Impulse); 

            // >> Ignore collision between the two broken halves <<
            Collider topCollider = GetComponent<Collider>();
            Collider bottomCollider = carrotBottomHalf.GetComponent<Collider>();
            
            if (topCollider != null && bottomCollider != null)
            {
                // Disable collision so they don't push each other apart or stick together
                Physics.IgnoreCollision(topCollider, bottomCollider, true);
            }
        }
        
        // --- 2. Final Cleanup ---
        
        // Destroy the Joint component explicitly to ensure it's fully broken
        // The Joint component is now useless.
        Destroy(GetComponent<Joint>());
    }
}