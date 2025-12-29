using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

public class GateHingeSound : MonoBehaviour
{
    public HandGrabInteractable handGrab;         // Assign in inspector
    public AudioSource hingeSound;                // Assign in inspector

    private float lastAngle;
    private bool isGrabbed = false;

    private void Awake()
    {
        handGrab.WhenPointerEventRaised += HandlePointerEvent;
    }

    private void OnDestroy()
    {
        handGrab.WhenPointerEventRaised -= HandlePointerEvent;
    }

    private void HandlePointerEvent(PointerEvent evt)
    {
        // Grab started
        if (evt.Type == PointerEventType.Select)
        {
            isGrabbed = true;
            lastAngle = transform.localEulerAngles.y;

            if (!hingeSound.isPlaying)
                hingeSound.Play();
        }

        // Grab ended
        if (evt.Type == PointerEventType.Unselect)
        {
            isGrabbed = false;
            hingeSound.Stop();
        }
    }

    private void Update()
    {
        if (!isGrabbed)
            return;

        // Detect rotation movement
        float currentAngle = transform.localEulerAngles.y;
        float delta = Mathf.Abs(Mathf.DeltaAngle(lastAngle, currentAngle));

        // If not rotating, stop sound
        if (delta < 0.1f && hingeSound.isPlaying)
            hingeSound.Stop();

        // If rotating, ensure sound plays
        if (delta >= 0.1f && !hingeSound.isPlaying)
            hingeSound.Play();

        lastAngle = currentAngle;
    }
}
