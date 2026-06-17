using UnityEngine;
using UnityEngine.InputSystem;

public class BlindnessToggle : MonoBehaviour
{
    [SerializeField] private InputActionReference toggleAction; 
    [SerializeField] private GameObject blindnessSphere;
    [SerializeField] private Camera mainCamera;
    
    private int normalMask = -1; 
    [SerializeField] private LayerMask blindnessMask; 

    private void OnEnable() => toggleAction.action.performed += OnTogglePressed;
    private void OnDisable() => toggleAction.action.performed -= OnTogglePressed;

    private void OnTogglePressed(InputAction.CallbackContext context)
    {
        bool isBlind = !blindnessSphere.activeSelf;
        blindnessSphere.SetActive(isBlind);
        
        // Switch the camera view
        mainCamera.cullingMask = isBlind ? blindnessMask.value : normalMask;
    }
}