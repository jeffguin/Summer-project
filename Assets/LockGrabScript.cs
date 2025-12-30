using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

public class LockGrabScript : MonoBehaviour
{
    // Drag both the Left and Right Interactables here in the Inspector
    [SerializeField] private HandGrabInteractable[] handGrabs; 

    private void OnEnable()
    {
        foreach(var hg in handGrabs) hg.WhenStateChanged += OnHandGrabStateChanged;
    }

    private void OnDisable()
    {
        foreach(var hg in handGrabs) hg.WhenStateChanged -= OnHandGrabStateChanged;
    }

    private void OnHandGrabStateChanged(InteractableStateChangeArgs args)
    {
        if (args.NewState == InteractableState.Select)
        {
            // Disable ALL interactables so the sword is locked to the hand that grabbed it
            // and the other hand can't "steal" it or trigger a release.
            foreach(var hg in handGrabs) 
            {
                hg.enabled = false;
            }
        }
    }
}