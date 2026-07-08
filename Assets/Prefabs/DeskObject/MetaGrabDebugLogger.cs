using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

public class MetaGrabDebugLogger : MonoBehaviour
{
    [SerializeField] private Grabbable grabbable;
    [SerializeField] private HandGrabInteractable handGrabInteractable;
    [SerializeField] private bool debugLog = true;

    private void Awake()
    {
        if (grabbable == null)
        {
            grabbable = GetComponent<Grabbable>();
        }

        if (handGrabInteractable == null)
        {
            handGrabInteractable = GetComponent<HandGrabInteractable>();
        }
    }

    private void OnEnable()
    {
        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        DebugMessage(
            $"Enabled. GrabbableFound={grabbable != null}, " +
            $"HandGrabInteractableFound={handGrabInteractable != null}, " +
            $"RigidbodyFound={GetComponent<Rigidbody>() != null}, " +
            $"ColliderFound={GetComponent<Collider>() != null}"
        );
    }

    private void OnDisable()
    {
        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }
    }

    private void Update()
    {
        if (!debugLog)
            return;

        if (Time.frameCount % 60 != 0)
            return;

        if (handGrabInteractable != null)
        {
            DebugMessage(
                $"HandGrabInteractable State={handGrabInteractable.State}, " +
                $"InteractorsCount={handGrabInteractable.Interactors.Count}, " +
                $"SelectingInteractorsCount={handGrabInteractable.SelectingInteractors.Count}"
            );
        }
    }

    private void HandlePointerEvent(PointerEvent pointerEvent)
    {
        DebugMessage(
            $"PointerEvent Type={pointerEvent.Type}, " +
            $"Identifier={pointerEvent.Identifier}, " +
            $"PosePosition={pointerEvent.Pose.position}"
        );
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[MetaGrabDebugLogger] {gameObject.name}: {message}");
    }
}