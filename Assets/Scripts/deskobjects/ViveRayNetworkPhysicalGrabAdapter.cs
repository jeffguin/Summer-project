#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN

using Fusion;
using UnityEngine;
using Valve.VR;

public class ViveRayNetworkPhysicalGrabAdapter : MonoBehaviour
{
    [Header("Role")]
    [SerializeField]
    private NetworkPhysicalGrabbable.GrabRole grabRole =
        NetworkPhysicalGrabbable.GrabRole.Audience;

    [Header("Ray Source")]
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("SteamVR Input")]
    [SerializeField] private SteamVR_Action_Boolean grabAction;
    [SerializeField] private SteamVR_Input_Sources inputSource = SteamVR_Input_Sources.RightHand;

    [Header("Ray Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private Material idleRayMaterial;
    [SerializeField] private Material hoverRayMaterial;
    [SerializeField] private Material grabRayMaterial;
    [SerializeField] private float rayWidth = 0.01f;

    [Header("Grab Settings")]
    [SerializeField] private bool keepInitialRotation = true;
    [SerializeField] private float defaultGrabDistance = 1.5f;
    [SerializeField] private float targetSendRate = 30f;

    [Header("Input Mode")]
    [Tooltip("开启后：第一次 click 抓取，第二次 click 释放。适合 SteamVR Boolean Click 输入。")]
    [SerializeField] private bool toggleGrabMode = true;

    [Header("Debug")]
    [SerializeField] private bool debugLog = true;
    [SerializeField] private bool drawDebugRay = false;
    [SerializeField] private bool debugHoverLog = false;

    private NetworkRunner runner;

    private NetworkPhysicalGrabbable hoveredObject;
    private NetworkPhysicalGrabbable grabbedObject;

    private float grabbedDistance;
    private Quaternion grabbedRotationOffset;
    private float nextTargetSendTime;

    private void Start()
    {
        runner = FindFirstObjectByType<NetworkRunner>();

        if (rayOrigin == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: RayOrigin is missing.");
        }

        if (grabAction == null)
        {
            Debug.LogError("ViveRayNetworkPhysicalGrabAdapter: GrabAction is missing.");
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = rayWidth;
            lineRenderer.endWidth = rayWidth;
            lineRenderer.enabled = true;
        }

        DebugMessage(
            $"Started. RunnerFound={runner != null}, Role={grabRole}, " +
            $"InputSource={inputSource}, ToggleGrabMode={toggleGrabMode}"
        );
    }

    private void Update()
    {
        if (runner == null)
        {
            runner = FindFirstObjectByType<NetworkRunner>();
        }

        UpdateHover();
        UpdateInput();

        // Toggle 模式下，只要已经抓住物体，就持续发送目标位置。
        // 非 Toggle 模式下，grabbedObject 也只会在按住期间存在。
        if (grabbedObject != null)
        {
            UpdateGrabTarget();
        }

        UpdateRayVisual();
    }

    private void UpdateHover()
    {
        hoveredObject = null;

        if (rayOrigin == null)
            return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (drawDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.cyan);
        }

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            hoveredObject = hit.collider.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (debugHoverLog && hoveredObject != null)
            {
                DebugMessage($"Hovering: {hoveredObject.name}, HitPoint={hit.point}");
            }
        }
    }

    private void UpdateInput()
    {
        if (grabAction == null)
        {
            DebugMessage("UpdateInput skipped because grabAction is null.");
            return;
        }

        if (runner == null)
        {
            DebugMessage("UpdateInput skipped because runner is null.");
            return;
        }

        if (toggleGrabMode)
        {
            if (grabAction.GetStateDown(inputSource))
            {
                if (grabbedObject == null)
                {
                    DebugMessage("Toggle click: begin grab.");
                    TryBeginGrab();
                }
                else
                {
                    DebugMessage("Toggle click: release grab.");
                    EndGrab();
                }
            }

            return;
        }

        // 非 Toggle 模式：按下抓取，松开释放。
        if (grabAction.GetStateDown(inputSource))
        {
            DebugMessage("Grab action down.");
            TryBeginGrab();
        }

        if (grabAction.GetState(inputSource) && grabbedObject != null)
        {
            DebugMessage($"Grab action holding. GrabbedObject={grabbedObject.name}");
        }

        if (grabAction.GetStateUp(inputSource))
        {
            DebugMessage("Grab action up.");
            EndGrab();
        }
    }

    private void TryBeginGrab()
    {
        NetworkPhysicalGrabbable targetObject = hoveredObject;

        // 按下 click 的那一帧 hoveredObject 可能刚好为空，
        // 所以这里重新做一次 raycast，避免“明明射线命中但抓取失败”。
        if (targetObject == null)
        {
            targetObject = FindGrabbableUnderRay(out RaycastHit hitInfo);

            if (targetObject != null)
            {
                DebugMessage(
                    $"Grab press recovered object by direct raycast. " +
                    $"Object={targetObject.name}, HitPoint={hitInfo.point}"
                );
            }
        }

        if (targetObject == null)
        {
            DebugMessage("Grab pressed but no NetworkPhysicalGrabbable was hit.");
            return;
        }

        grabbedObject = targetObject;

        grabbedDistance = Vector3.Distance(
            rayOrigin.position,
            grabbedObject.transform.position
        );

        if (grabbedDistance <= 0.05f)
        {
            grabbedDistance = defaultGrabDistance;
        }

        if (keepInitialRotation)
        {
            grabbedRotationOffset =
                Quaternion.Inverse(rayOrigin.rotation) * grabbedObject.transform.rotation;
        }
        else
        {
            grabbedRotationOffset = Quaternion.identity;
        }

        nextTargetSendTime = 0f;

        DebugMessage(
            $"Request grab. Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, Role={grabRole}, Distance={grabbedDistance}"
        );

        grabbedObject.RPC_RequestGrab(
            runner.LocalPlayer,
            (int)grabRole
        );

        SendGrabTargetImmediately();
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

        DebugMessage(
            $"UpdateGrabTarget running. " +
            $"Object={grabbedObject.name}, " +
            $"SendRate={targetSendRate}, " +
            $"NextSendTime={nextTargetSendTime}"
        );

        SendGrabTargetImmediately();
    }

    private void SendGrabTargetImmediately()
    {
        if (grabbedObject == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because grabbedObject is null.");
            return;
        }

        if (rayOrigin == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because rayOrigin is null.");
            return;
        }

        if (runner == null)
        {
            DebugMessage("SendGrabTargetImmediately skipped because runner is null.");
            return;
        }

        Vector3 targetPosition =
            rayOrigin.position + rayOrigin.forward * grabbedDistance;

        Quaternion targetRotation = keepInitialRotation
            ? rayOrigin.rotation * grabbedRotationOffset
            : rayOrigin.rotation;

        DebugMessage(
            $"Sending grab target. " +
            $"Object={grabbedObject.name}, " +
            $"Player={runner.LocalPlayer}, " +
            $"Role={grabRole}, " +
            $"TargetPosition={targetPosition}, " +
            $"TargetRotation={targetRotation.eulerAngles}"
        );

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
            DebugMessage("Grab released but no object was currently grabbed.");
            return;
        }

        if (runner == null)
        {
            grabbedObject = null;
            return;
        }

        DebugMessage(
            $"Request release. Object={grabbedObject.name}, Player={runner.LocalPlayer}, Role={grabRole}"
        );

        grabbedObject.RPC_RequestRelease(
            runner.LocalPlayer,
            (int)grabRole
        );

        grabbedObject = null;
    }

    private void UpdateRayVisual()
    {
        if (lineRenderer == null || rayOrigin == null)
            return;

        Vector3 start = rayOrigin.position;
        Vector3 end = start + rayOrigin.forward * maxDistance;

        if (Physics.Raycast(
                rayOrigin.position,
                rayOrigin.forward,
                out RaycastHit hit,
                maxDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            end = hit.point;
        }

        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);

        if (grabbedObject != null)
        {
            if (grabRayMaterial != null)
                lineRenderer.material = grabRayMaterial;
        }
        else if (hoveredObject != null)
        {
            if (hoverRayMaterial != null)
                lineRenderer.material = hoverRayMaterial;
        }
        else
        {
            if (idleRayMaterial != null)
                lineRenderer.material = idleRayMaterial;
        }
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
    }

    private NetworkPhysicalGrabbable FindGrabbableUnderRay(out RaycastHit hitInfo)
    {
        hitInfo = default;

        if (rayOrigin == null)
            return null;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        if (Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxDistance,
                interactableLayers,
                QueryTriggerInteraction.Ignore))
        {
            hitInfo = hit;

            NetworkPhysicalGrabbable grabbable =
                hit.collider.GetComponentInParent<NetworkPhysicalGrabbable>();

            if (grabbable != null)
            {
                return grabbable;
            }
        }

        return null;
    }

    private void DebugMessage(string message)
    {
        if (!debugLog)
            return;

        Debug.Log($"[ViveRayNetworkPhysicalGrabAdapter] {message}");
    }
}

#else

using UnityEngine;

public class ViveRayNetworkPhysicalGrabAdapter : MonoBehaviour
{
    private void Awake()
    {
        enabled = false;
    }
}

#endif
