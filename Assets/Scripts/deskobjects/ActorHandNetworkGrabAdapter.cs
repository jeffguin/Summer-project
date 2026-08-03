using Fusion;
using UnityEngine;

public class ActorHandNetworkGrabAdapter : MonoBehaviour
{
    public enum ActorHandSide
    {
        Left,
        Right
    }

    [Header("Role")]
    [SerializeField]
    private NetworkPhysicalGrabbable.GrabRole grabRole =
        NetworkPhysicalGrabbable.GrabRole.Actor;

    [Header("Hand Tracking")]
    [SerializeField] private ActorHandSide handSide = ActorHandSide.Right;

    [Tooltip("必须填写或自动获取当前物体上的 OVRHand。裸手 pinch 抓取依赖这个组件。")]
    [SerializeField] private OVRHand ovrHand;

    [Tooltip("用于计算抓取位置的 Transform。通常使用带 OVRHand 的手部物体 Transform。为空时自动使用 transform。")]
    [SerializeField] private Transform handTransform;

    [Header("Pinch Input")]
    [SerializeField] private OVRHand.HandFinger pinchFinger = OVRHand.HandFinger.Index;

    [Tooltip("Pinch 强度超过该值时开始抓取。")]
    [SerializeField] private float pinchStartThreshold = 0.75f;

    [Tooltip("Pinch 强度低于该值时释放。必须小于 Start Threshold，防止抖动。")]
    [SerializeField] private float pinchReleaseThreshold = 0.35f;

    [Header("Detection")]
    [SerializeField] private float grabRadius = 0.18f;
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("Grab Settings")]
    [SerializeField] private bool keepInitialPositionOffset = true;
    [SerializeField] private bool keepInitialRotationOffset = true;
    [SerializeField] private float targetSendRate = 30f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = false;
    [SerializeField] private bool drawDebugSphere = false;
    [SerializeField] private bool debugHoverLog = false;
    [SerializeField] private bool debugPinchLog = false;

    private const int OverlapResultCapacity = 32;
    private const float ContinuousDebugInterval = 1f;

    private NetworkRunner runner;
    private readonly Collider[] overlapResults =
        new Collider[OverlapResultCapacity];

    private NetworkPhysicalGrabbable hoveredObject;
    private NetworkPhysicalGrabbable grabbedObject;

    private Vector3 grabbedPositionOffset;
    private Quaternion grabbedRotationOffset;

    private float nextTargetSendTime;
    private float nextContinuousDebugTime;
    private bool isPinching;
    private bool hasObservedTrackingState;
    private bool wasHandTracked;

    private void Start()
    {
        runner = FindFirstObjectByType<NetworkRunner>();

        if (handTransform == null)
        {
            handTransform = transform;
        }

        if (ovrHand == null)
        {
            ovrHand = GetComponent<OVRHand>();
        }

        if (ovrHand == null)
        {
            Debug.LogError(
                "ActorHandNetworkGrabAdapter: OVRHand is missing. " +
                "Please attach this script to the object that has OVRHand, or assign OVRHand manually."
            );
        }

        DebugMessage(
            $"Started. RunnerFound={runner != null}, " +
            $"HandSide={handSide}, Role={grabRole}, " +
            $"OVRHandFound={ovrHand != null}, HandTransform={handTransform.name}"
        );
    }

    private void Update()
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        UpdateHover();
        UpdatePinchInput();

        if (grabbedObject != null && isPinching)
        {
            UpdateGrabTarget();
        }
    }

    private void UpdateHover()
    {
        NetworkPhysicalGrabbable previousHoveredObject = hoveredObject;
        hoveredObject = null;

        if (handTransform == null)
            return;

        int hitCount = Physics.OverlapSphereNonAlloc(
            handTransform.position,
            grabRadius,
            overlapResults,
            interactableLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;
        NetworkPhysicalGrabbable closestObject = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = overlapResults[i];

            if (hit == null)
                continue;

            NetworkPhysicalGrabbable grabbable =
                hit.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (grabbable == null)
                continue;

            float distance = Vector3.Distance(
                handTransform.position,
                grabbable.transform.position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestObject = grabbable;
            }
        }

        hoveredObject = closestObject;

        if (debugHoverLog && hoveredObject != previousHoveredObject)
        {
            if (hoveredObject == null)
            {
                DebugMessage("Hover ended.");
            }
            else
            {
                DebugMessage(
                    $"Hovering {hoveredObject.name}, Distance={closestDistance}, " +
                    $"HandPosition={handTransform.position}"
                );
            }
        }
    }

    private void UpdatePinchInput()
    {
        if (ovrHand == null)
        {
            return;
        }

        bool handIsTracked = ovrHand.IsTracked;

        if (!hasObservedTrackingState || handIsTracked != wasHandTracked)
        {
            if (debugPinchLog)
            {
                DebugMessage(
                    handIsTracked
                        ? "Hand tracking acquired."
                        : "Hand tracking lost."
                );
            }

            hasObservedTrackingState = true;
            wasHandTracked = handIsTracked;
        }

        if (!handIsTracked)
        {
            if (isPinching)
            {
                DebugMessage("Hand tracking lost while grabbing. Releasing object.");
                isPinching = false;
                EndGrab();
            }

            return;
        }

        float pinchStrength = ovrHand.GetFingerPinchStrength(pinchFinger);
        bool fingerIsPinching = ovrHand.GetFingerIsPinching(pinchFinger);

        if (!isPinching && fingerIsPinching && pinchStrength >= pinchStartThreshold)
        {
            isPinching = true;

            DebugMessage(
                $"Pinch started. Finger={pinchFinger}, Strength={pinchStrength:F2}"
            );

            TryBeginGrab();
            return;
        }

        if (isPinching && pinchStrength <= pinchReleaseThreshold)
        {
            isPinching = false;

            DebugMessage(
                $"Pinch released. Finger={pinchFinger}, Strength={pinchStrength:F2}"
            );

            EndGrab();
        }
    }

    private void TryBeginGrab()
    {
        if (runner == null)
        {
            DebugMessage("TryBeginGrab skipped because runner is null.");
            return;
        }

        if (grabbedObject != null)
        {
            DebugMessage($"Already grabbing {grabbedObject.name}.");
            return;
        }

        NetworkPhysicalGrabbable targetObject = hoveredObject;

        if (targetObject == null)
        {
            targetObject = FindClosestGrabbable();

            if (targetObject != null)
            {
                DebugMessage($"Recovered object by overlap check: {targetObject.name}");
            }
        }

        if (targetObject == null)
        {
            DebugMessage("Pinch started but no NetworkPhysicalGrabbable is near the hand.");
            return;
        }

        grabbedObject = targetObject;

        if (keepInitialPositionOffset)
        {
            grabbedPositionOffset =
                Quaternion.Inverse(handTransform.rotation) *
                (grabbedObject.transform.position - handTransform.position);
        }
        else
        {
            grabbedPositionOffset = Vector3.zero;
        }

        if (keepInitialRotationOffset)
        {
            grabbedRotationOffset =
                Quaternion.Inverse(handTransform.rotation) *
                grabbedObject.transform.rotation;
        }
        else
        {
            grabbedRotationOffset = Quaternion.identity;
        }

        nextTargetSendTime = 0f;

        DebugMessage(
            $"Request grab. Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, Hand={handSide}"
        );

        grabbedObject.RPC_RequestGrab(
            runner.LocalPlayer,
            (int)grabRole
        );

        SendGrabTargetImmediately();
    }

    private NetworkPhysicalGrabbable FindClosestGrabbable()
    {
        if (handTransform == null)
            return null;

        Collider[] hits = Physics.OverlapSphere(
            handTransform.position,
            grabRadius,
            interactableLayers,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance = float.MaxValue;
        NetworkPhysicalGrabbable closestObject = null;

        foreach (Collider hit in hits)
        {
            NetworkPhysicalGrabbable grabbable =
                hit.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (grabbable == null)
                continue;

            float distance = Vector3.Distance(
                handTransform.position,
                grabbable.transform.position
            );

            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestObject = grabbable;
            }
        }

        return closestObject;
    }

    private void UpdateGrabTarget()
    {
        if (grabbedObject == null)
            return;

        if (runner == null)
        {
            DebugMessage("UpdateGrabTarget skipped because runner is null.");
            return;
        }

        if (targetSendRate <= 0f)
        {
            DebugMessage("UpdateGrabTarget skipped because targetSendRate <= 0.");
            return;
        }

        if (Time.time < nextTargetSendTime)
            return;

        nextTargetSendTime = Time.time + 1f / targetSendRate;

        SendGrabTargetImmediately();
    }

    private void SendGrabTargetImmediately()
    {
        if (grabbedObject == null)
            return;

        if (handTransform == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because handTransform is null.");
            return;
        }

        if (runner == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because runner is null.");
            return;
        }

        Vector3 targetPosition =
            handTransform.position +
            handTransform.rotation * grabbedPositionOffset;

        Quaternion targetRotation =
            handTransform.rotation * grabbedRotationOffset;

        if (debugPinchLog && Time.unscaledTime >= nextContinuousDebugTime)
        {
            nextContinuousDebugTime =
                Time.unscaledTime + ContinuousDebugInterval;

            DebugMessage(
                $"Sending grab target. Object={grabbedObject.name}, " +
                $"Player={runner.LocalPlayer}, Role={grabRole}, " +
                $"TargetPosition={targetPosition}, TargetRotation={targetRotation.eulerAngles}"
            );
        }

        grabbedObject.RPC_UpdateGrabTarget(
            runner.LocalPlayer,
            (int)grabRole,
            targetPosition,
            targetRotation
        );
    }

    private void EndGrab()
    {
        if (grabbedObject == null)
        {
            DebugMessage("Pinch released but no object was currently grabbed.");
            return;
        }

        if (runner == null)
        {
            grabbedObject = null;
            return;
        }

        DebugMessage(
            $"Request release. Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, Hand={handSide}"
        );

        grabbedObject.RPC_RequestRelease(
            runner.LocalPlayer,
            (int)grabRole
        );

        grabbedObject = null;
    }

    private void OnDisable()
    {
        if (grabbedObject != null && runner != null)
        {
            grabbedObject.RPC_RequestRelease(
                runner.LocalPlayer,
                (int)grabRole
            );

            grabbedObject = null;
        }

        isPinching = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugSphere)
            return;

        Transform targetTransform = handTransform != null ? handTransform : transform;

        if (targetTransform == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(targetTransform.position, grabRadius);
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[ActorHandNetworkGrabAdapter] {message}");
    }
}
