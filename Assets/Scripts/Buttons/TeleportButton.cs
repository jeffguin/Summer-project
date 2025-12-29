using UnityEngine;

public class TeleportButton : MonoBehaviour
{
    [SerializeField] GameObject player;
    [SerializeField] GameObject teleportSpot;
    
    // NEW: Drag the Camera/HMD object (e.g., UnityXRHmd) here
    [SerializeField] Transform hmdTransform; 

    public void OnButtonClick()
    {
        // 1. Teleport position is correct
        player.transform.position = teleportSpot.transform.position;

        // 2. Calculate the rotation difference (Head's current Y-rotation)
        // We use localRotation because the HMD's rotation is relative to the Rig Root (player)
        float hmdYRotation = hmdTransform.localRotation.eulerAngles.y;
        
        // 3. Calculate the new rotation for the ROOT rig
        // Target Orientation - Current Head Offset
        float desiredYRotation = teleportSpot.transform.eulerAngles.y - hmdYRotation;

        // 4. Apply the new rotation to the root rig
        // This makes the HMD's current forward vector align with the teleportSpot's forward vector.
        player.transform.rotation = Quaternion.Euler(0, desiredYRotation, 0);
    }
}