using Fusion;
using UnityEngine;
using Oculus.Interaction;

[RequireComponent(typeof(Grabbable))]
[RequireComponent(typeof(NetworkPhysicalGrabbable))]
public class MetaHandGrabNetworkBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Grabbable grabbable;
    [SerializeField] private NetworkPhysicalGrabbable networkGrabbable;

    [Header("Role")]
    [SerializeField]
    private NetworkPhysicalGrabbable.GrabRole grabRole =
        NetworkPhysicalGrabbable.GrabRole.Actor;

    [Header("Target Settings")]
    [Tooltip("抓取时是否保留物体相对手部抓取点的位置偏移。建议开启。")]
    [SerializeField] private bool keepInitialPositionOffset = true;

    [Tooltip("抓取时是否保留物体相对手部抓取点的旋转偏移。建议开启。")]
    [SerializeField] private bool keepInitialRotationOffset = true;

    [Tooltip("每秒发送多少次目标位置给 Fusion Host。")]
    [SerializeField] private float targetSendRate = 30f;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool debugMoveLog = false;

    private NetworkRunner runner;

    private bool isSelected = false;
    private bool hasRequestedGrab = false;

    private int activePointerId = -1;

    private Vector3 grabbedPositionOffset;
    private Quaternion grabbedRotationOffset;

    private Pose latestPointerPose;
    private bool hasLatestPointerPose = false;

    private float nextTargetSendTime;

    private void Awake()
    {
        if (grabbable == null)
        {
            grabbable = GetComponent<Grabbable>();
        }

        if (networkGrabbable == null)
        {
            networkGrabbable = GetComponent<NetworkPhysicalGrabbable>();
        }
    }

    private void OnEnable()
    {
        runner = FindFirstObjectByType<NetworkRunner>();

        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        DebugMessage(
            $"Enabled. RunnerFound={runner != null}, " +
            $"GrabbableFound={grabbable != null}, " +
            $"NetworkGrabbableFound={networkGrabbable != null}"
        );
    }

    private void OnDisable()
    {
        if (grabbable != null)
        {
            grabbable.WhenPointerEventRaised -= HandlePointerEvent;
        }

        if (isSelected && hasRequestedGrab && networkGrabbable != null && runner != null)
        {
            networkGrabbable.RPC_RequestRelease(
                runner.LocalPlayer,
                (int)grabRole
            );
        }

        isSelected = false;
        hasRequestedGrab = false;
        activePointerId = -1;
        hasLatestPointerPose = false;
    }

    private void Update()
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        if (!isSelected)
            return;

        if (!hasRequestedGrab)
            return;

        if (!hasLatestPointerPose)
            return;

        if (targetSendRate <= 0f)
            return;

        if (Time.time < nextTargetSendTime)
            return;

        nextTargetSendTime = Time.time + 1f / targetSendRate;

        SendTargetFromLatestPointerPose();
    }

    private void HandlePointerEvent(PointerEvent pointerEvent)
    {
        if (debugMoveLog || pointerEvent.Type != PointerEventType.Move)
        {
            DebugMessage(
                $"PointerEvent Type={pointerEvent.Type}, " +
                $"Identifier={pointerEvent.Identifier}, " +
                $"PosePosition={pointerEvent.Pose.position}"
            );
        }

        switch (pointerEvent.Type)
        {
            case PointerEventType.Select:
                HandleSelect(pointerEvent);
                break;

            case PointerEventType.Move:
                HandleMove(pointerEvent);
                break;

            case PointerEventType.Unselect:
                HandleUnselect(pointerEvent);
                break;

            case PointerEventType.Cancel:
                HandleCancel(pointerEvent);
                break;
        }
    }

    private void HandleSelect(PointerEvent pointerEvent)
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        if (runner == null)
        {
            DebugMessage("Select ignored because NetworkRunner is null.");
            return;
        }

        if (networkGrabbable == null)
        {
            DebugMessage("Select ignored because NetworkPhysicalGrabbable is null.");
            return;
        }

        isSelected = true;
        hasRequestedGrab = true;
        activePointerId = pointerEvent.Identifier;

        latestPointerPose = pointerEvent.Pose;
        hasLatestPointerPose = true;

        if (keepInitialPositionOffset)
        {
            grabbedPositionOffset =
                Quaternion.Inverse(pointerEvent.Pose.rotation) *
                (transform.position - pointerEvent.Pose.position);
        }
        else
        {
            grabbedPositionOffset = Vector3.zero;
        }

        if (keepInitialRotationOffset)
        {
            grabbedRotationOffset =
                Quaternion.Inverse(pointerEvent.Pose.rotation) *
                transform.rotation;
        }
        else
        {
            grabbedRotationOffset = Quaternion.identity;
        }

        nextTargetSendTime = 0f;

        DebugMessage(
            $"Request grab. Object={gameObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, PointerId={activePointerId}"
        );

        networkGrabbable.RPC_RequestGrab(
            runner.LocalPlayer,
            (int)grabRole
        );

        SendTargetFromLatestPointerPose();
    }

    private void HandleMove(PointerEvent pointerEvent)
    {
        if (!isSelected)
            return;

        if (activePointerId != -1 && pointerEvent.Identifier != activePointerId)
            return;

        latestPointerPose = pointerEvent.Pose;
        hasLatestPointerPose = true;
    }

    private void HandleUnselect(PointerEvent pointerEvent)
    {
        if (!isSelected)
            return;

        if (activePointerId != -1 && pointerEvent.Identifier != activePointerId)
            return;

        RequestRelease("Unselect");
    }

    private void HandleCancel(PointerEvent pointerEvent)
    {
        if (!isSelected)
            return;

        if (activePointerId != -1 && pointerEvent.Identifier != activePointerId)
            return;

        RequestRelease("Cancel");
    }

    private void SendTargetFromLatestPointerPose()
    {
        if (runner == null)
        {
            DebugMessage("Send target skipped because runner is null.");
            return;
        }

        if (networkGrabbable == null)
        {
            DebugMessage("Send target skipped because networkGrabbable is null.");
            return;
        }

        Vector3 targetPosition =
            latestPointerPose.position +
            latestPointerPose.rotation * grabbedPositionOffset;

        Quaternion targetRotation =
            latestPointerPose.rotation * grabbedRotationOffset;

        if (debugMoveLog)
        {
            DebugMessage(
                $"Sending target. TargetPosition={targetPosition}, " +
                $"TargetRotation={targetRotation.eulerAngles}, PointerId={activePointerId}"
            );
        }

        networkGrabbable.RPC_UpdateGrabTarget(
            runner.LocalPlayer,
            (int)grabRole,
            targetPosition,
            targetRotation
        );
    }

    private void RequestRelease(string reason)
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        if (networkGrabbable == null)
        {
            DebugMessage($"Release skipped because networkGrabbable is null. Reason={reason}");
            ResetState();
            return;
        }

        if (runner == null)
        {
            DebugMessage($"Release skipped because runner is null. Reason={reason}");
            ResetState();
            return;
        }

        DebugMessage(
            $"Request release. Reason={reason}, Object={gameObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, PointerId={activePointerId}"
        );

        networkGrabbable.RPC_RequestRelease(
            runner.LocalPlayer,
            (int)grabRole
        );

        ResetState();
    }

    private void ResetState()
    {
        isSelected = false;
        hasRequestedGrab = false;
        activePointerId = -1;
        hasLatestPointerPose = false;
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[MetaHandGrabNetworkBridge] {gameObject.name}: {message}");
    }
}
