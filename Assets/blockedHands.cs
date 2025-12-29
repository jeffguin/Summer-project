using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

public class ConstrainedHandMover : MonoBehaviour
{
    // The Transform providing the raw controller tracking data
    public Transform trackedControllerTransform; 
    
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // 1. Calculate the new desired position from the controller tracking
        Vector3 targetPosition = trackedControllerTransform.position;
        Quaternion targetRotation = trackedControllerTransform.rotation;

        // 2. Use the Rigidbody's method to move
        // Rigidbody.MovePosition respects non-kinematic collisions,
        // effectively stopping the hand if it hits a wall's collider.
        rb.MovePosition(targetPosition);
        rb.MoveRotation(targetRotation);
    }
}